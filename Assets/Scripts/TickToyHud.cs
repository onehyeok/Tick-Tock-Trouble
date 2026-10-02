using UnityEngine;
using UnityEngine.UI;

public class TickToyHud : MonoBehaviour
{
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

    private float windProgress;
    private bool hasRunAbility;

    void Awake()
    {
        ApplyFont();
        RefreshStatus();

        if (windRoot != null)
            windRoot.SetActive(false);

        if (abilityIconRoot != null)
            abilityIconRoot.SetActive(false);
    }

    void Update()
    {
        if (hasRunAbility && Input.GetKeyDown(KeyCode.R))
            ActivateRunAbility();

        bool winding = IsAnyTickToyWinding();

        if (!winding || hasRunAbility)
        {
            windProgress = 0f;
            if (windRoot != null)
                windRoot.SetActive(false);
            return;
        }

        windRoot.SetActive(true);
        windProgress += Time.deltaTime / Mathf.Max(0.1f, windDuration);
        windFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(windProgress), 1f);

        if (windProgress >= 1f)
            GrantRunAbility();
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

    void GrantRunAbility()
    {
        hasRunAbility = true;
        windProgress = 0f;
        windRoot.SetActive(false);
        abilityIconRoot.SetActive(true);
        abilityIcon.sprite = runAbilityIcon;
        abilityIcon.color = runAbilityIcon != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        abilityFallbackText.gameObject.SetActive(runAbilityIcon == null);
    }

    void ActivateRunAbility()
    {
        TickToyMove[] toys = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < toys.Length; i++)
            toys[i].UseRunAbility();
    }
}
