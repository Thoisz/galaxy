using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)] // run BEFORE PlayerJump so we can block the Space down-frame
public class BoostJump : MonoBehaviour
{
    // Animator parameter names (must match your Animator)
    private const string CrouchParamName = "isBoostCrouching";
    private const string BoostParamName  = "isBoostJumping";

    [Header("Auto-find (from parents)")]
    [SerializeField] private PlayerFlight   playerFlight;   // optional
    [SerializeField] private Rigidbody      playerBody;
    [SerializeField] private Camera         playerCam;      // for passing base FOV to FX
    [SerializeField] private Component      gravityBody;    // something exposing gravity direction (e.g., GravityBody)
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerJump     playerJump;     // to suppress normal jumps

    [Header("Camera FX (auto-bound at runtime)")]
    [SerializeField] private CameraBoostFX cameraBoostFx;   // optional; will be auto-found if left empty
    [SerializeField] private bool debugFxBinding = false;

    [Header("Animator (auto from PlayerMovement)")]
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private bool animatorDebugLogs = false;

    [Header("Input")]
    [SerializeField] private KeyCode crouchKey  = KeyCode.LeftShift; // held to crouch
    [SerializeField] private KeyCode triggerKey = KeyCode.Space;     // pressed with crouch to charge

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

    // LATCHED INPUT: stores raw camera-planar input while charging
    private Vector3 _latchedHorizDirWS = Vector3.zero;

    // Crouch / boost state
    private bool _isCrouching     = false;
    private bool _boostPoseActive = false;

    // Animator cached
    private int  _crouchParamHash = -1;
    private int  _boostParamHash  = -1;
    private bool _animInitialized = false;
    private bool _hasCrouchParam  = false;
    private bool _hasBoostParam   = false;

    // ─────────────────────────────────────────────────────
    // lifecycle
    // ─────────────────────────────────────────────────────
    private void Awake()
    {
        // Find the *real* player root via PlayerMovement.
        if (!playerMovement) playerMovement = GetComponentInParent<PlayerMovement>(true);

        // If there is no PlayerMovement above us, this is probably the portrait/accessory preview.
        if (!playerMovement)
        {
            if (animatorDebugLogs)
                Debug.Log("[BoostJump] No PlayerMovement found in parents; disabling BoostJump on this instance.", this);
            enabled = false;
            return;
        }

        if (!playerFlight) playerFlight = playerMovement.GetComponent<PlayerFlight>();
        if (!playerBody)   playerBody   = playerMovement.GetComponent<Rigidbody>();
        if (!playerJump)   playerJump   = playerMovement.GetComponent<PlayerJump>();

        if (!playerCam)
        {
            var rig = playerMovement.GetComponentInChildren<PlayerCamera>(true);
            if (rig)
            {
                var camInRig = rig.GetComponentInChildren<Camera>(true);
                if (camInRig) playerCam = camInRig;
            }
            if (!playerCam && Camera.main) playerCam = Camera.main;
        }

        if (!gravityBody) gravityBody = playerMovement.GetComponent<GravityBody>();
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

        // Auto-find animator from PlayerMovement root if not already set.
        if (!characterAnimator)
            characterAnimator = playerMovement.GetComponentInChildren<Animator>(true);

        InitAnimator();

        TryBindCameraBoostFx();
        EnsureSpeedlinesBound();
        EnsureCameraTransform();
        EnsureGroundbreakPrefabBound();
    }

    private void OnEnable()
    {
        if (!enabled) return;

        if (!cameraBoostFx) TryBindCameraBoostFx();
        EnsureSpeedlinesBound();
        EnsureCameraTransform();
        EnsureGroundbreakPrefabBound();

        if (!jetpackRare) jetpackRare = GetComponentInParent<JetpackRare>(true);

        if (!_animInitialized) InitAnimator();
    }

    private void OnDisable()
    {
        if (!enabled) return;

        _chargedFlashOn = false;
        jetpackRare?.SetChargedFlash(false);
        jetpackRare?.SetEnergyBallMeshesVisible(false);

        _charging        = false;
        _holdTimer       = 0f;
        _latchedHorizDirWS = Vector3.zero;
        _isCrouching     = false;
        _boostPoseActive = false;

        if (playerMovement)
        {
            playerMovement.CancelExternalHorizontalHold();
            playerMovement.SetExternalStopMovement(false);
        }
        if (playerJump) playerJump.SetJumpSuppressed(false);

        LockFlightEntry(false);

        SetCrouchAnim(false);
        SetBoostAnim(false);

        cameraBoostFx?.OnChargeCancel();

        _speedlinesRequested = false;
        StopSpeedlines();
    }

