using UnityEngine;

/// <summary>틱토이·와인더 공통 설정. PlayerPrefs에 저장.</summary>
public static class GameSettings
{
    const string KeyHorizontal = "Settings.MouseSensHorizontal";
    const string KeyVertical = "Settings.MouseSensVertical";
    const string KeyLobbyVolume = "Settings.LobbyVolume";
    const string KeyGameVolume = "Settings.GameVolume";
    public const float DefaultVolume = 100f; // 0~100

    public const float DefaultSensitivity = 50f;
    public const float MinSensitivity = 1f;
    public const float MaxSensitivity = 100f;

    public static float HorizontalSensitivity { get; private set; } = DefaultSensitivity;
    public static float VerticalSensitivity { get; private set; } = DefaultSensitivity;
    /// <summary>로비(인트로·로비) 노래 크기 0~100</summary>
    public static float LobbyVolume { get; private set; } = DefaultVolume;
    /// <summary>게임 맵 노래 + 효과음 크기 0~100</summary>
    public static float GameVolume { get; private set; } = DefaultVolume;
    /// <summary>소리 크기가 바뀌면 (배경음악이 바로 반영)</summary>
    public static event System.Action VolumeChanged;
    static bool menuOpen;
    /// <summary>설정 메뉴가 열렸거나 채팅 입력 중이면 true → 캐릭터 조작 멈춤</summary>
    public static bool MenuOpen
    {
        get => menuOpen || ChatTyping;
        set => menuOpen = value;
    }
    /// <summary>게임 중 채팅 입력칸이 열려 있음</summary>
    public static bool ChatTyping { get; set; }

    static bool loaded;

    public static void EnsureLoaded()
    {
        if (loaded)
            return;
        loaded = true;
        HorizontalSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(KeyHorizontal, DefaultSensitivity), MinSensitivity, MaxSensitivity);
        VerticalSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(KeyVertical, DefaultSensitivity), MinSensitivity, MaxSensitivity);
        LobbyVolume = Mathf.Clamp(PlayerPrefs.GetFloat(KeyLobbyVolume, DefaultVolume), 0f, 100f);
        GameVolume = Mathf.Clamp(PlayerPrefs.GetFloat(KeyGameVolume, DefaultVolume), 0f, 100f);
    }

    public static void SetLobbyVolume(float value)
    {
        EnsureLoaded();
        LobbyVolume = Mathf.Clamp(value, 0f, 100f);
        PlayerPrefs.SetFloat(KeyLobbyVolume, LobbyVolume);
        PlayerPrefs.Save();
        VolumeChanged?.Invoke();
    }

    public static void SetGameVolume(float value)
    {
        EnsureLoaded();
        GameVolume = Mathf.Clamp(value, 0f, 100f);
        PlayerPrefs.SetFloat(KeyGameVolume, GameVolume);
        PlayerPrefs.Save();
        VolumeChanged?.Invoke();
    }

    public static void SetHorizontalSensitivity(float value)
    {
        EnsureLoaded();
        HorizontalSensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
        PlayerPrefs.SetFloat(KeyHorizontal, HorizontalSensitivity);
        PlayerPrefs.Save();
    }

    public static void SetVerticalSensitivity(float value)
    {
        EnsureLoaded();
        VerticalSensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
        PlayerPrefs.SetFloat(KeyVertical, VerticalSensitivity);
        PlayerPrefs.Save();
    }
}
