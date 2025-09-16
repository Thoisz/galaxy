using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class JetpackBoostJump : MonoBehaviour
{
    [Header("Auto-find (from parents)")]
    [SerializeField] private PlayerFlight playerFlight; // auto from parents if null
    [SerializeField] private Rigidbody    playerBody;   // auto from parents if null
    [SerializeField] private Camera       playerCam;    // auto from parents or Camera.main
    [SerializeField] private Component    gravityBody;  // optional (e.g., GravityBody). Auto if present in parents

    [Header("Input")]
    [SerializeField] private KeyCode holdKey    = KeyCode.LeftShift; // must hold
    [SerializeField] private KeyCode triggerKey = KeyCode.Space;     // must hold

    [Header("Charge Phases (seconds)")]
    [Tooltip("Phase 1 → Phase 2 (armed) time. Releasing before this just cancels.")]
    [SerializeField] private float warmupSeconds = 0.50f;
    [Tooltip("Minimum total hold time required before a launch is allowed.")]
    [SerializeField] private float minHoldSeconds = 0.90f;

    [Header("Start Conditions")]
    [Tooltip("Player must be essentially still to begin charging (horizontal speed <= this).")]
    [SerializeField] private float maxStartHorizSpeed = 0.2f;

    [Header("Launch")]
    [Tooltip("Upward velocity (relative to gravity up) applied on release when armed).")]
    [SerializeField] private float launchUpVelocity = 18f;
    [Tooltip("Optional forward carry added along player forward at launch.")]
    [SerializeField] private float launchForwardVelocity = 0f;

    [Header("FOV")]
    [SerializeField] private float chargeFovIncrease = 4f;
    [SerializeField] private float launchFovIncrease = 10f;
    [SerializeField] private float fovLerpUpSpeed   = 10f;
    [SerializeField] private float fovLerpDownSpeed = 4.5f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckRadius   = 0.25f;
    [SerializeField] private float groundCheckDistance = 0.55f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Movement While Charging")]
    [Tooltip("Zero horizontal velocity each FixedUpdate while charging.")]
    [SerializeField] private bool freezeHorizontal = true;

    [Header("Fuel (optional via reflection)")]
    [SerializeField] private bool          requireFuel = false;
    [SerializeField] private float         fuelCost    = 25f;
    [SerializeField] private MonoBehaviour fuelSource; // any component of yours
    [Tooltip("Bool HasFuel(float amount)")]
    [SerializeField] private string fuelHasMethod = "HasFuel";
    [Tooltip("Void ConsumeFuel(float amount)")]
    [SerializeField] private string fuelConsumeMethod = "ConsumeFuel";

    [Header("Charge FX (children of the jetpack)")]
    [SerializeField] private GameObject phase1FX;   // charging up
    [SerializeField] private GameObject phase2FX;   // armed

    [Header("Optional Audio (on the jetpack)")]
    [SerializeField] private AudioSource chargeLoop;
    [SerializeField] private AudioSource armedLoop;
    [SerializeField] private AudioSource launchSfx;

    // ───── internals ─────
    private float _holdTimer = 0f;
    private bool  _charging  = false;
    private bool  _armed     = false;
    private float _baseFov   = -1f;

    // Reflection into PlayerFlight.EnterFlight()
    private MethodInfo _miEnterFlight;

    // GravityBody.GetEffectiveGravityDirection()
    private MethodInfo _miGetGravityDir;

    private void Awake()
    {
        if (!playerFlight) playerFlight = GetComponentInParent<PlayerFlight>();
        if (!playerBody)   playerBody   = GetComponentInParent<Rigidbody>();

        if (!playerCam)
        {
            // Try PlayerFlight → PlayerCamera → Camera
            if (playerFlight)
            {
                var pc = playerFlight.GetComponentInChildren<PlayerCamera>(true);
                if (pc != null)
                {
                    var cam = pc.GetComponentInChildren<Camera>(true);
                    if (cam) playerCam = cam;
                }
            }
            if (!playerCam && Camera.main) playerCam = Camera.main;
        }
        if (playerCam) _baseFov = playerCam.fieldOfView;

        if (!gravityBody)
        {
            // If you use a custom gravity component (e.g., GravityBody), we’ll find it.
            gravityBody = GetComponentInParent<GravityBody>();
        }
        if (gravityBody)
        {
            _miGetGravityDir = gravityBody.GetType().GetMethod(
                "GetEffectiveGravityDirection",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        }

        // Reflect EnterFlight on PlayerFlight
        if (playerFlight)
        {
            _miEnterFlight = playerFlight.GetType().GetMethod(
                "EnterFlight",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
        }

        HideChargeFX();
    }

    private void Update()
    {
        // Never run while already flying
        if (playerFlight && playerFlight.IsFlying)
        {
            CancelCharge();
            EaseFovToBase();
            return;
        }

        // Must be grounded to do anything
        if (!IsGrounded())
        {
            CancelCharge();
            EaseFovToBase();
            return;
        }

        // Must be (nearly) still to start
        if (!_charging && playerBody)
        {
            var up = GetUp();
            var horiz = Vector3.ProjectOnPlane(playerBody.velocity, up);
            if (horiz.magnitude > maxStartHorizSpeed)
            {
                EaseFovToBase();
                return;
            }
        }

        bool wantsCharge = Input.GetKey(holdKey) && Input.GetKey(triggerKey);

        if (!_charging)
        {
            if (wantsCharge && (!requireFuel || HasEnoughFuel(fuelCost)))
                StartCharge();
        }
        else
        {
            // While charging: if keys released or we lost ground → resolve
            if (!wantsCharge || !IsGrounded())
            {
                if (_armed && _holdTimer >= minHoldSeconds && IsGrounded())
                    Launch();
                else
                    CancelCharge();
            }
        }

        if (!_charging) EaseFovToBase();
    }

    private void FixedUpdate()
    {
        if (!_charging) return;

        _holdTimer += Time.fixedDeltaTime;

        if (!_armed && _holdTimer >= warmupSeconds)
        {
            _armed = true;
            SetPhase(armed: true);
        }

        if (freezeHorizontal && playerBody)
        {
            Vector3 up = GetUp();
            Vector3 v  = playerBody.velocity;
            Vector3 horiz = Vector3.ProjectOnPlane(v, up);
            playerBody.velocity = v - horiz; // keep vertical only (usually 0 while grounded)
        }

        // FOV while charging
        if (playerCam && _baseFov > 0f)
        {
            float target = _baseFov + chargeFovIncrease * Mathf.Clamp01(_holdTimer / warmupSeconds);
            playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, target, Time.fixedDeltaTime * fovLerpUpSpeed);
        }
    }

    // ───── actions ─────

    private void StartCharge()
    {
        _charging  = true;
        _armed     = false;
        _holdTimer = 0f;

        SetPhase(armed: false);

        if (chargeLoop) { chargeLoop.loop = true; chargeLoop.Play(); }
    }

    private void CancelCharge()
    {
        if (!_charging) return;

        _charging  = false;
        _armed     = false;
        _holdTimer = 0f;

        HideChargeFX();
        StopAllSounds();
    }

    private void Launch()
    {
        _charging = false;

        if (requireFuel) ConsumeFuel(fuelCost);

        HideChargeFX();
        StopAllSounds();
        if (launchSfx) launchSfx.Play();

        if (playerBody)
        {
            Vector3 up = GetUp();
            Vector3 vel = up * Mathf.Max(0f, launchUpVelocity);
            if (launchForwardVelocity > 0f)
                vel += transform.forward * launchForwardVelocity;
            playerBody.velocity = vel;
        }

        // FOV kick
        if (playerCam && _baseFov > 0f)
            playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, _baseFov + launchFovIncrease, 0.85f);

        StartCoroutine(WaitForApexThenEnterFlight());
    }

    private System.Collections.IEnumerator WaitForApexThenEnterFlight()
    {
        const float timeout = 3.5f;
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

        TryEnterFlight();
        EaseFovToBase();
    }

    // ───── helpers ─────

    private void SetPhase(bool armed)
    {
        if (phase1FX) phase1FX.SetActive(!armed);
        if (phase2FX) phase2FX.SetActive(armed);

        if (armed)
        {
            if (chargeLoop && chargeLoop.isPlaying) chargeLoop.Stop();
            if (armedLoop) { armedLoop.loop = true; armedLoop.Play(); }
        }
    }

    private void HideChargeFX()
    {
        if (phase1FX) phase1FX.SetActive(false);
        if (phase2FX) phase2FX.SetActive(false);
    }

    private void StopAllSounds()
    {
        if (chargeLoop && chargeLoop.isPlaying) chargeLoop.Stop();
        if (armedLoop  && armedLoop.isPlaying)  armedLoop.Stop();
    }

    private void EaseFovToBase()
    {
        if (!playerCam || _baseFov <= 0f) return;
        playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, _baseFov, Time.deltaTime * fovLerpDownSpeed);
    }

    private bool IsGrounded()
    {
        if (!playerBody) return false;

        Vector3 up   = GetUp();
        Vector3 orig = playerBody.position + up * 0.1f;
        Vector3 dir  = -up;

        if (Physics.SphereCast(orig, groundCheckRadius, dir, out RaycastHit hit, groundCheckDistance,
                               groundMask, QueryTriggerInteraction.Ignore))
        {
            float dot = Vector3.Dot(hit.normal.normalized, up);
            return dot >= 0.5f;
        }
        return false;
    }

    private Vector3 GetUp()
    {
        // If you have custom gravity, ask for it
        if (gravityBody != null && _miGetGravityDir != null)
        {
            try
            {
                var g = (Vector3)_miGetGravityDir.Invoke(gravityBody, null);
                if (g.sqrMagnitude > 0.0001f) return (-g).normalized;
            }
            catch { }
        }
        // Fallback
        return Vector3.up;
    }

    private void TryEnterFlight()
    {
        if (playerFlight == null) return;

        // Respect your unlock if needed (optional)
        // if (!playerFlight.IsFlightUnlocked) return;

        try
        {
            if (_miEnterFlight != null)
                _miEnterFlight.Invoke(playerFlight, null);
        }
        catch { /* ignore */ }
    }

    // Fuel helpers (optional)
    private bool HasEnoughFuel(float amount)
    {
        if (!requireFuel || !fuelSource) return true;
        var m = fuelSource.GetType().GetMethod(fuelHasMethod, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null) return true;
        try { return (bool)m.Invoke(fuelSource, new object[] { amount }); }
        catch { return true; }
    }

    private void ConsumeFuel(float amount)
    {
        if (!requireFuel || !fuelSource) return;
        var m = fuelSource.GetType().GetMethod(fuelConsumeMethod, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null) return;
        try { m.Invoke(fuelSource, new object[] { amount }); }
        catch { }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        warmupSeconds   = Mathf.Max(0f, warmupSeconds);
        minHoldSeconds  = Mathf.Max(warmupSeconds, minHoldSeconds);
        maxStartHorizSpeed = Mathf.Max(0f, maxStartHorizSpeed);
        launchUpVelocity = Mathf.Max(0f, launchUpVelocity);
        groundCheckRadius   = Mathf.Max(0.01f, groundCheckRadius);
        groundCheckDistance = Mathf.Max(0.05f, groundCheckDistance);
        fovLerpUpSpeed   = Mathf.Max(0f, fovLerpUpSpeed);
        fovLerpDownSpeed = Mathf.Max(0f, fovLerpDownSpeed);
    }
#endif
}