    private void Update()
    {
        if (!enabled) return;

        if (playerFlight && playerFlight.IsFlying)
            CancelCharge();

        bool grounded   = playerMovement ? playerMovement.IsGrounded() : IsGrounded();
        bool crouchHeld = Input.GetKey(crouchKey);
        bool spaceHeld  = Input.GetKey(triggerKey);

        // ───────── Crouch ─────────
        bool shouldCrouch = grounded && crouchHeld;

        if (shouldCrouch)
        {
            if (!_isCrouching)
                EnterCrouch();
        }
        else
        {
            if (_isCrouching && !_charging)
                ExitCrouch();
        }

        // ───────── Charge ─────────
        bool wantsCharge = grounded && _isCrouching && spaceHeld;

        if (!_charging)
        {
            if (wantsCharge)
                StartCharge(crouchHeld, spaceHeld);
        }
        else
        {
            // latch movement dir while charging
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
            {
                CancelCharge();
            }
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
        if (!enabled) return;

        // keep movement fully locked while crouching or charging
        if (_isCrouching || _charging)
        {
            if (playerMovement)
                playerMovement.SetExternalStopMovement(true);

            if (playerBody)
            {
                Vector3 up   = GetUp();
                Vector3 v    = playerBody.velocity;
                Vector3 vert = Vector3.Project(v, up);
                playerBody.velocity = vert; // kill horizontal
            }
        }

        if (!_charging) return;

        _holdTimer += Time.fixedDeltaTime;

        float t = chargeTimeSeconds <= 0f ? 1f : Mathf.Clamp01(_holdTimer / chargeTimeSeconds);

        if (cameraBoostFx) cameraBoostFx.OnChargeProgress(t);

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
        if (!enabled) return;

        if (_camXform == null) EnsureCameraTransform();
        AlignSpeedlinesTowardCamera();
    }

    // ─────────────────────────────────────────────────────
    // Animator helpers (no layer weight changes)
    // ─────────────────────────────────────────────────────
    private void InitAnimator()
    {
        if (!characterAnimator)
        {
            if (animatorDebugLogs)
                Debug.LogWarning("[BoostJump] No Animator found via PlayerMovement; animations will be skipped on this instance.", this);
            _animInitialized = false;
            return;
        }

        _crouchParamHash = Animator.StringToHash(CrouchParamName);
        _boostParamHash  = Animator.StringToHash(BoostParamName);

        _hasCrouchParam = HasBoolParam(characterAnimator, CrouchParamName);
        _hasBoostParam  = HasBoolParam(characterAnimator, BoostParamName);

        if (animatorDebugLogs)
        {
            Debug.Log($"[BoostJump] Animator '{characterAnimator.name}' init. hasCrouch={_hasCrouchParam}, hasBoost={_hasBoostParam}", this);
        }

        _animInitialized = _hasCrouchParam && _hasBoostParam;
    }

    private static bool HasBoolParam(Animator anim, string name)
    {
        if (!anim || anim.runtimeAnimatorController == null) return false;
        foreach (var p in anim.parameters)
            if (p.type == AnimatorControllerParameterType.Bool && p.name == name)
                return true;
        return false;
    }

    private void SetCrouchAnim(bool on)
    {
        if (!_animInitialized || !_hasCrouchParam) return;
        characterAnimator.SetBool(_crouchParamHash, on);
    }

    private void SetBoostAnim(bool on)
    {
        if (!_animInitialized || !_hasBoostParam) return;

        _boostPoseActive = on;
        characterAnimator.SetBool(_boostParamHash, on);
    }

    // ─────────────────────────────────────────────────────
    // Crouch helpers
    // ─────────────────────────────────────────────────────
    private void EnterCrouch()
    {
        _isCrouching = true;

        if (playerMovement)
            playerMovement.SetExternalStopMovement(true);

        // kill horizontal immediately
        if (playerBody)
        {
            Vector3 up = GetUp();
            Vector3 v  = playerBody.velocity;
            Vector3 vert = Vector3.Project(v, up);
            playerBody.velocity = vert;
        }

        SetCrouchAnim(true);
    }

    private void ExitCrouch()
    {
        _isCrouching = false;

        if (!_charging && playerMovement)
            playerMovement.SetExternalStopMovement(false);

        SetCrouchAnim(false);
    }

    // ─────────────────────────────────────────────────────
    // Start/Cancel charge
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
            playerBody.velocity = vert;
        }

        if (playerJump) playerJump.SetJumpSuppressed(true);
        var dash = GetComponentInParent<PlayerDash>();
        dash?.SetDashSuppressed(true);

        LockFlightEntry(true);

