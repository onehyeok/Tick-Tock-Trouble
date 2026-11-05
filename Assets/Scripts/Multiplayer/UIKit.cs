using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>귀여운 장난감 톤 UI를 코드로 만드는 도우미 (로비 씬 생성기·게임 HUD 공용)</summary>
public static class UIKit
{
    public static readonly Color Cream = new Color(1f, 0.96f, 0.88f, 1f);
    public static readonly Color CreamDark = new Color(0.95f, 0.87f, 0.72f, 1f);
    public static readonly Color Brown = new Color(0.36f, 0.22f, 0.13f, 1f);
    public static readonly Color Orange = new Color(1f, 0.6f, 0.22f, 1f);
    public static readonly Color Mint = new Color(0.36f, 0.78f, 0.66f, 1f);
    public static readonly Color Pink = new Color(1f, 0.52f, 0.62f, 1f);
    public static readonly Color Sky = new Color(0.45f, 0.72f, 0.98f, 1f);
    public static readonly Color Gray = new Color(0.7f, 0.68f, 0.65f, 1f);
    public static readonly Color Shade = new Color(0.12f, 0.07f, 0.04f, 0.6f);

    static Font font;
    /// <summary>둥근 모서리 스프라이트 (에디터에서 생성 시 기본 UISprite 지정)</summary>
    public static Sprite RoundSprite;

    public static Font Font
    {
        get
        {
            if (font == null)
                font = Resources.Load<Font>("Fonts/Jua-Regular");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
        set => font = value;
    }

    public static Canvas Canvas(string name, int order = 0)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    public static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null)
            return;
        var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    public static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// <summary>앵커 기준 배치. anchor = 부모에서의 기준점(0~1), pos = 거기서 떨어진 거리</summary>
    public static RectTransform At(this RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        r.anchorMin = anchor;
        r.anchorMax = anchor;
        r.pivot = anchor;
        r.anchoredPosition = pos;
        r.sizeDelta = size;
        return r;
    }

    public static RectTransform Fill(this RectTransform r, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = new Vector2(left, bottom);
        r.offsetMax = new Vector2(-right, -top);
        return r;
    }

    public static Image Panel(Transform parent, string name, Color color, bool rounded = true)
    {
        var r = Rect(parent, name);
        var img = r.gameObject.AddComponent<Image>();
        img.color = color;
        if (rounded && RoundSprite != null)
        {
            img.sprite = RoundSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 0.35f;
        }
        return img;
    }

    public static Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var r = Rect(parent, name);
        var t = r.gameObject.AddComponent<Text>();
        t.font = Font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>글자에 테두리(외곽선) — 장난감 느낌</summary>
    public static Text Outlined(this Text t, Color outline, float dist = 3f)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = outline;
        o.effectDistance = new Vector2(dist, -dist);
        return t;
    }

    public static Button Button(Transform parent, string name, string text, Color bg, Color fg, int fontSize = 40)
    {
        var img = Panel(parent, name, bg);
        var shadow = img.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.25f);
        shadow.effectDistance = new Vector2(0f, -6f);
        var btn = img.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.6f);
        colors.normalColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        btn.colors = colors;
        var label = Label(img.transform, "Text", text, fontSize, fg);
        label.rectTransform.Fill();
        return btn;
    }

    public static InputField Input(Transform parent, string name, string placeholder, int fontSize = 40, bool password = false)
    {
        var img = Panel(parent, name, Color.white);
        var input = img.gameObject.AddComponent<InputField>();
        var text = Label(img.transform, "Text", "", fontSize, Brown, TextAnchor.MiddleLeft);
        text.rectTransform.Fill(24f, 6f, 24f, 6f);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.supportRichText = false;
        var ph = Label(img.transform, "Placeholder", placeholder, fontSize, new Color(0.36f, 0.22f, 0.13f, 0.4f), TextAnchor.MiddleLeft);
        ph.rectTransform.Fill(24f, 6f, 24f, 6f);
        input.textComponent = text;
        input.placeholder = ph;
        input.targetGraphic = img;
        input.caretColor = Brown;
        input.caretWidth = 3;
        if (password)
        {
            input.contentType = InputField.ContentType.Password;
            input.inputType = InputField.InputType.Password;
        }
        return input;
    }

    /// <summary>세로 스크롤 목록. content에 항목을 넣으면 자동 정렬</summary>
    public static ScrollRect Scroll(Transform parent, string name, out RectTransform content, float spacing = 14f)
    {
        var bg = Panel(parent, name, new Color(1f, 1f, 1f, 0.55f));
        var scroll = bg.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        var viewport = Rect(bg.transform, "Viewport").Fill(16f, 16f, 16f, 16f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.001f);

        content = Rect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = content;
        return scroll;
    }

    public static void SetText(Button b, string text)
    {
        var t = b.GetComponentInChildren<Text>(true);
        if (t != null)
            t.text = text;
    }

    public static void SetColor(Button b, Color c)
    {
        var img = b.GetComponent<Image>();
        if (img != null)
            img.color = c;
    }
}
