using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 맵(CITY) 안 라운드 진행.
/// 1) 역할 안내 + 20초 숨기 (와인더는 시작 지점에 갇힘)
/// 2) 문 열림 → 3분 술래잡기
/// 3) 틱토이 전멸 = 와인더 승리 / 와인더 전멸 또는 시간 끝까지 생존 = 틱토이 승리
/// 4) 같은 팀으로 다음 라운드 → 마지막 라운드 후 방으로
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

    /// <summary>와인더 방 문이 열렸는지 (숨는 시간 끝). 문이 열려야 태엽으로 능력을 얻음. 라운드 진행이 없는 씬은 항상 열림</summary>
    public static bool DoorOpen => Instance == null || Instance.phase != Phase.Hide;

    static bool hooked;

    Phase phase;
    float phaseLeft;
    GameObject localPlayer;
    TickToyMove localTickToy;
    WinderMove localWinder;
    readonly List<GameObject> startWalls = new List<GameObject>();
    TickToyHud hud;
    bool resultWinderWon;
    bool nextRoundRequested;

    // ---- 네트워크(Fusion)에서 쓰는 값 ----
    public GameObject LocalPlayerObject => localPlayer;
    public bool LocalIsWinderRole => localWinder != null;
    public int LocalCharIndex { get; private set; }
    public bool LocalAlive => localWinder != null ? !localWinder.IsDead : (localTickToy != null && !localTickToy.IsDead);

    /// <summary>내 캐릭터 상태 깃발 (다른 사람 화면에 똑같이 보이게: 정지·달리기·황금 몽둥이·덫)</summary>
    public int LocalFlags
    {
        get
        {
            int f = 0;
            if (localWinder != null)
            {
                if (localWinder.IsFrozen) f |= NetPose.FlagFrozen;
                if (localWinder.RunTimeLeft > 0f) f |= NetPose.FlagRunning;
                if (localWinder.IsGoldenClub) f |= NetPose.FlagGolden;
                if (localWinder.SpringPaused) f |= NetPose.FlagSpringPaused;
            }
            else if (localTickToy != null)
            {
                if (localTickToy.State == TickToyMove.TickToyState.Run) f |= NetPose.FlagRunning;
                if (localTickToy.IsTrapFrozen) f |= NetPose.FlagTrapped;
                if (localTickToy.HitFaceActive) f |= NetPose.FlagHitFace;
                if (localTickToy.IsFainted) f |= NetPose.FlagFainted;
            }
            return f;
        }
    }
    public int PhaseIndex => (int)phase;
    public float PhaseLeft => phaseLeft;
    public bool ResultWinderWon => resultWinderWon;

    /// <summary>온라인이고 방장이 아님 → 라운드 진행은 방장이 보낸 대로</summary>
    static bool NetFollower => FusionMatchBackend.Online != null && !FusionMatchBackend.Online.IsHost;

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
        DemoSceneAIBootstrap.SkipAISpawn = false;
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
        // 온라인에서 방장이 아니면 맵 AI 는 방장 것을 따라 보여주므로 직접 만들지 않음
        DemoSceneAIBootstrap.SkipAISpawn = NetFollower;
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
        SuspectMarks.Clear(); // 이전 라운드의 의심 표시가 새 라운드 캐릭터에 남지 않게
        BuildUI();
        hud = FindAnyObjectByType<TickToyHud>();
        FindStartWalls();
        SpawnLocalPlayer();
        if (hud != null)
            hud.SetDisguiseCount(localTickToy != null ? DisguiseLeft : -1); // 와인더는 숨김
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
            LocalCharIndex = Random.Range(0, prefabs.tickToyPrefabs.Length);
            GameObject prefab = prefabs.tickToyPrefabs[LocalCharIndex];
            Vector3 pos = RandomRoadPoint();
            localPlayer = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            localTickToy = localPlayer.GetComponent<TickToyMove>();
        }

        localPlayer.name = (MatchContext.LocalIsWinder ? "Winder_" : "TickToy_") + LocalNickname();
        localPlayer.SetActive(true);
        TickToyMove.KeepOneAudioListener();
    }

    /// <summary>
    /// 와인더 시작 위치: DemoScene 에 직접 놓은 WinderStartingPosition 방 안, WinderStarting 주변.
    /// 방 바닥 위에서 벽/물체와 안 겹치고 서로 1.6m 이상 떨어진 자리만 골라 순서대로 배정 (모든 클라이언트가 같은 결과).
    /// 모두 문(WinderStartingWall) 쪽을 봄. 씬 배치는 건드리지 않음.
    /// </summary>
    public void WinderSpawn(int index, out Vector3 pos, out Quaternion rot)
    {
        if (winderSpawnPoints.Count == 0)
            BuildWinderSpawnPoints();
        rot = winderSpawnRot;
        pos = winderSpawnPoints[Mathf.Abs(index) % winderSpawnPoints.Count];
    }

    readonly List<Vector3> winderSpawnPoints = new List<Vector3>();
    Quaternion winderSpawnRot = Quaternion.identity;
    const float WinderSpawnGap = 1.6f;
    const float WinderCapsuleRadius = 0.45f;

    void BuildWinderSpawnPoints()
    {
        winderSpawnPoints.Clear();
        bool hasRoom = TryGetRoomBounds(out Bounds room);
        Transform start = FindWinderStart();
        Transform roomRoot = FindActiveByName("WinderStartingPosition");
        Vector3 anchor = start != null ? start.position : (hasRoom ? room.center : Vector3.zero);

        // 문(벽): anchor 에서 가장 가까운 WinderStarting...Wall
        GameObject wall = null;
        float bestWall = float.MaxValue;
        foreach (GameObject w in startWalls)
        {
            if (w == null) continue;
            float d = (w.transform.position - anchor).sqrMagnitude;
            if (d < bestWall) { bestWall = d; wall = w; }
        }

        // 문 쪽 방향 = 벽의 얇은 축(앞뒤) 중 anchor → 벽 방향
        Vector3 forward = Vector3.forward;
        if (wall != null)
        {
            Vector3 toWall = wall.transform.position - anchor;
            toWall.y = 0f;
            Vector3 n = wall.transform.forward;
            n.y = 0f;
            if (n.sqrMagnitude > 0.01f)
                forward = Vector3.Dot(n, toWall) >= 0f ? n.normalized : -n.normalized;
            else if (toWall.sqrMagnitude > 0.01f)
                forward = toWall.normalized;
        }
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        winderSpawnRot = Quaternion.LookRotation(forward, Vector3.up);

        // anchor 주변 격자 후보 → anchor 에서 가까운 순
        List<Vector3> candidates = new List<Vector3>();
        for (int r = -4; r <= 4; r++)
            for (int c = -5; c <= 5; c++)
                candidates.Add(anchor + right * (c * WinderSpawnGap) + forward * (r * WinderSpawnGap));
        candidates.Sort((a, b) =>
        {
            int cmp = (a - anchor).sqrMagnitude.CompareTo((b - anchor).sqrMagnitude);
            if (cmp != 0) return cmp;
            cmp = Vector3.Dot(b - a, forward).CompareTo(0f); // 같은 거리면 문 쪽 먼저
            if (cmp != 0) return cmp;
            return Vector3.Dot(a - b, right).CompareTo(0f);
        });

        foreach (Vector3 c in candidates)
        {
            if (winderSpawnPoints.Count >= 16)
                break;
            // 문 안쪽(방 쪽)에만
            if (wall != null && Vector3.Dot(c - wall.transform.position, forward) > -0.8f)
                continue;
            if (!TryRoomFloor(c, anchor.y, roomRoot, out float y))
                continue;
            Vector3 p = new Vector3(c.x, y, c.z);
            if (!IsSpawnClear(p))
                continue;
            bool tooClose = false;
            foreach (Vector3 q in winderSpawnPoints)
            {
                Vector3 d = q - p; d.y = 0f;
                if (d.sqrMagnitude < (WinderSpawnGap - 0.05f) * (WinderSpawnGap - 0.05f)) { tooClose = true; break; }
            }
            if (!tooClose)
                winderSpawnPoints.Add(p);
        }

        if (winderSpawnPoints.Count == 0)
        {
            // 예비: 예전 방식 4x2
            Bounds b = hasRoom ? room : new Bounds(anchor, Vector3.one * 4f);
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = anchor + right * ((i % 4 - 1.5f) * WinderSpawnGap) + forward * ((0.5f - i / 4) * WinderSpawnGap);
                p.y = FloorY(p, b);
                winderSpawnPoints.Add(p);
            }
            Debug.LogWarning("[RoundManager] WinderStartingPosition 방 바닥을 못 찾아서 기본 배치로 스폰해요.");
        }
    }

    static Transform FindActiveByName(string name)
    {
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.name == name)
                return t;
        return null;
    }

    /// <summary>방 바닥 높이 (위에서 아래로 쏴서 제일 높은 바닥; 방 오브젝트의 바닥이어야 함)</summary>
    static bool TryRoomFloor(Vector3 p, float anchorY, Transform roomRoot, out float y)
    {
        y = 0f;
        Vector3 origin = new Vector3(p.x, anchorY + 2f, p.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        Transform bestT = null;
        foreach (RaycastHit h in hits)
        {
            if (h.collider.attachedRigidbody != null)
                continue;
            if (h.point.y > best)
            {
                best = h.point.y;
                bestT = h.collider.transform;
                if (h.normal.y < 0.6f) bestT = null; // 맨 위가 경사/벽이면 실패
            }
        }
        if (bestT == null)
            return false;
        if (roomRoot != null && !bestT.IsChildOf(roomRoot))
            return false;
        y = best;
        return true;
    }

    /// <summary>캐릭터 캡슐 자리에 벽/물체(정적 콜라이더)가 없는지</summary>
    static bool IsSpawnClear(Vector3 feet)
    {
        Vector3 a = feet + Vector3.up * (WinderCapsuleRadius + 0.1f);
        Vector3 b = feet + Vector3.up * 1.7f;
        foreach (Collider c in Physics.OverlapCapsule(a, b, WinderCapsuleRadius + 0.1f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.attachedRigidbody != null)
                continue;
            return false;
        }
        return true;
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
        GameObject floorsGo = GameObject.Find("Floors");
        Transform floors = floorsGo != null ? floorsGo.transform : null;
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
            // 맨 위에 맞은 게 바닥 타일(Floors)이어야 함 — 지붕·투명 벽·난간 위(공중) 스폰 방지
            if (floors != null && !hit.collider.transform.IsChildOf(floors))
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

        // 못 찾았으면 맵 가운데 바닥으로 (공중 X)
        if (!hasFallback && Physics.Raycast(new Vector3(area.center.x, area.max.y + 50f, area.center.z), Vector3.down, out RaycastHit h, 500f, ~0, QueryTriggerInteraction.Ignore))
            fallback = h.point;
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
        // 문이 열림 → GAME START (주황 글씨 + 갈색 테두리, 크게) + 시작 소리 (모두 각자 화면에서 같은 순간에 재생)
        countdownText.fontSize = 72;
        countdownText.color = UIKit.Orange;
        ShowCountdown("GAME START", 2f);
        GameAudio.PlayHere(SoundId.GameStart); // GAME START 효과음 (각자 화면에서 같은 순간에)
    }

    void EnterResult(bool winderWon, bool countWin = true)
    {
        phase = Phase.Result;
        phaseLeft = MatchContext.ResultSeconds;
        resultWinderWon = winderWon;
        if (countWin)
        {
            if (winderWon)
                MatchContext.WinderWins++;
            else
                MatchContext.TickToyWins++;
        }

        resultRoot.SetActive(true);
        resultTitle.text = winderWon ? "와인더 승리!" : "틱토이 승리!";
        resultTitle.color = winderWon ? UIKit.Pink : UIKit.Mint;
        bool last = MatchContext.Round >= MatchContext.Settings.rounds;
        resultSub.text = last ? "잠시 후 방으로 돌아갑니다" : "다음 라운드가 곧 시작됩니다";
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
        UpdateSpectate();
        SuspectMarks.Tick();
        UpdateWinderMark();
        UpdateWinderAim();
        UpdateDisguise();
        if (aborted)
            return;
        phaseLeft -= Time.deltaTime;

        switch (phase)
        {
            case Phase.Hide:
                countdownText.text = MatchContext.LocalIsWinder
                    ? $"{Mathf.CeilToInt(phaseLeft)}초 뒤 문이 열려요"
                    : $"와인더 출발까지 {Mathf.CeilToInt(phaseLeft)}초";
                if (phaseLeft <= 0f && !NetFollower)
                    EnterHunt();
                break;

            case Phase.Hunt:
                if (countdownMsgLeft > 0f)
                {
                    countdownMsgLeft -= Time.deltaTime;
                    if (countdownMsgLeft <= 0f)
                        countdownText.text = "";
                }

                // 틱토이 전멸 → 와인더 승리 / 와인더 전멸 또는 시간 끝까지 생존 → 틱토이 승리 (온라인은 방장이 판정)
                if (NetFollower)
                    break;
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
                if (phaseLeft <= 0f && !NetFollower && !nextRoundRequested)
                {
                    nextRoundRequested = true;
                    NextRound();
                }
                break;
        }

        UpdateHud();
    }

    /// <summary>살아있는 틱토이 플레이어 수 (오프라인 테스트 봇은 살아있는 것으로 계산)</summary>
    int AliveTickToys()
    {
        if (FusionMatchBackend.Online != null)
            return FusionMatchBackend.Online.AliveCount(false, MatchContext.TickToyCount);
        int total = MatchContext.TickToyCount;
        if (MatchContext.LocalIsWinder)
            return total;
        bool localAlive = localTickToy != null && !localTickToy.IsDead;
        return total - 1 + (localAlive ? 1 : 0);
    }

    /// <summary>살아있는 와인더 수 (오프라인 테스트 봇은 살아있는 것으로 계산)</summary>
    int AliveWinders()
    {
        if (FusionMatchBackend.Online != null)
            return FusionMatchBackend.Online.AliveCount(true, MatchContext.WinderIds.Count);
        int total = MatchContext.WinderIds.Count;
        if (!MatchContext.LocalIsWinder)
            return total;
        bool localAlive = localWinder != null && !localWinder.IsDead;
        return total - 1 + (localAlive ? 1 : 0);
    }

    /// <summary>(온라인, 방장 아님) 방장이 보낸 라운드 상태 반영</summary>
    public void ApplyNetPhase(int netPhase, float left, bool winderWon, int tickToyWins, int winderWins)
    {
        Phase target = (Phase)netPhase;
        if (target != phase)
        {
            if (target == Phase.Hunt && phase == Phase.Hide)
                EnterHunt();
            else if (target == Phase.Result)
            {
                MatchContext.TickToyWins = tickToyWins;
                MatchContext.WinderWins = winderWins;
                EnterResult(winderWon, false);
            }
        }
        phaseLeft = left;
    }

    /// <summary>(온라인) 다른 사람의 와인더에게 맞음</summary>
    /// <summary>(온라인) 틱토이가 내 와인더 등 뒤에서 E → 태엽 정지</summary>
    public void ApplyWinderStop(float seconds)
    {
        if (localWinder != null && !localWinder.IsDead)
            localWinder.Freeze(seconds);
    }

    // ---------------- 와인더 Z: 의심되는 틱토이 표시 ----------------
    const float MarkCooldown = 10f; // 한 번 표시하면 10초 뒤에 다시 (같은 대상이든 다른 대상이든)
    const float MarkRange = 60f;
    float markReadyTime;
    static readonly RaycastHit[] markHits = new RaycastHit[32];

    void UpdateWinderMark()
    {
        if (localWinder == null || localWinder.IsDead || GameSettings.MenuOpen || !Input.GetKeyDown(KeyCode.Z))
            return;
        if (Time.time < markReadyTime)
            return;

        Camera cam = localWinder.ViewCamera != null ? localWinder.ViewCamera : Camera.main;
        if (cam == null)
            return;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)); // 조준점
        // 정확히 맞으면 그 대상, 아니면 조금 넉넉하게(반지름 0.35) 한 번 더
        if (!FindMarkTarget(Physics.RaycastNonAlloc(ray, markHits, MarkRange, ~0, QueryTriggerInteraction.Ignore), out int id, out GameObject obj)
            && !FindMarkTarget(Physics.SphereCastNonAlloc(ray, 0.35f, markHits, MarkRange, ~0, QueryTriggerInteraction.Ignore), out id, out obj))
            return;

        markReadyTime = Time.time + MarkCooldown;
        SuspectMarks.Add(id, obj);
        if (id != 0)
            FusionMatchBackend.Online?.SendMark(id);
    }

    /// <summary>가장 가까운 것부터: 틱토이(사람 복사본·AI)면 대상, 벽 같은 게 먼저 막으면 실패</summary>
    bool FindMarkTarget(int count, out int id, out GameObject obj)
    {
        id = 0;
        obj = null;
        System.Array.Sort(markHits, 0, count, Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));
        FusionMatchBackend online = FusionMatchBackend.Online;
        for (int i = 0; i < count; i++)
        {
            Collider c = markHits[i].collider;
            if (c == null || c.transform.IsChildOf(localWinder.transform))
                continue;

            NetPuppet puppet = c.GetComponentInParent<NetPuppet>();
            if (puppet != null)
            {
                if (puppet.Kind == NetPose.KindWinder || !puppet.Alive)
                    return false;
                id = puppet.NetId;
                obj = puppet.gameObject;
                return true;
            }

            TickToyAI ai = c.GetComponentInParent<TickToyAI>();
            if (ai != null)
            {
                if (ai.IsDead)
                    return false;
                id = online != null ? online.AiNetId(ai) : 0;
                obj = ai.gameObject;
                return true;
            }
            return false; // 벽·바닥 등에 막힘
        }
        return false;
    }

    // ---------------- 와인더 조준점 + 근처 틱토이 힌트 바 ----------------
    const float HintStartLeft = 120f; // 남은 시간이 2:00 이 되면 힌트 바가 나타남
    const float HintFullDistance = 10f;  // 이 거리 안에 틱토이 플레이어가 있으면 가득
    const float HintEmptyDistance = 35f; // 이 거리보다 멀면 비어 있음 (범위를 넓게 → 한 마리씩 찾기엔 부족한 힌트)
    WinderAimHud winderAimHud;
    float hintFill;

    void UpdateWinderAim()
    {
        if (winderAimHud == null)
            winderAimHud = FindAnyObjectByType<WinderAimHud>(FindObjectsInactive.Include);
        if (winderAimHud == null)
            return;

        bool aim = localWinder != null && !localWinder.IsDead && !aborted;
        bool bar = aim && phase == Phase.Hunt && phaseLeft <= HintStartLeft;
        winderAimHud.SetVisible(aim, bar);
        if (!bar)
        {
            hintFill = 0f;
            winderAimHud.SetFill(0f);
            return;
        }

        // 가장 가까운 살아있는 틱토이 '플레이어' (맵 AI 는 세지 않음, 변장해도 셈)
        float nearest = float.MaxValue;
        Vector3 me = localWinder.transform.position;
        foreach (NetCharacter nc in NetCharacter.Players.Values)
        {
            if (nc == null || nc.Kind != NetPose.KindTickToy || !nc.Alive || !nc.HasPose)
                continue;
            nearest = Mathf.Min(nearest, Vector3.Distance(me, nc.Pos));
        }
        float target = Mathf.InverseLerp(HintEmptyDistance, HintFullDistance, nearest);
        target = Mathf.Round(target * 5f) / 5f; // 5칸으로만 → 정확한 위치는 안 알려 줌
        hintFill = Mathf.MoveTowards(hintFill, target, Time.deltaTime);
        winderAimHud.SetFill(hintFill);
    }

    // ---------------- 틱토이 변장 (Q) ----------------
    const int DisguiseUses = 5;
    const float DisguiseReach = 2.5f;
    public int DisguiseLeft { get; private set; } = DisguiseUses;

    void UpdateDisguise()
    {
        if (localTickToy == null || localTickToy.IsDead || DisguiseLeft <= 0 || GameSettings.MenuOpen || !Input.GetKeyDown(KeyCode.Q))
            return;
        if (!localTickToy.CanDisguise)
            return;
        MatchPrefabs prefabs = MatchPrefabs.Load();
        if (prefabs == null || prefabs.tickToyPrefabs == null)
            return;

        int ch = NearestAiChar(localTickToy.transform.position);
        if (ch < 0 || ch == LocalCharIndex || ch >= prefabs.tickToyPrefabs.Length)
            return; // 근처에 AI 가 없거나 이미 같은 모양

        localTickToy = localTickToy.DisguiseAs(prefabs.tickToyPrefabs[ch]);
        localPlayer = localTickToy.gameObject;
        LocalCharIndex = ch; // 다른 사람 화면의 내 복사본도 이 모양으로 바뀜
        DisguiseLeft--;
        TickToyMove.KeepOneAudioListener();
        if (hud != null)
            hud.SetDisguiseCount(DisguiseLeft);
    }

    /// <summary>가장 가까운 틱토이 AI 의 캐릭터 번호 (없으면 -1). 방장·오프라인은 진짜 AI, 참가자는 AI 복사본</summary>
    static int NearestAiChar(Vector3 pos)
    {
        int best = -1;
        float bestDist = DisguiseReach;
        foreach (TickToyAI ai in FindObjectsByType<TickToyAI>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(pos, ai.transform.position);
            if (!ai.IsDead && d < bestDist)
            {
                bestDist = d;
                best = FusionMatchBackend.CharIndexOf(ai.gameObject);
            }
        }
        foreach (NetPuppet p in FindObjectsByType<NetPuppet>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(pos, p.transform.position);
            if (p.Kind == NetPose.KindAI && p.Alive && d < bestDist)
            {
                bestDist = d;
                best = p.Ch;
            }
        }
        return best;
    }

    // ---------------- 관전 (죽은 틱토이 → 살아있는 다른 틱토이 플레이어 화면) ----------------
    int spectateId;
    readonly List<NetCharacter> spectateCandidates = new List<NetCharacter>();
    SpectatorBadge spectatorBadge;

    /// <summary>내가 관전 중인 틱토이 플레이어 id (0 = 안 봄). 와인더는 관전하지 않음</summary>
    public int SpectateTargetId => spectateId;

    void UpdateSpectate()
    {
        FusionMatchBackend online = FusionMatchBackend.Online;
        bool canSpectate = online != null && localTickToy != null && localTickToy.IsDead;
        NetCharacter target = null;
        if (canSpectate)
        {
            spectateCandidates.Clear();
            foreach (NetCharacter nc in NetCharacter.Players.Values)
            {
                if (nc != null && nc.NetId != online.LocalPlayerId && nc.Kind == NetPose.KindTickToy && nc.Alive
                    && nc.Puppet != null && nc.Puppet.gameObject.activeInHierarchy)
                    spectateCandidates.Add(nc);
            }
            spectateCandidates.Sort((a, b) => a.NetId.CompareTo(b.NetId));

            int index = spectateCandidates.FindIndex(nc => nc.NetId == spectateId);
            if (index < 0 && spectateCandidates.Count > 0)
                index = 0; // 보던 사람이 죽거나 나가면 다음 사람
            // 좌클릭: 다음 사람 보기
            if (index >= 0 && spectateCandidates.Count > 1 && !GameSettings.MenuOpen && Input.GetMouseButtonDown(0))
                index = (index + 1) % spectateCandidates.Count;
            if (index >= 0)
                target = spectateCandidates[index];
        }

        spectateId = target != null ? target.NetId : 0;
        if (localTickToy != null)
            localTickToy.SpectateTarget = target != null ? target.Puppet.transform : null;

        // 왼쪽 위 눈동자 표시: 살아있는 내 틱토이를 보는 사람 수 / 내가 관전 중인 사람을 보는 사람 수
        if (spectatorBadge == null)
            spectatorBadge = FindAnyObjectByType<SpectatorBadge>(FindObjectsInactive.Include);
        if (spectatorBadge != null)
        {
            int shownId = 0;
            if (online != null)
            {
                if (spectateId != 0)
                    shownId = spectateId;
                else if (localTickToy != null && !localTickToy.IsDead)
                    shownId = online.LocalPlayerId;
            }
            spectatorBadge.SetCount(NetCharacter.SpectatorCount(shownId));
        }
    }

    // ---------------- 게임 중단 (상대가 나감 / 방장이 나감) ----------------
    bool aborted;

    /// <summary>게임을 멈추고 가운데 띠에 안내 → 잠시 뒤 방으로 (방으로 이동은 호출한 쪽이 처리)</summary>
    public void ShowMatchAbort(string message)
    {
        aborted = true;
        phase = Phase.Result;
        bannerRoot.SetActive(false);
        countdownText.text = "";
        resultRoot.SetActive(true);
        resultTitle.text = "게임 종료";
        resultTitle.color = UIKit.Orange;
        resultSub.text = message + "  잠시 후 방으로 돌아갑니다";
        resultBand.localScale = Vector3.one;
        resultTitle.rectTransform.localScale = Vector3.one;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ApplyNetHit(int damage)
    {
        if (localTickToy != null && !localTickToy.IsDead)
            localTickToy.TakeHit(damage);
        else if (localWinder != null && !localWinder.IsDead)
            localWinder.TakeHit(damage);
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
        countdownText.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -245f), new Vector2(1200f, 70f)); // ROUND 배지 아래

        // 역할 안내 배너
        Image banner = UIKit.Panel(root, "RoleBanner", new Color(0.12f, 0.07f, 0.04f, 0.72f), false);
        banner.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(1300f, 260f));
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
