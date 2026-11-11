using UnityEngine;
using UnityEngine.UI;

/// <summary>명령하기 안내 창 (브릭 얼굴 + 문구 + 카운트다운). UI 는 DemoScene 에 미리 만들어져 있음.
/// 와인더: "멈춰! 너희들이 의심돼!" / "5초 뒤 Z키 사용" (처음부터 키가 보임)
/// 걸린 틱토이: "와인더에게 의심당했어! 아래 행동을 취하자!" / "5초 뒤" → 레벨에 따라 지시 공개 시점부터 "3초 뒤 Z키 사용"</summary>
public class CommandHud : MonoBehaviour
{
    public GameObject panel;
    public Text titleText;
    public Text countText;

    const string WinderTitle = "멈춰! 너희들이 의심돼!";
    const string TickToyTitle = "와인더에게 의심당했어! 아래 행동을 취하자!";

    static CommandHud instance;
    float endTime = -1f;
    float revealLeft;
    int emote;

    void Awake()
    {
        instance = this;
        if (panel != null)
            panel.SetActive(false);
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>카운트다운 시작. winder = 명령한 와인더 화면인지. revealLeft = 남은 시간이 이 값 이하가 되면 어떤 키인지 보여 줌 (음수 = 끝까지 안 알려줌)</summary>
    public static void Show(int emote, float seconds, float revealLeft, bool winder)
    {
        if (instance == null)
            return;
        instance.emote = emote;
        instance.revealLeft = revealLeft;
        instance.endTime = Time.time + seconds;
        if (instance.titleText != null)
            instance.titleText.text = winder ? WinderTitle : TickToyTitle;
    }

    void Update()
    {
        if (panel == null || endTime < 0f)
            return;

        float left = endTime - Time.time;
        RoundManager rm = RoundManager.Instance;
        // 끝나고 1.5초 뒤, 또는 (게임 중) 내가 쓰러지면 닫음
        if (left <= -1.5f || (rm != null && !rm.LocalAlive))
        {
            endTime = -1f;
            panel.SetActive(false);
            return;
        }

        if (!panel.activeSelf)
            panel.SetActive(true);
        bool reveal = revealLeft >= 0f && left <= revealLeft;
        string key = WinderCommand.KeyName(emote);
        if (left > 0f)
        {
            int sec = Mathf.CeilToInt(left);
            countText.text = reveal ? sec + "초 뒤 " + key + "키 사용" : sec + "초 뒤";
        }
        else
            countText.text = reveal ? key + "키 사용!" : "지금!";
    }
}
