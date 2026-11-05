using System.Collections.Generic;
using UnityEngine;

/// <summary>네트워크로 받은 위치·애니메이션을 그대로 보여주는 "다른 사람" 캐릭터 (조작 스크립트는 다 뺀 복사본)</summary>
public struct NetPose
{
    public const byte KindTickToy = 0;
    public const byte KindWinder = 1;
    public const byte KindAI = 2;

    public int id;
    public byte kind;
    public byte ch;
    public bool alive;
    public Vector3 pos;
    public float yaw;
    public int hash;
    public float[] prm;

    public bool IsTickToyLike => kind == KindTickToy || kind == KindAI;

    static readonly Dictionary<int, AnimatorControllerParameter[]> ParamCache = new Dictionary<int, AnimatorControllerParameter[]>();
    const int MaxParams = 12;
    static readonly List<float> CaptureBuffer = new List<float>(MaxParams);

    public static Animator FindAnimator(GameObject go)
    {
        if (go == null)
            return null;
        foreach (Animator a in go.GetComponentsInChildren<Animator>(true))
        {
            if (a.runtimeAnimatorController != null)
                return a;
        }
        return null;
    }

    static AnimatorControllerParameter[] Params(Animator a)
    {
        int key = a.GetInstanceID();
        if (!ParamCache.TryGetValue(key, out AnimatorControllerParameter[] ps) || ps == null)
        {
            ps = a.parameters;
            ParamCache[key] = ps;
        }
        return ps;
    }

    public static NetPose Capture(GameObject go, int id, byte kind, byte ch, bool alive)
    {
        NetPose p = new NetPose
        {
            id = id,
            kind = kind,
            ch = ch,
            alive = alive,
            pos = go.transform.position,
            yaw = go.transform.eulerAngles.y,
            prm = System.Array.Empty<float>()
        };

        Animator a = FindAnimator(go);
        if (a != null && a.isActiveAndEnabled)
        {
            AnimatorStateInfo st = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
            p.hash = st.fullPathHash;
            AnimatorControllerParameter[] ps = Params(a);
            List<float> values = CaptureBuffer;
            values.Clear();
            for (int i = 0; i < ps.Length && values.Count < MaxParams; i++)
            {
                switch (ps[i].type)
                {
                    case AnimatorControllerParameterType.Float:
                        values.Add(a.GetFloat(ps[i].nameHash));
                        break;
                    case AnimatorControllerParameterType.Int:
                        values.Add(a.GetInteger(ps[i].nameHash));
                        break;
                    case AnimatorControllerParameterType.Bool:
                        values.Add(a.GetBool(ps[i].nameHash) ? 1f : 0f);
                        break;
                }
            }
            p.prm = values.ToArray();
        }
        return p;
    }

    public void ApplyParams(Animator a)
    {
        if (a == null || prm == null)
            return;
        AnimatorControllerParameter[] ps = Params(a);
        int k = 0;
        for (int i = 0; i < ps.Length && k < prm.Length; i++)
        {
            switch (ps[i].type)
            {
                case AnimatorControllerParameterType.Float:
                    a.SetFloat(ps[i].nameHash, prm[k++]);
                    break;
                case AnimatorControllerParameterType.Int:
                    a.SetInteger(ps[i].nameHash, Mathf.RoundToInt(prm[k++]));
                    break;
                case AnimatorControllerParameterType.Bool:
                    a.SetBool(ps[i].nameHash, prm[k++] > 0.5f);
                    break;
            }
        }
    }

