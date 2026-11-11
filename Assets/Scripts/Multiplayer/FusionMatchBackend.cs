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
/// - 게임: 각자 자기 캐릭터를 움직이고 매 틱 위치·애니메이션을 Fusion 입력으로 보냄 → 방장이 NetCharacter
///   (NetworkObject) 에 기록 → Fusion 이 모두에게 동기화. 다른 사람은 NetPuppet 으로 보임.
///   맞은 판정은 방장을 거쳐 맞은 사람에게 전달.
///   라운드 진행(숨기/추격/결과)과 승패는 방장이 정하고 모두에게 보냄. 맵 AI 는 방장 쪽에서만 움직이고 나머지는 따라 보여줌.
/// PhotonAppSettings 의 App Id Fusion 이 비어 있으면 예전 오프라인 백엔드를 씁니다.
/// 파일 구성: 이 파일(연결·로비) / .Room(방) / .Game(게임 진행·네트워크 캐릭터·채팅) / .Messages(메시지 송수신)
/// </summary>
public partial class FusionMatchBackend : MonoBehaviour, IMatchBackend, INetworkRunnerCallbacks
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
    const byte C2H_Hit = 5;
    const byte C2H_Chat = 6;
    const byte C2H_Event = 7;      // 게임 이벤트(덫 설치·덫 발동·태엽 정지) → 방장이 모두에게 전달
    const byte C2H_Duplicate = 8;  // 분신 만들어 달라고 방장에게 요청
    const byte H2C_Room = 20;
    const byte H2C_Start = 21;
    const byte H2C_LoadMap = 22;
    const byte H2C_Return = 23;
    const byte H2C_Hit = 26;
    const byte H2C_Chat = 27;
    const byte H2C_Event = 28;
    const byte H2C_Abort = 29;     // 상대가 나가 게임 종료 안내
    const byte H2C_HostTransfer = 30; // 방장이 다른 사람에게 방장을 넘김 (id)
    const byte H2C_Kicked = 31;       // 방장이 나를 강퇴함
    const byte H2C_JoinMatch = 32;    // 게임 중에 들어온 사람에게: 지금 맵·라운드 (이번 라운드는 관전)

    const int AiIdBase = 10000;
    /// <summary>테스트 봇 id (1000~): 사람 PlayerId 와 AI id 사이</summary>
    const int BotIdBase = 1000;
    int nextBotId = BotIdBase;

    static bool IsBotId(int id) => id >= BotIdBase && id < AiIdBase;
    /// <summary>테스트 봇인지 (방 화면 메뉴에서 사용)</summary>
    public static bool IsBotPlayer(int id) => IsBotId(id);

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
    readonly Dictionary<TickToyAI, int> aiIds = new Dictionary<TickToyAI, int>();
    readonly Dictionary<int, TickToyAI> aiById = new Dictionary<int, TickToyAI>();
    int nextAiId = AiIdBase;
    bool playersSpawned;
    float aiRefreshTimer;

    public bool IsConnected => inLobby || InRoom || handingOver; // 방장이 바뀌는 중엔 로비에 따로 접속하지 않음
    public bool InRoom { get; private set; }
    public bool IsHost => InRoom && runner != null && runner.IsServer;
    public int LocalPlayerId => InRoom && runner != null ? runner.LocalPlayer.PlayerId : 0;
    public RoomSettings CurrentRoom => InRoom || handingOver ? current : null;
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
        r.ProvideInput = true; // 내 캐릭터 위치·애니메이션을 매 틱 입력으로 방장에게 보냄
        r.AddCallbacks(this);
        return r;
    }

    NetworkRunner EnsureRunner()
    {
        if (runner == null || runner.IsShutdown)
            runner = NewRunner();
        return runner;
    }

    void RaiseReliableSendRate()
    {
        PropertyInfo p = typeof(NetworkRunner).GetProperty("ReliableDataSendRate", BindingFlags.Public | BindingFlags.Instance);
        if (p != null && p.CanWrite && runner != null)
        {
            try { p.SetValue(runner, 60); } catch (Exception e) { Debug.LogWarning("[Fusion] ReliableDataSendRate 설정 실패: " + e.Message); }
        }
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
        NetworkRunner lobbyRunner = EnsureRunner();
        StartGameResult result = await lobbyRunner.JoinSessionLobby(SessionLobby.ClientServer, null, null, null, quitCts.Token);
        if (quitting || runner != lobbyRunner)
            return; // 그 사이 방 연결로 바뀜 → 방 연결을 건드리지 않음
        if (!result.Ok)
        {
            inLobby = false;
            runner = null;
            done?.Invoke(false, "인터넷 연결이 불안정해서 서버에 연결하지 못했어요.");
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
        NetworkRunner lobbyRunner = EnsureRunner();
        StartGameResult result = await lobbyRunner.JoinSessionLobby(SessionLobby.ClientServer, null, null, null, quitCts.Token);
        if (quitting || runner != lobbyRunner)
            return; // 그 사이 방 연결로 바뀜 → 방 연결을 건드리지 않음
        inLobby = result.Ok;
        if (!result.Ok)
        {
            runner = null;
            Error?.Invoke("인터넷 연결이 불안정해서 서버 연결이 끊겼어요.");
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
                roomId = PropString(s, "rid"),
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

    // ---------------- 안 쓰는 콜백 ----------------
    public void OnObjectExitAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
    public void OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason) { }
    public void OnConnectFailed(NetworkRunner r, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr message) { }
    public void OnReliableDataProgress(NetworkRunner r, PlayerRef player, ReliableKey key, float progress) { }
    public void OnInputMissing(NetworkRunner r, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner r) { }
    public void OnCustomAuthenticationResponse(NetworkRunner r, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner r, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner r) { }
    public void OnSceneLoadStart(NetworkRunner r) { }
}
