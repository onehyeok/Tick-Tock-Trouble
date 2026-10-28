using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// DemoScene 시작 시: 타일 맞춤 경계벽 → NavMesh(가능하면) → TickToy AI 여러 마리 스폰.
/// </summary>
public class DemoSceneAIBootstrap : MonoBehaviour
{
    [SerializeField] private GameObject[] tickToyPrefabs;
    [SerializeField] private int aiCount = 24;
    [SerializeField] private float spawnRadius = 35f;
    [SerializeField] private Vector3 spawnCenterFallback = new Vector3(-93f, 14f, 36f);
    [SerializeField] private float agentHeight = 1.6f;
    [SerializeField] private float agentRadius = 0.28f;

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
        BuildTileBarriers();
        yield return null;

        bool navReady = BakeNavMeshSafe();
        yield return null;

        SpawnAIs(navReady);
    }

    void BuildTileBarriers()
    {
        TileEdgeBarrierBuilder builder = gameObject.GetComponent<TileEdgeBarrierBuilder>();
        if (builder == null)
            builder = gameObject.AddComponent<TileEdgeBarrierBuilder>();
        builder.Build();
    }

    bool BakeNavMeshSafe()
    {
        try
        {
            NavMeshSurface surface = gameObject.GetComponent<NavMeshSurface>();
            if (surface == null)
                surface = gameObject.AddComponent<NavMeshSurface>();

            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.ignoreNavMeshAgent = true;
            surface.BuildNavMesh();

            // Physics로 거의 안 나오면 RenderMeshes로 재시도
            if (!HasAnyNavMesh())
            {
                surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
                surface.BuildNavMesh();
            }

            bool ok = HasAnyNavMesh();
            Debug.Log(ok
                ? "[DemoSceneAIBootstrap] NavMesh 베이크 성공."
                : "[DemoSceneAIBootstrap] NavMesh 비어 있음 → AI는 Rigidbody로 이동.");
            return ok;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[DemoSceneAIBootstrap] NavMesh 베이크 실패: " + e.Message);
            return false;
        }
    }

    static bool HasAnyNavMesh()
    {
        return NavMesh.SamplePosition(Vector3.zero, out _, 5000f, NavMesh.AllAreas)
            || NavMesh.SamplePosition(new Vector3(-93f, 14f, 36f), out _, 80f, NavMesh.AllAreas);
    }

    void SpawnAIs(bool navReady)
    {
        GameObject[] prefabs = ResolvePrefabs();
        if (prefabs.Length == 0)
        {
            Debug.LogError("[DemoSceneAIBootstrap] TickToy 프리팹을 불러오지 못했습니다.");
            return;
        }

        Vector3 center = ResolveSpawnCenter();
        Transform parent = new GameObject("AI_TickToys").transform;
        int spawned = 0;

        for (int i = 0; i < aiCount; i++)
        {
            GameObject prefab = prefabs[i % prefabs.Length];
            if (prefab == null)
                continue;

            if (!TryFindSpawnPose(center, navReady, out Vector3 pos, out Quaternion rot))
            {
                pos = center + new Vector3((i % 6) * 1.5f, 0.5f, (i / 6) * 1.5f);
                rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                SnapToGround(ref pos);
            }

            GameObject instance = Instantiate(prefab, pos, rot, parent);
            instance.name = $"AI_{prefab.name}_{i + 1}";
            ConvertToAI(instance, navReady);
            spawned++;
        }

        Debug.Log($"[DemoSceneAIBootstrap] AI {spawned}마리 스폰 완료 (prefabs={prefabs.Length}, nav={navReady}).");
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

        return list.ToArray();
    }

    bool TryFindSpawnPose(Vector3 center, bool navReady, out Vector3 pos, out Quaternion rot)
    {
        rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector2 circle = Random.insideUnitCircle * spawnRadius;
            Vector3 candidate = center + new Vector3(circle.x, 2f, circle.y);

            if (navReady && NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 8f, NavMesh.AllAreas))
            {
                pos = navHit.position;
                return true;
            }

            if (Physics.Raycast(candidate + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore))
            {
                // 너무 가파른 면/건물 옆면 제외
                if (hit.normal.y < 0.55f)
                    continue;
                pos = hit.point;
                return true;
            }
        }

        pos = center;
        return false;
    }

    void SnapToGround(ref Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore))
            pos = hit.point;
    }

    Vector3 ResolveSpawnCenter()
    {
        // 플레이어 TickToy가 있으면 그 근처
        TickToyMove player = FindAnyObjectByType<TickToyMove>();
        if (player != null)
            return player.transform.position;

        GameObject floors = GameObject.Find("Floors");
        if (floors != null)
        {
            Renderer[] renderers = floors.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                int used = 1;
                for (int i = 1; i < renderers.Length; i++)
                {
                    if (!renderers[i].enabled)
                        continue;
                    Bounds rb = renderers[i].bounds;
                    if (rb.size.y > 8f)
                        continue;
                    b.Encapsulate(rb);
                    used++;
                    if (used >= 300)
                        break;
                }

                return new Vector3(b.center.x, b.max.y + 0.2f, b.center.z);
            }
        }

        return spawnCenterFallback;
    }

    void ConvertToAI(GameObject instance, bool navReady)
    {
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

        Rigidbody rb = instance.GetComponent<Rigidbody>();
        if (rb == null)
            rb = instance.AddComponent<Rigidbody>();

        Collider body = instance.GetComponent<Collider>();
        if (body == null)
        {
            BoxCollider box = instance.AddComponent<BoxCollider>();
            box.size = new Vector3(0.8f, 1.6f, 0.8f);
            box.center = new Vector3(0f, 0.8f, 0f);
        }

        bool onNav = false;
        if (navReady)
        {
            NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
            if (agent == null)
                agent = instance.AddComponent<NavMeshAgent>();

            agent.radius = agentRadius;
            agent.height = agentHeight;
            agent.baseOffset = 0f;
            agent.autoBraking = true;

            if (NavMesh.SamplePosition(instance.transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                onNav = agent.isOnNavMesh;
            }

            if (!onNav)
                agent.enabled = false;
        }

        if (!onNav)
        {
            NavMeshAgent existing = instance.GetComponent<NavMeshAgent>();
            if (existing != null)
                existing.enabled = false;

            rb.isKinematic = false;
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }
        else
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (!instance.GetComponent<TickToyAI>())
            instance.AddComponent<TickToyAI>();
    }
}
