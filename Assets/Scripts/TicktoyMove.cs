using UnityEngine;

public class TicktoyMove : MonoBehaviour
{
    private float moveSpeed = 5f;
    private float sensitivity = 100f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Move();
    }

    void Move()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveY = Input.GetAxis("Vertical");

        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        transform.Rotate(mouseY * sensitivity * Time.deltaTime, mouseX * sensitivity * Time.deltaTime, 0);
        Vector3 dir = new Vector3(moveX, 0, moveY);

        transform.Translate(dir.normalized * moveSpeed * Time.deltaTime);
        
    }

    void Jump()
    {
        if(Input.GetKeyDown(KeyCode.Space))

        {

        }
    }

    void Lay()
    {
        if(Input.GetKeyDown(KeyCode.Z))
        {

        }
    }
}
