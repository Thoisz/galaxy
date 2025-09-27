using UnityEngine;

public class GravityModifier : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private PlayerJump _playerJump;
    [SerializeField] private PlayerDash _playerDash; // NEW: to detect dash

    [Header("Gravity Settings")]
    [SerializeField] private float _baseGravityMultiplier = 1.0f;
    [SerializeField] private float _fallingGravityMultiplier = 1.5f;
    [SerializeField] private float _fastFallingGravityMultiplier = 2.0f;

    [Tooltip("Speed along gravity (+) considered 'fast fall'. Use a POSITIVE value.")]
    [SerializeField] private float _fastFallVelocityThreshold = 8f; // FIX: positive

    [SerializeField] private float _terminalVelocity = -50f;

    // Private references
    private Rigidbody _rigidbody;
    private GravityBody _gravityBody;
    private float _defaultGravityForce;

    private void Start()
    {
        _rigidbody   = GetComponent<Rigidbody>();
        _gravityBody = GetComponent<GravityBody>();

        if (_playerMovement == null) _playerMovement = GetComponent<PlayerMovement>();
        if (_playerJump == null)     _playerJump     = GetComponent<PlayerJump>();
        if (_playerDash == null)     _playerDash     = GetComponent<PlayerDash>(); // NEW

        // Read GravityBody.gravityForce via reflection (kept from your original)
        if (_gravityBody != null)
        {
            System.Reflection.FieldInfo field = typeof(GravityBody).GetField(
                "gravityForce",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );
            _defaultGravityForce = field != null ? (float)field.GetValue(_gravityBody) : 800f;
        }
        else
        {
            _defaultGravityForce = 800f;
        }
    }

    private void FixedUpdate()
    {
        if (_gravityBody == null) return;

        ApplyGravityModifiers();
        ApplyTerminalVelocity();
    }

    private void ApplyGravityModifiers()
    {
        // No extra gravity when grounded
        if (_playerMovement != null && _playerMovement.IsGrounded()) return;

        // NEW: Skip extra gravity while dashing so dash can control vertical behavior cleanly
        if (_playerDash != null && _playerDash.IsDashing()) return;

        Vector3 gravityDir = _gravityBody.GravityDirection.normalized;

        // Positive when moving **with** gravity (i.e., falling)
        float vAlongGravity = Vector3.Dot(_rigidbody.velocity, gravityDir);

        // Default
        float gravityMultiplier = _baseGravityMultiplier;

        // Apply stronger gravity when falling
        if (vAlongGravity > 0.1f)
        {
            gravityMultiplier = _fallingGravityMultiplier;

            // FAST-FALL: compare to a POSITIVE threshold
            if (vAlongGravity > _fastFallVelocityThreshold)
                gravityMultiplier = _fastFallingGravityMultiplier;
        }

        // Add only the extra gravity beyond default
        float extraGravity = (_defaultGravityForce * gravityMultiplier) - _defaultGravityForce;
        if (extraGravity > 0f)
        {
            _rigidbody.AddForce(gravityDir * extraGravity * Time.fixedDeltaTime, ForceMode.Acceleration);
        }
        // If you ever want lighter-than-default while rising, you could allow negative extraGravity too.
    }

    private void ApplyTerminalVelocity()
    {
        Vector3 gravityDir = _gravityBody.GravityDirection.normalized;
        float vAlongGravity = Vector3.Dot(_rigidbody.velocity, gravityDir);

        // Clamp downward speed to |terminalVelocity|
        float maxDownSpeed = Mathf.Abs(_terminalVelocity); // e.g., 50
        if (vAlongGravity > maxDownSpeed)
        {
            float excess = vAlongGravity - maxDownSpeed;
            // Instantly remove the excess along gravity
            _rigidbody.AddForce(-gravityDir * excess, ForceMode.VelocityChange);
        }
    }
}