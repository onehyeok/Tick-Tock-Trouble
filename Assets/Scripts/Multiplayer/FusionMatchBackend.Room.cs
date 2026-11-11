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
        int myRank = order.FindIndex(p => p.id == myOldId);
        if (myRank < 0)
            myRank = order.Count; // (방장이던 내 연결이 끊긴 경우) 맨 뒤 순서

        pendingTeams.Clear();
        foreach (RoomPlayer p in order)
        {
            if (!string.IsNullOrEmpty(p.nickname))
                pendingTeams[p.nickname] = p.team;
        }

        // 게임 중이었으면 안내 띠를 보여 주고 방으로
        if (inMatch && RoundManager.Instance != null)
        {
            RoundManager.Instance.ShowMatchAbort(order.Count <= 1 ? "상대가 없어 게임을 종료합니다." : "방장이 나가 게임을 종료합니다.");
            yield return new WaitForSecondsRealtime(3f);
        }
        MatchContext.Active = false;
        MatchContext.ReturnToRoom = room != null;
        if (SceneManager.GetActiveScene().name != MatchService.LobbyScene)
            SceneManager.LoadScene(MatchService.LobbyScene);
        yield return null;
        yield return null;

        bool ok = false;
        bool iAmHost = false;
        float start = Time.realtimeSinceStartup;
        List<RoomPlayer> view = new List<RoomPlayer>(players); // 넘기는 동안 보여 줄 방 화면
        if (myRank > 0)
            yield return new WaitForSecondsRealtime(1.5f); // 새 방장이 방을 열 시간
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
                yield return new WaitForSecondsRealtime(1f);
            }
        }
        handingOver = false;

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
            StartCoroutine(ErrorLater(iAmHost ? "방장이 되셨습니다!" : (host != null ? host.nickname : "") + " 님이 새 방장이 되었어요."));
        }
        else
        {
            players.Clear();
            current = null;
            MatchContext.ReturnToRoom = false;
            RoomChanged?.Invoke();
            StartCoroutine(ErrorLater("인터넷 연결이 불안정해서 방에 다시 들어가지 못했어요."));
            RejoinLobby();
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
        yield return new WaitForSecondsRealtime(0.5f); // 알림이 모두에게 도착할 시간
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
