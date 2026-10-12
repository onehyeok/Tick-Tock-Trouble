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
    [SerializeField] private float proneMoveSpeedMultiplier = 0.5f;
    [SerializeField] private float sensitivity = 200f;

    private float cameraPitch;
    private Rigidbody rb;
    private Collider bodyCollider;
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
    private Collider stepIgnored;
    private float vaultTopY;
    private float stepIgnoreUntil;
    private static readonly Collider[] StepHits = new Collider[8];
    private static readonly RaycastHit[] GroundHits = new RaycastHit[8];
    private bool isProne = false;
    private float runTimeLeft;
    private bool faintCanBreak;
    private bool faintOnGround;
    private float faintTimeLeft;
    private int grantedAbility;
    private int grantedLevel;
    private float abilityDuration;

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
    public bool IsUsingAbility => State == TickToyState.Run || State == TickToyState.Faint || runTimeLeft > 0f;
    public float AbilityDuration => abilityDuration;
    public float AbilityTimeLeft => runTimeLeft > 0f ? runTimeLeft : faintTimeLeft;

    public bool IsGrounded => isGrounded;
    public bool CanJump => jumpLock <= 0f && !leftGround;

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
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        EnsureTpsCamera();
        KeepOneAudioListener();
    }

    public static void KeepOneAudioListener()
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        AudioListener keep = null;
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] != null && listeners[i].gameObject.activeInHierarchy)
            {
                keep = listeners[i];
                break;
            }
        }

        if (keep == null && listeners.Length > 0)
            keep = listeners[0];

        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] != null)
                listeners[i].enabled = listeners[i] == keep;
        }
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

    void FixedUpdate()
    {
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

        isGrounded = ProbeGround();
        if (isGrounded)
        {
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

        KeepStep();
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

        if (animationController.IsFaintSettled() && MoveInput.sqrMagnitude > 0.01f)
            BreakFaint();
        else if (faintTimeLeft >= 0f)
        {
            faintTimeLeft -= Time.deltaTime;
            if (faintTimeLeft <= 0f)
                BreakFaint();
        }

        if (State != TickToyState.Faint)
        {
            TickPlay();
            return;
        }

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

    void Prone()
    {
        isProne = Input.GetKey(KeyCode.LeftControl);
        animationController.SetProne(isProne);
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
        if (rb != null)
            rb.angularVelocity = Vector3.zero;

        if (!isGrounded)
            return;

        Vector3 planar = PlanarVelocity(speedScale);
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

        if (rb == null)
            return;

        rb.constraints = RigidbodyConstraints.FreezeRotation;
        if (rb.isKinematic)
            return;

        rb.angularVelocity = Vector3.zero;
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
    }

    bool PressedAbilityKey()
    {
        if (Input.GetKeyDown(KeyCode.R))
            return true;

        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
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

    public void BreakFaint()
    {
        if (State != TickToyState.Faint || !faintCanBreak)
            return;

        faintCanBreak = false;
        SetBodySolid(true);
        animationController.SetFaint(false);
        State = runTimeLeft > 0f ? TickToyState.Run : TickToyState.Play;
    }

    void EnterDead()
    {
        if (State == TickToyState.Dead)
            return;

        State = TickToyState.Dead;
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
        rb.angularVelocity = Vector3.zero;
        isGrounded = false;
        leftGround = true;
        restTime = 0f;
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
        {
            if (rb != null)
                rb.angularVelocity = Vector3.zero;
            Physics.IgnoreCollision(bodyCollider, stepIgnored, false);
        }

        stepIgnored = null;
    }

    void OnDisable()
    {
        ReleaseStep();
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

    bool ProbeGround()
    {
        if (bodyCollider == null || !bodyCollider.enabled)
            return false;

        Vector3 origin = new Vector3(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y + 0.2f, bodyCollider.bounds.center.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, GroundHits, 0.35f, ~0, QueryTriggerInteraction.Ignore);
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