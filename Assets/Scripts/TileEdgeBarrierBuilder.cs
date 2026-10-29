using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Floors(타일) 점유 칸의 바깥 모서리에만 보이지 않는 BoxCollider 벽을 세움.
/// 이어진 가장자리는 한 개의 긴 콜라이더로 합쳐 개수를 줄입니다.
/// </summary>
public class TileEdgeBarrierBuilder : MonoBehaviour
{
    [SerializeField] private string floorsObjectName = "Floors";
    [SerializeField] private float cellSize = 2f;
    [SerializeField] private float wallHeight = 12f;
    [SerializeField] private float wallThickness = 0.8f;
    [SerializeField] private float wallSink = 2f;
    [SerializeField] private bool buildOnAwake = false;

    readonly List<Bounds> floorBounds = new List<Bounds>(512);
    Transform wallRoot;

    void Awake()
    {
        if (buildOnAwake)
            Build();
    }

    [ContextMenu("Build Barriers")]
    public void Build()
    {
        if (wallRoot != null)
            DestroyImmediateSafe(wallRoot.gameObject);

        Transform floors = FindFloorsRoot();
        if (floors == null)
        {
            Debug.LogError("[TileEdgeBarrierBuilder] Floors 오브젝트를 찾지 못했습니다.");
            return;
        }

        CollectFloorBounds(floors, out float groundY);
        HashSet<Vector2Int> occupied = BuildOccupiedByCenterSample();
        if (occupied.Count == 0)
        {
            Debug.LogError("[TileEdgeBarrierBuilder] 타일 칸을 하나도 못 찾았습니다.");
            return;
        }

        wallRoot = new GameObject("TileEdgeBarriers").transform;
        wallRoot.SetParent(transform, false);

        // 방향별: 같은 선상의 칸 인덱스를 모아 연속 구간만 긴 벽 1개로
        var left = new Dictionary<int, List<int>>();   // key=cell.x, values=cell.y
        var right = new Dictionary<int, List<int>>();  // key=cell.x, values=cell.y
        var down = new Dictionary<int, List<int>>();   // key=cell.y, values=cell.x
        var up = new Dictionary<int, List<int>>();     // key=cell.y, values=cell.x

        foreach (Vector2Int cell in occupied)
        {
            if (!occupied.Contains(cell + Vector2Int.left))
                AddEdgeIndex(left, cell.x, cell.y);
            if (!occupied.Contains(cell + Vector2Int.right))
                AddEdgeIndex(right, cell.x, cell.y);
            if (!occupied.Contains(cell + Vector2Int.down))
                AddEdgeIndex(down, cell.y, cell.x);
            if (!occupied.Contains(cell + Vector2Int.up))
                AddEdgeIndex(up, cell.y, cell.x);
        }

        int wallCount = 0;
        wallCount += BuildMergedVertical(left, groundY, isRight: false);
        wallCount += BuildMergedVertical(right, groundY, isRight: true);
        wallCount += BuildMergedHorizontal(down, groundY, isUp: false);
        wallCount += BuildMergedHorizontal(up, groundY, isUp: true);

        Debug.Log($"[TileEdgeBarrierBuilder] 타일 칸 {occupied.Count}개, 합친 가장자리 벽 {wallCount}개, groundY={groundY:F2}");
    }

    static void AddEdgeIndex(Dictionary<int, List<int>> map, int lineKey, int along)
    {
        if (!map.TryGetValue(lineKey, out List<int> list))
        {
            list = new List<int>(8);
            map[lineKey] = list;
        }

        list.Add(along);
    }

    int BuildMergedVertical(Dictionary<int, List<int>> edges, float groundY, bool isRight)
    {
        int count = 0;
        float wallCenterY = groundY - wallSink + wallHeight * 0.5f;
        float half = wallThickness * 0.5f;

        foreach (KeyValuePair<int, List<int>> kv in edges)
        {
            int cellX = kv.Key;
            List<int> ys = kv.Value;
            ys.Sort();
            MergeRuns(ys, out List<int> runStart, out List<int> runEnd);

            float x = isRight
                ? (cellX + 1) * cellSize + half
                : cellX * cellSize - half;

            for (int i = 0; i < runStart.Count; i++)
            {
                int y0 = runStart[i];
                int y1 = runEnd[i];
                int cells = y1 - y0 + 1;
                float z = (y0 + y1 + 1) * 0.5f * cellSize;
                Vector3 size = new Vector3(wallThickness, wallHeight, cells * cellSize);
                AddWall($"Wall_V_{(isRight ? "R" : "L")}_{cellX}_{y0}_{y1}", new Vector3(x, wallCenterY, z), size);
                count++;
            }
        }

        return count;
    }

