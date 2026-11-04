using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Ear"로 시작하는 보조 본(귀)을 머리 움직임에 따라 살짝 늦게 따라오며 흔들리게 + 가만히 있어도 조금씩 살랑거림.
/// 애니메이션 클립은 건드리지 않음 (애니메이션 적용 후 LateUpdate에서 귀 본만 회전).
/// </summary>
public class EarWobble : MonoBehaviour
{
    [SerializeField] float stiffness = 90f;      // 클수록 빨리 제자리로
    [SerializeField] float damping = 9f;         // 클수록 덜 출렁임
    [SerializeField] float maxAngle = 28f;       // 최대 휘는 각도
    [SerializeField] float idleAngle = 5f;       // 가만히 있을 때 살랑거림 크기
    [SerializeField] float idleSpeed = 1.7f;

    class Ear
    {
        public Transform bone, head;
        public Quaternion restRelHead;   // 머리 기준 기본 회전
        public Vector3 restDirHead;      // 머리 기준 귀 방향
        public float length, phase;
        public Vector3 tip, vel;
    }
    readonly List<Ear> ears = new List<Ear>();

    void Start()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Ear") || t.parent == null) continue;
            var e = new Ear { bone = t, head = t.parent, length = 0.6f * t.lossyScale.y / Mathf.Max(0.0001f, transform.lossyScale.y) * transform.lossyScale.y };
            e.restRelHead = Quaternion.Inverse(e.head.rotation) * t.rotation;
            e.restDirHead = Quaternion.Inverse(e.head.rotation) * (t.rotation * Vector3.up);
            e.length = Mathf.Max(0.05f, 0.6f * transform.lossyScale.y);
            e.phase = t.name.EndsWith("R") ? 1.3f : 0f;
            e.tip = t.position + e.head.rotation * e.restDirHead * e.length;
            ears.Add(e);
        }
    }

    void LateUpdate()
    {
        float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.05f);
        foreach (var e in ears)
        {
            if (e.bone == null) continue;
            Quaternion restRot = e.head.rotation * e.restRelHead;
            Vector3 restDir = e.head.rotation * e.restDirHead;
            float sway = Mathf.Sin(Time.time * idleSpeed + e.phase) * idleAngle * Mathf.Deg2Rad;
            Vector3 target = e.bone.position + (restDir + e.head.right * sway).normalized * e.length;
            Vector3 acc = (target - e.tip) * stiffness - e.vel * damping;
            e.vel += acc * dt;
            e.tip += e.vel * dt;
            Vector3 dir = (e.tip - e.bone.position).normalized;
            if (Vector3.Angle(restDir, dir) > maxAngle)
                dir = Vector3.Slerp(restDir, dir, maxAngle / Vector3.Angle(restDir, dir));
            e.tip = e.bone.position + dir * e.length;
            e.bone.rotation = Quaternion.FromToRotation(restDir, dir) * restRot;
        }
    }
}
