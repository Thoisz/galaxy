using UnityEngine;

public class PlayerAnimationHandler : MonoBehaviour
{
    private Animator animator;
    private PlayerPhysicsController physicsController;
    private Rigidbody rb;

    void Start()
    {
        animator = GetComponent<Animator>();
        physicsController = GetComponent<PlayerPhysicsController>();
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        float speed = new Vector3(rb.velocity.x, 0, rb.velocity.z).magnitude;
        animator.SetFloat("Speed", speed);
        animator.SetBool("isGrounded", physicsController.IsGrounded);

        if (!physicsController.IsGrounded && rb.velocity.y < -0.1f)
        {
            animator.SetBool("isFalling", true);
        }
        else
        {
            animator.SetBool("isFalling", false);
        }
    }
}
