using UnityEngine;

public class TickToyMove : MonoBehaviour
{
    public Vector2 MoveInput { get; private set; }

    [Header("TPS Camera")]
    [SerializeField, HideInInspector] private Transform cameraPivot;
    private const float CameraDistance = 1f;
    private const float CameraHeight = 0.95f;
    private const float CameraLookHeight = 0.78f;
    private const float CameraVerticalRange = 0.5f;
    private const float MinCameraPitch = -20f;
    private const float MaxCameraPitch = 45f;
    private const float CameraSmooth = 14f;
    private const float FixedFieldOfView = 78f;

    private TickToyAnimation animationController;

    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float proneMoveSpeedMultiplier = 0.5f;
    [SerializeField] private float sensitivity = 200f;

    private float cameraPitch = 15f;
    private Rigidbody rb;
    private Camera tpsCamera;
    private float JumpPower = 3.0f;

    private bool isGrounded = true;
    private bool isProne = false;
    private bool isDead = false;

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
        tpsCamera = cameraPivot != null ? cameraPivot.GetComponent<Camera>() : null;
    }

    void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        if (isDead)
            return;

        InputUpdate();
        Prone();
        Look();
        Move();
        UpdateMoveAnimation();
    }

    void LateUpdate()
    {
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
        forward.y = 0;

        Vector3 right = transform.right;
        right.y = 0;

        Vector3 dir = forward * MoveInput.y + right * MoveInput.x;

        float currentSpeed = isProne ? moveSpeed * proneMoveSpeedMultiplier : moveSpeed;
        transform.position += dir.normalized * currentSpeed * Time.deltaTime;
    }

    void Look()
    {
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        transform.Rotate(
            Vector3.up * mouseX * sensitivity * Time.deltaTime);

        cameraPitch -= mouseY * sensitivity * Time.deltaTime;
        cameraPitch = Mathf.Clamp(cameraPitch, MinCameraPitch, MaxCameraPitch);
    }

    void UpdateTpsCamera()
    {
        if (cameraPivot == null)
            return;

        if (tpsCamera != null)
        {
            tpsCamera.fieldOfView = FixedFieldOfView;
        }

        float pitchRadians = cameraPitch * Mathf.Deg2Rad;
        Vector3 targetLocalPosition = new Vector3(
            0f,
            CameraHeight + Mathf.Sin(pitchRadians) * CameraVerticalRange,
            -CameraDistance * Mathf.Cos(pitchRadians));

        cameraPivot.localPosition = Vector3.Lerp(
            cameraPivot.localPosition,
            targetLocalPosition,
            Time.deltaTime * CameraSmooth);

        Vector3 lookTarget = transform.position + Vector3.up * CameraLookHeight;
        cameraPivot.LookAt(lookTarget, Vector3.up);
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