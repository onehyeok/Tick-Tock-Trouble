using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>채팅 기록 (방 ↔ 게임 씬이 바뀌어도 유지)</summary>
public static class ChatLog
{
    const int MaxLines = 40;
    static readonly List<string> lines = new List<string>();

    public static IReadOnlyList<string> Lines => lines;
    public static event Action Changed;

    public static void Add(string nickname, string text)
    {
        AddRaw("<color=#FFD27A>" + Escape(nickname) + "</color> : " + Escape(text));
    }

    public static void AddSystem(string text)
    {
        AddRaw("<color=#8FE0C8>" + Escape(text) + "</color>");
    }

    public static void Clear()
    {
        lines.Clear();
        Changed?.Invoke();
    }

    static void AddRaw(string line)
    {
        lines.Add(line);
        if (lines.Count > MaxLines)
            lines.RemoveAt(0);
        Changed?.Invoke();
    }

    // 글자색 태그를 못 넣게 < > 를 전각 문자로
    static string Escape(string s) => (s ?? "").Replace("<", "＜").Replace(">", "＞");
}

/// <summary>
/// 채팅창 (방 화면 / 게임 중). UI 는 씬에 미리 만들어져 있음.
/// - 방: 입력칸 항상 보임. Enter 로 입력칸 선택 / 보내기
/// - 게임: Enter → 입력칸 열림(캐릭터 조작 멈춤) → Enter 보내기 / Esc 취소
/// - 도배 방지: 5초 안에 5번째 메시지를 보내려 하면 30초 동안 채팅 금지
/// </summary>
public class ChatBox : MonoBehaviour
{
    public Text messagesText;
    public InputField input;
    public bool inGame;
    public int visibleLines = 6;

    const int MaxLength = 50;
    const int SpamCount = 5;
    const float SpamWindow = 5f;
    const float MuteSeconds = 30f;

    // 씬이 바뀌어도 금지 시간 유지
    static readonly Queue<float> sendTimes = new Queue<float>();
    static float muteUntil;

    bool typing;

    void OnEnable()
    {
        ChatLog.Changed += Refresh;
        Refresh();
        if (input != null)
        {
            input.characterLimit = MaxLength;
            if (inGame)
                input.gameObject.SetActive(false);
        }
    }

    void OnDisable()
    {
        ChatLog.Changed -= Refresh;
        SetTyping(false);
    }

    void Update()
    {
        if (input == null)
            return;

        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

        if (!inGame)
        {
            if (input.isFocused)
                typing = true;
            if (!enter)
                return;
            if (typing)
            {
                Submit();
                typing = false;
            }
            else if (!OtherInputFocused())
            {
                input.Select();
                input.ActivateInputField();
                typing = true;
            }
            return;
        }

        if (typing)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                input.text = "";
                Close();
            }
            else if (enter)
            {
                Submit();
                Close();
            }
            return;
        }

        if (enter && !GameSettings.MenuOpen)
            Open();
    }

    void Open()
    {
        input.gameObject.SetActive(true);
        input.text = "";
        input.Select();
        input.ActivateInputField();
        SetTyping(true);
    }

    void Close()
    {
        input.DeactivateInputField();
        input.gameObject.SetActive(false);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
        SetTyping(false);
    }

    void SetTyping(bool value)
    {
        typing = value;
        if (inGame)
            GameSettings.ChatTyping = value;
    }

    bool OtherInputFocused()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null)
            return false;
        InputField other = EventSystem.current.currentSelectedGameObject.GetComponent<InputField>();
        return other != null && other != input && other.isFocused;
    }

    void Submit()
    {
        string text = (input.text ?? "").Trim();
        input.text = "";
        if (text.Length == 0)
            return;

        float now = Time.unscaledTime;
        if (now < muteUntil)
        {
            ChatLog.AddSystem("채팅 금지 중이에요. " + Mathf.CeilToInt(muteUntil - now) + "초 뒤에 다시 보낼 수 있어요.");
            return;
        }

        while (sendTimes.Count > 0 && now - sendTimes.Peek() > SpamWindow)
            sendTimes.Dequeue();
        if (sendTimes.Count >= SpamCount - 1)
        {
            muteUntil = now + MuteSeconds;
            sendTimes.Clear();
            ChatLog.AddSystem("채팅을 너무 빨리 보내서 30초 동안 채팅이 금지됐어요.");
            return;
        }

        sendTimes.Enqueue(now);
        if (text.Length > MaxLength)
            text = text.Substring(0, MaxLength);
        MatchService.Backend.SendChat(text);
    }

    void Refresh()
    {
        if (messagesText == null)
            return;
        IReadOnlyList<string> lines = ChatLog.Lines;
        int start = Mathf.Max(0, lines.Count - visibleLines);
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = start; i < lines.Count; i++)
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(lines[i]);
        }
        messagesText.text = sb.ToString();
    }
}
