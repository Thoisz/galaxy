using UnityEngine;
using System.Collections;

public class PlayerFlight : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator _animator;
    [SerializeField] private Transform _cameraTransform;

    [Header("Flight Speed Settings")]
    [SerializeField] private float _slowFlightSpeed = 6f; // Lower speed for subtle stick movement
    [SerializeField] private float _fastFlightSpeed = 12f; // Higher speed for full stick/keyboard
    [SerializeField] private float _superSpeed = 25f; // Boosted super speed
    [SerializeField] private float _speedThreshold = 0.6f; // Input magnitude threshold to transition between speeds
    [SerializeField] private float _speedTransitionRate = 5f; // How quickly to transition between speeds
    [SerializeField] private float _minInputForSuperSpeed = 0.3f; // Minimum input needed to maintain super speed
    [SerializeField] private bool _useSmoothedInput = true; // Whether to use smoothed input for speed calculation

    [Header("Super Speed Activation")]
    [SerializeField] private KeyCode _superSpeedKey = KeyCode.V; // Key to trigger super speed
    [SerializeField] private float _superSpeedDoubleTapTime = 0.3f; // Time window for double tap to activate
    [SerializeField] private bool _showDebugLogs = true; // Show debug logs for super speed activation
    [SerializeField] private float _minSpeedForSuperSpeedActivation = 10f;

    [Header("Super Speed Effects")]
    [SerializeField] private bool _useScreenEffects = true; // Whether to use screen effects during super speed
    [SerializeField] private float _fovIncrease = 15f; // How much to increase FOV during super speed
    [SerializeField] private float _fovChangeSpeed = 5f; // How quickly FOV changes

    [Header("Vertical Movement")]
    [SerializeField] private float _ascentSpeed = 8f;
    [SerializeField] private float _descentSpeed = 6f;
    private float _downwardAngleThreshold = 0.3f; // Threshold for detecting downward movement (0-1)
    [SerializeField] private float _pitchAngle = 15f; // Maximum pitch angle in degrees during ascent/descent

    [Header("Pitch Input Angle Constraints")]
    [SerializeField] private float minPitchAllowed = -45f;
    [SerializeField] private float maxPitchAllowed = 45f;

    [Header("Movement Settings")]
    [SerializeField] private float _rotationSpeed = 5f; // How fast to rotate toward movement direction
    [SerializeField] private float _airDampingFactor = 1f; // Air resistance when not moving

    [Header("Vertical Movement Delay")]
    [SerializeField] private float _ascentDelay = 0.2f; // Delay in seconds before ascending starts
    [SerializeField] private float _descentDelay = 0.2f; // Delay in seconds before descending starts

    [Header("Flight Activation")]
    [SerializeField] private KeyCode _flightActivationKey = KeyCode.Space;
    [SerializeField] private float _flightActivationTime = 0.8f; // Time in seconds to hold Space to activate flight

    [Header("Flight Deactivation")]
    [SerializeField] private KeyCode _flightDeactivationKey = KeyCode.Q;
    [SerializeField] private float _doubleTapTime = 0.3f; // Time window for double tap Q to deactivate

    [Header("Ground Detection")]
    [SerializeField] private float _groundCheckDistance = 0.6f; // How far to check for ground
    [SerializeField] private LayerMask _groundLayers; // Which layers are considered ground
    [SerializeField] private float _landingVelocityThreshold = 2f; // Minimum downward velocity to consider for landing

    [Header("Hover Effect")]
    [SerializeField] private float _hoverAmplitude = 0.1f; // How high/low the hover goes
    [SerializeField] private float _hoverFrequency = 1.0f; // How fast the hover cycle is

    [Header("Gravity Transition Handling")]
    [SerializeField] private float _gravityTransitionSpeedFactor = 0.5f; // Speed reduction during gravity transitions
    [SerializeField] private bool _maintainFlightDuringTransition = true; // Continue flying during gravity transitions
    [SerializeField] private bool _preserveCameraPanningState = true; // Preserve camera panning state during transitions

    [Header("Gravity Transition Improvements")]
    [SerializeField] private float _transitionSpeedRecoveryRate = 10f; // How quickly to recover normal speed after transition
    [SerializeField] private float _transitionOrientationSpeed = 15f; // How quickly to reorient during transitions
    [SerializeField] private bool _enableTransitionDebugLogs = true; // For debugging gravity transitions

    // Private references
    private Rigidbody _rigidbody;
    private GravityBody _gravityBody;
    private PlayerMovement _playerMovement;
    private PlayerJump _playerJump;
    private PlayerDash _playerDash;
    private PlayerCamera _playerCamera;
    private bool _isFlying = false;
    private bool _wasFlying = false;
    private bool _hasMovementInput;
    private Camera _mainCamera;
    private float _defaultFOV;
    private bool _isIdleAscending = false;
    private bool _isIdleDescending = false;
    private bool _isFovTransitioning = false;
    private float _targetFOV;
    private float _fovTransitionStartTime;
    private float _fovTransitionDuration = 0.3f; // Adjust duration to match your preference
    private float _lockOrientationTimer = 0f;
    private Vector3 _lastYawDirection = Vector3.forward;
    private bool _justReactivated = false;

    // Movement tracking
    private Vector3 _moveDirection;
    private Vector3 _worldMoveDirection;
    private float _verticalInput;
    private bool _wasMovingLastFrame = false;

    // Speed control
    private float _currentTargetSpeed; // Current interpolated target speed
    private float _rawInputMagnitude; // Raw magnitude of input before smoothing
    private float _smoothedInputMagnitude; // Smoothed magnitude for more natural transitions
    private const float INPUT_SMOOTHING = 0.2f; // Smoothing factor for input magnitude

    // Super speed tracking
    private bool _isSuperSpeedActive = false;
    private float _lastSuperSpeedTapTime = -10f;
    private bool _superSpeedTapPending = false;
    private bool _preventSuperSpeedDoubleTap = false; // Flag to prevent regular input from triggering double tap

    // Vertical movement delay tracking
    private bool _isAscending = false;
    private bool _isDescending = false;
    private float _ascentPressTime = -10f;
    private float _descentPressTime = -10f;

    // Flight activation tracking
    private float _spaceHoldStartTime = 0f;
    private bool _isHoldingSpace = false;

    // Double tap tracking for deactivation
    private float _lastQTapTime = -10f;
    private bool _qTapPending = false;

    // Hover effect tracking
    private Vector3 _hoverStartPosition;
    private float _hoverTime;

    // Gravity compensation
    private Vector3 _originalGravity;
    private bool _wasUsingGravity;

    // Landing detection
    private bool _wasGroundedLastFrame = false;

    // Panning detection
    private bool _isCameraPanning = false;
    private Vector3 _lastCameraForward;
    private Vector3 _lastCameraRight;
    private Vector3 _lastCameraUp;

    // Gravity transition tracking
    private bool _isInGravityTransition = false;
    private float _gravityTransitionTimer = 0f;
    private float _gravityTransitionDuration = 0f;
    private bool _wasCameraPanningBeforeTransition = false;
    private Vector3 _previousGravityDirection = Vector3.zero;
    private Vector3 _upVector = Vector3.up; // Current up vector based on gravity
    private Vector3 _targetGravityDirection = Vector3.zero;
    private bool _isRecoveringFromTransition = false;
    private float _transitionRecoveryTimer = 0f;
    private float _transitionRecoveryDuration = 0.5f; // Shorter recovery period for better responsiveness
    private Quaternion _preTransitionRotation;
    private Vector3 _newGravityUp;
    private bool _hasUpdatedGravityDirection = false;

    private bool _isInSpace = false;
    private Vector3 _spaceUpVector = Vector3.up;
    private Vector3 _lastPlanetUpVector = Vector3.up;
    private bool _wasInSpaceLastFrame = false;
    private Vector3 _driftVelocity = Vector3.zero;
    private bool _isDrifting = false;

    void Start()
    {
        // Get components - keep your existing component references
        _rigidbody = GetComponent<Rigidbody>();
        _gravityBody = GetComponent<GravityBody>();
        _playerMovement = GetComponent<PlayerMovement>();
        _playerJump = GetComponent<PlayerJump>();
        _playerDash = GetComponent<PlayerDash>();
        _mainCamera = Camera.main;

        // Get PlayerCamera component
        _playerCamera = FindObjectOfType<PlayerCamera>();

        if (_cameraTransform == null && _mainCamera != null)
            _cameraTransform = _mainCamera.transform;

        if (_animator == null)
            _animator = GetComponent<Animator>();

        // Initialize _upVector based on gravity direction
        _upVector = _gravityBody != null ? -_gravityBody.GravityDirection.normalized : Vector3.up;
        _spaceUpVector = _upVector; // Initialize space up vector
        _lastPlanetUpVector = _upVector;

        // Store original gravity state
        _wasUsingGravity = _rigidbody.useGravity;
        _originalGravity = Physics.gravity;

        // Subscribe to space transition events
        if (_gravityBody != null)
        {
            _gravityBody.OnSpaceTransition += OnSpaceTransitionEvent;
        }

        // Initialize ground layers if not set
        if (_groundLayers.value == 0)
        {
            _groundLayers = LayerMask.GetMask("Default");
        }

        // Initialize speed
        _currentTargetSpeed = _fastFlightSpeed;

        // Store default camera FOV
        if (_mainCamera != null)
        {
            _defaultFOV = _mainCamera.fieldOfView;
        }

        // Register for gravity transition events
        if (_gravityBody != null)
        {
            _gravityBody.OnGravityTransitionStarted += OnGravityTransitionStarted;
            _gravityBody.OnGravityTransitionCompleted += OnGravityTransitionCompleted;
        }
    }

    private void OnDestroy()
    {
        // Clean up event subscriptions
        if (_gravityBody != null)
        {
            _gravityBody.OnGravityTransitionStarted -= OnGravityTransitionStarted;
            _gravityBody.OnGravityTransitionCompleted -= OnGravityTransitionCompleted;
        }
    }

    // Modify the Update method to ensure FOV transitions continue after flight is disabled
