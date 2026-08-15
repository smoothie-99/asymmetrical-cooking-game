using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5.0f;
    public float rotationSpeed = 10.0f;

    private Rigidbody rb;
    private Animator anim;
    private Vector3 moveDir;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>();
    }

    void Update()
    {
        float h = Input.GetAxisRaw("Horizontal"); //A, D
        float v = Input.GetAxisRaw("Vertical");   //W, S

        moveDir = new Vector3(h, 0, v).normalized;

        bool isWalking = moveDir.magnitude > 0.01f;
        if(anim != null) anim.SetBool("isWalking", isWalking);
    }

    void FixedUpdate()
    {
        if(moveDir.magnitude > 0.01f)
        {
            rb.MovePosition(transform.position + moveDir * moveSpeed * Time.fixedDeltaTime);

            Quaternion targetRotation = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }
        else
        {
            rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
            rb.angularVelocity = Vector3.zero;
        }
    }
}