    int BuildMergedHorizontal(Dictionary<int, List<int>> edges, float groundY, bool isUp)
    {
        int count = 0;
        float wallCenterY = groundY - wallSink + wallHeight * 0.5f;
        float half = wallThickness * 0.5f;

        foreach (KeyValuePair<int, List<int>> kv in edges)
        {
            int cellY = kv.Key;
            List<int> xs = kv.Value;
            xs.Sort();
            MergeRuns(xs, out List<int> runStart, out List<int> runEnd);

            float z = isUp
                ? (cellY + 1) * cellSize + half
                : cellY * cellSize - half;

            for (int i = 0; i < runStart.Count; i++)
            {
                int x0 = runStart[i];
                int x1 = runEnd[i];
                int cells = x1 - x0 + 1;
                float x = (x0 + x1 + 1) * 0.5f * cellSize;
                Vector3 size = new Vector3(cells * cellSize, wallHeight, wallThickness);
                AddWall($"Wall_H_{(isUp ? "U" : "D")}_{cellY}_{x0}_{x1}", new Vector3(x, wallCenterY, z), size);
                count++;
            }
        }

        return count;
    }

    static void MergeRuns(List<int> sorted, out List<int> runStart, out List<int> runEnd)
    {
        runStart = new List<int>();
        runEnd = new List<int>();
        if (sorted.Count == 0)
            return;

        int start = sorted[0];
        int prev = sorted[0];
        for (int i = 1; i < sorted.Count; i++)
        {
            int v = sorted[i];
            if (v == prev)
                continue;
            if (v == prev + 1)
            {
                prev = v;
                continue;
            }

            runStart.Add(start);
            runEnd.Add(prev);
            start = v;
            prev = v;
        }

        runStart.Add(start);
        runEnd.Add(prev);
    }

    void AddWall(string name, Vector3 pos, Vector3 size)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(wallRoot, false);
        wall.transform.position = pos;
        BoxCollider box = wall.AddComponent<BoxCollider>();
        box.size = size;
        box.center = Vector3.zero;
        box.isTrigger = false;
    }

    void DestroyImmediateSafe(GameObject go)
    {
        if (go == null)
            return;
        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
    }

    Transform FindFloorsRoot()
    {
        GameObject named = GameObject.Find(floorsObjectName);
        if (named != null)
            return named.transform;

        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == floorsObjectName)
                return all[i];
        }

        return null;
    }

    void CollectFloorBounds(Transform floors, out float groundY)
    {
        floorBounds.Clear();
        float ySum = 0f;
        int yCount = 0;

        Collider[] colliders = floors.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;
            if (!IsFloorLikeBounds(col.bounds))
                continue;

            floorBounds.Add(col.bounds);
            ySum += col.bounds.max.y;
            yCount++;
        }

        MeshRenderer[] renderers = floors.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer mr = renderers[i];
            if (mr == null || !mr.enabled)
                continue;

            bool nameOk = IsFloorName(mr.gameObject.name);
            bool boundsOk = IsFloorLikeBounds(mr.bounds);
            if (!nameOk && !boundsOk)
                continue;
            if (nameOk && !boundsOk && mr.bounds.size.y > Mathf.Max(mr.bounds.size.x, mr.bounds.size.z) * 1.1f)
                continue;

            floorBounds.Add(mr.bounds);
            ySum += mr.bounds.max.y;
            yCount++;
        }

        groundY = yCount > 0 ? ySum / yCount : floors.position.y;
    }

    HashSet<Vector2Int> BuildOccupiedByCenterSample()
    {
        var occupied = new HashSet<Vector2Int>();
        if (floorBounds.Count == 0)
            return occupied;

        Bounds total = floorBounds[0];
        for (int i = 1; i < floorBounds.Count; i++)
            total.Encapsulate(floorBounds[i]);

        int x0 = Mathf.FloorToInt(total.min.x / cellSize);
        int x1 = Mathf.FloorToInt((total.max.x - 0.001f) / cellSize);
        int z0 = Mathf.FloorToInt(total.min.z / cellSize);
        int z1 = Mathf.FloorToInt((total.max.z - 0.001f) / cellSize);

        for (int x = x0; x <= x1; x++)
        {
            for (int z = z0; z <= z1; z++)
            {
                float cx = (x + 0.5f) * cellSize;
                float cz = (z + 0.5f) * cellSize;
                if (IsPointOverFloor(cx, cz))
                    occupied.Add(new Vector2Int(x, z));
            }
        }

        return occupied;
    }

    bool IsPointOverFloor(float x, float z)
    {
        for (int i = 0; i < floorBounds.Count; i++)
        {
            Bounds b = floorBounds[i];
            if (x >= b.min.x && x <= b.max.x && z >= b.min.z && z <= b.max.z)
                return true;
        }

        return false;
    }

    static bool IsFloorName(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("street")
            || n.Contains("walk")
            || n.Contains("side")
            || n.Contains("stone")
            || n.Contains("grass")
            || n.Contains("floor")
            || n.Contains("tile")
            || n.Contains("road")
            || n.Contains("pavement");
    }

    static bool IsFloorLikeBounds(Bounds b)
    {
        float hx = b.size.x;
        float hz = b.size.z;
        float hy = b.size.y;
        float horizontal = Mathf.Max(hx, hz);
        if (horizontal < 0.4f)
            return false;

        if (hy <= 4f)
            return true;
        if (hx * hz >= hy * hy * 1.5f)
            return true;
        if (horizontal >= hy * 1.25f)
            return true;

        return false;
    }
}
