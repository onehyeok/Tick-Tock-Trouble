using UnityEngine;
using UnityEngine.UI;

public class TickToyHud : MonoBehaviour
{
    enum AbilityKind
    {
        None,
        Run,
        Faint
    }

    [Header("Status")]
    [SerializeField] private int tickToyCount;
    [SerializeField] private int winderCount;
    [SerializeField] private int roundCount = 1;
    [SerializeField] private float remainSeconds;
    [SerializeField] private int hp = 100;

    [Header("Wind")]
    [SerializeField] private float windDuration = 7f;

    [Header("Ability")]
    [SerializeField] private Sprite runAbilityIcon;
    [SerializeField] private Sprite faintAbilityIcon;
    [SerializeField] private Sprite zeroAbilityIcon;

    [Header("UI")]
    [SerializeField] private Text countText;
    [SerializeField] private Text roundText;
    [SerializeField] private Text timeText;
    [SerializeField] private Text hpText;
    [SerializeField] private GameObject windRoot;
    [SerializeField] private Image windFill;
    [SerializeField] private GameObject abilityIconRoot;
    [SerializeField] private Image abilityIcon;
    [SerializeField] private Text abilityFallbackText;
    [SerializeField] private Text levelText;

    private float windProgress;
    private AbilityKind abilityKind;
    private int abilityLevel;
    private Text windLabel;
    private Text stopPrompt;
    private Font displayFont;

    void Awake()
    {
        TickToyMove.KeepOneAudioListener();
        EnsureStopPrompt();
        ApplyFont();
        RefreshStatus();

        if (windRoot != null)
        {
            windRoot.SetActive(false);
            Transform label = windRoot.transform.Find("WindText");
            if (label != null)
                windLabel = label.GetComponent<Text>();
        }

        ShowEmptyAbility();
    }

    void Update()
    {
        RefreshStopPrompt();
        if (stopPrompt != null && stopPrompt.gameObject.activeSelf && Input.GetKeyDown(KeyCode.E))
            GrantOrLevelFromStop();

        if (abilityLevel > 0 && PressedAbilityKey())
            ConsumeAbility();

        bool winding = abilityLevel < 3 && IsAnyTickToyWinding();
        if (winding)
        {
            ShowWindGauge();
            return;
        }

        windProgress = 0f;

        if (ShowUsingGauge())
            return;

        HideGauge();
    }

    void ShowWindGauge()
    {
        if (windRoot == null || windFill == null)
            return;

        windRoot.SetActive(true);
        if (windLabel != null)
        {
            windLabel.gameObject.SetActive(true);
            windLabel.text = "태엽을 감는중입니다.";
        }

        windProgress += Time.deltaTime / Mathf.Max(0.1f, windDuration);
        windFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(windProgress), 1f);

