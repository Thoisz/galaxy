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

        // We need current input magnitude here to decide activation eligibility.
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float inputMag = Mathf.Clamp01(new Vector2(h, v).magnitude);

        if (Input.GetKeyDown(_superSpeedKey))
        {
            if (Time.time - _lastSuperTapTime <= _superSpeedDoubleTapTime)
            {
                // Attempt to toggle ON if currently off:
                if (!_superActive)
                {
                    bool fastEnoughNow = _currentSpeed >= _fastSpeed * _superActivateMinFactor;
                    bool pressingHard  = inputMag >= _fastInputThreshold;

                    if (fastEnoughNow && pressingHard)
                    {
                        _superActive = true;
                        _superJustActivated = true; // snap next FixedUpdate
                    }
                    // else: ignore the double-tap (not eligible yet)
                }
                else
                {
                    // Already in super → allow manual toggle off
                    _superActive = false;
                }

                _lastSuperTapTime = -10f; // reset
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

    // ───────────────────────── Movement / Rotation ─────────────────────────
private void ApplyFlightMovement_YawSmooth_PitchOneToOne_WithPitchAutoLevel()
{
    if (_cameraTransform == null) return;

    // Inputs
    float h = Input.GetAxis("Horizontal");
    float v = Input.GetAxis("Vertical");
    bool hasInput = Mathf.Abs(h) > 0.001f || Mathf.Abs(v) > 0.001f;
    float inputMag = Mathf.Clamp01(new Vector2(h, v).magnitude);

    // Camera basis relative to flight-up
    Vector3 camFwd = _cameraTransform.forward;
    Vector3 flatCamFwd = Vector3.ProjectOnPlane(camFwd, _flightUp).normalized;
    if (flatCamFwd.sqrMagnitude < 0.0001f)
        flatCamFwd = Vector3.ProjectOnPlane(Vector3.forward, _flightUp).normalized;

    Vector3 camRightOnPlane = Vector3.Cross(_flightUp, flatCamFwd).normalized;

    float camVert = Vector3.Dot(camFwd, _flightUp);
    float horizFactor = Mathf.Sqrt(Mathf.Clamp01(1f - camVert * camVert));

    // ───────────────────── In-flight Pitch Assist (Space/Shift while moving) ─────────────────────
    // Compute the *camera* pitch without assist, in degrees (+up / -down w.r.t. flightUp)
    float camPitchNoAssist = -Vector3.SignedAngle(
        flatCamFwd,
        camFwd,
        Vector3.Cross(flatCamFwd, _flightUp)
    );

    // Hold delays so we don't clash with Space double-tap (super) or Shift double-tap (exit)
    if (hasInput && Input.GetKey(_idleAscendKey)) _assistAscHoldTimer  += Time.fixedDeltaTime; else _assistAscHoldTimer  = 0f;
    if (hasInput && Input.GetKey(_idleDescendKey)) _assistDescHoldTimer += Time.fixedDeltaTime; else _assistDescHoldTimer = 0f;

    bool ascendReady  = hasInput && _assistAscHoldTimer  >= _pitchAssistHoldDelay; // Space → nose UP
    bool descendReady = hasInput && _assistDescHoldTimer >= _pitchAssistHoldDelay; // Shift → nose DOWN

    // Strength: any WASD (including strafing) drives the effect. (Fixes "A/D do nothing")
    float driveFactor = hasInput ? Mathf.Clamp01(Mathf.Abs(v) + Mathf.Abs(h)) : 0f;

    // Desired assist angle (for ORIENTATION only). Convention here:
    // Space (ascend) tilts nose UP = negative angle around right axis,
    // Shift (descend) tilts nose DOWN = positive angle.
    float desiredAssist = 0f;
    if (ascendReady  && driveFactor > 0f) desiredAssist += (-_pitchAssistDegrees) * driveFactor;
    if (descendReady && driveFactor > 0f) desiredAssist += (+_pitchAssistDegrees) * driveFactor;

    // Clamp so (camera pitch + assist) never exceeds ±_pitchAssistCamLimit
    float limit    = _pitchAssistCamLimit;
    float assistMin = -limit - camPitchNoAssist;
    float assistMax =  limit - camPitchNoAssist;
    desiredAssist  = Mathf.Clamp(desiredAssist, assistMin, assistMax);

    // Smooth toward target and clamp again (prevents overshoot when turning camera while holding)
    float smoothed = Mathf.MoveTowardsAngle(
        _activePitchAssistDeg,
        desiredAssist,
        _pitchAssistLerpSpeed * Time.fixedDeltaTime * _pitchAssistDegrees
    );
    _activePitchAssistDeg = Mathf.Clamp(smoothed, assistMin, assistMax);

    // Use the biased forward for movement/orientation while moving
    Vector3 camFwdForMove = hasInput
        ? (Quaternion.AngleAxis(_activePitchAssistDeg, camRightOnPlane) * camFwd)
        : camFwd;

    Vector3 flatCamFwdForMove = Vector3.ProjectOnPlane(camFwdForMove, _flightUp).normalized;
    if (flatCamFwdForMove.sqrMagnitude < 0.0001f)
        flatCamFwdForMove = flatCamFwd;

    float camVertForMove     = Vector3.Dot(camFwdForMove, _flightUp);
    float horizFactorForMove = Mathf.Sqrt(Mathf.Clamp01(1f - camVertForMove * camVertForMove));

    // ── Movement build
    // 1) Horizontal (yaw-plane) component from W/S and A/D
    Vector3 horiz = flatCamFwdForMove * (v * horizFactorForMove) + camRightOnPlane * h;

    // 2) Base vertical from camera pitch, but ONLY when moving forward (prevents backward flip/reversal)
    float forwardOnly = Mathf.Max(0f, v); // ignore backward (S) for base vertical
    Vector3 vertBase  = _flightUp * (camVertForMove * forwardOnly);

    // 3) Assist vertical: Space = up, Shift = down regardless of W/S sign (fixes "S reversed" & strafing)
    float assistSign = 0f; // +1 = up, -1 = down
    if (ascendReady)  assistSign += 1f;
    if (descendReady) assistSign -= 1f;

    // Map current assist angle to a small lift fraction, scale it, and tie to driveFactor so it fades with input
    float assistLiftFrac = Mathf.Sin(Mathf.Abs(_activePitchAssistDeg) * Mathf.Deg2Rad); // 0..sin(maxDeg)
    const float ASSIST_LIFT_SCALE = 0.75f; // tune to taste
    Vector3 vertAssist = _flightUp * (assistSign * assistLiftFrac * ASSIST_LIFT_SCALE * driveFactor);

    // Final desired direction
    Vector3 desiredMoveDir = horiz + vertBase + vertAssist;
    if (desiredMoveDir.sqrMagnitude > 1f) desiredMoveDir.Normalize();
    else if (desiredMoveDir.sqrMagnitude > 0f) desiredMoveDir = desiredMoveDir.normalized;

    // Persist last direction for decel / idle blend
    if (hasInput && desiredMoveDir.sqrMagnitude > 0.0001f)
        _lastMoveDir = desiredMoveDir;

    // ── Super maintenance: cancel instantly if not held hard enough
    if (_superActive && (!hasInput || inputMag < _superHoldThreshold))
        _superActive = false;

    // ── Determine target speed
    _moveTimer = hasInput ? (_moveTimer + Time.fixedDeltaTime) : 0f;

    float targetSpeed;
    if (_superActive)
    {
        targetSpeed = _superSpeed; // always super while active
    }
    else
    {
        bool allowFast = inputMag >= _fastInputThreshold && _moveTimer >= _timeToFast;
        targetSpeed = hasInput ? (allowFast ? _fastSpeed : _slowSpeed) : 0f;
    }

    // Pick accel/decel rate
    float rate = hasInput
        ? (_superActive || targetSpeed >= _fastSpeed - 0.001f ? _accelRateFast : _accelRateSlow)
        : _decelRate;

    // Smooth current speed toward target, but snap if we just entered super
    if (_superJustActivated)
    {
        _currentSpeed = _superSpeed;
        _superJustActivated = false;
    }
    else
    {
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, rate * Time.fixedDeltaTime);
    }

    // ── Idle ascend/descend overrides when NO input (unchanged)
    bool idleUpDownActive = !hasInput && (_idleAscending || _idleDescending);
    float targetIdleVert = 0f;
    if (idleUpDownActive)
        targetIdleVert = (_idleAscending ? 1f : -1f) * _idleVerticalSpeed;

    _idleVerticalVel = Mathf.MoveTowards(_idleVerticalVel, targetIdleVert, _idleVerticalAccel * Time.fixedDeltaTime);

    // Apply velocity
    if (hasInput)
    {
        _rb.velocity = desiredMoveDir * _currentSpeed;   // full 3D when moving (now with assist)
    }
    else
    {
        // No WASD: horizontal decays to 0 via _currentSpeed, but idle up/down may apply vertical
        Vector3 horizontal = Vector3.zero;
        if (_currentSpeed > 0.01f && _lastMoveDir.sqrMagnitude > 0.0001f)
            horizontal = _lastMoveDir * _currentSpeed;

        Vector3 vertical = _flightUp * _idleVerticalVel; // idle up/down (can be 0)
        _rb.velocity = horizontal + vertical;
    }

    // Update Animator params
    SetAnimatorParams(true, _rb.velocity.magnitude);

    // ── Orientation (use the same *biased* forward for pitch)
    float camPitchForMove = -Vector3.SignedAngle(
        flatCamFwdForMove,
        camFwdForMove,
        Vector3.Cross(flatCamFwdForMove, _flightUp)
    );

    // Yaw target (from horizontal motion only; never from pure vertical)
    Vector3 desiredYawFwd;
    if (hasInput)
    {
        Vector3 yawFromMove = Vector3.ProjectOnPlane(horiz, _flightUp).normalized;
        if (yawFromMove.sqrMagnitude < 0.0001f)
            yawFromMove = _lastYawForward;

        desiredYawFwd = Vector3.Slerp(_lastYawForward, yawFromMove, Time.fixedDeltaTime * _rotationSpeed).normalized;
        _lastYawForward = desiredYawFwd;
    }
    else
    {
        desiredYawFwd = _lastYawForward;
    }

    // Build target forward (moving: camera pitch 1:1 with assist, idle: level out pitch)
    Vector3 rightAxis = Vector3.Cross(_flightUp, desiredYawFwd).normalized;
    if (rightAxis.sqrMagnitude < 0.0001f)
        rightAxis = camRightOnPlane;

    Vector3 targetForwardMoving = Quaternion.AngleAxis(camPitchForMove, rightAxis) * desiredYawFwd;
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