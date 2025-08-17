using UnityEngine;
using System.Collections.Generic;

public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _groundCheck;
    [SerializeField] private Animator _animator;
    [SerializeField] private Transform _cameraTransform;

    [Header("Movement Settings")]
    [SerializeField] private LayerMask _groundMask;
    [SerializeField] private float _groundCheckRadius = 0.3f;
    [SerializeField] private float _moveSpeed = 8f;
    [SerializeField] private float _runningTurnSpeed = 15f;
    [SerializeField] private float _groundFrictionDamp = 10f;

    [Header("Equipment Speed Modifiers")]
    private float _baseMoveSpeed; // We'll set this from your current _moveSpeed value
    private List<float> _speedModifiers = new List<float>();
    
    [Header("Wall Interaction")]
    [SerializeField] private float _wallCheckDistance = 0.6f;

    // Private references
    private Rigidbody _rigidbody;
    private GravityBody _gravityBody;
    private Vector3 _moveDirection;
    private Vector3 _worldMoveDirection;
    private bool _isGrounded;
    private PlayerCamera _playerCamera;
    private float _lastJumpTime = -10f; // Track when the last jump occurred
    private PlayerDash _playerDash;

    // Movement state
    private Vector3 _lastFrameVelocity;
    private bool _wasGroundedLastFrame;
    private bool _hasMovementInput; // Added to track if there's active movement input
    
    // For preserving movement direction across gravity changes
    private Vector3 _lastValidMoveDirection = Vector3.forward;

    // Singleton for easy access from other scripts
    public static PlayerMovement instance;

    void Start()
{
    // Singleton setup
    if (instance == null)
    {
        instance = this;
    }
    else
    {
        Destroy(gameObject);
        return;
    }

    _rigidbody = GetComponent<Rigidbody>();
    _gravityBody = GetComponent<GravityBody>();
    _playerCamera = FindObjectOfType<PlayerCamera>();
    _playerDash = GetComponent<PlayerDash>();

    // Store the base speed from your inspector value
    _baseMoveSpeed = _moveSpeed;

    // Create and apply frictionless material
    CreateAndApplyFrictionlessMaterial();

    if (_groundCheck == null)
        Debug.LogWarning("GroundCheck transform not assigned on PlayerMovement.");

    if (_animator == null)
        Debug.LogWarning("Animator not assigned on PlayerMovement.");

    if (_cameraTransform == null && Camera.main != null)
        _cameraTransform = Camera.main.transform;
}

    void Update()
    {
        // 1) Ground check
        _wasGroundedLastFrame = _isGrounded;
        _isGrounded = CheckGrounded();

        // 2) Get input and calculate move direction relative to camera
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        
        // Track if there's any movement input
        _hasMovementInput = (Mathf.Abs(h) > 0.1f || Mathf.Abs(v) > 0.1f);
        
        CalculateMoveDirection(h, v);

        // 3) Update animator if available
        UpdateAnimator();
    }

    void FixedUpdate()
    {
        // Store velocity for next frame
        _lastFrameVelocity = _rigidbody.velocity;

        // Apply movement
        ApplyMovement();

        // Check for wall contact and handle it, or apply normal friction
        Vector3 wallNormal;
        if (!_isGrounded && CheckWallContact(out wallNormal))
        {
            ApplyWallSliding(wallNormal);
        }
        else
        {
            // Apply normal friction
            ApplyFriction();
        }
    }

    private bool CheckGrounded()
    {
        if (_groundCheck == null)
            return false;

        // Get gravity direction
        Vector3 gravityDir = _gravityBody != null ? _gravityBody.GravityDirection.normalized : Vector3.down;
        
        // ALWAYS return false for a short time after jumping
        float timeSinceJump = Time.time - _lastJumpTime;
        if (timeSinceJump < 0.2f) // Force "not grounded" for 0.2 seconds after jumping
        {
            return false;
        }
        
        // Use raycast instead of sphere check - more precise for ground detection
        float rayDistance = _groundCheckRadius + 0.1f;
        bool hitGround = Physics.Raycast(
            _groundCheck.position,
            gravityDir,
            out RaycastHit hitInfo,
            rayDistance,
            _groundMask
        );
        
        // Only consider grounded if we're close enough to the ground
        return hitGround && hitInfo.distance < rayDistance;
    }

    private void CalculateMoveDirection(float horizontal, float vertical)
    {
        // Get raw input direction
        _moveDirection = new Vector3(horizontal, 0f, vertical).normalized;

        // If we have camera and input, calculate direction relative to camera view
        if (_cameraTransform != null && _moveDirection.magnitude > 0.1f)
        {
            // Get camera forward and right, but project them onto the plane perpendicular to gravity
            Vector3 gravityUp = _gravityBody != null ? -_gravityBody.GravityDirection.normalized : transform.up;
            
            // Project camera forward/right onto character's horizontal plane
            Vector3 cameraForward = Vector3.ProjectOnPlane(_cameraTransform.forward, gravityUp).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(_cameraTransform.right, gravityUp).normalized;

            // Calculate move direction in world space relative to camera view
            _worldMoveDirection = (cameraForward * _moveDirection.z + cameraRight * _moveDirection.x).normalized;
            
            // Store valid movement direction for gravity transitions
            if (_worldMoveDirection.sqrMagnitude > 0.1f)
            {
                _lastValidMoveDirection = _worldMoveDirection;
            }
        }
        else
        {
            _worldMoveDirection = Vector3.zero;
        }
    }

    private void ApplyMovement()
{
    // FIXED: Skip movement application during dash
    if (_playerDash != null && _playerDash.IsDashing())
        return;
        
    if (_worldMoveDirection.magnitude < 0.1f)
        return;

    // Calculate current speed with modifiers
    float currentSpeed = GetCurrentMoveSpeed();

    // Calculate velocity change
    Vector3 targetVelocity = _worldMoveDirection * currentSpeed;
    
    // Apply velocity
    _rigidbody.AddForce(targetVelocity - GetHorizontalVelocity(), ForceMode.VelocityChange);

    // Rotate character to face move direction
    if (_worldMoveDirection.magnitude > 0.1f)
    {
        float turnSpeed = _runningTurnSpeed;

        // Calculate rotation that keeps character up aligned with gravity while facing movement direction
        Quaternion targetRotation = Quaternion.LookRotation(_worldMoveDirection, 
            _gravityBody != null ? -_gravityBody.GravityDirection.normalized : transform.up);

        // Apply rotation with smoothing
        _rigidbody.rotation = Quaternion.Slerp(
            _rigidbody.rotation,
            targetRotation,
            Time.fixedDeltaTime * turnSpeed
        );
    }
}

    private void ApplyFriction()
{
    // FIXED: Also skip friction during dash to avoid interfering with dash physics
    if (_playerDash != null && _playerDash.IsDashing())
        return;
        
    if (_isGrounded)
    {
        // Apply friction on ground
        ApplyHorizontalDamping(_groundFrictionDamp);
    }
}

    private void ApplyHorizontalDamping(float dampFactor)
    {
        // Get gravity direction (or use global up if no gravity body)
        Vector3 gravityDir = _gravityBody != null ? 
            _gravityBody.GravityDirection.normalized : 
            Vector3.down;

        // Get horizontal and vertical components of velocity
        Vector3 velocity = _rigidbody.velocity;
        Vector3 verticalVelocity = Vector3.Project(velocity, gravityDir);
        Vector3 horizontalVelocity = velocity - verticalVelocity;

        // Only apply friction if not actively moving or if slowing down
        if (_worldMoveDirection.magnitude < 0.1f || 
            Vector3.Dot(horizontalVelocity.normalized, _worldMoveDirection) < 0.5f)
        {
            // Lerp horizontal velocity to zero
            horizontalVelocity = Vector3.Lerp(
                horizontalVelocity,
                Vector3.zero,
                Time.fixedDeltaTime * dampFactor
            );

            // Apply the new total velocity
            _rigidbody.velocity = verticalVelocity + horizontalVelocity;
        }
    }

    private Vector3 GetHorizontalVelocity()
    {
        // Get gravity direction (or use global up if no gravity body)
        Vector3 gravityDir = _gravityBody != null ? 
            _gravityBody.GravityDirection.normalized : 
            Vector3.down;

        // Get velocity and remove gravity component
        Vector3 velocity = _rigidbody.velocity;
        Vector3 verticalVelocity = Vector3.Project(velocity, gravityDir);
        return velocity - verticalVelocity;
    }

    private void UpdateAnimator()
{
    if (_animator == null)
        return;

    // Update ground state
    _animator.SetBool("isGrounded", _isGrounded);
    
    // Update running state - now using input instead of velocity
    _animator.SetBool("isRunning", _hasMovementInput);
    
    // Still track velocity for movement speed (affects animation speed)
    float horizontalSpeed = GetHorizontalVelocity().magnitude;
    float currentMaxSpeed = GetCurrentMoveSpeed();
    _animator.SetFloat("moveSpeed", horizontalSpeed / currentMaxSpeed);
}

    // Public methods that can be called from other scripts
    public bool IsGrounded()
    {
        return _isGrounded;
    }

    public Vector3 GetMoveDirection()
    {
        // If actively moving, return current direction
        if (_worldMoveDirection.sqrMagnitude > 0.1f)
            return _worldMoveDirection;
        
        // If not actively moving but we're in a gravity transition, return the last valid direction
        if (_gravityBody != null && _gravityBody.IsTransitioningGravity)
            return _lastValidMoveDirection;
            
        // Otherwise return current (probably zero) direction
        return _worldMoveDirection;
    }

    public void AddSpeedModifier(float modifier)
{
    _speedModifiers.Add(modifier);
    Debug.Log($"Added speed modifier: +{modifier}. New speed: {GetCurrentMoveSpeed()}");
}

