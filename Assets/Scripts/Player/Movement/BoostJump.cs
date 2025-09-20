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
    [Tooltip("Nothing happens unless you hold at least this long.")]
    [SerializeField] private float minChargeTimeSeconds = 0.10f;
    [Tooltip("Time to ramp from min → max launch velocity.")]
    [SerializeField] private float chargeTimeSeconds = 1.00f;

    [Header("Vertical boost (independent)")]
    [Tooltip("Upward launch speed at minimum charge.")]
    [SerializeField] private float minLaunchVertical = 8f;
    [Tooltip("Upward launch speed at full charge.")]
    [SerializeField] private float maxLaunchVertical = 18f;

    [Header("Horizontal boost (independent)")]
    [Tooltip("Forward (flat) launch speed at minimum charge.")]
    [SerializeField] private float minLaunchHorizontal = 6f;
    [Tooltip("Forward (flat) launch speed at full charge.")]
    [SerializeField] private float maxLaunchHorizontal = 24f;
    [Tooltip("Require movement input to get the horizontal impulse.")]
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

    // ── internals ──
    private float _holdTimer = 0f;
    private bool  _charging  = false;

    private MethodInfo _miEnterFlight;   // PlayerFlight.EnterFlight()
    private MethodInfo _miGetGravityDir; // GravityBody.GetEffectiveGravityDirection()

    private bool _prevCrouchHeld = false;
    private bool _prevSpaceHeld  = false;
    private bool _hadMoveDuringCharge = false;     // keeps horizontal if the player moved at any time during charge
    private bool _wasBoostJumpThisLaunch = false;  // animator flag for current jump

    // animator cache
    private bool _animHasParam = false;
    private RuntimeAnimatorController _cachedController = null;

    // fall-boost coroutine handle
    private System.Collections.IEnumerator _fallBoostRoutine;

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

        EnsureAnimator(); // try binding animator at startup
        SetBoostAnim(false); // start clean
    }

    private void OnEnable()
    {
        // Rebind FX on enable (covers prefab spawn timing)
        if (!cameraBoostFx) TryBindCameraBoostFx();
    }

    private void OnDisable()
    {
        _charging  = false;
        _holdTimer = 0f;
        _hadMoveDuringCharge = false;

        if (playerMovement)
        {
            // if you added this method in PlayerMovement
            playerMovement.CancelExternalHorizontalHold();
            playerMovement.SetExternalStopMovement(false);
        }
        if (playerJump) playerJump.SetJumpSuppressed(false);

        SetBoostAnim(false);

        // tell FX we’re “not in a boost” anymore (safe no-op if null)
        cameraBoostFx?.OnChargeCancel();
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

    // Suppression is handled inside StartCharge/CancelCharge/BoostApexAndAfter

    if (!_charging)
    {
        if (wantsCharge)
            StartCharge(crouchHeld, spaceHeld);
    }
    else
    {
        // track “ever had move” during charge (optional info)
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
            // no auto-launch
            CancelCharge();
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

    // Keep jump/dash suppressed until boost arc finishes
    StartCoroutine(BoostApexAndAfter());
}

    // ── actions ──

    private void StartCharge(bool crouchHeldNow, bool spaceHeldNow)
{
    _charging  = true;
    _holdTimer = 0f;
    _hadMoveDuringCharge = false;

    // capture edge baselines
    _prevCrouchHeld = crouchHeldNow;
    _prevSpaceHeld  = spaceHeldNow;

    // Instantly zero horizontal so we stand still
    if (playerBody)
    {
        Vector3 up   = GetUp();
        Vector3 vert = Vector3.Project(playerBody.velocity, up);
        playerBody.velocity = vert; // horiz = 0
    }

    // Hard stop movement & suppress jump/dash for the whole boost window
    if (playerMovement) playerMovement.SetExternalStopMovement(true);
    SuppressJump(true);
    var dash = GetComponentInParent<PlayerDash>();
    dash?.SetDashSuppressed(true);

    // FX: start charging (progress 0)
    cameraBoostFx?.OnChargeProgress(0f);
}

    private void CancelCharge()
{
    if (!_charging) return;

    _charging  = false;
    _holdTimer = 0f;
    _hadMoveDuringCharge = false;

    if (playerMovement) playerMovement.SetExternalStopMovement(false);

    // lift blocks (we didn’t launch)
    SuppressJump(false);
    var dash = GetComponentInParent<PlayerDash>();
    dash?.SetDashSuppressed(false);

    SetBoostAnim(false);
    cameraBoostFx?.OnChargeCancel();
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

    cameraBoostFx?.OnApex(GetUp());

    if (!lockFlight && playerFlight != null)
    {
        if (playerMovement) playerMovement.CancelExternalHorizontalHold();
        TryEnterFlight();
        SetBoostAnim(false);
        cameraBoostFx?.OnLand();
    }
    else
    {
        if (_fallBoostRoutine != null) StopCoroutine(_fallBoostRoutine);
        _fallBoostRoutine = NoFlightFallBoostUntilGrounded();
        yield return StartCoroutine(_fallBoostRoutine);

        if (playerMovement) playerMovement.CancelExternalHorizontalHold();
        SetBoostAnim(false);
        cameraBoostFx?.OnLand();
    }

    // lift the gates now that the boost phase is over
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

    private void SuppressJump(bool on)
    {
        if (playerJump) playerJump.SetJumpSuppressed(on);
    }

    private bool IsGrounded()
    {
        // Prefer PlayerMovement's result if we have it
        if (playerMovement) return playerMovement.IsGrounded();

        // Fallback: simple sphere cast
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
        // If user assigned a correct animator, keep it
        if (AnimatorHasBool(characterAnimator, isBoostJumpParam))
        {
            _animHasParam = true;
            _cachedController = characterAnimator.runtimeAnimatorController;
            return;
        }

        // Try parents first (usual)
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

        // Try children (some rigs nest the animator)
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
        // Re-validate binding if controller changed at runtime
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
    // Only the full charge time matters now.
    chargeTimeSeconds = Mathf.Max(0f, chargeTimeSeconds);

    // We ignore minChargeTimeSeconds and the min launch fields.
    // Keep inspector sane by coercing mins to match maxes.
    minChargeTimeSeconds = 0f;

    maxLaunchVertical   = Mathf.Max(0f, maxLaunchVertical);
    maxLaunchHorizontal = Mathf.Max(0f, maxLaunchHorizontal);
    minLaunchVertical   = maxLaunchVertical;
    minLaunchHorizontal = maxLaunchHorizontal;

    fallAccelMax      = Mathf.Max(0f, fallAccelMax);
    fallAccelRampTime = Mathf.Max(0f, fallAccelRampTime);
}
#endif
}
