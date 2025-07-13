using System.Collections;
using UnityEngine;

public class PlayerDash : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _cameraTransform;
    [SerializeField] private Rigidbody _rigidbody;
    [SerializeField] private GravityBody _gravityBody;
    [SerializeField] private Animator _animator;
    [SerializeField] private PlayerJump _playerJump;

    [Header("Dash Settings")]
    [SerializeField] private float _dashDistance = 10f;
    [SerializeField] private float _dashDuration = 0.2f;
    [SerializeField] private float _dashCooldown = 1.0f;
    [SerializeField] private float _directionChangeDelay = 0.03f;
    [SerializeField] private float _dashBufferTime = 0.1f;
    [SerializeField] private float _dashSlowdownDuration = 0.05f;
    [SerializeField] private float _dashSlowdownSpeedMultiplier = 0.3f;
    
    [Header("Input Settings")]
    [SerializeField] private KeyCode _dashKey = KeyCode.LeftShift;
    
    [Header("Effects")]
    [SerializeField] private GameObject _dashVFXPrefab;
    [SerializeField] private AudioClip _dashSound;
    [SerializeField] private TrailRenderer _trailRenderer;
    
    // Private variables
    private bool _isDashing = false;
    private bool _canDash = true;
    private bool _canChangeDirection = false;
    private PlayerStamina _playerStamina;
    private Vector3 _dashDirection;
    private Vector3 _dashDirectionFlat;
    private float _dashStartTime;
    private Quaternion _targetRotation;
    private int _dashSide = 0;
    private PlayerCamera _playerCamera;
    private float _directionChangeTimer = 0f;
    private bool _dashBuffered = false;
    private float _dashBufferStartTime = 0f;
    private bool _isDashStopping = false;    
    private Vector3 _upVector;
    private bool _isRightMousePressed = false;
    private bool _hasInputDirection = false;
    private Vector3 _lastInputDirection = Vector3.zero;
    private bool _momentumGracePeriodActive = false;
    private float _momentumGracePeriodStartTime = 0f;
    private float _momentumGracePeriodDuration = 0.5f;
    
    // Original state storage
    private Vector3 _originalVelocity;
    private bool _wasUsingGravity;
    private RigidbodyConstraints _originalConstraints;
    private bool _dashStartedGrounded = false;
    private PlayerMovement _playerMovement;
    
    // Physics-based dash variables
    private Vector3 _dashVelocity;
    private float _dashSpeed;
    private bool _hasJumpedDuringDash = false;
    
    // For smooth direction changes
    private Vector3 _currentDashDirection;
    private Vector3 _lastAppliedDirection;
    
    // For altitude maintenance during airborne dash
    private float _dashStartAltitude;
    private Vector3 _gravityDirection;

    void Start()
{
    // Auto-reference components if not set
    if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
    if (_gravityBody == null) _gravityBody = GetComponent<GravityBody>();
    if (_animator == null) _animator = GetComponent<Animator>();
    if (_playerJump == null) _playerJump = GetComponent<PlayerJump>();
    _playerMovement = GetComponent<PlayerMovement>();
    _playerStamina = GetComponent<PlayerStamina>();
    
    // Find trail renderer if not assigned
    if (_trailRenderer == null)
        _trailRenderer = GetComponent<TrailRenderer>();
    
    if (_cameraTransform == null && Camera.main != null)
        _cameraTransform = Camera.main.transform;
        
    // Set dashEnd to true at start since we're not dashing
    if (_animator != null)
    {
        _animator.SetBool("dashEnd", true);
    }
    
    // Find player camera
    _playerCamera = FindObjectOfType<PlayerCamera>();
}

    void Update()
{
    // Cache mouse button state once per frame
    bool currentRightMousePressed = Input.GetMouseButton(1);
    
    // Check if right mouse was released during dash
    if (_isDashing && _isRightMousePressed && !currentRightMousePressed)
    {
        if (_playerCamera != null)
        {
            _playerCamera.ReleaseExternalPanControl();
        }
    }
    
    _isRightMousePressed = currentRightMousePressed;
    
    // Check for dash input
    if (Input.GetKeyDown(_dashKey))
    {
        if (_canDash && !_isDashing)
        {
            // Normal dash start
            StartDash();
        }
        else if (_isDashing && CanBufferDash())
        {
            // Buffer the dash input
            BufferDash();
        }
    }
    
    // Update animation parameters - ADDED: Update both trigger and boolean
    if (_animator != null)
    {
        _animator.SetBool("dash", _isDashing); // Keep existing boolean (this was already here)
        _animator.SetBool("isDashing", _isDashing); // Add new boolean
        _animator.SetBool("dashEnd", !_isDashing);
    }
}

    void FixedUpdate()
{
    if (_isDashing)
    {
        // Update direction change timer
        if (!_canChangeDirection)
        {
            _directionChangeTimer += Time.fixedDeltaTime;
            if (_directionChangeTimer >= _directionChangeDelay)
            {
                _canChangeDirection = true;
            }
        }
        
        UpdateDash();
    }
    else if (_momentumGracePeriodActive)
    {
        // Handle momentum grace period after dash ends
        UpdateMomentumGracePeriod();
    }
}

