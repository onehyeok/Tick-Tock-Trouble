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
        OneHit,
        Trap
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
    [SerializeField] private Sprite trapAbilityIcon;
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
    [SerializeField] private Image abilityTimerBase;
    [SerializeField] private Sprite timerIconSprite;
    [SerializeField] private GameObject abilityIconRoot;
    [SerializeField] private Image abilityIcon;
    [SerializeField] private Text abilityFallbackText;
    [SerializeField] private Text levelText;
    [SerializeField] private Text disguiseText; // "변장 x 5" (능력 칸 아래)

    private float windProgress;
    private bool windBlockedUntilRelease;
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
        displayFont = UIKit.Font; // 글꼴은 씬에 이미 적용돼 있음 (실행 중에 바꾸지 않음)
        EnsureStopPrompt();
        StyleStatusUi();
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
        EnsureAbilityTimerVisual();
        HideAbilityTimer();
        EnsureTrapAbilityIcon();
        if (hpText != null)
        {
            hpTextBaseScale = hpText.rectTransform.localScale;
            hpTextBaseColor = hpText.color;
        }
    }

    void EnsureAbilityTimerVisual()
    {
        Sprite timerSprite = LoadTimerIconSprite();
        if (abilityTimerRoot == null)
            return;

        // 아래: 밝은 Timer_Icon 전체 / 위: 같은 아이콘 어두운 Radial이 시계방향으로 덮음
        if (abilityTimerBase == null)
        {
            Transform existing = abilityTimerRoot.transform.Find("TimerBase");
            if (existing != null)
                abilityTimerBase = existing.GetComponent<Image>();
        }

        if (abilityTimerBase == null)
        {
            GameObject baseGo = new GameObject("TimerBase", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            baseGo.transform.SetParent(abilityTimerRoot.transform, false);
            RectTransform baseRt = baseGo.GetComponent<RectTransform>();
            baseRt.anchorMin = Vector2.zero;
            baseRt.anchorMax = Vector2.one;
            baseRt.offsetMin = Vector2.zero;
            baseRt.offsetMax = Vector2.zero;
            abilityTimerBase = baseGo.GetComponent<Image>();
            abilityTimerBase.raycastTarget = false;
            baseGo.transform.SetAsFirstSibling();
        }

        if (abilityTimerFill == null)
        {
            Transform fill = abilityTimerRoot.transform.Find("TimerFill");
            if (fill != null)
                abilityTimerFill = fill.GetComponent<Image>();
        }

        if (abilityTimerFill == null)
        {
            GameObject fillGo = new GameObject("TimerFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillGo.transform.SetParent(abilityTimerRoot.transform, false);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            abilityTimerFill = fillGo.GetComponent<Image>();
            abilityTimerFill.raycastTarget = false;
        }

        abilityTimerFill.transform.SetAsLastSibling();

        if (timerSprite != null)
        {
            abilityTimerBase.sprite = timerSprite;
            abilityTimerFill.sprite = timerSprite;
        }

        abilityTimerBase.type = Image.Type.Simple;
        abilityTimerBase.preserveAspect = true;
        abilityTimerBase.color = Color.white;

        // 경과 구간만 시계방향(12시 시작)으로 어둡게
        abilityTimerFill.type = Image.Type.Filled;
        abilityTimerFill.fillMethod = Image.FillMethod.Radial360;
        abilityTimerFill.fillOrigin = (int)Image.Origin360.Top;
        abilityTimerFill.fillClockwise = true;
        abilityTimerFill.preserveAspect = true;
        abilityTimerFill.color = new Color(0.22f, 0.12f, 0.08f, 0.82f);
        abilityTimerFill.fillAmount = 0f;
    }

    Sprite LoadTimerIconSprite()
    {
        if (timerIconSprite != null)
            return timerIconSprite;
#if UNITY_EDITOR
        timerIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/Timer_Icon.png");
#endif
        if (timerIconSprite == null)
            timerIconSprite = Resources.Load<Sprite>("Timer_Icon");
        return timerIconSprite;
    }

    void EnsureTrapAbilityIcon()
    {
        if (trapAbilityIcon != null)
            return;
#if UNITY_EDITOR
        trapAbilityIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/Images/Ability/Winder/Ability_Trap_Icon.png");
#endif
        if (trapAbilityIcon == null)
            trapAbilityIcon = Resources.Load<Sprite>("Ability/Ability_Trap_Icon");
    }

    void Update()
    {
        bool menuOpen = GameSettings.MenuOpen; // 설정 메뉴 / 채팅 입력 중엔 키 입력 무시
        if (!menuOpen)
            UpdateAbilityTestKeys();
        RefreshStopPrompt();
        TickDamageFlashUi();
        if (!menuOpen && stopPrompt != null && stopPrompt.gameObject.activeSelf && !stopTargetFrozen && Input.GetKeyDown(KeyCode.E))
            GrantOrLevelFromStop();

        if (!menuOpen && abilityLevel > 0 && PressedAbilityKey())
            ConsumeAbility();

        bool winding = abilityLevel < 3 && IsAnyTickToyWinding();
        // 문이 열리기 전: 태엽 감는 동작만 되고 바는 안 뜸.
        // 감는 중에 문이 열려도 바로 바가 뜨지 않음 → 좌클릭을 뗐다가 다시 눌러야 뜸
        if (winding && !RoundManager.DoorOpen)
            windBlockedUntilRelease = true;
        else if (!winding)
            windBlockedUntilRelease = false;
        if (windBlockedUntilRelease)
            winding = false;
        bool showingAbilityTimer = ShowUsingGauge();

        if (winding)
        {
            // 달리기/드러눕기 타이머는 유지한 채 태엽 바 표시 (시간이 끝났으면 시계는 숨김)
            if (!showingAbilityTimer)
                HideAbilityTimer();
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
        damagePopText.fontStyle = FontStyle.Normal;
        damagePopText.raycastTarget = false;
        damagePopText.horizontalOverflow = HorizontalWrapMode.Overflow;
        damagePopText.verticalOverflow = VerticalWrapMode.Overflow;
        damagePopText.font = displayFont;
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
        // 와인더가 조작 중이면 3은 Trap 전용(WinderMove)
        bool winderActive = IsAnyEnabledWinder();
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            GrantTestAbility(AbilityKind.Run);
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            GrantTestAbility(AbilityKind.Faint);
        else if (!winderActive && (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)))
            GrantTestAbility(AbilityKind.Duplicate);
    }

    static bool IsAnyEnabledWinder()
    {
        WinderMove[] winders = FindObjectsByType<WinderMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < winders.Length; i++)
        {
            if (winders[i] != null && winders[i].enabled)
                return true;
        }

        return false;
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

    /// <summary>남은 변장 횟수 표시 (음수 = 숨김, 와인더)</summary>
    public void SetDisguiseCount(int left)
    {
        if (disguiseText == null)
            return;
        disguiseText.gameObject.SetActive(left >= 0);
        if (left >= 0)
            disguiseText.text = "변장 x " + left;
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
        // 매 프레임 호출됨 → 화면에 보이는 값이 바뀔 때만 글자 갱신
        bool changed = tickToyCount != nextTickToyCount || winderCount != nextWinderCount || roundCount != nextRoundCount
                       || hp != nextHp || Mathf.CeilToInt(remainSeconds) != Mathf.CeilToInt(nextRemainSeconds);
        tickToyCount = nextTickToyCount;
        winderCount = nextWinderCount;
        roundCount = nextRoundCount;
        remainSeconds = nextRemainSeconds;
        hp = nextHp;
        if (changed)
            RefreshStatus();
    }

    void RefreshStatus()
    {
        if (countText == null)
            return;

        int seconds = Mathf.Max(0, Mathf.CeilToInt(remainSeconds));
        hpText.text = hp.ToString();

        if (statusStyled)
        {
            tickToyBadgeCount.text = tickToyCount.ToString();
            winderBadgeCount.text = winderCount.ToString();
            timerValueText.text = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            if (roundValueText != null)
            {
                int total = MatchContext.Active ? MatchContext.Settings.rounds : 0;
                roundValueText.text = total > 0 ? "ROUND " + roundCount + " / " + total : "ROUND " + roundCount;
            }
            return;
        }

        countText.text = "TickToy 남은 수 " + tickToyCount + "  |  Winder 남은 수 " + winderCount;
        roundText.text = "라운드 " + roundCount;
        timeText.text = "남은 시간 " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
    }

    // ---------------- 남은 수 · 남은 시간 (장난감 느낌 배지) ----------------

    bool statusStyled;
    // 씬에 있는 상단 UI (DemoScene / SampleScene 의 HUD 캔버스 아래 UpperBar · RoundBadge · TimerBadge)
    [SerializeField] Text tickToyBadgeCount;
    [SerializeField] Text winderBadgeCount;
    [SerializeField] Text timerValueText;
    [SerializeField] Text roundValueText;
    [SerializeField] RectTransform timerBadge;
    static readonly Color BadgeBrown = new Color(0.36f, 0.22f, 0.13f, 1f);
    static readonly Color BadgePink = new Color(1f, 0.52f, 0.62f, 1f);

    /// <summary>씬에 상단 UI 가 연결돼 있으면 그걸로 표시</summary>
    void StyleStatusUi()
    {
        statusStyled = tickToyBadgeCount != null && winderBadgeCount != null && timerValueText != null && timerBadge != null;
    }

    float timerPulse;

    // ---------------- "틱토이가 태엽을 정지시켰어요!" 알림 (씬에 있음) ----------------
    [SerializeField] RectTransform stopNotice;
    float stopNoticeTime = -1f;
    const float StopNoticeSeconds = 2.5f;

    /// <summary>틱토이가 와인더 등 뒤에서 E 로 태엽을 멈췄을 때</summary>
    public void ShowWinderStoppedNotice()
    {
        if (stopNotice == null)
            return;
        stopNotice.gameObject.SetActive(true);
        stopNoticeTime = 0f;
    }

    void TickStopNotice()
    {
        if (stopNotice == null || stopNoticeTime < 0f)
            return;
        stopNoticeTime += Time.unscaledDeltaTime;
        // 톡 튀어나왔다가 잠깐 뒤 사라짐
        float pop = Mathf.Clamp01(stopNoticeTime / 0.18f);
        float s = pop < 1f ? Mathf.Lerp(0.6f, 1.08f, pop) : Mathf.Lerp(1.08f, 1f, Mathf.Clamp01((stopNoticeTime - 0.18f) / 0.12f));
        stopNotice.localScale = new Vector3(s, s, 1f);
        if (stopNoticeTime >= StopNoticeSeconds)
        {
            stopNotice.gameObject.SetActive(false);
            stopNoticeTime = -1f;
        }
    }

    void LateUpdate()
    {
        TickStopNotice();
        if (!statusStyled || timerBadge == null)
            return;

        // 30초 이하: 숫자 빨갛게 + 톡톡 튀는 느낌
        bool urgent = remainSeconds > 0f && remainSeconds <= 30f;
        timerValueText.color = urgent ? BadgePink : BadgeBrown;
        if (urgent)
        {
            timerPulse += Time.unscaledDeltaTime * 6f;
            float s = 1f + Mathf.Abs(Mathf.Sin(timerPulse)) * 0.06f;
            timerBadge.localScale = new Vector3(s, s, 1f);
        }
        else if (timerBadge.localScale != Vector3.one)
            timerBadge.localScale = Vector3.one;
    }

    // 매 프레임 씬 전체 검색하지 않도록 0.5초마다만 다시 찾음
    TickToyMove[] cachedToys = System.Array.Empty<TickToyMove>();
    WinderMove[] cachedWinders = System.Array.Empty<WinderMove>();
    float charCacheTime = -1f;

    void RefreshCharacterCache()
    {
        if (Time.unscaledTime - charCacheTime < 0.5f && charCacheTime >= 0f)
            return;
        charCacheTime = Time.unscaledTime;
        cachedToys = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        cachedWinders = FindObjectsByType<WinderMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    }

    bool ShowUsingGauge()
    {
        TickToyMove usingToy = null;
        TickToyMove faintToy = null;
        bool faintCancelled = false;
        RefreshCharacterCache();
        TickToyMove[] toys = cachedToys;
        for (int i = 0; i < toys.Length; i++)
        {
            // 분신 복제본(비활성 Move)은 무시 — 본체만 달리기/드러눕기 UI
            if (toys[i] == null || !toys[i].enabled)
                continue;

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
            return ShowWinderRunGauge();

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
        EnsureAbilityTimerVisual();
        if (abilityTimerRoot == null || abilityTimerFill == null)
            return;

        abilityTimerRoot.SetActive(true);
        // 남은 비율 → 경과 비율. 시계방향으로 어두운 덮개가 늘어남
        float elapsed01 = 1f - Mathf.Clamp01(remaining01);
        abilityTimerFill.fillAmount = elapsed01;
    }

    /// <summary>와인더 달리기 능력 사용 중이면 틱토이처럼 시계 타이머 표시</summary>
    bool ShowWinderRunGauge()
    {
        for (int i = 0; i < cachedWinders.Length; i++)
        {
            WinderMove w = cachedWinders[i];
            if (w == null || !w.enabled || w.RunTimeLeft <= 0f || w.RunDuration <= 0.0001f)
                continue;
            ShowAbilityTimer(Mathf.Clamp01(w.RunTimeLeft / w.RunDuration));
            return true;
        }
        return false;
    }

    bool IsAnyTickToyWinding()
    {
        RefreshCharacterCache();
        TickToyMove[] toys = cachedToys;
        for (int i = 0; i < toys.Length; i++)
        {
            if (toys[i] != null && toys[i].enabled && toys[i].IsWinding)
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
        else if (kind == 5)
            abilityKind = AbilityKind.Trap;
        else
            abilityKind = AbilityKind.Run;
        // 황금 몽둥이 / 덫은 레벨 없음
        abilityLevel = (abilityKind == AbilityKind.OneHit || abilityKind == AbilityKind.Trap)
            ? 1
            : Mathf.Clamp(level, 1, 3);
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
        stopPrompt.font = UIKit.Font;
        stopPrompt.fontSize = 36;
        stopPrompt.color = Color.white;
        stopPrompt.horizontalOverflow = HorizontalWrapMode.Overflow;
        stopPrompt.verticalOverflow = VerticalWrapMode.Overflow;
        stopPrompt.text = "E를 눌러 와인더를 정지시키세요!";
        promptObject.SetActive(false);
    }

    bool stopTargetFrozen;
    static readonly Color StopPromptNormal = Color.white;
    static readonly Color StopPromptFrozen = new Color(1f, 0.25f, 0.2f, 1f);

    /// <summary>와인더 등 뒤에 있으면 E 안내. 이미 태엽이 멈춘 와인더면 빨간 글씨 (E 안 됨)</summary>
    void RefreshStopPrompt()
    {
        if (stopPrompt == null)
            return;

        int state = NearWinderState();
        stopTargetFrozen = state == 2;
        stopPrompt.gameObject.SetActive(state != 0);
        stopPrompt.color = stopTargetFrozen ? StopPromptFrozen : StopPromptNormal;
    }

    /// <summary>0 = 근처에 와인더 없음, 1 = 정지시킬 수 있음, 2 = 이미 태엽이 멈춘 와인더</summary>
    int NearWinderState()
    {
        TickToyMove[] toys = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < toys.Length; i++)
        {
            if (toys[i] == null || !toys[i].enabled || toys[i].IsDead)
                continue;
            WinderMove winder = toys[i].FindWinderInReach();
            if (winder != null)
                return winder.IsFrozen ? 2 : 1;
            NetPuppet puppet = toys[i].FindWinderPuppetInReach();
            if (puppet != null)
                return puppet.IsFrozen ? 2 : 1;
        }

        return 0;
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
        EnsureTrapAbilityIcon();
        Sprite sprite = runAbilityIcon;
        if (abilityKind == AbilityKind.Faint)
            sprite = faintAbilityIcon;
        else if (abilityKind == AbilityKind.Duplicate)
            sprite = duplicateAbilityIcon;
        else if (abilityKind == AbilityKind.OneHit)
            sprite = oneHitAbilityIcon;
        else if (abilityKind == AbilityKind.Trap)
            sprite = trapAbilityIcon;

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
            else if (abilityKind == AbilityKind.Trap)
                abilityFallbackText.text = "덫";
            else
                abilityFallbackText.text = "달리기";
            abilityFallbackText.gameObject.SetActive(sprite == null);
        }

        if (levelText != null)
        {
            // Brick 황금 몽둥이 / 덫: 레벨 표시 없음
            if (abilityKind == AbilityKind.OneHit || abilityKind == AbilityKind.Trap)
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
        {
            if (toys[i] == null || !toys[i].enabled)
                continue;
            toys[i].GrantAbility(kind, abilityLevel);
        }
    }
}
