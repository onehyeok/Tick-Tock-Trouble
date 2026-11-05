using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

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

    private float moveSpeed = 4f; // 걷기 4, 달리기 능력 ×2 = 8
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
    private Collider bodyCollider;
    private CapsuleCollider bodyCapsule;
    private float standCapsuleHeight;
    private float standCapsuleRadius;
    private int standCapsuleDirection;
    private Vector3 standColliderCenter;
    private bool standColliderCached;
    private Transform spring;
    private Quaternion springBaseRotation;
    private float springDegrees;
    private const float FullTurnSeconds = 10f;
    private const int AbilityRun = 1;
    private const int AbilityGold = 4; // Brick 황금 몽둥이 (레벨 없음)
    private const int AbilityTrap = 5; // 덫 (레벨 없음, 1회 설치)
    private const string GoldWeaponPath = "Assets/Models/BrickGoldWeapon/BrickGoldWeapon/BrickGoldWeapon.fbx";
    private const string TrapModelPath = "Assets/Models/Trap/Trap/Trap.fbx";
    private const float TrapTargetWidth = 1.35f;
    private const string TrapMaterialPath = "Assets/Materials/Trap.mat";
    private static readonly Color TrapPreviewValid = new Color(0.15f, 0.7f, 1f, 1f);
    private static readonly Color TrapPreviewInvalid = new Color(1f, 0.12f, 0.1f, 1f);

    private int abilityKind;
    private int abilityLevel;
    private float runTimeLeft;
    private float runDuration;
    private float faintTimeLeft;
    private bool faintCanBreak;
    private bool faintOnGround;
    private bool deadSettled;
    private float freezeTimeLeft;
    private bool freezeHeldBody;
    private bool goldClubActive;
    private Transform normalWeapon;
    private GameObject goldWeaponInstance;
    [SerializeField] private GameObject brickGoldWeaponPrefab;
    [SerializeField] private GameObject trapPrefab;
    private bool trapPlacing;
    private bool trapPlaceValid;
    private Vector3 trapPlacePos;
    private GameObject trapPreview;
    private Renderer[] trapPreviewRenderers;
    private Material[] trapPreviewMatValid;
    private Material[] trapPreviewMatInvalid;

    public bool IsFrozen => freezeTimeLeft > 0f;
    public bool IsGoldenClub => goldClubActive;
    public bool IsPlacingTrap => trapPlacing;

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

    // FPS 무기: 내 카메라가 그릴 때만 카메라에 붙은 무기 복사본을 보여주고 손 무기는 숨김.
    // 그 외(씬 뷰·다른 카메라)는 손에 든 무기만 보임.
    private Camera fpsCam;
    private Transform viewWeapon;        // 카메라 자식, 렌더러만 있는 복사본
    private Transform viewWeaponSource;  // 복사 원본(기본/황금)
    private Renderer[] viewRenderers;
    private Renderer[] handRenderers;
    private Vector3 viewBaseScale = Vector3.one;
    private float viewAttackWeight;
    [Header("FPS 무기 (내 화면에서 보이는 위치)")]
    [Tooltip("카메라 기준 위치 (x 오른쪽, y 위, z 앞)")]
    [SerializeField] private Vector3 fpsWeaponPosition = new Vector3(0.36f, -0.13f, 0.44f);
    [Tooltip("카메라 기준 회전")]
    [SerializeField] private Vector3 fpsWeaponRotation = new Vector3(4.8f, 206.1f, 328.2f);
    [Tooltip("내 화면 무기 크기 배율")]
    [SerializeField] private float fpsWeaponScale = 0.85f;
    [Tooltip("달리기(능력) 쓰는 순간 한 번 나오는 흙먼지 바람 이펙트 (프리팹 자식 RunWhoosh, 약 2초)")]
    [SerializeField] private GameObject runEffect;
    private ParticleSystem[] runParticles;
    [SerializeField] private int hp = 100;

    public int Hp => hp;
    /// <summary>달리기 능력 남은 시간 / 총 시간 (HUD 시계 타이머용)</summary>
    public float RunTimeLeft => runTimeLeft;
    public float RunDuration => runDuration;
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
        {
            hud.SetHp(hp);
            hud.PlayWinderDamageFlash(damage);
        }

        if (hp <= 0)
            EnterDead();
    }

    void EnterDead()
    {
        if (State == WinderState.Dead)
            return;

                State = WinderState.Dead;
        hp = 0;

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

        if (animationController != null)
            animationController.SetFaint(true);
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

    void Awake()
    {
        animationController = GetComponent<WinderAnimation>();
        rb = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<CapsuleCollider>();
        if (bodyCollider == null)
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
        if (cameraPivot != null)
            fpsCam = cameraPivot.GetComponentInChildren<Camera>();
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
        fpsCamera.depth = 0f;
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
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
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

        if (MoveInput.sqrMagnitude > 0.01f && StepOverLowSeam(PlanarVelocity()))
            isGrounded = true;

        KeepStep();

        // 소품/잔디 등에 끼면 입력 불능 — 탈출 + 벽 슬라이드
        if (State != WinderState.Faint && State != WinderState.Dead)
        {
            Vector3 wish = PlanarVelocity();
            wish = PlayerUnstickUtil.SlideWishAgainstWalls(bodyCollider, transform, wish);
            PlayerUnstickUtil.Resolve(rb, bodyCollider, transform, wish);
            if (rb != null && !rb.isKinematic && wish.sqrMagnitude > 0.01f && isGrounded)
            {
                Vector3 v = rb.linearVelocity;
                v.x = wish.x;
                v.z = wish.z;
                rb.linearVelocity = v;
            }
        }
    }

    void OnCollisionStay(Collision collision)
    {
        ClimbLowContact(collision, PlanarVelocity());
        if (State != WinderState.Faint && State != WinderState.Dead)
            PlayerUnstickUtil.Resolve(rb, bodyCollider, transform, PlanarVelocity());
    }

    void Update()
    {
        if (State != WinderState.Dead && IsWinderCameraActive())
            WinderTrap.ReportWinderViewing();

        if (State == WinderState.Dead)
        {
            if (trapPlacing)
                CancelTrapPlacement(false);
            if (IsFrozen)
                EndFreeze();
            TickDeadFall();
            return;
        }

        if (freezeTimeLeft > 0f)
        {
            freezeTimeLeft -= Time.deltaTime;
            if (freezeTimeLeft <= 0f)
                EndFreeze();
            return;
        }

        if (GameSettings.MenuOpen)
        {
            MoveInput = Vector2.zero;
            return;
        }

        UpdateTrapTestKey();
        InputUpdate();

        if (trapPlacing)
        {
            TickTrapPlacement();
            Prone();
            Look();
            Move();
            UpdateMoveAnimation();
            return;
        }

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
        UpdateViewWeapon();

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

    Transform ActiveHandWeapon()
    {
        if (goldWeaponInstance != null && goldWeaponInstance.activeInHierarchy)
            return goldWeaponInstance.transform;
        if (normalWeapon == null)
            CacheNormalWeapon();
        if (normalWeapon != null && normalWeapon.gameObject.activeInHierarchy)
            return normalWeapon;
        return null;
    }

    /// <summary>FPS 무기: 카메라 기준 고정 자세(Inspector 값). 공격 때만 손 무기의 휘두르는 동작을 따라감.</summary>
    void UpdateViewWeapon()
    {
        if (cameraPivot == null || fpsCam == null)
            return;

        Transform weapon = ActiveHandWeapon();
        if (weapon == null)
            return;

        if (weapon != viewWeaponSource)
            BuildViewWeapon(weapon);

        // 상하 회전을 뺀 카메라 기준 손 무기 자세 (공격 동작용)
        Transform parent = cameraPivot.parent;
        Vector3 livePos = parent.InverseTransformPoint(weapon.position) - cameraPivot.localPosition;
        Quaternion liveRot = Quaternion.Inverse(parent.rotation) * weapon.rotation;

        Vector3 restPos = fpsWeaponPosition;
        Quaternion restRot = Quaternion.Euler(fpsWeaponRotation);
        Vector3 restScale = viewBaseScale * fpsWeaponScale;

        viewAttackWeight = Mathf.MoveTowards(viewAttackWeight, IsAttacking() ? 1f : 0f, Time.deltaTime * 10f);
        if (viewAttackWeight <= 0f)
        {
            viewWeapon.localPosition = restPos;
            viewWeapon.localRotation = restRot;
            viewWeapon.localScale = restScale;
        }
        else
        {
            viewWeapon.localPosition = Vector3.Lerp(restPos, livePos, viewAttackWeight);
            viewWeapon.localRotation = Quaternion.Slerp(restRot, liveRot, viewAttackWeight);
            viewWeapon.localScale = Vector3.Lerp(restScale, viewBaseScale, viewAttackWeight);
        }
    }

    void BuildViewWeapon(Transform weapon)
    {
        if (viewWeapon != null)
            Destroy(viewWeapon.gameObject);

        // 렌더러만 복사 (콜라이더·스크립트 없음). 카메라 자식이라 위아래 봐도 화면 속 위치 고정
        GameObject copy = new GameObject("FPSWeapon");
        viewWeapon = copy.transform;
        viewWeapon.SetParent(cameraPivot, false);
        Vector3 ls = weapon.lossyScale;
        Vector3 ps = cameraPivot.lossyScale;
        viewBaseScale = new Vector3(ls.x / ps.x, ls.y / ps.y, ls.z / ps.z);
        viewWeapon.localScale = viewBaseScale;

        MeshFilter[] filters = weapon.GetComponentsInChildren<MeshFilter>(true);
        var list = new List<Renderer>();
        for (int i = 0; i < filters.Length; i++)
        {
            MeshRenderer src = filters[i].GetComponent<MeshRenderer>();
            if (src == null || filters[i].sharedMesh == null)
                continue;
            GameObject part = new GameObject(filters[i].name);
            part.transform.SetParent(viewWeapon, false);
            part.transform.localPosition = weapon.InverseTransformPoint(filters[i].transform.position);
            part.transform.localRotation = Quaternion.Inverse(weapon.rotation) * filters[i].transform.rotation;
            part.transform.localScale = filters[i].transform.lossyScale.x / weapon.lossyScale.x * Vector3.one;
            part.AddComponent<MeshFilter>().sharedMesh = filters[i].sharedMesh;
            MeshRenderer mr = part.AddComponent<MeshRenderer>();
            mr.sharedMaterials = src.sharedMaterials;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.enabled = false;
            list.Add(mr);
        }

        viewRenderers = list.ToArray();
        handRenderers = weapon.GetComponentsInChildren<Renderer>(true);
        viewWeaponSource = weapon;
    }

    void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam != fpsCam || viewRenderers == null || State != WinderState.Play)
            return;
        SetViewWeaponVisible(true);
    }

    void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam != fpsCam || viewRenderers == null)
            return;
        SetViewWeaponVisible(false);
    }

    void SetViewWeaponVisible(bool fps)
    {
        for (int i = 0; i < viewRenderers.Length; i++)
            viewRenderers[i].enabled = fps;
        for (int i = 0; i < handRenderers.Length; i++)
        {
            if (handRenderers[i] != null)
                handRenderers[i].forceRenderingOff = fps;
        }
    }

    bool IsAttacking()
    {
        if (bodyAnimator == null)
            return false;

        int layer = bodyAnimator.GetLayerIndex("Attack Layer");
        if (layer < 0)
            return false;

        if (IsAttackState(bodyAnimator.GetCurrentAnimatorStateInfo(layer)))
            return true;
        return bodyAnimator.IsInTransition(layer) && IsAttackState(bodyAnimator.GetNextAnimatorStateInfo(layer));
    }

    // 애니메이터 공격 상태 이름: "Attack" (예전 이름 "Attack 0"도 인식)
    static bool IsAttackState(AnimatorStateInfo info)
    {
        return info.IsName("Attack") || info.IsName("Attack 0");
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

        // 덫 설치 모드(R 후 좌클릭 전): 태엽 정지 — 능력 지급도 멈춤
        if (trapPlacing)
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
        // 황금/덫은 레벨 없음 — 이미 들고 있으면 다음 턴까지 유지
        if ((abilityKind == AbilityGold || abilityKind == AbilityTrap) && abilityLevel > 0)
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

    void UpdateTrapTestKey()
    {
        if (!(Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)))
            return;
        if (State == WinderState.Dead || trapPlacing)
            return;

        abilityKind = AbilityTrap;
        abilityLevel = 1;
        if (hud != null)
            hud.PresentAbility(AbilityTrap, 1);
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
        else if (kind == AbilityTrap)
            BeginTrapPlacement();
    }

    void BeginTrapPlacement()
    {
        if (State == WinderState.Dead)
            return;

        CancelTrapPlacement(false);
        trapPlacing = true;
        trapPlaceValid = false;
        EnsureTrapPreview();
    }

    void CancelTrapPlacement(bool refundAbility)
    {
        trapPlacing = false;
        trapPlaceValid = false;
        if (trapPreview != null)
            trapPreview.SetActive(false);

        if (refundAbility)
        {
            abilityKind = AbilityTrap;
            abilityLevel = 1;
            if (hud != null)
                hud.PresentAbility(AbilityTrap, 1);
        }
    }

    void TickTrapPlacement()
    {
        if (Input.GetMouseButtonDown(1))
        {
            CancelTrapPlacement(true);
            return;
        }

        EnsureTrapPreview();
        UpdateTrapPreviewPose();

        if (Input.GetMouseButtonDown(0) && trapPlaceValid)
            PlaceTrapAtPreview();
    }

    void EnsureTrapPreview()
    {
        if (trapPreview != null)
        {
            trapPreview.SetActive(true);
            return;
        }

        GameObject prefab = LoadTrapPrefab();
        if (prefab == null)
        {
            Debug.LogWarning("[WinderMove] Trap 모델을 불러오지 못했습니다.");
            CancelTrapPlacement(true);
            return;
        }

        trapPreview = Instantiate(prefab);
        trapPreview.name = "TrapPreview";
        StripTrapPreviewColliders(trapPreview);
        FitTrapVisualScale(trapPreview);
        trapPreviewRenderers = trapPreview.GetComponentsInChildren<Renderer>(true);
        BuildTrapPreviewTintMaterials();
        ApplyTrapPreviewTint(false);
    }

    void BuildTrapPreviewTintMaterials()
    {
        if (trapPreviewRenderers == null || trapPreviewRenderers.Length == 0)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        trapPreviewMatValid = new Material[trapPreviewRenderers.Length];
        trapPreviewMatInvalid = new Material[trapPreviewRenderers.Length];
        for (int i = 0; i < trapPreviewRenderers.Length; i++)
        {
            Renderer r = trapPreviewRenderers[i];
            if (r == null)
                continue;

            r.enabled = true;
            r.forceRenderingOff = false;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Material valid = new Material(shader);
            valid.name = "TrapPreviewValid";
            valid.color = TrapPreviewValid;
            if (valid.HasProperty("_BaseColor"))
                valid.SetColor("_BaseColor", TrapPreviewValid);

            Material invalid = new Material(shader);
            invalid.name = "TrapPreviewInvalid";
            invalid.color = TrapPreviewInvalid;
            if (invalid.HasProperty("_BaseColor"))
                invalid.SetColor("_BaseColor", TrapPreviewInvalid);

            trapPreviewMatValid[i] = valid;
            trapPreviewMatInvalid[i] = invalid;
        }
    }

    void ApplyTrapPreviewTint(bool valid)
    {
        if (trapPreviewRenderers == null)
            return;

        Material[] mats = valid ? trapPreviewMatValid : trapPreviewMatInvalid;
        if (mats == null)
            return;

        for (int i = 0; i < trapPreviewRenderers.Length; i++)
        {
            Renderer r = trapPreviewRenderers[i];
            if (r == null || i >= mats.Length || mats[i] == null)
                continue;
            r.sharedMaterial = mats[i];
        }
    }

    static void FitTrapVisualScale(GameObject root)
    {
        if (root == null)
            return;

        root.transform.localScale = Vector3.one;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                bounds.Encapsulate(renderers[i].bounds);
        }

        float width = Mathf.Max(bounds.size.x, bounds.size.z);
        if (width < 0.001f)
            width = 1f;

        float scale = TrapTargetWidth / width;
        root.transform.localScale = Vector3.one * scale;
    }

    static void SeatTrapOnGround(GameObject root, Vector3 groundPos)
    {
        if (root == null)
            return;

        // 피벗이 어디에 있든 렌더러 바닥면이 지면에 딱 붙도록 맞춤
        root.transform.position = new Vector3(groundPos.x, groundPos.y, groundPos.z);
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled)
                bounds.Encapsulate(renderers[i].bounds);
        }

        float lift = groundPos.y - bounds.min.y;
        root.transform.position = new Vector3(groundPos.x, groundPos.y + lift, groundPos.z);
    }

    static float GetTrapWorldHeight(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return 0.35f;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled)
                bounds.Encapsulate(renderers[i].bounds);
        }

        return Mathf.Max(0.05f, bounds.size.y);
    }

    static Transform BuildTrapVisualRoot(GameObject root)
    {
        Transform existing = root.transform.Find("TrapVisual");
        if (existing != null)
            return existing;

        GameObject visual = new GameObject("TrapVisual");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        List<Transform> move = new List<Transform>();
        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform child = root.transform.GetChild(i);
            if (child == null || child == visual.transform)
                continue;
            if (child.name == "TrapCatchTrigger")
                continue;
            move.Add(child);
        }

        for (int i = 0; i < move.Count; i++)
            move[i].SetParent(visual.transform, true);

        // FBX가 루트에 MeshRenderer를 다는 경우 — 비주얼 자식으로 옮김
        MeshFilter rootMesh = root.GetComponent<MeshFilter>();
        MeshRenderer rootRenderer = root.GetComponent<MeshRenderer>();
        if (rootMesh != null && rootRenderer != null)
        {
            GameObject meshGo = new GameObject("TrapMesh");
            meshGo.transform.SetParent(visual.transform, false);
            meshGo.transform.localPosition = Vector3.zero;
            meshGo.transform.localRotation = Quaternion.identity;
            meshGo.transform.localScale = Vector3.one;

            MeshFilter newFilter = meshGo.AddComponent<MeshFilter>();
            newFilter.sharedMesh = rootMesh.sharedMesh;

            MeshRenderer newRenderer = meshGo.AddComponent<MeshRenderer>();
            newRenderer.sharedMaterials = rootRenderer.sharedMaterials;
            newRenderer.shadowCastingMode = rootRenderer.shadowCastingMode;
            newRenderer.receiveShadows = rootRenderer.receiveShadows;

            Object.Destroy(rootRenderer);
            Object.Destroy(rootMesh);
        }

        return visual.transform;
    }

    static void EnsureTrapRenderersVisible(GameObject root)
    {
        if (root == null)
            return;

        Material trapMat = null;
#if UNITY_EDITOR
        trapMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(TrapMaterialPath);
#endif
        if (trapMat == null)
            trapMat = Resources.Load<Material>("Trap");

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;
            r.enabled = true;
            r.forceRenderingOff = false;
            if (trapMat != null && (r.sharedMaterial == null || r.sharedMaterial.shader == null || r.sharedMaterial.shader.name == "Hidden/InternalErrorShader"))
                r.sharedMaterial = trapMat;
        }
    }

    GameObject LoadTrapPrefab()
    {
        if (trapPrefab != null)
            return trapPrefab;
#if UNITY_EDITOR
        trapPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TrapModelPath);