private void UpdateMomentumGracePeriod()
{
    // Check if grace period has expired
    if (Time.time - _momentumGracePeriodStartTime >= _momentumGracePeriodDuration)
    {
        _momentumGracePeriodActive = false;
        
        // Check if there's still no input - if so, stop horizontal momentum
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        bool hasCurrentInput = Mathf.Abs(h) > 0.1f || Mathf.Abs(v) > 0.1f;
        
        if (!hasCurrentInput && _rigidbody != null)
        {
            Vector3 currentVelocity = _rigidbody.velocity;
            Vector3 gravityUp = GetCurrentUpVector();
            Vector3 verticalVelocity = Vector3.Project(currentVelocity, gravityUp);
            
            // Stop horizontal momentum
            _rigidbody.velocity = verticalVelocity;
        }
        
        return;
    }
    
    // During grace period, check if input is given - if so, end grace period early
    // FIXED: Use different variable names to avoid conflicts
    float horizontalInput = Input.GetAxisRaw("Horizontal");
    float verticalInput = Input.GetAxisRaw("Vertical");
    bool hasInput = Mathf.Abs(horizontalInput) > 0.1f || Mathf.Abs(verticalInput) > 0.1f;
    
    if (hasInput)
    {
        _momentumGracePeriodActive = false; // End grace period early since player gave input
    }
}

    private void StartDash()
{
    // Check stamina first
    if (_playerStamina != null && !_playerStamina.TryUseDashStamina())
    {
        return; // Not enough stamina, don't dash
    }
    
    _isDashing = true;
    _isDashStopping = false; // NEW: Reset stopping state (now slowing state)
    _canDash = false;
    _canChangeDirection = false;
    _directionChangeTimer = 0f;
    _hasInputDirection = false;
    _hasJumpedDuringDash = false;
    
    // Store whether dash started on ground
    if (_playerMovement != null)
    {
        _dashStartedGrounded = _playerMovement.IsGrounded();
    }
    
    // Trigger the instant dash animation
    StartCoroutine(TriggerDashInstant());
    
    // Get the up vector and gravity direction
    _upVector = _gravityBody != null ? -_gravityBody.GravityDirection.normalized : Vector3.up;
    _gravityDirection = _gravityBody != null ? _gravityBody.GravityDirection.normalized : Vector3.down;
    
    // Store current altitude for airborne dashes
    if (!_dashStartedGrounded)
    {
        _dashStartAltitude = Vector3.Dot(transform.position, -_gravityDirection);
    }
    
    // Store original state
    if (_rigidbody != null)
    {
        _originalVelocity = _rigidbody.velocity;
        _wasUsingGravity = _rigidbody.useGravity;
        _originalConstraints = _rigidbody.constraints;
    }
    
    // Calculate dash direction
    CalculateDashDirection();
    
    // Initialize smooth direction tracking
    _currentDashDirection = _dashDirectionFlat;
    _lastAppliedDirection = _dashDirectionFlat;
    
    // UPDATED: Calculate dash velocity accounting for slowdown phase
    // We still want to cover the same distance in the same time, but with variable speed
    _dashSpeed = _dashDistance / _dashDuration; // Keep original speed calculation
    _dashVelocity = _dashDirectionFlat * _dashSpeed;
    
    // Set start time
    _dashStartTime = Time.time;
    
    // Set rotation
    _targetRotation = Quaternion.LookRotation(_dashDirectionFlat, _upVector);
    transform.rotation = _targetRotation;
    
    // Apply initial dash velocity
    ApplyDashVelocity();
    
    // Effects
    if (_trailRenderer != null)
    {
        _trailRenderer.emitting = true;
    }
    
    if (_dashVFXPrefab != null)
    {
        Instantiate(_dashVFXPrefab, transform.position, Quaternion.identity);
    }
    
    if (_dashSound != null)
    {
        AudioSource.PlayClipAtPoint(_dashSound, transform.position);
    }
    
    // Animation - ADDED: Set both trigger and boolean
    if (_animator != null)
    {
        _animator.SetInteger("dashSide", _dashSide);
        _animator.SetTrigger("dash"); // Keep existing trigger
        _animator.SetBool("isDashing", true); // Add new boolean
        _animator.SetBool("dashEnd", false);
        _dashSide = (_dashSide == 0) ? 1 : 0;
    }
    
    // Camera panning - Enable if right mouse is pressed (regardless of input direction)
    if (_playerCamera != null && _isRightMousePressed)
    {
        _playerCamera.SetPanningActive(true, true);
    }
}

    private void CalculateDashDirection()
{
    float h = Input.GetAxisRaw("Horizontal");
    float v = Input.GetAxisRaw("Vertical");
    
    // Get camera directions projected on the plane
    Vector3 cameraForward = Vector3.ProjectOnPlane(_cameraTransform.forward, _upVector).normalized;
    Vector3 cameraRight = Vector3.ProjectOnPlane(_cameraTransform.right, _upVector).normalized;
    
    // Check if there's any input
    bool hasInput = Mathf.Abs(h) > 0.1f || Mathf.Abs(v) > 0.1f;
    
    if (hasInput)
    {
        // Any input direction - calculate direction relative to camera
        Vector3 inputDir = new Vector3(h, 0, v).normalized;
        _dashDirection = (cameraForward * inputDir.z + cameraRight * inputDir.x).normalized;
        _hasInputDirection = true;
        _lastInputDirection = _dashDirection;
    }
    else
    {
        // No input - dash in camera direction
        _dashDirection = cameraForward;
        _hasInputDirection = false;
        _lastInputDirection = _dashDirection;
    }
    
    // Safety check
    if (_dashDirection.magnitude < 0.1f)
    {
        _dashDirection = transform.forward;
    }
    
    // Calculate flattened direction - project onto the gravity plane
    _dashDirectionFlat = Vector3.ProjectOnPlane(_dashDirection, _upVector).normalized;
    
    // Ensure the direction has ZERO component along gravity direction
    float gravityComponent = Vector3.Dot(_dashDirectionFlat, _gravityDirection);
    if (Mathf.Abs(gravityComponent) > 0.001f)
    {
        // Remove any gravity-direction component
        _dashDirectionFlat = _dashDirectionFlat - (_gravityDirection * gravityComponent);
        _dashDirectionFlat = _dashDirectionFlat.normalized;
    }
}

    // Helper method to get current up vector relative to gravity
    private Vector3 GetCurrentUpVector()
    {
        if (_gravityBody != null && _gravityBody.GravityDirection != Vector3.zero)
        {
            return -_gravityBody.GravityDirection.normalized;
        }
        return Vector3.up; // Fallback to world up
    }

    private void UpdateDash()
{
    float timeSinceDashStart = Time.time - _dashStartTime;
    float dashMovementDuration = _dashDuration - _dashSlowdownDuration;
    
    // Check if we're in the slowdown phase
    if (timeSinceDashStart >= dashMovementDuration && !_isDashStopping)
    {
        // Enter slowdown phase
        _isDashStopping = true;
        StartDashSlowdown();
    }
    
    // End dash completely after full duration
    if (timeSinceDashStart >= _dashDuration)
    {
        EndDash();
        return;
    }
    
    // Only do normal dash movement if not in slowdown phase
    if (!_isDashStopping)
    {
        // Update direction if allowed
        if (_canChangeDirection)
        {
            UpdateDashDirection();
        }
        
        // Check if we should sync with camera for instant rotation
        bool shouldSync = _isRightMousePressed && _playerCamera != null && _playerCamera.IsPanningActive() && _hasInputDirection;
        
        if (shouldSync)
        {
            // INSTANT 1:1 camera following for yaw
            Vector3 cameraForward = Vector3.ProjectOnPlane(_cameraTransform.forward, _upVector).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(_cameraTransform.right, _upVector).normalized;
            
            if (cameraForward.sqrMagnitude > 0.01f && cameraRight.sqrMagnitude > 0.01f)
            {
                // Get current input to maintain relative direction to camera
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                
                // If no current input, use the stored direction relative to camera
                if (Mathf.Abs(h) <= 0.1f && Mathf.Abs(v) <= 0.1f)
                {
                    // Calculate what the input WOULD be based on last known direction
                    Vector3 localDirection = Vector3.zero;
                    localDirection.z = Vector3.Dot(_lastInputDirection, cameraForward);
                    localDirection.x = Vector3.Dot(_lastInputDirection, cameraRight);
                    localDirection = localDirection.normalized;
                    h = localDirection.x;
                    v = localDirection.z;
                }
                
                // Calculate new direction maintaining relative angle to camera
                Vector3 inputDir = new Vector3(h, 0, v).normalized;
                Vector3 newDirection = (cameraForward * inputDir.z + cameraRight * inputDir.x).normalized;
                
                // Update dash direction to follow camera while maintaining relative direction
                _dashDirectionFlat = newDirection;
                _currentDashDirection = _dashDirectionFlat;
                
                // INSTANT rotation - no smoothing or delay
                _targetRotation = Quaternion.LookRotation(_dashDirectionFlat, _upVector);
                transform.rotation = _targetRotation; // Apply immediately
                
                // FIXED: Apply new velocity direction even when jumped during dash
                // This allows camera panning to work during jump-dash
                ApplyDashVelocityForCameraPanning();
                _lastAppliedDirection = _currentDashDirection;
            }
        }
        else
        {
            // INSTANT direction snapping even when not syncing with camera
            // Always snap to the target direction immediately
            _currentDashDirection = _dashDirectionFlat;
            
            // Apply velocity immediately when direction changes
            float directionDifference = Vector3.Angle(_lastAppliedDirection, _currentDashDirection);
            if (directionDifference > 1f) // Lower threshold for more responsive changes
            {
                ApplyDashVelocity();
                _lastAppliedDirection = _currentDashDirection;
            }
            
            // INSTANT rotation - no smoothing
            _targetRotation = Quaternion.LookRotation(_currentDashDirection, _upVector);
            transform.rotation = _targetRotation; // Apply immediately
        }
        
        // Maintain exact dash velocity and altitude (only when not slowing down)
        MaintainDashState();
        
        // Debug line to show if we're syncing
        if (shouldSync)
        {
            Debug.DrawRay(transform.position, Vector3.up * 3, Color.yellow);
        }
    }
    else
    {
        // During slowdown phase, maintain altitude but reduce horizontal speed
        MaintainSlowdownState();
    }
    
    if (_gravityBody != null)
    {
        Debug.DrawRay(transform.position, _gravityBody.GravityDirection * 2, Color.red);
    }
}

