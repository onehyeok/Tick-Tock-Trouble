using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 와인더 Z "의심 표시": 가리킨 틱토이(사람·AI)에 3초 동안 빨간 테두리 (덫과 같은 테두리, 모두의 화면에).
/// 표시된 틱토이가 그 사이 변장해서 모형이 바뀌어도 새 모형에 테두리를 다시 그려서 3초 동안 유지.
/// </summary>
public static class SuspectMarks
{
    public const float ShowSeconds = 3f;

    class Mark
    {
        public int netId;          // 온라인: 캐릭터 id (변장·복사본이 바뀌어도 같은 id)
        public GameObject obj;     // 오프라인: 그 AI 오브젝트
        public float until;
        public GameObject applied; // 지금 테두리를 그린 오브젝트
    }

    static readonly List<Mark> marks = new List<Mark>();

    public static void Add(int netId, GameObject obj)
    {
        Mark m = marks.Find(x => netId != 0 ? x.netId == netId : x.obj == obj);
        if (m == null)
        {
            m = new Mark { netId = netId, obj = obj };
            marks.Add(m);
        }
        m.until = Time.time + ShowSeconds;
    }

    public static void Clear()
    {
        marks.Clear();
    }

    /// <summary>매 프레임 (RoundManager): 테두리 유지 / 3초 지나면 지움</summary>
    public static void Tick()
    {
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            Mark m = marks[i];
            GameObject target = m.netId != 0 ? FusionMatchBackend.Online?.CharacterObject(m.netId) : m.obj;
            bool expired = Time.time >= m.until;

            // 시간이 끝났거나 모형이 바뀌었으면 (변장) 이전 테두리 지움
            if (m.applied != null && (expired || m.applied != target))
                ClearUnlessTrapped(m.applied);
            if (m.applied != null && (expired || m.applied != target))
                m.applied = null;
            if (expired)
            {
                marks.RemoveAt(i);
                continue;
            }

            if (target == null || !target.activeInHierarchy)
                continue;
            if (target.GetComponent<TrapOutlineMarker>() == null)
                TrapOutlineUtil.Apply(target.transform);
            m.applied = target;
        }
    }

    static void ClearUnlessTrapped(GameObject go)
    {
        if (go == null)
            return;
        TickToyMove toy = go.GetComponent<TickToyMove>();
        NetPuppet puppet = go.GetComponent<NetPuppet>();
        if ((toy != null && toy.IsTrapFrozen) || (puppet != null && puppet.IsTrapped))
            return; // 덫에 걸려 있으면 덫 테두리는 그대로
        TrapOutlineUtil.Clear(go.transform);
    }
}
