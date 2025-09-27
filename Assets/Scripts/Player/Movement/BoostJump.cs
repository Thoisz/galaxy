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
    [SerializeField] private Vector3 speedlinesLookOffsetEuler = new Vector3(90f, 0f, 0f);
    [SerializeField] private bool speedlinesMatchCameraRoll = true;

    [Header("Groundbreak FX")]
    [Tooltip("Optional. If empty, we search the scene for a prefab named EXACTLY 'FX_groundbreak' via Resources or in-scene.")]
    [SerializeField] private GameObject fxGroundbreakPrefab;
    [Tooltip("If true and prefab isn’t assigned, will try Resources.Load(\"FX_groundbreak\"). Place your prefab in a Resources folder.")]
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
    private bool _flightLockActive = false;
    private bool _flightWasEnabled = false;

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

    // NEW: find the jetpack FX driver that owns the EnergyBallFlash children
    if (!jetpackRare) jetpackRare = GetComponentInParent<JetpackRare>(true);
    jetpackRare?.SetChargedFlash(false); // ensure off at boot

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
    _hadMoveDuringCharge = false;

    if (playerMovement)
    {
        playerMovement.CancelExternalHorizontalHold();
        playerMovement.SetExternalStopMovement(false);
    }
    if (playerJump) playerJump.SetJumpSuppressed(false);

    LockFlightEntry(false);

    SetBoostAnim(false);
    cameraBoostFx?.OnChargeCancel();

    // hard stop the flash if object disables
    _chargedFlashOn = false;
    jetpackRare?.SetChargedFlash(false);

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
            bool hasMove = playerMovement
                ? playerMovement.HasMovementInput()
                : (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f);
            _hadMoveDuringCharge |= hasMove;

            bool lostGround = !grounded;

            bool releasedCrouchThisFrame = _prevCrouchHeld && !crouchHeld;
            bool releasedSpaceThisFrame  = _prevSpaceHeld  && !spaceHeld;

            if (releasedCrouchThisFrame)
                CancelCharge();
            else if (releasedSpaceThisFrame)
            {
                if (_holdTimer >= chargeTimeSeconds) Launch();
                else                                  CancelCharge();
            }
            else if (lostGround)
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
        playerBody.velocity = vert;
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

        // your old flash toggle (keep if you still use it)
        jetpackRare?.SetChargedFlash(_chargedFlashOn);

        // NEW: dust is ON while charging, OFF when full
        jetpackRare?.SetChargeDustVisible(!_chargedFlashOn);
    }
}

    private void LateUpdate()
    {
        if (_camXform == null) EnsureCameraTransform();
        AlignSpeedlinesTowardCamera();
    }

    private void Launch()
{
    _charging = false;
    _wasBoostJumpThisLaunch = false;

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

        Vector3 newVel = up * vMag;

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
            horizDir = hasMoveNow ? moveDir.normalized : Vector3.zero;
        else
        {
            Vector3 fallback = Vector3.ProjectOnPlane(transform.forward, up);
            horizDir = (hasMoveNow ? moveDir : fallback).normalized;
        }

        if (horizDir.sqrMagnitude > 0.0001f && hMag > 0.001f)
        {
            horiz = horizDir * hMag;
            newVel += horiz;
        }

        playerBody.velocity = newVel;

        if (playerMovement && horiz.sqrMagnitude > 0.0001f)
        {
            playerMovement.HoldExternalHorizontal(horiz, 15f);
            _wasBoostJumpThisLaunch = true;
        }

        cameraBoostFx?.OnLaunch(up);
        SpawnGroundbreak();
    }

    if (playerMovement) playerMovement.SetExternalStopMovement(false);
    if (playerMovement) playerMovement.NotifyJumped();

    SetBoostAnim(_wasBoostJumpThisLaunch);
    PlaySpeedlines();
    StartCoroutine(BoostApexAndAfter());
}

    // ── actions ──

    private void StartCharge(bool crouchHeldNow, bool spaceHeldNow)
{
    _charging  = true;
    _holdTimer = 0f;
    _hadMoveDuringCharge = false;

    _prevCrouchHeld = crouchHeldNow;
    _prevSpaceHeld  = spaceHeldNow;

    if (playerBody)
    {
        Vector3 up   = GetUp();
        Vector3 vert = Vector3.Project(playerBody.velocity, up);
        playerBody.velocity = vert;
    }

    if (playerMovement) playerMovement.SetExternalStopMovement(true);
    SuppressJump(true);
    var dash = GetComponentInParent<PlayerDash>();
    dash?.SetDashSuppressed(true);

    LockFlightEntry(true);

    // reset flash state on new charge
    _chargedFlashOn = false;
    jetpackRare?.SetChargedFlash(false);

    cameraBoostFx?.OnChargeProgress(0f);

    // show energyball + DUST while charging
    jetpackRare?.SetEnergyBallMeshesVisible(true);
    jetpackRare?.SetChargeDustVisible(true);
}