private void StartDashSlowdown()
{
    Debug.Log("Dash slowdown phase started");
    // The actual slowdown happens in MaintainSlowdownState()
}

private void ApplyDashVelocityForCameraPanning()
{
    if (_rigidbody == null) return;
    
    // FIXED: Special case for camera panning during jump
    // We want to update horizontal direction even when jumped during dash
    Vector3 gravityUp = GetCurrentUpVector();
    Vector3 horizontalDashDirection = Vector3.ProjectOnPlane(_currentDashDirection, gravityUp).normalized;
    
    Vector3 currentVelocity = _rigidbody.velocity;
    
    if (_hasJumpedDuringDash)
    {
        // When jumped during dash, update horizontal direction while preserving vertical
        Vector3 verticalVelocity = Vector3.Project(currentVelocity, gravityUp);
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(currentVelocity, gravityUp);
        
        // FIXED: Use dash speed instead of preserving current horizontal speed
        // This allows direction changes to properly redirect the momentum
        float targetHorizontalSpeed = _dashSpeed;
        Vector3 newHorizontalVelocity = horizontalDashDirection * targetHorizontalSpeed;
        
        _rigidbody.velocity = newHorizontalVelocity + verticalVelocity;
    }
    else
    {
        // Normal dash velocity application
        Vector3 targetHorizontalVelocity = horizontalDashDirection * _dashSpeed;
        _rigidbody.velocity = targetHorizontalVelocity;
    }
}

