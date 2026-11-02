using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>ESC 설정창. 좌우/수직 마우스 감도 슬라이더 + 숫자 입력.</summary>
public class SettingsMenu : MonoBehaviour
{
    static SettingsMenu instance;

    GameObject root;
    Slider horizontalSlider;
    Slider verticalSlider;
    InputField horizontalInput;
    InputField verticalInput;
    bool syncing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<SettingsMenu>() != null)
            return;
        GameObject go = new GameObject("SettingsMenu");
        DontDestroyOnLoad(go);
        go.AddComponent<SettingsMenu>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        GameSettings.EnsureLoaded();
        EnsureEventSystem();
        BuildUi();
        SetOpen(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            SetOpen(!GameSettings.MenuOpen);
    }

    void SetOpen(bool open)
    {
        GameSettings.MenuOpen = open;
        if (root != null)
            root.SetActive(open);

        if (open)
        {
            RefreshFromSettings();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    void RefreshFromSettings()
    {
        syncing = true;
        if (horizontalSlider != null)
            horizontalSlider.value = GameSettings.HorizontalSensitivity;
        if (verticalSlider != null)
            verticalSlider.value = GameSettings.VerticalSensitivity;
        if (horizontalInput != null)
            horizontalInput.text = Mathf.RoundToInt(GameSettings.HorizontalSensitivity).ToString();
        if (verticalInput != null)
            verticalInput.text = Mathf.RoundToInt(GameSettings.VerticalSensitivity).ToString();
        syncing = false;
    }

    void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject es = new GameObject("EventSystem");
        DontDestroyOnLoad(es);
        es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    void BuildUi()
    {
        Font font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕" }, 28);
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasGo = new GameObject("SettingsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        root = new GameObject("SettingsRoot", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvasGo.transform, false);
        RectTransform rootRt = root.GetComponent<RectTransform>();
        StretchFull(rootRt);
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(620f, 320f);
        Image panelImg = panel.GetComponent<Image>();
        panelImg.color = new Color(0.12f, 0.14f, 0.18f, 0.96f);

        Text title = CreateText(panel.transform, "Title", "설정", font, 36, TextAnchor.MiddleCenter);
        RectTransform titleRt = title.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -18f);
        titleRt.sizeDelta = new Vector2(-40f, 48f);

        BuildSensitivityRow(panel.transform, font, "좌우 감도", 70f, true);
        BuildSensitivityRow(panel.transform, font, "수직 감도", 150f, false);

        Text hint = CreateText(panel.transform, "Hint", "ESC로 닫기", font, 22, TextAnchor.MiddleCenter);
        RectTransform hintRt = hint.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.anchoredPosition = new Vector2(0f, 18f);
        hintRt.sizeDelta = new Vector2(-40f, 36f);
        hint.color = new Color(0.75f, 0.78f, 0.82f, 1f);
    }

    void BuildSensitivityRow(Transform parent, Font font, string label, float yFromTop, bool horizontal)
    {
        GameObject row = new GameObject(label + "Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, -yFromTop);
        rowRt.sizeDelta = new Vector2(-40f, 56f);

        Text labelText = CreateText(row.transform, "Label", label, font, 26, TextAnchor.MiddleLeft);
        RectTransform labelRt = labelText.rectTransform;
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(0f, 1f);
        labelRt.pivot = new Vector2(0f, 0.5f);
        labelRt.anchoredPosition = new Vector2(8f, 0f);
        labelRt.sizeDelta = new Vector2(130f, 0f);

        GameObject sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(row.transform, false);
        RectTransform sliderRt = sliderGo.GetComponent<RectTransform>();
        sliderRt.anchorMin = new Vector2(0f, 0.5f);
        sliderRt.anchorMax = new Vector2(1f, 0.5f);
        sliderRt.pivot = new Vector2(0.5f, 0.5f);
        sliderRt.anchoredPosition = new Vector2(20f, 0f);
        sliderRt.sizeDelta = new Vector2(-280f, 28f);
        sliderRt.offsetMin = new Vector2(150f, -14f);
        sliderRt.offsetMax = new Vector2(-120f, 14f);

        Image track = CreateImage(sliderGo.transform, "Background", new Color(0.25f, 0.28f, 0.32f, 1f));
        StretchFull(track.rectTransform);

        Image fillArea = CreateImage(sliderGo.transform, "Fill Area", new Color(0f, 0f, 0f, 0f));
        StretchFull(fillArea.rectTransform);
        fillArea.raycastTarget = false;
        Image fill = CreateImage(fillArea.transform, "Fill", new Color(0.35f, 0.7f, 1f, 1f));
        StretchFull(fill.rectTransform);

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        StretchFull(handleArea.GetComponent<RectTransform>());
        Image handle = CreateImage(handleArea.transform, "Handle", new Color(0.95f, 0.95f, 0.98f, 1f));
        RectTransform handleRt = handle.rectTransform;
        handleRt.sizeDelta = new Vector2(22f, 28f);

        Slider builtSlider = sliderGo.GetComponent<Slider>();
        builtSlider.targetGraphic = handle;
        builtSlider.fillRect = fill.rectTransform;
        builtSlider.handleRect = handleRt;
        builtSlider.direction = Slider.Direction.LeftToRight;
        builtSlider.minValue = GameSettings.MinSensitivity;
        builtSlider.maxValue = GameSettings.MaxSensitivity;
        builtSlider.wholeNumbers = true;
        builtSlider.value = horizontal ? GameSettings.HorizontalSensitivity : GameSettings.VerticalSensitivity;

        GameObject inputGo = new GameObject("ValueInput", typeof(RectTransform), typeof(Image), typeof(InputField));
        inputGo.transform.SetParent(row.transform, false);
        RectTransform inputRt = inputGo.GetComponent<RectTransform>();
        inputRt.anchorMin = new Vector2(1f, 0.5f);
        inputRt.anchorMax = new Vector2(1f, 0.5f);
        inputRt.pivot = new Vector2(1f, 0.5f);
        inputRt.anchoredPosition = new Vector2(-8f, 0f);
        inputRt.sizeDelta = new Vector2(96f, 40f);
        Image inputBg = inputGo.GetComponent<Image>();
        inputBg.color = new Color(0.2f, 0.22f, 0.26f, 1f);

        Text placeholder = CreateText(inputGo.transform, "Placeholder", "0", font, 24, TextAnchor.MiddleCenter);
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        StretchFull(placeholder.rectTransform);
        placeholder.raycastTarget = false;

        Text inputText = CreateText(inputGo.transform, "Text", "", font, 24, TextAnchor.MiddleCenter);
        StretchFull(inputText.rectTransform);
        inputText.supportRichText = false;
        RectTransform textRt = inputText.rectTransform;
        textRt.offsetMin = new Vector2(6f, 2f);
        textRt.offsetMax = new Vector2(-6f, -2f);

        InputField builtInput = inputGo.GetComponent<InputField>();
        builtInput.textComponent = inputText;
        builtInput.placeholder = placeholder;
        builtInput.contentType = InputField.ContentType.IntegerNumber;
        builtInput.characterLimit = 4;
        builtInput.text = Mathf.RoundToInt(builtSlider.value).ToString();

        bool isHorizontal = horizontal;
        builtSlider.onValueChanged.AddListener(value =>
        {
            if (syncing)
                return;
            syncing = true;
            int rounded = Mathf.RoundToInt(value);
            builtInput.text = rounded.ToString();
            if (isHorizontal)
                GameSettings.SetHorizontalSensitivity(rounded);
            else
                GameSettings.SetVerticalSensitivity(rounded);
            syncing = false;
        });

        builtInput.onEndEdit.AddListener(text =>
        {
            if (syncing)
                return;
            if (!int.TryParse(text, out int parsed))
                parsed = Mathf.RoundToInt(builtSlider.value);
            parsed = Mathf.Clamp(parsed, (int)GameSettings.MinSensitivity, (int)GameSettings.MaxSensitivity);
            syncing = true;
            builtSlider.value = parsed;
            builtInput.text = parsed.ToString();
            if (isHorizontal)
                GameSettings.SetHorizontalSensitivity(parsed);
            else
                GameSettings.SetVerticalSensitivity(parsed);
            syncing = false;
        });

        if (horizontal)
        {
            horizontalSlider = builtSlider;
            horizontalInput = builtInput;
        }
        else
        {
            verticalSlider = builtSlider;
            verticalInput = builtInput;
        }
    }

    static Text CreateText(Transform parent, string name, string content, Font font, int size, TextAnchor align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = align;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = content;
        return text;
    }

    static Image CreateImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
