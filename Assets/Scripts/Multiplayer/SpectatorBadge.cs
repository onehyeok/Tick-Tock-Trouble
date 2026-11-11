using UnityEngine;
using UnityEngine.UI;

/// <summary>관전 표시: 왼쪽 위 관전자 수(눈동자 아이콘 + "X 2") / 화면 아래 관전 중인 사람 닉네임.
/// UI 는 DemoScene 에 미리 만들어져 있음</summary>
public class SpectatorBadge : MonoBehaviour
{
    public GameObject root;
    public Text countText;
    public Text nameText;

    int shown = -1;
    string shownName;

    void Awake()
    {
        SetCount(0);
        SetName(null);
    }

    public void SetCount(int n)
    {
        if (n == shown)
            return;
        shown = n;
        if (root != null)
            root.SetActive(n > 0);
        if (countText != null && n > 0)
            countText.text = "X " + n;
    }

    /// <summary>관전 중인 사람 닉네임 (null/빈 값 = 숨김)</summary>
    public void SetName(string nick)
    {
        nick = nick ?? "";
        if (nick == shownName)
            return;
        shownName = nick;
        if (nameText == null)
            return;
        nameText.gameObject.SetActive(nick.Length > 0);
        nameText.text = nick;
    }
}
