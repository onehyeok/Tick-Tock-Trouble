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
    private const float CameraDistanceScale = 0.62f;
    private const float CameraLiftScale = 0.22f;
    private const float CameraPitchScale = 0.28f;
    private const float CameraLookAheadScale = 0.4f;
    private const float MinCameraPitch = -20f;
    private const float MaxCameraPitch = 45f;
    private const float FixedFieldOfView = 78f;

    private TickToyAnimation animationController;

    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float proneMoveSpeedMultiplier = 0.5f;
    [SerializeField] private float sensitivity = 200f;

    private float cameraPitch = 15f;
    private Rigidbody rb;
    private Collider bodyCollider;
    private Camera tpsCamera;
    private Transform body;
    private Vector3 bodyRestLocalPosition;
    private float bodyHeight = 2.2f;
    private float focusHeight = 1.1f;
    private float yaw;
    private float JumpPower = 4.5f;

    private bool isGrounded = true;
    private bool isProne = false;
    private float runTimeLeft;
    private bool faintCanBreak;
    private float faintTimeLeft;
    private int grantedAbility;
    private int grantedLevel;
    private float abilityDuration;

    public bool IsFainted => State == TickToyState.Faint;
    public bool IsUsingAbility => State == TickToyState.Run || State == TickToyState.Faint || runTimeLeft > 0f;
    public float AbilityDuration => abilityDuration;
    public float AbilityTimeLeft => runTimeLeft > 0f ? runTimeLeft : faintTimeLeft;

    public bool IsGrounded => isGrounded;

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
    }

    void Start()
    {
        CacheBodyFrame();
        Vector3 flat = transform.forward;
        flat.y = 0f;
        yaw = flat.sqrMagnitude > 0.0001f
            ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg
            : transform.eulerAngles.y;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
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
        if (faintCanBreak && PressedMoveKeyDown())
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
        isProne = Input.GetKey(KeyCode.Z);

        animationController.SetProne(isProne);

    }

    void Move(float speedScale)
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

        float currentSpeed = moveSpeed * speedScale;
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
            - forward * (bodyHeight * CameraDistanceScale * Mathf.Cos(pitchRadians))
            + Vector3.up * (bodyHeight * CameraLiftScale + bodyHeight * CameraPitchScale * Mathf.Sin(pitchRadians));
        Vector3 lookTarget = focus + forward * (bodyHeight * CameraLookAheadScale);

        cameraPivot.position = desired;

        Vector3 look = lookTarget - desired;
        look.x = forward.x;
        look.z = forward.z;
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
        SetBodySolid(false);
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

    void SetBodySolid(bool solid)
    {
        if (bodyCollider != null)
            bodyCollider.enabled = solid;

        if (rb != null)
            rb.isKinematic = !solid;
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