// Cancel charging: turn OFF energy balls
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
    _hadMoveDuringCharge = false;

    if (playerMovement) playerMovement.SetExternalStopMovement(false);

    SuppressJump(false);
    var dash = GetComponentInParent<PlayerDash>();
    dash?.SetDashSuppressed(false);

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
            StopSpeedlines();

            LockFlightEntry(false);
        }

        SuppressJump(false);
        var dash = GetComponentInParent<PlayerDash>();
        dash?.SetDashSuppressed(false);

        // 🔸 spawn groundbreak on landing
        SpawnGroundbreak();
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
            // Requires a Resources/FX_groundbreak.prefab
            var res = Resources.Load<GameObject>("FX_groundbreak");
            if (res != null) fxGroundbreakPrefab = res;
        }
    }

    void SpawnGroundbreak()
{
    if (fxGroundbreakPrefab == null || playerBody == null) return;

    Vector3 up  = GetUp();
    Vector3 pos = playerBody.position; // let the root do the precise snap with a ray
    Quaternion rot = Quaternion.identity;

    var fx = Instantiate(fxGroundbreakPrefab, pos, rot);

    // Hand off to the prefab so it can raycast down, align to ground normal,
    // and inherit the ground material (all inside GroundbreakFXRoot).
    var root = fx.GetComponent<GroundbreakFXRoot>();
    if (root != null)
    {
        // Optional: expose a bool on the root to turn on logs in the Inspector
        root.ConfigureFromContact(playerBody.position, up);
    }
    else
    {
        // Fallback: if the prefab somehow lacks GroundbreakFXRoot, do a minimal place
        // so you still see *something* (but no material inheritance/alignment).
        fx.transform.position = playerBody.position - up * 0.05f;
        fx.transform.rotation = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(transform.forward, up).normalized + 0.0001f * Vector3.forward, up);
    }
}

    Material GetGroundMaterialUnderPlayer(Vector3 origin, Vector3 up, float rayLen = 3f)
    {
        if (!Physics.Raycast(origin, -up, out var hit, rayLen, ~0, QueryTriggerInteraction.Ignore))
            return null;

        // Terrain → synthesize a material from TerrainLayer (dominant)
        var terrain = hit.collider.GetComponent<Terrain>();
        if (terrain != null && terrain.terrainData != null)
        {
            var td = terrain.terrainData;
            Vector3 local = hit.point - terrain.transform.position;
            float u = Mathf.InverseLerp(0f, td.size.x, local.x);
            float v = Mathf.InverseLerp(0f, td.size.z, local.z);

            int sx = Mathf.Clamp(Mathf.RoundToInt(u * (td.alphamapWidth  - 1)), 0, td.alphamapWidth  - 1);
            int sy = Mathf.Clamp(Mathf.RoundToInt(v * (td.alphamapHeight - 1)), 0, td.alphamapHeight - 1);
            float[,,] alpha = td.GetAlphamaps(sx, sy, 1, 1);

            int best = 0; float bestW = 0f;
            for (int l = 0; l < td.alphamapLayers; l++)
            {
                float w = alpha[0,0,l];
                if (w > bestW) { bestW = w; best = l; }
            }

            var layer = (best >= 0 && best < td.terrainLayers.Length) ? td.terrainLayers[best] : null;
            if (layer != null)
            {
                // Build a lightweight URP/Lit material that matches the layer’s look
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null) return null;
                var m = new Material(lit) { name = (layer.diffuseTexture ? layer.diffuseTexture.name : "TerrainLayer") + " (Clone)" };
                if (layer.diffuseTexture) m.SetTexture("_BaseMap", layer.diffuseTexture);
                m.SetColor("_BaseColor", Color.white);
                // Set tiling roughly from tileSize in world space → UV tiling estimate
                Vector2 tiling = new Vector2(
                    td.size.x / Mathf.Max(0.0001f, layer.tileSize.x),
                    td.size.z / Mathf.Max(0.0001f, layer.tileSize.y)
                );
                m.SetTextureScale("_BaseMap", tiling);
                m.SetTextureOffset("_BaseMap", layer.tileOffset);
                return m;
            }
        }

        // MeshRenderer → clone its sharedMaterial to keep it independent
        var rend = hit.collider.GetComponent<Renderer>();
        if (rend != null && rend.sharedMaterial != null)
        {
            var clone = new Material(rend.sharedMaterial) { name = rend.sharedMaterial.name + " (Clone)" };
            return clone;
        }

        return null;
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

    // Animator utilities (unchanged)
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
#endif

    // Flight entry locker (unchanged from your version)
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