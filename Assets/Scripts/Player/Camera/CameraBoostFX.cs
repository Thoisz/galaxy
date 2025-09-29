using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(200)] // after most gameplay, before PlayerCamera's next Update
public class CameraBoostFX : MonoBehaviour
{
    [Header("Bindings (auto)")]
    [SerializeField] private PlayerCamera playerCamera; // where we push the offset + read gravity up
    [SerializeField] private Camera       cam;          // camera whose FOV we tween

    // ─────────────────────────────────────────────────────
    // POSITIONAL LAG

    [Header("Ascent lag")]
    [SerializeField, Tooltip("Meters to lag opposite to Up immediately after launch.")]
    private float ascentLagMax = 3f;

    [SerializeField, Tooltip("Seconds to fade the ascent lag to 0 (usually around apex).")]
    private float ascentCatchupTime = 0.35f;

    [SerializeField, Tooltip("Curve time=0..1 → scale 1..0;\nX=t/ascentCatchupTime, Y=lag scale.")]
    private AnimationCurve ascentLagCurve = AnimationCurve.Linear(0, 1, 1, 0);

    [Header("Fall lag")]
    [SerializeField, Tooltip("Meters to pull the camera downward while falling (ramps in over time).")]
    private float fallLagMax = 4f;

    [SerializeField, Tooltip("Seconds to ramp fall lag from 0 → 1.")]
    private float fallLagRamp = 0.6f;

    [SerializeField, Tooltip("Curve time=0..1 → scale 0..1;\nX=t/fallLagRamp, Y=lag scale.")]
    private AnimationCurve fallLagCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Post-landing lag catch-up")]
    [SerializeField, Tooltip("Seconds to ease any remaining lag back to zero after landing.")]
    private float postLandCatchupTime = 0.35f;

    [SerializeField, Tooltip("Curve time=0..1 → scale 1..0 for post-landing offset fade.")]
    private AnimationCurve postLandCurve = AnimationCurve.Linear(0, 1, 1, 0);

    // ─────────────────────────────────────────────────────
    // FOV

    [Header("FOV (Charging/Ascent)")]
    [SerializeField, Tooltip("Extra FOV (usually negative) while charging, scaled by charge 0..1.")]
    private float chargeFovDelta = -6f;

    [SerializeField, Tooltip("Extra FOV during ascent (constant while ascending).")]
    private float launchFovDelta = 10f;

    [Header("FOV (Falling → absolute target)")]
    [SerializeField, Tooltip("Absolute FOV to reach by end of fall, e.g. 95°.")]
    private float fallFovTarget = 95f;

    [SerializeField, Tooltip("Seconds to move from the apex FOV to the Fall FOV Target.")]
    private float fallFovTime = 0.6f;

    [SerializeField, Tooltip("Curve 0..1 for fall FOV blend (apex→target).")]
    private AnimationCurve fallFovCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Landing FOV Return")]
    [SerializeField, Tooltip("Seconds to ease FOV back to base after landing.")]
    private float landFovReturnTime = 0.6f;

    [SerializeField, Tooltip("Curve time=0..1 for landing FOV return. If null, linear.")]
    private AnimationCurve landFovCurve = null;

    [Header("FOV Lerp Rates (non-landing)")]
    [SerializeField, Tooltip("Lerp rate toward target FOV when effects engage (charging/ascending).")]
    private float fovLerpUp = 12f;

    [SerializeField, Tooltip("Lerp rate back to base FOV when effects end (idle).")]
    private float fovLerpDown = 6f;

    // ─────────────────────────────────────────────────────
    // EDGE BLUR (Renderer Feature)

    [Header("Edge Blur – Intensity")]
    [SerializeField, Tooltip("Max edge-blur during CHARGE (ignored if you keep charge blur off).")]
    private float chargeBlurMax = 0.5f;
    [SerializeField, Tooltip("Curve vs. charge 0..1 for CHARGE blur.")]
    private AnimationCurve chargeBlurCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [SerializeField, Tooltip("Fixed edge-blur while ASCENDING (often a small value).")]
    private float ascentBlur = 0.25f;

