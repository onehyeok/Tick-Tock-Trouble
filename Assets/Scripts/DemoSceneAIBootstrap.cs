using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// DemoScene 시작 시: 타일 맞춤 경계벽 → TickToy AI를 맵 전역에 골고루 스폰.
/// Jephy/Lulu/Momo/Volt 비슷한 수, 건물 안 스폰 없음.
/// </summary>
public class DemoSceneAIBootstrap : MonoBehaviour
{
    [SerializeField] private GameObject[] tickToyPrefabs;
    [SerializeField] private int aiCount = 40;
    [SerializeField] private float minSpawnSpacing = 2.5f;
    [SerializeField] private Vector3 spawnCenterFallback = new Vector3(-93f, 14f, 36f);
    [SerializeField] private float clearHeadroom = 3.2f;
    [SerializeField] private float bodyRadius = 0.28f;

    static readonly string[] PrefabPaths =
    {
        "Assets/Prefabs/Jephy.prefab",
        "Assets/Prefabs/Lulu.prefab",
        "Assets/Prefabs/Momo.prefab",
        "Assets/Prefabs/Volt.prefab"
    };

    void Start()
    {
        StartCoroutine(SetupRoutine());
    }

    IEnumerator SetupRoutine()
    {
        EnsureSceneAudioListener();
        BuildTileBarriers();
        // 한 프레임에 몰리지 않게 나눠 스폰 (시작 끊김 완화)
        yield return null;
        yield return SpawnAIsOverFrames();
        EnsureSceneAudioListener();
        TickToyMove.KeepOneAudioListener();
    }

