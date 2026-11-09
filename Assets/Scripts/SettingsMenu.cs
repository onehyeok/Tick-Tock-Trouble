using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>ESC 설정창. 좌우 감도 · 로비 사운드 · 인게임 사운드 (슬라이더 + 숫자 입력), 게임 중엔 "로비로 나가기" 버튼.</summary>
public class SettingsMenu : MonoBehaviour
{
    static SettingsMenu instance;

    GameObject root;
    /// <summary>창을 열 때 저장된 값으로 다시 맞춤 (줄마다 하나)</summary>
    System.Action refreshRows;
    GameObject exitButton;
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

    /// <summary>인트로·로비(방 목록/방 화면)에선 마우스를 잠그지 않고 나가기 버튼도 숨김</summary>
    static bool InMenuScene()
    {
        string scene = SceneManager.GetActiveScene().name;
        return scene == MatchService.IntroScene || scene == MatchService.LobbyScene;
    }

    void SetOpen(bool open)
    {
        GameSettings.MenuOpen = open;
        if (root != null)
            root.SetActive(open);
        if (exitButton != null)
            exitButton.SetActive(!InMenuScene());

        if (open)
        {
            RefreshFromSettings();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else if (!InMenuScene())
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    /// <summary>게임을 그만두고 로비(방 목록)로</summary>
    void ExitToLobby()
    {
        GameSettings.MenuOpen = false;
        if (root != null)
            root.SetActive(false);
        MatchContext.Active = false;
        MatchContext.ReturnToRoom = false;
        IMatchBackend backend = MatchService.Backend;
        if (backend.InRoom)
            backend.LeaveRoom();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        SceneManager.LoadScene(MatchService.LobbyScene);
    }

    void RefreshFromSettings()
    {
        syncing = true;
        refreshRows?.Invoke();
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

    // ---------------- 화면 (게임 UI 와 같은 Jua 글꼴 · 크림/주황 장난감 스타일) ----------------

    void BuildUi()
    {
        Canvas canvas = UIKit.Canvas("SettingsCanvas", 500);
        canvas.transform.SetParent(transform, false);

        // 뒤 어둡게
        Image dim = UIKit.Panel(canvas.transform, "SettingsRoot", new Color(0.12f, 0.07f, 0.04f, 0.6f), false);
        dim.rectTransform.Fill();
        dim.raycastTarget = true;
        root = dim.gameObject;

        // 주황 테두리 + 크림 판
        Image border = UIKit.Panel(root.transform, "Panel", UIKit.Orange, false);
        border.rectTransform.At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 520f));
        Shadow shadow = border.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.3f);
        shadow.effectDistance = new Vector2(0f, -10f);
        Transform panel = UIKit.Panel(border.transform, "Inner", UIKit.Cream, false).rectTransform.Fill(8f, 8f, 8f, 8f);

        Text title = UIKit.Label(panel, "Title", "설정", 48, UIKit.Orange).Outlined(UIKit.Brown, 3f);
        title.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(560f, 64f));

        BuildSliderRow(panel, "좌우 감도", -100f, GameSettings.MinSensitivity, GameSettings.MaxSensitivity,
            () => GameSettings.HorizontalSensitivity, GameSettings.SetHorizontalSensitivity);
        BuildSliderRow(panel, "로비 사운드", -175f, 0f, 100f,
            () => GameSettings.LobbyVolume, GameSettings.SetLobbyVolume);
        BuildSliderRow(panel, "인게임 사운드", -250f, 0f, 100f,
            () => GameSettings.GameVolume, GameSettings.SetGameVolume);

        Button exit = UIKit.Button(panel, "ExitToLobbyButton", "로비로 나가기", UIKit.Orange, Color.white, 34);
        exit.GetComponentInChildren<Text>().Outlined(UIKit.Brown, 2f);
        exit.GetComponent<RectTransform>().At(new Vector2(0.5f, 1f), new Vector2(0f, -345f), new Vector2(320f, 70f));
        exit.onClick.AddListener(ExitToLobby);
        exitButton = exit.gameObject;

        Text hint = UIKit.Label(panel, "Hint", "ESC 로 닫기", 24, new Color(0.36f, 0.22f, 0.13f, 0.7f));
        hint.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(560f, 36f));
    }

    /// <summary>한 줄: 이름 + 슬라이더 + 숫자 입력칸. 값을 바꾸면 바로 저장</summary>
    void BuildSliderRow(Transform parent, string label, float y, float min, float max,
        System.Func<float> getValue, System.Action<float> setValue)
    {
        RectTransform row = UIKit.Rect(parent, label + "Row").At(new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(560f, 56f));

        Text labelText = UIKit.Label(row, "Label", label, 27, UIKit.Brown, TextAnchor.MiddleLeft);
        labelText.horizontalOverflow = HorizontalWrapMode.Overflow; // "인게임 사운드"가 두 줄로 안 꺾이게
        labelText.rectTransform.At(new Vector2(0f, 0.5f), Vector2.zero, new Vector2(160f, 56f));

        // 슬라이더: 연한 바탕 + 주황 채움 + 갈색 손잡이
        RectTransform sliderRt = UIKit.Rect(row, "Slider").At(new Vector2(0f, 0.5f), new Vector2(170f, 0f), new Vector2(270f, 22f));
        Slider slider = sliderRt.gameObject.AddComponent<Slider>();
        UIKit.Panel(sliderRt, "Background", UIKit.CreamDark, false).rectTransform.Fill();
        RectTransform fillArea = UIKit.Rect(sliderRt, "Fill Area").Fill();
        Image fill = UIKit.Panel(fillArea, "Fill", UIKit.Orange, false);
        fill.rectTransform.Fill();
        RectTransform handleArea = UIKit.Rect(sliderRt, "Handle Slide Area").Fill();
        Image handle = UIKit.Panel(handleArea, "Handle", UIKit.Brown, false);
        handle.rectTransform.sizeDelta = new Vector2(20f, 36f);

        slider.targetGraphic = handle;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = true;
        slider.value = getValue();

        // 숫자 입력칸 (흰 바탕 · 갈색 글자)
        InputField input = UIKit.Input(row, "ValueInput", "0", 28);
        input.GetComponent<RectTransform>().At(new Vector2(1f, 0.5f), Vector2.zero, new Vector2(100f, 50f));
        input.textComponent.alignment = TextAnchor.MiddleCenter;
        ((Text)input.placeholder).alignment = TextAnchor.MiddleCenter;
        input.textComponent.rectTransform.Fill(6f, 2f, 6f, 2f);
        input.placeholder.rectTransform.Fill(6f, 2f, 6f, 2f);
        input.contentType = InputField.ContentType.IntegerNumber;
        input.characterLimit = 4;
        input.text = Mathf.RoundToInt(slider.value).ToString();

        slider.onValueChanged.AddListener(value =>
        {
            if (syncing)
                return;
            syncing = true;
            int rounded = Mathf.RoundToInt(value);
            input.text = rounded.ToString();
            setValue(rounded);
            syncing = false;
        });

        input.onEndEdit.AddListener(text =>
        {
            if (syncing)
                return;
            if (!int.TryParse(text, out int parsed))
                parsed = Mathf.RoundToInt(slider.value);
            parsed = Mathf.Clamp(parsed, (int)min, (int)max);
            syncing = true;
            slider.value = parsed;
            input.text = parsed.ToString();
            setValue(parsed);
            syncing = false;
        });

        refreshRows += () =>
        {
            slider.value = getValue();
            input.text = Mathf.RoundToInt(getValue()).ToString();
        };
    }
}