void Update()
{
    // Always update FOV effect, regardless of flight state
    UpdateFOVEffect();

    // Check for camera panning state from PlayerCamera
    if (_playerCamera != null)
    {
        _isCameraPanning = _playerCamera.IsPanningActive();
    }
    else
    {
        // Fallback in case PlayerCamera reference is missing
        _isCameraPanning = Input.GetMouseButton(1);
    }

    // Flight activation logic (hold space)
    if (!_isFlying)
    {
        // Check if player is starting to hold Space
        if (Input.GetKeyDown(_flightActivationKey))
        {
            _isHoldingSpace = true;
            _spaceHoldStartTime = Time.time;
        }

        // Check if player is still holding Space
        if (_isHoldingSpace && Input.GetKey(_flightActivationKey))
        {
            float holdTime = Time.time - _spaceHoldStartTime;

            // If held long enough, activate flight
            if (holdTime >= _flightActivationTime)
            {
                ActivateFlight();
            }
        }

        // Reset if player releases Space before activation time
        if (_isHoldingSpace && Input.GetKeyUp(_flightActivationKey))
        {
            _isHoldingSpace = false;
        }
    }
    else // When flying
    {
        // Reset the prevention flag when the super speed key is released
        if (Input.GetKeyUp(_superSpeedKey))
        {
            _preventSuperSpeedDoubleTap = false;
        }

        // Flight deactivation by double-tapping Q
        if (Input.GetKeyDown(_flightDeactivationKey))
        {
            float timeSinceLastQTap = Time.time - _lastQTapTime;

            // If this is a second tap within the double tap window
            if (_qTapPending && timeSinceLastQTap <= _doubleTapTime)
            {
                DeactivateFlight();
                _qTapPending = false;
            }
            else
            {
                // First tap - start tracking for double tap
                _lastQTapTime = Time.time;
                _qTapPending = true;
            }
        }

        // Super speed activation by double-tapping the super speed key
        if (Input.GetKeyDown(_superSpeedKey) && _isFlying && !_preventSuperSpeedDoubleTap)
        {
            float timeSinceLastSuperSpeedTap = Time.time - _lastSuperSpeedTapTime;

            if (_showDebugLogs)
            {
                Debug.Log($"Super speed key pressed. Time since last tap: {timeSinceLastSuperSpeedTap}");
            }

            // Check if this is a second tap within the double tap window
            if (_superSpeedTapPending && timeSinceLastSuperSpeedTap <= _superSpeedDoubleTapTime)
            {
                if (_showDebugLogs)
                {
                    Debug.Log("Double-tap detected! Toggling super speed.");
                }

                ToggleSuperSpeed();
                _superSpeedTapPending = false;
                _preventSuperSpeedDoubleTap = true;  // Prevent immediate re-triggering
            }
            else
            {
                // First tap - start tracking for double tap
                if (_showDebugLogs)
                {
                    Debug.Log("First tap detected, waiting for second tap...");
                }

                _lastSuperSpeedTapTime = Time.time;
                _superSpeedTapPending = true;
            }
        }

        // Reset pending tap if too much time has passed
        if (_superSpeedTapPending && (Time.time - _lastSuperSpeedTapTime) > _superSpeedDoubleTapTime)
        {
            _superSpeedTapPending = false;

            if (_showDebugLogs)
            {
                Debug.Log("Double-tap window expired.");
            }
        }

        // Get input and calculate move direction
        float h = Input.GetAxis("Horizontal");  // Using GetAxis instead of GetAxisRaw for controller support
        float v = Input.GetAxis("Vertical");    // Using GetAxis to get analog values

        // ✅ WoW-style while flying: both mouse buttons ⇒ force forward along camera
        bool bothMouseButtons = (Input.GetMouseButton(0) && Input.GetMouseButton(1));
        if (bothMouseButtons)
        {
            h = 0f;
            v = 1f; // forward
        }

        // Calculate raw input magnitude for speed control
        _rawInputMagnitude = new Vector2(h, v).magnitude;

        // Smooth input magnitude for natural transitions
        _smoothedInputMagnitude = Mathf.Lerp(_smoothedInputMagnitude, _rawInputMagnitude, INPUT_SMOOTHING);

        // Check if super speed should be automatically disabled due to low input
        if (_isSuperSpeedActive && _smoothedInputMagnitude < _minInputForSuperSpeed)
        {
            if (_showDebugLogs)
            {
                Debug.Log("Input too low, disabling super speed");
            }

            DeactivateSuperSpeed();
        }

        // Update target speed based on input magnitude and super speed state
        UpdateFlightSpeed();

        // Process vertical movement with delay
        HandleVerticalMovementWithDelay();

        // Track if there's any movement input (include both-buttons autorun as input)
        _hasMovementInput = (_rawInputMagnitude > 0.1f || Mathf.Abs(_verticalInput) > 0.1f || bothMouseButtons);

        CalculateMoveDirection(h, v);

        // Update idle ascending/descending states
        UpdateIdleVerticalStates();

        // Update animator if available
        UpdateAnimator();

        // Check for landing (direct ground check)
        if (CheckForGrounding())
        {
            DeactivateFlight();
        }
    }
}

    void FixedUpdate()
    {
        if (_isFlying)
        {
            // Apply flying movement
            ApplyFlightMovement();

            // Apply air damping - less than ground friction
            ApplyAirDamping();

            // Handle gravity interactions with improved transition support
            UpdateFlightGravityInteraction();

            // Make sure rigidbody's useGravity is disabled
            if (_rigidbody.useGravity)
            {
                _rigidbody.useGravity = false;
            }
        }
        else if (_wasFlying)
        {
            // Re-enable gravity when stopping flight
            if (_gravityBody != null)
            {
                _gravityBody.enabled = true;
            }

            // Restore original rigidbody gravity setting
            _rigidbody.useGravity = _wasUsingGravity;
            _wasFlying = false;
        }

        if (_isDrifting && _isInSpace)
        {
            UpdateDriftingInSpace();
        }
    }

    public void OnCameraPanning(Vector3 cameraForward)
    {
        // Only update stored values if currently panning
        if (_isCameraPanning)
        {
            _lastCameraForward = cameraForward;
            if (_playerCamera != null)
            {
                Vector3 forward, right, up;
                _playerCamera.GetCameraData(out forward, out right, out up);
                _lastCameraRight = right;
                _lastCameraUp = up;
            }
            else if (_cameraTransform != null)
            {
                _lastCameraRight = _cameraTransform.right;
                _lastCameraUp = _cameraTransform.up;
            }
        }
    }

    // Update flight speed based on input magnitude and super speed state
    private void UpdateFlightSpeed()
    {
        // If super speed is active, use super speed value
        if (_isSuperSpeedActive)
        {
            _currentTargetSpeed = _superSpeed;
            return;
        }

        // Get input magnitude (either raw or smoothed)
        float inputMagnitude = _useSmoothedInput ? _smoothedInputMagnitude : _rawInputMagnitude;

        // Determine target speed based on input magnitude
        float targetSpeed;
        if (inputMagnitude < _speedThreshold)
        {
            // For subtle input, use slow speed
            targetSpeed = _slowFlightSpeed;
        }
        else
        {
            // For full input, use fast speed
            targetSpeed = _fastFlightSpeed;
        }

        // Smoothly transition to target speed
        _currentTargetSpeed = Mathf.Lerp(_currentTargetSpeed, targetSpeed, Time.deltaTime * _speedTransitionRate);
    }

    // Toggle super speed on/off
    private void ToggleSuperSpeed()
    {
        if (_showDebugLogs)
        {
            Debug.Log("ToggleSuperSpeed called. Current state: " + _isSuperSpeedActive);
        }

        if (_isSuperSpeedActive)
        {
            DeactivateSuperSpeed();
        }
        else
        {
            ActivateSuperSpeed();
        }

        if (_showDebugLogs)
        {
            Debug.Log("After toggle, super speed state: " + _isSuperSpeedActive);
        }
    }

    private void ActivateSuperSpeed()
    {
        if (_showDebugLogs) Debug.Log("ActivateSuperSpeed called");

        // Check if speed is sufficient to activate super speed
        if (_currentTargetSpeed < _minSpeedForSuperSpeedActivation)
        {
            if (_showDebugLogs) Debug.Log($"Super speed activation blocked — current speed {_currentTargetSpeed:F2} is below required {_minSpeedForSuperSpeedActivation}");
            return;
        }

        _isSuperSpeedActive = true;
        if (_animator != null)
        {
            if (_showDebugLogs) Debug.Log("Setting animator parameter isSuperSpeed to true");
            _animator.SetBool("isSuperSpeed", true);
        }
    }

    // Deactivate super speed mode
    private void DeactivateSuperSpeed()
    {
        if (_showDebugLogs)
        {
            Debug.Log("DeactivateSuperSpeed called");
        }

        _isSuperSpeedActive = false;
        if (_animator != null)
        {
            if (_showDebugLogs)
            {
                Debug.Log("Setting animator parameter isSuperSpeed to false");
            }
            _animator.SetBool("isSuperSpeed", false);
        }
    }

    // Modified UpdateFOVEffect method to handle FOV transitioning in all states
    private void UpdateFOVEffect()
    {
        if (!_useScreenEffects || _mainCamera == null) return;

        // Handle FOV transitions - always process transitions, regardless of flight state
        if (_isFovTransitioning)
        {
            // Calculate how far we are through the transition
            float elapsed = Time.time - _fovTransitionStartTime;
            float t = Mathf.Clamp01(elapsed / _fovTransitionDuration);

            // Apply smooth easing
            float smoothT = Mathf.SmoothStep(0, 1, t);

            // Interpolate between current FOV and target FOV
            _mainCamera.fieldOfView = Mathf.Lerp(_mainCamera.fieldOfView, _targetFOV, smoothT);

            // Check if transition is complete
            if (t >= 1.0f)
            {
                _isFovTransitioning = false;
                // Ensure we reach exactly the target FOV
                _mainCamera.fieldOfView = _targetFOV;
            }
        }
        // Only update FOV based on flight state if not currently in a transition
        else if (_isFlying)
        {
            // Regular FOV update during flight
            // Target FOV based on super speed state
            float targetFOV = _isSuperSpeedActive ? _defaultFOV + _fovIncrease : _defaultFOV;

            // Smoothly transition FOV
            _mainCamera.fieldOfView = Mathf.Lerp(_mainCamera.fieldOfView, targetFOV, Time.deltaTime * _fovChangeSpeed);
        }
    }

    private void StartFOVTransition(float targetFOV)
    {
        if (!_useScreenEffects || _mainCamera == null) return;

        _targetFOV = targetFOV;
        _fovTransitionStartTime = Time.time;
        _isFovTransitioning = true;
    }

    // Custom ground check that works when PlayerMovement is disabled
    private bool CheckForGrounding()
    {
        // Get the correct "down" direction based on gravity
        Vector3 downDirection = _gravityBody != null ? _gravityBody.GravityDirection.normalized : Vector3.down;

        // Position to start the raycast (slightly above the collider base)
        Vector3 rayStart = transform.position;

        // Check for ground contact
        bool isGrounded = Physics.Raycast(rayStart, downDirection, _groundCheckDistance, _groundLayers);

        // Additional check: only consider landing if we were moving downward
        if (isGrounded && !_wasGroundedLastFrame)
        {
            // Calculate the downward component of our velocity
            float downwardVelocity = Vector3.Dot(_rigidbody.velocity, downDirection);

            // Only consider it a landing if we're moving downward with sufficient velocity
            isGrounded = downwardVelocity >= _landingVelocityThreshold;
        }

        // Store state for next frame
        _wasGroundedLastFrame = isGrounded;

        return isGrounded;
    }

    private bool IsPitchWithinAllowedRange()
    {
        if (_playerCamera == null) return true; // Fallback: assume valid if no camera

        float pitch = _playerCamera.GetPitch();
        return pitch >= minPitchAllowed && pitch <= maxPitchAllowed;
    }

    private void UpdateIdleVerticalStates()
    {
        // Only consider idle state when there's no horizontal movement
        bool isIdle = _rawInputMagnitude < 0.1f;

        if (isIdle)
        {
            // Update idle ascending/descending based on vertical input
            _isIdleAscending = _verticalInput > 0.1f;
            _isIdleDescending = _verticalInput < -0.1f;
        }
        else
        {
            // Reset idle vertical states when moving horizontally
            _isIdleAscending = false;
            _isIdleDescending = false;
        }
    }

    // Handle vertical movement with delay for Space/Q keys
    private void HandleVerticalMovementWithDelay()
    {
        // Reset vertical input first
        _verticalInput = 0;

        // Space key for ascent (with delay)
        if (Input.GetKeyDown(KeyCode.Space))
        {
            _ascentPressTime = Time.time;
        }

        if (Input.GetKey(KeyCode.Space))
        {
            // Only set ascending flag if held long enough
            if (!_isAscending && (Time.time - _ascentPressTime) >= _ascentDelay)
            {
                _isAscending = true;
            }

            // Apply input once the delay has passed
            if (_isAscending)
            {
                _verticalInput = 1;
            }
        }
        else
        {
            // Reset ascending state when key is released
            _isAscending = false;
        }

        // Shift key for descent (with delay)
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            _descentPressTime = Time.time;
        }

        if (Input.GetKey(KeyCode.LeftShift))
        {
            // Only set descending flag if held long enough
            if (!_isDescending && (Time.time - _descentPressTime) >= _descentDelay)
            {
                _isDescending = true;
            }

            // Apply input once the delay has passed
            if (_isDescending)
            {
                _verticalInput = -1;
            }
        }
        else
        {
            // Reset descending state when key is released
            _isDescending = false;
        }
    }

    private void CalculateMoveDirection(float horizontal, float vertical)
    {
        // Get raw input direction
        _moveDirection = new Vector3(horizontal, 0f, vertical).normalized;

        // If we have camera and input, calculate direction relative to camera
        if (_cameraTransform != null && _moveDirection.magnitude > 0.1f)
        {
            // Get camera vectors based on different states
            Vector3 cameraForward;
            Vector3 cameraRight;

            // Handle different camera states with priority:
            // 1. During gravity transition with preserved camera panning
            // 2. During active camera panning
            // 3. Normal camera operation
            if (_isInGravityTransition && _wasCameraPanningBeforeTransition && _lastCameraForward != Vector3.zero)
            {
                // Use stored camera vectors during gravity transition
                cameraForward = _lastCameraForward;
                cameraRight = _lastCameraRight;
            }
            // If we're panning and have PlayerCamera, use its vectors directly
            else if (_isCameraPanning && _playerCamera != null)
            {
                cameraForward = _playerCamera.GetCameraForward();
                cameraRight = _playerCamera.GetCameraRight();
            }
            // Otherwise use the camera transform's vectors
            else
            {
                cameraForward = _cameraTransform.forward;
                cameraRight = _cameraTransform.right;
            }

            // In space, allow full 3D movement based on camera orientation with no projection
            if (_isInSpace && _isFlying)
            {
                // When in space, directly use camera forward/right with no projection
                _worldMoveDirection = (cameraForward * _moveDirection.z + cameraRight * _moveDirection.x).normalized;

                // Update the space up vector based on camera's current up when panning
                if (_isCameraPanning)
                {
                    _spaceUpVector = _playerCamera.GetCameraUp();

                    // Also update the gravity body's space direction
                    if (_gravityBody != null)
                    {
                        _gravityBody.SetSpaceGravityDirection(-_spaceUpVector);
                    }
                }
            }
            else
            {
                // When not in space, follow the regular behavior
                // Check if the camera is looking too far down
                // Measure how much the camera is looking down by getting the dot product
                // between camera forward and the up vector
                Vector3 upVector = _gravityBody != null ? -_gravityBody.GravityDirection.normalized : Vector3.up;
                float downwardAmount = -Vector3.Dot(cameraForward, upVector);

                // Adjust camera forward vector to minimize downward movement when camera angle is subtle
                if (downwardAmount > 0 && downwardAmount < _downwardAngleThreshold)
                {
                    // Project the camera forward onto the horizontal plane to remove slight downward component
                    Vector3 horizontalCameraForward = Vector3.ProjectOnPlane(cameraForward, upVector).normalized;

                    // Smoothly transition between projected forward and actual forward based on downward angle
                    float blendFactor = downwardAmount / _downwardAngleThreshold;
                    cameraForward = Vector3.Lerp(horizontalCameraForward, cameraForward, blendFactor);
                }

                // Directly use camera direction for movement
                _worldMoveDirection = (cameraForward * _moveDirection.z + cameraRight * _moveDirection.x).normalized;
            }
        }
        else
        {
            _worldMoveDirection = Vector3.zero;
        }
    }

