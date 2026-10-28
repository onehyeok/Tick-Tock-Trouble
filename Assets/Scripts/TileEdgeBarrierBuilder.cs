using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Floors(타일) 점유 칸의 바깥 모서리에만 보이지 않는 벽을 세워,
/// 큰 사각형 한 방이 아니라 타일 외곽 모양 그대로 낙하를 막습니다.
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

        int wallCount = 0;
        foreach (Vector2Int cell in occupied)
        {
            wallCount += TryAddEdge(occupied, cell, Vector2Int.left, groundY);
            wallCount += TryAddEdge(occupied, cell, Vector2Int.right, groundY);
            wallCount += TryAddEdge(occupied, cell, Vector2Int.down, groundY);
            wallCount += TryAddEdge(occupied, cell, Vector2Int.up, groundY);
        }

        Debug.Log($"[TileEdgeBarrierBuilder] 타일 칸 {occupied.Count}개, 가장자리 벽 {wallCount}개, groundY={groundY:F2}");
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

        // 칸 중심이 실제 바닥 bounds 안에 있을 때만 점유로 인정 → 가장자리가 타일에 딱 맞음
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

    int TryAddEdge(HashSet<Vector2Int> occupied, Vector2Int cell, Vector2Int dir, float groundY)
    {
        Vector2Int neighbor = cell + dir;
        if (occupied.Contains(neighbor))
            return 0;

        float cx = (cell.x + 0.5f) * cellSize;
        float cz = (cell.y + 0.5f) * cellSize;
        float wallCenterY = groundY - wallSink + wallHeight * 0.5f;

        // 벽 전체를 칸 바깥(낙하 쪽)에 두어, 안쪽 면이 타일 모서리와 일치
        float half = wallThickness * 0.5f;
        Vector3 pos;
        Vector3 size;

        if (dir == Vector2Int.left)
        {
            pos = new Vector3(cell.x * cellSize - half, wallCenterY, cz);
            size = new Vector3(wallThickness, wallHeight, cellSize);
        }
        else if (dir == Vector2Int.right)
        {
            pos = new Vector3((cell.x + 1) * cellSize + half, wallCenterY, cz);
            size = new Vector3(wallThickness, wallHeight, cellSize);
        }
        else if (dir == Vector2Int.down)
        {
            pos = new Vector3(cx, wallCenterY, cell.y * cellSize - half);
            size = new Vector3(cellSize, wallHeight, wallThickness);
        }
        else
        {
            pos = new Vector3(cx, wallCenterY, (cell.y + 1) * cellSize + half);
            size = new Vector3(cellSize, wallHeight, wallThickness);
        }

        GameObject wall = new GameObject($"Wall_{cell.x}_{cell.y}_{dir.x}_{dir.y}");
        wall.transform.SetParent(wallRoot, false);
        wall.transform.position = pos;
        BoxCollider box = wall.AddComponent<BoxCollider>();
        box.size = size;
        box.center = Vector3.zero;
        box.isTrigger = false;

        return 1;
    }
}
