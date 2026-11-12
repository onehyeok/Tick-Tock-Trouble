using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>방 만들기·입장·나가기, 방 안(팀·준비·봇·플레이어 목록·세션 속성)</summary>
public partial class FusionMatchBackend : MonoBehaviour, IMatchBackend, INetworkRunnerCallbacks
{
    // ---------------- 방 만들기 / 들어가기 / 나가기 ----------------

    public void CreateRoom(RoomSettings settings)
    {
        pendingTeams.Clear();
        _ = CreateRoomAsync(settings, false);
    }

    /// <summary>방 만들기 (handover = 방장이 나가서 내가 새 방장으로 같은 방을 다시 여는 중)</summary>
    async Task<bool> CreateRoomAsync(RoomSettings settings, bool handover)
    {
        if (busy || InRoom || quitting)
            return false;
        busy = true;
        current = settings;
        // 새로 만드는 방이면 새 고유 번호 (방장이 바뀌어 다시 여는 방은 원래 번호 그대로 → 강퇴 기록 유지)
        if (!handover || string.IsNullOrEmpty(settings.roomId))
            settings.roomId = Guid.NewGuid().ToString("N").Substring(0, 12);
        if (!handover)
            bannedNicks.Clear(); // 새 방: 예전 방에서 강퇴한 사람도 들어올 수 있음

        Dictionary<string, SessionProperty> props = new Dictionary<string, SessionProperty>
        {
            { "rid", settings.roomId },
            { "map", settings.mapIndex },
            { "rounds", settings.rounds },
            { "lock", settings.HasPassword ? 1 : 0 },
            { "ing", 0 },
            { "nicks", nickname }
        };

        NetworkRunner r = await FreshRunner();
        if (quitting)
            return false;
        StartGameResult result = await r.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Host,
            SessionName = settings.roomName,
            PlayerCount = settings.maxPlayers,
            SessionProperties = props,
            SceneManager = r.GetComponent<NetworkSceneManagerDefault>(),
            IsOpen = true,
            IsVisible = true,
            StartGameCancellationToken = quitCts.Token
        });
        if (quitting)
            return false;
        busy = false;

        if (!result.Ok)
        {
            runner = null;
            if (handover)
                return false;
            Error?.Invoke(result.ShutdownReason == ShutdownReason.GameIdAlreadyExists ? "같은 이름의 방이 이미 있어요." : "인터넷 연결이 불안정해서 방을 만들지 못했어요. 다시 시도해 주세요.");
            RejoinLobby();
            return false;
        }

        inLobby = false;
        InRoom = true;
        // 위치·애니메이션을 자주 보내므로 신뢰성 데이터 전송 속도를 최대로 (기본값이 낮아 밀리면 동기화가 안 됨)
        RaiseReliableSendRate();
        ChatLog.Clear();
        players.Clear();
        int myTeam = pendingTeams.TryGetValue(nickname, out int kept) ? kept : RoomPlayer.TeamTickToy;
        players.Add(new RoomPlayer { id = LocalPlayerId, nickname = nickname, team = myTeam, ready = true, isHost = true, isLocal = true });
        UpdateSessionProps();
        RoomChanged?.Invoke();
        return true;
    }

    /// <summary>방 만들기·입장 전용 새 연결. 로비(방 목록)용 연결은 끊음 — 로비 연결을 그대로 쓰면 방 연결이 가끔 안 붙음</summary>
    async Task<NetworkRunner> FreshRunner()
    {
        if (runner != null && !runner.IsShutdown)
        {
            NetworkRunner old = runner;
            runner = null; // 먼저 비워서 이 연결이 끊길 때의 처리(OnShutdown)가 무시되게
            inLobby = false;
            await old.Shutdown();
        }
        return EnsureRunner();
    }

    public void JoinRoom(string roomName, string password)
    {
        RoomInfo info = rooms.Find(x => x.name == roomName);
        if (info != null && IsKickedRoom(info.roomId))
        {
            Error?.Invoke(KickedJoinMessage);
            return;
        }
        if (info != null && !info.IsJoinable)
        {
            Error?.Invoke("방이 꽉 찼어요.");
            return;
        }
        _ = JoinAsync(roomName, password, false);
    }

    public void JoinRandom()
    {
        List<RoomInfo> open = rooms.FindAll(x => x.IsJoinable && !x.locked && !IsKickedRoom(x.roomId));
        if (open.Count == 0)
        {
            Error?.Invoke("참가할 수 있는 방이 없어요.");
            return;
        }
        _ = JoinAsync(open[UnityEngine.Random.Range(0, open.Count)].name, "", false);
    }

    const int JoinAttempts = 3;
    const float JoinTimeoutSeconds = 15f; // 보통 몇 초 안에 연결됨. 이보다 오래 걸리면 멈춘 것으로 봄
    const float RoomInfoTimeoutSeconds = 6f; // 들어간 뒤 방장이 방 정보를 보내 주기까지 기다리는 시간
    bool joinStuck;

    /// <summary>방 연결 중 Fusion 내부 오류가 나면 바로 알아채서 기다리지 않고 다시 시도</summary>
    void WatchJoinError(string condition, string stackTrace, UnityEngine.LogType type)
    {
        if (type == UnityEngine.LogType.Exception && condition != null && condition.Contains("Trying to allocate"))
            joinStuck = true;
    }

    async Task<bool> JoinAsync(string roomName, string password, bool silent, int attempts = JoinAttempts)
    {
        if (busy || InRoom || quitting)
            return false;
        busy = true;

        // Fusion 2.1.4 버그: 연결 순간 내부 오류("Trying to allocate < 0 bytes")가 가끔 나면 연결이 멈춤 (운에 따라 됨/안 됨).
        // → 멈추면 그 연결을 끊고 새 연결로 다시 시도 (최대 3번). 암호 틀림·방 꽉 참·방 없음은 다시 해도 같으니 바로 실패.
        StartGameResult result = null;
        bool joined = false;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            NetworkRunner r = await FreshRunner();
            if (quitting)
                return false;
            joinStuck = false;
            joinKicked = false;
            Application.logMessageReceived += WatchJoinError;
            Task<StartGameResult> start = r.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Client,
                SessionName = roomName,
                // 암호가 없으면 null (빈 배열을 넣으면 Fusion 이 0바이트를 할당하려다 "Trying to allocate < 0 bytes" 오류로 연결이 멈춤)
                ConnectionToken = string.IsNullOrEmpty(password) ? null : Encoding.UTF8.GetBytes(password),
                SceneManager = r.GetComponent<NetworkSceneManagerDefault>(),
                StartGameCancellationToken = quitCts.Token
            });

            // 끝나거나, 그 오류가 나거나, 너무 오래 걸릴 때까지 기다림
            float until = Time.realtimeSinceStartup + JoinTimeoutSeconds;
            while (!start.IsCompleted && !joinStuck && Time.realtimeSinceStartup < until && !quitting)
                await Task.Yield();
            Application.logMessageReceived -= WatchJoinError;
            bool killed = false;
            if (!start.IsCompleted || joinStuck)
            {
                Debug.LogWarning("[Fusion] 방 연결이 멈춰서 다시 시도해요 (" + attempt + "/" + attempts + ")");
                killed = true;
                await r.Shutdown(); // 멈춘 연결을 끊음
            }
            result = await start;
            if (quitting)
                return false;
            // 끊은 연결은 결과가 '성공'으로 와도 쓰면 안 됨 (방장과 연결 안 된 채로 방 화면만 뜨고 아무것도 동기화 안 됐음)
            joined = result.Ok && !killed && !r.IsShutdown;
            // 방장에게 인사를 보내고 방 정보(인원·라운드)가 실제로 오는지 확인 → 안 오면 연결이 안 된 것 → 다시 시도
            if (joined)
            {
                players.Clear();
                current = new RoomSettings { roomName = roomName };
                SendToHost(Msg(C2H_Hello, w => w.Write(nickname)));
                float wait = Time.realtimeSinceStartup + RoomInfoTimeoutSeconds;
                while (players.Count == 0 && !joinKicked && !r.IsShutdown && Time.realtimeSinceStartup < wait && !quitting)
                    await Task.Yield();
                if (quitting)
                    return false;
                if (players.Count > 0 && !joinKicked)
                    break;
                if (joinKicked)
                {
                    // 강퇴당한 방 → 다시 시도하지 않음
                    joined = false;
                    if (!r.IsShutdown)
                        await r.Shutdown();
                    runner = null;
                    break;
                }
                Debug.LogWarning("[Fusion] 방에 들어갔지만 방장 응답이 없어서 다시 시도해요 (" + attempt + "/" + attempts + ")");
                joined = false;
                killed = true;
                if (!r.IsShutdown)
                    await r.Shutdown();
            }

            runner = null;
            bool retry = killed
                || (result.ShutdownReason != ShutdownReason.ConnectionRefused
                    && result.ShutdownReason != ShutdownReason.GameIsFull
                    && result.ShutdownReason != ShutdownReason.GameNotFound);
            if (!retry || attempt == attempts)
                break;
            await Task.Delay(1000);
        }
        busy = false;

        if (!joined)
        {
            runner = null;
            if (joinKicked)
            {
                RoomInfo kickedFrom = rooms.Find(x => x.name == roomName);
                if (kickedFrom != null && !string.IsNullOrEmpty(kickedFrom.roomId))
                    kickedRooms.Add(kickedFrom.roomId);
            }
            if (silent)
                return false;
            string msg;
            if (joinKicked)
                msg = KickedJoinMessage;
            else switch (result.ShutdownReason)
            {
                case ShutdownReason.ConnectionRefused: msg = "들어갈 수 없어요. 암호가 틀렸거나 게임 중인 방이에요."; break;
                case ShutdownReason.GameIsFull: msg = "방이 꽉 찼어요."; break;
                case ShutdownReason.GameNotFound: msg = "방을 찾을 수 없어요."; break;
                default: msg = "인터넷 연결이 불안정해서 방에 들어가지 못했어요. 다시 시도해 주세요."; break;
            }
            Error?.Invoke(msg);
            RejoinLobby();
            return false;
        }

        inLobby = false;
        InRoom = true;
        // 위치·애니메이션을 자주 보내므로 신뢰성 데이터 전송 속도를 최대로 (기본값이 낮아 밀리면 동기화가 안 됨)
        RaiseReliableSendRate();
        ChatLog.Clear();
        RoomChanged?.Invoke();
        return true;
    }

    public async void LeaveRoom()
    {
        if (handingOver)
        {
            // 방장 넘기는 중에 나가기 → 넘기기를 멈추고 방 목록으로
            handoverCancelled = true;
            handingOver = false;
            handoverGhosts.Clear();
            players.Clear();
            current = null;
            RoomChanged?.Invoke();
            return;
        }
        if (!InRoom || runner == null)
            return;
        leaving = true;
        NetworkRunner r = runner;
        InRoom = false;
        players.Clear();
        current = null;
        RoomChanged?.Invoke();
        await r.Shutdown();
        leaving = false;
        if (quitting)
            return;
        RejoinLobby();
    }

    public void OnShutdown(NetworkRunner r, ShutdownReason reason)
    {
        if (r != runner || leaving)
            return;
        if (!InRoom)
            return;

        // 방장이 나갔거나 연결이 끊김 → 방을 없애지 않고, 먼저 들어온 사람이 바로 새 방장이 되어 같은 방을 이어감
        int myOldId = LocalPlayerId;
        RoomSettings oldRoom = current;
        List<RoomPlayer> oldPlayers = new List<RoomPlayer>(players);
        bool inMatch = MatchContext.Active;

        InRoom = false;
        runner = null;
        ClearGameState();

        // 넘기는 동안에도 방 화면은 그대로 (나간 방장·봇만 빼고 보여 줌)
        handingOver = oldRoom != null;
        handoverCancelled = false;
        current = oldRoom;
        players.RemoveAll(p => p.isHost || IsBotId(p.id));
        RoomChanged?.Invoke();
        StartCoroutine(Handover(oldRoom, oldPlayers, myOldId, inMatch));
    }

    // ---------------- 방장이 나갔을 때: 먼저 들어온 사람에게 방장 권한 ----------------

    /// <summary>새 방에 다시 모였을 때 원래 팀을 그대로 (닉네임 → 팀)</summary>
    readonly Dictionary<string, int> pendingTeams = new Dictionary<string, int>();
    /// <summary>방장이 나가서 새 방장에게 옮겨 가는 중 (방 화면은 계속 보여 줌)</summary>
    bool handingOver;
    bool handoverCancelled;
    const float HandoverGiveUpSeconds = 40f;
    /// <summary>방장이 바뀌는 동안 아직 다시 안 들어온 사람들 ('연결 중' 으로 목록에 남겨 둠)</summary>
    readonly List<RoomPlayer> handoverGhosts = new List<RoomPlayer>();
    float handoverGhostsUntil;
    const float HandoverGhostSeconds = 15f;

    void SetHandoverGhosts(List<RoomPlayer> order)
    {
        handoverGhosts.Clear();
        string me = PlayerProfile.Nickname;
        for (int i = 0; i < order.Count; i++)
        {
            RoomPlayer p = order[i];
            if (string.IsNullOrEmpty(p.nickname))
                continue;
            handoverGhosts.Add(new RoomPlayer { id = -1000 - i, nickname = p.nickname, team = p.team, isLocal = p.nickname == me, connecting = true });
        }
        handoverGhostsUntil = Time.realtimeSinceStartup + HandoverGhostSeconds;
    }

    /// <summary>실제 목록 + 아직 안 들어온 사람 (같은 닉네임이 들어오면 그 사람으로 바뀜)</summary>
    List<RoomPlayer> WithGhosts()
    {
        List<RoomPlayer> list = new List<RoomPlayer>(players);
        foreach (RoomPlayer g in handoverGhosts)
        {
            if (players.Find(p => p.nickname == g.nickname) == null)
                list.Add(g);
        }
        return list;
    }

    /// <summary>모두 다시 들어왔거나 시간이 지나면 '연결 중' 표시를 지움</summary>
    void UpdateHandoverGhosts()
    {
        if (handoverGhosts.Count == 0)
            return;
        bool allBack = handoverGhosts.TrueForAll(g => players.Find(p => p.nickname == g.nickname) != null);
        bool gone = !InRoom && !handingOver;
        if (allBack || gone || Time.realtimeSinceStartup > handoverGhostsUntil)
        {
            handoverGhosts.Clear();
            RoomChanged?.Invoke();
        }
    }
    const float HandoverTurnSeconds = 6f; // 앞 순서 사람이 방을 못 열면 이만큼 뒤에 다음 사람이 엶

    IEnumerator Handover(RoomSettings room, List<RoomPlayer> oldPlayers, int myOldId, bool inMatch)
    {
        // 남은 사람(들어온 순서 = 목록 순서, 방장·봇 제외). 맨 앞 사람이 새 방장.
        // 그 사람이 못 열면(튕김 등) 다음 사람이 순서대로 대신 엶 → 누가 나가도 방이 사라지지 않음
        List<RoomPlayer> order = oldPlayers.FindAll(p => !p.isHost && !IsBotId(p.id));
        // 방장 넘기기: 고른 사람이 맨 앞(새 방장), 원래 방장은 맨 뒤에 일반 참가자로 다시 들어옴
        int chosen = order.FindIndex(p => p.id == transferToId);
        if (chosen > 0)
        {
            RoomPlayer first = order[chosen];
            order.RemoveAt(chosen);
            order.Insert(0, first);
        }
        if (transferToId != 0)
        {
            RoomPlayer oldHost = oldPlayers.Find(p => p.isHost);
            if (oldHost != null)
                order.Add(new RoomPlayer { id = oldHost.id, nickname = oldHost.nickname, team = oldHost.team });
        }
        transferToId = 0;
        if (!inMatch)
            SetHandoverGhosts(order); // 다시 들어올 때까지 목록에서 사라지지 않게
        int myRank = order.FindIndex(p => p.id == myOldId);
        if (myRank < 0)
            myRank = order.Count; // (방장이던 내 연결이 끊긴 경우) 맨 뒤 순서

        pendingTeams.Clear();
        foreach (RoomPlayer p in order)
        {
            if (!string.IsNullOrEmpty(p.nickname))
                pendingTeams[p.nickname] = p.team;
        }

        // 게임 중이면 게임을 끝내지 않고 그대로 이어감 (맵·라운드·남은 시간 유지, 새 방장에게 다시 연결되는 동안 잠깐 멈춤)
        // 게임 중이 아니면 방 화면으로
        if (inMatch)
            MatchContext.Migrating = true;
        else
        {
            MatchContext.Active = false;
            MatchContext.ReturnToRoom = room != null;
            if (SceneManager.GetActiveScene().name != MatchService.LobbyScene)
                SceneManager.LoadScene(MatchService.LobbyScene);
            yield return null;
            yield return null;
        }

        bool ok = false;
        bool iAmHost = false;
        float start = Time.realtimeSinceStartup;
        List<RoomPlayer> view = new List<RoomPlayer>(players); // 넘기는 동안 보여 줄 방 화면
        if (myRank > 0)
            yield return new WaitForSecondsRealtime(0.7f); // 새 방장이 방을 열 시간
        while (room != null && !ok && !quitting && !handoverCancelled && Time.realtimeSinceStartup - start < HandoverGiveUpSeconds)
        {
            // 내 차례가 되면 방을 직접 엶 (이미 누가 열었으면 실패 → 들어가기)
            if (Time.realtimeSinceStartup - start >= myRank * HandoverTurnSeconds)
            {
                Task<bool> c = CreateRoomAsync(room, true);
                while (!c.IsCompleted)
                    yield return null;
                ok = iAmHost = c.Result;
            }
            if (!ok && !handoverCancelled)
            {
                Task<bool> j = JoinAsync(room.roomName, room.password, true, 1);
                while (!j.IsCompleted)
                    yield return null;
                ok = j.Result;
            }
            if (!ok)
            {
                // 실패한 시도가 방 화면을 지우지 않게 되돌림
                if (!handoverCancelled)
                {
                    current = room;
                    players.Clear();
                    players.AddRange(view);
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }
        handingOver = false;
        MatchContext.Migrating = false;

        if (ok && handoverCancelled)
        {
            LeaveRoom(); // 넘기는 중에 나가기를 누름
            yield break;
        }
        if (handoverCancelled || quitting)
            yield break;

        if (ok)
        {
            RoomPlayer host = players.Find(p => p.isHost);
            if (inMatch && iAmHost)
            {
                RoomPlayer leftHost = oldPlayers.Find(p => p.isHost);
                HostResumeMatch(leftHost != null ? leftHost.nickname : null); // 새 방장: 맵 AI 를 이어받아 게임을 계속 진행
            }
            if (inMatch)
                matchSawNet = false; // 새 방장의 네트워크 캐릭터가 생길 때까지 '방으로 돌아가기'로 착각하지 않게
            if (!inMatch)
                StartCoroutine(ErrorLater(iAmHost ? "방장이 되셨습니다!" : (host != null ? host.nickname : "") + " 님이 새 방장이 되었어요."));
        }
        else
        {
            players.Clear();
            current = null;
            MatchContext.ReturnToRoom = false;
            if (inMatch)
            {
                // 다시 연결 못 함 → 게임에서 나와 로비로
                MatchContext.Active = false;
                SceneManager.LoadScene(MatchService.LobbyScene);
            }
            RoomChanged?.Invoke();
            StartCoroutine(ErrorLater("인터넷 연결이 불안정해서 방에 다시 들어가지 못했어요."));
            RejoinLobby();
        }
    }

    // ---------------- 게임 중 방장 교체: 게임을 끝내지 않고 이어감 ----------------

    /// <summary>(새 방장) 경기 명단에서 닉네임이 같은 사람의 id 를 새 연결 id 로 바꾸고 모두에게 명단을 보냄. 명단에 없으면 false</summary>
    bool RemapRosterId(string nick, int newId)
    {
        RoomPlayer entry = MatchContext.Players.Find(p => p.nickname == nick);
        if (entry == null)
            return false;
        entry.id = newId;
        MatchContext.AssignRolesFromTeams();
        SendToClients(Msg(H2C_Roster, WriteRoster));
        return true;
    }

    void WriteRoster(BinaryWriter w)
    {
        w.Write((byte)MatchContext.Players.Count);
        foreach (RoomPlayer p in MatchContext.Players)
        {
            w.Write(p.id);
            w.Write(p.nickname ?? "");
            w.Write((byte)p.team);
        }
    }

    /// <summary>(참가자) 새 방장이 보낸 경기 명단 (방장이 바뀌면 모두의 연결 id 가 바뀜)</summary>
    void ReadRoster(BinaryReader rd)
    {
        MatchContext.Players.Clear();
        int n = rd.ReadByte();
        for (int i = 0; i < n; i++)
            MatchContext.Players.Add(new RoomPlayer { id = rd.ReadInt32(), nickname = rd.ReadString(), team = rd.ReadByte() });
        MatchContext.AssignRolesFromTeams();
        if (runner != null)
            MatchContext.LocalId = runner.LocalPlayer.PlayerId;
    }

    /// <summary>(새 방장) 게임 이어받기: 내 id 갱신, 마지막으로 보던 맵 AI 들을 진짜 AI 로 다시 만들고 네트워크 캐릭터 생성.
    /// 라운드·단계·남은 시간은 내 화면 값 그대로 이어서 진행</summary>
    void HostResumeMatch(string leftHostNick)
    {
        // 나간 방장은 명단에서 빼고, 나는 새 id. 아직 다시 연결 안 된 사람은 겹치지 않는 임시 id(음수) → 들어오면 새 id 로
        MatchContext.Players.RemoveAll(p => p.nickname == leftHostNick);
        for (int i = 0; i < MatchContext.Players.Count; i++)
        {
            RoomPlayer p = MatchContext.Players[i];
            p.id = p.nickname == nickname ? LocalPlayerId : -(i + 1);
        }
        MatchContext.LocalId = LocalPlayerId;
        MatchContext.AssignRolesFromTeams();
        // 승패는 바로 판정. 단, 아직 다시 연결 중인 사람(임시 id)은 10초 동안 방장이 나가기 직전 상태(살아 있었는지)로 셈
        // → 연결 중인 팀을 '전멸'로 잘못 보지 않으면서, 그 사이 마지막 틱토이가 잡히면 바로 와인더 승리
        rejoinGraceUntil = Time.realtimeSinceStartup + RejoinGraceSeconds;

        MatchPrefabs prefabs = MatchPrefabs.Load();
        if (prefabs != null && prefabs.tickToyPrefabs != null && prefabs.tickToyPrefabs.Length > 0)
        {
            foreach (AiSnapshot a in aiSnapshot)
            {
                GameObject prefab = prefabs.tickToyPrefabs[Mathf.Clamp(a.ch, 0, prefabs.tickToyPrefabs.Length - 1)];
                GameObject ai = Instantiate(prefab, a.pos, Quaternion.Euler(0f, a.yaw, 0f));
                ai.name = prefab.name + "_AI";
                DemoSceneAIBootstrap.ConvertToAI(ai, a.pos);
                // 쓰러져 있던 AI 는 쓰러진 채로 (기절한 척하는 틱토이와 헷갈리게 하는 요소라 그대로 남김)
                if (ai.TryGetComponent(out TickToyAI restored))
                {
                    if (!a.alive)
                        restored.SpawnDown();
                    else
                        restored.RestoreState(a.state, a.stateLeft, a.moveYaw, a.commandLeft, a.commandEmote, a.emoting); // 하던 동작 그대로
                }
            }
        }
        aiSnapshot.Clear();
        playersSpawned = false; // 모두의 네트워크 캐릭터를 새로 만듦
        aiRefreshTimer = 1f;
        StartCoroutine(CheckTeamsAfterResume());
    }

    /// <summary>(새 방장) 잠시 기다린 뒤에도 한 팀이 비어 있으면 (그 팀 사람이 방장이었음) 게임 종료 → 방으로</summary>
    IEnumerator CheckTeamsAfterResume()
    {
        yield return new WaitForSecondsRealtime(RejoinGraceSeconds);
        if (IsHost && MatchContext.Active)
            HostCheckTeamEmptyAfterLeave();
    }

    struct AiSnapshot
    {
        public Vector3 pos;
        public float yaw;
        public int ch;
        public bool alive;
        public int state;
        public float stateLeft, moveYaw, commandLeft;
        public int commandEmote, emoting;
    }

    /// <summary>(참가자) 맵 AI 들의 마지막 위치 — 방장이 나가면 새 방장이 이걸로 AI 를 이어받음</summary>
    readonly List<AiSnapshot> aiSnapshot = new List<AiSnapshot>();
    float aiSnapshotTimer;

    const float RejoinGraceSeconds = 10f;
    /// <summary>(새 방장) 이 시간까지는 아직 다시 연결 중인 사람을 나가기 직전 상태로 셈</summary>
    float rejoinGraceUntil;
    /// <summary>(참가자) 방장이 나가기 직전 각 플레이어가 살아 있었는지 (닉네임 → 생존)</summary>
    readonly Dictionary<string, bool> lastAlive = new Dictionary<string, bool>();

    /// <summary>(새 방장 판정용) 아직 다시 연결 중인 사람이면 true 와 직전 생존 여부</summary>
    bool PendingRejoin(RoomPlayer p, out bool alive)
    {
        alive = false;
        if (p.id >= 0 || Time.realtimeSinceStartup >= rejoinGraceUntil)
            return false;
        alive = !lastAlive.TryGetValue(p.nickname ?? "", out bool a) || a;
        return true;
    }

    void ClientRememberAIs()
    {
        aiSnapshotTimer += Time.unscaledDeltaTime;
        if (aiSnapshotTimer < 0.5f)
            return;
        aiSnapshotTimer = 0f;
        aiSnapshot.Clear();
        lastAlive.Clear();
        RoundManager rm = RoundManager.Instance;
        foreach (RoomPlayer p in MatchContext.Players)
        {
            if (string.IsNullOrEmpty(p.nickname))
                continue;
            if (p.id == MatchContext.LocalId)
                lastAlive[p.nickname] = rm == null || rm.LocalAlive;
            else if (NetCharacter.Players.TryGetValue(p.id, out NetCharacter pc) && pc != null && pc.Object != null && pc.Object.IsValid
                     && pc.HasPose && pc.PoseRound == MatchContext.Round)
                lastAlive[p.nickname] = pc.Alive;
        }
        foreach (NetCharacter nc in NetCharacter.ById.Values)
        {
            if (nc == null || nc.Object == null || !nc.Object.IsValid || nc.Kind != NetPose.KindAI || !nc.HasPose)
                continue;
            aiSnapshot.Add(new AiSnapshot
            {
                pos = nc.Pos, yaw = nc.Yaw, ch = nc.Ch, alive = nc.Alive,
                state = nc.AiStateIndex, stateLeft = Mathf.Max(0f, nc.AiStateEnd - nc.Runner.SimulationTime), moveYaw = nc.AiMoveYaw,
                commandLeft = nc.AiCommandAt < 0f ? -1f : Mathf.Max(0f, nc.AiCommandAt - nc.Runner.SimulationTime),
                commandEmote = nc.AiCommandEmote, emoting = nc.AiEmoting
            });
        }
    }

    // ---------------- 방장 넘기기 / 강퇴 (방 화면에서 방장이 우클릭) ----------------

    /// <summary>방장이 넘겨주기로 한 사람 id (방장이 나가면 이 사람이 새 방장). 0 = 없음</summary>
    int transferToId;

    /// <summary>방장: 고른 사람에게 방장을 넘김. 방장 연결을 넘길 수는 없으므로, 모두에게 새 방장을 알린 뒤
    /// 방을 닫고 → 그 사람이 같은 방을 다시 열고 → 나머지(원래 방장 포함)가 바로 다시 들어감</summary>
    public void TransferHost(int playerId)
    {
        if (!IsHost || handingOver || MatchContext.Active)
            return;
        RoomPlayer target = players.Find(p => p.id == playerId);
        if (target == null || target.isHost || IsBotId(playerId))
            return;
        transferToId = playerId;
        SendToClients(Msg(H2C_HostTransfer, w => w.Write(playerId)));
        StartCoroutine(TransferThenRejoin());
    }

    IEnumerator TransferThenRejoin()
    {
        yield return new WaitForSecondsRealtime(0.25f); // 알림이 모두에게 도착할 시간
        if (!InRoom || runner == null)
            yield break;

        int myOldId = LocalPlayerId;
        RoomSettings room = current;
        List<RoomPlayer> oldPlayers = new List<RoomPlayer>(players);
        oldPlayers.RemoveAll(p => IsBotId(p.id)); // 봇은 방이 다시 열리면 사라짐

        // 방 화면은 그대로 둔 채 내 방(연결)을 닫음
        handingOver = true;
        handoverCancelled = false;
        leaving = true;
        NetworkRunner r = runner;
        InRoom = false;
        runner = null;
        players.RemoveAll(p => p.isHost || IsBotId(p.id));
        RoomChanged?.Invoke();
        Task t = r.Shutdown();
        while (!t.IsCompleted)
            yield return null;
        leaving = false;
        StartCoroutine(Handover(room, oldPlayers, myOldId, false));
    }

    const string KickedJoinMessage = "강퇴 당한 방이라서 들어갈 수 없어요.";
    /// <summary>내가 강퇴당한 방의 고유 번호 (게임을 끌 때까지 그 방엔 다시 못 들어감. 같은 이름으로 새로 만든 방은 들어감)</summary>
    readonly HashSet<string> kickedRooms = new HashSet<string>();

    bool IsKickedRoom(string roomId) => !string.IsNullOrEmpty(roomId) && kickedRooms.Contains(roomId);
    /// <summary>방장: 강퇴한 사람 닉네임 (다시 들어오려 하면 바로 내보냄)</summary>
    readonly HashSet<string> bannedNicks = new HashSet<string>();
    bool joinKicked;

    /// <summary>방장: 강퇴. 봇은 바로 빼고, 사람은 알림을 보낸 뒤 연결을 끊음 (다시 못 들어옴)</summary>
    public void KickPlayer(int playerId)
    {
        if (!IsHost || MatchContext.Active)
            return;
        RoomPlayer target = players.Find(p => p.id == playerId);
        if (target == null || target.isHost)
            return;
        players.Remove(target);
        if (!IsBotId(playerId))
        {
            if (!string.IsNullOrEmpty(target.nickname))
                bannedNicks.Add(target.nickname);
            SendKickAndDisconnect(playerId);
        }
        BroadcastRoom();
        Error?.Invoke(target.nickname + " 님을 강퇴했어요.");
    }

    void SendKickAndDisconnect(int playerId)
    {
        foreach (PlayerRef p in runner.ActivePlayers)
        {
            if (p.PlayerId != playerId)
                continue;
            runner.SendReliableDataToPlayer(p, default(ReliableKey), Msg(H2C_Kicked, w => { }));
            StartCoroutine(DisconnectLater(p));
            return;
        }
    }

    IEnumerator DisconnectLater(PlayerRef p)
    {
        yield return new WaitForSecondsRealtime(1f); // 알림이 먼저 도착하게
        if (runner != null && runner.IsServer)
            runner.Disconnect(p);
    }

    /// <summary>(참가자) 방장에게 강퇴당함 → 방 목록으로, 그 방엔 다시 못 들어감</summary>
    void OnKicked()
    {
        // 들어가는 중이면 아직 방 정보가 없음 → 방 목록에서 고유 번호를 찾음
        string roomId = current != null && !string.IsNullOrEmpty(current.roomId) ? current.roomId
            : (current != null ? rooms.Find(x => x.name == current.roomName)?.roomId : null);
        if (!string.IsNullOrEmpty(roomId))
            kickedRooms.Add(roomId);
        if (!InRoom)
        {
            joinKicked = true; // 들어가는 중에 거절됨
            return;
        }
        LeaveRoom(); // 내가 먼저 나가므로 방장 넘기기는 일어나지 않음
        StartCoroutine(ErrorLater("방장에게 강퇴당했어요."));
    }

    IEnumerator ErrorLater(string msg)
    {
        yield return new WaitForSecondsRealtime(0.6f);
        Error?.Invoke(msg);
    }

    // ---------------- 방 안 ----------------

    public void SetReady(bool ready)
    {
        if (!InRoom)
            return;
        if (IsHost)
            HostSetReady(LocalPlayerId, ready);
        else
            SendToHost(Msg(C2H_Ready, w => w.Write(ready)));
    }

    public void SetTeam(int team)
    {
        if (!InRoom)
            return;
        RoomPlayer me = players.Find(p => p.isLocal);
        if (me != null && me.team != team && TeamCount(team) >= MatchContext.TeamSize)
        {
            Error?.Invoke("그 팀은 꽉 찼어요!");
            return;
        }
        if (IsHost)
            HostSetTeam(LocalPlayerId, team);
        else
            SendToHost(Msg(C2H_Team, w => w.Write((byte)team)));
    }

    /// <summary>혼자 테스트용 인원 채우기 봇 (방장만). 준비 완료 상태, 인원 적은 팀으로. 게임 중엔 살아있는 것으로 계산</summary>
    public void AddTestBot()
    {
        if (!InRoom)
            return;
        if (!IsHost)
        {
            Error?.Invoke("봇은 방장만 넣을 수 있어요.");
            return;
        }
        if (current != null && players.Count >= current.maxPlayers)
        {
            Error?.Invoke("방이 꽉 찼어요.");
            return;
        }
        int team = TeamCount(RoomPlayer.TeamWinder) < TeamCount(RoomPlayer.TeamTickToy) ? RoomPlayer.TeamWinder : RoomPlayer.TeamTickToy;
        if (TeamCount(team) >= MatchContext.TeamSize)
            team = 1 - team;
        int id = nextBotId++;
        players.Add(new RoomPlayer { id = id, nickname = "봇" + (id - BotIdBase + 1), team = team, ready = true });
        BroadcastRoom();
    }

    int TeamCount(int team)
    {
        int n = 0;
        foreach (RoomPlayer p in players)
        {
            if (p.team == team)
                n++;
        }
        return n;
    }

    /// <summary>팀에서 준비된 사람 수 (방장은 항상 준비)</summary>
    int ReadyCount(int team)
    {
        int n = 0;
        foreach (RoomPlayer p in players)
        {
            if (p.team == team && (p.ready || p.isHost))
                n++;
        }
        return n;
    }

    void HostSetReady(int id, bool ready)
    {
        RoomPlayer p = players.Find(x => x.id == id);
        if (p == null)
            return;
        p.ready = ready || p.isHost;
        BroadcastRoom();
    }

    void HostSetTeam(int id, int team)
    {
        RoomPlayer p = players.Find(x => x.id == id);
        if (p == null || p.team == team || (team != RoomPlayer.TeamTickToy && team != RoomPlayer.TeamWinder))
            return;
        if (TeamCount(team) >= MatchContext.TeamSize)
            return;
        p.team = team;
        BroadcastRoom();
    }

    public void OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        string pw = token != null ? Encoding.UTF8.GetString(token) : "";
        bool pwOk = current == null || !current.HasPassword || pw == current.password;
        bool full = current != null && players.Count >= current.maxPlayers; // 봇이 자리 차지 중
        if (!pwOk || full) // 게임 중이어도 자리가 있으면 받음
            request.Refuse();
        else
            request.Accept();
    }

    public void OnPlayerJoined(NetworkRunner r, PlayerRef player)
    {
        // 닉네임(Hello)을 받으면 목록에 넣음
    }

    public void OnPlayerLeft(NetworkRunner r, PlayerRef player)
    {
        if (r != runner || !IsHost)
            return;
        players.RemoveAll(p => p.id == player.PlayerId);
        if (NetCharacter.Players.TryGetValue(player.PlayerId, out NetCharacter nc) && nc != null && nc.Object != null)
            runner.Despawn(nc.Object);
        BroadcastRoom();
        HostCheckTeamEmptyAfterLeave(); // 게임 중 한 팀이 비면 "상대가 없어 게임을 종료합니다" → 방으로
    }

    void HostAddPlayer(PlayerRef player, string nick)
    {
        if (players.Exists(p => p.id == player.PlayerId))
            return;
        if (bannedNicks.Contains(nick ?? ""))
        {
            // 강퇴한 사람이 다시 들어옴 → 알리고 내보냄
            runner.SendReliableDataToPlayer(player, default(ReliableKey), Msg(H2C_Kicked, w => { }));
            StartCoroutine(DisconnectLater(player));
            return;
        }
        int team = TeamCount(RoomPlayer.TeamWinder) < TeamCount(RoomPlayer.TeamTickToy) ? RoomPlayer.TeamWinder : RoomPlayer.TeamTickToy;
        if (pendingTeams.TryGetValue(nick ?? "", out int kept))
            team = kept; // 방장이 바뀌어 다시 모인 경우: 원래 팀 그대로
        if (TeamCount(team) >= MatchContext.TeamSize)
            team = 1 - team;
        players.Add(new RoomPlayer { id = player.PlayerId, nickname = nick, team = team, ready = false });
        BroadcastRoom();

        // 방장이 바뀌어 같은 게임에 다시 연결된 사람 → 경기 명단의 id 만 새 연결 id 로 바꾸고 그대로 계속
        if (MatchContext.Active && RemapRosterId(nick, player.PlayerId))
            return;

        // 게임 중에 들어옴 → 지금 맵으로 불러서 이번 라운드는 관전, 다음 라운드부터 참가
        if (MatchContext.Active)
        {
            MapEntry map = MapCatalog.Load()?.Get(current.mapIndex);
            if (map != null)
            {
                playersSpawned = false; // 이 사람 네트워크 캐릭터도 만듦
                runner.SendReliableDataToPlayer(player, default(ReliableKey), Msg(H2C_JoinMatch, w =>
                {
                    WriteRoom(w);
                    w.Write(map.sceneName);
                    w.Write(MatchContext.Round);
                    w.Write(MatchContext.TickToyWins);
                    w.Write(MatchContext.WinderWins);
                    w.Write((byte)MatchContext.Players.Count);
                    foreach (RoomPlayer p in MatchContext.Players)
                        w.Write(p.id); // 이번 라운드를 뛰는 사람들
                }));
            }
        }
    }

    void BroadcastRoom()
    {
        if (!IsHost)
            return;
        SendToClients(Msg(H2C_Room, WriteRoom));
        UpdateSessionProps();
        RoomChanged?.Invoke();
    }

    void WriteRoom(BinaryWriter w)
    {
        w.Write(current.roomName ?? "");
        w.Write(current.roomId ?? "");
        w.Write(current.mapIndex);
        w.Write(current.rounds);
        w.Write(current.maxPlayers);
        w.Write(current.password ?? ""); // 방장이 바뀌어도 같은 암호로 다시 열 수 있게 (방 안 사람에게만 보냄)
        w.Write((byte)players.Count);
        foreach (RoomPlayer p in players)
        {
            w.Write(p.id);
            w.Write(p.nickname ?? "");
            w.Write((byte)p.team);
            w.Write(p.ready);
            w.Write(p.isHost);
        }
    }

    void ReadRoom(BinaryReader rd)
    {
        current = new RoomSettings
        {
            roomName = rd.ReadString(),
            roomId = rd.ReadString(),
            mapIndex = rd.ReadInt32(),
            rounds = rd.ReadInt32(),
            maxPlayers = rd.ReadInt32()
        };
        current.password = rd.ReadString();
        players.Clear();
        int n = rd.ReadByte();
        // 입장 확인 중(아직 InRoom 전)에도 '나'를 알아볼 수 있게 러너에서 직접 (LocalPlayerId 는 InRoom 전엔 0)
        int me = runner != null ? runner.LocalPlayer.PlayerId : 0;
        for (int i = 0; i < n; i++)
        {
            RoomPlayer p = new RoomPlayer
            {
                id = rd.ReadInt32(),
                nickname = rd.ReadString(),
                team = rd.ReadByte(),
                ready = rd.ReadBoolean(),
                isHost = rd.ReadBoolean()
            };
            p.isLocal = p.id == me;
            players.Add(p);
        }
    }

    void UpdateSessionProps()
    {
        if (!IsHost || runner.SessionInfo == null)
            return;
        List<string> nicks = new List<string>();
        foreach (RoomPlayer p in players)
            nicks.Add(p.nickname);
        runner.SessionInfo.UpdateCustomProperties(new Dictionary<string, SessionProperty>
        {
            { "rid", current.roomId ?? "" },
            { "map", current.mapIndex },
            { "rounds", current.rounds },
            { "lock", current.HasPassword ? 1 : 0 },
            { "ing", MatchContext.Active ? 1 : 0 },
            { "nicks", string.Join(",", nicks) }
        });
    }
}