    [SerializeField, Tooltip("Max edge-blur reached at end of FALL.")]
    private float fallBlurMax = 0.8f;
    [SerializeField, Tooltip("Seconds to ramp fall blur from apex → max.")]
    private float fallBlurTime = 0.6f;
    [SerializeField, Tooltip("Curve 0..1 over fallBlurTime.")]
    private AnimationCurve fallBlurCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [SerializeField, Tooltip("Seconds to fade blur back to 0 after landing.")]
    private float landBlurReturnTime = 0.5f;
    [SerializeField, Tooltip("Curve 0..1 for landing blur fade.")]
    private AnimationCurve landBlurCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

    [Header("Edge Blur – Radius")]
    [SerializeField, Range(0f,1f), Tooltip("Inner radius while charging/ascent.")]
    private float chargeInner = 0.55f;
    [SerializeField, Range(0f,1f), Tooltip("Outer radius while charging/ascent.")]
    private float chargeOuter = 0.95f;

    [SerializeField, Range(0f,1f), Tooltip("Inner radius while falling.")]
    private float fallInner = 0.5f;
    [SerializeField, Range(0f,1f), Tooltip("Outer radius while falling.")]
    private float fallOuter = 0.98f;

    [Header("Edge Blur – Quality")]
    [SerializeField, Range(1,16), Tooltip("Blur sample count (performance).")]
    private int blurSamples = 8;
    [SerializeField, Min(0.001f), Tooltip("Blur spread (pixels-ish).")]
    private float blurSpread = 1.5f;

    // Shader property IDs (globals)
    static readonly int ID_Intensity = Shader.PropertyToID("_EdgeBlurIntensity");
    static readonly int ID_Inner     = Shader.PropertyToID("_EdgeBlurInner");
    static readonly int ID_Outer     = Shader.PropertyToID("_EdgeBlurOuter");
    static readonly int ID_Spread    = Shader.PropertyToID("_EdgeBlurSpread");
    static readonly int ID_Samples   = Shader.PropertyToID("_EdgeBlurSamples");

    // ─────────────────────────────────────────────────────
    // CHARGE TREMBLE (camera shake while charging)

    [Header("Charge Tremble")]
    [SerializeField, Tooltip("Meters of positional jitter while charging (local to camera axes).")]
    private float chargeShakeAmplitude = 0.05f;
    [SerializeField, Tooltip("Jitter frequency (Hz).")]
    private float chargeShakeFrequency = 12f;
    [SerializeField, Tooltip("Axis weights (x=right, y=up, z=forward).")]
    private Vector3 chargeShakeAxes = new Vector3(0.6f, 1f, 0.3f);
    [SerializeField, Tooltip("How quickly the shake offset smooths (bigger = snappier).")]
    private float chargeShakeDamping = 20f;
    [SerializeField, Tooltip("Scale shake by charge amount (0..1).")]
    private bool chargeShakeScaleByCharge = true;

    // seeds so noise channels are decorrelated
    private Vector3 _shakeSeed;

    // ── runtime state ──
    private enum Phase { Idle, Charging, Ascending, Falling }
    private Phase _phase = Phase.Idle;

    private float _baseFov;
    private float _targetFov;

    private float _charge01;      // 0..1 during charging
    private float _ascentTimer;   // since launch (positional lag)
    private float _fallTimer;     // since apex (positional lag)

    // falling FOV tween (absolute)
    private float _fallFovT = 0f;       // 0..1 time along fall curve
    private float _fallFovStart = 60f;  // FOV at the exact apex

    // landing FOV tween
    private bool  _landFovActive = false;
    private float _landFovT = 0f;
    private float _landFovStart = 60f; // filled at landing

    // landing BLUR tween
    private bool  _landBlurActive = false;
    private float _landBlurT = 0f;
    private float _landBlurStart = 0f;

    // fall BLUR timer
    private float _fallBlurT = 0f;

    private Vector3 _lastUp = Vector3.up;