        if (windProgress >= 1f)
            GrantOrLevelAbility();
    }

    void HideGauge()
    {
        if (windRoot != null)
            windRoot.SetActive(false);
    }

    public void SetStatus(int nextTickToyCount, int nextWinderCount, int nextRoundCount, float nextRemainSeconds, int nextHp)
    {
        tickToyCount = nextTickToyCount;
        winderCount = nextWinderCount;
        roundCount = nextRoundCount;
        remainSeconds = nextRemainSeconds;
        hp = nextHp;
        RefreshStatus();
    }

    void RefreshStatus()
    {
        if (countText == null)
            return;

        int seconds = Mathf.Max(0, Mathf.CeilToInt(remainSeconds));
        countText.text = "TickToy 남은 수 " + tickToyCount + "  |  Winder 남은 수 " + winderCount;
        roundText.text = "라운드 " + roundCount;
        timeText.text = "남은 시간 " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        hpText.text = hp.ToString();
    }

    void ApplyFont()
    {
        Font bodyFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕" }, 32);
        if (bodyFont == null)
            bodyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        displayFont = Font.CreateDynamicFontFromOSFont(new[] { "Arial Rounded MT Bold", "Arial Rounded MT", "Trebuchet MS" }, 48);
        if (displayFont == null)
            displayFont = bodyFont;

        Text[] texts = GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] == levelText || texts[i] == hpText)
                continue;

            texts[i].font = bodyFont;
        }

        ApplyDisplayFont(levelText, 50);
        if (hpText != null)
            ApplyDisplayFont(hpText, hpText.fontSize);
        if (hpText != null)
            hpText.color = new Color(0.45f, 0.22f, 0.08f, 1f);
    }

    void ApplyDisplayFont(Text text, int size)
    {
        if (text == null)
            return;

        text.font = displayFont;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;

        Shadow shadow = text.GetComponent<Shadow>();
        if (shadow == null)
            shadow = text.gameObject.AddComponent<Shadow>();

        shadow.effectColor = new Color(0.18f, 0.08f, 0.02f, 0.55f);
        shadow.effectDistance = new Vector2(2f, -2f);
    }

    bool ShowUsingGauge()
    {
        TickToyMove usingToy = null;
        TickToyMove[] toys = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < toys.Length; i++)
        {
            if (toys[i].IsUsingAbility)
                usingToy = toys[i];
        }

        if (usingToy == null || windRoot == null || windFill == null)
            return false;

        if (usingToy.AbilityDuration <= 0f)
        {
            HideGauge();
            return true;
        }

        windRoot.SetActive(true);
        if (windLabel != null)
            windLabel.gameObject.SetActive(false);

        float amount = Mathf.Clamp01(usingToy.AbilityTimeLeft / usingToy.AbilityDuration);
        windFill.rectTransform.anchorMax = new Vector2(amount, 1f);
        return true;
    }

    bool IsAnyTickToyWinding()
    {
        TickToyMove[] toys = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < toys.Length; i++)
        {
            if (toys[i].IsWinding)
                return true;
        }

        return false;
    }

    public void GrantOrLevelFromStop()
    {
        if (abilityLevel >= 3)
            return;

        if (abilityLevel <= 0)
        {
            abilityKind = Random.Range(0, 2) == 0 ? AbilityKind.Run : AbilityKind.Faint;
            abilityLevel = 1;
        }
        else
            abilityLevel++;

        windProgress = 0f;
        if (windRoot != null)
            windRoot.SetActive(false);
        ShowAbility();
        ApplyAbilityToToys();
    }

    public void PresentAbility(int kind, int level)
    {
        abilityKind = kind == 2 ? AbilityKind.Faint : AbilityKind.Run;
        abilityLevel = Mathf.Clamp(level, 1, 3);
        ShowAbility();
    }

    public void ClearPresentedAbility()
    {
        abilityLevel = 0;
        abilityKind = AbilityKind.None;
        ShowEmptyAbility();
    }

    void ConsumeAbility()
    {
        ClearPresentedAbility();
    }

    void EnsureStopPrompt()
    {
        GameObject promptObject = new GameObject("StopWinderPrompt", typeof(RectTransform), typeof(Text));
        promptObject.transform.SetParent(transform, false);

        RectTransform rect = promptObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.62f);
        rect.anchorMax = new Vector2(0.5f, 0.62f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1100f, 80f);

        stopPrompt = promptObject.GetComponent<Text>();
        stopPrompt.alignment = TextAnchor.MiddleCenter;
        stopPrompt.fontSize = 36;
        stopPrompt.color = Color.white;
        stopPrompt.horizontalOverflow = HorizontalWrapMode.Overflow;
        stopPrompt.verticalOverflow = VerticalWrapMode.Overflow;
        stopPrompt.text = "E를 눌러 와인더를 정지시키세요!";
        promptObject.SetActive(false);
    }

    void RefreshStopPrompt()
    {
        if (stopPrompt == null)
            return;

        stopPrompt.gameObject.SetActive(IsAnyTickToyNearWinder());
    }

    bool IsAnyTickToyNearWinder()
    {
        TickToyMove[] toys = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < toys.Length; i++)
        {
            if (!toys[i].IsDead && toys[i].FindWinderInReach() != null)
                return true;
        }

        return false;
    }

    void ShowEmptyAbility()
    {
        if (abilityIconRoot != null)
            abilityIconRoot.SetActive(true);

        if (abilityIcon != null)
        {
            abilityIcon.sprite = zeroAbilityIcon;
            abilityIcon.preserveAspect = true;
            abilityIcon.color = zeroAbilityIcon != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        if (levelText != null)
            levelText.gameObject.SetActive(false);
    }

    bool PressedAbilityKey()
    {
        if (Input.GetKeyDown(KeyCode.R))
            return true;

        return UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame;
    }

    void GrantOrLevelAbility()
    {
        if (abilityLevel <= 0)
        {
            abilityKind = Random.Range(0, 2) == 0 ? AbilityKind.Run : AbilityKind.Faint;
            abilityLevel = 1;
        }
        else
            abilityLevel = Mathf.Min(abilityLevel + 1, 3);
        windProgress = 0f;
        windRoot.SetActive(false);
        ShowAbility();
        ApplyAbilityToToys();
    }

    void ShowAbility()
    {
        Sprite sprite = abilityKind == AbilityKind.Faint ? faintAbilityIcon : runAbilityIcon;
        abilityIconRoot.SetActive(true);
        if (abilityIcon != null)
        {
            abilityIcon.sprite = sprite;
            abilityIcon.preserveAspect = true;
            abilityIcon.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        if (abilityFallbackText != null)
        {
            abilityFallbackText.text = abilityKind == AbilityKind.Faint ? "기절하기" : "달리기";
            abilityFallbackText.gameObject.SetActive(sprite == null);
        }

        if (levelText != null)
        {
            levelText.gameObject.SetActive(true);
            levelText.text = "Lv " + abilityLevel;
            if (abilityLevel <= 1)
                levelText.color = Color.white;
            else if (abilityLevel == 2)
                levelText.color = new Color(0.25f, 0.58f, 1f, 1f);
            else
                levelText.color = new Color(0.96f, 0.18f, 0.16f, 1f);
        }
    }

    void ApplyAbilityToToys()
    {
        int kind = abilityKind == AbilityKind.Faint ? 2 : 1;
        TickToyMove[] toys = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < toys.Length; i++)
            toys[i].GrantAbility(kind, abilityLevel);
    }
}
