using UnityEngine;

public class PlayerGravity : MonoBehaviour
{
    [SerializeField] public float gravityStrength = 9.81f;
    [SerializeField] private float fallMultiplier = 2.0f;
    [SerializeField] private float groundStickForce = 5f;

    private CharacterController controller;
    private Vector3 velocity;
    private Vector3 currentGravity; // Current gravity vector computed each frame

    // This will be set dynamically when entering a GravityZone.
    // We use the zone's groundObject to update gravity direction as the planet rotates.
    private Transform gravityZoneReference;

    private bool isGrounded = false;
    private bool wasGrounded = false;

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        currentGravity = Vector3.down * gravityStrength; // Default gravity if no zone is active.
    }

    private void FixedUpdate()
    {
        UpdateGravityDirection();
        ApplyGravity();
    }

    // Update the gravity vector based on the active gravity zone.
    // If a GravityZone is active, currentGravity updates dynamically using its ground object's orientation.
    private void UpdateGravityDirection()
    {
        if (gravityZoneReference != null)
        {
            currentGravity = -gravityZoneReference.up * gravityStrength;
        }
        else
        {
            currentGravity = Vector3.down * gravityStrength;
        }
    }

    // Applies gravitational acceleration and moves the character.
    private void ApplyGravity()
    {
        isGrounded = controller.isGrounded;

        if (!isGrounded)
        {
            // Increase falling speed for a more natural feel.
            velocity += currentGravity * fallMultiplier * Time.deltaTime;
        }
        else
        {
            if (!wasGrounded)
            {
                // Apply a slight force to keep the player firmly on the ground.
                velocity = -currentGravity * groundStickForce * Time.deltaTime;
            }
        }

        controller.Move(velocity * Time.deltaTime);
        wasGrounded = isGrounded;
    }

    // Called by PlayerJump to apply an upward impulse.
    public void ApplyJumpImpulse(Vector3 impulse)
    {
        velocity = impulse;
    }

    // Returns the current upward direction relative to gravity.
    // When in a gravity zone, this is simply the ground object's up.
    public Vector3 GetUpwardDirection()
    {
        if (gravityZoneReference != null)
            return gravityZoneReference.up;
        return -currentGravity.normalized;
    }

    // When the player enters a GravityZone, update the gravity reference.
    private void OnTriggerEnter(Collider other)
    {
        GravityZone gravityZone = other.GetComponent<GravityZone>();
        if (gravityZone != null)
        {
            gravityZoneReference = gravityZone.groundObject;
            currentGravity = -gravityZoneReference.up * gravityStrength;
            AlignPlayerToGravity();
        }
    }

    // Optionally, when exiting the zone, you might want to reset the gravity reference.
    private void OnTriggerExit(Collider other)
    {
        GravityZone gravityZone = other.GetComponent<GravityZone>();
        if (gravityZone != null && gravityZone.groundObject == gravityZoneReference)
        {
            gravityZoneReference = null;
        }
    }

    // Smoothly rotates the player so that its up aligns with the current gravity zone's up.
    private void AlignPlayerToGravity()
    {
        if (gravityZoneReference != null)
        {
            Quaternion targetRotation = Quaternion.FromToRotation(transform.up, gravityZoneReference.up) * transform.rotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
        }
    }
}
