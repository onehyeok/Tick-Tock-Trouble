using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>인트로: 닉네임 정하고 로비로 (닉네임 겹치면 알림)</summary>
public class IntroUI : MonoBehaviour
{
    public InputField nicknameInput;
    public Button startButton;
    public Text messageText;
    public GameObject busyRoot;

    bool busy;

    void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UIKit.EnsureEventSystem();

        if (nicknameInput != null)
        {
            nicknameInput.text = PlayerProfile.Nickname;
            nicknameInput.onEndEdit.AddListener(_ =>
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    OnStart();
            });
        }
        if (startButton != null)
            startButton.onClick.AddListener(OnStart);
        ShowMessage("");
        SetBusy(false);
    }

    void OnStart()
    {
        if (busy)
            return;

        string nick = nicknameInput != null ? nicknameInput.text.Trim() : "";
        string invalid = MatchService.ValidateNickname(nick);
        if (invalid != null)
        {
            ShowMessage(invalid);
            return;
        }

        SetBusy(true);
        ShowMessage("서버에 연결하는 중...");
        IMatchBackend backend = MatchService.Backend;
        backend.ConnectLobby(nick, (ok, error) =>
        {
            if (!ok)
            {
                SetBusy(false);
                ShowMessage(string.IsNullOrEmpty(error) ? "연결에 실패했어요. 다시 시도해 주세요." : error);
                return;
            }

            if (backend.IsNicknameTaken(nick))
            {
                SetBusy(false);
                ShowMessage("이미 사용 중인 닉네임이에요. 다른 닉네임을 정해 주세요!");
                return;
            }

            PlayerProfile.Nickname = nick;
            SceneManager.LoadScene(MatchService.LobbyScene);
        });
    }

    void SetBusy(bool value)
    {
        busy = value;
        if (startButton != null)
            startButton.interactable = !value;
        if (busyRoot != null)
            busyRoot.SetActive(value);
    }

    void ShowMessage(string msg)
    {
        if (messageText != null)
            messageText.text = msg;
    }
}
