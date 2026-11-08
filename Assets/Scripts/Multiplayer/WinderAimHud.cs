using UnityEngine;
using UnityEngine.UI;

/// <summary>와인더 화면: 가운데 조준점 + 조준점 왼쪽 세로 힌트 바 (근처 틱토이 플레이어). UI 는 DemoScene 에 미리 만들어져 있음</summary>
public class WinderAimHud : MonoBehaviour
{
    public GameObject crosshair;
    public GameObject barRoot;
    public RectTransform barFill; // 위쪽 끝(anchorMax.y)으로 차오름

    float shownFill = -1f;

    void Awake()
    {
        SetVisible(false, false);
        SetFill(0f);
    }

    public void SetVisible(bool aim, bool bar)
    {
        if (crosshair != null && crosshair.activeSelf != aim)
            crosshair.SetActive(aim);
        if (barRoot != null && barRoot.activeSelf != bar)
            barRoot.SetActive(bar);
    }

    public void SetFill(float amount)
    {
        amount = Mathf.Clamp01(amount);
        if (barFill == null || Mathf.Approximately(amount, shownFill))
            return;
        shownFill = amount;
        barFill.anchorMax = new Vector2(1f, amount);
    }
}
