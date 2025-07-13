using UnityEngine;

public class GravityModifier : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private PlayerJump _playerJump;

    [Header("Gravity Settings")]
    [SerializeField] private float _baseGravityMultiplier = 1.0f;
    [SerializeField] private float _fallingGravityMultiplier = 1.5f;
    [SerializeField] private float _fastFallingGravityMultiplier = 2.0f;
    [SerializeField] private float _fastFallVelocityThreshold = -8f;
    [SerializeField] private float _terminalVelocity = -50f;

    // Private references
    private Rigidbody _rigidbody;
    private GravityBody _gravityBody;
    private float _defaultGravityForce;

    private void Start()
    {
        // Get components
        _rigidbody = GetComponent<Rigidbody>();
        _gravityBody = GetComponent<GravityBody>();
        
        // Find player movement script if not assigned
        if (_playerMovement == null)
            _playerMovement = GetComponent<PlayerMovement>();
            
        // Find jump controller if not assigned
        if (_playerJump == null)
            _playerJump = GetComponent<PlayerJump>();
            
        // Store default gravity force
        if (_gravityBody != null)
        {
            // Using reflection to get the private field's value
            // This is a bit of a hack, but allows us to access the gravityBody's force value
            System.Reflection.FieldInfo field = typeof(GravityBody).GetField("gravityForce", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
                
            if (field != null)
                _defaultGravityForce = (float)field.GetValue(_gravityBody);
            else
                _defaultGravityForce = 800f; // Fallback to default value in GravityBody
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
        // Skip if not falling
        if (_playerMovement.IsGrounded())
            return;
            
        // Get velocity in gravity direction
        Vector3 gravityDir = _gravityBody.GravityDirection.normalized;
        float velocityInGravityDirection = Vector3.Dot(_rigidbody.velocity, gravityDir);
        
        // Determine current jump state and apply appropriate gravity multiplier
        float gravityMultiplier = _baseGravityMultiplier;
        
        // Apply stronger gravity when falling
        if (velocityInGravityDirection > 0.1f) // Positive value means falling toward gravity
        {
            // Apply extra gravity when falling
            gravityMultiplier = _fallingGravityMultiplier;
            
            // Apply even more gravity when falling fast
            if (velocityInGravityDirection > _fastFallVelocityThreshold)
                gravityMultiplier = _fastFallingGravityMultiplier;
        }
        
        // Apply extra gravity force
        float extraGravity = (_defaultGravityForce * gravityMultiplier) - _defaultGravityForce;
        _rigidbody.AddForce(gravityDir * extraGravity * Time.fixedDeltaTime, ForceMode.Acceleration);
    }

    private void ApplyTerminalVelocity()
    {
        // Limit falling speed to terminal velocity
        Vector3 gravityDir = _gravityBody.GravityDirection.normalized;
        float velocityInGravityDirection = Vector3.Dot(_rigidbody.velocity, gravityDir);
        
        // If exceeding terminal velocity in gravity direction
        if (velocityInGravityDirection > Mathf.Abs(_terminalVelocity))
        {
            // Calculate how much velocity to remove
            float excessVelocity = velocityInGravityDirection - Mathf.Abs(_terminalVelocity);
            
            // Apply opposing force to limit velocity
            _rigidbody.AddForce(-gravityDir * excessVelocity, ForceMode.VelocityChange);
        }
    }
}