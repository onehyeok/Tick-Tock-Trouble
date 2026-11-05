using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Photon Fusion (Host 모드) 로비·방·게임 진행.
/// - 로비: 세션 목록 = 방 목록 (세션 속성: map / rounds / lock / ing / nicks)
/// - 방: 방장이 플레이어 목록(팀/준비)을 관리하고 모두에게 보냄
/// - 게임: 각자 자기 캐릭터를 움직이고 위치·애니메이션을 방장에게 보냄 → 방장이 모두에게 전달.
///   다른 사람은 NetPuppet 으로 보임. 맞은 판정은 방장을 거쳐 맞은 사람에게 전달.
///   라운드 진행(숨기/추격/결과)과 승패는 방장이 정하고 모두에게 보냄. 맵 AI 는 방장 쪽에서만 움직이고 나머지는 따라 보여줌.
/// PhotonAppSettings 의 App Id Fusion 이 비어 있으면 예전 오프라인 백엔드를 씁니다.
/// </summary>
public class FusionMatchBackend : MonoBehaviour, IMatchBackend, INetworkRunnerCallbacks
{
    public static FusionMatchBackend Instance { get; private set; }

    /// <summary>방에 들어가 있고 Fusion 세션이 돌고 있을 때</summary>
    public static FusionMatchBackend Online => Instance != null && Instance.InRoom && Instance.runner != null ? Instance : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register()
    {
        Instance = null;
        MatchService.Factory = () =>
        {
            if (!HasAppId())
            {
                Debug.LogWarning("[Fusion] Assets/Photon/Fusion/Resources/PhotonAppSettings 의 App Id Fusion 이 비어 있어서 오프라인 모드로 실행해요.");
                return new OfflineMatchBackend();
            }
            GameObject go = new GameObject("FusionMatch");
            DontDestroyOnLoad(go);
            return go.AddComponent<FusionMatchBackend>();
        };
    }

    static bool HasAppId()
    {
        ScriptableObject settings = Resources.Load<ScriptableObject>("PhotonAppSettings");
        if (settings == null)
            return false;
        object app = GetMember(settings, "AppSettings");
        return !string.IsNullOrEmpty(GetMember(app, "AppIdFusion") as string);
    }

    static object GetMember(object o, string name)
    {
        if (o == null)
            return null;
        Type t = o.GetType();
        FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (f != null)
            return f.GetValue(o);
        PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return p != null ? p.GetValue(o) : null;
    }

    // ---------------- 메시지 종류 ----------------
    const byte C2H_Hello = 1;
    const byte C2H_Team = 2;
    const byte C2H_Ready = 3;
    const byte C2H_Pose = 4;
    const byte C2H_Hit = 5;
    const byte C2H_Chat = 6;
    const byte H2C_Room = 20;
    const byte H2C_Start = 21;
    const byte H2C_LoadMap = 22;
    const byte H2C_Return = 23;
    const byte H2C_Phase = 24;
    const byte H2C_Poses = 25;
    const byte H2C_Hit = 26;
    const byte H2C_Chat = 27;

    const int AiIdBase = 10000;
    /// <summary>테스트 봇 id (1000~): 사람 PlayerId 와 AI id 사이</summary>
    const int BotIdBase = 1000;
    int nextBotId = BotIdBase;

    static bool IsBotId(int id) => id >= BotIdBase && id < AiIdBase;
    const float PoseInterval = 0.1f;
    const float AiPoseInterval = 0.2f;

    NetworkRunner runner;
    string nickname = "";
    bool inLobby;
    bool busy;
    bool leaving;
    bool lobbyListReceived;
    RoomSettings current;

    readonly List<RoomInfo> rooms = new List<RoomInfo>();
    readonly List<RoomPlayer> players = new List<RoomPlayer>();

    // 게임 중
    readonly Dictionary<int, NetPose> remotePoses = new Dictionary<int, NetPose>();
    readonly Dictionary<int, NetPuppet> puppets = new Dictionary<int, NetPuppet>();
    readonly Dictionary<TickToyAI, int> aiIds = new Dictionary<TickToyAI, int>();
    readonly Dictionary<int, TickToyAI> aiById = new Dictionary<int, TickToyAI>();
    int nextAiId = AiIdBase;
    float poseTimer;
    float aiPoseTimer;
    float aiRefreshTimer;
    float phaseTimer;
    int lastSentPhase = -1;

