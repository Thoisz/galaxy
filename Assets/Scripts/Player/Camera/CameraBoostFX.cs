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
    [Tooltip("Meters to lag opposite to Up immediately after launch.")]
    [SerializeField] private float ascentLagMax = 3f;

    [Tooltip("Seconds to fade the ascent lag to 0 (usually around apex).")]
    [SerializeField] private float ascentCatchupTime = 0.35f;

    [Tooltip("Curve time=0..1 → scale 1..0; recommend start near 1 and end at 0.\nX=t/ascentCatchupTime, Y=lag scale.")]
    [SerializeField] private AnimationCurve ascentLagCurve = AnimationCurve.Linear(0, 1, 1, 0);

    [Header("Fall lag")]
    [Tooltip("Meters to pull the camera downward while falling (ramps in over time).")]
    [SerializeField] private float fallLagMax = 4f;

    [Tooltip("Seconds to ramp fall lag from 0 → 1.")]
    [SerializeField] private float fallLagRamp = 0.6f;

    [Tooltip("Curve time=0..1 → scale 0..1; recommend ease-in.\nX=t/fallLagRamp, Y=lag scale.")]
    [SerializeField] private AnimationCurve fallLagCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Post-landing lag catch-up")]
    [Tooltip("Seconds to ease any remaining lag back to zero after landing.")]
    [SerializeField] private float postLandCatchupTime = 0.35f;

    [Tooltip("Curve time=0..1 → scale 1..0 for post-landing offset fade.")]
    [SerializeField] private AnimationCurve postLandCurve = AnimationCurve.Linear(0, 1, 1, 0);

    // ─────────────────────────────────────────────────────
    // FOV

    [Header("FOV (Charging/Ascent)")]
    [Tooltip("Extra FOV (usually negative) while charging, scaled by charge 0..1.")]
    [SerializeField] private float chargeFovDelta = -6f;

    [Tooltip("Extra FOV during ascent (constant while ascending).")]
    [SerializeField] private float launchFovDelta = 10f;

    [Header("FOV (Falling → absolute target)")]
    [Tooltip("Absolute FOV the camera should reach by the end of the fall.\nExample: 95 means \"go to 95° over Fall FOV Time\".")]
    [SerializeField] private float fallFovTarget = 95f;

    [Tooltip("Seconds to move from the apex FOV to the Fall FOV Target.")]
    [SerializeField] private float fallFovTime = 0.6f;

    [Tooltip("Curve for fall FOV blend: X = normalized time 0..1, Y = blend 0..1.\nY=0 keeps apex FOV; Y=1 is the Fall FOV Target.")]
    [SerializeField] private AnimationCurve fallFovCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Landing FOV Return")]
    [Tooltip("Seconds to ease FOV back to base after landing.")]
    [SerializeField] private float landFovReturnTime = 0.6f;

    [Tooltip("Curve time=0..1 for landing FOV return. If null, linear.")]
    [SerializeField] private AnimationCurve landFovCurve = null;

    [Header("FOV Lerp Rates (non-landing)")]
    [Tooltip("Lerp rate toward target FOV when effects engage (charging/ascending).")]
    [SerializeField] private float fovLerpUp = 12f;

    [Tooltip("Lerp rate back to base FOV when effects end (idle).")]
    [SerializeField] private float fovLerpDown = 6f;

    // ─────────────────────────────────────────────────────
    // CHARGE TREMBLE (Perlin shake)

    [Header("Charge tremble (camera shake while charging)")]
    [Tooltip("Peak shake amplitude in meters (applied as camera offset).")]
    [SerializeField] private float chargeShakeAmplitude = 0.15f;

    [Tooltip("Shake frequency in Hz (how fast the noise wiggles).")]
    [SerializeField] private float chargeShakeFrequency = 18f;

    [Tooltip("Seconds for shake to fade in after Charge begins.")]
    [SerializeField] private float chargeShakeFadeIn = 0.12f;

    [Tooltip("Seconds for shake to fade out when Charge ends/cancels.")]
    [SerializeField] private float chargeShakeFadeOut = 0.18f;

    [Tooltip("If ON, shake axes are camera-space (Right/Up/Forward). If OFF, world-space.")]
    [SerializeField] private bool chargeShakeInCameraSpace = true;

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

    private Vector3 _lastUp = Vector3.up;

    // offset management
    private Vector3 _lastAppliedOffset = Vector3.zero; // what we gave PlayerCamera last frame
    private float   _postLandT = 0f;                   // timer for landing offset fade

    // charge shake runtime
    private float _chargeShakeTime = 0f;
    private float _chargeShakeOutT = 0f; // time into fade out
    private float _noiseSeedX, _noiseSeedY, _noiseSeedZ;

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

        // initialize shake seeds so each play session is a bit different
        _noiseSeedX = Random.Range(0f, 1000f);
        _noiseSeedY = Random.Range(0f, 1000f);
        _noiseSeedZ = Random.Range(0f, 1000f);
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
        _landFovActive = false;
    }

    private void Update()
    {
        // Landing FOV tween overrides normal target logic while active
        if (_landFovActive && cam)
        {
            _landFovT += Time.deltaTime / Mathf.Max(0.0001f, landFovReturnTime);
            float t = Mathf.Clamp01(_landFovT);
            float eased = (landFovCurve != null) ? Mathf.Clamp01(landFovCurve.Evaluate(t)) : t;
            cam.fieldOfView = Mathf.Lerp(_landFovStart, _baseFov, eased);

            if (_landFovT >= 1f) _landFovActive = false;
            return;
        }

        // ── Phase-based FOV (charging / ascent / fall / idle) ──
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
                if (!cam) return;

                // Progress time 0..1 over fallFovTime
                _fallFovT += Time.deltaTime / Mathf.Max(0.0001f, fallFovTime);
                float t = Mathf.Clamp01(_fallFovT);

                // Evaluate curve 0..1 (clamped) and lerp from exact apex FOV to absolute fall target
                float k = Mathf.Clamp01(fallFovCurve.Evaluate(t));
                cam.fieldOfView = Mathf.Lerp(_fallFovStart, fallFovTarget, k);

                return; // Falling drives FOV directly this frame
            }

            default: // Idle
                _targetFov = _baseFov;
                break;
        }

        if (cam)
        {
            float rate = (_phase == Phase.Idle) ? fovLerpDown : fovLerpUp;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, _targetFov, Time.deltaTime * Mathf.Max(0f, rate));
        }
    }

    private void LateUpdate()
    {
        if (playerCamera == null) return;

        // Keep a fresh 'up' from PlayerCamera
        _lastUp = playerCamera.GetCurrentGravityUp();
        if (_lastUp.sqrMagnitude < 1e-6f) _lastUp = Vector3.up;

        Vector3 newOffset = Vector3.zero;

        if (_phase == Phase.Ascending)
        {
            _ascentTimer += Time.deltaTime;
            float norm  = (ascentCatchupTime <= 0f) ? 1f : Mathf.Clamp01(_ascentTimer / ascentCatchupTime);
            float scale = Mathf.Clamp01(ascentLagCurve.Evaluate(norm)); // expect 1→0
            newOffset = -_lastUp * (ascentLagMax * scale);
            _lastAppliedOffset = newOffset;
            _chargeShakeOutT = 0f; // ensure no charge tail continues here
        }
        else if (_phase == Phase.Falling)
        {
            _fallTimer += Time.deltaTime;
            float norm  = (fallLagRamp <= 0f) ? 1f : Mathf.Clamp01(_fallTimer / fallLagRamp);
            float scale = Mathf.Clamp01(fallLagCurve.Evaluate(norm)); // expect 0→1
            newOffset = -_lastUp * (fallLagMax * scale);
            _lastAppliedOffset = newOffset;
            _chargeShakeOutT = 0f; // ensure no charge tail continues here
        }
        else if (_phase == Phase.Charging)
        {
            // during charge, no positional lag — but we add tremble
            Vector3 shake = ComputeChargeShakeOffset(Time.deltaTime);
            newOffset = shake; // do not write to _lastAppliedOffset (we don't want post-land catch-up from shake)
        }
        else // Idle
        {
            // Post-landing catch-up: ease whatever offset we had toward zero
            if (_lastAppliedOffset.sqrMagnitude > 1e-6f && postLandCatchupTime > 0f)
            {
                _postLandT += Time.deltaTime / Mathf.Max(0.0001f, postLandCatchupTime);
                float t = Mathf.Clamp01(_postLandT);
                float scale = Mathf.Clamp01(postLandCurve.Evaluate(t)); // 1→0
                newOffset = _lastAppliedOffset * scale;

                if (_postLandT >= 1f)
                {
                    _lastAppliedOffset = Vector3.zero;
                    _postLandT = 0f;
                }
            }
            else
            {
                newOffset = Vector3.zero;
                _lastAppliedOffset = Vector3.zero;
            }

            // allow a short “tail” fade after leaving Charging
            if (_chargeShakeOutT > 0f && _chargeShakeOutT < chargeShakeFadeOut)
            {
                newOffset += ComputeChargeShakeOffset(Time.deltaTime);
            }
        }

        playerCamera.SetExternalCameraOffset(newOffset);
    }

    // ─────────────────────────────────────────────────────
    // Public API (called by BoostJump)

    public void OnChargeProgress(float normalized)
    {
        _landFovActive = false;

        // if we just entered Charging, reset shake timers
        if (_phase != Phase.Charging)
        {
            _chargeShakeTime = 0f;
            _chargeShakeOutT = 0f;
        }

        _phase = Phase.Charging;
        _charge01 = Mathf.Clamp01(normalized);
        // keep offsets; charging uses only shake offset
    }

    public void OnChargeCancel()
    {
        _landFovActive = false;

        // leaving Charging → start fade-out tail for shake
        if (_phase == Phase.Charging)
        {
            _chargeShakeOutT = 0f; // will fade in LateUpdate idle branch
        }

        _phase = Phase.Idle;
        _charge01 = 0f;
        _ascentTimer = _fallTimer = 0f;
        // keep residual offset for post-land fade if any (comes from ascent/fall only)
    }

    public void OnLaunch(Vector3 up)
    {
        _landFovActive = false;

        _phase = Phase.Ascending;
        _ascentTimer = 0f;
        _fallTimer = 0f;

        // Reset fall FOV progress so when we hit apex we start clean from that FOV
        _fallFovT = 0f;
        _fallFovStart = cam ? cam.fieldOfView : _baseFov;

        _lastUp = (up.sqrMagnitude > 1e-6f) ? up.normalized : Vector3.up;

        // stop any charge shake immediately
        _chargeShakeOutT = chargeShakeFadeOut; // kill tail
    }

    public void OnApex(Vector3 up)
    {
        _landFovActive = false;

        // Switch to falling — capture the exact apex FOV as the start point
        _phase = Phase.Falling;
        _fallTimer = 0f;

        _fallFovT = 0f;
        if (cam) _fallFovStart = cam.fieldOfView;

        _lastUp = (up.sqrMagnitude > 1e-6f) ? up.normalized : Vector3.up;
    }

    public void OnLand()
    {
        _phase = Phase.Idle;
        _charge01 = 0f;
        _ascentTimer = _fallTimer = 0f;

        // Start post-landing lag fade from whatever offset we had
        _postLandT = 0f;

        // Begin smooth FOV return from current value to base
        if (cam)
        {
            _landFovActive = true;
            _landFovT = 0f;
            _landFovStart = cam.fieldOfView; // whatever it is at landing
        }

        // ensure no residual shake
        _chargeShakeOutT = chargeShakeFadeOut;
    }

    public void CancelAll()
    {
        _phase = Phase.Idle;
        _charge01 = 0f;
        _ascentTimer = _fallTimer = 0f;
        _landFovActive = false;

        _lastAppliedOffset = Vector3.zero;
        _postLandT = 0f;

        _chargeShakeTime = 0f;
        _chargeShakeOutT = chargeShakeFadeOut; // no shake

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

        _lastAppliedOffset = Vector3.zero;
        _postLandT = 0f;

        _chargeShakeTime = 0f;
        _chargeShakeOutT = chargeShakeFadeOut; // no shake initially

        if (cam) { _baseFov = cam.fieldOfView; }
    }

    // ─────────────────────────────────────────────────────
    // Helpers

    // Returns a small 3D offset for tremble during charging.
    // Uses Perlin noise so it’s smooth, with fade in/out and respects camera/world space.
    private Vector3 ComputeChargeShakeOffset(float dt)
    {
        // advance time
        _chargeShakeTime += dt;

        // base amplitude factor from charge progress (more charge = stronger tremble)
        float chargeFactor = Mathf.Clamp01(_charge01);

        // fade in while charging
        float fadeIn = (chargeShakeFadeIn <= 0f) ? 1f : Mathf.Clamp01(_chargeShakeTime / Mathf.Max(0.0001f, chargeShakeFadeIn));

        // if we’re no longer in Charging, allow a short fade-out tail
        float fadeOut = 1f;
        if (_phase != Phase.Charging && _chargeShakeOutT < chargeShakeFadeOut)
        {
            _chargeShakeOutT += dt;
            float t = Mathf.Clamp01(1f - (_chargeShakeOutT / Mathf.Max(0.0001f, chargeShakeFadeOut)));
            fadeOut = t;
        }

        float amp = chargeShakeAmplitude * chargeFactor * fadeIn * fadeOut;
        if (amp <= 0.00001f) return Vector3.zero;

        // noise time scale
        float tX = _noiseSeedX + _chargeShakeTime * chargeShakeFrequency;
        float tY = _noiseSeedY + _chargeShakeTime * chargeShakeFrequency * 1.07f;
        float tZ = _noiseSeedZ + _chargeShakeTime * chargeShakeFrequency * 0.93f;

        // Perlin gives [0,1]; remap to [-1,1]
        float nx = Mathf.PerlinNoise(tX, 17.123f) * 2f - 1f;
        float ny = Mathf.PerlinNoise(tY, 37.456f) * 2f - 1f;
        float nz = Mathf.PerlinNoise(tZ, 73.789f) * 2f - 1f;

        Vector3 dir = new Vector3(nx, ny, nz);

        // pick a basis: camera space or world space
        if (chargeShakeInCameraSpace && cam != null)
        {
            // camera basis
            Transform ct = cam.transform;
            Vector3 offset =
                ct.right   * dir.x +
                ct.up      * dir.y +
                ct.forward * dir.z;

            return offset * amp;
        }
        else
        {
            // simple world-space shake
            return dir * amp;
        }
    }
}