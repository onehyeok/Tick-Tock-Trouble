using UnityEngine;
using UnityEngine.UI;

public class TickToyHud : MonoBehaviour
{
    enum AbilityKind
    {
        None,
        Run,
        Faint,
        Duplicate,
        OneHit
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
    [SerializeField] private Sprite duplicateAbilityIcon;
    [SerializeField] private Sprite oneHitAbilityIcon;
    [SerializeField] private Sprite zeroAbilityIcon;

    [Header("UI")]
    [SerializeField] private Text countText;
    [SerializeField] private Text roundText;
    [SerializeField] private Text timeText;
    [SerializeField] private Text hpText;
    [SerializeField] private GameObject windRoot;
    [SerializeField] private Image windFill;
    [SerializeField] private GameObject abilityTimerRoot;
    [SerializeField] private Image abilityTimerFill;
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
    private Image damageFlashImage;
    private Text damagePopText;
    private float damageFlashLeft;
    private float damagePopLeft;
    private Vector2 damagePopBasePos;
    private Vector3 hpTextBaseScale = Vector3.one;
    private Color hpTextBaseColor = Color.white;

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
        EnsureDamageFlashUi();
        HideAbilityTimer();
        if (hpText != null)
        {
            hpTextBaseScale = hpText.rectTransform.localScale;
            hpTextBaseColor = hpText.color;
        }
    }

    void Update()
    {
        UpdateAbilityTestKeys();
        RefreshStopPrompt();
        TickDamageFlashUi();
        if (stopPrompt != null && stopPrompt.gameObject.activeSelf && Input.GetKeyDown(KeyCode.E))
            GrantOrLevelFromStop();

        if (abilityLevel > 0 && PressedAbilityKey())
            ConsumeAbility();

        bool winding = abilityLevel < 3 && IsAnyTickToyWinding();
        bool showingAbilityTimer = ShowUsingGauge();

        if (winding)
        {
            // 달리기/드러눕기 타이머는 유지한 채 태엽 바 표시
            ShowWindGauge();
            return;
        }

        windProgress = 0f;
        if (windRoot != null)
            windRoot.SetActive(false);

        if (showingAbilityTimer)
            return;

        HideAbilityTimer();
    }

    /// <summary>브릭(와인더)이 AI 때려서 자해 피해 입을 때 — 귀여운 화면/HP 연출.</summary>
    public void PlayWinderDamageFlash(int damageAmount = 25)
    {
        EnsureDamageFlashUi();
        damageFlashLeft = 0.55f;
        damagePopLeft = 0.85f;

        if (damageFlashImage != null)
        {
            damageFlashImage.gameObject.SetActive(true);
            Color c = new Color(1f, 0.35f, 0.55f, 0.45f); // 파스텔 핑크-레드
            damageFlashImage.color = c;
        }

        if (damagePopText != null)
        {
            damagePopText.gameObject.SetActive(true);
            damagePopText.text = "-" + Mathf.Abs(damageAmount);
            damagePopText.color = new Color(1f, 0.25f, 0.45f, 1f);
            damagePopText.rectTransform.anchoredPosition = damagePopBasePos;
            damagePopText.rectTransform.localScale = Vector3.one * 1.15f;
        }

        if (hpText != null)
        {
            hpText.color = new Color(1f, 0.2f, 0.4f, 1f);
            hpText.rectTransform.localScale = hpTextBaseScale * 1.35f;
        }
    }