public void RemoveSpeedModifier(float modifier)
{
    _speedModifiers.Remove(modifier);
    Debug.Log($"Removed speed modifier: -{modifier}. New speed: {GetCurrentMoveSpeed()}");
}

public float GetCurrentMoveSpeed()
{
    float totalSpeed = _baseMoveSpeed;
    
    // Add all speed modifiers
    foreach (float modifier in _speedModifiers)
    {
        totalSpeed += modifier;
    }
    
    // Ensure minimum speed
    return Mathf.Max(totalSpeed, 0.1f);
}

public float GetBaseMoveSpeed()
{
    return _baseMoveSpeed;
}

// Optional: Method to test speed modifiers easily
[ContextMenu("Test Speed Boost")]
public void TestSpeedBoost()
{
    AddSpeedModifier(10f); // Much more noticeable!
}

[ContextMenu("Remove Speed Boost")]
public void RemoveSpeedBoost()
{
    RemoveSpeedModifier(10f);
}

[ContextMenu("Test CRAZY Speed Boost")]
public void TestCrazySpeedBoost()
{
    AddSpeedModifier(20f); // SUPER fast!
}

[ContextMenu("Remove CRAZY Speed Boost")]
public void RemoveCrazySpeedBoost()
{
    RemoveSpeedModifier(20f);
}
    
    public bool HasMovementInput()
    {
        return _hasMovementInput;
    }
    
    public void NotifyJumped()
    {
        _lastJumpTime = Time.time;
        _isGrounded = false; // Force grounded to false
    }
    
    // Force update of movement direction - useful during gravity transitions
    public void UpdateMovementDirection()
    {
        // Get raw input
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        
        // Update movement direction
        CalculateMoveDirection(h, v);
    }
    
    // For debugging - visualize ground check
    void OnDrawGizmosSelected() 
    {
        if (_groundCheck != null) 
        {
            // Use different colors for grounded state
            Gizmos.color = Application.isPlaying && _isGrounded ? Color.green : Color.red;
            
            // Draw ground check radius
            Gizmos.DrawWireSphere(_groundCheck.position, _groundCheckRadius);
            
            // Draw ray for ground detection
            Vector3 gravityDir = _gravityBody != null && Application.isPlaying ? 
                _gravityBody.GravityDirection.normalized : Vector3.down;
                
            Gizmos.DrawRay(_groundCheck.position, gravityDir * (_groundCheckRadius + 0.1f));
        }
    }
    
    // NEW METHODS FOR WALL INTERACTION
    
    private void CreateAndApplyFrictionlessMaterial()
    {
        // Find all colliders on this object
        Collider[] colliders = GetComponents<Collider>();
        
        if (colliders.Length > 0)
        {
            // Create frictionless physics material
            PhysicMaterial frictionlessMaterial = new PhysicMaterial("Frictionless");
            frictionlessMaterial.dynamicFriction = 0f;
            frictionlessMaterial.staticFriction = 0f;
            frictionlessMaterial.frictionCombine = PhysicMaterialCombine.Minimum;
            
            // Apply to all colliders on character
            foreach (Collider col in colliders)
            {
                col.material = frictionlessMaterial;
            }
        }
    }
    
    private bool CheckWallContact(out Vector3 wallNormal)
    {
        wallNormal = Vector3.zero;
        
        // Skip if grounded
        if (_isGrounded)
            return false;
            
        // Check horizontal velocity - if we're not moving horizontally, no need to check walls
        Vector3 horizontalVel = GetHorizontalVelocity();
        if (horizontalVel.magnitude < 0.5f)
            return false;
            
        // Raycast in the direction of movement to detect walls
        if (Physics.Raycast(
            transform.position,
            horizontalVel.normalized,
            out RaycastHit hit,
            _wallCheckDistance,
            _groundMask))
        {
            wallNormal = hit.normal;
            return true;
        }
        
        return false;
    }
    
    private void ApplyWallSliding(Vector3 wallNormal)
    {
        // Current velocity
        Vector3 velocity = _rigidbody.velocity;
        
        // Project velocity onto the wall plane
        Vector3 slideVelocity = Vector3.ProjectOnPlane(velocity, wallNormal);
        
        // Apply the projected velocity
        _rigidbody.velocity = slideVelocity;
    }
}