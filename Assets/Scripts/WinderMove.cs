using UnityEngine;
using UnityEngine.InputSystem;

public class WinderMove : MonoBehaviour
{
    public enum WinderState
    {
        Play,
        Faint,
        Dead
    }

    public Vector2 MoveInput { get; private set; }
    public WinderState State { get; private set; } = WinderState.Play;

    [SerializeField] private Transform cameraPivot;

    private WinderAnimation animationController;

    private float moveSpeed = 5f;
    [SerializeField] private float proneMoveSpeedMultiplier = 0.5f;
    private float sensitivity = 200f;

    private float xRotation;
    private Animator bodyAnimator;
    private Transform spine;
    private Transform chest;
    private Transform aimArm;
    private Transform aimLower;
    private Transform aimHand;
    private Quaternion holdUpper;
    private Quaternion holdLower;
    private Quaternion holdHand;
    private bool holdReady;
    private bool aimLeft;
    private Rigidbody rb;
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
    private Collider bodyCollider;
    private Transform spring;
    private Quaternion springBaseRotation;
    private float springDegrees;
    private const float FullTurnSeconds = 10f;
    private int abilityKind;
    private int abilityLevel;
    private float runTimeLeft;
    private float faintTimeLeft;
    private bool faintCanBreak;
    private bool faintOnGround;
    private float freezeTimeLeft;
    private bool freezeHeldBody;

    public bool IsFrozen => freezeTimeLeft > 0f;

    public Transform Spring
    {
        get
        {
            if (spring == null)
                CacheSpring();
            return spring;
        }
    }
    private TickToyHud hud;

    public bool IsGrounded => isGrounded;
    public bool CanJump => jumpLock <= 0f && jumpCooldown <= 0f && !leftGround;

    public bool IsDead
    {
        get { return State == WinderState.Dead; }
        set
        {
            if (value)
                State = WinderState.Dead;
        }
    }

    void Awake()
    {
        animationController = GetComponent<WinderAnimation>();
        rb = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        CacheSpring();
        TickToyMove.KeepOneAudioListener();
    }