    public bool IsConnected => inLobby || InRoom;
    public bool InRoom { get; private set; }
    public bool IsHost => InRoom && runner != null && runner.IsServer;
    public int LocalPlayerId => InRoom && runner != null ? runner.LocalPlayer.PlayerId : 0;
    public RoomSettings CurrentRoom => InRoom ? current : null;
    public IReadOnlyList<RoomInfo> Rooms => rooms;
    public IReadOnlyList<RoomPlayer> RoomPlayers => players;

    public event Action RoomsChanged;
    public event Action RoomChanged;
    public event Action<string> Error;
    public event Action GameLoading;

    void Awake()
    {
        Instance = this;
        SceneManager.sceneLoaded += OnUnitySceneLoaded;
    }

    // 플레이 모드 종료 / 게임 종료 시 진행 중인 접속을 끊음 (안 끊으면 에디터 모드에서 접속이 이어져
    // "DontDestroyOnLoad can only be used in play mode" 오류가 뜸)
    readonly CancellationTokenSource quitCts = new CancellationTokenSource();
    bool quitting;

    void OnApplicationQuit()
    {
        StopAll();
    }

    void StopAll()
    {
        if (quitting)
            return;
        quitting = true;
        quitCts.Cancel();
        if (runner != null && !runner.IsShutdown)
            runner.Shutdown();
        runner = null;
    }

    void OnDestroy()
    {
        StopAll();
        SceneManager.sceneLoaded -= OnUnitySceneLoaded;
        if (Instance == this)
            Instance = null;
    }

    // ---------------- 러너 ----------------

    NetworkRunner NewRunner()
    {
        GameObject go = new GameObject("FusionRunner");
        go.transform.SetParent(transform, false);
        NetworkRunner r = go.AddComponent<NetworkRunner>();
        go.AddComponent<NetworkSceneManagerDefault>();
        r.ProvideInput = false;
        r.AddCallbacks(this);
        return r;
    }

    NetworkRunner EnsureRunner()
    {
        if (runner == null || runner.IsShutdown)
            runner = NewRunner();
        return runner;
    }

    // ---------------- 로비 ----------------

    public async void ConnectLobby(string nick, Action<bool, string> done)
    {
        if (quitting)
            return;
        nickname = nick ?? "";
        if (inLobby || InRoom)
        {
            done?.Invoke(true, null);
            return;
        }

        lobbyListReceived = false;
        StartGameResult result = await EnsureRunner().JoinSessionLobby(SessionLobby.ClientServer, null, null, null, quitCts.Token);
        if (quitting)
            return;
        if (!result.Ok)
        {
            inLobby = false;
            runner = null;
            done?.Invoke(false, "서버에 연결하지 못했어요. (" + result.ShutdownReason + ")");
            return;
        }

        inLobby = true;
        StartCoroutine(WaitFirstList(done));
    }

    IEnumerator WaitFirstList(Action<bool, string> done)
    {
        float until = Time.realtimeSinceStartup + 2f;
        while (!lobbyListReceived && Time.realtimeSinceStartup < until)
            yield return null;
        done?.Invoke(true, null);
        RoomsChanged?.Invoke();
    }

    async void RejoinLobby()
    {
        if (quitting)
            return;
        rooms.Clear();
        RoomsChanged?.Invoke();
        runner = null;
        StartGameResult result = await EnsureRunner().JoinSessionLobby(SessionLobby.ClientServer, null, null, null, quitCts.Token);
        if (quitting)
            return;
        inLobby = result.Ok;
        if (!result.Ok)
        {
            runner = null;
            Error?.Invoke("서버 연결이 끊겼어요. (" + result.ShutdownReason + ")");
        }
    }

