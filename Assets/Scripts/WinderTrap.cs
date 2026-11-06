using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>와인더 덫. 플레이어 틱토이만 걸림. 틱토이 시점에선 월드Y로 가라앉아 이빨만 보이게.</summary>
public class WinderTrap : MonoBehaviour
{
    public const float HoldSeconds = 5f;
    public const float PlaceBlockRadius = 1.15f;

    static int tickToyViewFrame = -1;
    static int winderViewFrame = -1;

    bool armed;
    bool sprung;
    readonly HashSet<int> ignoredUntilExit = new HashSet<int>();
    SphereCollider catchTrigger;
    Vector3 seatedPos;
    float buryDepth = 0.25f;
    bool seatReady;

    public Vector3 PlaceCenter => seatedPos;

    /// <summary>온라인 덫 번호 (0 = 오프라인). 같은 덫을 모든 화면에서 찾기 위함</summary>
    public int NetId;

    /// <summary>다른 화면에서 이 덫에 누가 걸렸음 → 내 화면의 같은 덫도 닫히고 사라짐</summary>
    public static void RemoteSprung(int id)
    {
        WinderTrap[] traps = FindObjectsByType<WinderTrap>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < traps.Length; i++)
        {
            WinderTrap trap = traps[i];
            if (trap == null || trap.NetId != id || trap.sprung)
                continue;
            trap.sprung = true;
            trap.armed = false;
            trap.ApplyViewerPose();
            trap.StartCoroutine(trap.ReleaseAndDestroy(null));
        }
    }

    /// <summary>틱토이 카메라가 이번 프레임에 활성임을 알림.</summary>
    public static void ReportTickToyViewing()
    {
        tickToyViewFrame = Time.frameCount;
    }

    /// <summary>와인더 카메라가 이번 프레임에 활성임을 알림.</summary>
    public static void ReportWinderViewing()
    {
        winderViewFrame = Time.frameCount;
    }

    public static bool IsTickToyViewing()
    {
        // MainCamera가 누구 자식인지가 가장 확실
        Camera main = Camera.main;
        if (main != null)
        {
            TickToyMove mainToy = main.GetComponentInParent<TickToyMove>();
            if (mainToy != null && mainToy.enabled)
                return true;
            if (main.GetComponentInParent<WinderMove>() != null)
                return false;
        }

        bool tick = tickToyViewFrame == Time.frameCount;
        bool wind = winderViewFrame == Time.frameCount;
        if (tick && !wind)
            return true;
        if (wind && !tick)
            return false;
        if (tick && wind)
        {
            float td = MaxCameraDepthForType(true);
            float wd = MaxCameraDepthForType(false);
            return td >= wd;
        }

        return MaxCameraDepthForType(true) > MaxCameraDepthForType(false);
    }

    static float MaxCameraDepthForType(bool tickToy)
    {
        float best = float.NegativeInfinity;
        bool found = false;
        if (tickToy)
        {
            TickToyMove[] toys = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < toys.Length; i++)
            {
                if (toys[i] == null || !toys[i].enabled)
                    continue;
                Camera[] cams = toys[i].GetComponentsInChildren<Camera>(false);
                for (int c = 0; c < cams.Length; c++)
                {
                    if (cams[c] == null || !cams[c].enabled || !cams[c].gameObject.activeInHierarchy)
                        continue;
                    found = true;
                    if (cams[c].depth > best)
                        best = cams[c].depth;
                }
            }
        }
        else
        {
            WinderMove[] winders = FindObjectsByType<WinderMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < winders.Length; i++)
            {
                if (winders[i] == null || !winders[i].enabled)
                    continue;
                Camera[] cams = winders[i].GetComponentsInChildren<Camera>(false);
                for (int c = 0; c < cams.Length; c++)
                {
                    if (cams[c] == null || !cams[c].enabled || !cams[c].gameObject.activeInHierarchy)
                        continue;
                    found = true;
                    if (cams[c].depth > best)
                        best = cams[c].depth;
                }
            }
        }

        return found ? best : float.NegativeInfinity;
    }

    public void Arm(SphereCollider trigger, float worldHeight)
    {
        catchTrigger = trigger;
        seatedPos = transform.position;
        // 틱토이 시점에 이빨이 조금 더 보이게 (너무 깊지 않게)
        buryDepth = Mathf.Max(0.08f, worldHeight * 0.72f);
        seatReady = true;
        armed = false;
        sprung = false;
        ignoredUntilExit.Clear();
        StopAllCoroutines();
        StartCoroutine(ArmAfterPhysics());
        ApplyViewerPose();
    }

    void Start()
    {
        if (!seatReady)
        {
            seatedPos = transform.position;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            float height = 0.3f;
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        b.Encapsulate(renderers[i].bounds);
                }
                height = Mathf.Max(0.05f, b.size.y);
            }

            buryDepth = Mathf.Max(0.08f, height * 0.72f);
            if (catchTrigger == null)
                catchTrigger = GetComponentInChildren<SphereCollider>();
            seatReady = true;
        }
    }

    void LateUpdate()
    {
        if (seatReady)
            ApplyViewerPose();
    }

    void ApplyViewerPose()
    {
        // 걸리면 틱토이 시점에서도 브릭처럼 완전 노출
        bool bury = !sprung && IsTickToyViewing();
        float y = seatedPos.y - (bury ? buryDepth : 0f);
        transform.position = new Vector3(seatedPos.x, y, seatedPos.z);

        // 트리거는 항상 지표면 근처(와인더가 앉힌 위치)에 유지
        if (catchTrigger != null)
            catchTrigger.transform.position = new Vector3(seatedPos.x, seatedPos.y + 0.12f, seatedPos.z);
    }

    IEnumerator ArmAfterPhysics()
    {
        yield return new WaitForFixedUpdate();
        yield return null;
        CollectCurrentOverlapsAsIgnored();
        armed = true;
    }

    void CollectCurrentOverlapsAsIgnored()
    {
        ignoredUntilExit.Clear();
        if (catchTrigger == null)
            return;

        Vector3 center = catchTrigger.bounds.center;
        float radius = catchTrigger.bounds.extents.magnitude;
        Collider[] hits = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null || hit == catchTrigger)
                continue;
            TickToyMove toy = hit.GetComponentInParent<TickToyMove>();
            if (toy != null && toy.enabled)
                ignoredUntilExit.Add(hit.GetInstanceID());
        }
    }

    void OnTriggerEnter(Collider other) => TryCatch(other);
    void OnTriggerStay(Collider other) => TryCatch(other);

    void OnTriggerExit(Collider other)
    {
        if (other == null)
            return;
        ignoredUntilExit.Remove(other.GetInstanceID());
    }

    void TryCatch(Collider other)
    {
        if (!armed || sprung || other == null)
            return;
        if (ignoredUntilExit.Contains(other.GetInstanceID()))
            return;

        // AI 틱토이(맵 AI·분신)는 밟아도 아무 일 없음 — 플레이어 틱토이만 걸림
        if (other.GetComponentInParent<TickToyAI>() != null)
            return;

        TickToyMove toy = other.GetComponentInParent<TickToyMove>();
        if (toy == null || !toy.enabled || toy.IsDead || toy.IsTrapFrozen)
            return;

        BeginHold(toy);
    }

    void BeginHold(TickToyMove toy)
    {
        Transform victim = toy.transform;
        sprung = true;
        armed = false;
        ApplyViewerPose(); // 걸린 즉시 틱토이 시점에서도 덫 전체 노출

        Vector3 center = seatedPos;
        center.y = victim.position.y;
        SnapVictim(victim, center);
        TrapOutlineUtil.Apply(victim);
        toy.TrapFreeze(HoldSeconds);
        StartCoroutine(ReleaseAndDestroy(victim));
        if (NetId != 0)
            FusionMatchBackend.Online?.SendTrapSprung(NetId);
    }

    static void SnapVictim(Transform victim, Vector3 center)
    {
        victim.position = center;
        Rigidbody rb = victim.GetComponent<Rigidbody>();
        if (rb == null)
            return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = center;
        if (!rb.isKinematic)
            rb.MovePosition(center);
    }

    IEnumerator ReleaseAndDestroy(Transform victim)
    {
        yield return new WaitForSeconds(HoldSeconds);
        if (victim != null)
            TrapOutlineUtil.Clear(victim);
        Destroy(gameObject);
    }

    public static bool IsPlacementBlocked(Vector3 worldPos, float blockRadius = PlaceBlockRadius)
    {
        WinderTrap[] traps = FindObjectsByType<WinderTrap>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        float r2 = blockRadius * blockRadius;
        for (int i = 0; i < traps.Length; i++)
        {
            WinderTrap trap = traps[i];
            if (trap == null || !trap.isActiveAndEnabled)
                continue;
            Vector3 other = trap.PlaceCenter;
            float dx = other.x - worldPos.x;
            float dz = other.z - worldPos.z;
            if (dx * dx + dz * dz < r2)
                return true;
        }

        return false;
    }
}
