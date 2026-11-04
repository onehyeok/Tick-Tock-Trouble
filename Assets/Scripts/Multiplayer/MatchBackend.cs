using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 로비·방·게임 시작을 담당하는 네트워크 계층.
/// 지금은 OfflineMatchBackend(혼자 테스트용), Photon Fusion을 설치하면 Fusion 백엔드로 교체.
/// UI는 이 인터페이스만 사용합니다.
/// </summary>
public interface IMatchBackend
{
    bool IsConnected { get; }
    bool InRoom { get; }
    bool IsHost { get; }
    int LocalPlayerId { get; }
    RoomSettings CurrentRoom { get; }
    IReadOnlyList<RoomInfo> Rooms { get; }
    IReadOnlyList<RoomPlayer> RoomPlayers { get; }

    event Action RoomsChanged;
    event Action RoomChanged;
    event Action<string> Error;
    event Action GameLoading;

    void ConnectLobby(string nickname, Action<bool, string> done);
    bool IsNicknameTaken(string nickname);
    void CreateRoom(RoomSettings settings);
    void JoinRoom(string roomName, string password);
    void JoinRandom();
    void LeaveRoom();
    void SetReady(bool ready);
    /// <summary>팀 옮기기 (0 틱토이 / 1 와인더). 꽉 차 있으면 실패</summary>
    void SetTeam(int team);
    void StartGame();
    /// <summary>라운드 끝 → 다음 라운드 맵 다시 로드 (방장만)</summary>
    void LoadMap(string sceneName);
    /// <summary>모든 라운드 끝 → 방(로비 씬)으로 (방장만)</summary>
    void ReturnToRoom();
}

public static class MatchService
{
    public const string IntroScene = "Intro";
    public const string LobbyScene = "Lobby";

    static IMatchBackend backend;

    /// <summary>Fusion 백엔드가 있으면 그걸 등록 (FusionMatchBackend에서 호출)</summary>
    public static Func<IMatchBackend> Factory;

    public static IMatchBackend Backend
    {
        get
        {
            if (backend == null)
                backend = Factory != null ? Factory() : new OfflineMatchBackend();
            return backend;
        }
    }

    public static string ValidateNickname(string nick)
    {
        nick = (nick ?? "").Trim();
        if (nick.Length < 2)
            return "닉네임은 2글자 이상이어야 해요.";
        if (nick.Length > 10)
            return "닉네임은 10글자 이하로 해 주세요.";
        if (nick.Contains(",") || nick.Contains("|"))
            return "닉네임에 , 나 | 는 쓸 수 없어요.";
        return null;
    }
}

/// <summary>
/// 네트워크 없이 UI 흐름을 확인하는 백엔드. 방은 나 혼자 + (에디터 전용) 테스트 봇.
/// </summary>
public class OfflineMatchBackend : IMatchBackend
{
    readonly List<RoomInfo> rooms = new List<RoomInfo>();
    readonly List<RoomPlayer> players = new List<RoomPlayer>();
    string nickname = "";
    int nextBotId = 100;

    public bool IsConnected { get; private set; }
    public bool InRoom { get; private set; }
    public bool IsHost => true;
    public int LocalPlayerId => 1;
    public RoomSettings CurrentRoom { get; private set; }
    public IReadOnlyList<RoomInfo> Rooms => rooms;
    public IReadOnlyList<RoomPlayer> RoomPlayers => players;

    public event Action RoomsChanged;
    public event Action RoomChanged;
    public event Action<string> Error;
    public event Action GameLoading;

    public void ConnectLobby(string nick, Action<bool, string> done)
    {
        nickname = nick;
        IsConnected = true;
        done?.Invoke(true, null);
        RoomsChanged?.Invoke();
    }

    public bool IsNicknameTaken(string nick) => false;

    public void CreateRoom(RoomSettings settings)
    {
        CurrentRoom = settings;
        InRoom = true;
        players.Clear();
        players.Add(new RoomPlayer { id = LocalPlayerId, nickname = nickname, isHost = true, isLocal = true, ready = true, team = RoomPlayer.TeamTickToy });
        RoomChanged?.Invoke();
    }

    public void JoinRoom(string roomName, string password)
    {
        Error?.Invoke("오프라인 모드에서는 다른 방에 들어갈 수 없어요.");
    }

    public void JoinRandom()
    {
        Error?.Invoke("참가할 수 있는 방이 없어요.");
    }

    public void LeaveRoom()
    {
        InRoom = false;
        CurrentRoom = null;
        players.Clear();
        RoomChanged?.Invoke();
    }

    public void SetReady(bool ready)
    {
    }

    public void SetTeam(int team)
    {
        RoomPlayer me = players.Find(p => p.isLocal);
        if (me == null || me.team == team)
            return;
        if (TeamCount(team) >= MatchContext.TeamSize)
        {
            Error?.Invoke("그 팀은 꽉 찼어요!");
            return;
        }
        me.team = team;
        RoomChanged?.Invoke();
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

    /// <summary>에디터 테스트용: 준비된 봇 추가</summary>
    public void AddTestBot()
    {
        if (!InRoom || players.Count >= CurrentRoom.maxPlayers)
            return;
        // 인원이 적은 쪽으로
        int team = TeamCount(RoomPlayer.TeamWinder) < TeamCount(RoomPlayer.TeamTickToy) ? RoomPlayer.TeamWinder : RoomPlayer.TeamTickToy;
        if (TeamCount(team) >= MatchContext.TeamSize)
            team = 1 - team;
        players.Add(new RoomPlayer { id = nextBotId++, nickname = "봇" + (players.Count), ready = true, team = team });
        RoomChanged?.Invoke();
    }

    public void StartGame()
    {
        if (!InRoom)
            return;
        if (TeamCount(RoomPlayer.TeamTickToy) < 1 || TeamCount(RoomPlayer.TeamWinder) < 1)
        {
            Error?.Invoke("틱토이와 와인더가 한 명씩은 있어야 해요.");
            return;
        }

        MatchContext.BeginMatch(CurrentRoom, players, LocalPlayerId);
        GameLoading?.Invoke();
        MapEntry map = MapCatalog.Load()?.Get(CurrentRoom.mapIndex);
        if (map != null)
            SceneManager.LoadSceneAsync(map.sceneName);
    }

    public void LoadMap(string sceneName)
    {
        SceneManager.LoadSceneAsync(sceneName);
    }

    public void ReturnToRoom()
    {
        MatchContext.End();
        SceneManager.LoadSceneAsync(MatchService.LobbyScene);
    }
}