    // offsets
    private Vector3 _lastAppliedOffset = Vector3.zero; // lag
    private Vector3 _chargeShakeOffset = Vector3.zero; // tremble
    private float   _postLandT = 0f;                   // timer for landing offset fade

    // blur bookkeeping
    float _currentBlurIntensity = 0f;

    private void Awake()
    {
        if (!playerCamera) playerCamera = GetComponent<PlayerCamera>();
        if (!cam)          cam          = GetComponentInChildren<Camera>(true);
        _baseFov = cam ? cam.fieldOfView : 60f;

        if (ascentLagCurve == null || ascentLagCurve.length == 0)
            ascentLagCurve = AnimationCurve.Linear(0, 1, 1, 0);
        if (fallLagCurve == null || fallLagCurve.length == 0)
            fallLagCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        if (postLandCurve == null || postLandCurve.length == 0)
            postLandCurve = AnimationCurve.Linear(0, 1, 1, 0);
        if (fallFovCurve == null || fallFovCurve.length == 0)
            fallFovCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        if (landBlurCurve == null || landBlurCurve.length == 0)
            landBlurCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        if (chargeBlurCurve == null || chargeBlurCurve.length == 0)
            chargeBlurCurve = AnimationCurve.Linear(0, 0, 1, 1);

        // randomize shake seeds
        _shakeSeed = new Vector3(
            Random.Range(0.1f, 1000f),
            Random.Range(0.1f, 1000f),
            Random.Range(0.1f, 1000f)
        );
    }

    private void OnEnable()
    {
        ResetAllState();
        if (playerCamera) playerCamera.SetExternalCameraOffset(Vector3.zero);
    }

    private void OnDisable()
    {
        if (playerCamera) playerCamera.SetExternalCameraOffset(Vector3.zero);
        if (cam) cam.fieldOfView = _baseFov;
        _phase = Phase.Idle;
        _landFovActive  = false;
        _landBlurActive = false;

        // zero out blur globals
        PushEdgeBlurGlobals(0f, chargeInner, chargeOuter);
    }

    private void Update()
    {
        // --- FOV ---

        if (_landFovActive && cam)
        {
            _landFovT += Time.deltaTime / Mathf.Max(0.0001f, landFovReturnTime);
            float t = Mathf.Clamp01(_landFovT);
            float eased = (landFovCurve != null) ? Mathf.Clamp01(landFovCurve.Evaluate(t)) : t;
            cam.fieldOfView = Mathf.Lerp(_landFovStart, _baseFov, eased);

            if (_landFovT >= 1f) _landFovActive = false;
        }
        else
        {
            switch (_phase)
            {
                case Phase.Charging:
                    _targetFov = _baseFov + chargeFovDelta * Mathf.Clamp01(_charge01);
                    break;

                case Phase.Ascending:
                    _targetFov = _baseFov + launchFovDelta; // constant while ascending
                    break;

                case Phase.Falling:
                {
                    if (cam)
                    {
                        // progress apex → target over fallFovTime
                        _fallFovT += Time.deltaTime / Mathf.Max(0.0001f, fallFovTime);
                        float t = Mathf.Clamp01(_fallFovT);
                        float k = Mathf.Clamp01(fallFovCurve.Evaluate(t));
                        cam.fieldOfView = Mathf.Lerp(_fallFovStart, fallFovTarget, k);
                        // falling sets FOV directly this frame
                        goto AfterFov;
                    }
                    break;
                }

                default:
                    _targetFov = _baseFov;
                    break;
            }

            if (cam)
            {
                float rate = (_phase == Phase.Idle) ? fovLerpDown : fovLerpUp;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, _targetFov, Time.deltaTime * Mathf.Max(0f, rate));
            }
        }

    AfterFov:

        // --- EDGE BLUR ---
        // Requirement: no blur while charging. Blur during whole jump (ascent + fall). Fade on land.

        float intensity = 0f;
        float inner = chargeInner, outer = chargeOuter;

