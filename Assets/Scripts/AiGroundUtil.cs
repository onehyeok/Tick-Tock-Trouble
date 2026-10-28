using UnityEngine;

/// <summary>
/// AI 스폰/이동용 바닥 찾기. 전선·건물 돌출부가 아니라 길/인도 등 실제 지면을 고릅니다.
/// </summary>
public static class AiGroundUtil
{
    static readonly string[] WalkableHints =
    {
        "street", "walk", "side", "stone", "grass", "floor", "tile", "road", "pavement", "ground"
    };

    static readonly string[] RejectHints =
    {
        "building", "house", "shop", "bank", "hospital", "roof",
        "lamp", "light", "pole", "wire", "cable", "fence", "sign",
        "tree", "bush", "flower", "bin", "bench", "traffic", "car",
        "air conditioner", "conditioner", "glass", "window"
    };

    static readonly RaycastHit[] Hits = new RaycastHit[96];

    public static bool TryFindGround(Vector3 near, out Vector3 ground)
    {
        return TryFindGround(near, near.y, out ground);
    }

    public static bool TryFindGround(Vector3 near, float preferredY, out Vector3 ground)
    {
        ground = near;
        Vector3 origin = new Vector3(near.x, preferredY + 80f, near.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, 200f, ~0, QueryTriggerInteraction.Ignore);
        if (count <= 0)
        {
            origin.y = 250f;
            count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, 500f, ~0, QueryTriggerInteraction.Ignore);
            if (count <= 0)
                return false;
        }

        bool foundStrict = false;
        bool foundLoose = false;
        float bestStrict = float.MaxValue;
        float bestLoose = float.MaxValue;
        Vector3 strictPos = near;
        Vector3 loosePos = near;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = Hits[i];
            if (hit.collider == null)
                continue;
            if (hit.normal.y < 0.45f)
                continue;

            // TileEdgeBarrier 같은 벽은 바닥 아님
            if (hit.collider.gameObject.name.StartsWith("Wall_"))
                continue;
            if (hit.collider.transform.parent != null && hit.collider.transform.parent.name == "TileEdgeBarriers")
                continue;

            float score = Mathf.Abs(hit.point.y - preferredY);
            if (hit.point.y > preferredY + 1.5f)
                score += 50f;

            if (IsWalkableGround(hit.collider))
            {
                if (!foundStrict || score < bestStrict)
                {
                    foundStrict = true;
                    bestStrict = score;
                    strictPos = hit.point;
                }
            }
            else if (IsLooseGround(hit.collider))
            {
                if (!foundLoose || score < bestLoose)
                {
                    foundLoose = true;
                    bestLoose = score;
                    loosePos = hit.point;
                }
            }
        }

        if (foundStrict)
        {
            ground = strictPos;
            return true;
        }

        if (foundLoose)
        {
            ground = loosePos;
            return true;
        }

        return false;
    }

    public static bool TryFindGroundNearPlayerOrWorld(Vector3 xzHint, float preferredY, out Vector3 ground)
    {
        Vector3 hint = new Vector3(xzHint.x, preferredY, xzHint.z);
        if (TryFindGround(hint, preferredY, out ground))
            return true;

        return TryFindGround(hint, preferredY, out ground);
    }

    public static bool IsWalkableGround(Collider col)
    {
        if (col == null)
            return false;

        string path = GetPathName(col.transform).ToLowerInvariant();
        for (int i = 0; i < RejectHints.Length; i++)
        {
            if (path.Contains(RejectHints[i]))
                return false;
        }

        string n = col.gameObject.name.ToLowerInvariant();
        for (int i = 0; i < WalkableHints.Length; i++)
        {
            if (n.Contains(WalkableHints[i]) || path.Contains(WalkableHints[i]))
                return true;
        }

        return IsUnderFloors(col.transform) && IsFlatish(col.bounds);
    }

    public static bool IsLooseGround(Collider col)
    {
        if (col == null)
            return false;
        if (col.gameObject.name.StartsWith("Wall_"))
            return false;

        string path = GetPathName(col.transform).ToLowerInvariant();
        if (path.Contains("building") || path.Contains("house") || path.Contains("roof"))
            return false;

        // Floors 아래이거나 충분히 납작하면 허용
        return (IsUnderFloors(col.transform) || IsFlatish(col.bounds)) && col.bounds.size.y < 6f;
    }

    static bool IsUnderFloors(Transform t)
    {
        Transform cur = t;
        while (cur != null)
        {
            if (cur.name == "Floors")
                return true;
            cur = cur.parent;
        }
        return false;
    }

    static bool IsFlatish(Bounds b)
    {
        return b.size.y <= 3.5f || Mathf.Max(b.size.x, b.size.z) >= b.size.y * 1.1f;
    }

    static string GetPathName(Transform t)
    {
        string s = t.name;
        Transform p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 8)
        {
            s = p.name + "/" + s;
            p = p.parent;
        }
        return s;
    }
}
