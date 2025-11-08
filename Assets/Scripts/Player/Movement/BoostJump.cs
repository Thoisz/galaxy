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
    [SerializeField] private float maxLaunchHorizontal = 40f;

    [Tooltip("Require movement input at the moment of launch to apply horizontal impulse.")]
    [SerializeField] private bool requireMovementInputForHorizontal = true;

    [Tooltip("Add a slice of vertical speed into the horizontal if input exists.")]
    [SerializeField, Range(0f, 1.5f)] private float forwardBoostFromVertical = 0.35f;

    [Header("Ground detach assist")]
    [SerializeField] private float preLaunchLift = 0.06f;     // small upward nudge before applying velocity
    [SerializeField] private float ungroundGrace = 0.18f;     // time window to force ungrounded after launch

    [Header("Flight integration")]
    [Tooltip("If ON, we will NOT enter PlayerFlight after the boost. Horizontal is preserved to landing.")]
    [SerializeField] private bool lockFlight = true;

    [Header("Fall boost (no-flight path)")]
    [SerializeField] private bool           extraFallAcceleration = true;
    [Tooltip("Extra downward acceleration cap (units/s^2).")]
    [SerializeField] private float          fallAccelMax = 100f;
    [Tooltip("Seconds to reach full extra acceleration (time on the curve's X axis).")]
    [SerializeField] private float          fallAccelRampTime = 3f;
    [Tooltip("Curve mapping 0..1 → 0..1 where X is normalized time to 'Fall Accel Ramp Time'.")]
    [SerializeField] private AnimationCurve fallAccelCurve = AnimationCurve.EaseInOut(0,0, 1,1);

    [Header("Speedlines FX")]
    [Tooltip("Optional. If empty, we search the scene for a GameObject named EXACTLY 'FX_speedlines' and use its ParticleSystem.")]
    [SerializeField] private ParticleSystem speedlines;
    [SerializeField] private Vector3 speedlinesLookOffsetEuler = new Vector3(90f, 0f, 0f);
    [SerializeField] private bool speedlinesMatchCameraRoll = true;

    [Header("Groundbreak FX")]
    [Tooltip("Optional. If empty, we search the scene for a prefab named EXACTLY 'FX_groundbreak' via Resources or in-scene.")]
    [SerializeField] private GameObject fxGroundbreakPrefab;
    [SerializeField] private bool tryResourcesLoad = true;

    [SerializeField] private JetpackRare jetpackRare; // auto-found in Awake if left empty
    private bool _chargedFlashOn = false;             // internal toggle state

    // ── internals ──
    private float _holdTimer = 0f;
    private bool  _charging  = false;

    private MethodInfo _miEnterFlight;   // PlayerFlight.EnterFlight()
    private MethodInfo _miGetGravityDir; // GravityBody.GetEffectiveGravityDirection()

    private bool _prevCrouchHeld = false;
    private bool _prevSpaceHeld  = false;

    // animator cache
    private bool _animHasParam = false;
    private RuntimeAnimatorController _cachedController = null;

    // fall-boost coroutine handle
    private System.Collections.IEnumerator _fallBoostRoutine;

    // cached transforms
    private Transform _speedlinesXform;
    private Transform _camXform;

    // flight entry locking state
    private bool _flightLockActive = false;
    private bool _flightWasEnabled = false;

    // speedlines request
    private bool _speedlinesRequested = false;

    // Grounding grace
    private float _forceUngroundedUntil = 0f;

    // LATCHED INPUT: stores raw camera-planar input while charging (ignores external stop)
    private Vector3 _latchedHorizDirWS = Vector3.zero;

    // ── lifecycle ──
    private void Awake()
    {
        if (!playerFlight)   playerFlight   = GetComponentInParent<PlayerFlight>();
        if (!playerBody)     playerBody     = GetComponentInParent<Rigidbody>();
        if (!playerCrouch)   playerCrouch   = GetComponentInParent<PlayerCrouch>();
        if (!playerMovement) playerMovement = GetComponentInParent<PlayerMovement>();
        if (!playerJump)     playerJump     = GetComponentInParent<PlayerJump>();

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

        if (!gravityBody) gravityBody = GetComponentInParent<GravityBody>();
        if (gravityBody)
        {
            _miGetGravityDir = gravityBody.GetType().GetMethod(
                "GetEffectiveGravityDirection",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        }

        if (playerFlight)
        {
            _miEnterFlight = playerFlight.GetType().GetMethod(
                "EnterFlight",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
        }

        if (!jetpackRare) jetpackRare = GetComponentInParent<JetpackRare>(true);
        jetpackRare?.SetChargedFlash(false);

        TryBindCameraBoostFx();

        EnsureSpeedlinesBound();
        EnsureCameraTransform();

        EnsureAnimator();
        SetBoostAnim(false);

        EnsureGroundbreakPrefabBound();
    }

    private void OnEnable()
    {
        if (!cameraBoostFx) TryBindCameraBoostFx();
        EnsureSpeedlinesBound();
        EnsureCameraTransform();
        EnsureGroundbreakPrefabBound();

        if (!jetpackRare) jetpackRare = GetComponentInParent<JetpackRare>();
    }

    private void OnDisable()
    {
        _chargedFlashOn = false;
        jetpackRare?.SetChargedFlash(false);
        jetpackRare?.SetEnergyBallMeshesVisible(false);

        _charging  = false;
        _holdTimer = 0f;
        _latchedHorizDirWS = Vector3.zero;

        if (playerMovement)
        {
            playerMovement.CancelExternalHorizontalHold();
            playerMovement.SetExternalStopMovement(false);
        }
        if (playerJump) playerJump.SetJumpSuppressed(false);

        LockFlightEntry(false);

        SetBoostAnim(false);
        cameraBoostFx?.OnChargeCancel();

        _speedlinesRequested = false;
        StopSpeedlines();
    }

    private void Update()
    {
        if (playerFlight && playerFlight.IsFlying)
            CancelCharge();

        bool grounded   = playerMovement ? playerMovement.IsGrounded() : IsGrounded();
        bool crouchHeld = playerCrouch && playerCrouch.IsCrouching;
        bool spaceHeld  = Input.GetKey(triggerKey);

        bool wantsCharge = grounded && crouchHeld && spaceHeld;

        if (!_charging)
        {
            if (wantsCharge) StartCharge(crouchHeld, spaceHeld);
        }
        else
        {
            // LATCH raw input every frame while charging (even though we externally stop movement)
            Vector3 up = GetUp();
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 camF = playerCam ? Vector3.ProjectOnPlane(playerCam.transform.forward, up).normalized : Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 camR = playerCam ? Vector3.ProjectOnPlane(playerCam.transform.right,   up).normalized : Vector3.Cross(up, camF).normalized;
            Vector3 rawDir = (camF * v + camR * h);
            rawDir = Vector3.ProjectOnPlane(rawDir, up);
            if (rawDir.sqrMagnitude > 0.0001f) _latchedHorizDirWS = rawDir.normalized;

            bool releasedCrouchThisFrame = _prevCrouchHeld && !crouchHeld;
            bool releasedSpaceThisFrame  = _prevSpaceHeld  && !spaceHeld;

            if (releasedCrouchThisFrame)
                CancelCharge();
            else if (releasedSpaceThisFrame)
            {
                if (_holdTimer >= chargeTimeSeconds) Launch();
                else                                  CancelCharge();
            }
            else if (!grounded)
            {
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

        if (playerBody)
        {
            Vector3 up   = GetUp();
            Vector3 v    = playerBody.velocity;
            Vector3 vert = Vector3.Project(v, up);
            playerBody.velocity = vert; // freeze horizontal while charging
        }

        // normalized charge 0..1
        float t = chargeTimeSeconds <= 0f ? 1f : Mathf.Clamp01(_holdTimer / chargeTimeSeconds);

        // update camera FX
        if (cameraBoostFx) cameraBoostFx.OnChargeProgress(t);

        // fully charged?
        bool nowFull = (t >= 1f - 1e-4f);
        if (nowFull != _chargedFlashOn)
        {
            _chargedFlashOn = nowFull;

            jetpackRare?.SetChargedFlash(_chargedFlashOn);
            jetpackRare?.SetChargeDustVisible(!_chargedFlashOn);
        }
    }

    private void LateUpdate()
    {
        if (_camXform == null) EnsureCameraTransform();
        AlignSpeedlinesTowardCamera();
    }

    // ─────────────────────────────────────────────────────
    // Start/Cancel charge (missing earlier)
    // ─────────────────────────────────────────────────────
    private void StartCharge(bool crouchHeldNow, bool spaceHeldNow)
    {
        _charging  = true;
        _holdTimer = 0f;
        _latchedHorizDirWS = Vector3.zero;

        _prevCrouchHeld = crouchHeldNow;
        _prevSpaceHeld  = spaceHeldNow;

        if (playerBody)
        {
            Vector3 up   = GetUp();
            Vector3 vert = Vector3.Project(playerBody.velocity, up);
            playerBody.velocity = vert; // kill horizontal while charging
        }

        if (playerMovement) playerMovement.SetExternalStopMovement(true);
        SuppressJump(true);
        var dash = GetComponentInParent<PlayerDash>();
        dash?.SetDashSuppressed(true);

        LockFlightEntry(true);

        _chargedFlashOn = false;
        jetpackRare?.SetChargedFlash(false);

        cameraBoostFx?.OnChargeProgress(0f);

        // show energyball + DUST while charging
        jetpackRare?.SetEnergyBallMeshesVisible(true);
        jetpackRare?.SetChargeDustVisible(true);
    }

    private void CancelCharge()
    {
        _chargedFlashOn = false;
        jetpackRare?.SetChargedFlash(false);

        // hide orb + DUST when not charging
        jetpackRare?.SetEnergyBallMeshesVisible(false);
        jetpackRare?.SetChargeDustVisible(false);

        if (!_charging) return;

        _charging  = false;
        _holdTimer = 0f;
        _latchedHorizDirWS = Vector3.zero;

        if (playerMovement) playerMovement.SetExternalStopMovement(false);

        SuppressJump(false);
        var dash = GetComponentInParent<PlayerDash>();
        dash?.SetDashSuppressed(false);

        LockFlightEntry(false);

        SetBoostAnim(false);
        cameraBoostFx?.OnChargeCancel();

        _speedlinesRequested = false;
        StopSpeedlines();
    }

    // ─────────────────────────────────────────────────────
    // Launch
    // ─────────────────────────────────────────────────────
    private void Launch()
    {
        _charging = false;

        _chargedFlashOn = false;
        jetpackRare?.SetChargedFlash(false);

        // hide orb + DUST when launching
        jetpackRare?.SetEnergyBallMeshesVisible(false);
        jetpackRare?.SetChargeDustVisible(false);

        Vector3 horiz = Vector3.zero;

        if (playerBody)
        {
            Vector3 up = GetUp();

            float vMag = Mathf.Max(0f, maxLaunchVertical);
            float hMag = Mathf.Max(0f, maxLaunchHorizontal);

            // anti-stick: pop up a hair and clear down-velocity
            PreLaunchSeparation(preLaunchLift);
            _forceUngroundedUntil = Time.time + Mathf.Max(0.02f, ungroundGrace);

            Vector3 newVel = up * vMag;

            // Build a movement dir from *current* input; if none, use LATCHED
            Vector3 moveDir;
            {
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");

                if (playerCam)
                {
                    Vector3 fwd = Vector3.ProjectOnPlane(playerCam.transform.forward, up).normalized;
                    Vector3 right = Vector3.ProjectOnPlane(playerCam.transform.right,  up).normalized;
                    moveDir = (fwd * v + right * h);
                }
                else
                {
                    moveDir = new Vector3(h, 0f, v);
                }
                moveDir = Vector3.ProjectOnPlane(moveDir, up);
                if (moveDir.sqrMagnitude < 0.0001f && _latchedHorizDirWS.sqrMagnitude > 0.0001f)
                    moveDir = _latchedHorizDirWS; // ← use latched if current is zero
            }

            bool hasMoveNow = moveDir.sqrMagnitude > 0.0001f;

            Vector3 horizDir;
            if (requireMovementInputForHorizontal)
                horizDir = hasMoveNow ? moveDir.normalized : Vector3.zero;
            else
            {
                Vector3 fallback = Vector3.ProjectOnPlane(transform.forward, up);
                horizDir = (hasMoveNow ? moveDir : fallback).normalized;
            }

            if (horizDir.sqrMagnitude > 0.0001f && hMag > 0.001f)
            {
                float boostedH = hMag + vMag * Mathf.Max(0f, forwardBoostFromVertical);
                horiz = horizDir * boostedH;
                newVel += horiz;
            }

            // apply in one shot so no other script steals our impulse this frame
            playerBody.velocity = newVel;

            // preserve that horizontal for a while so movement doesn’t immediately damp it
            if (playerMovement && horiz.sqrMagnitude > 0.0001f)
                playerMovement.HoldExternalHorizontal(horiz, 15f);

            cameraBoostFx?.OnLaunch(up);
            SpawnGroundbreak();
        }

        if (playerMovement) playerMovement.SetExternalStopMovement(false);
        if (playerMovement) playerMovement.NotifyJumped();

        SetBoostAnim(true); // keep true until we land in coroutine below

        _speedlinesRequested = true;
        PlaySpeedlines();

        StartCoroutine(BoostApexAndAfter());
    }

    private void PreLaunchSeparation(float lift)
    {
        if (!playerBody) return;
        Vector3 up = GetUp();

        // nudge up a bit, and remove any downward velocity
        playerBody.position += up * Mathf.Max(0f, lift);
        Vector3 v = playerBody.velocity;
        float vDown = Vector3.Dot(v, -up);
        if (vDown > 0f) playerBody.velocity = v + up * vDown;
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
            if (vUp > 0.5f) wentUp = true;
            if (wentUp && vUp <= 0.05f) break;
        }

        cameraBoostFx?.OnApex(GetUp());

        if (!lockFlight && playerFlight != null)
        {
            if (playerMovement) playerMovement.CancelExternalHorizontalHold();

            LockFlightEntry(false);
            TryEnterFlight();

            SetBoostAnim(false);
            cameraBoostFx?.OnLand();

            _speedlinesRequested = false;
            StopSpeedlines();
        }
        else
        {
            if (_fallBoostRoutine != null) StopCoroutine(_fallBoostRoutine);
            _fallBoostRoutine = NoFlightFallBoostUntilGrounded();
            yield return StartCoroutine(_fallBoostRoutine);

            if (playerMovement) playerMovement.CancelExternalHorizontalHold();
            SetBoostAnim(false);

            cameraBoostFx?.OnLand();

            _speedlinesRequested = false;
            StopSpeedlines();

            LockFlightEntry(false);

            SpawnGroundbreak();
        }

        if (playerJump) playerJump.SetJumpSuppressed(false);
        var dash = GetComponentInParent<PlayerDash>();
        dash?.SetDashSuppressed(false);
    }

    private System.Collections.IEnumerator NoFlightFallBoostUntilGrounded()
    {
        float elapsed = 0f;

        while (true)
        {
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
                    playerBody.AddForce(-up * accel * Time.deltaTime, ForceMode.Acceleration);
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // ── Groundbreak spawn + material handoff ──

    void EnsureGroundbreakPrefabBound()
    {
        if (fxGroundbreakPrefab != null) return;

        // Try Find in-scene first (disabled templates)
        var go = GameObject.Find("FX_groundbreak");
        if (go != null && go.scene.IsValid())
        {
            fxGroundbreakPrefab = go; // will instantiate this scene object (acts like a prefab)
            return;
        }

        if (tryResourcesLoad)
        {
            var res = Resources.Load<GameObject>("FX_groundbreak");
            if (res != null) fxGroundbreakPrefab = res;
        }
    }

    void SpawnGroundbreak()
    {
        if (fxGroundbreakPrefab == null || playerBody == null) return;

        Vector3 up  = GetUp();

        // Safety: only spawn if there is ground reasonably close below us.
        const float maxRayDist = 20f;
        if (!Physics.Raycast(playerBody.position + up * 0.1f, -up, out var hit, maxRayDist, ~0, QueryTriggerInteraction.Ignore))
            return; // no ground under us → don't spawn

        Vector3 pos = hit.point;
        Quaternion rot = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized + 0.0001f * Vector3.forward,
            hit.normal
        );

        var fx = Instantiate(fxGroundbreakPrefab, pos, rot);

        var root = fx.GetComponent<GroundbreakFXRoot>();
        if (root != null)
        {
            root.ConfigureFromContact(playerBody.position, up);
        }
        else
        {
            fx.transform.position = pos;
            fx.transform.rotation = rot;
        }
    }

    // ── helpers (existing) ──

    private void TryBindCameraBoostFx()
    {
        if (cameraBoostFx && cameraBoostFx.isActiveAndEnabled)
        {
            if (playerCam) cameraBoostFx.SetBaseFov(playerCam.fieldOfView);
            if (debugFxBinding) Debug.Log($"[BoostJump] Using assigned CameraBoostFX on '{cameraBoostFx.gameObject.name}'.", this);
            return;
        }

        CameraBoostFX fx = null;

        var rig = GetComponentInParent<PlayerCamera>(true);
        if (!rig) rig = FindObjectOfType<PlayerCamera>(true);
        if (rig) fx = rig.GetComponent<CameraBoostFX>();

        if (!fx && playerCam)
        {
            fx = playerCam.GetComponent<CameraBoostFX>();
            if (!fx) fx = playerCam.GetComponentInParent<CameraBoostFX>(true);
            if (!fx) fx = playerCam.GetComponentInChildren<CameraBoostFX>(true);
        }

        if (!fx)
        {
            var all = FindObjectsOfType<CameraBoostFX>(true);
            float best = float.PositiveInfinity;
            foreach (var cand in all)
            {
                float d = (cand.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; fx = cand; }
            }
        }

        cameraBoostFx = fx;

        if (cameraBoostFx)
        {
            if (!playerCam)
            {
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

        // In first person: keep them off, but DO NOT clear the request flag.
        if (IsFirstPersonView())
        {
            if (speedlines != null && speedlines.isPlaying)
                speedlines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        // Back in third person: if a boost is ongoing and the effect is stopped, resume it.
        if (_speedlinesRequested && speedlines != null && !speedlines.isPlaying)
            speedlines.Play(true);

        // Normal look-at alignment
        Vector3 toCam = _camXform.position - _speedlinesXform.position;
        if (toCam.sqrMagnitude < 1e-6f) return;

        Vector3 upRef = speedlinesMatchCameraRoll ? _camXform.up : Vector3.up;

        Quaternion look = Quaternion.LookRotation(toCam.normalized, upRef);
        look *= Quaternion.Euler(speedlinesLookOffsetEuler);

        _speedlinesXform.rotation = look;
    }

    private void PlaySpeedlines()
    {
        EnsureSpeedlinesBound();
        EnsureCameraTransform();
        if (speedlines == null) return;

        // In first person we *want* them hidden but keep the request true
        if (IsFirstPersonView())
        {
            if (speedlines.isPlaying)
                speedlines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        if (!speedlines.isPlaying) speedlines.Play(true);
    }

    private void StopSpeedlines()
    {
        if (speedlines == null) return;
        speedlines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // Helper: are we currently in first-person view?
    private bool IsFirstPersonView()
    {
        var rig = GetComponentInParent<PlayerCamera>(true);
        if (!rig) rig = FindObjectOfType<PlayerCamera>(true);
        return rig != null && rig.IsInFirstPerson;
    }

    private void SuppressJump(bool on)
    {
        if (playerJump) playerJump.SetJumpSuppressed(on);
    }

    private bool IsGrounded()
    {
        if (Time.time < _forceUngroundedUntil) return false; // grace after launch

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
        if (animatorDebugLogs) Debug.LogWarning($"[BoostJump] Could not find an Animator with bool '{isBoostJumpParam}'.", this);
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
            Debug.Log($"[BoostJump] Set '{isBoostJumpParam}' = {on} on Animator '{characterAnimator?.name}'.", this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        chargeTimeSeconds    = Mathf.Max(0f, chargeTimeSeconds);
        maxLaunchVertical    = Mathf.Max(0f, maxLaunchVertical);
        maxLaunchHorizontal  = Mathf.Max(0f, maxLaunchHorizontal);
        forwardBoostFromVertical = Mathf.Clamp(forwardBoostFromVertical, 0f, 1.5f);
        preLaunchLift        = Mathf.Max(0f, preLaunchLift);
        ungroundGrace        = Mathf.Max(0f, ungroundGrace);
        fallAccelMax         = Mathf.Max(0f, fallAccelMax);
        fallAccelRampTime    = Mathf.Max(0f, fallAccelRampTime);
    }
#endif

    // Flight entry locker (unchanged)
    private void LockFlightEntry(bool on)
    {
        if (playerFlight == null)
        {
            _flightLockActive = false;
            return;
        }

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

        if (on)
        {
            if (!_flightLockActive) _flightWasEnabled = playerFlight.enabled;
            if (playerFlight.enabled) playerFlight.enabled = false;
            _flightLockActive = true;
        }
        else
        {
            if (_flightLockActive)
            {
                if (playerFlight.enabled != _flightWasEnabled)
                    playerFlight.enabled = _flightWasEnabled;
            }
            _flightLockActive = false;
        }
    }
}