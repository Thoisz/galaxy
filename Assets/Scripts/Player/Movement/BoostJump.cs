using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)] // run BEFORE PlayerJump so we can block the Space down-frame
public class BoostJump : MonoBehaviour
{
    [Header("Auto-find (from parents)")]
    [SerializeField] private PlayerFlight   playerFlight;   // optional
    [SerializeField] private Rigidbody      playerBody;
    [SerializeField] private Camera         playerCam;      // for passing base FOV to FX
    [SerializeField] private Component      gravityBody;    // something exposing gravity direction (e.g., GravityBody)
    [SerializeField] private PlayerCrouch   playerCrouch;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerJump     playerJump;     // to suppress normal jumps

    [Header("Camera FX (auto-bound at runtime)")]
    [SerializeField] private CameraBoostFX cameraBoostFx;   // optional; will be auto-found if left empty
    [SerializeField] private bool debugFxBinding = false;

    [Header("Animator (optional)")]
    [Tooltip("Animator with a bool parameter named `isBoostJump` (or rename below).")]
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private string   isBoostJumpParam = "isBoostJump";
    [SerializeField] private bool     animatorDebugLogs = false;

    [Header("Input")]
    [SerializeField] private KeyCode triggerKey = KeyCode.Space; // pressed with crouch

    [Header("Charge")]
    [Tooltip("Time to hold Space (with crouch) before launch becomes available.")]
    [SerializeField] private float chargeTimeSeconds = 1.00f;

    [Header("Launch (single strength)")]
    [Tooltip("Upward launch speed (world units/s).")]
    [SerializeField] private float maxLaunchVertical = 18f;

    [Tooltip("Forward (flat) launch speed (world units/s).")]
    [SerializeField] private float maxLaunchHorizontal = 24f;

    [Tooltip("Require movement input at the moment of launch to apply horizontal impulse.")]
    [SerializeField] private bool requireMovementInputForHorizontal = true;

    [Header("Flight integration")]
    [Tooltip("If ON, we will NOT enter PlayerFlight after the boost. Horizontal is preserved to landing.")]
    [SerializeField] private bool lockFlight = false;

    [Header("Fall boost (no-flight path)")]
    [SerializeField] private bool           extraFallAcceleration = true;
    [Tooltip("Extra downward acceleration cap (units/s^2).")]
    [SerializeField] private float          fallAccelMax = 100f;
    [Tooltip("Seconds to reach full extra acceleration (time on the curve's X axis).")]
    [SerializeField] private float          fallAccelRampTime = 3f;
    [Tooltip("Curve mapping 0..1 → 0..1 where X is normalized time to 'Fall Accel Ramp Time'.")]
    [SerializeField] private AnimationCurve fallAccelCurve = AnimationCurve.EaseInOut(0,0, 1,1);

    [Header("Speedlines FX")]
    [Tooltip("Optional. If empty, we search the scene for a GameObject named EXACTLY 'FX_speedlines' and use its ParticleSystem.\nKeep this object parented to the PLAYER ROOT (not the camera).")]
    [SerializeField] private ParticleSystem speedlines;
    [Tooltip("Extra rotation applied after facing the camera. Use this to fix systems that 'pour down'. Try (90,0,0) or (-90,0,0).")]
    [SerializeField] private Vector3 speedlinesLookOffsetEuler = new Vector3(90f, 0f, 0f);
    [Tooltip("If ON, match the camera roll (use camera.up). If OFF, keep world-up roll.")]
    [SerializeField] private bool speedlinesMatchCameraRoll = true;

    // ── internals ──
    private float _holdTimer = 0f;
    private bool  _charging  = false;

    private MethodInfo _miEnterFlight;   // PlayerFlight.EnterFlight()
    private MethodInfo _miGetGravityDir; // GravityBody.GetEffectiveGravityDirection()

    private bool _prevCrouchHeld = false;
    private bool _prevSpaceHeld  = false;
    private bool _hadMoveDuringCharge = false;
    private bool _wasBoostJumpThisLaunch = false;

    // animator cache
    private bool _animHasParam = false;
    private RuntimeAnimatorController _cachedController = null;

    // fall-boost coroutine handle
    private System.Collections.IEnumerator _fallBoostRoutine;

    // cached transforms
    private Transform _speedlinesXform;
    private Transform _camXform;

