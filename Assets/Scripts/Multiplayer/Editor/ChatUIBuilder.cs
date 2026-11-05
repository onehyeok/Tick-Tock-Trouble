using UnityEngine;
using UnityEngine.UI;

/// <summary>에디터: 채팅창 UI 를 씬에 만들어 넣음 (방 화면 / 게임 HUD)</summary>
public static class ChatUIBuilder
{
    public static ChatBox Build(Transform parent, bool inGame, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        const float inputHeight = 46f;

        Image bg = UIKit.Panel(parent, "ChatBox", inGame ? new Color(0f, 0f, 0f, 0.3f) : new Color(0.36f, 0.22f, 0.13f, 0.7f), false);
        bg.rectTransform.At(anchor, pos, size);
        bg.raycastTarget = false;
        bg.gameObject.AddComponent<RectMask2D>();

        ChatBox box = bg.gameObject.AddComponent<ChatBox>();
        box.inGame = inGame;
        box.visibleLines = inGame ? 6 : 5;

        Text messages = UIKit.Label(bg.transform, "Messages", "", 26, Color.white, TextAnchor.LowerLeft);
        messages.supportRichText = true;
        messages.rectTransform.Fill(14f, inputHeight + 16f, 14f, 8f);
        if (inGame)
            messages.Outlined(new Color(0f, 0f, 0f, 0.6f), 1.5f);
        box.messagesText = messages;

        InputField input = UIKit.Input(bg.transform, "ChatInput", inGame ? "Enter 보내기 · Esc 취소" : "채팅 입력 (Enter)", 26);
        // 입력칸: 둥근 모서리 없이 네모
        Image inputBg = input.GetComponent<Image>();
        inputBg.sprite = null;
        inputBg.type = Image.Type.Simple;
        RectTransform ir = (RectTransform)input.transform;
        ir.anchorMin = new Vector2(0f, 0f);
        ir.anchorMax = new Vector2(1f, 0f);
        ir.pivot = new Vector2(0.5f, 0f);
        ir.sizeDelta = new Vector2(-16f, inputHeight);
        ir.anchoredPosition = new Vector2(0f, 8f);
        box.input = input;
        return box;
    }
}
