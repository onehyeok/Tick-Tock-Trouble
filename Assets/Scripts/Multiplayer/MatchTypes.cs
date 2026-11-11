using System;
using System.Collections.Generic;
using UnityEngine;

// 멀티플레이 공용 데이터 (네트워크 방식과 무관)

[Serializable]
public class MapEntry
{
    public string displayName = "CITY";
    public string sceneName = "DemoScene";
    public Sprite preview;
    public bool available = true;
}

[Serializable]
public class RoomSettings
{
    public string roomName = "";
    public int mapIndex;
    public int maxPlayers = MatchContext.TeamSize * 2;
    public string password = "";
    public int rounds = 3;

    public bool HasPassword => !string.IsNullOrEmpty(password);
}

/// <summary>로비 방 목록 한 줄</summary>
public class RoomInfo
{
    public string name;
    public int mapIndex;
    public int players;
    public int maxPlayers;
    public bool locked;
    public int rounds;
    public bool inGame;
    public List<string> nicknames = new List<string>();

    public bool IsJoinable => players < maxPlayers; // 게임 중이어도 자리가 있으면 들어감 (그 라운드는 관전)
}

/// <summary>방 안 플레이어</summary>
public class RoomPlayer
{
    public const int TeamTickToy = 0;
    public const int TeamWinder = 1;

    public int id;
    /// <summary>0 = 틱토이(왼쪽), 1 = 와인더(오른쪽)</summary>
    public int team;
    public string nickname;
    public bool ready;
    public bool isHost;
    public bool isLocal;
}

public static class PlayerProfile
{
    const string Key = "ttt_nickname";

    public static string Nickname
    {
        get => PlayerPrefs.GetString(Key, "");
        set
        {
            PlayerPrefs.SetString(Key, value ?? "");
            PlayerPrefs.Save();
        }
    }
}

/// <summary>게임(라운드) 진행 정보 — 씬이 바뀌어도 유지</summary>
public static class MatchContext
{
    public const float HideSeconds = 20f;
    public const float HuntSeconds = 180f;
    public const float ResultSeconds = 5f;
    /// <summary>팀당 최대 인원 (8 vs 8)</summary>
    public const int TeamSize = 8;

    public static bool Active;
    public static RoomSettings Settings = new RoomSettings();
    public static readonly List<RoomPlayer> Players = new List<RoomPlayer>();
    public static readonly List<int> WinderIds = new List<int>();
    public static int LocalId;
    public static int Round = 1;
    public static int WinderWins;
    public static int TickToyWins;
    /// <summary>모든 라운드가 끝나 로비 씬으로 돌아갈 때 방 화면을 바로 보여줌</summary>
    public static bool ReturnToRoom;

    public static bool LocalIsWinder => WinderIds.Contains(LocalId);
    public static int TickToyCount => Mathf.Max(0, Players.Count - WinderIds.Count);

    public static void BeginMatch(RoomSettings settings, IReadOnlyList<RoomPlayer> players, int localId)
    {
        Active = true;
        Settings = settings;
        Players.Clear();
        Players.AddRange(players);
        LocalId = localId;
        Round = 1;
        WinderWins = 0;
        TickToyWins = 0;
        ReturnToRoom = false;
        AssignRolesFromTeams();
    }

    /// <summary>방에서 고른 팀 그대로: 오른쪽 = 와인더, 왼쪽 = 틱토이 (무작위 아님)</summary>
    public static void AssignRolesFromTeams()
    {
        WinderIds.Clear();
        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i].team == RoomPlayer.TeamWinder)
                WinderIds.Add(Players[i].id);
        }
    }

    public static void End()
    {
        Active = false;
        ReturnToRoom = true;
    }
}
