using System.Collections.Generic;
using UnityEngine;

/// <summary>캐릭터 한 명의 보이는 상태 (위치·방향·애니메이션 레이어별 상태·파라미터)</summary>
public struct NetPose
{
    public const byte KindTickToy = 0;
    public const byte KindWinder = 1;
    public const byte KindAI = 2;
    public const int MaxLayers = 6;
    public const int MaxParams = 12;

    // 상태 깃발 (다른 사람 화면에 똑같이 보이게)
    public const int FlagFrozen = 1;   // 와인더: 태엽 정지당함
    public const int FlagRunning = 2;  // 달리기 능력 중
    public const int FlagGolden = 4;   // 와인더: 황금 몽둥이
    public const int FlagTrapped = 8;  // 틱토이: 덫에 걸림
    public const int FlagHitFace = 16; // 틱토이·AI: 맞아서 잠깐 @@ 표정
    public const int FlagFainted = 32; // 틱토이: 기절한 척하기 중 (@@ 표정)
    public const int FlagSpringPaused = 64; // 와인더: 황금 몽둥이·덫을 쓰기 전이라 등 태엽 멈춤

    public int id;
    public byte kind;
    public byte ch;
    public bool alive;
    public int flags;
    public Vector3 pos;
    public float yaw;
    public int[] hashes;   // 레이어별 현재 상태 (점프·공격·감정표현 포함)
    public float[] weights; // 레이어 1~3 가중치 (0번은 항상 1)
    public float[] prm;

    static readonly Dictionary<int, AnimatorControllerParameter[]> ParamCache = new Dictionary<int, AnimatorControllerParameter[]>();
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

    public static NetPose Capture(GameObject go, int id, byte kind, byte ch, bool alive, int flags = 0)
    {
        NetPose p = new NetPose
        {
            id = id,
            kind = kind,
            ch = ch,
            alive = alive,
            flags = flags,
            pos = go.transform.position,
            yaw = go.transform.eulerAngles.y,
            hashes = new int[MaxLayers],
            weights = new float[MaxLayers],
            prm = System.Array.Empty<float>()
        };

        Animator a = FindAnimator(go);
        if (a != null && a.isActiveAndEnabled)
        {
            int layers = Mathf.Min(MaxLayers, a.layerCount);
            for (int l = 0; l < layers; l++)
            {
                AnimatorStateInfo st = a.IsInTransition(l) ? a.GetNextAnimatorStateInfo(l) : a.GetCurrentAnimatorStateInfo(l);
                p.hashes[l] = st.fullPathHash;
                p.weights[l] = l == 0 ? 1f : a.GetLayerWeight(l);
            }

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
}

/// <summary>
/// 다른 사람(또는 방장 쪽 AI) 캐릭터를 보여주는 복사본. 조작 스크립트는 빼고, NetCharacter 가 받은 상태로 움직임.
/// - 위치: 부드럽게 따라감
/// - 애니메이션: 레이어마다 상태가 바뀌는 즉시 그 상태로 (점프·공격·감정표현이 빠지지 않게)
/// - 태엽: 틱토이 등 태엽은 감는 중일 때만 보이게(원래 TickToyAnimation 과 같음), 와인더 등 태엽은 계속 돌아감
/// </summary>
public class NetPuppet : MonoBehaviour
{
    public int NetId { get; private set; }
    public byte Kind { get; private set; }
    public byte Ch { get; private set; }
    public bool Alive { get; private set; } = true;
    /// <summary>덫에 걸린 상태 (빨간 테두리를 와인더 Z 표시가 지우지 않도록)</summary>
    public bool IsTrapped => (flags & NetPose.FlagTrapped) != 0;
    /// <summary>와인더: 틱토이가 E 로 태엽을 멈춘 상태</summary>
    public bool IsFrozen => (flags & NetPose.FlagFrozen) != 0;
    public bool IsTickToyLike => Kind == NetPose.KindTickToy || Kind == NetPose.KindAI;

    Animator animator;
    Vector3 targetPos;
    float targetYaw;
    readonly int[] targetHash = new int[NetPose.MaxLayers];
    readonly int[] appliedHash = new int[NetPose.MaxLayers];
    readonly float[] mismatch = new float[NetPose.MaxLayers];
    bool hasTarget;

    GameObject[] spineSprings;
    bool springsOn;
    Transform winderSpring;
    Quaternion winderSpringBase;
    float winderSpringDeg;
    int windingParam;
    int flags;
    Transform normalWeapon;
    GameObject goldWeaponPrefab;
    GameObject goldWeapon;
    ParticleSystem[] runParticles;

    /// <summary>와인더 등 태엽 (틱토이가 등 뒤에서 E 로 멈출 때 위치 판정)</summary>
    public Transform Spring => winderSpring;

    public static NetPuppet Create(GameObject prefab, NetPose pose)
    {
        if (prefab == null)
            return null;

        GameObject holder = new GameObject("PuppetHolder");
        holder.SetActive(false);
        GameObject go = Instantiate(prefab, pose.pos, Quaternion.Euler(0f, pose.yaw, 0f), holder.transform);

        // 황금 몽둥이 모양 (와인더 프리팹에 연결된 것)
        WinderMove prefabWinder = prefab.GetComponent<WinderMove>();
        GameObject goldPrefab = prefabWinder != null ? prefabWinder.GoldWeaponPrefab : null;

        // 팔 메시 분리(겨드랑이 보정)는 보이는 모양이라 유지
        bool armpitFix = false;
        foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb != null && mb.GetType().Name.EndsWith("ModelController"))
                armpitFix = true;
        }

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
        p.Ch = pose.ch;
        p.animator = NetPose.FindAnimator(go);
        if (p.animator != null)
        {
            p.animator.applyRootMotion = false;
            p.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (armpitFix)
                ArmpitSeparator.Apply(p.animator);
            p.windingParam = HasParam(p.animator, "IsWinding") ? Animator.StringToHash("IsWinding") : 0;
        }
        p.CacheSprings();
        p.CacheFaces();
        p.goldWeaponPrefab = goldPrefab;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
        {
            if (p.normalWeapon == null && t.name.StartsWith("BrickWeapon"))
                p.normalWeapon = t;
            if (t.name == "RunWhoosh")
                p.runParticles = t.GetComponentsInChildren<ParticleSystem>(true);
        }
        go.name = (pose.kind == NetPose.KindWinder ? "NetWinder_" : pose.kind == NetPose.KindAI ? "NetAI_" : "NetTickToy_") + pose.id;
        p.SetTarget(pose);
        p.transform.position = pose.pos;
        return p;
    }

