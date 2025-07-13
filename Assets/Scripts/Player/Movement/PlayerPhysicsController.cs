using UnityEngine;

public class PlayerPhysicsController : MonoBehaviour
{
    [Header("Physics Settings")]
    public float gravityStrength = 20f;
    public float groundCheckRadius = 0.3f;
    public LayerMask groundLayer;
    public Transform groundCheck;

    [Header("Rotation Smoothing")]
    public float rotationSmoothing = 5f; // Adjust this to control the smoothness of the rotation transition

    private Rigidbody rb;
    private Vector3 gravityDirection = Vector3.down;
    private bool isGrounded;
    private Quaternion targetRotation; // The desired rotation based on current gravity

    public bool IsGrounded => isGrounded;
    public Vector3 GravityDirection => gravityDirection;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false; // We'll apply gravity manually.
        targetRotation = transform.rotation; // Start with the current orientation.
    }

    void FixedUpdate()
    {
        ApplyGravity();
        CheckGrounded();
    }

    void Update()
    {
        // Smoothly transition the player's rotation toward the target rotation.
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSmoothing * Time.deltaTime);
    }

    /// <summary>
    /// Applies a continuous gravitational force to the player based on the current gravity direction.
    /// </summary>
    void ApplyGravity()
    {
        rb.velocity += gravityDirection * gravityStrength * Time.fixedDeltaTime;
    }

    /// <summary>
    /// Checks if the player is grounded using a sphere check.
    /// Note: Ensure the groundCheck transform moves with the player’s orientation.
    /// </summary>
    void CheckGrounded()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer);
    }

    /// <summary>
    /// Updates the player's gravity direction and computes a new target rotation.
    /// </summary>
    /// <param name="newGravity">The new gravity direction to apply.</param>
    public void SetGravity(Vector3 newGravity)
    {
        gravityDirection = newGravity.normalized;
        // Calculate the rotation needed so that the player's "up" (transform.up) aligns with the opposite of gravity.
        targetRotation = Quaternion.FromToRotation(transform.up, -gravityDirection) * transform.rotation;
    }

    /// <summary>
    /// Moves the player horizontally relative to the current gravity direction.
    /// The velocity is projected onto the plane perpendicular to gravity.
    /// </summary>
    /// <param name="velocity">The desired movement velocity.</param>
    public void Move(Vector3 velocity)
    {
        // Project the provided velocity onto the plane that is perpendicular to the gravity direction.
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(velocity, gravityDirection);
        // Preserve any velocity already along the gravity direction (e.g., falling or jumping)
        rb.velocity = horizontalVelocity + Vector3.Project(rb.velocity, gravityDirection);
    }

    /// <summary>
    /// Adds an extra velocity to the player's rigidbody.
    /// </summary>
    /// <param name="velocity">The additional velocity to add.</param>
    public void AddVelocity(Vector3 velocity)
    {
        rb.velocity += velocity;
    }
}
