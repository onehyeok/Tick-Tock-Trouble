using UnityEngine;

public class WinderMove : MonoBehaviour
{
    public Vector2 MoveInput { get; private set; }

    [SerializeField] private Transform cameraPivot;

    private float moveSpeed = 5f;
    private float sensitivity = 200f;

    private float xRotation;

    void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }
    void Update()
    {
        InputUpdate();
        Look();
        Move();
    }

    void InputUpdate()
    {
        MoveInput = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical"));
    }

    void Move()
    {
        Vector3 forward = transform.forward;
        forward.y = 0;

        Vector3 right = transform.right;
        right.y = 0;

        Vector3 dir =
            forward * MoveInput.y +
            right * MoveInput.x;

        transform.position +=
            dir.normalized *
            moveSpeed *
            Time.deltaTime;
    }

    void Look()
    {
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        // 좌우 회전
        transform.Rotate(Vector3.up * mouseX *sensitivity * Time.deltaTime);

        // 상하 회전
        xRotation -= mouseY * sensitivity * Time.deltaTime;

        xRotation = Mathf.Clamp(
            xRotation,
            -80f,
            80f);

        cameraPivot.localRotation =
            Quaternion.Euler(
                xRotation,
                0f,
                0f);
    }
}