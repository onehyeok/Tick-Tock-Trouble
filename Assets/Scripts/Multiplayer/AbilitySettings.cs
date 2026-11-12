using UnityEngine;

/// <summary>
/// 능력·게임 수치를 한곳에 모은 설정 (Resources/AbilitySettings 에셋). 인스펙터에서 숫자만 바꾸면 바로 반영됨.
/// 레벨별 값은 [Lv1, Lv2, Lv3] 순서. 에셋이 없으면 아래 기본값(지금 게임과 같은 값)을 씀.
/// </summary>
[CreateAssetMenu(menuName = "TTT/Ability Settings", fileName = "AbilitySettings")]
public class AbilitySettings : ScriptableObject
{
    [Header("틱토이 능력 (Lv1, Lv2, Lv3)")]
    [Tooltip("달리기 지속 시간(초)")]
    public float[] runSeconds = { 5f, 8f, 12f };
    [Tooltip("기절한 척 지속 시간(초). -1 = 직접 일어날 때까지")]
    public float[] faintSeconds = { 10f, 20f, -1f };
    [Tooltip("분신 연막 지속 시간(초)")]
    public float[] duplicateSeconds = { 3f, 5f, 7f };
    [Tooltip("분신 수")]
    public int[] duplicateCount = { 1, 2, 4 };

    [Header("틱토이 기타")]
    [Tooltip("판당 변장 횟수")]
    public int disguiseUses = 5;
    [Tooltip("변장할 수 있는 AI 까지 거리(m)")]
    public float disguiseReach = 2.5f;
    [Tooltip("와인더 등 뒤에서 E → 와인더 태엽 정지 시간(초)")]
    public float winderStopSeconds = 5f;

    [Header("와인더 태엽")]
    [Tooltip("등 태엽 한 바퀴(= 능력 하나) 걸리는 시간(초)")]
    public float springTurnSeconds = 20f;
    [Tooltip("황금 방망이가 나올 확률 (나머지를 회복하기·덫·명령하기가 똑같이 나눔)")]
    [Range(0f, 1f)] public float goldChance = 0.05f;

    [Header("와인더 능력 (Lv1, Lv2, Lv3)")]
    [Tooltip("회복하기 회복량. -1 = 최대 체력까지")]
    public int[] healAmount = { 25, 50, -1 };
    [Tooltip("명령하기: 틱토이 플레이어에게 어떤 키인지 보여 주는 시점 (남은 초). -1 = 안 알려줌")]
    public float[] commandReveal = { 3f, 1f, -1f };
    [Tooltip("명령하기 범위 반지름(m)")]
    public float commandRadius = 6f;
    [Tooltip("명령하기: 가만히 있다가 이모트할 때까지 시간(초)")]
    public float commandDelay = 5f;

    [Header("와인더 추리 보조")]
    [Tooltip("Z 의심 표시가 보이는 시간(초)")]
    public float markSeconds = 3f;
    [Tooltip("Z 의심 표시 재사용 대기(초)")]
    public float markCooldown = 10f;
    [Tooltip("남은 시간이 이 값(초) 이하가 되면 거리 힌트 막대가 나타남")]
    public float hintStartLeft = 120f;
    [Tooltip("이 거리(m) 안에 틱토이 플레이어가 있으면 막대 가득")]
    public float hintFullDistance = 10f;
    [Tooltip("이 거리(m)보다 멀면 막대 비어 있음")]
    public float hintEmptyDistance = 35f;

    static AbilitySettings current;

    /// <summary>지금 쓰는 설정 (에셋이 없으면 기본값)</summary>
    public static AbilitySettings Current
    {
        get
        {
            if (current == null)
                current = Resources.Load<AbilitySettings>("AbilitySettings");
            if (current == null)
                current = CreateInstance<AbilitySettings>();
            return current;
        }
    }

    /// <summary>레벨별 값 (Lv1 → [0], Lv3 이상 → 마지막)</summary>
    public static T ByLevel<T>(T[] values, int level) => values[Mathf.Clamp(level, 1, values.Length) - 1];
}
