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
    public TickToyState State { get; private set; } = TickToyState.Play;

    [Header("TPS Camera")]
    [SerializeField] private Transform cameraPivot;
    [Tooltip("뒤로 빠지는 거리. 크면 멀어지고, 캐릭터가 안 보이면 줄이세요.")]
    [SerializeField] private float cameraDistance = 0.95f;
    [Tooltip("카메라 높이. 크면 위에서 내려다봅니다.")]
    [SerializeField] private float cameraHeight = 0.3f;
    [Tooltip("앞을 보는 정도. 크면 캐릭터가 화면 위로 밀려납니다.")]
    [SerializeField] private float cameraLookAhead = 0.2f;
    [Tooltip("시작할 때 위아래 각도.")]
    [SerializeField] private float cameraStartPitch = 12f;
    private const float CameraPitchScale = 0.22f;
    private const float MinCameraPitch = -20f;
    private const float MaxCameraPitch = 45f;
    private const float FixedFieldOfView = 78f;

    private TickToyAnimation animationController;

    [SerializeField] private float moveSpeed = 5f;
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
    private BoxCollider bodyBox;
    private Vector3 standColliderSize;
    private Vector3 standColliderCenter;
    private bool standColliderCached;
    private Animator bodyAnimator;
    private Camera tpsCamera;
    private Transform body;
    private Vector3 bodyRestLocalPosition;
    private float bodyHeight = 2.2f;
    private float focusHeight = 1.1f;
    private float yaw;
    private float JumpPower = 5.5f;

    private bool isGrounded = true;
    private bool leftGround;
    private float restTime;
    private float jumpLock;
    private float jumpCooldown;
    private bool jumped;
    private Collider stepIgnored;
    private float vaultTopY;
    private float stepIgnoreUntil;
    private static readonly Collider[] StepHits = new Collider[8];
    private static readonly RaycastHit[] GroundHits = new RaycastHit[8];
    private bool isProne = false;
    private float runTimeLeft;
    private bool faintCanBreak;
    private bool waitStandRelease;
    private bool faintOnGround;
    private float faintTimeLeft;
    private float duplicateTimeLeft;
    // Instantiate 복제 시 원본 분신 리스트가 같이 복사·삭제되지 않게
    [System.NonSerialized] private List<GameObject> duplicates = new List<GameObject>();
    [System.NonSerialized] private bool skipClearDuplicates;
    private static Material duplicateCloudMaterial;
    private int grantedAbility;
    private int grantedLevel;
    private float abilityDuration;
    private int hp = 100;

    public WinderMove FindWinderInReach()
    {
        if (IsFainted)
            return null;

        if (cameraPivot == null)
            EnsureTpsCamera();
        if (cameraPivot == null)
            return null;

        Vector3 look = cameraPivot.forward;
        if (look.sqrMagnitude < 0.0001f)
            return null;
        look.Normalize();

        const float reach = 2.4f;
        WinderMove[] winders = FindObjectsByType<WinderMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        WinderMove closest = null;
        float closestDistance = reach;
        for (int i = 0; i < winders.Length; i++)
        {
            Transform spring = winders[i].Spring;
            if (winders[i].IsDead || spring == null)
                continue;

            Vector3 toSpringFlat = spring.position - transform.position;
            toSpringFlat.y = 0f;
            float distance = toSpringFlat.magnitude;
            if (distance > reach || distance < 0.05f)
                continue;

            Vector3 back = spring.position - winders[i].transform.position;
            back.y = 0f;
            if (back.sqrMagnitude < 0.0004f)
            {
                back = -winders[i].transform.forward;
                back.y = 0f;
            }

            if (back.sqrMagnitude < 0.0001f)
                continue;
            back.Normalize();

            Vector3 fromWinder = transform.position - winders[i].transform.position;
            fromWinder.y = 0f;
            if (fromWinder.sqrMagnitude < 0.0001f || Vector3.Dot(fromWinder.normalized, back) < 0.45f)
                continue;

            Vector3 toSpring = spring.position - cameraPivot.position;
            if (toSpring.sqrMagnitude < 0.0001f || Vector3.Angle(look, toSpring) > 34f)
                continue;

            if (distance >= closestDistance)
                continue;

            closestDistance = distance;
            closest = winders[i];
        }

        return closest;
    }

    public bool IsFainted => State == TickToyState.Faint;
    public bool FaintCancelled { get; private set; }
    public bool IsUsingAbility =>
        State == TickToyState.Run || State == TickToyState.Faint
        || runTimeLeft > 0f || duplicateTimeLeft > 0f;
    public float AbilityDuration => abilityDuration;
    public float AbilityTimeLeft
    {
        get
        {
            if (runTimeLeft > 0f)
                return runTimeLeft;
            if (duplicateTimeLeft > 0f)
                return duplicateTimeLeft;
            return faintTimeLeft;
        }
    }

    public bool IsGrounded => isGrounded;
    public bool CanJump => jumpLock <= 0f && jumpCooldown <= 0f && !leftGround;

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
        bodyCollider = GetComponent<Collider>();
        if (rb != null)
            physicsBodyId = rb.GetInstanceID();
        if (bodyCollider != null)
            bodyCollider.hasModifiableContacts = true;
        CacheStandCollider();
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
        }

        if (MoveInput.sqrMagnitude > 0.01f && StepOverLowSeam(PlanarVelocity(State == TickToyState.Run ? 2f : 1f)))
            isGrounded = true;

        KeepStep();

        // AI 등 외부 충돌이 플레이어를 옆으로 밀지 못하게, 수평 속도는 입력만 반영
        if (rb != null && isGrounded && State != TickToyState.Faint && State != TickToyState.Dead)
        {
            Vector3 planar = PlanarVelocity(State == TickToyState.Run ? 2f : 1f);
            Vector3 velocity = rb.linearVelocity;
            velocity.x = planar.x;
            velocity.z = planar.z;
            rb.linearVelocity = velocity;
        }
    }

    void OnCollisionStay(Collision collision)
    {
        ClimbLowContact(collision, PlanarVelocity(State == TickToyState.Run ? 2f : 1f));

        // AI와는 통과하지 않고 비비기만: AI가 플레이어를 밀면 위치/속도 되돌림
        if (rb == null || collision == null)
            return;
        if (collision.collider.GetComponentInParent<TickToyAI>() == null)
            return;

        Vector3 planar = PlanarVelocity(State == TickToyState.Run ? 2f : 1f);
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
        if (State == TickToyState.Dead)
            return;

        InputUpdate();
        if (PressedAbilityKey())
            UseGrantedAbility();

        if (runTimeLeft > 0f)
        {
            runTimeLeft -= Time.deltaTime;
            if (runTimeLeft <= 0f && State == TickToyState.Run)
                State = TickToyState.Play;
        }

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
            SetBodySolid(false);
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
        return WantsRun() || Input.GetMouseButton(0) || Input.GetKey(KeyCode.Z);
    }

    void Prone()
    {
        isProne = Input.GetKey(KeyCode.LeftControl);
        animationController.SetProne(isProne);
    }

    void CacheStandCollider()
    {
        bodyBox = bodyCollider as BoxCollider;
        if (bodyBox == null)
            return;

        standColliderSize = bodyBox.size;
        standColliderCenter = bodyBox.center;
        standColliderCached = true;
    }

    public void FitProneCollider()
    {
        if (!standColliderCached)
            return;

        if (bodyAnimator == null)
            bodyAnimator = GetComponentInChildren<Animator>();

        bool lying = isProne;
        if (!lying && bodyAnimator != null)
        {
            if (bodyAnimator.GetCurrentAnimatorStateInfo(0).IsName("Prone"))
                lying = true;
            else if (bodyAnimator.IsInTransition(0) && bodyAnimator.GetNextAnimatorStateInfo(0).IsName("Prone"))
                lying = true;
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
        EncapsulateBone(HumanBodyBones.Head, ref min, ref max);
        EncapsulateBone(HumanBodyBones.LeftUpperArm, ref min, ref max);
        EncapsulateBone(HumanBodyBones.RightUpperArm, ref min, ref max);
        EncapsulateBone(HumanBodyBones.LeftHand, ref min, ref max);
        EncapsulateBone(HumanBodyBones.RightHand, ref min, ref max);
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
                Vector3 snout = headLocal + along.normalized * 0.22f;
                min = Vector3.Min(min, snout);
                max = Vector3.Max(max, snout);
            }

            Vector3 ears = headLocal + Vector3.up * 0.18f;
            min = Vector3.Min(min, ears);
            max = Vector3.Max(max, ears);
        }

        min.x -= 0.16f;
        max.x += 0.16f;
        min.z -= 0.14f;
        max.z += 0.14f;
        max.y += 0.08f;

        float standBottom = standColliderCenter.y - standColliderSize.y * 0.5f;
        if (min.y < standBottom)
            min.y = standBottom;

        if (max.y - min.y > standColliderSize.y * 0.75f)
        {
            RestoreStandCollider();
            return;
        }

        Vector3 size = max - min;
        size.x = Mathf.Max(size.x, 0.45f);
        size.y = Mathf.Max(size.y, 0.35f);
        size.z = Mathf.Max(size.z, 0.45f);
        bodyBox.size = size;
        bodyBox.center = (min + max) * 0.5f;
    }

    void RestoreStandCollider()
    {
        if (bodyBox.size == standColliderSize && bodyBox.center == standColliderCenter)
            return;

        bodyBox.size = standColliderSize;
        bodyBox.center = standColliderCenter;
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

        Vector3 planar = PlanarVelocity(speedScale);
        if (planar.sqrMagnitude > 0.01f)
            StepOverLowSeam(planar);

        if (rb != null)
        {
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
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        if (PressedTurnAround())
            yaw += 180f;

        yaw += mouseX * sensitivity * Time.deltaTime;

        cameraPitch -= mouseY * sensitivity * Time.deltaTime;
        cameraPitch = Mathf.Clamp(cameraPitch, MinCameraPitch, MaxCameraPitch);
    }

    void UpdateTpsCamera()
    {
        if (cameraPivot == null)
            EnsureTpsCamera();

        if (cameraPivot == null)
            return;

        if (tpsCamera != null)
            tpsCamera.fieldOfView = FixedFieldOfView;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        else
            forward.Normalize();

        float pitchRadians = cameraPitch * Mathf.Deg2Rad;
        Vector3 focus = transform.TransformPoint(bodyRestLocalPosition) + Vector3.up * focusHeight;
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
        }
    }

    public void GrantAbility(int kind, int level)
    {
        grantedAbility = kind;
        grantedLevel = Mathf.Clamp(level, 1, 3);
    }

    void UseGrantedAbility()
    {
        if (grantedLevel <= 0)
            return;

        int kind = grantedAbility;
        int level = grantedLevel;
        grantedLevel = 0;

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

        if (level <= 1)
            abilityDuration = 5f;
        else if (level == 2)
            abilityDuration = 8f;
        else
            abilityDuration = 12f;

        runTimeLeft = abilityDuration;
        if (State != TickToyState.Faint)
            State = TickToyState.Run;
    }

    public void UseFaintAbility(int level)
    {
        if (State == TickToyState.Dead)
            return;

        State = TickToyState.Faint;
        FaintCancelled = false;
        faintCanBreak = false;
        faintOnGround = FeetNearGround();
        if (faintOnGround)
            SetBodySolid(false);
        else if (rb != null)
            rb.useGravity = true;
        if (level <= 1)
            abilityDuration = 10f;
        else if (level == 2)
            abilityDuration = 20f;
        else
            abilityDuration = -1f;

        faintTimeLeft = abilityDuration;
    }

    public void UseDuplicateAbility(int level)
    {
        if (State == TickToyState.Dead)
            return;

        int cloneCount;
        if (level <= 1)
        {
            abilityDuration = 3f;
            cloneCount = 1;
        }
        else if (level == 2)
        {
            abilityDuration = 5f;
            cloneCount = 2;
        }
        else
        {
            abilityDuration = 7f;
            cloneCount = 4;
        }

        duplicateTimeLeft = abilityDuration;
        SpawnDuplicates(cloneCount);
        SpawnDuplicateCloud(abilityDuration);
    }

    void SpawnDuplicates(int count)
    {
        const float radius = 1.15f;
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

            // 본체와 같은 루트를 복제 → 실제 AI(TickToyAI)로 전환
            GameObject clone = Instantiate(gameObject, spawnPos, transform.rotation);
            clone.name = gameObject.name + "_Duplicate_" + (i + 1);
            PrepareDuplicateAsAI(clone, spawnPos);
            duplicates.Add(clone);
        }
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
            BoxCollider box = clone.AddComponent<BoxCollider>();
            box.size = new Vector3(0.8f, 1.6f, 0.8f);
            box.center = new Vector3(0f, 0.8f, 0f);
        }
        else
            bodyCol.enabled = true;

        Animator anim = clone.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            anim.enabled = true;
            anim.applyRootMotion = false;
        }

        Physics.SyncTransforms();

        TickToyAI ai = clone.GetComponent<TickToyAI>();
        if (ai == null)
            ai = clone.AddComponent<TickToyAI>();
        ai.BeginMovingNow();

        KeepOneAudioListener();
    }

    void SpawnDuplicateCloud(float totalSeconds)
    {
        GameObject cloud = new GameObject(gameObject.name + "_DuplicateCloud");
        cloud.transform.position = bodyCollider != null
            ? bodyCollider.bounds.center
            : transform.position + Vector3.up * (bodyHeight * 0.45f);

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

        HitFeedback.PlayTickToyHit(transform);
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
        ClearDuplicates();
        SetBodySolid(false);
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
        if (rb == null || rb.isKinematic || !CanJump)
            return;

        float speedScale = State == TickToyState.Run ? 2f : 1f;
        Vector3 planar = PlanarVelocity(speedScale);
        Vector3 dir = planar.sqrMagnitude > 0.01f ? planar.normalized : FlatForward();
        Collider step = null;
        bool climb = FindClimbableStep(dir, out step);
        if (climb && planar.sqrMagnitude < 0.01f)
        {
            float speed = moveSpeed * speedScale;
            if (isProne)
                speed *= proneMoveSpeedMultiplier;
            planar = dir * speed;
        }

        if (climb && bodyCollider != null)
        {
            ReleaseStep();
            Physics.IgnoreCollision(bodyCollider, step, true);
            stepIgnored = step;
            vaultTopY = step.bounds.max.y;
            stepIgnoreUntil = Time.time + 0.5f;
        }

        Vector3 velocity = rb.linearVelocity;
        velocity.x = planar.x;
        velocity.z = planar.z;
        velocity.y = JumpPower;
        rb.linearVelocity = velocity;
        isGrounded = false;
        leftGround = true;
        restTime = 0f;
        jumped = true;
        jumpLock = 0.12f;
    }

    Vector3 FlatForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            return Vector3.forward;
        return forward.normalized;
    }

    void KeepStep()
    {
        if (stepIgnored == null || bodyCollider == null)
            return;

        bool feetClear = bodyCollider.bounds.min.y >= vaultTopY - 0.05f;
        bool falling = rb != null && rb.linearVelocity.y < -0.2f;
        if (feetClear || falling || Time.time >= stepIgnoreUntil)
            ReleaseStep();
    }

    void ReleaseStep()
    {
        if (stepIgnored != null && bodyCollider != null)
            Physics.IgnoreCollision(bodyCollider, stepIgnored, false);

        stepIgnored = null;
    }

    void OnDisable()
    {
        Physics.ContactModifyEvent -= ModifyStepContacts;
        ReleaseStep();
        if (!skipClearDuplicates)
            ClearDuplicates();
    }

    bool FindClimbableStep(Vector3 dir, out Collider step)
    {
        step = null;
        if (bodyCollider == null || dir.sqrMagnitude < 0.01f)
            return false;

        float gravity = Mathf.Abs(Physics.gravity.y);
        if (gravity < 0.01f)
            gravity = 12f;
        float jumpHeight = (JumpPower * JumpPower) / (2f * gravity);
        float feet = bodyCollider.bounds.min.y;
        float reach = bodyCollider.bounds.extents.x + bodyCollider.bounds.extents.z;
        Vector3 center = bodyCollider.bounds.center + dir * (reach * 0.35f);
        int count = Physics.OverlapBoxNonAlloc(center, bodyCollider.bounds.extents, StepHits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        float bestRise = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider hit = StepHits[i];
            if (hit == null || hit == bodyCollider)
                continue;

            Transform hitTransform = hit.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;

            float rise = hit.bounds.max.y - feet;
            if (rise < 0.08f || rise > jumpHeight + 0.15f)
                continue;

            Vector3 to = hit.bounds.center - bodyCollider.bounds.center;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f && Vector3.Dot(to.normalized, dir) < 0.15f)
                continue;

            if (rise < bestRise)
            {
                bestRise = rise;
                step = hit;
            }
        }

        return step != null;
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
        if (!cachedCanStep || physicsBodyId == 0)
            return;

        float lip = Mathf.Max(0.05f, walkLipHeight);
        float feet = cachedFeetY;
        Vector3 wish = cachedWish;
        wish.y = 0f;
        if (wish.sqrMagnitude < 0.0001f)
            return;
        wish.Normalize();

        for (int p = 0; p < pairs.Length; p++)
        {
            ModifiableContactPair pair = pairs[p];
            bool first = pair.bodyInstanceID == physicsBodyId;
            bool second = pair.otherBodyInstanceID == physicsBodyId;
            if (!first && !second)
                continue;
            if ((first ? pair.otherBodyInstanceID : pair.bodyInstanceID) != 0)
                continue;

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
            return;

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