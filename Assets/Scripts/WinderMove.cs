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
    private Rigidbody rb;
    private float JumpPower = 4.5f;

    private bool isGrounded = true;
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
    private TickToyHud hud;

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
        bodyCollider = GetComponent<Collider>();
        CacheSpring();
    }

    void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        hud = FindAnyObjectByType<TickToyHud>();
    }

    void Update()
    {
        if (State == WinderState.Dead)
            return;

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
        if (State == WinderState.Faint)
            faintCanBreak = true;

        TickSpring();
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
        SetBodySolid(false);
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

        float currentSpeed = runTimeLeft > 0f ? moveSpeed * 2f : moveSpeed;
        if (isProne)
            currentSpeed *= proneMoveSpeedMultiplier;
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