    void EnsureDamageFlashUi()
    {
        if (damageFlashImage != null)
            return;

        // 화면 가장자리 비네트용 풀스크린 이미지
        GameObject flashGo = new GameObject("DamageFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        flashGo.transform.SetParent(transform, false);
        RectTransform flashRt = flashGo.GetComponent<RectTransform>();
        flashRt.anchorMin = Vector2.zero;
        flashRt.anchorMax = Vector2.one;
        flashRt.offsetMin = Vector2.zero;
        flashRt.offsetMax = Vector2.zero;
        damageFlashImage = flashGo.GetComponent<Image>();
        damageFlashImage.raycastTarget = false;
        damageFlashImage.color = new Color(1f, 0.4f, 0.55f, 0f);
        // 단색 스프라이트
        Texture2D tex = Texture2D.whiteTexture;
        damageFlashImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        flashGo.SetActive(false);

        GameObject popGo = new GameObject("DamagePop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        popGo.transform.SetParent(transform, false);
        RectTransform popRt = popGo.GetComponent<RectTransform>();
        if (hpText != null)
        {
            popRt.anchorMin = hpText.rectTransform.anchorMin;
            popRt.anchorMax = hpText.rectTransform.anchorMax;
            popRt.pivot = hpText.rectTransform.pivot;
            popRt.anchoredPosition = hpText.rectTransform.anchoredPosition + new Vector2(0f, 48f);
            popRt.sizeDelta = new Vector2(220f, 80f);
        }
        else
        {
            popRt.anchorMin = new Vector2(0.5f, 0.75f);
            popRt.anchorMax = new Vector2(0.5f, 0.75f);
            popRt.sizeDelta = new Vector2(220f, 80f);
        }

        damagePopText = popGo.GetComponent<Text>();
        damagePopText.alignment = TextAnchor.MiddleCenter;
        damagePopText.fontSize = 42;
        damagePopText.fontStyle = FontStyle.Bold;
        damagePopText.raycastTarget = false;
        damagePopText.horizontalOverflow = HorizontalWrapMode.Overflow;
        damagePopText.verticalOverflow = VerticalWrapMode.Overflow;
        if (displayFont != null)
            damagePopText.font = displayFont;
        else
            ApplyDisplayFont(damagePopText, 42);
        damagePopBasePos = popRt.anchoredPosition;
        popGo.SetActive(false);

        // 맨 위에 보이게
        flashGo.transform.SetAsLastSibling();
        popGo.transform.SetAsLastSibling();
    }

    void TickDamageFlashUi()
    {
        if (damageFlashLeft > 0f)
        {
            damageFlashLeft -= Time.deltaTime;
            float u = Mathf.Clamp01(damageFlashLeft / 0.55f);
            if (damageFlashImage != null)
            {
                // 가장자리만 느껴지게 알파 곡선
                float a = 0.5f * u * u;
                Color c = damageFlashImage.color;
                c.a = a;
                // 중앙은 비우고 테두리감: 전체 페이드로 귀엽게
                damageFlashImage.color = c;
                damageFlashImage.gameObject.SetActive(u > 0.01f);
            }
        }
        else if (damageFlashImage != null && damageFlashImage.gameObject.activeSelf)
        {
            damageFlashImage.gameObject.SetActive(false);
        }

        if (damagePopLeft > 0f)
        {
            damagePopLeft -= Time.deltaTime;
            float u = Mathf.Clamp01(damagePopLeft / 0.85f);
            if (damagePopText != null)
            {
                Color c = damagePopText.color;
                c.a = u;
                damagePopText.color = c;
                float rise = (1f - u) * 36f;
                damagePopText.rectTransform.anchoredPosition = damagePopBasePos + Vector2.up * rise;
                damagePopText.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.25f, u);
                damagePopText.gameObject.SetActive(u > 0.02f);
            }

            if (hpText != null)
            {
                float punch = Mathf.Clamp01(damagePopLeft / 0.35f);
                hpText.rectTransform.localScale = Vector3.Lerp(hpTextBaseScale, hpTextBaseScale * 1.35f, punch);
                hpText.color = Color.Lerp(hpTextBaseColor, new Color(1f, 0.2f, 0.4f, 1f), punch);
            }
        }
        else
        {
            if (damagePopText != null && damagePopText.gameObject.activeSelf)
                damagePopText.gameObject.SetActive(false);
            if (hpText != null)
            {
                hpText.rectTransform.localScale = hpTextBaseScale;
                hpText.color = hpTextBaseColor;
            }
        }
    }

    void UpdateAbilityTestKeys()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            GrantTestAbility(AbilityKind.Run);
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            GrantTestAbility(AbilityKind.Faint);
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            GrantTestAbility(AbilityKind.Duplicate);
    }

    void GrantTestAbility(AbilityKind kind)
    {
        abilityKind = kind;
        abilityLevel = 1;
        windProgress = 0f;
        HideGauge();
        ShowAbility();
        ApplyAbilityToToys();
    }