        if (_landBlurActive)
        {
            _landBlurT += Time.deltaTime / Mathf.Max(0.0001f, landBlurReturnTime);
            float t = Mathf.Clamp01(_landBlurT);
            float fade = (landBlurCurve != null) ? Mathf.Clamp01(landBlurCurve.Evaluate(t)) : t;
            intensity = Mathf.Lerp(_landBlurStart, 0f, fade);
            inner = chargeInner; outer = chargeOuter; // consistent radii during fade

            if (_landBlurT >= 1f) _landBlurActive = false;
        }
        else
        {
            switch (_phase)
            {
                case Phase.Charging:
                    // INTENTIONALLY DISABLED DURING CHARGE
                    intensity = 0f;
                    inner = chargeInner; outer = chargeOuter;
                    break;

                case Phase.Ascending:
                    // Constant light blur while ascending
                    intensity = Mathf.Max(0f, ascentBlur);
                    inner = chargeInner; outer = chargeOuter;
                    break;

                case Phase.Falling:
                    // Ramp blur across the fall
                    _fallBlurT += Time.deltaTime / Mathf.Max(0.0001f, fallBlurTime);
                    {
                        float bt = Mathf.Clamp01(_fallBlurT);
                        float k  = (fallBlurCurve != null) ? Mathf.Clamp01(fallBlurCurve.Evaluate(bt)) : bt;
                        intensity = k * Mathf.Max(0f, fallBlurMax);
                        inner = fallInner; outer = fallOuter;
                    }
                    break;

                default:
                    intensity = 0f;
                    inner = chargeInner; outer = chargeOuter;
                    break;
            }
        }

