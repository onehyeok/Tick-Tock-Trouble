using UnityEngine;

/// <summary>
/// Winder/TickToy가 정적 콜라이더에 끼어 입력이 먹히지 않는 상태를 풀어 줍니다.
/// </summary>
public static class PlayerUnstickUtil
{
    static readonly Collider[] Overlaps = new Collider[32];

    public static void Resolve(
        Rigidbody rb,
        Collider body,
        Transform self,
        Vector3 wishPlanar,
        bool allowVerticalAssist = true)
    {
        if (rb == null || body == null || self == null || rb.isKinematic)
            return;

        GetProbe(body, rb.position, self, out Vector3 center, out Vector3 half);
        int count = Physics.OverlapBoxNonAlloc(
            center, half, Overlaps, self.rotation, ~0, QueryTriggerInteraction.Ignore);
        if (count <= 0)
            return;

        Vector3 push = Vector3.zero;
        int hits = 0;
        for (int i = 0; i < count; i++)
        {
            Collider other = Overlaps[i];
            if (!IsBlockingCollider(other, body, self))
                continue;

            if (!Physics.ComputePenetration(
                    body, rb.position, self.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 dir, out float dist))
                continue;

            if (dist < 0.001f)
                continue;

            // 지면처럼 위로만 밀면 수평 탈출도 보조
            if (dir.y > 0.7f)
            {
                if (!allowVerticalAssist)
                    continue;

                Vector3 flat = wishPlanar;
                flat.y = 0f;
                if (flat.sqrMagnitude < 0.01f)
                {
                    // ClosestPoint는 볼록하지 않은 메쉬(길·건물)에서 쓰면 경고 폭주 → 그땐 바운즈 기준
                    bool supported = !(other is MeshCollider mc) || mc.convex;
                    Vector3 near = supported ? other.ClosestPoint(center) : other.bounds.ClosestPoint(center);
                    Vector3 away = center - near;
                    away.y = 0f;
                    flat = away;
                }

                if (flat.sqrMagnitude > 0.0001f)
                {
                    flat.Normalize();
                    push += flat * (dist + 0.04f);
                    hits++;
                }

                continue;
            }

            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                continue;
            dir.Normalize();
            push += dir * (dist + 0.04f);
            hits++;
        }

        if (hits <= 0 || push.sqrMagnitude < 0.00001f)
            return;

        if (push.sqrMagnitude > 2.25f)
            push = push.normalized * 1.5f;

        Vector3 freed = rb.position + push;
        // 살짝 띄워 메쉬에 재삽입되는 것 완화
        freed.y = rb.position.y + 0.02f;
        rb.MovePosition(freed);

        if (wishPlanar.sqrMagnitude > 0.01f)
        {
            Vector3 v = rb.linearVelocity;
            Vector3 wish = wishPlanar;
            wish.y = 0f;
            // 끼인 뒤에도 입력 방향으로 속도 유지
            v.x = wish.x;
            v.z = wish.z;
            if (v.y < 0f)
                v.y = 0f;
            rb.linearVelocity = v;
        }
    }

    public static Vector3 SlideWishAgainstWalls(
        Collider body,
        Transform self,
        Vector3 wishPlanar,
        float probeRadius = 0.22f,
        float probeDist = 0.35f)
    {
        if (body == null || self == null || wishPlanar.sqrMagnitude < 0.0001f)
            return wishPlanar;

        Vector3 wish = wishPlanar;
        wish.y = 0f;
        float speed = wish.magnitude;
        wish.Normalize();

        Vector3 origin = body.bounds.center;
        origin.y = body.bounds.min.y + Mathf.Clamp(body.bounds.extents.y * 0.55f, 0.45f, 1.1f);

        if (!Physics.SphereCast(origin, probeRadius, wish, out RaycastHit hit, probeDist, ~0, QueryTriggerInteraction.Ignore))
            return wishPlanar;

        if (hit.collider == null)
            return wishPlanar;
        if (hit.collider == body || hit.collider.transform == self || hit.collider.transform.IsChildOf(self))
            return wishPlanar;
        if (hit.collider.GetComponentInParent<TickToyMove>() != null)
            return wishPlanar;
        if (hit.collider.GetComponentInParent<WinderMove>() != null)
            return wishPlanar;
        if (hit.collider.GetComponentInParent<TickToyAI>() != null)
            return wishPlanar;

        // 걸을 수 있는 낮은 턱/경사는 슬라이드하지 않음
        if (hit.normal.y >= 0.45f)
            return wishPlanar;
        if (hit.point.y <= body.bounds.min.y + 0.35f && hit.normal.y >= 0.2f)
            return wishPlanar;

        Vector3 n = hit.normal;
        n.y = 0f;
        if (n.sqrMagnitude < 0.0001f)
            return Vector3.zero;
        n.Normalize();

        Vector3 slid = Vector3.ProjectOnPlane(wish, n);
        if (slid.sqrMagnitude < 0.05f)
            return Vector3.zero;

        return slid.normalized * speed;
    }

    static bool IsBlockingCollider(Collider other, Collider body, Transform self)
    {
        if (other == null || other == body || other.isTrigger)
            return false;
        if (other.transform == self || other.transform.IsChildOf(self) || self.IsChildOf(other.transform))
            return false;
        if (other.GetComponentInParent<TickToyMove>() != null)
            return false;
        if (other.GetComponentInParent<WinderMove>() != null)
            return false;
        if (other.GetComponentInParent<TickToyAI>() != null)
            return false;
        if (other.GetComponentInParent<WinderTrap>() != null)
            return false;
        return true;
    }

    static void GetProbe(Collider body, Vector3 worldPos, Transform self, out Vector3 center, out Vector3 half)
    {
        CapsuleCollider cap = body as CapsuleCollider;
        if (cap != null)
        {
            Vector3 lossy = self.lossyScale;
            float radius = Mathf.Max(cap.radius * Mathf.Max(lossy.x, lossy.z), 0.12f);
            float height = Mathf.Max(cap.height * lossy.y, radius * 2f);
            // 발끝·머리 여유를 줄여 지면/모자 오탐 감소, 몸통 위주
            half = new Vector3(
                Mathf.Max(0.12f, radius * 0.82f),
                Mathf.Max(0.35f, height * 0.5f * 0.62f),
                Mathf.Max(0.12f, radius * 0.82f));
            center = worldPos + self.rotation * Vector3.Scale(cap.center, lossy);
            center.y = worldPos.y + half.y + 0.1f;
            return;
        }

        Bounds b = body.bounds;
        half = b.extents;
        half.x = Mathf.Max(0.12f, half.x * 0.82f);
        half.y = Mathf.Max(0.35f, half.y * 0.62f);
        half.z = Mathf.Max(0.12f, half.z * 0.82f);
        center = new Vector3(b.center.x, worldPos.y + half.y + 0.1f, b.center.z);
        center.x += worldPos.x - self.position.x;
        center.z += worldPos.z - self.position.z;
    }
}
