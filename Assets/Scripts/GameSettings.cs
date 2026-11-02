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
    public static bool MenuOpen { get; set; }

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
