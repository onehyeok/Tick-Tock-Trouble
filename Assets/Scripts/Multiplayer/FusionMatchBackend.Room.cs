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

        Dictionary<string, SessionProperty> props = new Dictionary<string, SessionProperty>
        {
            { "map", settings.mapIndex },
            { "rounds", settings.rounds },
            { "lock", settings.HasPassword ? 1 : 0 },
            { "ing", 0 },
            { "nicks", nickname }
        };

        NetworkRunner r = EnsureRunner();
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

    public void JoinRoom(string roomName, string password)
    {
        RoomInfo info = rooms.Find(x => x.name == roomName);
        if (info != null && info.inGame)
        {
            Error?.Invoke("게임 중인 방이에요.");
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
        List<RoomInfo> open = rooms.FindAll(x => x.IsJoinable && !x.locked);
        if (open.Count == 0)
        {
            Error?.Invoke("참가할 수 있는 방이 없어요.");
            return;
        }
        _ = JoinAsync(open[UnityEngine.Random.Range(0, open.Count)].name, "", false);
    }

    async Task<bool> JoinAsync(string roomName, string password, bool silent)
    {
        if (busy || InRoom || quitting)
            return false;
        busy = true;

        NetworkRunner r = EnsureRunner();
        StartGameResult result = await r.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Client,
            SessionName = roomName,
            ConnectionToken = Encoding.UTF8.GetBytes(password ?? ""),
            SceneManager = r.GetComponent<NetworkSceneManagerDefault>(),
            StartGameCancellationToken = quitCts.Token
        });
        if (quitting)
            return false;
        busy = false;

        if (!result.Ok)
        {
            runner = null;
            if (silent)
                return false;
            string msg;
            switch (result.ShutdownReason)
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
        current = new RoomSettings { roomName = roomName };
        players.Clear();
        SendToHost(Msg(C2H_Hello, w => w.Write(nickname)));
        RoomChanged?.Invoke();
        return true;
    }

    public async void LeaveRoom()
    {
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

        // 방장이 나갔거나 연결이 끊김 → 방을 없애지 않고, 먼저 들어온 사람이 새 방장이 되어 같은 방을 다시 엶
        int myOldId = LocalPlayerId;
        RoomSettings oldRoom = current;
        List<RoomPlayer> oldPlayers = new List<RoomPlayer>(players);
        bool inMatch = MatchContext.Active;

        InRoom = false;
        players.Clear();
        current = null;
        runner = null;
        ClearGameState();
        RoomChanged?.Invoke();
        StartCoroutine(Handover(oldRoom, oldPlayers, myOldId, inMatch));
    }

    // ---------------- 방장이 나갔을 때: 먼저 들어온 사람에게 방장 권한 ----------------

    /// <summary>새 방에 다시 모였을 때 원래 팀을 그대로 (닉네임 → 팀)</summary>
    readonly Dictionary<string, int> pendingTeams = new Dictionary<string, int>();

    IEnumerator Handover(RoomSettings room, List<RoomPlayer> oldPlayers, int myOldId, bool inMatch)
    {
        // 남은 사람 중 가장 먼저 들어온 사람(목록 순서 = 들어온 순서, 봇 제외)
        RoomPlayer successor = oldPlayers.Find(p => !p.isHost && !IsBotId(p.id));
        int remaining = oldPlayers.FindAll(p => !p.isHost && !IsBotId(p.id)).Count;
        bool iAmNext = successor != null && successor.id == myOldId;

        pendingTeams.Clear();
        foreach (RoomPlayer p in oldPlayers)
        {
            if (!p.isHost && !IsBotId(p.id) && !string.IsNullOrEmpty(p.nickname))
                pendingTeams[p.nickname] = p.team;
        }

        // 게임 중이었으면 안내 띠를 보여 주고 방으로
        if (inMatch && RoundManager.Instance != null)
        {
            RoundManager.Instance.ShowMatchAbort(remaining <= 1 ? "상대가 없어 게임을 종료합니다." : "방장이 나가 게임을 종료합니다.");
            yield return new WaitForSecondsRealtime(3f);
        }
        MatchContext.Active = false;
        MatchContext.ReturnToRoom = room != null;
        if (SceneManager.GetActiveScene().name != MatchService.LobbyScene)
            SceneManager.LoadScene(MatchService.LobbyScene);
        yield return null;
        yield return null;

        bool ok = false;
        if (room != null && (iAmNext || successor != null))
        {
            if (iAmNext)
            {
                // 내가 새 방장: 같은 이름·설정으로 방을 다시 엶 (이전 방이 닫힐 때까지 몇 번 재시도)
                for (int tries = 0; tries < 6 && !ok && !quitting; tries++)
                {
                    Task<bool> t = CreateRoomAsync(room, true);
                    while (!t.IsCompleted)
                        yield return null;
                    ok = t.Result;
                    if (!ok)
                        yield return new WaitForSecondsRealtime(1.2f);
                }
                if (ok)
                    StartCoroutine(ErrorLater("방장이 되셨습니다!"));
            }
            else
            {
                // 새 방장이 방을 열 때까지 기다렸다가 다시 들어감
                yield return new WaitForSecondsRealtime(2.5f);
                for (int tries = 0; tries < 8 && !ok && !quitting; tries++)
                {
                    Task<bool> t = JoinAsync(room.roomName, room.password, true);
                    while (!t.IsCompleted)
                        yield return null;
                    ok = t.Result;
                    if (!ok)
                        yield return new WaitForSecondsRealtime(1.5f);
                }
                if (ok)
                    StartCoroutine(ErrorLater((successor.nickname ?? "") + " 님이 새 방장이 되었어요."));
            }
        }

        if (!ok && !quitting)
        {
            MatchContext.ReturnToRoom = false;
            RoomChanged?.Invoke();
            StartCoroutine(ErrorLater("방장이 나가서 방이 닫혔어요."));
            RejoinLobby();
        }
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
        if (!pwOk || full || MatchContext.Active)
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
        int team = TeamCount(RoomPlayer.TeamWinder) < TeamCount(RoomPlayer.TeamTickToy) ? RoomPlayer.TeamWinder : RoomPlayer.TeamTickToy;
        if (pendingTeams.TryGetValue(nick ?? "", out int kept))
            team = kept; // 방장이 바뀌어 다시 모인 경우: 원래 팀 그대로
        if (TeamCount(team) >= MatchContext.TeamSize)
            team = 1 - team;
        players.Add(new RoomPlayer { id = player.PlayerId, nickname = nick, team = team, ready = false });
        BroadcastRoom();
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
            mapIndex = rd.ReadInt32(),
            rounds = rd.ReadInt32(),
            maxPlayers = rd.ReadInt32()
        };
        current.password = rd.ReadString();
        players.Clear();
        int n = rd.ReadByte();
        int me = LocalPlayerId;
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
            { "map", current.mapIndex },
            { "rounds", current.rounds },
            { "lock", current.HasPassword ? 1 : 0 },
            { "ing", MatchContext.Active ? 1 : 0 },
            { "nicks", string.Join(",", nicks) }
        });
    }
}