        PushEdgeBlurGlobals(intensity, inner, outer);
    }

    private void LateUpdate()
{
    if (playerCamera == null) return;

    // Fresh gravity up
    _lastUp = playerCamera.GetCurrentGravityUp();
    if (_lastUp.sqrMagnitude < 1e-6f) _lastUp = Vector3.up;

    // Compute positional lag offset (ascent/fall) or post-land fade
    Vector3 lagOffset = Vector3.zero;

    if (_phase == Phase.Ascending)
    {
        _ascentTimer += Time.deltaTime;
        float norm  = (ascentCatchupTime <= 0f) ? 1f : Mathf.Clamp01(_ascentTimer / ascentCatchupTime);
        float scale = Mathf.Clamp01(ascentLagCurve.Evaluate(norm)); // expect 1→0
        lagOffset = -_lastUp * (ascentLagMax * scale);
        _lastAppliedOffset = lagOffset;
    }
    else if (_phase == Phase.Falling)
    {
        _fallTimer += Time.deltaTime;
        float norm  = (fallLagRamp <= 0f) ? 1f : Mathf.Clamp01(_fallTimer / fallLagRamp);
        float scale = Mathf.Clamp01(fallLagCurve.Evaluate(norm)); // expect 0→1
        lagOffset = -_lastUp * (fallLagMax * scale);
        _lastAppliedOffset = lagOffset;
    }
    else // Idle or Charging
    {
        // Post-landing catch-up: ease previous lag to zero
        if (_lastAppliedOffset.sqrMagnitude > 1e-6f && postLandCatchupTime > 0f)
        {
            _postLandT += Time.deltaTime / Mathf.Max(0.0001f, postLandCatchupTime);
            float t = Mathf.Clamp01(_postLandT);
            float scale = Mathf.Clamp01(postLandCurve.Evaluate(t)); // 1→0
            lagOffset = _lastAppliedOffset * scale;

            if (_postLandT >= 1f)
            {
                _lastAppliedOffset = Vector3.zero;
                _postLandT = 0f;
            }
        }
        else
        {
            lagOffset = Vector3.zero;
            _lastAppliedOffset = Vector3.zero;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // First-person safety: remove vertical drop during FALL (and optionally ascent)
    // so the camera never sinks below the head pivot (seeing accessories "fall past").
    if (playerCamera.IsInFirstPerson)
    {
        lagOffset = RedirectLagForFirstPerson(lagOffset, _lastUp);
    }
    // ─────────────────────────────────────────────────────────────

    // Charging tremble (added on top of lagOffset)
    Vector3 shakeOffset = Vector3.zero;
    if (_phase == Phase.Charging && chargeShakeAmplitude > 0f)
    {
        Transform basis = cam ? cam.transform : transform;
        float t = Time.time;

        // Per-channel Perlin in [0,1] → shift to [-1,1]
        float nx = Mathf.PerlinNoise(_shakeSeed.x, t * chargeShakeFrequency) * 2f - 1f;
        float ny = Mathf.PerlinNoise(_shakeSeed.y, t * chargeShakeFrequency) * 2f - 1f;
        float nz = Mathf.PerlinNoise(_shakeSeed.z, t * chargeShakeFrequency) * 2f - 1f;

        float envelope = chargeShakeScaleByCharge ? Mathf.Clamp01(_charge01) : 1f;
        Vector3 local = new Vector3(nx * chargeShakeAxes.x, ny * chargeShakeAxes.y, nz * chargeShakeAxes.z)
                        * (chargeShakeAmplitude * envelope);

        // Convert to world using camera local axes
        Vector3 target =
            basis.right   * local.x +
            basis.up      * local.y +
            basis.forward * local.z;

        // Smooth the shake so it doesn't buzz
        _chargeShakeOffset = Vector3.Lerp(_chargeShakeOffset, target, 1f - Mathf.Exp(-chargeShakeDamping * Time.deltaTime));
        shakeOffset = _chargeShakeOffset;
    }
    else
    {
        // Decay shake to zero quickly when not charging
        _chargeShakeOffset = Vector3.Lerp(_chargeShakeOffset, Vector3.zero, 1f - Mathf.Exp(-chargeShakeDamping * Time.deltaTime));
        shakeOffset = _chargeShakeOffset;
    }

    // Hand the combined offset to PlayerCamera
    playerCamera.SetExternalCameraOffset(lagOffset + shakeOffset);
}

/// <summary>
/// In first-person we don't want vertical lag to sink the camera below the head.
/// This removes (or redirects) the component along -up and optionally gives a tiny
/// backward pull for "drag" feel that won't expose the body/accessories.
/// </summary>
private Vector3 RedirectLagForFirstPerson(Vector3 lagOffset, Vector3 up)
{
    if (lagOffset.sqrMagnitude < 1e-8f) return lagOffset;

    // Remove vertical component entirely (pure safety)
    Vector3 vertical = Vector3.Project(lagOffset, -up);
    Vector3 horizontal = lagOffset - vertical;

    // Option A (safe): Zero vertical during Falling; keep horizontal unchanged
    // If you also want to kill it during Ascent, remove the phase check.
    if (_phase == Phase.Falling)
    {
        // Optional small backward pull to preserve a sense of drag without dipping the camera.
        // This uses the camera's forward so it works in both FP/TP.
        Vector3 back = cam ? -cam.transform.forward : -transform.forward;

        // Scale the redirected amount by how much vertical we removed.
        float vMag = vertical.magnitude;

        // Tunables (feel free to tweak constants or surface as [SerializeField]):
        const float redirectScale = 0.35f;   // how much of the removed vertical becomes backward drag
        const float maxMeters     = 0.6f;    // cap how far we can pull back in FP

        Vector3 redirected = back.normalized * Mathf.Min(vMag * redirectScale, maxMeters);

        return horizontal + redirected; // no vertical; tiny backward drag
    }

    // For other phases (e.g., Ascending) you can keep the vertical,
    // or also strip it if you noticed similar issues going up.
    return horizontal;
}

    // ─────────────────────────────────────────────────────
    // Public API (called by BoostJump)

    public void OnChargeProgress(float normalized)
    {
        // No blur during charge; keep tremble
        _landFovActive  = false;
        _landBlurActive = false;

        _phase    = Phase.Charging;
        _charge01 = Mathf.Clamp01(normalized);
        // offsets handled in LateUpdate (shake only; no lag)
    }

    public void OnChargeCancel()
    {
        _landFovActive  = false;
        _landBlurActive = false;

        _phase = Phase.Idle;
        _charge01 = 0f;
        _ascentTimer = _fallTimer = 0f;

        // reset blur immediately
        PushEdgeBlurGlobals(0f, chargeInner, chargeOuter);
    }

    public void OnLaunch(Vector3 up)
    {
        _landFovActive  = false;
        _landBlurActive = false;

        _phase = Phase.Ascending;
        _ascentTimer = 0f;
        _fallTimer = 0f;

        // Reset fall FOV progress so apex starts from that FOV
        _fallFovT = 0f;
        _fallFovStart = cam ? cam.fieldOfView : _baseFov;

        // Reset fall blur progress
        _fallBlurT = 0f;

        _lastUp = (up.sqrMagnitude > 1e-6f) ? up.normalized : Vector3.up;
    }

    public void OnApex(Vector3 up)
    {
        _landFovActive  = false;
        _landBlurActive = false;

        // Switch to falling — capture the exact apex FOV as the start point
        _phase = Phase.Falling;
        _fallTimer = 0f;

        _fallFovT = 0f;
        if (cam) _fallFovStart = cam.fieldOfView;

        _fallBlurT = 0f;

        _lastUp = (up.sqrMagnitude > 1e-6f) ? up.normalized : Vector3.up;
    }

    public void OnLand()
    {
        _phase = Phase.Idle;
        _charge01 = 0f;
        _ascentTimer = _fallTimer = 0f;

        // Start post-landing lag fade from whatever offset we had
        _postLandT = 0f;

        // FOV: smooth return from current value to base
        if (cam)
        {
            _landFovActive = true;
            _landFovT = 0f;
            _landFovStart = cam.fieldOfView;
        }

        // BLUR: fade out from current intensity
        _landBlurActive = true;
        _landBlurT = 0f;
        _landBlurStart = _currentBlurIntensity; // captured via PushEdgeBlurGlobals bookkeeping
    }

    public void CancelAll()
    {
        _phase = Phase.Idle;
        _charge01 = 0f;
        _ascentTimer = _fallTimer = 0f;
        _landFovActive  = false;
        _landBlurActive = false;

        _lastAppliedOffset = Vector3.zero;
        _chargeShakeOffset = Vector3.zero;
        _postLandT = 0f;

        _currentBlurIntensity = 0f;
        PushEdgeBlurGlobals(0f, chargeInner, chargeOuter);

        if (playerCamera) playerCamera.SetExternalCameraOffset(Vector3.zero);
        if (cam) cam.fieldOfView = _baseFov;
    }

    public void SetBaseFov(float fov)
    {
        _baseFov = fov;
    }

    private void ResetAllState()
    {
        _phase = Phase.Idle;
        _charge01 = 0f;
        _ascentTimer = _fallTimer = 0f;

        _fallFovT = 0f;
        _fallFovStart = _baseFov;

        _landFovActive = false;
        _landFovT = 0f;

        _landBlurActive = false;
        _landBlurT = 0f;
        _landBlurStart = 0f;
        _currentBlurIntensity = 0f;

        _lastAppliedOffset = Vector3.zero;
        _chargeShakeOffset = Vector3.zero;
        _postLandT = 0f;

        if (cam) { _baseFov = cam.fieldOfView; }

        // ensure globals are sane on enable
        PushEdgeBlurGlobals(0f, chargeInner, chargeOuter);
    }

    // ── Edge blur globals driving ──
    void PushEdgeBlurGlobals(float intensity, float inner, float outer)
    {
        _currentBlurIntensity = Mathf.Clamp01(intensity);
        inner = Mathf.Clamp01(inner);
        outer = Mathf.Clamp01(Mathf.Max(inner + 0.001f, outer));

        Shader.EnableKeyword("_EDGE_BLUR_DRIVEN");
        Shader.SetGlobalFloat(ID_Intensity, _currentBlurIntensity);
        Shader.SetGlobalFloat(ID_Inner, inner);
        Shader.SetGlobalFloat(ID_Outer, outer);
        Shader.SetGlobalFloat(ID_Spread, Mathf.Max(0.001f, blurSpread));
        Shader.SetGlobalFloat(ID_Samples, Mathf.Clamp(blurSamples, 1, 16));
    }
}