    // flight entry locking state
    private bool _flightLockActive = false;   // we are currently locking any flight entry
    private bool _flightWasEnabled = false;   // original enabled state of PlayerFlight (for restore)

    // ── lifecycle ──
    private void Awake()
    {
        if (!playerFlight)   playerFlight   = GetComponentInParent<PlayerFlight>();
        if (!playerBody)     playerBody     = GetComponentInParent<Rigidbody>();
        if (!playerCrouch)   playerCrouch   = GetComponentInParent<PlayerCrouch>();
        if (!playerMovement) playerMovement = GetComponentInParent<PlayerMovement>();
        if (!playerJump)     playerJump     = GetComponentInParent<PlayerJump>();

        // Find a camera (for base FOV only)
        if (!playerCam)
        {
            var rig = GetComponentInParent<PlayerCamera>(true);
            if (rig)
            {
                var camInRig = rig.GetComponentInChildren<Camera>(true);
                if (camInRig) playerCam = camInRig;
            }
            if (!playerCam && Camera.main) playerCam = Camera.main;
        }

        // Bind a gravity provider method, if any
        if (!gravityBody) gravityBody = GetComponentInParent<GravityBody>();
        if (gravityBody)
        {
            _miGetGravityDir = gravityBody.GetType().GetMethod(
                "GetEffectiveGravityDirection",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        }

        // Bind PlayerFlight.EnterFlight() if present
        if (playerFlight)
        {
            _miEnterFlight = playerFlight.GetType().GetMethod(
                "EnterFlight",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
        }

        // Auto-bind CameraBoostFX (works with CameraController holder)
        TryBindCameraBoostFx();

        // Bind speedlines + camera transform
        EnsureSpeedlinesBound();
        EnsureCameraTransform();

        EnsureAnimator();
        SetBoostAnim(false);
    }

    private void OnEnable()
    {
        if (!cameraBoostFx) TryBindCameraBoostFx();
        EnsureSpeedlinesBound();
        EnsureCameraTransform();
    }

    private void OnDisable()
{
    _charging  = false;
    _holdTimer = 0f;
    _hadMoveDuringCharge = false;

    if (playerMovement)
    {
        playerMovement.CancelExternalHorizontalHold();
        playerMovement.SetExternalStopMovement(false);
    }
    if (playerJump) playerJump.SetJumpSuppressed(false);

    // Always ensure flight gets unlocked/restored if this component disables mid-boost
    LockFlightEntry(false);

    SetBoostAnim(false);
    cameraBoostFx?.OnChargeCancel();

    StopSpeedlines();
}

    private void Update()
    {
        // If already flying, cancel any charge
        if (playerFlight && playerFlight.IsFlying)
            CancelCharge();

        bool grounded   = playerMovement ? playerMovement.IsGrounded() : IsGrounded();
        bool crouchHeld = playerCrouch && playerCrouch.IsCrouching; // Shift
        bool spaceHeld  = Input.GetKey(triggerKey);

        // Combo to start charging: crouch + space on ground
        bool wantsCharge = grounded && crouchHeld && spaceHeld;

        if (!_charging)
        {
            if (wantsCharge)
                StartCharge(crouchHeld, spaceHeld);
        }
        else
        {
            bool hasMove = playerMovement
                ? playerMovement.HasMovementInput()
                : (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f);
            _hadMoveDuringCharge |= hasMove;

            bool lostGround = !grounded;

            // Edges (based on previous frame)
            bool releasedCrouchThisFrame = _prevCrouchHeld && !crouchHeld; // SHIFT up => cancel
            bool releasedSpaceThisFrame  = _prevSpaceHeld  && !spaceHeld;  // SPACE up => maybe launch

            if (releasedCrouchThisFrame)
            {
                CancelCharge();
            }
            else if (releasedSpaceThisFrame)
            {
                if (_holdTimer >= chargeTimeSeconds) Launch();
                else                                  CancelCharge();
            }
            else if (lostGround)
            {
                CancelCharge(); // no auto-launch
            }
        }

        _prevCrouchHeld = crouchHeld;
        _prevSpaceHeld  = spaceHeld;
    }

    private void FixedUpdate()
    {
        if (!_charging) return;

        _holdTimer += Time.fixedDeltaTime;

        // While charging: HARD LOCK horizontal every physics step
        if (playerBody)
        {
            Vector3 up   = GetUp();
            Vector3 v    = playerBody.velocity;
            Vector3 vert = Vector3.Project(v, up);
            playerBody.velocity = vert; // kill horizontal each step
        }

        // Feed charge progress to camera FX (0..1)
        if (cameraBoostFx)
        {
            float t = chargeTimeSeconds <= 0f ? 1f : Mathf.Clamp01(_holdTimer / chargeTimeSeconds);
            cameraBoostFx.OnChargeProgress(t);
        }
    }

    private void LateUpdate()
    {
        // Rebind camera transform if needed (handles camera swaps)
        if (_camXform == null) EnsureCameraTransform();

        // Align speedlines to the camera view each frame
        AlignSpeedlinesTowardCamera();
    }

    private void Launch()
    {
        _charging = false;
        _wasBoostJumpThisLaunch = false;

        Vector3 horiz = Vector3.zero;

        if (playerBody)
        {
            Vector3 up = GetUp();

            // single-strength values
            float vMag = Mathf.Max(0f, maxLaunchVertical);
            float hMag = Mathf.Max(0f, maxLaunchHorizontal);

            // Always apply vertical
            Vector3 newVel = up * vMag;

            // Horizontal direction uses **current** input at launch; never flipped.
            Vector3 moveDir = Vector3.zero;
            if (playerMovement != null)
                moveDir = playerMovement.GetMoveDirection();
            else
            {
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                moveDir = new Vector3(h, 0f, v);
            }

            moveDir = Vector3.ProjectOnPlane(moveDir, up);
            bool hasMoveNow = moveDir.sqrMagnitude > 0.0001f;

            Vector3 horizDir;
            if (requireMovementInputForHorizontal)
            {
                horizDir = hasMoveNow ? moveDir.normalized : Vector3.zero;
            }
            else
            {
                // prefer current input, otherwise fallback to player forward
                Vector3 fallback = Vector3.ProjectOnPlane(transform.forward, up);
                horizDir = (hasMoveNow ? moveDir : fallback).normalized;
            }

            if (horizDir.sqrMagnitude > 0.0001f && hMag > 0.001f)
            {
                horiz = horizDir * hMag;
                newVel += horiz;
            }

            playerBody.velocity = newVel;

            // keep horizontal from being overwritten
            if (playerMovement && horiz.sqrMagnitude > 0.0001f)
            {
                playerMovement.HoldExternalHorizontal(horiz, 15f);
                _wasBoostJumpThisLaunch = true;
            }

            cameraBoostFx?.OnLaunch(up);
        }

        if (playerMovement) playerMovement.SetExternalStopMovement(false);
        if (playerMovement) playerMovement.NotifyJumped();

        SetBoostAnim(_wasBoostJumpThisLaunch);

        // Start the visual FX
        PlaySpeedlines();

        // Keep jump/dash suppressed until boost arc finishes
        StartCoroutine(BoostApexAndAfter());
    }

    // ── actions ──

    private void StartCharge(bool crouchHeldNow, bool spaceHeldNow)
{
    _charging  = true;
    _holdTimer = 0f;
    _hadMoveDuringCharge = false;

    // capture edges
    _prevCrouchHeld = crouchHeldNow;
    _prevSpaceHeld  = spaceHeldNow;

    // zero horizontal while charging
    if (playerBody)
    {
        Vector3 up   = GetUp();
        Vector3 vert = Vector3.Project(playerBody.velocity, up);
        playerBody.velocity = vert;
    }

    // Hard stop movement & suppress jump/dash for the whole boost window
    if (playerMovement) playerMovement.SetExternalStopMovement(true);
    SuppressJump(true);
    var dash = GetComponentInParent<PlayerDash>();
    dash?.SetDashSuppressed(true);

    // 🔒 lock any flight entry for the entire jump window (even if lockFlight=false)
    LockFlightEntry(true);

    // FX
    cameraBoostFx?.OnChargeProgress(0f);
}

    private void CancelCharge()
{
    if (!_charging) return;

    _charging  = false;
    _holdTimer = 0f;
    _hadMoveDuringCharge = false;

    if (playerMovement) playerMovement.SetExternalStopMovement(false);

    // lift gates (no launch happened)
    SuppressJump(false);
    var dash = GetComponentInParent<PlayerDash>();
    dash?.SetDashSuppressed(false);

    // 🔓 unlock flight lock (we’re back on ground)
    LockFlightEntry(false);

    SetBoostAnim(false);
    cameraBoostFx?.OnChargeCancel();

    StopSpeedlines();
}

    private System.Collections.IEnumerator BoostApexAndAfter()
{
    const float timeout = 4f;
    float t = 0f;
    bool wentUp = false;

    while (t < timeout)
    {
        yield return null;
        t += Time.deltaTime;

        if (!playerBody) break;

        Vector3 up = GetUp();
        float vUp = Vector3.Dot(playerBody.velocity, up);
        if (vUp > 0.5f) wentUp = true;      // rising
        if (wentUp && vUp <= 0.05f) break;  // reached apex
    }

    // We've reached the apex
    cameraBoostFx?.OnApex(GetUp());

    if (!lockFlight && playerFlight != null)
    {
        // We kept flight entry locked the whole jump; now, at halfway (apex),
        // FORCE entry into flight and remove the lock just-in-time.
        if (playerMovement) playerMovement.CancelExternalHorizontalHold();

        // 🔓 unlock just before we request entry
        LockFlightEntry(false);

        TryEnterFlight(); // <— force it now (halfway)

        SetBoostAnim(false);

        cameraBoostFx?.OnLand();
        StopSpeedlines(); // entering flight ends the jump FX
    }
    else
    {
        // lockFlight = true → keep flight locked the entire airborne path until landing
        if (_fallBoostRoutine != null) StopCoroutine(_fallBoostRoutine);
        _fallBoostRoutine = NoFlightFallBoostUntilGrounded();
        yield return StartCoroutine(_fallBoostRoutine);

        if (playerMovement) playerMovement.CancelExternalHorizontalHold();
        SetBoostAnim(false);

        cameraBoostFx?.OnLand();
        StopSpeedlines();

        // 🔓 now that we’ve landed, release the flight lock
        LockFlightEntry(false);
    }

    // lift jump/dash suppression (boost phase is over)
    SuppressJump(false);
    var dash = GetComponentInParent<PlayerDash>();
    dash?.SetDashSuppressed(false);
}

    private System.Collections.IEnumerator NoFlightFallBoostUntilGrounded()
    {
        float elapsed = 0f;

        while (true)
        {
            // break on ground
            bool grounded = playerMovement ? playerMovement.IsGrounded() : IsGrounded();
            if (grounded) yield break;

            if (playerBody)
            {
                Vector3 up = GetUp();

                if (extraFallAcceleration && fallAccelMax > 0f)
                {
                    float normT = (fallAccelRampTime <= 0f) ? 1f : Mathf.Clamp01(elapsed / fallAccelRampTime);
                    float curve = fallAccelCurve != null ? Mathf.Clamp01(fallAccelCurve.Evaluate(normT)) : normT;

                    float accel = fallAccelMax * curve;
                    // extra downward accel
                    playerBody.AddForce(-up * accel * Time.deltaTime, ForceMode.Acceleration);
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // ── helpers ──

    private void TryBindCameraBoostFx()
    {
        if (cameraBoostFx && cameraBoostFx.isActiveAndEnabled)
        {
            if (playerCam) cameraBoostFx.SetBaseFov(playerCam.fieldOfView);
            if (debugFxBinding) Debug.Log($"[BoostJump] Using assigned CameraBoostFX on '{cameraBoostFx.gameObject.name}'.", this);
            return;
        }

        CameraBoostFX fx = null;

        // 1) Prefer the PlayerCamera rig (e.g., your CameraController)
        var rig = GetComponentInParent<PlayerCamera>(true);
        if (!rig) rig = FindObjectOfType<PlayerCamera>(true);
        if (rig) fx = rig.GetComponent<CameraBoostFX>();

        // 2) If we have a camera, try near it (parent/children)
        if (!fx && playerCam)
        {
            fx = playerCam.GetComponent<CameraBoostFX>();
            if (!fx) fx = playerCam.GetComponentInParent<CameraBoostFX>(true);
            if (!fx) fx = playerCam.GetComponentInChildren<CameraBoostFX>(true);
        }

        // 3) Last resort: any in scene (pick the closest to us)
        if (!fx)
        {
            var all = FindObjectsOfType<CameraBoostFX>(true);
            float best = float.PositiveInfinity;
            foreach (var cand in all)
            {
                float d = (cand.transform.position - transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    fx = cand;
                }
            }
        }

        cameraBoostFx = fx;

        if (cameraBoostFx)
        {
            if (!playerCam)
            {
                // try to find any camera under the same rig/fx, else fallback to main
                var camNearFx = cameraBoostFx.GetComponentInChildren<Camera>(true);
                if (!camNearFx) camNearFx = cameraBoostFx.GetComponentInParent<Camera>(true);
                if (!camNearFx) camNearFx = Camera.main;
                playerCam = camNearFx ? camNearFx : playerCam;
            }

            if (playerCam) cameraBoostFx.SetBaseFov(playerCam.fieldOfView);

            if (debugFxBinding)
                Debug.Log($"[BoostJump] Bound CameraBoostFX '{cameraBoostFx.gameObject.name}' (cam: '{(playerCam?playerCam.name:"none")}').", this);
        }
        else if (debugFxBinding)
        {
            Debug.LogWarning("[BoostJump] Could not find CameraBoostFX in scene. Camera effects will be disabled.", this);
        }
    }

    private void EnsureCameraTransform()
    {
        if (playerCam != null) _camXform = playerCam.transform;
        else if (Camera.main != null) _camXform = Camera.main.transform;
    }

    private void EnsureSpeedlinesBound()
    {
        if (speedlines == null)
        {
            var go = GameObject.Find("FX_speedlines");
            if (go != null)
            {
                speedlines = go.GetComponent<ParticleSystem>();
                if (speedlines == null && debugFxBinding)
                    Debug.LogWarning("[BoostJump] Found FX_speedlines but it has no ParticleSystem component.", this);
            }
            else if (debugFxBinding)
            {
                Debug.LogWarning("[BoostJump] Could not find a GameObject named 'FX_speedlines' in the scene.", this);
            }
        }

        _speedlinesXform = speedlines ? speedlines.transform : null;
    }

    private void AlignSpeedlinesTowardCamera()
    {
        if (_speedlinesXform == null || _camXform == null) return;

        // Face the camera: forward points from speedlines to camera.
        // Choose up vector: camera.up (match camera roll) or world up.
        Vector3 toCam = _camXform.position - _speedlinesXform.position;
        if (toCam.sqrMagnitude < 1e-6f) return;

        Vector3 upRef = speedlinesMatchCameraRoll ? _camXform.up : Vector3.up;

        Quaternion look = Quaternion.LookRotation(toCam.normalized, upRef);
        // apply user-tweakable offset AFTER look-at to correct for system's emission axis
        look *= Quaternion.Euler(speedlinesLookOffsetEuler);

        _speedlinesXform.rotation = look;
    }

    private void PlaySpeedlines()
    {
        EnsureSpeedlinesBound();
        EnsureCameraTransform();
        if (speedlines == null) return;

        if (!speedlines.isPlaying) speedlines.Play(true);
    }

    private void StopSpeedlines()
    {
        if (speedlines == null) return;
        speedlines.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void SuppressJump(bool on)
    {
        if (playerJump) playerJump.SetJumpSuppressed(on);
    }

    private bool IsGrounded()
    {
        if (playerMovement) return playerMovement.IsGrounded();

        if (!playerBody) return false;
        Vector3 up   = GetUp();
        Vector3 orig = playerBody.position + up * 0.1f;
        Vector3 dir  = -up;

        float radius = 0.25f;
        float distance = 0.55f;
        LayerMask groundMask = ~0;

        if (Physics.SphereCast(orig, radius, dir, out RaycastHit hit, distance,
                               groundMask, QueryTriggerInteraction.Ignore))
        {
            float dot = Vector3.Dot(hit.normal.normalized, up);
            return dot >= 0.5f;
        }
        return false;
    }

    private Vector3 GetUp()
    {
        if (gravityBody != null && _miGetGravityDir != null)
        {
            try
            {
                var g = (Vector3)_miGetGravityDir.Invoke(gravityBody, null);
                if (g.sqrMagnitude > 0.0001f) return (-g).normalized;
            }
            catch { }
        }
        return Vector3.up;
    }

    private void TryEnterFlight()
    {
        if (playerFlight == null) return;

        try
        {
            if (_miEnterFlight != null)
                _miEnterFlight.Invoke(playerFlight, null);
        }
        catch { /* ignore */ }
    }

    // Animator utilities
    private void EnsureAnimator()
    {
        if (AnimatorHasBool(characterAnimator, isBoostJumpParam))
        {
            _animHasParam = true;
            _cachedController = characterAnimator.runtimeAnimatorController;
            return;
        }

        foreach (var a in GetComponentsInParent<Animator>(true))
        {
            if (AnimatorHasBool(a, isBoostJumpParam))
            {
                characterAnimator = a;
                _animHasParam = true;
                _cachedController = a.runtimeAnimatorController;
                if (animatorDebugLogs) Debug.Log($"[BoostJump] Bound Animator '{a.name}' with bool '{isBoostJumpParam}'.", this);
                return;
            }
        }

        foreach (var a in GetComponentsInChildren<Animator>(true))
        {
            if (AnimatorHasBool(a, isBoostJumpParam))
            {
                characterAnimator = a;
                _animHasParam = true;
                _cachedController = a.runtimeAnimatorController;
                if (animatorDebugLogs) Debug.Log($"[BoostJump] Bound Animator '{a.name}' with bool '{isBoostJumpParam}'.", this);
                return;
            }
        }

        _animHasParam = false;
        _cachedController = null;
        if (animatorDebugLogs) Debug.LogWarning($"[BoostJump] Could not find an Animator with bool '{isBoostJumpParam}'. Assign one on {name}.", this);
    }

    private static bool AnimatorHasBool(Animator a, string paramName)
    {
        if (!a || a.runtimeAnimatorController == null) return false;
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].type == AnimatorControllerParameterType.Bool && ps[i].name == paramName)
                return true;
        return false;
    }

    private void SetBoostAnim(bool on)
    {
        if (characterAnimator == null ||
            characterAnimator.runtimeAnimatorController != _cachedController ||
            !_animHasParam)
        {
            EnsureAnimator();
        }

        if (!_animHasParam) return;

        characterAnimator.SetBool(isBoostJumpParam, on);

        if (animatorDebugLogs)
            Debug.Log($"[BoostJump] Set '{isBoostJumpParam}' = {on} on Animator '{characterAnimator.name}'.", this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        chargeTimeSeconds    = Mathf.Max(0f, chargeTimeSeconds);
        maxLaunchVertical    = Mathf.Max(0f, maxLaunchVertical);
        maxLaunchHorizontal  = Mathf.Max(0f, maxLaunchHorizontal);

        fallAccelMax         = Mathf.Max(0f, fallAccelMax);
        fallAccelRampTime    = Mathf.Max(0f, fallAccelRampTime);
    }

    /// <summary>
/// Prevents PlayerFlight from being entered while 'on' is true.
/// Tries a reflective "SetFlightSuppressed(bool)" or "SetEntrySuppressed(bool)" first.
/// If none exist, temporarily disables the PlayerFlight component and restores it later.
/// </summary>
private void LockFlightEntry(bool on)
{
    if (playerFlight == null)
    {
        _flightLockActive = false;
        return;
    }

    // Try reflective suppressor first (non-alloc once would be nicer, but this is safe)
    var t = playerFlight.GetType();
    var setSupp = t.GetMethod("SetFlightSuppressed",
                              BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    var setEntrySupp = t.GetMethod("SetEntrySuppressed",
                                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    if (setSupp != null)
    {
        setSupp.Invoke(playerFlight, new object[] { on });
        _flightLockActive = on;
        return;
    }
    if (setEntrySupp != null)
    {
        setEntrySupp.Invoke(playerFlight, new object[] { on });
        _flightLockActive = on;
        return;
    }

    // Fallback: toggle component enabled state
    if (on)
    {
        if (!_flightLockActive) // only capture once
            _flightWasEnabled = playerFlight.enabled;

        if (playerFlight.enabled)
            playerFlight.enabled = false;

        _flightLockActive = true;
    }
    else
    {
        if (_flightLockActive)
        {
            // restore to original enabled state
            if (playerFlight.enabled != _flightWasEnabled)
                playerFlight.enabled = _flightWasEnabled;
        }
        _flightLockActive = false;
    }
}
#endif
}