private void MaintainSlowdownState()
{
    if (_rigidbody == null) return;
    
    Vector3 currentVelocity = _rigidbody.velocity;
    Vector3 gravityUp = GetCurrentUpVector();
    
    // Don't interfere if jumped during slowdown phase
    if (_hasJumpedDuringDash)
    {
        return;
    }
    
    // Calculate reduced horizontal speed
    Vector3 horizontalDirection = Vector3.ProjectOnPlane(_currentDashDirection, gravityUp).normalized;
    float slowedSpeed = _dashSpeed * _dashSlowdownSpeedMultiplier;
    Vector3 targetHorizontalVelocity = horizontalDirection * slowedSpeed;
    
    // Keep vertical component for altitude maintenance
    Vector3 verticalVelocity = Vector3.Project(currentVelocity, gravityUp);
    
    // For airborne dashes: maintain exact altitude during slowdown
    if (!_dashStartedGrounded)
    {
        float currentAltitude = Vector3.Dot(transform.position, -_gravityDirection);
        float altitudeDifference = currentAltitude - _dashStartAltitude;
        
        if (Mathf.Abs(altitudeDifference) > 0.01f)
        {
            Vector3 correction = -_gravityDirection * altitudeDifference;
            transform.position -= correction;
        }
    }
    
    // Apply reduced horizontal velocity + maintain vertical for altitude
    _rigidbody.velocity = targetHorizontalVelocity + verticalVelocity;
}

