using UnityEngine;

public class WinderMove : MonoBehaviour
{
    public enum WinderState
    {
        Play,
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
    private Rigidbody rb;
    private float JumpPower = 4.5f;

    private bool isGrounded = true;
    private bool isProne = false;

    public bool IsGrounded => isGrounded;

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
    }

    void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        switch (State)
        {
            case WinderState.Dead:
                return;
            default:
                InputUpdate();
                Prone();
                Look();
                Move();
                UpdateMoveAnimation();
                return;
        }
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

        transform.Rotate(Vector3.up * mouseX * sensitivity * Time.deltaTime);

        xRotation -= mouseY * sensitivity * Time.deltaTime;
        xRotation = Mathf.Clamp(xRotation, -80f, 80f);

        cameraPivot.localRotation =
            Quaternion.Euler(xRotation, 0f, 0f);
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