using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 맵(CITY) 안 라운드 진행.
/// 1) 역할 안내 + 20초 숨기 (와인더는 시작 지점에 갇힘)
/// 2) 문 열림 → 3분 술래잡기
/// 3) 틱토이 전멸 = 와인더 승리 / 한 명이라도 살아남으면 틱토이 승리
/// 4) 다음 라운드(역할 무작위 재배정) → 마지막 라운드 후 방으로
/// MatchContext.Active 일 때 맵 씬이 열리면 자동으로 생깁니다.
/// </summary>
public class RoundManager : MonoBehaviour
{
    enum Phase
    {
        Hide,
        Hunt,
        Result
    }

    public static RoundManager Instance { get; private set; }

    static bool hooked;

    Phase phase;
    float phaseLeft;
    GameObject localPlayer;
    TickToyMove localTickToy;
    WinderMove localWinder;
    readonly List<GameObject> startWalls = new List<GameObject>();
    TickToyHud hud;
    bool resultWinderWon;

    // UI
    Canvas canvas;
    GameObject bannerRoot;
    Text bannerTitle;
    Text bannerSub;
    Text countdownText;
    GameObject resultRoot;
    Text resultTitle;
    Text resultSub;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        if (hooked)
            return;
        hooked = true;
        SceneManager.sceneLoaded += (scene, mode) => TryCreate(scene);
        TryCreate(SceneManager.GetActiveScene());
    }

    static void TryCreate(Scene scene)
    {
        if (!MatchContext.Active || Instance != null)
            return;
        MapCatalog catalog = MapCatalog.Load();
        if (catalog == null || catalog.FindByScene(scene.name) == null)
            return;
        new GameObject("RoundManager").AddComponent<RoundManager>();
    }

    void Awake()
    {
        Instance = this;
        // 씬에 놓여 있던 테스트용 캐릭터는 Start 전에 꺼 둠
        DisableScenePlayers();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        BuildUI();
        hud = FindAnyObjectByType<TickToyHud>();
        EnsureWinderRoom();
        FindStartWalls();
        SpawnLocalPlayer();
        EnterHide();
    }

    // ---------------- 스폰 ----------------

    /// <summary>씬에 미리 놓여 있던 테스트용 캐릭터는 끔 (AI는 나중에 따로 스폰됨)</summary>
    static void DisableScenePlayers()
    {
        foreach (TickToyMove m in FindObjectsByType<TickToyMove>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            m.gameObject.SetActive(false);
        foreach (WinderMove m in FindObjectsByType<WinderMove>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            m.gameObject.SetActive(false);
    }

    Transform FindWinderStart()
    {
        Transform best = null;
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!t.name.StartsWith("WinderStarting"))
                continue;
            if (t.name.Contains("Wall") || t.name.Contains("Door"))
                continue;
            if (best == null || t.name.Length < best.name.Length)
                best = t;
        }
        return best;
    }

    void FindStartWalls()
    {
        startWalls.Clear();
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            string n = t.name;
            if (n.StartsWith("WinderStarting") && (n.Contains("Wall") || n.Contains("Door")))
                startWalls.Add(t.gameObject);
        }
    }

    void SpawnLocalPlayer()
    {
        MatchPrefabs prefabs = MatchPrefabs.Load();
        if (prefabs == null)
        {
            Debug.LogError("[RoundManager] Resources/MatchPrefabs 가 없어요.");
            return;
        }

        if (MatchContext.LocalIsWinder)
        {
            int index = Mathf.Max(0, MatchContext.WinderIds.IndexOf(MatchContext.LocalId));
            Vector3 pos;
            Quaternion rot;
            WinderSpawn(index, out pos, out rot);
            localPlayer = Instantiate(prefabs.winderPrefab, pos, rot);
            localWinder = localPlayer.GetComponent<WinderMove>();
        }
        else
        {
            GameObject prefab = prefabs.tickToyPrefabs[Random.Range(0, prefabs.tickToyPrefabs.Length)];
            Vector3 pos = RandomRoadPoint();
            localPlayer = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            localTickToy = localPlayer.GetComponent<TickToyMove>();
        }

        localPlayer.name = (MatchContext.LocalIsWinder ? "Winder_" : "TickToy_") + LocalNickname();
        localPlayer.SetActive(true);
        TickToyMove.KeepOneAudioListener();
    }

    /// <summary>
    /// 와인더 시작 방(Models/WinderStartingPosition)을 맵의 WinderStarting 자리에 넣음.
    /// 방 안 바닥(WinderStartingPos)이 WinderStarting 위치에 오도록 맞추고, 아래로 빠지지 않게 투명 바닥을 깔아 둠.
    /// </summary>
    /// <summary>시작 방 회전 (프리팹 원래 방향 기준, 음수 = 위에서 볼 때 왼쪽/반시계)</summary>
    const float WinderRoomYaw = 270f;

    static void EnsureWinderRoom()
    {
        Transform anchor = null;
        Transform room = null;
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t.name == "WinderStarting")
                anchor = t;
            else if (t.name.StartsWith("WinderStartingPosition"))
                room = t;
        }

        if (room == null)
        {
            MatchPrefabs prefabs = MatchPrefabs.Load();
            if (prefabs == null || prefabs.winderStartRoom == null)
                return;
            room = Instantiate(prefabs.winderStartRoom).transform;
            room.name = "WinderStartingPosition";
        }

        if (anchor != null && !room.IsChildOf(anchor))
        {
            room.SetParent(anchor, true);
            Transform floorRef = FindChild(room, "WinderStartingPos");
            Vector3 delta = anchor.position - (floorRef != null ? floorRef.position : room.position);
            room.position += delta;
            Physics.SyncTransforms();
            // 도로 끝에 건물을 세움: 입구(문)가 도로를 보고, 문이 도로 타일 끝에 딱 붙게
            if (!PlaceAtRoadEnd(room, anchor.position))
                room.RotateAround(anchor.position, Vector3.up, WinderRoomYaw);
        }
        Physics.SyncTransforms();

        // 투명 바닥 (방 모델에 바닥이 없어도 아래로 안 빠짐)
        if (TryGetRoomBounds(out Bounds b))
        {
            float floorTop = anchor != null ? anchor.position.y : b.min.y;
            var floor = new GameObject("WinderStartingFloor");
            floor.transform.SetParent(room, true);
            floor.transform.position = new Vector3(b.center.x, floorTop - 0.25f, b.center.z);
            floor.transform.rotation = Quaternion.identity;
            var box = floor.AddComponent<BoxCollider>();
            Vector3 lossy = floor.transform.lossyScale;
            box.size = new Vector3(
                (b.size.x + 1f) / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                0.5f / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                (b.size.z + 1f) / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
            Physics.SyncTransforms();

            AddVisibleFloor(room, b, floorTop);
            AddSideBlockers(room, b, floorTop);
        }
    }

    /// <summary>
    /// 방 바닥 메우기: 도로 끝에 놓인 방의 양옆(도로 밖) 빈 바닥을 회색 콘크리트 판으로 채움.
    /// 도로보다 아주 살짝 낮게 깔아서 도로 위는 도로가 그대로 보임.
    /// </summary>
    static void AddVisibleFloor(Transform room, Bounds b, float floorTop)
    {
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "WinderStartingFloorVisual";
        Object.Destroy(visual.GetComponent<Collider>());
        visual.transform.SetParent(room, true);
        visual.transform.rotation = Quaternion.identity;
        const float thickness = 0.2f;
        visual.transform.position = new Vector3(b.center.x, floorTop - 0.03f - thickness * 0.5f, b.center.z);
        Vector3 parentScale = room.lossyScale;
        visual.transform.localScale = new Vector3(
            b.size.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            thickness / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            b.size.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));

        // 맵 도로와 같은 셰이더를 복사하고 텍스처만 빼서 단색 콘크리트로 (빌드에서도 안전)
        Material baseMat = FindGroundMaterial(b, floorTop);
        Material mat = baseMat != null ? new Material(baseMat) : new Material(visual.GetComponent<Renderer>().sharedMaterial);
        Color concrete = new Color(0.43f, 0.47f, 0.54f, 1f);
        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", null);
        if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", null);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", concrete);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", concrete);
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.1f);
        visual.GetComponent<Renderer>().sharedMaterial = mat;
    }

    /// <summary>
    /// 문 앞 왼쪽 끝·오른쪽 끝(도로 밖, 바닥 없는 곳)만 보이지 않는 벽으로 막음.
    /// 도로 폭 부분은 비워 둬서 문이 열리면 그대로 지나갈 수 있음.
    /// </summary>
    static void AddSideBlockers(Transform room, Bounds roomBounds, float floorTop)
    {
        Transform wall = FindChild(room, "WinderStartingWall");
        if (wall == null)
            return;

        Vector3 forward = wall.position - roomBounds.center;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            return;
        forward.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        float halfWidth = (Mathf.Abs(side.x) * roomBounds.size.x + Mathf.Abs(side.z) * roomBounds.size.z) * 0.5f;
        Vector3 doorLine = new Vector3(wall.position.x, floorTop, wall.position.z);

        // 문 바로 앞 도로 폭 (도로 = 지나갈 수 있는 곳)
        float roadMin = float.MaxValue;
        float roadMax = float.MinValue;
        for (float t = -halfWidth; t <= halfWidth; t += 0.25f)
        {
            if (IsRoadAt(doorLine + forward * 1.5f + side * t, floorTop, room))
            {
                roadMin = Mathf.Min(roadMin, t);
                roadMax = Mathf.Max(roadMax, t);
            }
        }
        if (roadMin > roadMax)
            return;

        MakeBlocker(room, doorLine, forward, side, -halfWidth - 1f, roadMin - 0.15f, floorTop, "WinderStartingBlockLeft");
        MakeBlocker(room, doorLine, forward, side, roadMax + 0.15f, halfWidth + 1f, floorTop, "WinderStartingBlockRight");
        Physics.SyncTransforms();
    }

    /// <summary>옆 칸(side t0~t1)에서 바닥이 없는 거리까지 보이지 않는 벽(콜라이더만)</summary>
    static void MakeBlocker(Transform room, Vector3 doorLine, Vector3 forward, Vector3 side, float t0, float t1, float floorTop, string name)
    {
        if (t1 - t0 < 0.2f)
            return;

        // 이 칸 앞쪽에서 바닥이 비어 있는 마지막 거리
        float emptyUntil = 0f;
        for (float d = 0.25f; d <= 40f; d += 0.5f)
        {
            for (float t = t0; t <= t1; t += 0.5f)
            {
                if (!HasGroundAt(doorLine + forward * d + side * t, floorTop, room))
                {
                    emptyUntil = d;
                    break;
                }
            }
        }
        if (emptyUntil <= 0f)
            return;

        float length = emptyUntil + 0.5f;
        float width = t1 - t0;
        const float height = 4f;
        Vector3 center = doorLine + forward * (length * 0.5f) + side * ((t0 + t1) * 0.5f) + Vector3.up * (height * 0.5f - 0.5f);

        var go = new GameObject(name);
        go.transform.SetParent(room, true);
        go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(forward, Vector3.up));
        var box = go.AddComponent<BoxCollider>();
        Vector3 ls = go.transform.lossyScale;
        box.size = new Vector3(width / Mathf.Max(0.0001f, ls.x), height / Mathf.Max(0.0001f, ls.y), length / Mathf.Max(0.0001f, ls.z));
    }

    /// <summary>그 지점 근처 높이에 밟을 바닥이 있는지 (건물 자신·캐릭터 제외)</summary>
    static bool HasGroundAt(Vector3 p, float floorTop, Transform ignore)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, floorTop + 3f, p.z), Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit h in hits)
        {
            if (h.collider.transform.IsChildOf(ignore) || h.collider.attachedRigidbody != null || h.normal.y < 0.6f)
                continue;
            if (Mathf.Abs(h.point.y - floorTop) < 0.6f)
                return true;
        }
        return false;
    }

    static Material FindGroundMaterial(Bounds b, float y)
    {
        Vector3[] probes =
        {
            b.center + new Vector3(b.extents.x + 3f, 0f, 0f),
            b.center - new Vector3(b.extents.x + 3f, 0f, 0f),
            b.center + new Vector3(0f, 0f, b.extents.z + 3f),
            b.center - new Vector3(0f, 0f, b.extents.z + 3f)
        };
        foreach (Vector3 p in probes)
        {
            if (Physics.Raycast(new Vector3(p.x, y + 20f, p.z), Vector3.down, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore))
            {
                Renderer r = hit.collider.GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null)
                    return r.sharedMaterial;
            }
        }
        return null;
    }

    /// <summary>
    /// WinderStarting 근처 도로를 찾아서
    /// 1) 문(WinderStartingWall)이 도로 쪽을 보게 돌리고
    /// 2) 문이 도로 타일 끝 선에, 도로 폭 가운데에 오도록 건물을 옮김 (건물은 도로 밖, 도로는 문 앞으로 이어짐)
    /// </summary>
    static bool PlaceAtRoadEnd(Transform room, Vector3 anchor)
    {
        Transform wall = FindChild(room, "WinderStartingWall");
        if (wall == null)
            return false;

        float y = anchor.y;
        // 도로가 뻗은 방향 (4방향 중 도로가 가장 길게 이어지는 쪽)
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        Vector3 roadDir = Vector3.zero;
        int bestCount = 0;
        foreach (Vector3 d in dirs)
        {
            int count = 0;
            for (float dist = 2f; dist <= 16f; dist += 2f)
            {
                if (IsRoadAt(anchor + d * dist, y, room))
                    count++;
            }
            if (count > bestCount)
            {
                bestCount = count;
                roadDir = d;
            }
        }
        if (bestCount < 3)
            return false;

        // 문이 도로 쪽을 보게
        Vector3 doorDir = wall.position - anchor;
        doorDir.y = 0f;
        room.RotateAround(anchor, Vector3.up, Vector3.SignedAngle(doorDir, roadDir, Vector3.up));
        Physics.SyncTransforms();

        // 도로 끝: 도로 안쪽(6m)에서 반대쪽으로 걸어가며 도로가 끊기는 곳
        float endS = -10f;
        for (float sStep = 6f; sStep >= -10f; sStep -= 0.25f)
        {
            if (!IsRoadAt(anchor + roadDir * sStep, y, room))
            {
                endS = sStep + 0.25f;
                break;
            }
        }

        // 도로 폭 가운데
        Vector3 side = Vector3.Cross(Vector3.up, roadDir);
        Vector3 probe = anchor + roadDir * (endS + 3f);
        float minT = float.MaxValue;
        float maxT = float.MinValue;
        for (float t = -15f; t <= 15f; t += 0.25f)
        {
            if (IsRoadAt(probe + side * t, y, room))
            {
                minT = Mathf.Min(minT, t);
                maxT = Mathf.Max(maxT, t);
            }
        }
        float centerT = minT <= maxT ? (minT + maxT) * 0.5f : 0f;

        // 문(벽)을 도로 끝 선 + 도로 가운데로
        Vector3 target = anchor + roadDir * endS + side * centerT;
        Vector3 move = target - wall.position;
        move.y = 0f;
        room.position += move;
        Physics.SyncTransforms();
        return true;
    }

    /// <summary>그 지점 맨 위 바닥이 도로(street/road, 인도 제외)인지</summary>
    static bool IsRoadAt(Vector3 p, float refY, Transform ignore)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, refY + 20f, p.z), Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
        float top = float.NegativeInfinity;
        Collider topCol = null;
        foreach (RaycastHit h in hits)
        {
            if (h.collider.transform.IsChildOf(ignore) || h.collider.attachedRigidbody != null || h.normal.y < 0.6f)
                continue;
            if (h.point.y > top)
            {
                top = h.point.y;
                topCol = h.collider;
            }
        }
        if (topCol == null)
            return false;
        string n = topCol.name.ToLowerInvariant();
        string parent = topCol.transform.parent != null ? topCol.transform.parent.name.ToLowerInvariant() : "";
        bool road = n.Contains("street") || n.Contains("road") || parent.Contains("street") || parent.Contains("road");
        bool sidewalk = n.Contains("side") || n.Contains("walk");
        return road && !sidewalk;
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }
        return null;
    }

    /// <summary>
    /// 와인더 시작 위치: WinderStartingPosition 방 안.
    /// 최대 8명이 서로 겹치지 않게 4칸 x 2줄로 세우고, 모두 문(벽) 쪽을 봄.
    /// </summary>
    public void WinderSpawn(int index, out Vector3 pos, out Quaternion rot)
    {
        bool hasRoom = TryGetRoomBounds(out Bounds room);
        Transform start = FindWinderStart();
        Vector3 center = hasRoom ? room.center : (start != null ? start.position : Vector3.zero);

        // 문(벽) 쪽 방향
        Vector3 forward = Vector3.forward;
        if (startWalls.Count > 0 && startWalls[0] != null)
        {
            Vector3 toWall = startWalls[0].transform.position - center;
            toWall.y = 0f;
            if (toWall.sqrMagnitude > 0.01f)
                forward = toWall.normalized;
        }
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        rot = Quaternion.LookRotation(forward, Vector3.up);

        // 방 크기에 맞춘 간격 (좁아도 최소 1.4m — 캡슐끼리 안 부딪힘)
        float width = hasRoom ? Mathf.Abs(right.x) * room.size.x + Mathf.Abs(right.z) * room.size.z : 8f;
        float depth = hasRoom ? Mathf.Abs(forward.x) * room.size.x + Mathf.Abs(forward.z) * room.size.z : 4f;
        float colGap = Mathf.Clamp((width - 2f) / 4f, 1.4f, 2.5f);
        float rowGap = Mathf.Clamp((depth - 2f) / 2f, 1.4f, 2.5f);

        int col = index % 4;
        int row = (index / 4) % 2;
        Vector3 offset = right * ((col - 1.5f) * colGap) + forward * ((0.5f - row) * rowGap);
        pos = center + offset;
        pos.y = FloorY(pos, hasRoom ? room : new Bounds(center, Vector3.one * 4f));
    }

    static bool TryGetRoomBounds(out Bounds bounds)
    {
        bounds = new Bounds();
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t.name != "WinderStartingPos")
                continue;
            Renderer r = t.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                bounds = r.bounds;
                return true;
            }
            Collider c = t.GetComponentInChildren<Collider>();
            if (c != null)
            {
                bounds = c.bounds;
                return true;
            }
        }
        return false;
    }

    /// <summary>방 안 바닥 높이 (방 안쪽 위에서 아래로 쏴서 찾음)</summary>
    static float FloorY(Vector3 p, Bounds room)
    {
        Vector3 origin = new Vector3(p.x, room.min.y + room.size.y * 0.6f, p.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, room.size.y + 2f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (RaycastHit h in hits)
        {
            if (h.collider.attachedRigidbody != null || h.normal.y < 0.6f)
                continue;
            if (h.point.y > best)
                best = h.point.y;
        }
        if (!float.IsNegativeInfinity(best))
            return best;
        if (AiGroundUtil.TryFindGround(p, room.min.y, out Vector3 g))
            return g.y;
        return room.min.y;
    }

    /// <summary>도로(street/road) 위 무작위 지점</summary>
    public static Vector3 RandomRoadPoint()
    {
        Bounds area = MapBounds();
        Vector3 fallback = area.center;
        bool hasFallback = false;

        for (int i = 0; i < 300; i++)
        {
            Vector3 p = new Vector3(
                Random.Range(area.min.x, area.max.x),
                area.max.y + 50f,
                Random.Range(area.min.z, area.max.z));
            if (!Physics.Raycast(p, Vector3.down, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore))
                continue;
            if (hit.normal.y < 0.8f || hit.collider.attachedRigidbody != null)
                continue;
            if (!AiGroundUtil.IsWalkableGround(hit.collider))
                continue;

            string n = hit.collider.name.ToLowerInvariant();
            bool road = n.Contains("street") || n.Contains("road");
            if (Physics.CheckCapsule(hit.point + Vector3.up * 0.6f, hit.point + Vector3.up * 1.6f, 0.4f, ~0, QueryTriggerInteraction.Ignore))
                continue;

            if (road)
                return hit.point;
            if (!hasFallback)
            {
                fallback = hit.point;
                hasFallback = true;
            }
        }

        return fallback;
    }

    static Bounds MapBounds()
    {
        GameObject floors = GameObject.Find("Floors");
        Bounds b = new Bounds(Vector3.zero, new Vector3(100f, 10f, 100f));
        if (floors == null)
            return b;
        bool init = false;
        foreach (Collider c in floors.GetComponentsInChildren<Collider>())
        {
            if (!init)
            {
                b = c.bounds;
                init = true;
            }
            else
                b.Encapsulate(c.bounds);
        }
        return b;
    }

    static string LocalNickname()
    {
        foreach (RoomPlayer p in MatchContext.Players)
        {
            if (p.id == MatchContext.LocalId)
                return p.nickname;
        }
        return PlayerProfile.Nickname;
    }

    // ---------------- 진행 ----------------

    void EnterHide()
    {
        phase = Phase.Hide;
        phaseLeft = MatchContext.HideSeconds;
        bool winder = MatchContext.LocalIsWinder;

        // 문(벽)이 없으면 와인더를 20초 동안 묶어 둠
        if (winder && startWalls.Count == 0 && localWinder != null)
            localWinder.Freeze(MatchContext.HideSeconds);

        bannerRoot.SetActive(true);
        bannerTitle.text = winder ? "당신은 와인더입니다." : "당신은 틱토이입니다.";
        bannerTitle.color = winder ? UIKit.Pink : UIKit.Mint;
        bannerSub.text = winder ? "숨어있는 틱토이를 찾아내세요!" : "와인더를 따돌려 살아남으세요!";
        resultRoot.SetActive(false);
    }

    void EnterHunt()
    {
        phase = Phase.Hunt;
        phaseLeft = MatchContext.HuntSeconds;
        bannerRoot.SetActive(false);
        for (int i = 0; i < startWalls.Count; i++)
        {
            if (startWalls[i] != null)
                startWalls[i].SetActive(false);
        }
        ShowCountdown("문이 열렸어요!", 2f);
    }

    void EnterResult(bool winderWon)
    {
        phase = Phase.Result;
        phaseLeft = MatchContext.ResultSeconds;
        resultWinderWon = winderWon;
        if (winderWon)
            MatchContext.WinderWins++;
        else
            MatchContext.TickToyWins++;

        bool localWon = winderWon == MatchContext.LocalIsWinder;
        resultRoot.SetActive(true);
        resultTitle.text = winderWon ? "와인더 승리!" : "틱토이 승리!";
        resultTitle.color = winderWon ? UIKit.Pink : UIKit.Mint;
        bool last = MatchContext.Round >= MatchContext.Settings.rounds;
        resultSub.text = (localWon ? "이겼어요!" : "아쉬워요!") +
                         $"   틱토이 {MatchContext.TickToyWins} : {MatchContext.WinderWins} 와인더   " +
                         (last ? "잠시 후 방으로 돌아갑니다" : "잠시 후 다음 라운드!");
        resultBandTime = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    float countdownMsgLeft;
    RectTransform resultBand;
    float resultBandTime = 99f;

    void ShowCountdown(string msg, float seconds)
    {
        countdownText.text = msg;
        countdownMsgLeft = seconds;
    }

    void Update()
    {
        phaseLeft -= Time.deltaTime;

        switch (phase)
        {
            case Phase.Hide:
                countdownText.text = MatchContext.LocalIsWinder
                    ? $"{Mathf.CeilToInt(phaseLeft)}초 뒤 문이 열려요"
                    : $"와인더 출발까지 {Mathf.CeilToInt(phaseLeft)}초";
                if (phaseLeft <= 0f)
                    EnterHunt();
                break;

            case Phase.Hunt:
                if (countdownMsgLeft > 0f)
                {
                    countdownMsgLeft -= Time.deltaTime;
                    if (countdownMsgLeft <= 0f)
                        countdownText.text = "";
                }

                // 틱토이 전멸 → 와인더 승리 / 와인더 전멸 또는 시간 끝까지 생존 → 틱토이 승리
                if (AliveTickToys() <= 0)
                    EnterResult(true);
                else if (AliveWinders() <= 0)
                    EnterResult(false);
                else if (phaseLeft <= 0f)
                    EnterResult(false);
                break;

            case Phase.Result:
                // 띠가 위아래로 펼쳐지고 글씨가 톡 튀어나옴
                resultBandTime += Time.deltaTime;
                if (resultBand != null)
                {
                    float t = Mathf.Clamp01(resultBandTime / 0.25f);
                    resultBand.localScale = new Vector3(1f, Mathf.SmoothStep(0f, 1f, t), 1f);
                    float pop = Mathf.Clamp01((resultBandTime - 0.15f) / 0.25f);
                    float sc = pop < 1f ? Mathf.Lerp(0.6f, 1.12f, pop) : Mathf.Lerp(1.12f, 1f, Mathf.Clamp01((resultBandTime - 0.4f) / 0.15f));
                    resultTitle.rectTransform.localScale = Vector3.one * sc;
                }
                if (phaseLeft <= 0f)
                    NextRound();
                break;
        }

        UpdateHud();
    }

    /// <summary>살아있는 틱토이 플레이어 수 (오프라인 테스트 봇은 살아있는 것으로 계산)</summary>
    int AliveTickToys()
    {
        int total = MatchContext.TickToyCount;
        if (MatchContext.LocalIsWinder)
            return total;
        bool localAlive = localTickToy != null && !localTickToy.IsDead;
        return total - 1 + (localAlive ? 1 : 0);
    }

    /// <summary>살아있는 와인더 수 (오프라인 테스트 봇은 살아있는 것으로 계산)</summary>
    int AliveWinders()
    {
        int total = MatchContext.WinderIds.Count;
        if (!MatchContext.LocalIsWinder)
            return total;
        bool localAlive = localWinder != null && !localWinder.IsDead;
        return total - 1 + (localAlive ? 1 : 0);
    }

    void UpdateHud()
    {
        if (hud == null)
            return;
        float remain = phase == Phase.Hunt ? Mathf.Max(0f, phaseLeft) : MatchContext.HuntSeconds;
        int hp = localWinder != null ? localWinder.Hp : (localTickToy != null ? localTickToy.Hp : 100);
        hud.SetStatus(AliveTickToys(), AliveWinders(), MatchContext.Round, remain, hp);
    }

    void NextRound()
    {
        IMatchBackend backend = MatchService.Backend;
        if (MatchContext.Round >= MatchContext.Settings.rounds)
        {
            backend.ReturnToRoom();
            return;
        }

        MatchContext.Round++;
        // 팀은 방에서 고른 그대로 유지 (무작위 배정 없음)
        MapEntry map = MapCatalog.Load().Get(MatchContext.Settings.mapIndex);
        backend.LoadMap(map.sceneName);
    }

    // ---------------- UI ----------------

    void BuildUI()
    {
        UIKit.EnsureEventSystem();
        canvas = UIKit.Canvas("RoundUI", 50);
        canvas.transform.SetParent(transform, false);
        Transform root = canvas.transform;

        // 남은 시간·라운드·남은 수는 기존 HUD(TickToyHud)가 표시 → 여기선 안내 문구만
        countdownText = UIKit.Label(root, "Countdown", "", 46, Color.white).Outlined(UIKit.Brown);
        countdownText.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1200f, 70f));

        // 역할 안내 배너
        Image banner = UIKit.Panel(root, "RoleBanner", new Color(0.12f, 0.07f, 0.04f, 0.72f), false);
        banner.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1300f, 260f));
        bannerRoot = banner.gameObject;
        bannerTitle = UIKit.Label(banner.transform, "Title", "", 86, Color.white).Outlined(UIKit.Brown, 4f);
        bannerTitle.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1250f, 110f));
        bannerSub = UIKit.Label(banner.transform, "Sub", "", 54, UIKit.Cream);
        bannerSub.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 35f), new Vector2(1250f, 80f));

        // 결과: 화면 가운데 연한 회색 띠 + 큰 글씨
        Image result = UIKit.Panel(root, "Result", new Color(0f, 0f, 0f, 0.35f), false);
        result.rectTransform.Fill();
        resultRoot = result.gameObject;
        Image band = UIKit.Panel(result.transform, "Band", new Color(0.9f, 0.9f, 0.9f, 0.95f), false);
        band.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        band.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        band.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        band.rectTransform.anchoredPosition = Vector2.zero;
        band.rectTransform.sizeDelta = new Vector2(0f, 330f);
        resultBand = band.rectTransform;
        Image lineTop = UIKit.Panel(band.transform, "LineTop", new Color(0.78f, 0.78f, 0.78f, 1f), false);
        lineTop.rectTransform.anchorMin = new Vector2(0f, 1f);
        lineTop.rectTransform.anchorMax = new Vector2(1f, 1f);
        lineTop.rectTransform.pivot = new Vector2(0.5f, 1f);
        lineTop.rectTransform.sizeDelta = new Vector2(0f, 8f);
        lineTop.rectTransform.anchoredPosition = Vector2.zero;
        Image lineBottom = UIKit.Panel(band.transform, "LineBottom", new Color(0.78f, 0.78f, 0.78f, 1f), false);
        lineBottom.rectTransform.anchorMin = new Vector2(0f, 0f);
        lineBottom.rectTransform.anchorMax = new Vector2(1f, 0f);
        lineBottom.rectTransform.pivot = new Vector2(0.5f, 0f);
        lineBottom.rectTransform.sizeDelta = new Vector2(0f, 8f);
        lineBottom.rectTransform.anchoredPosition = Vector2.zero;
        resultTitle = UIKit.Label(band.transform, "Title", "", 150, Color.white).Outlined(UIKit.Brown, 6f);
        resultTitle.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 35f), new Vector2(1700f, 200f));
        resultSub = UIKit.Label(band.transform, "Sub", "", 42, UIKit.Brown);
        resultSub.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1700f, 60f));
        resultRoot.SetActive(false);
    }
}
