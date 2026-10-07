using UnityEngine;
using UnityEngine.InputSystem;

public class TickToyMove : MonoBehaviour
{
    public Vector2 MoveInput { get; private set; }
    public bool IsWinding { get; set; }

    [Header("TPS Camera")]
    [SerializeField] private Transform cameraPivot;
    private const float CameraDistanceScale = 0.62f;
    private const float CameraLiftScale = 0.22f;
    private const float CameraPitchScale = 0.28f;
    private const float CameraLookAheadScale = 0.4f;
    private const float MinCameraPitch = -20f;
    private const float MaxCameraPitch = 45f;
    private const float CameraSmoothTime = 0.12f;
    private const float FixedFieldOfView = 78f;

    private TickToyAnimation animationController;

    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float proneMoveSpeedMultiplier = 0.5f;
    [SerializeField] private float sensitivity = 200f;

    private float cameraPitch = 15f;
    private Rigidbody rb;
    private Camera tpsCamera;
    private Transform body;
    private Vector3 bodyRestLocalPosition;
    private float bodyHeight = 2.2f;
    private float focusHeight = 1.1f;
    private Vector3 cameraVelocity;
    private bool cameraPlaced;
    private float JumpPower = 4.5f;

    private bool isGrounded = true;
    private bool isProne = false;
    private bool isDead = false;
    private bool runAbilityActive;
    private float runTimeLeft;
    private bool isFainted;
    private bool faintCanBreak;
    private float faintTimeLeft;
    private int grantedAbility;
    private int grantedLevel;
    private float abilityDuration;

    public bool IsFainted => isFainted;
    public bool IsUsingAbility => runAbilityActive || isFainted;
    public float AbilityDuration => abilityDuration;
    public float AbilityTimeLeft => runAbilityActive ? runTimeLeft : faintTimeLeft;

    public bool IsGrounded => isGrounded;

    public bool IsDead
    {
        get { return isDead; }
        set { isDead = value; }
    }

    void Awake()
    {
        animationController = GetComponent<TickToyAnimation>();
        rb = GetComponent<Rigidbody>();
        EnsureTpsCamera();
    }

    void Start()
    {
        CacheBodyFrame();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        if (isDead)
            return;

        InputUpdate();
        if (PressedAbilityKey())
            UseGrantedAbility();
        UpdateRunAbility();

        if (isFainted && faintCanBreak && PressedMoveKeyDown())
            BreakFaint();

        if (isFainted && faintTimeLeft >= 0f)
        {
            faintTimeLeft -= Time.deltaTime;
            if (faintTimeLeft <= 0f)
                BreakFaint();
        }

        if (isFainted)
        {
            animationController.SetFaint(true);
            if (rb != null)
            {
                rb.angularVelocity = Vector3.zero;
                Vector3 velocity = rb.linearVelocity;
                velocity.x = 0f;
                velocity.z = 0f;
                rb.linearVelocity = velocity;
            }
            return;
        }

        Prone();
        Look();
        Move();
        UpdateMoveAnimation();
    }

    void LateUpdate()
    {
        if (isFainted)
            faintCanBreak = true;

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
        isProne = Input.GetKey(KeyCode.Z);

        animationController.SetProne(isProne);
    }

    void Move()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            return;

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 dir = forward * MoveInput.y + right * MoveInput.x;
        if (dir.sqrMagnitude > 0.0001f)
            dir.Normalize();

        float currentSpeed = runAbilityActive ? moveSpeed * 1.4f : moveSpeed;
        if (isProne)
            currentSpeed *= proneMoveSpeedMultiplier;

        if (rb != null)
        {
            rb.angularVelocity = Vector3.zero;
            Vector3 velocity = rb.linearVelocity;
            velocity.x = dir.x * currentSpeed;
            velocity.z = dir.z * currentSpeed;
            rb.linearVelocity = velocity;
            return;
        }

        if (dir.sqrMagnitude < 0.0001f)
            return;

        transform.position += dir.normalized * currentSpeed * Time.deltaTime;
    }

    void Look()
    {
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        transform.Rotate(Vector3.up * mouseX * sensitivity * Time.deltaTime);

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
            - forward * (bodyHeight * CameraDistanceScale * Mathf.Cos(pitchRadians))
            + Vector3.up * (bodyHeight * CameraLiftScale + bodyHeight * CameraPitchScale * Mathf.Sin(pitchRadians));
        Vector3 lookTarget = focus + forward * (bodyHeight * CameraLookAheadScale);

        if (!cameraPlaced)
        {
            cameraPivot.position = desired;
            cameraVelocity = Vector3.zero;
            cameraPlaced = true;
        }
        else
        {
            cameraPivot.position = Vector3.SmoothDamp(cameraPivot.position, desired, ref cameraVelocity, CameraSmoothTime);
        }

        cameraPivot.LookAt(lookTarget, Vector3.up);
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

    void UpdateRunAbility()
    {
        if (!runAbilityActive)
            return;

        runTimeLeft -= Time.deltaTime;
        if (runTimeLeft <= 0f)
            runAbilityActive = false;
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
        runAbilityActive = true;
        if (level <= 1)
            abilityDuration = 5f;
        else if (level == 2)
            abilityDuration = 8f;
        else
            abilityDuration = 12f;

        runTimeLeft = abilityDuration;
    }

    public void UseFaintAbility(int level)
    {
        if (isDead)
            return;

        isFainted = true;
        faintCanBreak = false;
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
        if (!isFainted)
            return;

        if (!faintCanBreak)
            return;

        isFainted = false;
        faintCanBreak = false;
        animationController.SetFaint(false);
    }

    public void Jump()
    {
        if (isGrounded)
        {
            rb.AddForce(
                Vector3.up * JumpPower,
                ForceMode.Impulse);

            isGrounded = false;
        }
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

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer ==
            LayerMask.NameToLayer("Ground"))
        {
            isGrounded = true;
        }
    }
}