// REPLACE the whole method
private void ApplyFlightMovement()
{
    Vector3 currentVelocity = _rigidbody.velocity;
    Vector3 targetVelocity = Vector3.zero;

    float speedFactor = 1.0f;
    if (_isInGravityTransition)
        speedFactor = _gravityTransitionSpeedFactor;
    else if (_isRecoveringFromTransition)
        speedFactor = Mathf.Lerp(_gravityTransitionSpeedFactor, 1.0f, _transitionRecoveryTimer / _transitionRecoveryDuration);

    // Is the vertical thruster engaged?
    bool verticalActive = Mathf.Abs(_verticalInput) > 0.1f;

    // Choose the "up" we use to separate horizontal vs vertical
    Vector3 effectiveUp = _isInSpace
        ? _spaceUpVector
        : (_gravityBody != null ? -_gravityBody.GravityDirection.normalized : Vector3.up);

    // 1) Horizontal (camera-driven) movement
    if (_worldMoveDirection.magnitude > 0.1f)
    {
        Vector3 horiz = _worldMoveDirection * _currentTargetSpeed * speedFactor;

        // ✨ Critical: if Space/Shift is held, flatten horizontal so it can't fight vertical thrust
        if (verticalActive)
            horiz = Vector3.ProjectOnPlane(horiz, effectiveUp);

        targetVelocity = horiz;
    }

    // 2) Vertical thrust (Space/Shift) — DO NOT gate by camera pitch
    if (verticalActive)
    {
        float verticalSpeed = (_verticalInput > 0 ? _ascentSpeed : _descentSpeed) * speedFactor;

        // In space while panning, respect camera-up; otherwise use gravity-up
        Vector3 upForThrust = (_isInSpace && _isFlying && _isCameraPanning && _playerCamera != null)
            ? _playerCamera.GetCameraUp()
            : effectiveUp;

        targetVelocity += upForThrust * _verticalInput * verticalSpeed;
    }

    // 3) Apply velocity (same smoothing you had)
    if (_hasMovementInput)
    {
        if (!_wasMovingLastFrame)
            _hoverStartPosition = _rigidbody.position;

        float lerpFactor = _isSuperSpeedActive ? Time.fixedDeltaTime * 20f : Time.fixedDeltaTime * 10f;
        if (_isInGravityTransition || _isRecoveringFromTransition) lerpFactor *= 1.5f;

        if ((targetVelocity - currentVelocity).sqrMagnitude < 0.05f)
            _rigidbody.velocity = targetVelocity;
        else
            _rigidbody.velocity = Vector3.Lerp(currentVelocity, targetVelocity, lerpFactor);

        _wasMovingLastFrame = true;
        _driftVelocity = _rigidbody.velocity;
    }
    else
    {
        // Idle/hover path unchanged
        if (_wasMovingLastFrame)
        {
            _hoverStartPosition = _rigidbody.position;
            _hoverTime = 0f;
            if (_isSuperSpeedActive) DeactivateSuperSpeed();
            _driftVelocity = Vector3.zero;
        }

        _rigidbody.velocity = Vector3.zero;

        if (!_isInGravityTransition)
        {
            _hoverTime += Time.fixedDeltaTime;
            float hoverOffset = Mathf.Sin(_hoverTime * _hoverFrequency * Mathf.PI * 2f) * _hoverAmplitude;
            Vector3 upDir = _isInSpace ? _spaceUpVector : (_gravityBody != null ? -_gravityBody.GravityDirection.normalized : transform.up);
            _rigidbody.MovePosition(_hoverStartPosition + upDir * hoverOffset);
        }

        _wasMovingLastFrame = false;
    }

    RotateTowardsFlyingDirection();
}

    private void RotateTowardsFlyingDirection()
    {
        if (_lockOrientationTimer > 0)
        {
            _lockOrientationTimer -= Time.deltaTime;
            return;
        }

        if (_justReactivated)
        {
            // Skip this frame to avoid early snapping
            _justReactivated = false;
            return;
        }

        Vector3 stableUp = _isInSpace ? _spaceUpVector : (_gravityBody != null ? -_gravityBody.GravityDirection.normalized : Vector3.up);
        Quaternion targetRotation;

        if (_hasMovementInput && _worldMoveDirection.sqrMagnitude > 0.01f)
        {
            Vector3 moveDir = _worldMoveDirection;

            if (Mathf.Abs(_verticalInput) > 0.1f && _playerCamera != null)
            {
                float cameraPitch = _playerCamera.GetPitch();
                if (cameraPitch >= minPitchAllowed && cameraPitch <= maxPitchAllowed)
                {
                    float pitchAmount = -_verticalInput * _pitchAngle;
                    Vector3 rightAxis = Vector3.Cross(stableUp, moveDir).normalized;
                    Quaternion pitchRotation = Quaternion.AngleAxis(pitchAmount, rightAxis);
                    moveDir = pitchRotation * moveDir;
                }
            }

            _lastYawDirection = moveDir;

            if (_isInSpace)
            {
                Vector3 upDir = (_playerCamera != null) ? _playerCamera.GetCameraUp() : Vector3.up;
                targetRotation = Quaternion.LookRotation(moveDir, upDir);
            }
            else
            {
                targetRotation = Quaternion.LookRotation(moveDir, stableUp);
            }
        }
        else
        {
            Vector3 lookDir;
            Vector3 upDir;

            if (_isInSpace)
            {
                lookDir = (_playerCamera != null) ? _playerCamera.GetCameraForward() : (_cameraTransform != null ? _cameraTransform.forward : transform.forward);
                upDir = (_playerCamera != null) ? _playerCamera.GetCameraUp() : (_cameraTransform != null ? _cameraTransform.up : Vector3.up);
                targetRotation = Quaternion.LookRotation(lookDir, upDir);
            }
            else
            {
                Vector3 fallbackForward = (_playerCamera != null) ? _playerCamera.GetCameraForward() : (_cameraTransform != null ? _cameraTransform.forward : transform.forward);
                Vector3 alignedForward = Vector3.ProjectOnPlane(fallbackForward, stableUp).normalized;

                if (alignedForward.sqrMagnitude < 0.01f)
                {
                    alignedForward = transform.forward;
                }

                targetRotation = Quaternion.LookRotation(alignedForward, stableUp);
            }
        }

        float rotationFactor = _isSuperSpeedActive ? _rotationSpeed * 1.5f : _rotationSpeed;
        if (_isRecoveringFromTransition) rotationFactor *= 2f;

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationFactor);
    }

    private void UpdateAnimator()
    {
        if (_animator == null) return;

        // Update flying state
        _animator.SetBool("isFlying", _isFlying);

        // Update super speed state
        _animator.SetBool("isSuperSpeed", _isSuperSpeedActive);

        // Update movement speed - normalized between slow and fast flight, or beyond for super speed
        float normalizedSpeed;
        if (_isSuperSpeedActive)
        {
            normalizedSpeed = 2.0f; // Value greater than 1 to indicate super speed
        }
        else
        {
            normalizedSpeed = (_currentTargetSpeed - _slowFlightSpeed) / (_fastFlightSpeed - _slowFlightSpeed);
        }
        _animator.SetFloat("flySpeed", normalizedSpeed);

        // Set whether player has movement input
        _animator.SetBool("hasFlightInput", _hasMovementInput);

        // Set idle ascending/descending animation parameters
        _animator.SetBool("isIdleAscending", _isIdleAscending);
        _animator.SetBool("isIdleDescending", _isIdleDescending);
    }

    // Activate flight mode
    private void ActivateFlight()
    {
        if (_isFlying) return;

        _isFlying = true;
        _wasFlying = true;

        // Store original gravity state
        _wasUsingGravity = _rigidbody.useGravity;

        // Disable Unity's built-in gravity
        _rigidbody.useGravity = false;

        // Disable ground movement script if active
        if (_playerMovement != null)
        {
            _playerMovement.enabled = false;
        }

        // Disable jump script to prevent space from triggering jumps
        if (_playerJump != null)
        {
            _playerJump.enabled = false;
        }

        // Disable dash script to prevent Q from triggering dashes
        if (_playerDash != null)
        {
            _playerDash.enabled = false;
        }

        // Reset vertical velocity when starting flight
        Vector3 velocity = _rigidbody.velocity;
        Vector3 gravityDir = _gravityBody != null ? _gravityBody.GravityDirection.normalized : Vector3.down;
        Vector3 verticalVelocity = Vector3.Project(velocity, gravityDir);
        _rigidbody.velocity = velocity - verticalVelocity;

        // Reset input magnitudes
        _rawInputMagnitude = 0f;
        _smoothedInputMagnitude = 0f;

        // Update animator
        if (_animator != null)
        {
            _animator.SetBool("isFlying", true);
        }

        // Initialize hover effect
        _hoverStartPosition = transform.position;
        _hoverTime = 0f;

        // Clear any pending tap state
        _qTapPending = false;
        _lastQTapTime = -10f;
        _superSpeedTapPending = false;
        _lastSuperSpeedTapTime = -10f;

        // Reset vertical movement state
        _isAscending = false;
        _isDescending = false;

        // Reset ground detection
        _wasGroundedLastFrame = false;

        // Reset super speed
        _isSuperSpeedActive = false;
        if (_animator != null)
        {
            _animator.SetBool("isSuperSpeed", false);
        }
    }

    // Deactivate flight mode
    private void DeactivateFlight()
    {
        if (!_isFlying) return;

        _isFlying = false;

        // Special handling for deactivating flight in space
        if (_isInSpace)
        {
            _isDrifting = true;

            // Maintain current velocity for drifting
            _rigidbody.velocity = _driftVelocity;

            // Do not re-enable gravity in space
            // Do not re-enable ground movement scripts

            // Make the camera keep looking at the player
            if (_playerCamera != null)
            {
                _playerCamera.SetSpaceDriftMode(true);
            }

            Debug.Log("Flight deactivated in space - drifting!");
        }
        else
        {
            // Normal planet deactivation
            _isDrifting = false;

            // Restore original rigidbody gravity setting
            _rigidbody.useGravity = _wasUsingGravity;

            // Re-enable ground movement script
            if (_playerMovement != null)
            {
                _playerMovement.enabled = true;
            }

            // Re-enable jump script
            if (_playerJump != null)
            {
                _playerJump.enabled = true;
            }

            // Re-enable dash script
            if (_playerDash != null)
            {
                _playerDash.enabled = true;
            }
        }

        // Update animator
        if (_animator != null)
        {
            _animator.SetBool("isFlying", false);
            _animator.SetBool("isSuperSpeed", false);

            // Add drifting animation parameter if needed
            if (_isInSpace)
            {
                _animator.SetBool("isDrifting", true);
            }
            else
            {
                _animator.SetBool("isDrifting", false);
            }
        }
    }

    private void UpdateFlightGravityInteraction()
{
    if (_gravityBody != null)
    {
        if (_isInGravityTransition)
        {
            if (!_gravityBody.enabled)
            {
                _gravityBody.enabled = true;
                if (_enableTransitionDebugLogs) Debug.Log("Flight: Re-enabled GravityBody for transition detection");
            }
        }
        else if (_isRecoveringFromTransition)
        {
            if (!_gravityBody.enabled)
            {
                _gravityBody.enabled = true;
                if (_enableTransitionDebugLogs) Debug.Log("Flight: GravityBody enabled during recovery");
            }
        }
        else
        {
            if (_gravityBody.enabled)
            {
                _gravityBody.enabled = false;
                if (_enableTransitionDebugLogs) Debug.Log("Flight: GravityBody disabled after recovery");
            }
        }
    }

    // While idle-hovering (no input, no transition), don't push with anti-gravity.
    if (!_hasMovementInput && !_isInGravityTransition && !_isRecoveringFromTransition)
        return;

    // Apply anti-gravity only when moving or during/after transitions
    Vector3 gravityDirection =
        (_gravityBody != null && _gravityBody.enabled)
            ? _gravityBody.GravityDirection.normalized
            : Physics.gravity.normalized;

    _rigidbody.AddForce(-gravityDirection * Physics.gravity.magnitude, ForceMode.Acceleration);
}

    private void OnSpaceTransitionEvent(bool enteringSpace, Vector3 gravityDirection)
    {
        _isInSpace = enteringSpace;

        if (enteringSpace)
        {
            // Store the last planet up vector when entering space
            _lastPlanetUpVector = -gravityDirection;
            _spaceUpVector = _lastPlanetUpVector;

            // When entering space, lock the orientation briefly to prevent issues
            _lockOrientationTimer = 0.5f; // Lock orientation changes for 0.5 seconds

            Debug.Log("Entered space! Maintaining orientation: " + _spaceUpVector);
        }
        else
        {
            // Update the up vector when entering a planet
            _upVector = -gravityDirection;
            Debug.Log("Exited space! New gravity direction: " + gravityDirection);
        }

        // Update camera up vector immediately but prevent any camera movement for a moment
        if (_playerCamera != null)
        {
            _playerCamera.UpdateGravityUpVector(_isInSpace ? _spaceUpVector : _upVector);
        }
    }

    private void UpdateDriftingInSpace()
    {
        if (_isDrifting && _isInSpace)
        {
            // Maintain the drifting velocity
            _rigidbody.velocity = _driftVelocity;

            // Rotate character into a funny rotating state
            transform.Rotate(0.5f, 1.0f, 0.3f, Space.Self);
        }
    }

    private void ApplyAirDamping()
    {
        // Only apply air damping when not actively moving
        if (_worldMoveDirection.magnitude < 0.1f)
        {
            // Apply mild damping to slow down gradually
            _rigidbody.velocity = Vector3.Lerp(
                _rigidbody.velocity,
                Vector3.zero,
                Time.fixedDeltaTime * _airDampingFactor
            );
        }
    }

    // Return flight state for other scripts
    public bool IsFlying()
    {
        return _isFlying;
    }

    // Return super speed state for other scripts
    public bool IsSuperSpeed()
    {
        return _isSuperSpeedActive;
    }

    //Gravity transition event handlers
    public void OnGravityTransitionStarted(Vector3 oldDirection, Vector3 newDirection, float duration)
    {
        // Store the gravity transition state
        _isInGravityTransition = true;
        _gravityTransitionTimer = 0f;
        _gravityTransitionDuration = duration;
        _previousGravityDirection = oldDirection;
        _targetGravityDirection = newDirection;
        _preTransitionRotation = transform.rotation;
        _newGravityUp = -newDirection.normalized;
        _hasUpdatedGravityDirection = false;

        // Immediately update upVector for better responsiveness
        _upVector = -oldDirection.normalized;

        if (_enableTransitionDebugLogs)
        {
            Debug.Log($"Flight: Gravity transition started. Old: {oldDirection}, New: {newDirection}, Duration: {duration}");
        }

        // Store camera panning state to restore it after transition
        if (_preserveCameraPanningState)
        {
            _wasCameraPanningBeforeTransition = _isCameraPanning;

            // Store current camera orientation
            if (_playerCamera != null)
            {
                Vector3 forward, right, up;
                _playerCamera.GetCameraData(out forward, out right, out up);
                _lastCameraForward = forward;
                _lastCameraRight = right;
                _lastCameraUp = up;
            }
            else if (_cameraTransform != null)
            {
                _lastCameraForward = _cameraTransform.forward;
                _lastCameraRight = _cameraTransform.right;
                _lastCameraUp = _cameraTransform.up;
            }
        }
    }

    public void OnGravityTransitionCompleted(Vector3 oldDirection, Vector3 newDirection, float duration)
    {
        _isInGravityTransition = false;
        _isRecoveringFromTransition = true;
        _transitionRecoveryTimer = 0f;

        _upVector = _newGravityUp;

        if (_enableTransitionDebugLogs)
        {
            Debug.Log("Flight: Gravity transition completed, entering recovery phase");
        }

        if (_preserveCameraPanningState && _wasCameraPanningBeforeTransition)
        {
            if (_playerCamera != null)
            {
                _playerCamera.OnPanningRestored(_lastCameraForward);
            }
        }

        // Start coroutine to auto-disable GravityBody after recovery
        StartCoroutine(DisableGravityBodyAfterRecovery());
    }

    // Public method to force-enable or disable flight
    public void SetFlightState(bool state)
    {
        if (state && !_isFlying)
        {
            ActivateFlight();
        }
        else if (!state && _isFlying)
        {
            DeactivateFlight();
        }
    }

    // Public method to force-enable or disable super speed
    public void SetSuperSpeedState(bool state)
    {
        if (state && !_isSuperSpeedActive && _isFlying)
        {
            ActivateSuperSpeed();
        }
        else if (!state && _isSuperSpeedActive)
        {
            DeactivateSuperSpeed();
        }
    }

    public void UpdateGravityDirection()
    {
        if (_gravityBody != null && _gravityBody.enabled)
        {
            _upVector = -_gravityBody.GravityDirection.normalized;
            if (_enableTransitionDebugLogs)
            {
                Debug.Log($"Flight: Explicitly updated gravity direction to {_upVector}");
            }
        }
    }

    public void ReactivateFlightFromDrift()
    {
        if (_isDrifting && _isInSpace)
        {
            _isDrifting = false;

            // Exit drift mode in camera
            if (_playerCamera != null)
            {
                _playerCamera.SetSpaceDriftMode(false);
            }

            // Disable drifting animation
            if (_animator != null)
            {
                _animator.SetBool("isDrifting", false);
            }

            // Align character rotation to camera forward/up to reset orientation
            if (_playerCamera != null)
            {
                Vector3 forward = _playerCamera.GetCameraForward();
                Vector3 up = _playerCamera.GetCameraUp();

                // Normalize and project forward onto up plane to ensure no weird tilts
                forward = Vector3.ProjectOnPlane(forward, up).normalized;
                if (forward.sqrMagnitude < 0.01f) forward = transform.forward; // fallback if projection fails

                Quaternion newRotation = Quaternion.LookRotation(forward, up);
                transform.rotation = newRotation;

                // Reset yaw tracking
                _lastYawDirection = forward;
            }

            // Stop any drifting spin
            if (_rigidbody != null)
            {
                _rigidbody.angularVelocity = Vector3.zero;
            }

            // Mark that we just reactivated — used in rotation logic
            _justReactivated = true;

            // Now reactivate flight normally
            ActivateFlight();
        }
    }

    public void HandleGravityTransitionStarted()
    {
        // Default values to use when called via SendMessage
        if (_gravityBody != null)
        {
            // Fix: Adding definition for _lastGravityDirection variable
            Vector3 _lastGravityDirection = Vector3.zero;

            // Try to get the last gravity direction from GravityBody if possible
            if (_previousGravityDirection != Vector3.zero)
            {
                _lastGravityDirection = _previousGravityDirection;
            }
            else
            {
                // Fallback to using the current up vector
                _lastGravityDirection = -_gravityBody.GravityDirection;
            }

            Vector3 newDirection = _gravityBody.GravityDirection;
            Vector3 oldDirection = _lastGravityDirection;
            float defaultDuration = 0.5f;

            // Call the proper handler with these values
            OnGravityTransitionStarted(oldDirection, newDirection, defaultDuration);
        }
    }

    public void OnGravityTransitionCompleted()
    {
        // This overload handles SendMessage calls with no parameters
        // It will use the cached gravity directions from the previous transition
        if (_gravityBody != null)
        {
            Vector3 newDirection = _gravityBody.GravityDirection;
            Vector3 oldDirection = _previousGravityDirection != Vector3.zero ? _previousGravityDirection : newDirection;
            float defaultDuration = 0.5f;

            // Call the full parameter version with our best guess at the parameters
            OnGravityTransitionCompleted(oldDirection, newDirection, defaultDuration);
        }
        else
        {
            // Fallback if gravityBody is null
            OnGravityTransitionCompleted(Vector3.down, Vector3.down, 0.5f);
        }
    }

    // Debug visualization
    void OnDrawGizmosSelected()
    {
        // Draw ground check ray
        Vector3 downDirection = _gravityBody != null ? _gravityBody.GravityDirection.normalized : Vector3.down;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + downDirection * _groundCheckDistance);

        // Draw downward angle threshold visualization
        if (_cameraTransform != null && Application.isEditor)
        {
            // Draw a ray showing the threshold angle
            Vector3 upVector = _gravityBody != null ? -_gravityBody.GravityDirection.normalized : Vector3.up;

            // Calculate the threshold angle in degrees
            float thresholdAngleDegrees = Mathf.Asin(_downwardAngleThreshold) * Mathf.Rad2Deg;

            // Create a direction that's at the threshold angle from horizontal
            Vector3 thresholdDir = Quaternion.AngleAxis(-thresholdAngleDegrees, Vector3.right) * Vector3.forward;

            // Transform to camera space
            thresholdDir = _cameraTransform.TransformDirection(thresholdDir);

            // Draw the threshold line
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(_cameraTransform.position, _cameraTransform.position + thresholdDir * 5f);

            // Draw current camera forward
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(_cameraTransform.position, _cameraTransform.position + _cameraTransform.forward * 5f);
        }
    }

    private IEnumerator DisableGravityBodyAfterRecovery()
    {
        // Wait until recovery is finished
        yield return new WaitUntil(() => !_isRecoveringFromTransition);

        if (_gravityBody != null && _gravityBody.enabled)
        {
            _gravityBody.enabled = false;
            if (_enableTransitionDebugLogs)
            {
                Debug.Log("Flight: GravityBody disabled after recovery (via coroutine)");
            }
        }
    }
}