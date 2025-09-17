using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)] // run BEFORE PlayerJump so we can block the Space down-frame
public class JetpackBoostJump : MonoBehaviour
{
    [Header("Auto-find (from parents)")]
    [SerializeField] private PlayerFlight   playerFlight;
    [SerializeField] private Rigidbody      playerBody;
    [SerializeField] private Camera         playerCam;
    [SerializeField] private Component      gravityBody;
    [SerializeField] private PlayerCrouch   playerCrouch;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerJump     playerJump;   // to suppress normal jumps

    [Header("Input")]
    [SerializeField] private KeyCode triggerKey = KeyCode.Space; // pressed with crouch

    [Header("Charge")]
    [Tooltip("Nothing happens unless you hold at least this long.")]
    [SerializeField] private float minChargeTimeSeconds = 0.10f;
    [Tooltip("Time to ramp from min → max launch velocity.")]
    [SerializeField] private float chargeTimeSeconds = 1.00f;
    [Tooltip("Launch velocity at t=0 (applied Up, and if moving, Forward).")]
    [SerializeField] private float minLaunchVelocity = 8f;
    [Tooltip("Launch velocity at t=chargeTimeSeconds.")]
    [SerializeField] private float maxLaunchVelocity = 18f;

    [Header("Forward boost")]
    [Tooltip("Horizontal launch is Up * this ratio when movement is held.")]
    [SerializeField] private float forwardUpRatio = 1.5f;

    [Header("FOV (optional flair)")]
    [SerializeField] private float chargeFovIncrease = 4f;
    [SerializeField] private float launchFovIncrease = 10f;
    [SerializeField] private float fovLerpUpSpeed   = 10f;
    [SerializeField] private float fovLerpDownSpeed = 4.5f;

    [Header("Ground Check (fallback if PlayerMovement missing)")]
    [SerializeField] private float groundCheckRadius   = 0.25f;
    [SerializeField] private float groundCheckDistance = 0.55f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Fuel (optional via reflection)")]
    [SerializeField] private bool          requireFuel = false;
    [SerializeField] private float         fuelCost    = 25f;
    [SerializeField] private MonoBehaviour fuelSource;
    [Tooltip("Bool HasFuel(float amount)")]
    [SerializeField] private string fuelHasMethod     = "HasFuel";
    [Tooltip("Void ConsumeFuel(float amount)")]
    [SerializeField] private string fuelConsumeMethod = "ConsumeFuel";

    // ── internals ──
    private float _holdTimer = 0f;
    private bool  _charging  = false;
    private float _baseFov   = -1f;

    private MethodInfo _miEnterFlight;   // PlayerFlight.EnterFlight()
    private MethodInfo _miGetGravityDir; // GravityBody.GetEffectiveGravityDirection()