    void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        hud = FindAnyObjectByType<TickToyHud>();
        HideBodyFromOwnCamera();
        PlaceFpsCamera();
        CacheAimBones();
    }

    void PlaceFpsCamera()
    {
        if (cameraPivot == null)
            return;

        Transform head = FindChildNamed("Head");
        if (head != null && cameraPivot.parent != null)
        {
            Vector3 eye = cameraPivot.parent.InverseTransformPoint(head.position);
            eye.x = -0.04f;
            cameraPivot.localPosition = eye;
        }

        Camera fpsCamera = cameraPivot.GetComponent<Camera>();
        if (fpsCamera == null)
            return;

        fpsCamera.nearClipPlane = 0.02f;
        fpsCamera.fieldOfView = 70f;
    }

    Transform FindChildNamed(string childName)
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == childName)
                return transforms[i];
        }

        return null;
    }

    void HideBodyFromOwnCamera()
    {
        if (cameraPivot == null)
            return;

        Camera fpsCamera = cameraPivot.GetComponent<Camera>();
        int hiddenLayer = LayerMask.NameToLayer("FirstPersonBody");
        if (fpsCamera == null || hiddenLayer < 0)
            return;

        SkinnedMeshRenderer[] meshes = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < meshes.Length; i++)
        {
            if (IsWeaponOrSpring(meshes[i].transform))
                continue;

            meshes[i].gameObject.layer = hiddenLayer;
        }

        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (!transforms[i].name.StartsWith("Spring"))
                continue;

            Renderer[] renderers = transforms[i].GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
                renderers[r].gameObject.layer = hiddenLayer;
        }

        fpsCamera.cullingMask &= ~(1 << hiddenLayer);
    }

    bool IsWeaponOrSpring(Transform target)
    {
        Transform current = target;
        while (current != null && current != transform)
        {
            if (current.name.StartsWith("BrickWeapon") || current.name.StartsWith("Spring"))
                return true;

            current = current.parent;
        }

        return false;
    }

    public void Freeze(float seconds)
    {
        if (State == WinderState.Dead)
            return;

        freezeTimeLeft = seconds;
        if (animationController != null)
            animationController.SetPaused(true);

        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
            freezeHeldBody = true;
        }
    }

    void EndFreeze()
    {
        freezeTimeLeft = 0f;
        if (animationController != null)
            animationController.SetPaused(false);

        if (!freezeHeldBody || rb == null)
            return;

        freezeHeldBody = false;
        if (State != WinderState.Faint && State != WinderState.Dead)
            rb.isKinematic = false;
    }

    void FixedUpdate()
    {
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

        KeepStep();
    }

    void Update()
    {
        if (State == WinderState.Dead)
        {
            if (IsFrozen)
                EndFreeze();
            return;
        }

        if (freezeTimeLeft > 0f)
        {
            freezeTimeLeft -= Time.deltaTime;
            if (freezeTimeLeft <= 0f)
                EndFreeze();
            return;
        }

        InputUpdate();
        if (PressedAbilityKey())
            UseAbility();

        if (runTimeLeft > 0f)
            runTimeLeft -= Time.deltaTime;

        switch (State)
        {
            case WinderState.Faint:
                TickFaint();
                break;
            default:
                Prone();
                Look();
                Move();
                UpdateMoveAnimation();
                break;
        }
    }

    void LateUpdate()
    {
        if (IsFrozen)
            return;

        if (State == WinderState.Faint)
            faintCanBreak = true;

        TickSpring();
        AimArmsWithLook();
    }

    void CacheAimBones()
    {
        bodyAnimator = GetComponentInChildren<Animator>();
        if (bodyAnimator == null || !bodyAnimator.isHuman)
            return;

        spine = bodyAnimator.GetBoneTransform(HumanBodyBones.Spine);
        chest = bodyAnimator.GetBoneTransform(HumanBodyBones.Chest);

        Transform weapon = null;
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (!transforms[i].name.StartsWith("BrickWeapon"))
                continue;
            weapon = transforms[i];
            break;
        }

        Transform leftHand = bodyAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
        aimLeft = weapon != null && leftHand != null && weapon.IsChildOf(leftHand);
        if (aimLeft)
        {
            aimArm = bodyAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            aimLower = bodyAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            aimHand = leftHand;
        }
        else
        {
            aimArm = bodyAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            aimLower = bodyAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            aimHand = bodyAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        }
    }

    void CacheHoldPose()
    {
        if (holdReady || aimArm == null || aimLower == null || aimHand == null)
            return;
        if (isProne || MoveInput.sqrMagnitude > 0.01f || IsAttacking())
            return;

        holdUpper = aimArm.localRotation;
        holdLower = aimLower.localRotation;
        holdHand = aimHand.localRotation;
        holdReady = true;
    }

    void AimArmsWithLook()
    {
        if (State == WinderState.Faint || State == WinderState.Dead)
            return;

        CacheHoldPose();
        HoldWeaponArm();

        Vector3 axis = transform.right;
        if (spine != null)
            spine.Rotate(axis, xRotation * 0.35f, Space.World);
        if (chest != null)
            chest.Rotate(axis, xRotation * 0.35f, Space.World);
        if (aimArm != null)
            aimArm.Rotate(axis, xRotation * 0.3f, Space.World);
    }

    void HoldWeaponArm()
    {
        if (!holdReady || isProne || IsAttacking())
            return;

        aimArm.localRotation = holdUpper;
        aimLower.localRotation = holdLower;
        aimHand.localRotation = holdHand;
        aimArm.Rotate(transform.right, -70f, Space.World);
        aimArm.Rotate(transform.up, aimLeft ? 18f : -18f, Space.World);
    }

    bool IsAttacking()
    {
        if (bodyAnimator == null)
            return false;

        int layer = bodyAnimator.GetLayerIndex("Attack Layer");
        if (layer < 0)
            return false;

        if (bodyAnimator.GetCurrentAnimatorStateInfo(layer).IsName("Attack 0"))
            return true;
        return bodyAnimator.IsInTransition(layer) && bodyAnimator.GetNextAnimatorStateInfo(layer).IsName("Attack 0");
    }

    void CacheSpring()
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name != "Spring")
                continue;

            spring = transforms[i];
            springBaseRotation = spring.localRotation;
            return;
        }
    }

    void TickSpring()
    {
        if (spring == null || State == WinderState.Dead)
            return;

        springDegrees += 360f / FullTurnSeconds * Time.deltaTime;
        while (springDegrees >= 360f)
        {
            springDegrees -= 360f;
            CompleteSpringTurn();
        }

        spring.localRotation = springBaseRotation * Quaternion.Euler(0f, -springDegrees, 0f);
    }

    void CompleteSpringTurn()
    {
        if (abilityLevel >= 3)
            return;

        if (abilityLevel <= 0)
        {
            abilityKind = Random.Range(0, 2) == 0 ? 1 : 2;
            abilityLevel = 1;
        }
        else
            abilityLevel++;

        if (hud != null)
            hud.PresentAbility(abilityKind, abilityLevel);
    }

    void UseAbility()
    {
        if (abilityLevel <= 0 || State == WinderState.Dead)
            return;

        int kind = abilityKind;
        int level = abilityLevel;
        abilityKind = 0;
        abilityLevel = 0;
        if (hud != null)
            hud.ClearPresentedAbility();

        if (kind == 1)
            UseRunAbility(level);
        else if (kind == 2)
            UseFaintAbility(level);
    }

    void UseRunAbility(int level)
    {
        if (level <= 1)
            runTimeLeft = 5f;
        else if (level == 2)
            runTimeLeft = 8f;
        else
            runTimeLeft = 12f;
    }

    void UseFaintAbility(int level)
    {
        if (State == WinderState.Dead)
            return;

        State = WinderState.Faint;
        faintCanBreak = false;
        faintOnGround = FeetNearGround();
        if (faintOnGround)
            SetBodySolid(false);
        else if (rb != null)
            rb.useGravity = true;
        if (level <= 1)
            faintTimeLeft = 10f;
        else if (level == 2)
            faintTimeLeft = 20f;
        else
            faintTimeLeft = -1f;

        animationController.SetFaint(true);
    }

    void TickFaint()
    {
        if (!faintOnGround && FeetNearGround())
        {
            faintOnGround = true;
            SetBodySolid(false);
        }

        if (faintCanBreak && PressedMoveKeyDown())
            BreakFaint();
        else if (faintTimeLeft >= 0f)
        {
            faintTimeLeft -= Time.deltaTime;
            if (faintTimeLeft <= 0f)
                BreakFaint();
        }

        if (State != WinderState.Faint)
        {
            Prone();
            Look();
            Move();
            UpdateMoveAnimation();
        }
    }

    public void BreakFaint()
    {
        if (State != WinderState.Faint || !faintCanBreak)
            return;

        faintCanBreak = false;
        SetBodySolid(true);
        animationController.SetFaint(false);
        State = WinderState.Play;
    }

    void SetBodySolid(bool solid)
    {
        if (bodyCollider != null)
            bodyCollider.enabled = solid;

        if (rb != null)
            rb.isKinematic = !solid;
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

    Vector3 PlanarVelocity()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        Vector3 right = transform.right;
        right.y = 0f;
        Vector3 dir = forward * MoveInput.y + right * MoveInput.x;
        if (dir.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        float currentSpeed = runTimeLeft > 0f ? moveSpeed * 2f : moveSpeed;
        if (isProne)
            currentSpeed *= proneMoveSpeedMultiplier;
        return dir.normalized * currentSpeed;
    }

    void Move()
    {
        if (!isGrounded)
            return;

        Vector3 planar = PlanarVelocity();
        if (rb != null && !rb.isKinematic)
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

    void Look()
    {
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        transform.Rotate(Vector3.up * mouseX * sensitivity * Time.deltaTime);

        xRotation -= mouseY * sensitivity * Time.deltaTime;
        xRotation = Mathf.Clamp(xRotation, -80f, 80f);

        cameraPivot.localRotation =
            Quaternion.Euler(xRotation, 0f, 0f);
    }

    public void Jump()
    {
        if (rb == null || rb.isKinematic || !CanJump)
            return;

        Vector3 planar = PlanarVelocity();
        Vector3 dir = planar.sqrMagnitude > 0.01f ? planar.normalized : FlatForward();
        Collider step = null;
        bool climb = FindClimbableStep(dir, out step);
        if (climb && planar.sqrMagnitude < 0.01f)
        {
            float speed = runTimeLeft > 0f ? moveSpeed * 2f : moveSpeed;
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