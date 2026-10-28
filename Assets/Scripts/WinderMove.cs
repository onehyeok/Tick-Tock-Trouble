using Unity.Collections;
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

    private float moveSpeed = 6f;
    [Tooltip("이 높이보다 낮은 턱은 점프 없이 걸어 올라갑니다. 단위는 미터입니다.")]
    [SerializeField] private float walkLipHeight = 0.45f;
    int physicsBodyId;
    int stepFrame = -1;
    float cachedFeetY;
    Vector3 cachedWish;
    bool cachedCanStep;
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
    private BoxCollider bodyBox;
    private Vector3 standColliderSize;
    private Vector3 standColliderCenter;
    private bool standColliderCached;
    private Transform spring;
    private Quaternion springBaseRotation;
    private float springDegrees;
    private const float FullTurnSeconds = 10f;
    private const int AbilityRun = 1;
    private const int AbilityGold = 4; // Brick 황금 몽둥이 (레벨 없음)
    private const string GoldWeaponPath = "Assets/Models/BrickGoldWeapon/BrickGoldWeapon/BrickGoldWeapon.fbx";

    private int abilityKind;
    private int abilityLevel;
    private float runTimeLeft;
    private float faintTimeLeft;
    private bool faintCanBreak;
    private bool faintOnGround;
    private float freezeTimeLeft;
    private bool freezeHeldBody;
    private bool goldClubActive;
    private Transform normalWeapon;
    private GameObject goldWeaponInstance;
    [SerializeField] private GameObject brickGoldWeaponPrefab;

    public bool IsFrozen => freezeTimeLeft > 0f;
    public bool IsGoldenClub => goldClubActive;

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
    [SerializeField] private int hp = 100;

    public int Hp => hp;
    public bool IsGrounded => isGrounded;
    public bool CanJump => jumpLock <= 0f && jumpCooldown <= 0f && !leftGround;

    public bool IsDead
    {
        get { return State == WinderState.Dead; }
        set
        {
            if (value)
                EnterDead();
        }
    }

    public void TakeHit(int damage)
    {
        if (State == WinderState.Dead)
            return;

        hp = Mathf.Max(0, hp - Mathf.Max(0, damage));
        if (hud != null)
            hud.SetHp(hp);

        if (hp <= 0)
            EnterDead();
    }

    void EnterDead()
    {
        if (State == WinderState.Dead)
            return;

        State = WinderState.Dead;
        hp = 0;
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        if (animationController != null)
            animationController.SetFaint(true);
    }

    void Awake()
    {
        animationController = GetComponent<WinderAnimation>();
        rb = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        if (rb != null)
            physicsBodyId = rb.GetInstanceID();
        if (bodyCollider != null)
            bodyCollider.hasModifiableContacts = true;
        CacheStandCollider();
        CacheSpring();
        CacheNormalWeapon();
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
            if (current.name.StartsWith("BrickWeapon")
                || current.name.StartsWith("BrickGoldWeapon")
                || current.name.StartsWith("Spring"))
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

    void OnEnable()
    {
        Physics.ContactModifyEvent += ModifyStepContacts;
    }

    void FixedUpdate()
    {
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

        if (MoveInput.sqrMagnitude > 0.01f && StepOverLowSeam(PlanarVelocity()))
            isGrounded = true;

        KeepStep();
    }

    void OnCollisionStay(Collision collision)
    {
        ClimbLowContact(collision, PlanarVelocity());
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
        FitProneCollider();
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
            string n = transforms[i].name;
            if (!n.StartsWith("BrickWeapon") && !n.StartsWith("BrickGoldWeapon"))
                continue;
            if (!transforms[i].gameObject.activeInHierarchy)
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
        // Brick 풀: 달리기 + 황금 몽둥이만 (드러눕기/기절 제외)
        // 황금은 레벨 없음 — 이미 들고 있으면 다음 턴까지 유지
        if (abilityKind == AbilityGold && abilityLevel > 0)
            return;

        if (abilityKind == AbilityRun && abilityLevel >= 3)
            return;

        if (abilityLevel <= 0)
        {
            abilityKind = Random.value < 0.5f ? AbilityRun : AbilityGold;
            abilityLevel = 1;
        }
        else if (abilityKind == AbilityRun)
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

        if (kind == AbilityRun)
            UseRunAbility(level);
        else if (kind == AbilityGold)
            UseGoldClubAbility();
    }

    void UseGoldClubAbility()
    {
        if (State == WinderState.Dead)
            return;

        EquipGoldWeapon();
        goldClubActive = true;
    }

    /// <summary>황금 몽둥이로 누구든 맞추면 능력 종료 → 기본 무기 복귀.</summary>
    public void ConsumeGoldClubOnHit()
    {
        if (!goldClubActive)
            return;

        goldClubActive = false;
        if (goldWeaponInstance != null)
            goldWeaponInstance.SetActive(false);

        if (normalWeapon == null)
            CacheNormalWeapon();
        if (normalWeapon != null)
            normalWeapon.gameObject.SetActive(true);
    }

    void EquipGoldWeapon()
    {
        if (normalWeapon == null)
            CacheNormalWeapon();

        if (normalWeapon != null)
            normalWeapon.gameObject.SetActive(false);

        if (goldWeaponInstance != null)
        {
            goldWeaponInstance.SetActive(true);
            return;
        }

        GameObject prefab = brickGoldWeaponPrefab;
#if UNITY_EDITOR
        if (prefab == null)
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GoldWeaponPath);
#endif
        if (prefab == null)
            prefab = Resources.Load<GameObject>("BrickGoldWeapon");

        if (prefab == null || normalWeapon == null)
        {
            Debug.LogWarning("[WinderMove] BrickGoldWeapon 을 불러오지 못했습니다.");
            return;
        }

        goldWeaponInstance = Instantiate(prefab, normalWeapon.parent);
        goldWeaponInstance.name = "BrickGoldWeapon";
        goldWeaponInstance.transform.localPosition = normalWeapon.localPosition;
        goldWeaponInstance.transform.localRotation = normalWeapon.localRotation;
        goldWeaponInstance.transform.localScale = normalWeapon.localScale;
        goldWeaponInstance.SetActive(true);
    }

    void CacheNormalWeapon()
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null && transforms[i].name.StartsWith("BrickWeapon"))
            {
                normalWeapon = transforms[i];
                return;
            }
        }
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
        // No mid-air WASD steering. Jump() already baked held W/S/A/D into planar velocity.
        if (!isGrounded)
            return;

        Vector3 planar = PlanarVelocity();
        if (planar.sqrMagnitude > 0.01f)
            StepOverLowSeam(planar);

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

        if (PressedTurnAround())
        {
            Quaternion turned = Quaternion.Euler(0f, transform.eulerAngles.y + 180f, 0f);
            transform.rotation = turned;
            if (rb != null && !rb.isKinematic)
                rb.rotation = turned;
        }

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
        Physics.ContactModifyEvent -= ModifyStepContacts;
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

    void RefreshStepCache()
    {
        cachedCanStep = false;
        cachedFeetY = bodyCollider != null ? bodyCollider.bounds.min.y : transform.position.y;
        if (State == WinderState.Dead || rb == null || rb.isKinematic || jumpLock > 0f)
            return;
        if (rb.linearVelocity.y > 1.5f)
            return;

        Vector3 wish = PlanarVelocity();
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