    private void Awake()
    {
        if (!playerFlight)   playerFlight   = GetComponentInParent<PlayerFlight>();
        if (!playerBody)     playerBody     = GetComponentInParent<Rigidbody>();
        if (!playerCrouch)   playerCrouch   = GetComponentInParent<PlayerCrouch>();
        if (!playerMovement) playerMovement = GetComponentInParent<PlayerMovement>();
        if (!playerJump)     playerJump     = GetComponentInParent<PlayerJump>();

        if (!playerCam)
        {
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
    }

    private void OnDisable()
    {
        _charging  = false;
        _holdTimer = 0f;
        if (playerMovement) playerMovement.SetExternalStopMovement(false);
        if (playerJump)     playerJump.SetJumpSuppressed(false);
    }

    private void Update()
    {
        if (playerFlight && playerFlight.IsFlying)
        {
            CancelCharge();
            EaseFovToBase();
        }

        bool grounded   = playerMovement ? playerMovement.IsGrounded() : IsGrounded();
        bool crouchHeld = playerCrouch && playerCrouch.IsCrouching; // Shift
        bool spaceHeld  = Input.GetKey(triggerKey);

        bool wantsCharge = grounded && crouchHeld && spaceHeld;

        // Block normal jump when crouching on ground or while charging
        SuppressJump((crouchHeld && grounded) || _charging);

        if (!_charging)
        {
            if (wantsCharge && (!requireFuel || HasEnoughFuel(fuelCost)))
                StartCharge();
        }
        else
        {
            bool comboReleased = !(crouchHeld && spaceHeld);
            bool lostGround    = !grounded;

            if (comboReleased || lostGround)
            {
                if (_holdTimer >= minChargeTimeSeconds)
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

        // While charging: HARD LOCK horizontal each physics step
        if (playerBody)
        {
            Vector3 up   = GetUp();
            Vector3 v    = playerBody.velocity;
            Vector3 vert = Vector3.Project(v, up);
            playerBody.velocity = vert; // no horizontal
        }

        // FOV while charging (scaled by charge fraction)
        if (playerCam && _baseFov > 0f)
        {
            float t = chargeTimeSeconds <= 0f ? 1f : Mathf.Clamp01(_holdTimer / chargeTimeSeconds);
            float target = _baseFov + chargeFovIncrease * t;
            playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, target, Time.fixedDeltaTime * fovLerpUpSpeed);
        }
    }

    // ── actions ──

    private void StartCharge()
    {
        _charging  = true;
        _holdTimer = 0f;

        // Instantly zero horizontal
        if (playerBody)
        {
            Vector3 up   = GetUp();
            Vector3 vert = Vector3.Project(playerBody.velocity, up);
            playerBody.velocity = vert;
        }

        if (playerMovement) playerMovement.SetExternalStopMovement(true);
    }

    private void CancelCharge()
    {
        if (!_charging) return;

        _charging  = false;
        _holdTimer = 0f;

        if (playerMovement) playerMovement.SetExternalStopMovement(false);
        SuppressJump(false);
    }

    private void Launch()
    {
        _charging = false;

        if (requireFuel) ConsumeFuel(fuelCost);

        if (playerBody)
        {
            Vector3 up = GetUp();

            // Charge-scaled magnitude
            float t = chargeTimeSeconds <= 0f ? 1f : Mathf.Clamp01(_holdTimer / chargeTimeSeconds);
            float v = Mathf.Lerp(minLaunchVelocity, maxLaunchVelocity, t);

            // Are we holding any movement input at release?
            bool movingHeld = playerMovement
                ? playerMovement.HasMovementInput()
                : (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f);

            Vector3 newVel = up * v; // vertical component

            if (movingHeld)
            {
                // Use intended move direction (camera-relative), else fallback
                Vector3 fwd = Vector3.zero;
                if (playerMovement != null)
                    fwd = playerMovement.GetMoveDirection();

                if (fwd.sqrMagnitude < 1e-6f) fwd = transform.forward;

                // Purely horizontal forward
                fwd = Vector3.ProjectOnPlane(fwd, up);
                if (fwd.sqrMagnitude < 1e-6f)
                {
                    Vector3 camFwd = playerCam ? playerCam.transform.forward : transform.forward;
                    fwd = Vector3.ProjectOnPlane(camFwd, up);
                }
                if (fwd.sqrMagnitude > 1e-6f) fwd.Normalize();

                // Forward is 1.5x up (configurable via forwardUpRatio)
                newVel += fwd * (v * forwardUpRatio);
            }

            playerBody.velocity = newVel;
        }

        if (playerMovement) playerMovement.SetExternalStopMovement(false);
        if (playerMovement) playerMovement.NotifyJumped();

        // FOV kick
        if (playerCam && _baseFov > 0f)
            playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, _baseFov + launchFovIncrease, 0.85f);

        SuppressJump(false);
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

    // ── helpers ──

    private void SuppressJump(bool on)
    {
        if (playerJump) playerJump.SetJumpSuppressed(on);
    }

    private void EaseFovToBase()
    {
        if (!playerCam || _baseFov <= 0f) return;
        playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, _baseFov, Time.deltaTime * fovLerpDownSpeed);
    }

    private bool IsGrounded()
    {
        if (playerMovement) return playerMovement.IsGrounded();

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
        minChargeTimeSeconds = Mathf.Max(0f, minChargeTimeSeconds);
        chargeTimeSeconds    = Mathf.Max(0f, chargeTimeSeconds);
        minLaunchVelocity    = Mathf.Max(0f, minLaunchVelocity);
        maxLaunchVelocity    = Mathf.Max(minLaunchVelocity, maxLaunchVelocity);
        forwardUpRatio       = Mathf.Max(0f, forwardUpRatio);
        fovLerpUpSpeed       = Mathf.Max(0f, fovLerpUpSpeed);
        fovLerpDownSpeed     = Mathf.Max(0f, fovLerpDownSpeed);
        groundCheckRadius    = Mathf.Max(0.01f, groundCheckRadius);
        groundCheckDistance  = Mathf.Max(0.05f, groundCheckDistance);
    }
#endif
}