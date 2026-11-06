using UnityEngine;
using UnityEngine.UI;

/// <summary>왼쪽 위 관전자 수 표시 (눈동자 아이콘 + "X 2"). UI 는 DemoScene 에 미리 만들어져 있음</summary>
public class SpectatorBadge : MonoBehaviour
{
    public GameObject root;
    public Text countText;

    int shown = -1;

    void Awake()
    {
        SetCount(0);
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
}