    static void EnsureSceneAudioListener()
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] != null && listeners[i].enabled)
                return;
        }

        Camera main = Camera.main;
        if (main != null)
        {
            AudioListener onCam = main.GetComponent<AudioListener>();
            if (onCam == null)
                onCam = main.gameObject.AddComponent<AudioListener>();
            onCam.enabled = true;
            return;
        }

        GameObject go = GameObject.Find("SceneAudioListener");
        if (go == null)
            go = new GameObject("SceneAudioListener");
        AudioListener listener = go.GetComponent<AudioListener>();
        if (listener == null)
            listener = go.AddComponent<AudioListener>();
        listener.enabled = true;
    }

    void BuildTileBarriers()
    {
        TileEdgeBarrierBuilder builder = gameObject.GetComponent<TileEdgeBarrierBuilder>();
        if (builder == null)
            builder = gameObject.AddComponent<TileEdgeBarrierBuilder>();
        builder.Build();
    }

    IEnumerator SpawnAIsOverFrames()
    {
        GameObject[] prefabs = ResolvePrefabs();
        if (prefabs.Length == 0)
        {
            Debug.LogError("[DemoSceneAIBootstrap] TickToy 프리팹을 불러오지 못했습니다. tickToyPrefabs / Assets/Prefabs 확인.");
            yield break;
        }

        List<GameObject> spawnQueue = BuildBalancedQueue(prefabs, aiCount);

        if (!TryGetMapBounds(out Bounds mapBounds, out float preferY))
        {
            Vector3 c = spawnCenterFallback;
            mapBounds = new Bounds(c, new Vector3(120f, 10f, 120f));
            preferY = c.y;
            Debug.LogWarning("[DemoSceneAIBootstrap] Floors bounds 실패 → fallback bounds 사용.");
        }

        Debug.Log($"[DemoSceneAIBootstrap] 스폰 준비 prefabs={prefabs.Length}, queue={spawnQueue.Count}, bounds={mapBounds.size}, preferY={preferY:F2}");

        Transform parent = new GameObject("AI_TickToys").transform;
        var placed = new List<Vector3>(aiCount);
        int spawned = 0;
        int failGround = 0;
        int failHeight = 0;
        int failBuilding = 0;
        int failSpacing = 0;
        const int perFrame = 2;

        List<Vector3> slots = BuildSpreadSlots(mapBounds, Mathf.Max(aiCount * 4, 80));
        int slotIndex = 0;

        for (int i = 0; i < spawnQueue.Count; i++)
        {
            GameObject prefab = spawnQueue[i];
            if (prefab == null)
                continue;

            bool ok = false;
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;

            // 1) 그리드 슬롯
            int guard = 0;
            while (slotIndex < slots.Count && guard++ < slots.Count)
            {
                Vector3 candidate = slots[slotIndex++];
                candidate.x += Random.Range(-1.5f, 1.5f);
                candidate.z += Random.Range(-1.5f, 1.5f);

                if (!TryMakeOutdoorPose(candidate, preferY, placed, strictBuilding: true, minSpawnSpacing, out pos, out rot, out int reason))
                {
                    CountFail(reason, ref failGround, ref failHeight, ref failBuilding, ref failSpacing);
                    continue;
                }

                ok = true;
                break;
            }

            // 2) 랜덤 + 건물 검사
            if (!ok)
            {
                for (int t = 0; t < 60; t++)
                {
                    Vector3 candidate = RandomPointInBounds(mapBounds);
                    if (!TryMakeOutdoorPose(candidate, preferY, placed, strictBuilding: true, minSpawnSpacing, out pos, out rot, out int reason))
                    {
                        CountFail(reason, ref failGround, ref failHeight, ref failBuilding, ref failSpacing);
                        continue;
                    }
                    ok = true;
                    break;
                }
            }

            // 3) 건물 검사 완화
            if (!ok)
            {
                for (int t = 0; t < 80; t++)
                {
                    Vector3 candidate = RandomPointInBounds(mapBounds);
                    if (!TryMakeOutdoorPose(candidate, preferY, placed, strictBuilding: false, minSpawnSpacing * 0.7f, out pos, out rot, out int reason))
                    {
                        CountFail(reason, ref failGround, ref failHeight, ref failBuilding, ref failSpacing);
                        continue;
                    }
                    ok = true;
                    break;
                }
            }

            // 4) 최후: 간격 거의 무시하고라도 40 채우기
            if (!ok)
            {
                for (int t = 0; t < 100; t++)
                {
                    Vector3 candidate = RandomPointInBounds(mapBounds);
                    if (!TryMakeOutdoorPose(candidate, preferY, placed, strictBuilding: false, 1.2f, out pos, out rot, out int reason))
                    {
                        CountFail(reason, ref failGround, ref failHeight, ref failBuilding, ref failSpacing);
                        continue;
                    }
                    ok = true;
                    break;
                }
            }

            if (!ok)
                continue;

            GameObject instance = Instantiate(prefab, pos, rot, parent);
            instance.name = $"AI_{prefab.name}_{spawned + 1}";
            ConvertToAI(instance, pos);
            placed.Add(pos);
            spawned++;

            if (spawned % perFrame == 0)
                yield return null;
        }

        Debug.Log($"[DemoSceneAIBootstrap] AI {spawned}/{aiCount} 스폰. 실패 ground={failGround} height={failHeight} building={failBuilding} spacing={failSpacing}");
    }

    static void CountFail(int reason, ref int failGround, ref int failHeight, ref int failBuilding, ref int failSpacing)
    {
        switch (reason)
        {
            case 1: failGround++; break;
            case 2: failHeight++; break;
            case 3: failBuilding++; break;
            case 4: failSpacing++; break;
        }
    }

    List<GameObject> BuildBalancedQueue(GameObject[] prefabs, int total)
    {
        var queue = new List<GameObject>(total);
        int kinds = prefabs.Length;
        if (kinds <= 0)
            return queue;

        int baseCount = total / kinds;
        int remain = total % kinds;

        for (int k = 0; k < kinds; k++)
        {
            int n = baseCount + (k < remain ? 1 : 0);
            for (int i = 0; i < n; i++)
                queue.Add(prefabs[k]);
        }

        for (int i = queue.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            GameObject tmp = queue[i];
            queue[i] = queue[j];
            queue[j] = tmp;
        }

        return queue;
    }

    List<Vector3> BuildSpreadSlots(Bounds mapBounds, int count)
    {
        var slots = new List<Vector3>(count);
        float width = Mathf.Max(8f, mapBounds.size.x);
        float depth = Mathf.Max(8f, mapBounds.size.z);

        int cols = Mathf.CeilToInt(Mathf.Sqrt(count * (width / Mathf.Max(0.01f, depth))));
        int rows = Mathf.CeilToInt((float)count / Mathf.Max(1, cols));
        cols = Mathf.Max(2, cols);
        rows = Mathf.Max(2, rows);

        float x0 = mapBounds.min.x + 3f;
        float x1 = mapBounds.max.x - 3f;
        float z0 = mapBounds.min.z + 3f;
        float z1 = mapBounds.max.z - 3f;
        if (x1 <= x0) { x0 = mapBounds.min.x; x1 = mapBounds.max.x; }
        if (z1 <= z0) { z0 = mapBounds.min.z; z1 = mapBounds.max.z; }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                float tx = c / (float)(cols - 1);
                float tz = r / (float)(rows - 1);
                float x = Mathf.Lerp(x0, x1, tx);
                float z = Mathf.Lerp(z0, z1, tz);
                slots.Add(new Vector3(x, mapBounds.center.y, z));
            }
        }

        for (int i = slots.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Vector3 tmp = slots[i];
            slots[i] = slots[j];
            slots[j] = tmp;
        }

        return slots;
    }

    Vector3 RandomPointInBounds(Bounds b)
    {
        float x0 = b.min.x + 2f;
        float x1 = b.max.x - 2f;
        float z0 = b.min.z + 2f;
        float z1 = b.max.z - 2f;
        if (x1 <= x0) { x0 = b.min.x; x1 = b.max.x; }
        if (z1 <= z0) { z0 = b.min.z; z1 = b.max.z; }

        return new Vector3(Random.Range(x0, x1), b.center.y, Random.Range(z0, z1));
    }

    /// <summary> reason: 1 ground, 2 height, 3 building, 4 spacing </summary>
    bool TryMakeOutdoorPose(Vector3 candidate, float preferY, List<Vector3> placed, bool strictBuilding, float spacing, out Vector3 pos, out Quaternion rot, out int reason)
    {
        rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        pos = candidate;
        reason = 0;

        if (!AiGroundUtil.TryFindGroundNearPlayerOrWorld(candidate, preferY, out Vector3 ground))
        {
            reason = 1;
            return false;
        }

        if (Mathf.Abs(ground.y - preferY) > 4f)
        {
            reason = 2;
            return false;
        }

        if (strictBuilding && IsClearlyInsideBuilding(ground))
        {
            reason = 3;
            return false;
        }

        float minSq = spacing * spacing;
        for (int i = 0; i < placed.Count; i++)
        {
            Vector3 d = placed[i] - ground;
            d.y = 0f;
            if (d.sqrMagnitude < minSq)
            {
                reason = 4;
                return false;
            }
        }

        pos = ground;
        return true;
    }

    GameObject[] ResolvePrefabs()
    {
        var list = new List<GameObject>();

        if (tickToyPrefabs != null)
        {
            for (int i = 0; i < tickToyPrefabs.Length; i++)
            {
                if (tickToyPrefabs[i] != null)
                    list.Add(tickToyPrefabs[i]);
            }
        }

        if (list.Count > 0)
            return list.ToArray();

#if UNITY_EDITOR
        for (int i = 0; i < PrefabPaths.Length; i++)
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPaths[i]);
            if (prefab != null)
                list.Add(prefab);
        }