private void BufferDash()
{
    _dashBuffered = true;
    _dashBufferStartTime = Time.time;
    
    // Optional: Add some visual/audio feedback that dash was buffered
    Debug.Log("Dash buffered!");
}

    private void ApplyDashVelocity()
{
    if (_rigidbody == null) return;
    
    // FIXED: Don't apply dash velocity when jumped during dash
    // Let the jump handle the velocity instead
    if (_hasJumpedDuringDash)
    {
        return;
    }
    
    // ALWAYS ensure dash direction is purely horizontal relative to gravity
    Vector3 gravityUp = GetCurrentUpVector();
    Vector3 horizontalDashDirection = Vector3.ProjectOnPlane(_currentDashDirection, gravityUp).normalized;
    
    // Calculate pure horizontal velocity
    Vector3 targetHorizontalVelocity = horizontalDashDirection * _dashSpeed;
    
    // Normal dash - pure horizontal velocity (no vertical component)
    Vector3 newVelocity = targetHorizontalVelocity;
    _rigidbody.velocity = newVelocity;
}

    private void MaintainDashState()
{
    if (_rigidbody == null) return;
    
    Vector3 currentVelocity = _rigidbody.velocity;
    Vector3 gravityUp = GetCurrentUpVector();
    
    // FIXED: Don't maintain velocity at all when jumped during dash
    // Let natural physics take over for the jump
    if (_hasJumpedDuringDash)
    {
        // When jumped during dash, don't interfere with velocity at all
        // Let the jump physics work naturally
        return;
    }
    
    // Normal dash state maintenance (existing code for when not jumped)
    Vector3 expectedVelocity = _currentDashDirection * _dashSpeed;
    
    float velocityDifference = Vector3.Distance(currentVelocity, expectedVelocity);
    if (velocityDifference > 1f)
    {            
        _rigidbody.velocity = expectedVelocity;
    }
    
    Vector3 currentHorizontalVelocity = Vector3.ProjectOnPlane(currentVelocity, _gravityDirection);
    float currentHorizontalSpeed = currentHorizontalVelocity.magnitude;
    float targetSpeed = _dashSpeed;
    
    if (currentHorizontalSpeed < targetSpeed * 0.95f)
    {
        Vector3 targetHorizontalVelocity = _currentDashDirection * targetSpeed;
        _rigidbody.velocity = targetHorizontalVelocity;
    }
    
    // For airborne dashes: maintain exact altitude (only when not jumped)
    if (!_dashStartedGrounded)
    {
        float currentAltitude = Vector3.Dot(transform.position, -_gravityDirection);
        float altitudeDifference = currentAltitude - _dashStartAltitude;
        
        if (Mathf.Abs(altitudeDifference) > 0.01f)
        {
            Vector3 correction = -_gravityDirection * altitudeDifference;
            transform.position -= correction;
            
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(_rigidbody.velocity, _gravityDirection);
            _rigidbody.velocity = horizontalVelocity;
        }
    }
}

    private void UpdateDashDirection()
{
    float h = Input.GetAxisRaw("Horizontal");
    float v = Input.GetAxisRaw("Vertical");
    bool hasCurrentInput = Mathf.Abs(h) > 0.1f || Mathf.Abs(v) > 0.1f;
    
    // Check if we should be syncing with camera
    bool shouldSync = _isRightMousePressed && _playerCamera != null && _playerCamera.IsPanningActive() && _hasInputDirection;
    
    if (shouldSync)
    {
        // When syncing with camera, direction is handled in UpdateDash()
        // Don't override it here to maintain smooth camera following
        return;
    }
    
    // FIXED: Handle input changes even when jumped during dash
    if (hasCurrentInput)
    {
        Vector3 cameraForward = Vector3.ProjectOnPlane(_cameraTransform.forward, _upVector).normalized;
        Vector3 cameraRight = Vector3.ProjectOnPlane(_cameraTransform.right, _upVector).normalized;
        
        Vector3 inputDir = new Vector3(h, 0, v).normalized;
        Vector3 newDirection = (cameraForward * inputDir.z + cameraRight * inputDir.x).normalized;
        
        if (newDirection.sqrMagnitude > 0.01f)
        {
            _dashDirection = newDirection;
            _dashDirectionFlat = Vector3.ProjectOnPlane(_dashDirection, _upVector).normalized;
            _hasInputDirection = true;
            _lastInputDirection = newDirection;
            
            // FIXED: Update current dash direction immediately
            _currentDashDirection = _dashDirectionFlat;
            
            // FIXED: Apply new direction immediately, even when jumped during dash
            if (_hasJumpedDuringDash)
            {
                // When jumped during dash, apply direction change with special method
                ApplyDashVelocityForCameraPanning();
            }
            else
            {
                // Normal dash direction change
                ApplyDashVelocity();
            }
            
            _lastAppliedDirection = _currentDashDirection;
            
            // Enable camera panning if right mouse is pressed
            if (_playerCamera != null && _isRightMousePressed)
            {
                _playerCamera.SetPanningActive(true, true);
            }
            return;
        }
    }
    else
    {
        // NO INPUT: This is the key condition - stop camera sync when input is released
        if (_hasInputDirection)
        {
            // Input was just released - disable camera sync but keep last direction
            _hasInputDirection = false;
            _dashDirection = _lastInputDirection;
            _dashDirectionFlat = Vector3.ProjectOnPlane(_dashDirection, _upVector).normalized;
            
            // Disable camera panning since no input
            if (_playerCamera != null)
            {
                _playerCamera.SetPanningActive(false, true);
            }
            
            return;
        }
    }
    
    // Standstill case - only if we never had input direction
    if (_isRightMousePressed && !_hasInputDirection)
    {
        if (_playerCamera != null && !_playerCamera.IsPanningActive())
        {
            _playerCamera.SetPanningActive(true);
        }
        
        Vector3 newDirection = Vector3.ProjectOnPlane(_cameraTransform.forward, _upVector).normalized;
        
        if (newDirection.sqrMagnitude > 0.01f)
        {
            _dashDirection = newDirection;
            _dashDirectionFlat = newDirection;
        }
    }
}

    private void EndDash()
{
    _isDashing = false;
    _isDashStopping = false;
    
    // Check if this dash had no input direction
    bool dashHadNoInput = !_hasInputDirection;
    
    _hasInputDirection = false;
    _hasJumpedDuringDash = false;
    
    // FIXED: Handle velocity based on whether there was input during the dash
    if (_rigidbody != null)
    {
        Vector3 currentVelocity = _rigidbody.velocity;
        Vector3 gravityUp = GetCurrentUpVector();
        
        // Get the horizontal component of current velocity
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(currentVelocity, gravityUp);
        Vector3 verticalVelocity = Vector3.Project(currentVelocity, gravityUp);
        
        if (horizontalVelocity.magnitude > 0.1f)
        {
            // Normalize to movement speed
            Vector3 normalizedHorizontalVelocity = horizontalVelocity.normalized * 9f;
            _rigidbody.velocity = normalizedHorizontalVelocity + verticalVelocity;
            
            // Start grace period if dash had no input
            if (dashHadNoInput)
            {
                _momentumGracePeriodActive = true;
                _momentumGracePeriodStartTime = Time.time;
            }
        }
        else
        {
            // Zero horizontal momentum
            _rigidbody.velocity = verticalVelocity;
        }
    }
    
    // Effects cleanup
    if (_trailRenderer != null)
    {
        _trailRenderer.emitting = false;
    }
    
    // Animation
    if (_animator != null)
    {
        _animator.SetBool("dash", false);
        _animator.SetBool("isDashing", false);
        _animator.SetBool("dashEnd", true);
    }
    
    // Camera
    if (_playerCamera != null)
    {
        _playerCamera.ReleaseExternalPanControl();
    }
    
    // Check for buffered dash
    if (_dashBuffered)
    {
        _dashBuffered = false;
        _canDash = true;
        StartDash();
        return;
    }
    
    StartCoroutine(DashCooldown());
}

    // Jump handling - super simple since physics are normal
    public void NotifyJumpedDuringDash()
    {
        _hasJumpedDuringDash = true;
    }

    private IEnumerator TriggerDashInstant()
    {
        if (_animator != null)
        {
            _animator.SetBool("dashInstant", true);
        }
        
        yield return new WaitForSeconds(0.01f);
        
        if (_animator != null)
        {
            _animator.SetBool("dashInstant", false);
        }
    }

    private IEnumerator DashCooldown()
    {
        yield return new WaitForSeconds(_dashCooldown);
        _canDash = true;
    }

    // Public accessors
    public bool IsDashing() { return _isDashing; }
    public bool CanDash() { return _canDash; }
    public bool WasDashStartedGrounded() { return _dashStartedGrounded; }

    public void ResetDashSide()
{
    _dashSide = 0;
    if (_animator != null)
    {
        _animator.SetBool("dash", false); // Keep existing boolean
        _animator.SetBool("isDashing", false); // Add new boolean
        _animator.SetBool("dashEnd", true);
        _animator.SetBool("dashInstant", false);
    }
}

    public void EndDashEarly()
    {
        if (_isDashing)
        {
            EndDash();
        }
    }

    private bool CanBufferDash()
{
    if (!_isDashing || _dashBuffered) return false;
    
    float timeSinceDashStart = Time.time - _dashStartTime;
    float timeUntilDashEnd = _dashDuration - timeSinceDashStart;
    
    // Allow buffering only in the last portion of the dash
    return timeUntilDashEnd <= _dashBufferTime;
}

    private bool IsCameraInFirstPerson()
    {
        return _playerCamera != null && _playerCamera.IsInFirstPerson;
    }
}