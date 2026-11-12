using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class TickToyMove : MonoBehaviour
{
    public enum TickToyState
    {
        Play,
        Run,
        Faint,
        Dead
    }

    public Vector2 MoveInput { get; private set; }
    public bool IsWinding { get; set; }
    /// <summary>죽은 뒤 관전 중인 틱토이 (카메라가 따라감, null = 내 몸)</summary>
    public Transform SpectateTarget { get; set; }
    public TickToyState State { get; private set; } = TickToyState.Play;

    [Header("TPS Camera")]
    [SerializeField] private Transform cameraPivot;
    [Tooltip("뒤로 빠지는 거리. 크면 멀어지고, 캐릭터가 안 보이면 줄이세요.")]
    [SerializeField] private float cameraDistance = 1.1f;
    [Tooltip("카메라 높이. 크면 위에서 내려다봅니다.")]
    [SerializeField] private float cameraHeight = 0.55f;
    [Tooltip("앞을 보는 정도. 크면 시선이 멀리 가서 캐릭터가 화면 아래쪽에 보입니다.")]
    [SerializeField] private float cameraLookAhead = 4f;
    [Tooltip("시작할 때 위아래 각도.")]
    [SerializeField] private float cameraStartPitch = 12f;
    [Tooltip("달리기(능력) 쓰는 순간 한 번 나오는 흙먼지 바람 이펙트 (프리팹 자식 RunWhoosh, 약 2초)")]
    [SerializeField] private GameObject runEffect;
    private ParticleSystem[] runParticles;
    private bool wasRunning;
    private const float CameraPitchScale = 0.22f;
    private const float FixedFieldOfView = 78f;

    private TickToyAnimation animationController;

    [SerializeField] private float moveSpeed = 3f; // 걷기 3, 달리기 ×2 = 6
    [Tooltip("이 높이보다 낮은 턱은 점프 없이 걸어 올라갑니다. 단위는 미터입니다.")]
    [SerializeField] private float walkLipHeight = 0.45f;
    int physicsBodyId;
    int stepFrame = -1;
    float cachedFeetY;
    Vector3 cachedWish;
    bool cachedCanStep;
    [SerializeField] private float proneMoveSpeedMultiplier = 0.5f;
    [SerializeField] private float sensitivity = 200f;

    private float cameraPitch;
    private Rigidbody rb;
    private Collider bodyCollider;
    private CapsuleCollider bodyCapsule;
    private float standCapsuleHeight;
    private float standCapsuleRadius;
    private int standCapsuleDirection;
    private Vector3 standColliderCenter;
    private bool standColliderCached;
    private Animator bodyAnimator;
    private Camera tpsCamera;
    private Transform body;
    private Vector3 bodyRestLocalPosition;
    private float bodyHeight = 2.2f;
    private float focusHeight = 1.1f;
    private float yaw;
    private float JumpPower = 5f;

    private bool isGrounded = true;
    private bool leftGround;
    private float restTime;
    private float jumpLock;
    private float jumpCooldown;
    private bool jumped;
    private float jumpAscentY;
    private Collider stepIgnored;
    private float vaultTopY;
    private float stepIgnoreUntil;
    private bool vaultWallPass;
    private bool vaultIgnoredCollision;
    private Vector3 vaultDir;
    private static readonly RaycastHit[] GroundHits = new RaycastHit[8];
    private bool isProne = false;
    private float runTimeLeft;
    private bool faintCanBreak;
    private bool waitStandRelease;
    private bool faintOnGround;
    private float faintTimeLeft;
    private bool deadSettled;
    private float duplicateTimeLeft;
    // Instantiate 복제 시 원본 분신 리스트가 같이 복사·삭제되지 않게
    [System.NonSerialized] private List<GameObject> duplicates = new List<GameObject>();
    [System.NonSerialized] private bool skipClearDuplicates;
    private static Material duplicateCloudMaterial;
    private int grantedAbility;
    private int grantedLevel;
    private float abilityDuration;
    private float runDuration;
    private float faintDuration;
    private int hp = 100;
    private float trapFreezeLeft;
    private bool trapHeldBody;

    const float WinderReach = 2.4f;

    public WinderMove FindWinderInReach()
    {
        if (!CanReachWinder(out Vector3 look))
            return null;

        WinderMove[] winders = FindObjectsByType<WinderMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        WinderMove closest = null;
        float closestDistance = WinderReach;
        for (int i = 0; i < winders.Length; i++)
        {
            if (winders[i].IsDead)
                continue;
            if (SpringReachable(winders[i].Spring, winders[i].transform, look, out float distance) && distance < closestDistance)
            {
                closestDistance = distance;
                closest = winders[i];
            }
        }
        return closest;
    }

    /// <summary>온라인: 다른 사람이 조작하는 와인더(복사본)의 등 태엽이 닿는 거리인지</summary>
    public NetPuppet FindWinderPuppetInReach()
    {
        if (FusionMatchBackend.Online == null || !CanReachWinder(out Vector3 look))
            return null;

        NetPuppet[] puppets = FindObjectsByType<NetPuppet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        NetPuppet closest = null;
        float closestDistance = WinderReach;
        for (int i = 0; i < puppets.Length; i++)
        {
            NetPuppet p = puppets[i];
            if (p.Kind != NetPose.KindWinder || !p.Alive)
                continue;
            if (SpringReachable(p.Spring, p.transform, look, out float distance) && distance < closestDistance)
            {
                closestDistance = distance;
                closest = p;
            }
        }
        return closest;
    }

    bool CanReachWinder(out Vector3 look)
    {
        look = Vector3.zero;
        if (IsFainted)
            return false;
        if (cameraPivot == null)
            EnsureTpsCamera();
        if (cameraPivot == null)
            return false;
        look = cameraPivot.forward;
        if (look.sqrMagnitude < 0.0001f)
            return false;
        look.Normalize();
        return true;
    }

    /// <summary>와인더 등 뒤에서, 태엽을 바라보고, 손이 닿는 거리인지</summary>
    bool SpringReachable(Transform spring, Transform winder, Vector3 look, out float distance)
    {
        distance = float.MaxValue;
        if (spring == null)
            return false;

        Vector3 toSpringFlat = spring.position - transform.position;
        toSpringFlat.y = 0f;
        distance = toSpringFlat.magnitude;
        if (distance > WinderReach || distance < 0.05f)
            return false;

        Vector3 back = spring.position - winder.position;
        back.y = 0f;
        if (back.sqrMagnitude < 0.0004f)
        {
            back = -winder.forward;
            back.y = 0f;
        }
        if (back.sqrMagnitude < 0.0001f)
            return false;
        back.Normalize();

        Vector3 fromWinder = transform.position - winder.position;
        fromWinder.y = 0f;
        if (fromWinder.sqrMagnitude < 0.0001f || Vector3.Dot(fromWinder.normalized, back) < 0.45f)
            return false;

        Vector3 toSpring = spring.position - cameraPivot.position;
        return toSpring.sqrMagnitude >= 0.0001f && Vector3.Angle(look, toSpring) <= 34f;
    }

    public bool IsFainted => State == TickToyState.Faint;

    /// <summary>변장할 수 있는 상태 (살아있고, 기절한 척·덫·태엽 감기 중이 아님)</summary>
    public bool CanDisguise => (State == TickToyState.Play || State == TickToyState.Run) && !IsTrapFrozen && !IsWinding;

    /// <summary>변장: 같은 자리에 다른 틱토이 모형으로 다시 만들고 HP·능력·달리기 상태를 그대로 옮김</summary>
    public TickToyMove DisguiseAs(GameObject prefab)
    {
        Vector3 pos = rb != null ? rb.position : transform.position;
        GameObject go = Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f));
        go.name = gameObject.name;
        TickToyMove next = go.GetComponent<TickToyMove>();
        next.hp = hp;
        next.yaw = yaw;
        next.State = State;
        next.runTimeLeft = runTimeLeft;
        next.runDuration = runDuration;
        next.abilityDuration = abilityDuration;
        next.duplicateTimeLeft = duplicateTimeLeft;
        next.grantedAbility = grantedAbility;
        next.grantedLevel = grantedLevel;
        next.wasRunning = wasRunning; // 달리기 이펙트가 다시 터지지 않게

        gameObject.SetActive(false); // 같은 자리에서 몸끼리 부딪히지 않게 바로 끔
        Destroy(gameObject);
        return next;
    }
    public bool HitFaceActive => animationController != null && animationController.HitFaceActive;
    public int Hp => hp;
    public bool FaintCancelled { get; private set; }
    public bool IsUsingAbility =>
        State == TickToyState.Run || State == TickToyState.Faint
        || runTimeLeft > 0f || duplicateTimeLeft > 0f;
    /// <summary>타이머 UI용 총 시간. 분신 사용이 달리기/드러눕기 값을 덮어쓰지 않음.</summary>
    public float AbilityDuration
    {
        get
        {
            if (State == TickToyState.Run || runTimeLeft > 0f)
                return runDuration;
            if (IsFainted)
                return faintDuration;
            return abilityDuration;
        }
    }

    public float AbilityTimeLeft
    {
        get
        {
            if (State == TickToyState.Run || runTimeLeft > 0f)
                return runTimeLeft;
            if (IsFainted)
                return faintTimeLeft;
            if (duplicateTimeLeft > 0f)
                return duplicateTimeLeft;
            return faintTimeLeft;
        }
    }

    public bool IsGrounded => isGrounded;
    public bool CanJump => jumpLock <= 0f && jumpCooldown <= 0f && !leftGround;

    public bool IsTrapFrozen => trapFreezeLeft > 0f;

    bool IsTickToyCameraActive()
    {
        if (tpsCamera != null && tpsCamera.enabled && tpsCamera.gameObject.activeInHierarchy)
            return true;
        if (cameraPivot == null)
            return false;
        Camera cam = cameraPivot.GetComponent<Camera>();
        return cam != null && cam.enabled && cam.gameObject.activeInHierarchy;
    }

    /// <summary>와인더 덫 — 지정 초 동안 이동·입력 불가.</summary>
    public void TrapFreeze(float seconds)
    {
        if (State == TickToyState.Dead)
            return;

        trapFreezeLeft = Mathf.Max(0.05f, seconds);
        MoveInput = Vector2.zero;
        if (animationController != null)
            animationController.SetMove(false);
        if (bodyAnimator != null)
            bodyAnimator.speed = 0f;

        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            trapHeldBody = true;
        }
    }

    void EndTrapFreeze()
    {
        trapFreezeLeft = 0f;
        if (bodyAnimator != null)
            bodyAnimator.speed = 1f;

        if (!trapHeldBody || rb == null)
            return;

        trapHeldBody = false;
        if (State != TickToyState.Dead && State != TickToyState.Faint)
            rb.isKinematic = false;
    }

    public bool IsDead
    {
        get { return State == TickToyState.Dead; }
        set
        {
            if (value)
                EnterDead();
        }
    }

    void Awake()
    {
        animationController = GetComponent<TickToyAnimation>();
        rb = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<CapsuleCollider>();
        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider>();
        if (rb != null)
            physicsBodyId = rb.GetInstanceID();
        if (bodyCollider != null)
            bodyCollider.hasModifiableContacts = true;
        CacheStandCollider();
        // 콜라이더 모양이 바뀔 때 바닥에서 튕겨나가는 속도 제한
        if (rb != null)
            rb.maxDepenetrationVelocity = 2f;
        EnsureTpsCamera();
        KeepOneAudioListener();
    }

    public static void KeepOneAudioListener()
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // 이미 켜진 리스너가 있으면 그걸 유지하고, 나머지만 끔 (스폰 중 플레이어 리스너가 꺼지는 것 방지)
        AudioListener keep = null;
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener l = listeners[i];
            if (l == null)
                continue;
            if (l.enabled && l.gameObject.activeInHierarchy)
            {
                keep = l;
                break;
            }
        }

        if (keep == null)
        {
            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener l = listeners[i];
                if (l != null && l.gameObject.activeInHierarchy && l.GetComponentInParent<TickToyAI>() == null)
                {
                    keep = l;
                    break;
                }
            }
        }

        if (keep == null && listeners.Length > 0)
            keep = listeners[0];

        // 씬에 리스너가 아예 없으면 하나 생성 (경고 스팸 방지)
        if (keep == null)
        {
            Camera main = Camera.main;
            if (main != null)
            {
                keep = main.GetComponent<AudioListener>();
                if (keep == null)
                    keep = main.gameObject.AddComponent<AudioListener>();
            }
            else
            {
                GameObject go = GameObject.Find("SceneAudioListener");
                if (go == null)
                    go = new GameObject("SceneAudioListener");
                keep = go.GetComponent<AudioListener>();
                if (keep == null)
                    keep = go.AddComponent<AudioListener>();
            }
            listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] != null)
                listeners[i].enabled = listeners[i] == keep;
        }

        if (keep != null)
            keep.enabled = true;
    }

    void Start()
    {
        CacheBodyFrame();
        Vector3 flat = transform.forward;
        flat.y = 0f;
        yaw = flat.sqrMagnitude > 0.0001f
            ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg
            : transform.eulerAngles.y;
        cameraPitch = cameraStartPitch;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void OnEnable()
    {
        Physics.ContactModifyEvent += ModifyStepContacts;
    }

    Vector3 prePhysicsPos;

    void FixedUpdate()
    {
        if (rb != null)
            prePhysicsPos = rb.position;

        RefreshStepCache();
        if (jumpCooldown > 0f)
            jumpCooldown -= Time.fixedDeltaTime;

        if (rb != null && rb.isKinematic)
        {
            ReleaseStep();
            return;
        }

        if (jumpLock > 0f)
        {
            jumpLock -= Time.fixedDeltaTime;
            isGrounded = false;
            leftGround = true;
            PreserveJumpAscent();
            KeepStep();
            return;
        }

        bool wasGrounded = isGrounded;
        isGrounded = ProbeGround();
        if (isGrounded)
        {
            if (!wasGrounded && jumped)
                jumpCooldown = 0.2f;
            jumped = false;
            jumpAscentY = 0f;
            vaultWallPass = false;
            leftGround = false;
            restTime = 0f;
        }
        else
        {
            float verticalSpeed = rb != null ? rb.linearVelocity.y : 0f;
            if (Mathf.Abs(verticalSpeed) < 0.5f)
            {
                restTime += Time.fixedDeltaTime;
                leftGround = restTime <= 0.12f;
            }
            else
            {
                restTime = 0f;
                leftGround = true;
            }

            PreserveJumpAscent();
        }

        if (MoveInput.sqrMagnitude > 0.01f && StepOverLowSeam(PlanarVelocity(State == TickToyState.Run ? 2f : 1f)))
            isGrounded = true;

        KeepStep();

        // AI 등 외부 충돌이 플레이어를 옆으로 밀지 못하게, 수평 속도는 입력만 반영
        // 소품/잔디 끼임 시에도 입력 방향으로 빠져나오게 함
        if (rb != null && isGrounded && State != TickToyState.Faint && State != TickToyState.Dead)
        {
            Vector3 planar = PlayerUnstickUtil.SlideWishAgainstWalls(
                bodyCollider, transform, PlanarVelocity(State == TickToyState.Run ? 2f : 1f));
            PlayerUnstickUtil.Resolve(rb, bodyCollider, transform, planar);
            Vector3 velocity = rb.linearVelocity;
            velocity.x = planar.x;
            velocity.z = planar.z;
            rb.linearVelocity = velocity;
        }
    }

    void OnCollisionStay(Collision collision)
    {
        Vector3 wish = PlanarVelocity(State == TickToyState.Run ? 2f : 1f);
        ClimbLowContact(collision, wish);

        if (rb == null || collision == null)
            return;

        // 월드 콜라이더에 끼면 탈출
        if (collision.collider.GetComponentInParent<TickToyAI>() == null
            && State != TickToyState.Faint && State != TickToyState.Dead)
        {
            PlayerUnstickUtil.Resolve(rb, bodyCollider, transform, wish);
        }

        // AI와는 통과하지 않고 비비기만: AI가 플레이어를 밀면 위치/속도 되돌림
        if (collision.collider.GetComponentInParent<TickToyAI>() == null)
            return;

        Vector3 planar = wish;
        Vector3 v = rb.linearVelocity;
        v.x = planar.x;
        v.z = planar.z;
        if (v.y > 0f && isGrounded)
            v.y = 0f;
        rb.linearVelocity = v;

        // 입력으로 안 움직일 때 AI에게 밀려난 xz는 복구 (비비되 안 밀림)
        if (planar.sqrMagnitude < 0.01f)
        {
            Vector3 p = rb.position;
            p.x = prePhysicsPos.x;
            p.z = prePhysicsPos.z;
            rb.position = p;
        }
    }

    void Update()
    {
        if (enabled && State != TickToyState.Dead && IsTickToyCameraActive())
            WinderTrap.ReportTickToyViewing();

        if (State == TickToyState.Dead)
        {
            UpdateRunEffect();
            if (IsTrapFrozen)
                EndTrapFreeze();
            TickDeadFall();
            return;
        }

        if (trapFreezeLeft > 0f)
        {
            MoveInput = Vector2.zero;
            trapFreezeLeft -= Time.deltaTime;
            if (trapFreezeLeft <= 0f)
                EndTrapFreeze();
            return;
        }

        if (GameSettings.MenuOpen)
        {
            MoveInput = Vector2.zero;
            return;
        }

        InputUpdate();
        if (PressedAbilityKey())
            UseGrantedAbility();

        if (runTimeLeft > 0f)
        {
            runTimeLeft -= Time.deltaTime;
            if (runTimeLeft <= 0f && State == TickToyState.Run)
                State = TickToyState.Play;
        }
        UpdateRunEffect();

        // 분신 지속 UI용 타이머만 감소 — 분신 자체는 영구(맵 AI처럼 유지)
        if (duplicateTimeLeft > 0f)
        {
            duplicateTimeLeft -= Time.deltaTime;
            if (duplicateTimeLeft < 0f)
                duplicateTimeLeft = 0f;
        }

        switch (State)
        {
            case TickToyState.Faint:
                TickFaint();
                break;
            default:
                TickPlay();
                break;
        }
    }

    void TickPlay()
    {
        if (waitStandRelease)
        {
            if (WantsRun())
                waitStandRelease = false;
            else if (WantsStand())
            {
                Look();
                animationController.SetMove(false);
                return;
            }
            else
                waitStandRelease = false;
        }

        Prone();
        Look();
        Move(State == TickToyState.Run ? 2f : 1f);
        UpdateMoveAnimation();
    }

    void TickFaint()
    {
        if (!faintOnGround && FeetNearGround())
        {
            faintOnGround = true;
            SettleDownedBody();
        }

        if (faintOnGround && faintCanBreak && animationController.IsFaintSettled() && WantsStand())
            BreakFaint(true);
        else if (faintTimeLeft >= 0f)
        {
            faintTimeLeft -= Time.deltaTime;
            if (faintTimeLeft <= 0f)
                BreakFaint(false);
        }

        if (State != TickToyState.Faint)
            return;

        animationController.SetFaint(true);
    }

    void LateUpdate()
    {
        if (State == TickToyState.Faint)
            faintCanBreak = true;

        KeepUpright();
        UpdateTpsCamera();
    }

    

    /// <summary>달리기 능력을 쓰는 순간 슝! (이펙트는 약 2초 뒤 저절로 사라짐)</summary>
    void UpdateRunEffect()
    {
        bool running = State == TickToyState.Run;
        if (running && !wasRunning && runEffect != null)
        {
            if (runParticles == null)
                runParticles = runEffect.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < runParticles.Length; i++)
            {
                runParticles[i].Clear(true);
                runParticles[i].Play(true);
            }
        }
        wasRunning = running;
    }

    void InputUpdate()
    {
        MoveInput = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical"));
    }

    bool WantsRun()
    {
        if (MoveInput.sqrMagnitude > 0.01f)
            return true;

        return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D)
            || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
    }

    bool WantsStand()
    {
        return WantsRun() || Input.GetMouseButton(0)
            || Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.X) || Input.GetKey(KeyCode.C);
    }

    void Prone()
    {
        isProne = Input.GetKey(KeyCode.LeftControl);
        animationController.SetProne(isProne);
    }

    void CacheStandCollider()
    {
        bodyCapsule = bodyCollider as CapsuleCollider;
        if (bodyCapsule == null)
            return;

        standCapsuleHeight = bodyCapsule.height;
        standCapsuleRadius = bodyCapsule.radius;
        standCapsuleDirection = bodyCapsule.direction;
        standColliderCenter = bodyCapsule.center;
        standColliderCached = true;
    }

    public void FitProneCollider()
    {
        if (!standColliderCached || bodyCapsule == null)
            return;

        if (bodyAnimator == null)
            bodyAnimator = GetComponentInChildren<Animator>();

        // 사망: 바닥에 정착하면 콜라이더 제거. (공중에서 떨어지는 동안만 착지용으로 유지)
        if (State == TickToyState.Dead && deadSettled)
        {
            if (bodyCollider != null && bodyCollider.enabled)
                bodyCollider.enabled = false;
            return;
        }

        bool downed = State == TickToyState.Dead || State == TickToyState.Faint;
        bool lying = isProne || downed;
        if (!lying && bodyAnimator != null)
        {
            AnimatorStateInfo cur = bodyAnimator.GetCurrentAnimatorStateInfo(0);
            if (cur.IsName("Prone") || cur.IsName("Die"))
                lying = true;
            else if (bodyAnimator.IsInTransition(0))
            {
                AnimatorStateInfo next = bodyAnimator.GetNextAnimatorStateInfo(0);
                if (next.IsName("Prone") || next.IsName("Die"))
                    lying = true;
            }
        }

        if (!lying || bodyAnimator == null || !bodyAnimator.isHuman)
        {
            RestoreStandCollider();
            return;
        }

        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        EncapsulateBone(HumanBodyBones.Hips, ref min, ref max);
        EncapsulateBone(HumanBodyBones.Spine, ref min, ref max);
        EncapsulateBone(HumanBodyBones.Chest, ref min, ref max);
        EncapsulateBone(HumanBodyBones.Neck, ref min, ref max);
        EncapsulateBone(HumanBodyBones.Head, ref min, ref max);
        EncapsulateBone(HumanBodyBones.LeftUpperArm, ref min, ref max);
        EncapsulateBone(HumanBodyBones.RightUpperArm, ref min, ref max);
        EncapsulateBone(HumanBodyBones.LeftLowerArm, ref min, ref max);
        EncapsulateBone(HumanBodyBones.RightLowerArm, ref min, ref max);
        EncapsulateBone(HumanBodyBones.LeftHand, ref min, ref max);
        EncapsulateBone(HumanBodyBones.RightHand, ref min, ref max);
        EncapsulateBone(HumanBodyBones.LeftUpperLeg, ref min, ref max);
        EncapsulateBone(HumanBodyBones.RightUpperLeg, ref min, ref max);
        EncapsulateBone(HumanBodyBones.LeftLowerLeg, ref min, ref max);
        EncapsulateBone(HumanBodyBones.RightLowerLeg, ref min, ref max);
        EncapsulateBone(HumanBodyBones.LeftFoot, ref min, ref max);
        EncapsulateBone(HumanBodyBones.RightFoot, ref min, ref max);
        EncapsulateBone(HumanBodyBones.LeftToes, ref min, ref max);
        EncapsulateBone(HumanBodyBones.RightToes, ref min, ref max);
        if (float.IsPositiveInfinity(min.x))
        {
            RestoreStandCollider();
            return;
        }

        Transform head = bodyAnimator.GetBoneTransform(HumanBodyBones.Head);
        Transform hips = bodyAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (head != null && hips != null)
        {
            Vector3 headLocal = transform.InverseTransformPoint(head.position);
            Vector3 hipsLocal = transform.InverseTransformPoint(hips.position);
            Vector3 along = headLocal - hipsLocal;
            if (along.sqrMagnitude > 0.0001f)
            {
                Vector3 snout = headLocal + along.normalized * 0.35f;
                min = Vector3.Min(min, snout);
                max = Vector3.Max(max, snout);
            }
        }

        min.x -= 0.14f;
        max.x += 0.14f;
        min.z -= 0.14f;
        max.z += 0.14f;
        min.y -= 0.04f;
        max.y += 0.08f;

        if (!downed)
        {
            // 엎드린 캡슐 바닥 = 서있는 캡슐 바닥(지면). 전환 시 바닥 파고듦 → 튀어오름 방지
            float standBottom = standColliderCenter.y - standCapsuleHeight * 0.5f;
            min.y = standBottom;
        }

        float height = max.y - min.y;
        if (!downed && !isProne && height > standCapsuleHeight * 0.75f)
        {
            RestoreStandCollider();
            return;
        }

        Vector3 size = max - min;
        size.x = Mathf.Max(size.x, 0.5f);
        size.y = Mathf.Max(size.y, downed ? 0.28f : 0.35f);
        size.z = Mathf.Max(size.z, 0.5f);
        if (size.y < 0.35f)
        {
            float extra = 0.35f - size.y;
            size.y = 0.35f;
            max.y += extra;
        }

        bool alongZ = size.z >= size.x;
        float longLen = alongZ ? size.z : size.x;
        float lateral = alongZ ? size.x : size.z;
        // 반지름은 '높이' 기준. (좌우폭 기준이면 넓은 캐릭터는 캡슐이 바닥 밑으로 파고들어 일어날 때 튀어오름)
        float radius = Mathf.Clamp(size.y * 0.5f, 0.18f, Mathf.Max(lateral * 0.5f, 0.18f));
        float capsuleHeight = Mathf.Max(longLen, radius * 2f + 0.02f);
        Vector3 center = (min + max) * 0.5f;
        center.y = min.y + radius; // 캡슐 바닥을 min.y(지면)에 맞춤

        bodyCapsule.direction = alongZ ? 2 : 0;
        bodyCapsule.height = capsuleHeight;
        bodyCapsule.radius = radius;
        bodyCapsule.center = center;
    }

    void EncapsulateBone(HumanBodyBones bone, ref Vector3 min, ref Vector3 max)
    {
        Transform boneTransform = bodyAnimator.GetBoneTransform(bone);
        if (boneTransform == null)
            return;

        Vector3 local = transform.InverseTransformPoint(boneTransform.position);
        min = Vector3.Min(min, local);
        max = Vector3.Max(max, local);
    }

    void RestoreStandCollider()
    {
        if (bodyCapsule == null)
            return;

        if (bodyCapsule.height == standCapsuleHeight
            && bodyCapsule.radius == standCapsuleRadius
            && bodyCapsule.direction == standCapsuleDirection
            && bodyCapsule.center == standColliderCenter)
            return;

        bodyCapsule.height = standCapsuleHeight;
        bodyCapsule.radius = standCapsuleRadius;
        bodyCapsule.direction = standCapsuleDirection;
        bodyCapsule.center = standColliderCenter;
    }

    Vector3 PlanarVelocity(float speedScale)
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 dir = forward * MoveInput.y + right * MoveInput.x;
        if (dir.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        dir.Normalize();
        float currentSpeed = moveSpeed * speedScale;
        if (isProne)
            currentSpeed *= proneMoveSpeedMultiplier;
        return dir * currentSpeed;
    }

    void Move(float speedScale)
    {
        // No mid-air WASD steering. Jump() already baked held W/S/A/D into planar velocity.
        if (!isGrounded)
            return;

        Vector3 planar = PlayerUnstickUtil.SlideWishAgainstWalls(
            bodyCollider, transform, PlanarVelocity(speedScale));
        if (planar.sqrMagnitude > 0.01f)
            StepOverLowSeam(planar);

        if (rb != null)
        {
            PlayerUnstickUtil.Resolve(rb, bodyCollider, transform, planar);
            Vector3 velocity = rb.linearVelocity;
            velocity.x = planar.x;
            velocity.z = planar.z;
            rb.linearVelocity = velocity;
            return;
        }

        if (planar.sqrMagnitude < 0.0001f)
            return;

        transform.position += planar * Time.deltaTime;
    }

    void Look()
    {
        if (GameSettings.MenuOpen)
            return;

        GameSettings.EnsureLoaded();
        float mouseX = Input.GetAxis("Mouse X");

        if (PressedTurnAround())
            yaw += 180f;

        // 틱토이는 좌우만 회전 (마우스 위아래로 카메라 각도 안 바뀜)
        yaw += mouseX * GameSettings.HorizontalSensitivity * Time.deltaTime;
    }

    void UpdateTpsCamera()
    {
        if (cameraPivot == null)
            EnsureTpsCamera();

        if (cameraPivot == null)
            return;

        if (tpsCamera != null)
            tpsCamera.fieldOfView = FixedFieldOfView;

        // 관전 중이면 내 몸 대신 보고 있는 틱토이 뒤에서 같은 각도로
        Transform follow = SpectateTarget != null ? SpectateTarget : transform;
        Vector3 forward = follow.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        else
            forward.Normalize();

        float pitchRadians = cameraPitch * Mathf.Deg2Rad;
        Vector3 focus = SpectateTarget != null
            ? SpectateTarget.position + Vector3.up * focusHeight
            : transform.TransformPoint(bodyRestLocalPosition) + Vector3.up * focusHeight;
        Vector3 desired = focus
            - forward * (bodyHeight * cameraDistance * Mathf.Cos(pitchRadians))
            + Vector3.up * (bodyHeight * cameraHeight + bodyHeight * CameraPitchScale * Mathf.Sin(pitchRadians));
        Vector3 lookTarget = focus + forward * (bodyHeight * cameraLookAhead);

        cameraPivot.position = desired;

        Vector3 look = lookTarget - desired;
        if (look.sqrMagnitude < 0.0001f)
            return;

        cameraPivot.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        Vector3 cameraEuler = cameraPivot.eulerAngles;
        cameraPivot.rotation = Quaternion.Euler(cameraEuler.x, cameraEuler.y, 0f);
    }

    void KeepUpright()
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        transform.rotation = rotation;

        if (rb != null && !rb.isKinematic)
            rb.rotation = rotation;
    }

    void CacheBodyFrame()
    {
        if (body == null)
            body = transform;

        bodyRestLocalPosition = transform.InverseTransformPoint(body.position);
        focusHeight = bodyHeight * 0.46f;

        SkinnedMeshRenderer[] meshes = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool hasBounds = false;
        Bounds bounds = new Bounds(body.position, Vector3.zero);
        for (int i = 0; i < meshes.Length; i++)
        {
            if (!hasBounds)
            {
                bounds = meshes[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(meshes[i].bounds);
            }
        }

        if (!hasBounds)
            return;

        bodyHeight = Mathf.Max(bounds.size.y, 1f);
        focusHeight = bounds.center.y - body.position.y;
    }

    void EnsureTpsCamera()
    {
        if (body == null)
        {
            Animator animator = GetComponentInChildren<Animator>(true);
            body = animator != null ? animator.transform : transform;
        }

        if (cameraPivot == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>(true);
            if (childCamera != null)
                cameraPivot = childCamera.transform;
        }

        if (cameraPivot == null)
        {
            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(transform, false);
            tpsCamera = cameraObject.AddComponent<Camera>();
            tpsCamera.fieldOfView = FixedFieldOfView;
            tpsCamera.nearClipPlane = 0.15f;
            tpsCamera.depth = 10f;
            cameraPivot = cameraObject.transform;
            return;
        }

        if (cameraPivot.parent != transform)
            cameraPivot.SetParent(transform, true);

        cameraPivot.localScale = Vector3.one;
        tpsCamera = cameraPivot.GetComponent<Camera>();
        if (tpsCamera != null)
        {
            tpsCamera.gameObject.SetActive(true);
            tpsCamera.fieldOfView = FixedFieldOfView;
            tpsCamera.nearClipPlane = 0.15f;
            tpsCamera.depth = 10f; // 와인더보다 위 — 덫 시점 판별용
            if (!tpsCamera.CompareTag("MainCamera"))
                tpsCamera.tag = "MainCamera";
        }
    }

    public void GrantAbility(int kind, int level)
    {
        grantedAbility = kind;
        grantedLevel = Mathf.Clamp(level, 1, 3);
    }

    /// <summary>분신 복제본용 — 달리기/드러눕기/분신 타이머·부여 능력을 모두 제거.</summary>
    public void ClearGrantedAndTimedAbilities()
    {
        grantedAbility = 0;
        grantedLevel = 0;
        runTimeLeft = 0f;
        runDuration = 0f;
        faintTimeLeft = 0f;
        faintDuration = 0f;
        duplicateTimeLeft = 0f;
        abilityDuration = 0f;
        if (State == TickToyState.Run || State == TickToyState.Faint)
            State = TickToyState.Play;
    }

    void UseGrantedAbility()
    {
        if (grantedLevel <= 0)
            return;

        int kind = grantedAbility;
        int level = grantedLevel;
        grantedLevel = 0;
        MatchStats.Abilities++; // 내 기록: 능력 사용 수

        if (kind == 1)
            UseRunAbility(level);
        else if (kind == 2)
            UseFaintAbility(level);
        else if (kind == 3)
            UseDuplicateAbility(level);
    }

    bool PressedAbilityKey()
    {
        if (Input.GetKeyDown(KeyCode.R))
            return true;

        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
    }

    bool PressedTurnAround()
    {
        if (Input.GetKeyDown(KeyCode.F))
            return true;

        return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
    }

    bool PressedMoveKeyDown()
    {
        return Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D)
            || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow);
    }

    public void UseRunAbility(int level)
    {
        if (State == TickToyState.Dead)
            return;

        runDuration = AbilitySettings.ByLevel(AbilitySettings.Current.runSeconds, level);

        abilityDuration = runDuration;
        runTimeLeft = runDuration;
        if (State != TickToyState.Faint)
            State = TickToyState.Run;
    }

    public void UseFaintAbility(int level)
    {
        if (State == TickToyState.Dead)
            return;

        GameAudio.Play(SoundId.PlayPossum, SoundScope.Private); // 나와 나를 관전하는 사람만 들음

        State = TickToyState.Faint;
        FaintCancelled = false;
        faintCanBreak = false;
        faintOnGround = FeetNearGround();
        if (faintOnGround)
            SettleDownedBody();
        else if (rb != null)
        {
            if (bodyCollider != null)
                bodyCollider.enabled = true;
            rb.isKinematic = false;
            rb.useGravity = true;
        }
        faintDuration = AbilitySettings.ByLevel(AbilitySettings.Current.faintSeconds, level); // -1 = 직접 일어날 때까지

        abilityDuration = faintDuration;
        faintTimeLeft = faintDuration;
    }

    public void UseDuplicateAbility(int level)
    {
        if (State == TickToyState.Dead)
            return;

        int cloneCount = AbilitySettings.ByLevel(AbilitySettings.Current.duplicateCount, level);
        float duplicateDuration = AbilitySettings.ByLevel(AbilitySettings.Current.duplicateSeconds, level);

        // 달리기/드러눕기 타이머용 abilityDuration·runDuration은 건드리지 않음
        duplicateTimeLeft = duplicateDuration;
        SpawnDuplicates(cloneCount);
        Vector3 cloudPos = bodyCollider != null
            ? bodyCollider.bounds.center
            : transform.position + Vector3.up * (bodyHeight * 0.45f);
        SpawnDuplicateCloud(cloudPos, duplicateDuration);
        FusionMatchBackend.Online?.SendDuplicateCloud(cloudPos, duplicateDuration); // 다른 사람 화면에도 연막
    }

    void SpawnDuplicates(int count)
    {
        const float radius = 1.15f;
        // 온라인 + 내가 방장이 아님: 방장이 분신(AI)을 만들어야 모두에게 보임 → 자리만 계산해서 요청
        FusionMatchBackend online = FusionMatchBackend.Online;
        Vector3[] requested = online != null && !online.IsHost ? new Vector3[count] : null;
        for (int i = 0; i < count; i++)
        {
            float angle = count == 1 ? 90f : (360f * i / count);
            float radians = angle * Mathf.Deg2Rad;
            Vector3 offset =
                transform.right * (Mathf.Cos(radians) * radius)
                + transform.forward * (Mathf.Sin(radians) * radius);

            Vector3 spawnPos = transform.position + offset;
            if (AiGroundUtil.TryFindGroundNearPlayerOrWorld(spawnPos, transform.position.y, out Vector3 ground))
                spawnPos = new Vector3(spawnPos.x, ground.y, spawnPos.z);

            if (requested != null)
            {
                requested[i] = spawnPos;
                continue;
            }

            // 본체와 같은 루트를 복제 → 실제 AI(TickToyAI)로 전환
            GameObject clone = Instantiate(gameObject, spawnPos, transform.rotation);
            clone.name = gameObject.name + "_Duplicate_" + (i + 1);
            PrepareDuplicateAsAI(clone, spawnPos);
            duplicates.Add(clone);
        }
        if (requested != null && RoundManager.Instance != null)
            online.RequestDuplicates(RoundManager.Instance.LocalCharIndex, requested, transform.eulerAngles.y);
    }

    void PrepareDuplicateAsAI(GameObject clone, Vector3 groundedPos)
    {
        // Start가 돌기 전에 플레이어 전용 스크립트 차단
        // OnDisable→ClearDuplicates가 원본 분신 리스트를 지워버리는 것 방지
        TickToyMove move = clone.GetComponent<TickToyMove>();
        if (move != null)
        {
            move.skipClearDuplicates = true;
            move.duplicates = new List<GameObject>();
            // 본체 달리기/능력 상태가 Instantiate로 복사되지 않게 즉시 제거
            move.ClearGrantedAndTimedAbilities();
            move.enabled = false;
            Destroy(move);
        }

        TickToyAnimation animDriver = clone.GetComponent<TickToyAnimation>();
        if (animDriver != null)
        {
            animDriver.enabled = false;
            Destroy(animDriver);
        }

        TickToyHud[] huds = clone.GetComponentsInChildren<TickToyHud>(true);
        for (int i = 0; i < huds.Length; i++)
        {
            if (huds[i] != null)
                Destroy(huds[i]);
        }

        Camera[] cameras = clone.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
            cameras[i].gameObject.SetActive(false);

        AudioListener[] listeners = clone.GetComponentsInChildren<AudioListener>(true);
        for (int i = 0; i < listeners.Length; i++)
            listeners[i].enabled = false;

        Transform[] transforms = clone.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name.StartsWith("Spring"))
                transforms[i].gameObject.SetActive(false);
        }

        Rigidbody rbClone = clone.GetComponent<Rigidbody>();
        if (rbClone == null)
            rbClone = clone.AddComponent<Rigidbody>();
        rbClone.isKinematic = true;
        rbClone.useGravity = false;
        rbClone.constraints = RigidbodyConstraints.FreezeRotation;
        rbClone.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rbClone.interpolation = RigidbodyInterpolation.Interpolate;
        rbClone.position = groundedPos;
        clone.transform.position = groundedPos;

        Collider bodyCol = clone.GetComponent<Collider>();
        if (bodyCol == null)
        {
            CapsuleCollider cap = clone.AddComponent<CapsuleCollider>();
            cap.height = 1.6f;
            cap.radius = 0.4f;
            cap.direction = 1;
            cap.center = new Vector3(0f, 0.8f, 0f);
        }
        else
            bodyCol.enabled = true;

        Animator anim = clone.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            anim.enabled = true;
            anim.applyRootMotion = false;
            anim.SetFloat("MoveX", 0f);
            anim.SetFloat("MoveY", 0f);
            anim.SetBool("IsWinding", false);
            anim.SetBool("IsProne", false);
            anim.ResetTrigger("Die");
            anim.CrossFadeInFixedTime("Idle", 0.05f, 0, 0f);
        }

        Physics.SyncTransforms();

        TickToyAI ai = clone.GetComponent<TickToyAI>();
        if (ai == null)
            ai = clone.AddComponent<TickToyAI>();
        ai.BeginMovingNow();

        KeepOneAudioListener();
    }

    /// <summary>분신술 연막 (온라인이면 다른 사람 화면에서도 같은 자리에 만듦)</summary>
    public static void SpawnDuplicateCloud(Vector3 position, float totalSeconds)
    {
        GameObject cloud = new GameObject("DuplicateCloud");
        cloud.transform.position = position;

        ParticleSystem particles = cloud.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer particleRenderer = cloud.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.material = GetDuplicateCloudMaterial();

        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.1f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(totalSeconds * 0.8f, totalSeconds);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(7f, 13f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 280;
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)170)
        });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 4.25f;
        shape.radiusThickness = 1f;

        ParticleSystem.LimitVelocityOverLifetimeModule limit = particles.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.28f;
        limit.dampen = 0.65f;

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.12f;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.08f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.02f),
                new GradientAlphaKey(1f, 0.78f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.35f),
            new Keyframe(0.12f, 1f),
            new Keyframe(0.75f, 1.08f),
            new Keyframe(1f, 0.8f));
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        particles.Play();
    }

    static Material GetDuplicateCloudMaterial()
    {
        if (duplicateCloudMaterial != null)
            return duplicateCloudMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        duplicateCloudMaterial = new Material(shader);
        duplicateCloudMaterial.name = "Duplicate Cloud Material";
        duplicateCloudMaterial.hideFlags = HideFlags.HideAndDontSave;
        duplicateCloudMaterial.SetColor("_BaseColor", Color.white);
        duplicateCloudMaterial.SetTexture("_BaseMap", CreateCloudTexture());

        if (duplicateCloudMaterial.HasProperty("_Surface"))
            duplicateCloudMaterial.SetFloat("_Surface", 1f);
        if (duplicateCloudMaterial.HasProperty("_Blend"))
            duplicateCloudMaterial.SetFloat("_Blend", 0f);
        if (duplicateCloudMaterial.HasProperty("_SrcBlend"))
            duplicateCloudMaterial.SetFloat("_SrcBlend", 5f);
        if (duplicateCloudMaterial.HasProperty("_DstBlend"))
            duplicateCloudMaterial.SetFloat("_DstBlend", 10f);
        if (duplicateCloudMaterial.HasProperty("_ZWrite"))
            duplicateCloudMaterial.SetFloat("_ZWrite", 0f);

        duplicateCloudMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        duplicateCloudMaterial.SetOverrideTag("RenderType", "Transparent");
        duplicateCloudMaterial.renderQueue = 3000;
        return duplicateCloudMaterial;
    }

    static Texture2D CreateCloudTexture()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Duplicate Cloud Puff";
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = (y + 0.5f) / size * 2f - 1f;
                float distance = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = 1f - Mathf.SmoothStep(0.82f, 1f, distance);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    void ClearDuplicates()
    {
        // 분신은 영구 보존(실제 AI). 추적 리스트만 비움 — Destroy 하지 않음
        if (duplicates == null)
            duplicates = new List<GameObject>();
        else
            duplicates.Clear();
    }

    public void TakeHit(int damage)
    {
        if (State == TickToyState.Dead)
            return;

        if (State == TickToyState.Faint)
            BreakFaint();

        hp = Mathf.Max(0, hp - Mathf.Max(0, damage));
        TickToyHud hud = FindAnyObjectByType<TickToyHud>();
        if (hud != null)
            hud.SetHp(hp);

        HitFeedback.PlayTickToyHit(transform);
        if (animationController != null)
            animationController.ShowHitFace();

        if (hp <= 0)
        {
            EnterDead();
            if (hud != null)
                hud.HideAll();
            if (animationController != null)
                animationController.PlayDie();
            return;
        }

        if (animationController != null)
            animationController.PlayGetHit();
    }

    public void BreakFaint()
    {
        BreakFaint(true);
    }

    void BreakFaint(bool cancelled)
    {
        if (State != TickToyState.Faint || !faintCanBreak)
            return;

        faintCanBreak = false;
        if (cancelled)
        {
            faintTimeLeft = 0f;
            FaintCancelled = true;
        }

        bool run = WantsRun();
        waitStandRelease = !run;
        SetBodySolid(true);
        animationController.SetFaint(false, run);
        State = run && runTimeLeft > 0f ? TickToyState.Run : TickToyState.Play;
    }

    void EnterDead()
    {
        if (State == TickToyState.Dead)
            return;

        State = TickToyState.Dead;
        if (IsTrapFrozen)
            EndTrapFreeze();
        TrapOutlineUtil.Clear(transform);
        ClearDuplicates();

        // 점프 중 사망 시 공중 고정 방지 — 바닥에 닿을 때까지 중력 유지
        bool onGround = isGrounded || FeetNearGround();
        if (onGround)
        {
            deadSettled = true;
            SettleDownedBody();
        }
        else
        {
            deadSettled = false;
            if (bodyCollider != null)
                bodyCollider.enabled = true;
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                Vector3 v = rb.linearVelocity;
                v.x = 0f;
                v.z = 0f;
                rb.linearVelocity = v;
            }
        }
    }

    void TickDeadFall()
    {
        if (deadSettled)
            return;

        if (isGrounded || FeetNearGround())
        {
            deadSettled = true;
            SettleDownedBody();
        }
    }

    /// <summary>쓰러짐/기절 정착: 콜라이더는 켜둔 채 kinematic으로 고정하고 누워 있는 박스에 맞춤.</summary>
    void SettleDownedBody()
    {
        if (bodyCollider != null)
            bodyCollider.enabled = true;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        FitProneCollider();
    }

    bool FeetNearGround()
    {
        Vector3 origin = transform.position + Vector3.up * 0.2f;
        float distance = 0.55f;
        if (bodyCollider != null && bodyCollider.enabled)
        {
            origin = new Vector3(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y + 0.25f, bodyCollider.bounds.center.z);
            distance = 0.6f;
        }

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hit = hits[i].collider.transform;
            if (hit == transform || hit.IsChildOf(transform))
                continue;
            return true;
        }

        return false;
    }

    void SetBodySolid(bool solid)
    {
        if (bodyCollider != null)
            bodyCollider.enabled = solid;

        if (rb != null)
            rb.isKinematic = !solid;
    }

    public void Jump()
    {
        // 공중·쿨다운 중이면 점프 불가
        if (rb == null || rb.isKinematic || !CanJump)
            return;

        // 남아 있던 충돌무시 상태 정리
        ReleaseStep();

        // 이동키(WASD)로 만든 수평 속도. 키 없으면 0 → 제자리 수직 점프
        float speedScale = State == TickToyState.Run ? 2f : 1f;
        Vector3 planar = PlanarVelocity(speedScale);
        if (planar.sqrMagnitude < 0.01f)
            planar = Vector3.zero;

        // 그 방향 그대로 점프 (담에 비비면 물리가 막음 — 충돌무시 없음)
        Vector3 velocity = rb.linearVelocity;
        velocity.x = planar.x;
        velocity.z = planar.z;
        velocity.y = JumpPower;
        rb.linearVelocity = velocity;

        jumpAscentY = JumpPower;
        isGrounded = false;
        leftGround = true;
        restTime = 0f;
        jumped = true;
        jumpLock = 0.12f;
    }

    void PreserveJumpAscent()
    {
        // 상승 중 천장 등에 스쳐도 점프 곡선이 바로 죽지 않게 Y만 보정
        if (!jumped || rb == null || rb.isKinematic)
            return;

        Vector3 velocity = rb.linearVelocity;
        if (jumpAscentY > 0f)
        {
            jumpAscentY += Physics.gravity.y * Time.fixedDeltaTime;
            if (velocity.y < jumpAscentY)
                velocity.y = jumpAscentY;
            if (jumpAscentY < 0f)
                jumpAscentY = 0f;
        }

        rb.linearVelocity = velocity;
    }

    void KeepStep()
    {
        // 점프 vault 제거됨 — 혹시 남은 ignore만 시간 지나면 해제
        if (stepIgnored != null && Time.time >= stepIgnoreUntil)
            ReleaseStep();
    }

    void ReleaseStep()
    {
        if (vaultIgnoredCollision && stepIgnored != null && bodyCollider != null)
            Physics.IgnoreCollision(bodyCollider, stepIgnored, false);

        stepIgnored = null;
        vaultWallPass = false;
        vaultIgnoredCollision = false;
    }

    void OnDisable()
    {
        Physics.ContactModifyEvent -= ModifyStepContacts;
        ReleaseStep();
        if (!skipClearDuplicates)
            ClearDuplicates();
    }

    bool FindClimbableStep(Vector3 dir, out Collider step, out float topY)
    {
        step = null;
        topY = 0f;
        if (bodyCollider == null || dir.sqrMagnitude < 0.01f)
            return false;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            return false;
        dir.Normalize();

        float gravity = Mathf.Abs(Physics.gravity.y);
        if (gravity < 0.01f)
            gravity = 12f;
        float jumpHeight = (JumpPower * JumpPower) / (2f * gravity);
        Bounds bounds = bodyCollider.bounds;
        float feet = bounds.min.y;
        float front = Mathf.Max(bounds.extents.x, bounds.extents.z);

        float bestRise = float.MaxValue;
        float[] probeHeights = { 0.12f, 0.28f, 0.45f, 0.7f, 1.0f };
        for (int i = 0; i < probeHeights.Length; i++)
        {
            Vector3 origin = new Vector3(bounds.center.x, feet + probeHeights[i], bounds.center.z);
            if (!Physics.Raycast(origin, dir, out RaycastHit wall, front + 0.5f, ~0, QueryTriggerInteraction.Ignore))
                continue;
            if (IsOwnCollider(wall.collider) || wall.collider.attachedRigidbody != null)
                continue;
            if (wall.normal.y > 0.45f)
                continue;

            Vector3 topOrigin = wall.point + dir * 0.1f;
            topOrigin.y = feet + jumpHeight + 0.45f;
            if (!Physics.Raycast(topOrigin, Vector3.down, out RaycastHit top, jumpHeight + 0.6f, ~0, QueryTriggerInteraction.Ignore))
                continue;
            if (IsOwnCollider(top.collider) || top.normal.y < 0.45f)
                continue;

            float rise = top.point.y - feet;
            if (rise < 0.08f || rise > jumpHeight + 0.2f)
                continue;

            if (rise < bestRise)
            {
                bestRise = rise;
                step = wall.collider;
                topY = top.point.y;
            }
        }

        return step != null;
    }

    bool ColliderSupportsFeet(Collider other)
    {
        if (other == null || bodyCollider == null)
            return false;

        Bounds bounds = bodyCollider.bounds;
        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + 0.25f, bounds.center.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, GroundHits, 0.55f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (GroundHits[i].collider == other && GroundHits[i].normal.y >= 0.45f)
                return true;
        }

        return false;
    }

    bool TryWallNormalAhead(Vector3 dir, out Vector3 wallNormal)
    {
        wallNormal = Vector3.zero;
        if (bodyCollider == null || dir.sqrMagnitude < 0.0001f)
            return false;

        dir.y = 0f;
        dir.Normalize();
        Bounds bounds = bodyCollider.bounds;
        float front = Mathf.Max(bounds.extents.x, bounds.extents.z);
        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + 0.45f, bounds.center.z);
        if (!Physics.Raycast(origin, dir, out RaycastHit wall, front + 0.4f, ~0, QueryTriggerInteraction.Ignore))
            return false;
        if (IsOwnCollider(wall.collider) || wall.normal.y > 0.45f)
            return false;

        wallNormal = wall.normal;
        wallNormal.y = 0f;
        if (wallNormal.sqrMagnitude < 0.0001f)
            return false;
        wallNormal.Normalize();
        return true;
    }

    void RefreshStepCache()
    {
        cachedCanStep = false;
        cachedFeetY = bodyCollider != null ? bodyCollider.bounds.min.y : transform.position.y;
        if (State == TickToyState.Dead || rb == null || rb.isKinematic || jumpLock > 0f)
            return;
        if (rb.linearVelocity.y > 1.5f)
            return;

        Vector3 wish = PlanarVelocity(State == TickToyState.Run ? 2f : 1f);
        wish.y = 0f;
        if (wish.sqrMagnitude < 0.0001f)
            return;

        cachedWish = wish;
        cachedCanStep = !TallWallAhead(wish);
    }

    void ClimbLowContact(Collision collision, Vector3 planar)
    {
        if (collision == null || collision.contactCount == 0)
            return;
        StepOverLowSeam(planar);
    }

    void ModifyStepContacts(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
    {
        if (physicsBodyId == 0)
            return;

        bool wallPass = vaultWallPass && jumped && rb != null && !rb.isKinematic
            && (jumpLock > 0f || rb.linearVelocity.y > -0.15f);

        float lip = Mathf.Max(0.05f, walkLipHeight);
        float feet = cachedFeetY;
        Vector3 wish = cachedWish;
        wish.y = 0f;
        bool canWalkStep = cachedCanStep && wish.sqrMagnitude > 0.0001f;
        if (canWalkStep)
            wish.Normalize();

        if (!wallPass && !canWalkStep)
            return;

        for (int p = 0; p < pairs.Length; p++)
        {
            ModifiableContactPair pair = pairs[p];
            bool first = pair.bodyInstanceID == physicsBodyId;
            bool second = pair.otherBodyInstanceID == physicsBodyId;
            if (!first && !second)
                continue;
            if ((first ? pair.otherBodyInstanceID : pair.bodyInstanceID) != 0)
                continue;

            if (wallPass)
            {
                for (int i = 0; i < pair.contactCount; i++)
                {
                    Vector3 normal = pair.GetNormal(i);
                    Vector3 towardUs = first ? normal : -normal;
                    if (towardUs.y > 0.45f)
                        continue;
                    pair.SetMaxImpulse(i, 0f);
                }

                pairs[p] = pair;
                continue;
            }

            bool tall = false;
            bool lowBlock = false;
            for (int i = 0; i < pair.contactCount; i++)
            {
                Vector3 normal = pair.GetNormal(i);
                Vector3 towardUs = first ? normal : -normal;
                if (towardUs.y > 0.45f)
                    continue;
                if (pair.GetPoint(i).y > feet + lip)
                {
                    tall = true;
                    break;
                }
                if (Vector3.Dot(wish, towardUs) < -0.15f)
                    lowBlock = true;
            }

            if (tall || !lowBlock)
                continue;

            Vector3 stepNormal = first ? Vector3.up : Vector3.down;
            for (int i = 0; i < pair.contactCount; i++)
            {
                Vector3 normal = pair.GetNormal(i);
                Vector3 towardUs = first ? normal : -normal;
                if (towardUs.y > 0.45f || pair.GetPoint(i).y > feet + lip)
                    continue;
                pair.SetNormal(i, stepNormal);
                pair.SetSeparation(i, -0.12f);
            }

            pairs[p] = pair;
        }
    }

    bool TallWallAhead(Vector3 wish)
    {
        Vector3 dir = wish;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f || bodyCollider == null)
            return false;
        dir.Normalize();

        Bounds bounds = bodyCollider.bounds;
        float front = Mathf.Max(bounds.extents.x, bounds.extents.z);
        float lip = Mathf.Max(0.05f, walkLipHeight);
        Vector3 from = new Vector3(bounds.center.x, bounds.min.y + lip + 0.25f, bounds.center.z) + dir * (front * 0.4f);
        if (!Physics.Raycast(from, dir, out RaycastHit wall, front + 0.5f, ~0, QueryTriggerInteraction.Ignore))
            return false;
        if (IsOwnCollider(wall.collider) || wall.collider.attachedRigidbody != null)
            return false;
        return wall.normal.y < 0.45f;
    }

    bool IsOwnCollider(Collider other)
    {
        return other != null && (other.transform == transform || other.transform.IsChildOf(transform));
    }

    bool StepOverLowSeam(Vector3 planar)
    {
        if (rb == null || rb.isKinematic || bodyCollider == null)
            return false;
        if (rb.linearVelocity.y > 1.5f)
            return false;
        if (Time.frameCount == stepFrame)
            return true;

        Vector3 dir = planar;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            return false;
        dir.Normalize();

        float lip = Mathf.Max(0.05f, walkLipHeight);
        if (!TryAheadRise(dir, bodyCollider.bounds, lip, out float rise, out float distance))
            return false;

        Vector3 position = rb.position;
        position.y += rise + 0.05f;
        position += dir * Mathf.Clamp(distance + 0.05f, 0.04f, 0.2f);
        rb.position = position;
        Vector3 velocity = rb.linearVelocity;
        velocity.x = planar.x;
        velocity.z = planar.z;
        if (velocity.y < 0f)
            velocity.y = 0f;
        rb.linearVelocity = velocity;
        isGrounded = true;
        stepFrame = Time.frameCount;
        return true;
    }

    bool TryAheadRise(Vector3 dir, Bounds bounds, float lip, out float rise, out float distance)
    {
        rise = 0f;
        distance = 0f;
        float feet = bounds.min.y;
        float front = Mathf.Max(bounds.extents.x, bounds.extents.z);
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        float[] distances = { 0.02f, 0.1f, 0.22f };
        float[] sides = { 0f, 0.45f, -0.45f };
        for (int d = 0; d < distances.Length; d++)
        {
            float best = -1f;
            for (int s = 0; s < sides.Length; s++)
            {
                Vector3 origin = new Vector3(bounds.center.x, feet + lip + 0.25f, bounds.center.z)
                    + dir * (front + distances[d])
                    + side * sides[s];
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit top, lip + 0.6f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if (IsOwnCollider(top.collider) || top.collider.attachedRigidbody != null)
                    continue;
                if (top.normal.y < 0.45f)
                    continue;

                float found = top.point.y - feet;
                if (found > lip || found < 0.02f)
                    continue;
                if (found > best)
                    best = found;
            }

            if (best < 0.02f)
                continue;

            rise = best;
            distance = distances[d];
            return true;
        }

        return false;
    }

    bool ProbeGround()
    {
        if (bodyCollider == null || !bodyCollider.enabled)
            return false;

        Bounds bounds = bodyCollider.bounds;
        float y = bounds.min.y + 0.25f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        else
            forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        if (GroundAt(bounds.center.x, y, bounds.center.z))
            return true;
        if (GroundAt(bounds.center.x + forward.x * 0.28f, y, bounds.center.z + forward.z * 0.28f))
            return true;
        if (GroundAt(bounds.center.x - forward.x * 0.18f, y, bounds.center.z - forward.z * 0.18f))
            return true;
        if (GroundAt(bounds.center.x + right.x * 0.22f, y, bounds.center.z + right.z * 0.22f))
            return true;
        return GroundAt(bounds.center.x - right.x * 0.22f, y, bounds.center.z - right.z * 0.22f);
    }

    bool GroundAt(float x, float y, float z)
    {
        int count = Physics.RaycastNonAlloc(new Vector3(x, y, z), Vector3.down, GroundHits, 0.5f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = GroundHits[i].collider.transform;
            if (hit == transform || hit.IsChildOf(transform))
                continue;
            if (GroundHits[i].normal.y < 0.45f)
                continue;
            return true;
        }

        return false;
    }

    void UpdateMoveAnimation()
    {
        if (isProne)
        {
            animationController.SetProneMove(MoveInput.magnitude > 0.1f ? (Vector2)MoveInput : Vector2.zero);
            return;
        }

        if (MoveInput.magnitude > 0.1f)
        {
            animationController.SetMove(true);
        }
        else
        {
            animationController.SetMove(false);
        }
    }

}