#endif
        if (trapPrefab == null)
            trapPrefab = Resources.Load<GameObject>("Trap");
        return trapPrefab;
    }

    static void StripTrapPreviewColliders(GameObject root)
    {
        Collider[] cols = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                cols[i].enabled = false;
        }

        Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null)
                bodies[i].detectCollisions = false;
        }

        WinderTrap trap = root.GetComponent<WinderTrap>();
        if (trap != null)
            trap.enabled = false;
    }

    void UpdateTrapPreviewPose()
    {
        Camera cam = null;
        if (cameraPivot != null)
        {
            cam = cameraPivot.GetComponent<Camera>();
            if (cam == null)
                cam = cameraPivot.GetComponentInChildren<Camera>();
        }
        if (cam == null)
            cam = Camera.main;
        if (cam == null || trapPreview == null)
            return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        Vector3 aimPoint = cam.transform.position + cam.transform.forward * 8f;
        bool hitGround = Physics.Raycast(ray, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore);
        if (hitGround)
            aimPoint = hit.point;

        trapPlaceValid = false;
        trapPlacePos = aimPoint;
        if (hitGround && hit.normal.y >= 0.35f)
        {
            trapPlacePos = hit.point;
            if (!WinderTrap.IsPlacementBlocked(trapPlacePos))
                trapPlaceValid = true;
        }

        trapPreview.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        SeatTrapOnGround(trapPreview, trapPlacePos);
        ApplyTrapPreviewTint(trapPlaceValid);
    }

    void PlaceTrapAtPreview()
    {
        if (WinderTrap.IsPlacementBlocked(trapPlacePos))
            return;

        GameObject prefab = LoadTrapPrefab();
        if (prefab == null)
            return;

        GameObject placed = Instantiate(prefab, trapPlacePos, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        placed.name = "WinderTrap";
        FitTrapVisualScale(placed);
        SeatTrapOnGround(placed, trapPlacePos);
        EnsureTrapRenderersVisible(placed);
        PreparePlacedTrap(placed);

        CancelTrapPlacement(false);
    }

    static void PreparePlacedTrap(GameObject root)
    {
        // 모델 콜라이더는 끄고, 전용 가장자리 트리거만 사용
        Collider[] cols = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                cols[i].enabled = false;
        }

        float height = GetTrapWorldHeight(root);

        Bounds bounds = new Bounds(root.transform.position, Vector3.one * 0.5f);
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    bounds.Encapsulate(renderers[i].bounds);
            }
        }

        GameObject triggerGo = new GameObject("TrapCatchTrigger");
        triggerGo.transform.SetParent(root.transform, false);
        triggerGo.transform.position = new Vector3(bounds.center.x, root.transform.position.y + 0.2f, bounds.center.z);
        SphereCollider sphere = triggerGo.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        float worldRadius = Mathf.Max(0.55f, Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.9f);
        float lossy = Mathf.Max(0.01f, root.transform.lossyScale.x);
        sphere.radius = worldRadius / lossy;
        sphere.center = Vector3.zero;

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb == null)
            rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.detectCollisions = true;

        WinderTrap trap = root.GetComponent<WinderTrap>();
        if (trap == null)
            trap = root.AddComponent<WinderTrap>();
        trap.Arm(sphere, height);
    }

    bool IsWinderCameraActive()
    {
        if (cameraPivot == null)
            return false;
        Camera cam = cameraPivot.GetComponent<Camera>();
        if (cam == null)
            cam = cameraPivot.GetComponentInChildren<Camera>();
        return cam != null && cam.enabled && cam.gameObject.activeInHierarchy;
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
        runDuration = runTimeLeft;
        PlayRunEffect();
    }

    /// <summary>달리기 능력을 쓰는 순간 슝! (이펙트는 약 2초 뒤 저절로 사라짐)</summary>
    void PlayRunEffect()
    {
        if (runEffect == null)
            return;
        if (runParticles == null)
            runParticles = runEffect.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < runParticles.Length; i++)
        {
            runParticles[i].Clear(true);
            runParticles[i].Play(true);
        }
    }

    void UseFaintAbility(int level)
    {
        if (State == WinderState.Dead)
            return;

        State = WinderState.Faint;
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
            SettleDownedBody();
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

        bool downed = State == WinderState.Dead || State == WinderState.Faint;
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
            float standBottom = standColliderCenter.y - standCapsuleHeight * 0.5f;
            if (min.y < standBottom)
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
        // 반지름은 높이 기준 — 폭 기준이면 캡슐 바닥이 땅 밑으로 내려가 몸이 위로 떠오름
        float radius = Mathf.Clamp(size.y * 0.5f, 0.18f, Mathf.Max(lateral * 0.5f, 0.18f));
        float capsuleHeight = Mathf.Max(longLen, radius * 2f + 0.02f);

        Vector3 center = (min + max) * 0.5f;
        center.y = min.y + radius;
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

        Vector3 planar = PlayerUnstickUtil.SlideWishAgainstWalls(bodyCollider, transform, PlanarVelocity());
        if (planar.sqrMagnitude > 0.01f)
            StepOverLowSeam(planar);

        if (rb != null && !rb.isKinematic)
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
        if (GameSettings.MenuOpen)
            return;

        GameSettings.EnsureLoaded();
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        if (PressedTurnAround())
        {
            Quaternion turned = Quaternion.Euler(0f, transform.eulerAngles.y + 180f, 0f);
            transform.rotation = turned;
            if (rb != null && !rb.isKinematic)
                rb.rotation = turned;
        }

        transform.Rotate(Vector3.up * mouseX * GameSettings.HorizontalSensitivity * Time.deltaTime);

        xRotation -= mouseY * GameSettings.VerticalSensitivity * Time.deltaTime;
        xRotation = Mathf.Clamp(xRotation, -80f, 80f);

        cameraPivot.localRotation =
            Quaternion.Euler(xRotation, 0f, 0f);
    }

    public void Jump()
    {
        // 공중·쿨다운 중이면 점프 불가
        if (rb == null || rb.isKinematic || !CanJump)
            return;

        ReleaseStep();

        // 이동키로 만든 수평 속도. 없으면 제자리 수직 점프
        Vector3 planar = PlanarVelocity();
        if (planar.sqrMagnitude < 0.01f)
            planar = Vector3.zero;

        // 그 방향 그대로 점프 (장애물에 비비면 물리가 막음)
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
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        Physics.ContactModifyEvent -= ModifyStepContacts;
        ReleaseStep();
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
            // 엎드린 상태: 기어가기 방향 (W 앞, S 뒤, A 왼쪽, D 오른쪽)
            animationController.SetProneMove(MoveInput);
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