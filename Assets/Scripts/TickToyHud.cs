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

    void Awake()
    {
        ApplyFont();
        RefreshStatus();

        if (windRoot != null)
        {
            windRoot.SetActive(false);
            Transform label = windRoot.transform.Find("WindText");
            if (label != null)
                windLabel = label.GetComponent<Text>();
        }

        if (abilityIconRoot != null)
            abilityIconRoot.SetActive(false);
    }

    void Update()
    {
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
        hpText.text = "HP : " + hp;
    }

    void ApplyFont()
    {
        Font font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕" }, 32);
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Text[] texts = GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
            texts[i].font = font;
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

        if (abilityIconRoot != null)
            abilityIconRoot.SetActive(false);

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

    void ConsumeAbility()
    {
        abilityLevel = 0;
        abilityKind = AbilityKind.None;
        if (abilityIconRoot != null)
            abilityIconRoot.SetActive(false);
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
        abilityIcon.sprite = sprite;
        abilityIcon.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        abilityFallbackText.text = abilityKind == AbilityKind.Faint ? "기절하기" : "달리기";
        abilityFallbackText.gameObject.SetActive(sprite == null);

        if (levelText != null)
            levelText.text = "Lv." + abilityLevel;
    }

    void ApplyAbilityToToys()
    {
        int kind = abilityKind == AbilityKind.Faint ? 2 : 1;
        TickToyMove[] toys = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < toys.Length; i++)
            toys[i].GrantAbility(kind, abilityLevel);
    }
}