    public bool IsNicknameTaken(string nick)
    {
        foreach (RoomInfo r in rooms)
        {
            foreach (string n in r.nicknames)
            {
                if (string.Equals(n, nick, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    public void OnSessionListUpdated(NetworkRunner r, List<SessionInfo> sessionList)
    {
        if (r != runner)
            return;
        rooms.Clear();
        foreach (SessionInfo s in sessionList)
        {
            if (s == null || !s.IsVisible)
                continue;
            RoomInfo info = new RoomInfo
            {
                name = s.Name,
                players = s.PlayerCount,
                maxPlayers = s.MaxPlayers,
                mapIndex = PropInt(s, "map", 0),
                rounds = PropInt(s, "rounds", 3),
                locked = PropInt(s, "lock", 0) == 1,
                inGame = PropInt(s, "ing", 0) == 1 || !s.IsOpen
            };
            string nicks = PropString(s, "nicks");
            if (!string.IsNullOrEmpty(nicks))
                info.nicknames.AddRange(nicks.Split(','));
            rooms.Add(info);
        }
        lobbyListReceived = true;
        RoomsChanged?.Invoke();
    }

    static int PropInt(SessionInfo s, string key, int fallback)
    {
        if (s.Properties != null && s.Properties.TryGetValue(key, out SessionProperty p) && p != null && p.PropertyValue != null)
        {
            try { return Convert.ToInt32(p.PropertyValue); } catch { }
        }
        return fallback;
    }

    static string PropString(SessionInfo s, string key)
    {
        if (s.Properties != null && s.Properties.TryGetValue(key, out SessionProperty p) && p != null)
            return p.PropertyValue as string;
        return null;
    }

    // ---------------- 방 만들기 / 들어가기 / 나가기 ----------------

    public async void CreateRoom(RoomSettings settings)
    {
        if (busy || InRoom || quitting)
            return;
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
            return;
        busy = false;

        if (!result.Ok)
        {
            Error?.Invoke(result.ShutdownReason == ShutdownReason.GameIdAlreadyExists ? "같은 이름의 방이 이미 있어요." : "방을 만들지 못했어요. (" + result.ShutdownReason + ")");
            RejoinLobby();
            return;
        }

        inLobby = false;
        InRoom = true;
        ChatLog.Clear();
        players.Clear();
        players.Add(new RoomPlayer { id = LocalPlayerId, nickname = nickname, team = RoomPlayer.TeamTickToy, ready = true, isHost = true, isLocal = true });
        RoomChanged?.Invoke();
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
        Join(roomName, password);
    }

    public void JoinRandom()
    {
        List<RoomInfo> open = rooms.FindAll(x => x.IsJoinable && !x.locked);
        if (open.Count == 0)
        {
            Error?.Invoke("참가할 수 있는 방이 없어요.");
            return;
        }
        Join(open[UnityEngine.Random.Range(0, open.Count)].name, "");
    }

    async void Join(string roomName, string password)
    {
        if (busy || InRoom || quitting)
            return;
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
            return;
        busy = false;

        if (!result.Ok)
        {
            string msg;
            switch (result.ShutdownReason)
            {
                case ShutdownReason.ConnectionRefused: msg = "들어갈 수 없어요. 암호가 틀렸거나 게임 중인 방이에요."; break;
                case ShutdownReason.GameIsFull: msg = "방이 꽉 찼어요."; break;
                case ShutdownReason.GameNotFound: msg = "방을 찾을 수 없어요."; break;
                default: msg = "방에 들어가지 못했어요. (" + result.ShutdownReason + ")"; break;
            }
            Error?.Invoke(msg);
            RejoinLobby();
            return;
        }

        inLobby = false;
        InRoom = true;
        ChatLog.Clear();
        current = new RoomSettings { roomName = roomName };
        players.Clear();
        SendToHost(Msg(C2H_Hello, w => w.Write(nickname)));
        RoomChanged?.Invoke();
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

        // 방장이 나갔거나 연결이 끊김
        InRoom = false;
        players.Clear();
        current = null;
        ClearGameState();
        bool inMatch = MatchContext.Active;
        MatchContext.Active = false;
        MatchContext.ReturnToRoom = false;
        if (inMatch || SceneManager.GetActiveScene().name != MatchService.LobbyScene)
            SceneManager.LoadScene(MatchService.LobbyScene);
        RoomChanged?.Invoke();
        StartCoroutine(ErrorLater("방과 연결이 끊어졌어요. (방장이 나갔을 수 있어요)"));
        // 바로 새 러너를 만들면 Fusion 이 러너 목록을 도는 중(스크립트 재컴파일 등)에 목록이 바뀌어 오류 → 다음 프레임에
        StartCoroutine(RejoinNextFrame());
    }

    IEnumerator RejoinNextFrame()
    {
        yield return null;
        RejoinLobby();
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
        remotePoses.Remove(player.PlayerId);
        RemovePuppet(player.PlayerId);
        BroadcastRoom();
    }

    void HostAddPlayer(PlayerRef player, string nick)
    {
        if (players.Exists(p => p.id == player.PlayerId))
            return;
        int team = TeamCount(RoomPlayer.TeamWinder) < TeamCount(RoomPlayer.TeamTickToy) ? RoomPlayer.TeamWinder : RoomPlayer.TeamTickToy;
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
        w.Write(current.HasPassword);
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
        bool locked = rd.ReadBoolean();
        current.password = locked ? "*" : "";
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

    // ---------------- 게임 시작 / 라운드 / 방으로 ----------------

    public void StartGame()
    {
        if (!IsHost)
            return;
        if (TeamCount(RoomPlayer.TeamTickToy) < 1 || TeamCount(RoomPlayer.TeamWinder) < 1)
        {
            Error?.Invoke("틱토이와 와인더가 한 명씩은 있어야 해요.");
            return;
        }
        foreach (RoomPlayer p in players)
        {
            if (!p.isHost && !p.ready)
            {
                Error?.Invoke("모두 준비해야 시작할 수 있어요.");
                return;
            }
        }

        MapEntry map = MapCatalog.Load()?.Get(current.mapIndex);
        if (map == null)
            return;

        MatchContext.BeginMatch(current, players, LocalPlayerId);
        UpdateSessionProps();
        SendToClients(Msg(H2C_Start, w =>
        {
            WriteRoom(w);
            w.Write(map.sceneName);
        }));
        LoadMatchScene(map.sceneName);
    }

    public void LoadMap(string sceneName)
    {
        if (!IsHost)
            return;
        SendToClients(Msg(H2C_LoadMap, w =>
        {
            w.Write(MatchContext.Round);
            w.Write(MatchContext.TickToyWins);
            w.Write(MatchContext.WinderWins);
            w.Write(sceneName);
        }));
        LoadMatchScene(sceneName);
    }

    public void ReturnToRoom()
    {
        if (!IsHost)
            return;
        SendToClients(Msg(H2C_Return, w => { }));
        DoReturnToRoom();
        foreach (RoomPlayer p in players)
            p.ready = p.isHost;
        BroadcastRoom();
    }

    void DoReturnToRoom()
    {
        MatchContext.End();
        ClearGameState();
        SceneManager.LoadSceneAsync(MatchService.LobbyScene);
    }

    void LoadMatchScene(string sceneName)
    {
        ClearGameState();
        GameLoading?.Invoke();
        SceneManager.LoadSceneAsync(sceneName);
    }

    void ClearGameState()
    {
        remotePoses.Clear();
        foreach (NetPuppet p in puppets.Values)
        {
            if (p != null)
                Destroy(p.gameObject);
        }
        puppets.Clear();
        aiIds.Clear();
        aiById.Clear();
        nextAiId = AiIdBase;
        lastSentPhase = -1;
        netAliveTickToys = -1;
        netAliveWinders = -1;
    }

    void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
            return;
        puppets.Clear();
        aiIds.Clear();
        aiById.Clear();
        lastSentPhase = -1;
    }

    // ---------------- 게임 중 동기화 ----------------

    int netAliveTickToys = -1;
    int netAliveWinders = -1;
    readonly HashSet<int> connectedIds = new HashSet<int>();

    /// <summary>살아있는 수. 방장은 직접 계산, 나머지는 방장이 보낸 값</summary>
    public int AliveCount(bool winders, int localAliveFallback)
    {
        if (!IsHost)
        {
            int v = winders ? netAliveWinders : netAliveTickToys;
            return v >= 0 ? v : localAliveFallback;
        }

        HashSet<int> connected = connectedIds;
        connected.Clear();
        foreach (PlayerRef p in runner.ActivePlayers)
            connected.Add(p.PlayerId);

        RoundManager rm = RoundManager.Instance;
        int n = 0;
        foreach (RoomPlayer p in MatchContext.Players)
        {
            bool isWinder = MatchContext.WinderIds.Contains(p.id);
            if (isWinder != winders)
                continue;
            if (p.id == LocalPlayerId)
            {
                if (rm != null && rm.LocalAlive)
                    n++;
                continue;
            }
            if (IsBotId(p.id))
            {
                n++; // 테스트 봇은 항상 살아있음 (오프라인 봇과 같음)
                continue;
            }
            if (!connected.Contains(p.id))
                continue;
            if (remotePoses.TryGetValue(p.id, out NetPose pose) && !pose.alive)
                continue;
            n++;
        }
        return n;
    }

    // ---------------- 채팅 ----------------

    public void SendChat(string text)
    {
        if (!InRoom || string.IsNullOrEmpty(text))
            return;
        if (IsHost)
            HostBroadcastChat(nickname, text);
        else
            SendToHost(Msg(C2H_Chat, w => w.Write(text)));
    }

    void HostBroadcastChat(string nick, string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return;
        if (text.Length > 50)
            text = text.Substring(0, 50);
        SendToClients(Msg(H2C_Chat, w =>
        {
            w.Write(nick ?? "");
            w.Write(text);
        }));
        ChatLog.Add(nick, text);
    }

    /// <summary>내 와인더가 다른 사람(또는 방장 쪽 AI)의 퍼펫을 때림</summary>
    public void SendHit(NetPuppet target, int damage)
    {
        if (target == null || !InRoom)
            return;
        if (IsHost)
            RouteHit(target.NetId, damage);
        else
            SendToHost(Msg(C2H_Hit, w =>
            {
                w.Write(target.NetId);
                w.Write(damage);
            }));
    }

    void RouteHit(int targetId, int damage)
    {
        if (targetId >= AiIdBase)
        {
            if (aiById.TryGetValue(targetId, out TickToyAI ai) && ai != null && !ai.IsDead)
                ai.TakeHit(damage);
            return;
        }
        if (targetId == LocalPlayerId)
        {
            RoundManager.Instance?.ApplyNetHit(damage);
            return;
        }
        foreach (PlayerRef p in runner.ActivePlayers)
        {
            if (p.PlayerId == targetId)
            {
                runner.SendReliableDataToPlayer(p, default(ReliableKey), Msg(H2C_Hit, w => w.Write(damage)));
                return;
            }
        }
    }

    void Update()
    {
        if (!InRoom || runner == null || !MatchContext.Active)
            return;
        RoundManager rm = RoundManager.Instance;
        if (rm == null || rm.LocalPlayerObject == null)
            return;

        float dt = Time.unscaledDeltaTime;
        poseTimer += dt;
        aiPoseTimer += dt;
        aiRefreshTimer += dt;
        phaseTimer += dt;

        if (poseTimer >= PoseInterval)
        {
            poseTimer = 0f;
            NetPose mine = NetPose.Capture(rm.LocalPlayerObject, LocalPlayerId,
                rm.LocalIsWinderRole ? NetPose.KindWinder : NetPose.KindTickToy, (byte)rm.LocalCharIndex, rm.LocalAlive);

            if (IsHost)
            {
                bool withAi = aiPoseTimer >= AiPoseInterval;
                if (withAi)
                    aiPoseTimer = 0f;
                if (aiRefreshTimer >= 1f)
                {
                    aiRefreshTimer = 0f;
                    RefreshAIs();
                }
                HostBroadcastPoses(mine, withAi);
            }
            else
                SendToHost(Msg(C2H_Pose, w => mine.Write(w)));
        }

        if (IsHost)
        {
            // 방장: 다른 플레이어 퍼펫 갱신
            foreach (KeyValuePair<int, NetPose> kv in remotePoses)
                ApplyPose(kv.Value);

            int phase = rm.PhaseIndex;
            if (phase != lastSentPhase || phaseTimer >= 1f)
            {
                lastSentPhase = phase;
                phaseTimer = 0f;
                int aliveT = AliveCount(false, 0);
                int aliveW = AliveCount(true, 0);
                SendToClients(Msg(H2C_Phase, w =>
                {
                    w.Write((byte)phase);
                    w.Write(rm.PhaseLeft);
                    w.Write(rm.ResultWinderWon);
                    w.Write(MatchContext.TickToyWins);
                    w.Write(MatchContext.WinderWins);
                    w.Write((byte)aliveT);
                    w.Write((byte)aliveW);
                }));
            }
        }
    }

    void RefreshAIs()
    {
        foreach (TickToyAI ai in FindObjectsByType<TickToyAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
        {
            if (aiIds.ContainsKey(ai))
                continue;
            int id = nextAiId++;
            aiIds[ai] = id;
            aiById[id] = ai;
        }
    }

    void HostBroadcastPoses(NetPose mine, bool withAi)
    {
        List<NetPose> list = new List<NetPose>(remotePoses.Count + 1 + (withAi ? aiIds.Count : 0));
        list.Add(mine);
        list.AddRange(remotePoses.Values);
        if (withAi)
        {
            foreach (KeyValuePair<TickToyAI, int> kv in aiIds)
            {
                TickToyAI ai = kv.Key;
                if (ai == null || !ai.isActiveAndEnabled)
                    continue;
                list.Add(NetPose.Capture(ai.gameObject, kv.Value, NetPose.KindAI, (byte)CharIndexOf(ai.gameObject), !ai.IsDead));
            }
        }

        SendToClients(Msg(H2C_Poses, w =>
        {
            w.Write(withAi);
            w.Write((ushort)list.Count);
            foreach (NetPose p in list)
                p.Write(w);
        }));
    }

    /// <summary>틱토이 프리팹 중 몇 번째 캐릭터인지 (이름으로)</summary>
    public static int CharIndexOf(GameObject go)
    {
        MatchPrefabs prefabs = MatchPrefabs.Load();
        if (prefabs == null || prefabs.tickToyPrefabs == null || go == null)
            return 0;
        string n = go.name.ToLowerInvariant();
        for (int i = 0; i < prefabs.tickToyPrefabs.Length; i++)
        {
            GameObject p = prefabs.tickToyPrefabs[i];
            if (p != null && n.Contains(p.name.ToLowerInvariant()))
                return i;
        }
        return 0;
    }

    void ApplyPose(NetPose pose)
    {
        if (pose.id == LocalPlayerId)
            return;
        if (!puppets.TryGetValue(pose.id, out NetPuppet pp) || pp == null)
        {
            MatchPrefabs prefabs = MatchPrefabs.Load();
            if (prefabs == null)
                return;
            GameObject prefab = pose.kind == NetPose.KindWinder
                ? prefabs.winderPrefab
                : (prefabs.tickToyPrefabs != null && prefabs.tickToyPrefabs.Length > 0
                    ? prefabs.tickToyPrefabs[Mathf.Clamp(pose.ch, 0, prefabs.tickToyPrefabs.Length - 1)]
                    : null);
            pp = NetPuppet.Create(prefab, pose);
            if (pp == null)
                return;
            puppets[pose.id] = pp;
        }
        pp.SetTarget(pose);
    }

    void RemovePuppet(int id)
    {
        if (puppets.TryGetValue(id, out NetPuppet pp) && pp != null)
            Destroy(pp.gameObject);
        puppets.Remove(id);
    }

    void ClientApplyPoses(BinaryReader rd)
    {
        bool withAi = rd.ReadBoolean();
        int n = rd.ReadUInt16();
        HashSet<int> seen = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            NetPose p = NetPose.Read(rd);
            seen.Add(p.id);
            if (RoundManager.Instance != null)
                ApplyPose(p);
        }

        // 목록에서 빠진 캐릭터 제거 (AI 는 AI 목록이 같이 온 경우에만)
        List<int> remove = new List<int>();
        foreach (KeyValuePair<int, NetPuppet> kv in puppets)
        {
            bool isAi = kv.Key >= AiIdBase;
            if (isAi && !withAi)
                continue;
            if (!seen.Contains(kv.Key))
                remove.Add(kv.Key);
        }
        foreach (int id in remove)
            RemovePuppet(id);
    }

    // ---------------- 메시지 송수신 ----------------

    static byte[] Msg(byte op, Action<BinaryWriter> body)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(op);
            body?.Invoke(w);
            w.Flush();
            return ms.ToArray();
        }
    }

    void SendToHost(byte[] data)
    {
        if (runner != null && runner.IsRunning && !runner.IsServer)
            runner.SendReliableDataToServer(default(ReliableKey), data);
    }

    void SendToClients(byte[] data)
    {
        if (runner == null || !runner.IsRunning || !runner.IsServer)
            return;
        foreach (PlayerRef p in runner.ActivePlayers)
        {
            if (p != runner.LocalPlayer)
                runner.SendReliableDataToPlayer(p, default(ReliableKey), data);
        }
    }

    public void OnReliableDataReceived(NetworkRunner r, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
    {
        if (r != runner || data.Length == 0)
            return;
        byte[] buf = data.ToArray();
        try
        {
            using (MemoryStream ms = new MemoryStream(buf))
            using (BinaryReader rd = new BinaryReader(ms))
            {
                byte op = rd.ReadByte();
                if (runner.IsServer)
                    HandleOnHost(player, op, rd);
                else
                    HandleOnClient(op, rd);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Fusion] 메시지 처리 실패: " + e.Message);
        }
    }

    void HandleOnHost(PlayerRef from, byte op, BinaryReader rd)
    {
        switch (op)
        {
            case C2H_Hello:
                HostAddPlayer(from, rd.ReadString());
                break;
            case C2H_Team:
                HostSetTeam(from.PlayerId, rd.ReadByte());
                break;
            case C2H_Ready:
                HostSetReady(from.PlayerId, rd.ReadBoolean());
                break;
            case C2H_Pose:
            {
                NetPose p = NetPose.Read(rd);
                p.id = from.PlayerId;
                if (MatchContext.Active)
                    remotePoses[p.id] = p;
                break;
            }
            case C2H_Chat:
            {
                string text = rd.ReadString();
                RoomPlayer p = players.Find(x => x.id == from.PlayerId);
                if (p != null)
                    HostBroadcastChat(p.nickname, text);
                break;
            }
            case C2H_Hit:
            {
                int target = rd.ReadInt32();
                int damage = rd.ReadInt32();
                if (MatchContext.Active)
                    RouteHit(target, damage);
                break;
            }
        }
    }

    void HandleOnClient(byte op, BinaryReader rd)
    {
        switch (op)
        {
            case H2C_Room:
                ReadRoom(rd);
                RoomChanged?.Invoke();
                break;

            case H2C_Start:
            {
                ReadRoom(rd);
                string scene = rd.ReadString();
                MatchContext.BeginMatch(current, players, LocalPlayerId);
                LoadMatchScene(scene);
                break;
            }

            case H2C_LoadMap:
            {
                MatchContext.Round = rd.ReadInt32();
                MatchContext.TickToyWins = rd.ReadInt32();
                MatchContext.WinderWins = rd.ReadInt32();
                LoadMatchScene(rd.ReadString());
                break;
            }

            case H2C_Return:
                DoReturnToRoom();
                break;

            case H2C_Phase:
            {
                int phase = rd.ReadByte();
                float left = rd.ReadSingle();
                bool winderWon = rd.ReadBoolean();
                int ttWins = rd.ReadInt32();
                int wWins = rd.ReadInt32();
                netAliveTickToys = rd.ReadByte();
                netAliveWinders = rd.ReadByte();
                RoundManager.Instance?.ApplyNetPhase(phase, left, winderWon, ttWins, wWins);
                break;
            }

            case H2C_Poses:
                if (MatchContext.Active)
                    ClientApplyPoses(rd);
                break;

            case H2C_Hit:
                RoundManager.Instance?.ApplyNetHit(rd.ReadInt32());
                break;

            case H2C_Chat:
            {
                string nick = rd.ReadString();
                ChatLog.Add(nick, rd.ReadString());
                break;
            }
        }
    }

    // ---------------- 안 쓰는 콜백 ----------------
    public void OnObjectExitAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
    public void OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason) { }
    public void OnConnectFailed(NetworkRunner r, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr message) { }
    public void OnReliableDataProgress(NetworkRunner r, PlayerRef player, ReliableKey key, float progress) { }
    public void OnInput(NetworkRunner r, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner r, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner r) { }
    public void OnCustomAuthenticationResponse(NetworkRunner r, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner r, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner r) { }
    public void OnSceneLoadStart(NetworkRunner r) { }
}