    public void Write(System.IO.BinaryWriter w)
    {
        w.Write(id);
        w.Write(kind);
        w.Write(ch);
        w.Write(alive);
        w.Write(pos.x);
        w.Write(pos.y);
        w.Write(pos.z);
        w.Write((ushort)Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 360f * 65535f));
        w.Write(hash);
        int n = prm != null ? Mathf.Min(prm.Length, MaxParams) : 0;
        w.Write((byte)n);
        for (int i = 0; i < n; i++)
            w.Write((short)Mathf.Clamp(Mathf.RoundToInt(prm[i] * 100f), short.MinValue, short.MaxValue));
    }

    public static NetPose Read(System.IO.BinaryReader r)
    {
        NetPose p = new NetPose();
        p.id = r.ReadInt32();
        p.kind = r.ReadByte();
        p.ch = r.ReadByte();
        p.alive = r.ReadBoolean();
        p.pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        p.yaw = r.ReadUInt16() / 65535f * 360f;
        p.hash = r.ReadInt32();
        int n = r.ReadByte();
        p.prm = new float[n];
        for (int i = 0; i < n; i++)
            p.prm[i] = r.ReadInt16() / 100f;
        return p;
    }
}

public class NetPuppet : MonoBehaviour
{
    public int NetId { get; private set; }
    public byte Kind { get; private set; }
    public bool Alive { get; private set; } = true;
    public bool IsTickToyLike => Kind == NetPose.KindTickToy || Kind == NetPose.KindAI;
    public float LastSeen { get; private set; }

    Animator animator;
    Vector3 targetPos;
    float targetYaw;
    int targetHash;
    float mismatchTime;
    bool hasTarget;

    /// <summary>프리팹 복사본에서 조작/카메라/소리 리스너를 빼고 보여주기용으로 만듦</summary>
    public static NetPuppet Create(GameObject prefab, NetPose pose)
    {
        if (prefab == null)
            return null;

        GameObject holder = new GameObject("PuppetHolder");
        holder.SetActive(false);
        GameObject go = Instantiate(prefab, pose.pos, Quaternion.Euler(0f, pose.yaw, 0f), holder.transform);

        // 조작 스크립트 제거 (RequireComponent 때문에 몇 번 반복)
        for (int pass = 0; pass < 4; pass++)
        {
            bool any = false;
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null)
                    continue;
                DestroyImmediate(mb);
                any = true;
            }
            if (!any)
                break;
        }
        foreach (AudioListener l in go.GetComponentsInChildren<AudioListener>(true))
            DestroyImmediate(l);
        foreach (Camera c in go.GetComponentsInChildren<Camera>(true))
            DestroyImmediate(c);
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        go.transform.SetParent(null, true);
        Destroy(holder);

        NetPuppet p = go.AddComponent<NetPuppet>();
        p.NetId = pose.id;
        p.Kind = pose.kind;
        p.animator = NetPose.FindAnimator(go);
        if (p.animator != null)
        {
            p.animator.applyRootMotion = false;
            p.animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }
        go.name = (pose.kind == NetPose.KindWinder ? "NetWinder_" : pose.kind == NetPose.KindAI ? "NetAI_" : "NetTickToy_") + pose.id;
        p.SetTarget(pose);
        p.transform.position = pose.pos;
        return p;
    }

    public void SetTarget(NetPose pose)
    {
        LastSeen = Time.unscaledTime;
        targetPos = pose.pos;
        targetYaw = pose.yaw;
        targetHash = pose.hash;
        Alive = pose.alive;
        pose.ApplyParams(animator);
        if (!hasTarget || (transform.position - targetPos).sqrMagnitude > 25f)
        {
            transform.position = targetPos;
            transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
        }
        hasTarget = true;
    }

    void Update()
    {
        if (!hasTarget)
            return;

        float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), k);

        // 애니메이션 상태가 계속 다르면 그 상태로 맞춤 (공격·죽음 등 트리거 동작)
        if (animator != null && targetHash != 0 && animator.isActiveAndEnabled)
        {
            int cur = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            int next = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0).fullPathHash : 0;
            if (cur != targetHash && next != targetHash)
            {
                mismatchTime += Time.deltaTime;
                if (mismatchTime > 0.1f && animator.HasState(0, targetHash))
                {
                    animator.CrossFadeInFixedTime(targetHash, 0.1f, 0);
                    mismatchTime = 0f;
                }
            }
            else
                mismatchTime = 0f;
        }
    }
}
