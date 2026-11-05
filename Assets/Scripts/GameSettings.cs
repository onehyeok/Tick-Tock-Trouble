using UnityEngine;

/// <summary>틱토이·와인더 공통 설정. PlayerPrefs에 저장.</summary>
public static class GameSettings
{
    const string KeyHorizontal = "Settings.MouseSensHorizontal";
    const string KeyVertical = "Settings.MouseSensVertical";

    public const float DefaultSensitivity = 50f;
    public const float MinSensitivity = 1f;
    public const float MaxSensitivity = 100f;

    public static float HorizontalSensitivity { get; private set; } = DefaultSensitivity;
    public static float VerticalSensitivity { get; private set; } = DefaultSensitivity;
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