    void ShowWindGauge()
    {
        if (windRoot == null || windFill == null)
            return;

        windRoot.SetActive(true);
        if (windLabel != null)
        {
            windLabel.gameObject.SetActive(true);
            windLabel.text = "태엽을 감는중...";
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
        HideAbilityTimer();
    }

    void HideAbilityTimer()
    {
        if (abilityTimerRoot != null)
            abilityTimerRoot.SetActive(false);
    }

    public void HideAll()
    {
        if (stopPrompt != null)
            stopPrompt.gameObject.SetActive(false);
        gameObject.SetActive(false);
    }

    public void SetHp(int nextHp)
    {
        hp = Mathf.Max(0, nextHp);
        if (hpText != null)
            hpText.text = hp.ToString();
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
        TickToyMove faintToy = null;
        bool faintCancelled = false;
        TickToyMove[] toys = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < toys.Length; i++)
        {
            if (toys[i].FaintCancelled)
                faintCancelled = true;

            // 달리기 / 드러눕기만 타이머 UI. 분신술은 스크롤바·타이머 없음.
            if (toys[i].IsFainted)
                faintToy = toys[i];
            else if (toys[i].State == TickToyMove.TickToyState.Run)
                usingToy = toys[i];
        }

        if (usingToy == null && faintToy != null && !faintCancelled)
            usingToy = faintToy;

        if (usingToy == null)
            return false;

        if (usingToy.AbilityDuration <= 0f)
        {
            HideAbilityTimer();
            return true;
        }

        float duration = usingToy.AbilityDuration;
        float amount = duration > 0.0001f
            ? Mathf.Clamp01(usingToy.AbilityTimeLeft / duration)
            : 0f;
        ShowAbilityTimer(amount);
        return true;
    }

    void ShowAbilityTimer(float remaining01)
    {
        if (abilityTimerRoot == null || abilityTimerFill == null)
            return;

        abilityTimerRoot.SetActive(true);
        // 남은 시간만큼만 보이고, 지나간 구간은 시계방향으로 사라짐
        abilityTimerFill.fillAmount = Mathf.Clamp01(remaining01);
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
            abilityKind = (AbilityKind)Random.Range(1, 4);
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
        if (kind == 2)
            abilityKind = AbilityKind.Faint;
        else if (kind == 3)
            abilityKind = AbilityKind.Duplicate;
        else if (kind == 4)
            abilityKind = AbilityKind.OneHit;
        else
            abilityKind = AbilityKind.Run;
        // 황금 몽둥이는 레벨 없음
        abilityLevel = abilityKind == AbilityKind.OneHit ? 1 : Mathf.Clamp(level, 1, 3);
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
            abilityKind = (AbilityKind)Random.Range(1, 4);
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
        Sprite sprite = runAbilityIcon;
        if (abilityKind == AbilityKind.Faint)
            sprite = faintAbilityIcon;
        else if (abilityKind == AbilityKind.Duplicate)
            sprite = duplicateAbilityIcon;
        else if (abilityKind == AbilityKind.OneHit)
            sprite = oneHitAbilityIcon;

        if (abilityIconRoot != null)
            abilityIconRoot.SetActive(true);
        if (abilityIcon != null)
        {
            abilityIcon.sprite = sprite;
            abilityIcon.preserveAspect = true;
            abilityIcon.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        if (abilityFallbackText != null)
        {
            if (abilityKind == AbilityKind.Faint)
                abilityFallbackText.text = "기절하기";
            else if (abilityKind == AbilityKind.Duplicate)
                abilityFallbackText.text = "분신술";
            else if (abilityKind == AbilityKind.OneHit)
                abilityFallbackText.text = "황금 몽둥이";
            else
                abilityFallbackText.text = "달리기";
            abilityFallbackText.gameObject.SetActive(sprite == null);
        }

        if (levelText != null)
        {
            // Brick 황금 몽둥이: 레벨 표시 없음
            if (abilityKind == AbilityKind.OneHit)
            {
                levelText.gameObject.SetActive(false);
            }
            else
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
    }

    void ApplyAbilityToToys()
    {
        int kind = 1;
        if (abilityKind == AbilityKind.Faint)
            kind = 2;
        else if (abilityKind == AbilityKind.Duplicate)
            kind = 3;
        TickToyMove[] toys = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < toys.Length; i++)
            toys[i].GrantAbility(kind, abilityLevel);
    }
}