#endif

        // 플레이 중에도 Resources 백업 (없으면 무시)
        if (list.Count == 0)
        {
            string[] names = { "Jephy", "Lulu", "Momo", "Volt" };
            for (int i = 0; i < names.Length; i++)
            {
                GameObject p = Resources.Load<GameObject>(names[i]);
                if (p != null)
                    list.Add(p);
            }
        }

        return list.ToArray();
    }

    bool TryGetMapBounds(out Bounds bounds, out float preferY)
    {
        bounds = new Bounds();
        preferY = ResolvePreferY();

        GameObject floors = GameObject.Find("Floors");
        if (floors == null)
            return false;

        // walkable 이름 위주로 bounds (건물 옥상 renderer 제외)
        Renderer[] renderers = floors.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || !renderers[i].enabled)
                continue;

            string n = renderers[i].gameObject.name.ToLowerInvariant();
            bool nameOk = n.Contains("street") || n.Contains("walk") || n.Contains("side")
                          || n.Contains("grass") || n.Contains("stone") || n.Contains("floor");
            Bounds rb = renderers[i].bounds;
            if (!nameOk && rb.size.y > 4f)
                continue;

            if (!any)
            {
                bounds = rb;
                any = true;
            }
            else
            {
                bounds.Encapsulate(rb);
            }
        }

        if (!any)
            return false;

        return true;
    }

    /// <summary>
    /// 스폰 높이 기준: TickToy → Winder(Brick) → fallback.
    /// TickToy만 끄고 Brick만 켠 테스트에서도 AI가 나오게.
    /// </summary>
    float ResolvePreferY()
    {
        TickToyMove tickToy = FindAnyObjectByType<TickToyMove>();
        if (tickToy != null)
            return tickToy.transform.position.y;

        WinderMove winder = FindAnyObjectByType<WinderMove>();
        if (winder != null)
            return winder.transform.position.y;

        return spawnCenterFallback.y;
    }

    /// <summary>
    /// 전선/나무에 걸리지 않도록, 확실한 실내만 거절.
    /// </summary>
    bool IsClearlyInsideBuilding(Vector3 ground)
    {
        Vector3 feet = ground + Vector3.up * 0.2f;
        Vector3 chest = ground + Vector3.up * 1.1f;

        // 머리 위: 건물/지붕만 실내로 판정 (전선·나무는 무시)
        if (Physics.Raycast(feet, Vector3.up, out RaycastHit upHit, clearHeadroom, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IsBuildingObject(upHit.collider.transform))
                return true;
        }

        Collider[] overlaps = Physics.OverlapSphere(chest, bodyRadius * 0.9f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider c = overlaps[i];
            if (c == null || c.isTrigger)
                continue;
            if (c.gameObject.name.StartsWith("Wall_"))
                continue;
            if (AiGroundUtil.IsWalkableGround(c) || AiGroundUtil.IsLooseGround(c))
                continue;
            if (IsBuildingObject(c.transform))
                return true;
        }

        return false;
    }

    static bool IsBuildingObject(Transform t)
    {
        Transform cur = t;
        while (cur != null)
        {
            string n = cur.name.ToLowerInvariant();
            if (n.Contains("building")
                || n.Contains("house")
                || n.Contains("shop")
                || n.Contains("bank")
                || n.Contains("hospital")
                || n.Contains("roof")
                || n == "buildings")
                return true;
            cur = cur.parent;
        }

        return false;
    }

    void ConvertToAI(GameObject instance, Vector3 groundedPos)
    {
        Rigidbody rb = instance.GetComponent<Rigidbody>();
        if (rb == null)
            rb = instance.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        // kinematic 이면 linear/angularVelocity 설정 불가 (경고 스팸)
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.position = groundedPos;
        instance.transform.position = groundedPos;

        TickToyMove move = instance.GetComponent<TickToyMove>();
        if (move != null)
        {
            move.enabled = false;
            Destroy(move);
        }

        TickToyAnimation animDriver = instance.GetComponent<TickToyAnimation>();
        if (animDriver != null)
        {
            animDriver.enabled = false;
            Destroy(animDriver);
        }

        Camera[] cams = instance.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cams.Length; i++)
            cams[i].gameObject.SetActive(false);

        AudioListener[] listeners = instance.GetComponentsInChildren<AudioListener>(true);
        for (int i = 0; i < listeners.Length; i++)
            listeners[i].enabled = false;

        NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = false;
            Destroy(agent);
        }

        Collider body = instance.GetComponent<Collider>();
        if (body == null)
        {
            BoxCollider box = instance.AddComponent<BoxCollider>();
            box.size = new Vector3(0.8f, 1.6f, 0.8f);
            box.center = new Vector3(0f, 0.8f, 0f);
        }

        Physics.SyncTransforms();

        if (!instance.GetComponent<TickToyAI>())
            instance.AddComponent<TickToyAI>();

        // AI 카메라 리스너를 끈 뒤, 씬에 리스너 1개 보장
        EnsureSceneAudioListener();
        TickToyMove.KeepOneAudioListener();
    }
}
