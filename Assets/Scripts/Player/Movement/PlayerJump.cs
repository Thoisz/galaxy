using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerJump : MonoBehaviour
{
    [SerializeField] private InputActionReference jumpControl;
    [SerializeField] private float jumpForce = 7.0f;
    [SerializeField] private int maxJumps = 12;
    
    private CharacterController controller;
    private Animator animator;
    private int jumpCount = 0;
    private bool wasGrounded = false;
    
    private PlayerGravity playerGravity; // Reference to the gravity system

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        playerGravity = GetComponent<PlayerGravity>(); // Get reference to gravity system
    }

    private void OnEnable()
    {
        jumpControl?.action.Enable();
    }

    private void OnDisable()
    {
        jumpControl?.action.Disable();
    }

    // This method is now solely responsible for handling input and jump-related animations.
    public void HandleJump()
    {
        if (jumpControl.action.triggered && jumpCount < maxJumps)
        {
            PerformJump();
        }
        HandleLanding();
    }

    private void PerformJump()
    {
        if (controller.isGrounded)
        {
            jumpCount = 0; // Reset jump count when grounded
        }

        if (jumpCount < maxJumps)
        {
            // Instead of modifying local velocity and moving the controller here,
            // we apply the jump impulse to the gravity system.
            Vector3 jumpImpulse = playerGravity.GetUpwardDirection() * (jumpForce * Mathf.Sqrt(playerGravity.gravityStrength));
            playerGravity.ApplyJumpImpulse(jumpImpulse);

            jumpCount++;
            animator.ResetTrigger("Land");

            if (jumpCount == 1)
            {
                animator.SetTrigger("FirstJump");
            }
            else if (jumpCount == 2)
            {
                animator.SetTrigger("SecondJump");
            }
            else
            {
                int alternateJumpIndex = (jumpCount % 2 == 0) ? 0 : 1;
                animator.SetInteger("AlternateJumpIndex", alternateJumpIndex);
                animator.SetTrigger("SubsequentJump");
            }
        }
    }

    private void HandleLanding()
    {
        if (controller.isGrounded)
        {
            if (!wasGrounded)
            {
                OnLanding();
                wasGrounded = true;
            }
        }
        else
        {
            wasGrounded = false;
        }
    }

    private void OnLanding()
    {
        jumpCount = 0;
        animator.ResetTrigger("FirstJump");
        animator.ResetTrigger("SecondJump");
        animator.ResetTrigger("SubsequentJump");
        animator.SetTrigger("Land");
    }
}