        _chargedFlashOn = false;
        jetpackRare?.SetChargedFlash(false);

        cameraBoostFx?.OnChargeProgress(0f);

        jetpackRare?.SetEnergyBallMeshesVisible(true);
        jetpackRare?.SetChargeDustVisible(true);

        // Boost pose not yet, only when we actually launch.
        SetBoostAnim(false);
    }

    private void CancelCharge()
    {
        _chargedFlashOn = false;
        jetpackRare?.SetChargedFlash(false);

        jetpackRare?.SetEnergyBallMeshesVisible(false);
        jetpackRare?.SetChargeDustVisible(false);

        if (!_charging) return;

        _charging  = false;
        _holdTimer = 0f;
        _latchedHorizDirWS = Vector3.zero;

        if (!_isCrouching && playerMovement)
            playerMovement.SetExternalStopMovement(false);

        if (playerJump) playerJump.SetJumpSuppressed(false);
        var dash = GetComponentInParent<PlayerDash>();
        dash?.SetDashSuppressed(false);

        LockFlightEntry(false);

        cameraBoostFx?.OnChargeCancel();

        _speedlinesRequested = false;
        StopSpeedlines();

        SetBoostAnim(false);
    }

    // ─────────────────────────────────────────────────────
    // Launch
    // ─────────────────────────────────────────────────────
    private void Launch()
    {
        _charging = false;

        _chargedFlashOn = false;
        jetpackRare?.SetChargedFlash(false);

        jetpackRare?.SetEnergyBallMeshesVisible(false);
        jetpackRare?.SetChargeDustVisible(false);

        if (_isCrouching)
        {
            _isCrouching = false;
            SetCrouchAnim(false);
        }

        Vector3 horiz = Vector3.zero;

        if (playerBody)
        {
            Vector3 up = GetUp();

            float vMag = Mathf.Max(0f, maxLaunchVertical);
            float hMag = Mathf.Max(0f, maxLaunchHorizontal);

            PreLaunchSeparation(preLaunchLift);
            _forceUngroundedUntil = Time.time + Mathf.Max(0.02f, ungroundGrace);

            Vector3 newVel = up * vMag;

            // Build a movement dir from *current* input; if none, use latched
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
                    moveDir = _latchedHorizDirWS;
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

            playerBody.velocity = newVel;

            if (playerMovement && horiz.sqrMagnitude > 0.0001f)
                playerMovement.HoldExternalHorizontal(horiz, 15f);

            cameraBoostFx?.OnLaunch(up);
            SpawnGroundbreak();
        }

        if (playerMovement)
        {
            playerMovement.SetExternalStopMovement(false); // allow air control again
            playerMovement.NotifyJumped();
        }

        // Turn on boost pose for the boost duration
        SetBoostAnim(true);

        _speedlinesRequested = true;
        PlaySpeedlines();

        StartCoroutine(BoostApexAndAfter());
    }

    private void PreLaunchSeparation(float lift)
    {
        if (!playerBody) return;
        Vector3 up = GetUp();

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

            cameraBoostFx?.OnLand();

            _speedlinesRequested = false;
            StopSpeedlines();

            LockFlightEntry(false);

            SpawnGroundbreak();
        }

        // Done boosting/falling → turn off boost pose
        SetBoostAnim(false);

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

    private void EnsureGroundbreakPrefabBound()
    {
        if (fxGroundbreakPrefab != null) return;

        var go = GameObject.Find("FX_groundbreak");
        if (go != null && go.scene.IsValid())
        {
            fxGroundbreakPrefab = go;
            return;
        }

        if (tryResourcesLoad)
        {
            var res = Resources.Load<GameObject>("FX_groundbreak");
            if (res != null) fxGroundbreakPrefab = res;
        }
    }

    private void SpawnGroundbreak()
    {
        if (fxGroundbreakPrefab == null || playerBody == null) return;

        Vector3 up  = GetUp();

        const float maxRayDist = 20f;
        if (!Physics.Raycast(playerBody.position + up * 0.1f, -up, out var hit, maxRayDist, ~0, QueryTriggerInteraction.Ignore))
            return;

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

        if (IsFirstPersonView())
        {
            if (speedlines != null && speedlines.isPlaying)
                speedlines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        if (_speedlinesRequested && speedlines != null && !speedlines.isPlaying)
            speedlines.Play(true);

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

    private bool IsFirstPersonView()
    {
        var rig = GetComponentInParent<PlayerCamera>(true);
        if (!rig) rig = FindObjectOfType<PlayerCamera>(true);
        return rig != null && rig.IsInFirstPerson;
    }

    private bool IsGrounded()
    {
        if (Time.time < _forceUngroundedUntil) return false;

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