using UnityEngine;
using UnityEngine.UI;

/// <summary>게임 중 1분 넘게 아무 조작(키·마우스)이 없으면 "움직이지 않으면 잠시 후 방에서 추방됩니다" 안내.
/// 조작하면 바로 사라짐. 살아 있을 때만 (관전·결과 화면 제외). UI 는 DemoScene 에 미리 만들어져 있음</summary>
public class AfkWarning : MonoBehaviour
{
    public Text warningText;

    const float IdleSeconds = 60f;
    float idle;
    Vector3 lastMouse;

    void Awake()
    {
        if (warningText != null)
            warningText.gameObject.SetActive(false);
    }

    void Update()
    {
        RoundManager rm = RoundManager.Instance;
        bool playing = MatchContext.Active && rm != null && rm.LocalAlive && rm.PhaseIndex != 2; // 2 = 결과 화면
        if (!playing || HasInput())
            idle = 0f;
        else
            idle += Time.unscaledDeltaTime;

        bool show = idle >= IdleSeconds;
        if (warningText != null && warningText.gameObject.activeSelf != show)
            warningText.gameObject.SetActive(show);
    }

    bool HasInput()
    {
        bool moved = Input.mousePosition != lastMouse;
        lastMouse = Input.mousePosition;
        if (moved || Input.anyKey)
            return true;
        // 커서가 잠겨 있어도 마우스를 움직이면 알 수 있게
        return Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.001f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.001f;
    }
}
