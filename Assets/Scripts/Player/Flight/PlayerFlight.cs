using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerFlight : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _cameraTransform;

    [Header("Movement")]
    [SerializeField] private float _slowSpeed = 5f;                 // cruise speed
    [SerializeField] private float _fastSpeed = 20f;                // top speed
    [SerializeField] private float _timeToFast = 0.25f;             // time holding movement before fast is allowed
    [SerializeField, Range(0f, 1f)] private float _fastInputThreshold = 0.75f; // stick magnitude to allow fast

    [SerializeField] private float _accelRateSlow = 30f;            // accel toward slow (units/s^2)
    [SerializeField] private float _accelRateFast = 25f;            // accel toward fast (units/s^2)
    [SerializeField] private float _decelRate = 35f;                // decel when releasing input (units/s^2)

    [SerializeField] private float _rotationSpeed = 15f;            // smooth YAW toward WASD
    [SerializeField] private float _pitchReturnSpeed = 12f;         // how fast pitch returns to neutral when not moving

    // ── Super Speed
    [Header("Super Speed")]
    [SerializeField] private float _superSpeed = 40f;               // speed when super is active
    [SerializeField] private float _superSpeedFovIncrease = 15f;    // extra FOV while super is active
    [SerializeField] private KeyCode _superSpeedKey = KeyCode.Space;// double-tap this key to toggle super
    [SerializeField] private float _superSpeedDoubleTapTime = 0.4f; // max gap between taps
    [SerializeField, Range(0.9f, 1f)] private float _superHoldThreshold = 0.98f; // how hard to hold to MAINTAIN super
    [SerializeField, Range(0.5f, 1f)] private float _superActivateMinFactor = 0.9f; // must be at least this * fastSpeed to trigger

    // ── Idle Ascend / Descend (hold while NO WASD)
    [Header("Idle Vertical")]
    [SerializeField] private KeyCode _idleAscendKey = KeyCode.Space;
    [SerializeField] private KeyCode _idleDescendKey = KeyCode.LeftShift;
    [SerializeField] private float _idleHoldDelay = 0.25f;          // hold time before idle up/down begins
    [SerializeField] private float _idleVerticalSpeed = 4f;         // m/s up/down while idling
    [SerializeField] private float _idleVerticalAccel = 20f;        // accel for smoothing vertical idle speed

    [Header("In-Flight Pitch Assist")]
    [SerializeField, Range(0f, 25f)] private float _pitchAssistDegrees = 12f;     // max tilt while Space/Shift held
    [SerializeField, Range(4f, 20f)] private float _pitchAssistLerpSpeed = 10f;   // assist fade in/out speed
    [SerializeField, Min(0f)] private float _pitchAssistHoldDelay = 0.25f;

    [SerializeField, Range(1f, 89f)] private float _pitchAssistCamLimit = 55f;

    [Header("Landing")]
    [SerializeField] private bool _autoExitOnGround = true;         // auto-exit flight when touching ground
    [SerializeField] private float _groundCheckRadius = 0.25f;      // sphere radius
    [SerializeField] private float _groundCheckDistance = 0.6f;     // cast distance below the player
    [SerializeField] private float _groundMaxSlopeDot = 0.5f;       // require hit.normal ⋅ flightUp >= this (0.5 ~= 60° max slope)
    [SerializeField] private LayerMask _groundMask = ~0;            // which layers count as ground

    [Header("Activation")]
    [SerializeField] private KeyCode _flightActivationKey = KeyCode.Space;
    [SerializeField] private float _flightActivationTime = 0.3f;
    [SerializeField] private KeyCode _flightDeactivationKey = KeyCode.LeftShift;
    [SerializeField] private float _doubleTapTime = 0.4f;

    [Header("Flight – Script Control")]
    [SerializeField] private List<MonoBehaviour> _alwaysOnWhileFlying = new List<MonoBehaviour>();

    public bool IsFlying => _isFlying;

    // Internals
    private Rigidbody _rb;
    private PlayerDash _dash;
    private PlayerMovement _playerMovement;
    private GravityModifier _gravityModifier;
    private PlayerJump _playerJump;
    private Animator _animator;
    private PlayerCamera _playerCamera;
    private GravityBody _gravityBody;
    private Camera _playerCam;                     // used for FOV boost

    private bool _isFlying = false;
    private bool _isInGravityTransition = false;

    private bool _activationHeld = false;
    private float _activationTimer = 0f;

    private float _lastDeactivationTapTime = -10f;
    private int _deactivationTapCount = 0;

    private Vector3 _flightUp = Vector3.up;        // frozen up during flight

    // Super speed state
    private bool _superActive = false;
    private bool _superJustActivated = false;      // snap to super next FixedUpdate
    private float _lastSuperTapTime = -10f;

    // Speed ramp state
    private float _moveTimer = 0f;                 // time movement stick has been held this press
    private float _currentSpeed = 0f;              // smoothed scalar speed
    private Vector3 _lastMoveDir = Vector3.zero;   // persists direction for decel

    // Idle ascend/descend state
    private float _ascendHoldTimer = 0f;
    private float _descendHoldTimer = 0f;
    private bool _idleAscending = false;
    private bool _idleDescending = false;
    private float _idleVerticalVel = 0f;

    // Animator parameter hashes
    private static readonly int Hash_IsFlying         = Animator.StringToHash("isFlying");
    private static readonly int Hash_FlySpeed         = Animator.StringToHash("flySpeed");
    private static readonly int Hash_IsIdleAscending  = Animator.StringToHash("isIdleAscending");
    private static readonly int Hash_IsIdleDescending = Animator.StringToHash("isIdleDescending");

    private float _baseFov = -1f;                  // remember camera base FOV

    // Yaw memory
    private Vector3 _lastYawForward = Vector3.forward;

    private readonly List<MonoBehaviour> _disabledScripts = new List<MonoBehaviour>();

    private const float SAME_GRAVITY_DOT = 0.99985f;
    private float _gravityLockTimer = 0f;   // fail-safe timer while in transition
    private const float GRAVITY_LOCK_TIMEOUT = 1.0f; // seconds

    private float _activePitchAssistDeg = 0f; // smoothed current assist
    private float _assistAscHoldTimer = 0f;
    private float _assistDescHoldTimer = 0f;
    private const float _pitchAssistLimitEpsilon = 0.25f; // deg, safety gap to camera clamp

    // ── LMB free-look state (flight-only)
    private bool _leftPanLockActive = false;     // true while LMB held
    private bool _waitingLmbRealign = false;     // true while camera is auto-aligning back
    private bool _wasLeftPanActive = false;      // edge detection

    // Frozen basis while LMB is down
    private Vector3 _lmbFrozenFwd = Vector3.forward;  // yaw-forward at LMB-down
    private Vector3 _lmbFrozenRight = Vector3.right;  // right at LMB-down
    private float   _lmbFrozenPitchDeg = 0f;          // player pitch at LMB-down

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _dash = GetComponent<PlayerDash>();
        _playerMovement = GetComponent<PlayerMovement>();
        _gravityModifier = GetComponent<GravityModifier>();
        _playerJump = GetComponent<PlayerJump>();
        _animator = GetComponent<Animator>();
        _playerCamera = GetComponentInChildren<PlayerCamera>();
        _gravityBody = GetComponent<GravityBody>();

        if (_cameraTransform == null && Camera.main != null)
            _cameraTransform = Camera.main.transform;

        // Find the actual Camera for FOV control
        if (_playerCamera != null)
        {
            _playerCam = _playerCamera.GetComponentInChildren<Camera>();
        }
        if (_playerCam == null && Camera.main != null)
        {
            _playerCam = Camera.main;
        }

        // Capture base FOV so we can always lerp back smoothly
        if (_playerCam != null)
            _baseFov = _playerCam.fieldOfView;
    }

    private void Update()
    {
        HandleActivationInput();
        HandleDeactivationInput();
        HandleSuperSpeedInput();
        HandleIdleAscendDescendInput();

        // Smooth FOV toward target (applies in & out of flight)
        if (_playerCam != null && _baseFov > 0f)
        {
            // FOV bump only while super is ACTIVE; cancels as soon as super ends
            float targetFov = _baseFov + (_isFlying && _superActive ? _superSpeedFovIncrease : 0f);
            _playerCam.fieldOfView = Mathf.Lerp(_playerCam.fieldOfView, targetFov, Time.deltaTime * 8f);
        }

        if (!_isFlying)
            SetAnimatorParams(false, 0f);
    }

    private void FixedUpdate()
    {
        // Fail-safe: never stay locked forever if some volume never fires "Completed"
if (_isInGravityTransition)
{
    _gravityLockTimer += Time.fixedDeltaTime;
    if (_gravityLockTimer >= GRAVITY_LOCK_TIMEOUT)
    {
        _isInGravityTransition = false;
        _gravityLockTimer = 0f;
    }
}

        if (!_isFlying) return;
        if (_isInGravityTransition) return;
        if (_dash != null && _dash.IsDashing()) return;

        // Auto-exit when we touch ground
        if (_autoExitOnGround && IsTouchingGround())
        {
            ExitFlight();
            return;
        }

        ApplyFlightMovement_YawSmooth_PitchOneToOne_WithPitchAutoLevel();
    }

    // ───────────────────────── Activation / Deactivation ─────────────────────────

    private void HandleActivationInput()
    {
        bool isGrounded = _playerMovement != null && _playerMovement.IsGrounded();

        if (Input.GetKey(_flightActivationKey))
        {
            if (!_activationHeld)
            {
                _activationHeld = true;
                _activationTimer = 0f;
            }
            else
            {
                _activationTimer += Time.deltaTime;
                if (!_isFlying && _activationTimer >= _flightActivationTime && !isGrounded)
                {
                    EnterFlight();
                }
            }
        }
        else
        {
            _activationHeld = false;
            _activationTimer = 0f;
        }
    }

    private void HandleDeactivationInput()
    {
        if (Input.GetKeyDown(_flightDeactivationKey) && _isFlying)
        {
            if (Time.time - _lastDeactivationTapTime <= _doubleTapTime)
            {
                _deactivationTapCount++;
                if (_deactivationTapCount >= 2)
                {
                    ExitFlight();
                    _deactivationTapCount = 0;
                }
            }
            else
            {
                _deactivationTapCount = 1;
            }

            _lastDeactivationTapTime = Time.time;
        }
    }

    private void HandleSuperSpeedInput()
{
    if (!_isFlying) return;

    // Read current WASD input (for non-mouse driving)
    float h = Input.GetAxis("Horizontal");
    float v = Input.GetAxis("Vertical");
    float inputMag = Mathf.Clamp01(new Vector2(h, v).magnitude);

    // "StraightOnly" == purely along forward/back axis (W or S) with NO A/D
    bool straightOnly = (Mathf.Abs(h) < 0.01f) && (Mathf.Abs(v) > 0.01f);

    // NEW: treat "both mouse buttons" as holding full-forward for super eligibility
    var mouse = UnityEngine.InputSystem.Mouse.current;
    bool bothMouse = mouse != null && mouse.leftButton.isPressed && mouse.rightButton.isPressed;
    if (bothMouse)
    {
        // Conceptually we’re driving forward at full stick
        straightOnly = true;
        inputMag = 1f;
    }

    if (Input.GetKeyDown(_superSpeedKey))
    {
        if (Time.time - _lastSuperTapTime <= _superSpeedDoubleTapTime)
        {
            if (!_superActive)
            {
                bool fastEnoughNow = _currentSpeed >= _fastSpeed * _superActivateMinFactor;

                // NEW: pressingHard counts if both mouse buttons are down
                bool pressingHard  = (inputMag >= _fastInputThreshold) || bothMouse;

                // Allow turning ON super while using two-button autorun
                if (fastEnoughNow && pressingHard && straightOnly)
                {
                    _superActive = true;
                    _superJustActivated = true; // snap next FixedUpdate
                }
                // else: ignore this double-tap (not eligible yet)
            }
            else
            {
                // Manual toggle OFF always allowed
                _superActive = false;
            }

            _lastSuperTapTime = -10f; // reset chain
        }
        else
        {
            _lastSuperTapTime = Time.time; // first tap
        }
    }
}

    // ───────────────────────── Idle Ascend / Descend Input (no WASD) ─────────────────────────

    private void HandleIdleAscendDescendInput()
    {
        if (!_isFlying)
        {
            ResetIdleUpDown();
            return;
        }

        // If there is any directional input, idle up/down is disabled.
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        bool hasInput = Mathf.Abs(h) > 0.001f || Mathf.Abs(v) > 0.001f;

        if (hasInput)
        {
            ResetIdleUpDown();
            return;
        }

        // Ascend hold
        if (Input.GetKey(_idleAscendKey))
        {
            _ascendHoldTimer += Time.deltaTime;
            if (_ascendHoldTimer >= _idleHoldDelay)
            {
                _idleAscending = true;
                _idleDescending = false;
            }
        }
        else
        {
            _ascendHoldTimer = 0f;
            if (_idleAscending) _idleAscending = false;
        }

        // Descend hold
        if (Input.GetKey(_idleDescendKey))
        {
            _descendHoldTimer += Time.deltaTime;
            if (_descendHoldTimer >= _idleHoldDelay)
            {
                _idleDescending = true;
                _idleAscending = false;
            }
        }
        else
        {
            _descendHoldTimer = 0f;
            if (_idleDescending) _idleDescending = false;
        }
    }

    private void ResetIdleUpDown()
    {
        _ascendHoldTimer = 0f;
        _descendHoldTimer = 0f;
        _idleAscending = false;
        _idleDescending = false;
    }

    private void EnterFlight()
{
    _isFlying = true;

    if (_dash != null && _dash.IsDashing())
        _dash.EndDashEarly();

    _rb.velocity = Vector3.zero;
    _rb.angularVelocity = Vector3.zero;
    _rb.useGravity = false;

    SetAnimatorParams(true, 0f);
    DisableOtherScriptsForFlight();

    // Freeze a stable "up" for the whole flight session
    Vector3 upCandidate = Vector3.up;
    if (_playerCamera != null)
        upCandidate = _playerCamera.GetCurrentGravityUp();
    else if (_gravityBody != null)
    {
        Vector3 g = _gravityBody.GetEffectiveGravityDirection();
        if (g != Vector3.zero) upCandidate = (-g).normalized;
    }
    _flightUp = upCandidate;

    // Initialize last yaw using current facing, flattened on flight-up
    _lastYawForward = Vector3.ProjectOnPlane(transform.forward, _flightUp).normalized;
    if (_lastYawForward.sqrMagnitude < 0.0001f)
        _lastYawForward = Vector3.ProjectOnPlane(Vector3.forward, _flightUp).normalized;

    // Reset runtime states
    _moveTimer = 0f;
    _currentSpeed = 0f;
    _lastMoveDir = Vector3.zero;
    _superActive = false;
    _superJustActivated = false;
    ResetIdleUpDown();

    // Reset LMB free-look state (flight)
    _leftPanLockActive = false;
    _waitingLmbRealign = false;
    _wasLeftPanActive = false;

    // Record base FOV if we didn't in Awake (just in case)
    if (_playerCam != null && _baseFov <= 0f)
        _baseFov = _playerCam.fieldOfView;
}

    private void ExitFlight()
    {
        _isFlying = false;

        SetAnimatorParams(false, 0f);

        RestoreDisabledScripts();
        _rb.useGravity = true;

        // Do NOT snap FOV; Update() keeps lerping back to _baseFov smoothly.
        _superActive = false;
        _superJustActivated = false;
        ResetIdleUpDown();
        _idleVerticalVel = 0f;
    }

    private void ApplyFlightMovement_YawSmooth_PitchOneToOne_WithPitchAutoLevel()
{
    if (_cameraTransform == null) return;

    // ── Inputs (keyboard axes)
    float h = Input.GetAxis("Horizontal");
    float v = Input.GetAxis("Vertical");
    bool hasInput = Mathf.Abs(h) > 0.001f || Mathf.Abs(v) > 0.001f;
    float inputMag = Mathf.Clamp01(new Vector2(h, v).magnitude);

    const float EPS = 0.01f;
    bool pureStrafe    = Mathf.Abs(h) > EPS && Mathf.Abs(v) < EPS;      // A or D only
    bool backwardInput = v < -EPS;                                      // any backward
    bool straightOnly  = (Mathf.Abs(h) < EPS) && (Mathf.Abs(v) > EPS);  // W or S only (for super)

    // ── Mouse state (read in FixedUpdate to avoid Update/FixedUpdate race)
    var mouse = UnityEngine.InputSystem.Mouse.current;
    bool lmb   = mouse != null && mouse.leftButton.isPressed;
    bool rmb   = mouse != null && mouse.rightButton.isPressed;
    bool overUi = UnityEngine.EventSystems.EventSystem.current != null
               && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

    // LMB-only free-look (not while RMB also pressed, not over UI)
    bool leftPanDesiredNow = lmb && !rmb && !overUi;

    // ───────────────────────── BOTH-BUTTON AUTORUN/STEER ─────────────────────────
    // When both mouse buttons are down: treat as "hold W" and steer by the camera.
    // Also cancel any LMB freelook ignore so camera instantly drives movement & yaw.
    bool bothMouse = lmb && rmb;
    if (bothMouse)
    {
        // Override "movement input" to be straight forward at full stick
        h = 0f;
        v = 1f;
        hasInput = true;
        inputMag = 1f;
        pureStrafe = false;
        backwardInput = false;
        straightOnly = true;

        // Ensure we are not ignoring the camera due to a previous LMB snapshot
        _leftPanLockActive = false;
        _waitingLmbRealign = false;
        _wasLeftPanActive  = false;
    }

    // ── Edge: LMB started → freeze a stable basis and the current pitch (so player never spins)
    if (leftPanDesiredNow && !_wasLeftPanActive)
    {
        _leftPanLockActive = true;

        // Freeze yaw-forward on the flight plane
        _lmbFrozenFwd = Vector3.ProjectOnPlane(
            _lastYawForward.sqrMagnitude > 0.0001f
                ? _lastYawForward
                : Vector3.ProjectOnPlane(transform.forward, _flightUp),
            _flightUp
        ).normalized;
        if (_lmbFrozenFwd.sqrMagnitude < 0.0001f)
            _lmbFrozenFwd = Vector3.ProjectOnPlane(Vector3.forward, _flightUp).normalized;

        _lmbFrozenRight = Vector3.Cross(_flightUp, _lmbFrozenFwd).normalized;

        // Freeze current player pitch (relative to the yaw-forward/right axes)
        Vector3 yawFwdNow = Vector3.ProjectOnPlane(transform.forward, _flightUp).normalized;
        if (yawFwdNow.sqrMagnitude < 0.0001f) yawFwdNow = _lmbFrozenFwd;
        Vector3 rightNow = Vector3.Cross(_flightUp, yawFwdNow).normalized;
        _lmbFrozenPitchDeg = Vector3.SignedAngle(yawFwdNow, transform.forward, rightNow);
    }

    // ── Edge: LMB released → keep ignoring camera until it’s actually behind the player
    if (!leftPanDesiredNow && _wasLeftPanActive)
    {
        _leftPanLockActive = false;

        // Enter "waiting realign" unless RMB is pressed (RMB/both actively steer camera)
        _waitingLmbRealign = !rmb;

        if (_playerCamera != null && !rmb)
            _playerCamera.StartAutoAlignBehindPlayer(0.35f, null);
    }
    _wasLeftPanActive = leftPanDesiredNow;

    // If we're waiting for the camera to get behind us, release that lock once close enough.
    if (_waitingLmbRealign)
    {
        const float REALIGN_RELEASE_ANGLE = 8f; // deg tolerance to consider "behind player"
        Vector3 camFlat = Vector3.ProjectOnPlane(_cameraTransform.forward, _flightUp).normalized;
        if (camFlat.sqrMagnitude < 0.0001f)
            camFlat = _lastYawForward;

        float misalignment = Vector3.Angle(camFlat, _lastYawForward);
        if (misalignment <= REALIGN_RELEASE_ANGLE)
            _waitingLmbRealign = false;
    }

    // While LMB is down or waiting realign, ignore the live camera for controls.
    // BUT: if both mouse buttons are down, we already canceled these above.
    bool ignoreCameraForControls = _leftPanLockActive || _waitingLmbRealign;

    // ── Control basis & "camera forward for move"
    Vector3 camBaseFwd, camRightOnPlane, flatCamFwdForMove;
    float camVertForMove, horizFactorForMove;

    // ── Pitch assist (Space/Shift) — also allowed during LMB freelook (so vertical flying still works)
    bool canApplyPitch = hasInput && !(pureStrafe || backwardInput);
    if (canApplyPitch && Input.GetKey(_idleAscendKey)) _assistAscHoldTimer += Time.fixedDeltaTime; else _assistAscHoldTimer = 0f;
    if (canApplyPitch && Input.GetKey(_idleDescendKey)) _assistDescHoldTimer += Time.fixedDeltaTime; else _assistDescHoldTimer = 0f;

    bool ascendReady  = canApplyPitch && _assistAscHoldTimer  >= _pitchAssistHoldDelay;
    bool descendReady = canApplyPitch && _assistDescHoldTimer >= _pitchAssistHoldDelay;

    // Target assist (degrees). When ignoring the camera, clamp to +/- _pitchAssistDegrees.
    float desiredAssist = 0f;
    if (ascendReady)  desiredAssist += (-_pitchAssistDegrees) * inputMag; // Space → nose up
    if (descendReady) desiredAssist += (+_pitchAssistDegrees) * inputMag; // Shift → nose down

    float assistMin, assistMax;
    if (ignoreCameraForControls)
    {
        assistMin = -_pitchAssistDegrees;
        assistMax = +_pitchAssistDegrees;
    }
    else
    {
        // respect camera pitch limit when not ignoring camera
        Vector3 camFwdNoAssist = _cameraTransform.forward;
        Vector3 flatNoAssist   = Vector3.ProjectOnPlane(camFwdNoAssist, _flightUp).normalized;
        float camPitchNoAssist = -Vector3.SignedAngle(flatNoAssist, camFwdNoAssist, Vector3.Cross(flatNoAssist, _flightUp));
        float limit = _pitchAssistCamLimit;
        assistMin = -limit - camPitchNoAssist;
        assistMax =  limit - camPitchNoAssist;
    }

    desiredAssist = Mathf.Clamp(desiredAssist, assistMin, assistMax);
    _activePitchAssistDeg = Mathf.MoveTowardsAngle(
        _activePitchAssistDeg, desiredAssist,
        _pitchAssistLerpSpeed * Time.fixedDeltaTime * _pitchAssistDegrees
    );

    // ───────────────────────── IGNORE-CAMERA BRANCH (LMB freelook) ─────────────────────────
    if (ignoreCameraForControls)
    {
        camBaseFwd      = _lmbFrozenFwd;
        camRightOnPlane = _lmbFrozenRight;

        float pitchDeg  = _lmbFrozenPitchDeg + _activePitchAssistDeg;
        Vector3 camFwdForMove = Quaternion.AngleAxis(pitchDeg, camRightOnPlane) * camBaseFwd;

        flatCamFwdForMove   = camBaseFwd;
        camVertForMove      = Vector3.Dot(camFwdForMove, _flightUp);
        horizFactorForMove  = Mathf.Sqrt(Mathf.Clamp01(1f - camVertForMove * camVertForMove));

        // Movement
        Vector3 horiz = flatCamFwdForMove * (v * horizFactorForMove) + camRightOnPlane * h;
        Vector3 vertBase = canApplyPitch ? (_flightUp * (camVertForMove * inputMag)) : Vector3.zero;

        Vector3 desiredMoveDir = horiz + vertBase;
        if (desiredMoveDir.sqrMagnitude > 1f) desiredMoveDir.Normalize();
        else if (desiredMoveDir.sqrMagnitude > 0f) desiredMoveDir = desiredMoveDir.normalized;

        if (hasInput && desiredMoveDir.sqrMagnitude > 0.0001f)
            _lastMoveDir = desiredMoveDir;

        // Super maintenance
        if (_superActive && (!hasInput || inputMag < _superHoldThreshold || !(Mathf.Abs(h) < EPS && Mathf.Abs(v) > EPS)))
            _superActive = false;

        // Speed
        _moveTimer = hasInput ? (_moveTimer + Time.fixedDeltaTime) : 0f;
        float targetSpeed =
            _superActive ? _superSpeed :
            (hasInput ? (inputMag >= _fastInputThreshold && _moveTimer >= _timeToFast ? _fastSpeed : _slowSpeed) : 0f);
        float rate = hasInput
            ? (_superActive || targetSpeed >= _fastSpeed - 0.001f ? _accelRateFast : _accelRateSlow)
            : _decelRate;
        if (_superJustActivated) { _currentSpeed = _superSpeed; _superJustActivated = false; }
        else                     { _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, rate * Time.fixedDeltaTime); }

        // Idle up/down
        bool idleUpDownActive = !hasInput && (_idleAscending || _idleDescending);
        float targetIdleVert = idleUpDownActive ? (_idleAscending ? 1f : -1f) * _idleVerticalSpeed : 0f;
        _idleVerticalVel = Mathf.MoveTowards(_idleVerticalVel, targetIdleVert, _idleVerticalAccel * Time.fixedDeltaTime);

        // Apply velocity
        if (hasInput)
            _rb.velocity = desiredMoveDir * _currentSpeed;
        else
        {
            Vector3 horizontal = (_currentSpeed > 0.01f && _lastMoveDir.sqrMagnitude > 0.0001f) ? _lastMoveDir * _currentSpeed : Vector3.zero;
            Vector3 vertical = _flightUp * _idleVerticalVel;
            _rb.velocity = horizontal + vertical;
        }

        SetAnimatorParams(true, _rb.velocity.magnitude);

        // Orientation (stay on frozen yaw)
        float maskedPitch = canApplyPitch ? (_lmbFrozenPitchDeg + _activePitchAssistDeg) : _lmbFrozenPitchDeg;

        Vector3 desiredYawFwd;
        if (hasInput)
        {
            Vector3 horizForYaw = flatCamFwdForMove * (v * horizFactorForMove) + camRightOnPlane * h;
            Vector3 yawFromMove = Vector3.ProjectOnPlane(horizForYaw, _flightUp).normalized;
            if (yawFromMove.sqrMagnitude < 0.0001f) yawFromMove = _lastYawForward;

            desiredYawFwd = Vector3.Slerp(_lastYawForward, yawFromMove, Time.fixedDeltaTime * _rotationSpeed).normalized;
            _lastYawForward = desiredYawFwd;
        }
        else
        {
            desiredYawFwd = _lastYawForward;
        }

        Vector3 rightAxis = Vector3.Cross(_flightUp, desiredYawFwd).normalized;
        if (rightAxis.sqrMagnitude < 0.0001f) rightAxis = camRightOnPlane;

        Vector3 targetForwardMoving = Quaternion.AngleAxis(maskedPitch, rightAxis) * desiredYawFwd;
        Vector3 targetForwardLevel  = desiredYawFwd;

        if (hasInput)
        {
            Quaternion targetRot = Quaternion.LookRotation(targetForwardMoving, _flightUp);
            Quaternion newRot = Quaternion.Slerp(_rb.rotation, targetRot, Time.fixedDeltaTime * _rotationSpeed);
            _rb.MoveRotation(newRot);
        }
        else
        {
            Quaternion targetRot = Quaternion.LookRotation(targetForwardLevel, _flightUp);
            Quaternion newRot = Quaternion.Slerp(_rb.rotation, targetRot, Time.fixedDeltaTime * _pitchReturnSpeed);
            _rb.MoveRotation(newRot);
        }

        return; // end ignore-camera branch
    }

    // ───────────────────────── LIVE-CAMERA BRANCH (camera drives movement) ─────────────────────────
    Vector3 camFwd = _cameraTransform.forward;
    Vector3 flatCamFwd = Vector3.ProjectOnPlane(camFwd, _flightUp).normalized;
    if (flatCamFwd.sqrMagnitude < 0.0001f)
        flatCamFwd = Vector3.ProjectOnPlane(Vector3.forward, _flightUp).normalized;

    camRightOnPlane = Vector3.Cross(_flightUp, flatCamFwd).normalized;

    // Apply assist to camera forward (only when using WASD-style forward; in both-mouse we still allow assist keys)
    Vector3 camFwdForMoveLive = hasInput
        ? (Quaternion.AngleAxis(_activePitchAssistDeg, camRightOnPlane) * camFwd)
        : camFwd;

    flatCamFwdForMove = Vector3.ProjectOnPlane(camFwdForMoveLive, _flightUp).normalized;
    if (flatCamFwdForMove.sqrMagnitude < 0.0001f)
        flatCamFwdForMove = flatCamFwd;

    camVertForMove      = Vector3.Dot(camFwdForMoveLive, _flightUp);
    horizFactorForMove  = Mathf.Sqrt(Mathf.Clamp01(1f - camVertForMove * camVertForMove));

    // Movement build (h,v will be (0,1) in both-mouse mode)
    Vector3 horizLive = flatCamFwdForMove * (v * horizFactorForMove) + camRightOnPlane * h;

    Vector3 vertBaseLive = (!pureStrafe && !backwardInput)
        ? (_flightUp * (camVertForMove * (hasInput ? inputMag : 0f)))
        : Vector3.zero;

    // Extra lift from assist (only when pitch-driven vertical is allowed)
    float assistSign = 0f;
    if (!pureStrafe && !backwardInput && canApplyPitch && _assistAscHoldTimer >= _pitchAssistHoldDelay)  assistSign += (Input.GetKey(_idleAscendKey)  ? 1f : 0f);
    if (!pureStrafe && !backwardInput && canApplyPitch && _assistDescHoldTimer >= _pitchAssistHoldDelay) assistSign -= (Input.GetKey(_idleDescendKey) ? 1f : 0f);

    const float ASSIST_LIFT_SCALE = 0.75f;
    float assistLiftFrac = Mathf.Sin(Mathf.Abs(_activePitchAssistDeg) * Mathf.Deg2Rad);
    Vector3 vertAssistLive = (!pureStrafe && !backwardInput)
        ? (_flightUp * (assistSign * assistLiftFrac * ASSIST_LIFT_SCALE * (hasInput ? inputMag : 0f)))
        : Vector3.zero;

    Vector3 desiredMoveDirLive = horizLive + vertBaseLive + vertAssistLive;
    if (desiredMoveDirLive.sqrMagnitude > 1f) desiredMoveDirLive.Normalize();
    else if (desiredMoveDirLive.sqrMagnitude > 0f) desiredMoveDirLive = desiredMoveDirLive.normalized;

    if (hasInput && desiredMoveDirLive.sqrMagnitude > 0.0001f)
        _lastMoveDir = desiredMoveDirLive;

    // Super maintenance
    if (_superActive && (!hasInput || inputMag < _superHoldThreshold || !(Mathf.Abs(h) < EPS && Mathf.Abs(v) > EPS)))
        _superActive = false;

    // Speed target
    _moveTimer = hasInput ? (_moveTimer + Time.fixedDeltaTime) : 0f;
    float targetSpeedLive =
        _superActive ? _superSpeed :
        (hasInput ? (inputMag >= _fastInputThreshold && _moveTimer >= _timeToFast ? _fastSpeed : _slowSpeed) : 0f);
    float rateLive = hasInput
        ? (_superActive || targetSpeedLive >= _fastSpeed - 0.001f ? _accelRateFast : _accelRateSlow)
        : _decelRate;
    if (_superJustActivated) { _currentSpeed = _superSpeed; _superJustActivated = false; }
    else                     { _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeedLive, rateLive * Time.fixedDeltaTime); }

    // Idle ascend/descend (no WASD)
    bool idleUpDownActiveLive = !hasInput && (_idleAscending || _idleDescending);
    float targetIdleVertLive = idleUpDownActiveLive ? (_idleAscending ? 1f : -1f) * _idleVerticalSpeed : 0f;
    _idleVerticalVel = Mathf.MoveTowards(_idleVerticalVel, targetIdleVertLive, _idleVerticalAccel * Time.fixedDeltaTime);

    // Apply velocity
    if (hasInput)
        _rb.velocity = desiredMoveDirLive * _currentSpeed;
    else
    {
        Vector3 horizontal = (_currentSpeed > 0.01f && _lastMoveDir.sqrMagnitude > 0.0001f) ? _lastMoveDir * _currentSpeed : Vector3.zero;
        Vector3 vertical = _flightUp * _idleVerticalVel;
        _rb.velocity = horizontal + vertical;
    }

    SetAnimatorParams(true, _rb.velocity.magnitude);

    // Orientation: yaw toward camera horizontal, pitch toward camera pitch (masked by strafe/back flags)
    float camPitchForMove = (!pureStrafe && !backwardInput)
        ? -Vector3.SignedAngle(flatCamFwdForMove, camFwdForMoveLive, Vector3.Cross(flatCamFwdForMove, _flightUp))
        : 0f;

    float maskedPitchLive = (pureStrafe || backwardInput) ? 0f : camPitchForMove;

    Vector3 desiredYawFwdLive;
    if (hasInput)
    {
        Vector3 yawFromMove = Vector3.ProjectOnPlane(horizLive, _flightUp).normalized;
        if (yawFromMove.sqrMagnitude < 0.0001f) yawFromMove = _lastYawForward;

        desiredYawFwdLive = Vector3.Slerp(_lastYawForward, yawFromMove, Time.fixedDeltaTime * _rotationSpeed).normalized;
        _lastYawForward = desiredYawFwdLive;
    }
    else
    {
        desiredYawFwdLive = _lastYawForward;
    }

    Vector3 rightAxisLive = Vector3.Cross(_flightUp, desiredYawFwdLive).normalized;
    if (rightAxisLive.sqrMagnitude < 0.0001f) rightAxisLive = camRightOnPlane;

    Vector3 targetForwardMovingLive = Quaternion.AngleAxis(maskedPitchLive, rightAxisLive) * desiredYawFwdLive;
    Vector3 targetForwardLevelLive  = desiredYawFwdLive;

    if (hasInput)
    {
        Quaternion targetRot = Quaternion.LookRotation(targetForwardMovingLive, _flightUp);
        Quaternion newRot = Quaternion.Slerp(_rb.rotation, targetRot, Time.fixedDeltaTime * _rotationSpeed);
        _rb.MoveRotation(newRot);
    }
    else
    {
        Quaternion targetRot = Quaternion.LookRotation(targetForwardLevelLive, _flightUp);
        Quaternion newRot = Quaternion.Slerp(_rb.rotation, targetRot, Time.fixedDeltaTime * _pitchReturnSpeed);
        _rb.MoveRotation(newRot);
    }
}

    private void SetAnimatorParams(bool flying, float speed)
    {
        if (_animator == null) return;
        _animator.SetBool(Hash_IsFlying, flying);
        _animator.SetFloat(Hash_FlySpeed, speed); // m/s; map to your blend tree as needed
        _animator.SetBool(Hash_IsIdleAscending,  _idleAscending);
        _animator.SetBool(Hash_IsIdleDescending, _idleDescending);
    }

    // ───────────────────────── Ground check ─────────────────────────

    private bool IsTouchingGround()
    {
        // Cast slightly from above the body along -flightUp
        Vector3 origin = transform.position + _flightUp * 0.1f;
        Vector3 dir = -_flightUp;
        float dist = Mathf.Max(0.01f, _groundCheckDistance);

        if (Physics.SphereCast(origin, _groundCheckRadius, dir, out RaycastHit hit, dist, _groundMask, QueryTriggerInteraction.Ignore))
        {
            // Ensure the surface is "ground-like" (not a wall/ceiling) relative to current flight-up
            float dot = Vector3.Dot(hit.normal.normalized, _flightUp);
            return dot >= _groundMaxSlopeDot;
        }

        return false;
    }

    // ───────────────────────── Script / Animator helpers ─────────────────────────

    private void DisableOtherScriptsForFlight()
    {
        _disabledScripts.Clear();
        MonoBehaviour[] all = GetComponents<MonoBehaviour>();
        foreach (var mb in all)
        {
            if (mb == null) continue;
            if (mb == (MonoBehaviour)this) continue;

            if (mb.enabled)
            {
                mb.enabled = false;
                _disabledScripts.Add(mb);
            }
        }
    }

    private void RestoreDisabledScripts()
    {
        for (int i = _disabledScripts.Count - 1; i >= 0; i--)
        {
            var mb = _disabledScripts[i];
            if (mb != null) mb.enabled = true;
        }
        _disabledScripts.Clear();
    }

    // ───────────────────────── Integration hooks ─────────────────────────

    public void OnCameraPanning(Vector3 cameraForward) { /* handled in FixedUpdate */ }

    public void OnGravityTransitionStarted(Vector3 oldDir, Vector3 newDir, float duration)
{
    // Ignore trivial changes (same-up)
    if (oldDir.sqrMagnitude > 0.0001f && newDir.sqrMagnitude > 0.0001f)
    {
        float dot = Vector3.Dot(oldDir.normalized, newDir.normalized);
        if (dot >= SAME_GRAVITY_DOT)
            return;
    }

    _isInGravityTransition = true;
    _gravityLockTimer = 0f;
}

public void OnGravityTransitionCompleted(Vector3 oldDir, Vector3 newDir, float duration)
{
    _isInGravityTransition = false;
    _gravityLockTimer = 0f;

    // Refresh flight-up if we’re still flying
    if (_isFlying)
    {
        Vector3 upCandidate = (_playerCamera != null)
            ? _playerCamera.GetCurrentGravityUp()
            : (newDir.sqrMagnitude > 0.0001f ? newDir.normalized : _flightUp);

        if (upCandidate.sqrMagnitude > 0.0001f)
            _flightUp = upCandidate;

        _rb.velocity = Vector3.zero; // small safety to avoid odd impulses
    }
}
}