    static bool HasParam(Animator a, string name)
    {
        foreach (AnimatorControllerParameter p in a.parameters)
        {
            if (p.name == name)
                return true;
        }
        return false;
    }

    void CacheSprings()
    {
        List<GameObject> springs = new List<GameObject>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (Kind == NetPose.KindWinder)
            {
                if (winderSpring == null && t.name == "Spring")
                {
                    winderSpring = t;
                    winderSpringBase = t.localRotation;
                }
                continue;
            }
            if (t.name != "Spine")
                continue;
            foreach (Transform child in t)
            {
                if (child.name.StartsWith("Spring"))
                    springs.Add(child.gameObject);
            }
        }
        spineSprings = springs.ToArray();
        SetSpringsActive(false);
    }

    // ---------- 얼굴 (TickToyAnimation 과 같은 규칙: Face_xxx 렌더러 중 하나만 보이게) ----------
    readonly Dictionary<string, List<Renderer>> faces = new Dictionary<string, List<Renderer>>();
    string currentFace;
    int[] danceHashes = System.Array.Empty<int>();

    void CacheFaces()
    {
        faces.Clear();
        currentFace = null;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            int i = r.name.IndexOf("Face_", System.StringComparison.Ordinal);
            if (i < 0)
                continue;
            string key = r.name.Substring(i + 5);
            if (!faces.TryGetValue(key, out List<Renderer> list))
                faces[key] = list = new List<Renderer>();
            list.Add(r);
        }

        // 댄스 상태 해시 (레이어 이름.Dance1 / Dance2)
        if (animator != null)
        {
            List<int> hs = new List<int>();
            for (int l = 0; l < animator.layerCount; l++)
            {
                string layer = animator.GetLayerName(l);
                hs.Add(Animator.StringToHash(layer + ".Dance1"));
                hs.Add(Animator.StringToHash(layer + ".Dance2"));
            }
            danceHashes = hs.ToArray();
        }
        SetFace("Default");
    }

    void SetFace(string key)
    {
        if (faces.Count == 0 || key == currentFace)
            return;
        if (!faces.ContainsKey(key))
            key = "Default";
        foreach (KeyValuePair<string, List<Renderer>> kv in faces)
        {
            foreach (Renderer r in kv.Value)
            {
                if (r != null)
                    r.enabled = kv.Key == key;
            }
        }
        currentFace = key;
    }

    float danceTime;

    void UpdateFace()
    {
        if (faces.Count == 0)
            return;
        bool dancing = false;
        for (int l = 0; l < NetPose.MaxLayers && !dancing; l++)
        {
            for (int d = 0; d < danceHashes.Length; d++)
            {
                if (targetHash[l] != 0 && targetHash[l] == danceHashes[d])
                {
                    dancing = true;
                    break;
                }
            }
        }

        string want = "Default";
        if (!Alive || (flags & (NetPose.FlagHitFace | NetPose.FlagFainted)) != 0)
            want = "Dizzy";
        else if (dancing)
        {
            danceTime += Time.deltaTime;
            want = ((int)(danceTime / 0.35f) % 2 == 0) ? "Happy" : "Surprised";
        }
        else
            danceTime = 0f;
        SetFace(want);
    }

    void SetSpringsActive(bool on)
    {
        springsOn = on;
        if (spineSprings == null)
            return;
        for (int i = 0; i < spineSprings.Length; i++)
        {
            if (spineSprings[i] != null && spineSprings[i].activeSelf != on)
                spineSprings[i].SetActive(on);
        }
    }

    public void SetTarget(NetPose pose)
    {
        targetPos = pose.pos;
        targetYaw = pose.yaw;
        Alive = pose.alive;
        ApplyFlags(pose.flags);

        if (animator != null && animator.isActiveAndEnabled)
        {
            pose.ApplyParams(animator);
            int layers = Mathf.Min(NetPose.MaxLayers, animator.layerCount);
            for (int l = 0; l < layers; l++)
            {
                if (l > 0 && pose.weights != null)
                    animator.SetLayerWeight(l, pose.weights[l]);
                int h = pose.hashes != null ? pose.hashes[l] : 0;
                if (h != 0 && h != targetHash[l])
                {
                    targetHash[l] = h;
                    // 새 동작 시작(점프·공격·감정표현 등) → 바로 맞춤
                    if (CurrentOrNext(l) != h && animator.HasState(l, h))
                        animator.CrossFadeInFixedTime(h, 0.08f, l);
                    appliedHash[l] = h;
                    mismatch[l] = 0f;
                }
            }

            bool winding = windingParam != 0 && animator.GetBool(windingParam);
            if (winding != springsOn)
                SetSpringsActive(winding);
        }

        if (!hasTarget || (transform.position - targetPos).sqrMagnitude > 25f)
        {
            transform.position = targetPos;
            transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
        }
        hasTarget = true;
    }

    /// <summary>정지·달리기·황금 몽둥이·덫 상태를 그 사람 화면과 똑같이</summary>
    void ApplyFlags(int next)
    {
        int prev = flags;
        flags = next;
        bool Has(int f, int v) => (v & f) != 0;

        // 태엽 정지: 애니메이션 멈춤
        if (animator != null)
            animator.speed = Has(NetPose.FlagFrozen, next) ? 0f : 1f;

        // 달리기 능력 쓰는 순간 슝 이펙트
        if (Has(NetPose.FlagRunning, next) && !Has(NetPose.FlagRunning, prev) && runParticles != null)
        {
            for (int i = 0; i < runParticles.Length; i++)
            {
                runParticles[i].Clear(true);
                runParticles[i].Play(true);
            }
        }

        // 황금 몽둥이
        bool gold = Has(NetPose.FlagGolden, next);
        if (gold != Has(NetPose.FlagGolden, prev) || (gold && goldWeapon == null))
            SetGolden(gold);

        // 덫에 걸림 → 빨간 테두리
        bool trapped = Has(NetPose.FlagTrapped, next);
        if (trapped != Has(NetPose.FlagTrapped, prev))
        {
            if (trapped)
                TrapOutlineUtil.Apply(transform);
            else
                TrapOutlineUtil.Clear(transform);
        }
    }

    void SetGolden(bool gold)
    {
        if (normalWeapon == null)
            return;
        if (gold && goldWeapon == null && goldWeaponPrefab != null)
        {
            goldWeapon = Instantiate(goldWeaponPrefab, normalWeapon.parent);
            goldWeapon.name = "BrickGoldWeapon";
            goldWeapon.transform.localPosition = normalWeapon.localPosition;
            goldWeapon.transform.localRotation = normalWeapon.localRotation;
            goldWeapon.transform.localScale = normalWeapon.localScale;
            foreach (Collider c in goldWeapon.GetComponentsInChildren<Collider>(true))
                c.enabled = false;
        }
        if (goldWeapon != null)
            goldWeapon.SetActive(gold);
        normalWeapon.gameObject.SetActive(!gold || goldWeapon == null);
    }

    int CurrentOrNext(int layer)
    {
        if (animator.IsInTransition(layer))
            return animator.GetNextAnimatorStateInfo(layer).fullPathHash;
        return animator.GetCurrentAnimatorStateInfo(layer).fullPathHash;
    }

    void Update()
    {
        if (!hasTarget)
            return;

        float k = 1f - Mathf.Exp(-18f * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), k);

        // 상태가 어긋난 채로 남아 있으면 다시 맞춤
        if (animator != null && animator.isActiveAndEnabled)
        {
            int layers = Mathf.Min(NetPose.MaxLayers, animator.layerCount);
            for (int l = 0; l < layers; l++)
            {
                int h = targetHash[l];
                if (h == 0 || CurrentOrNext(l) == h)
                {
                    mismatch[l] = 0f;
                    continue;
                }
                mismatch[l] += Time.deltaTime;
                if (mismatch[l] > 0.2f && animator.HasState(l, h))
                {
                    animator.CrossFadeInFixedTime(h, 0.1f, l);
                    mismatch[l] = 0f;
                }
            }
        }

        UpdateFace();

        // 와인더 등 태엽은 천천히 계속 돌아감 (10초에 한 바퀴, 문이 열린 뒤부터)
        if (winderSpring != null && Alive && RoundManager.DoorOpen && (flags & (NetPose.FlagSpringPaused | NetPose.FlagFrozen)) == 0) // 문 열리기 전·능력 쓰기 전·태엽 정지당함 → 멈춤
        {
            winderSpringDeg = Mathf.Repeat(winderSpringDeg + 36f * Time.deltaTime, 360f);
            winderSpring.localRotation = winderSpringBase * Quaternion.Euler(0f, -winderSpringDeg, 0f);
        }
    }
}
