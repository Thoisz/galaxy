using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class JetpackCommon : MonoBehaviour
{
    [Header("Auto-find (optional)")]
    [SerializeField] private PlayerFlight playerFlight;   // auto from parents if null
    [SerializeField] private Rigidbody    playerBody;     // auto from parents if null

    [Header("Speeds (fallbacks)")]
    [Tooltip("Read _fastSpeed/_superSpeed/_superActive/_idleAscending from PlayerFlight via reflection.")]
    [SerializeField] private bool  readFromPlayerFlight = true;
    [SerializeField] private float fastSpeed  = 20f;      // used if reflection off/unavailable
    [SerializeField] private float superSpeed = 40f;

    [Header("Lifetimes (seconds)")]
    [SerializeField] private float idleLifetime  = 0.5f;
    [SerializeField] private float fastLifetime  = 0.7f;
    [SerializeField] private float superLifetime = 1.5f;

    [Header("Smoothing")]
    [Tooltip("Base smoothing speed for lifetime changes (units/sec).")]
    [SerializeField] private float lifetimeLerpSpeed = 12f;
    [Tooltip("Multiplier when going from lower → higher (idle→fast).")]
    [SerializeField] private float speedUpMultiplier = 2f;
    [Tooltip("While idle-ascending (Space, no WASD) force FAST and blend × this.")]
    [SerializeField] private float idleAscendToFastMultiplier = 3f;

    [Header("Thruster hole (glow planes)")]
    [Tooltip("Uniform localScale used while NOT in super speed.")]
    [SerializeField] private float holeScaleNormal = 0.0011f;
    [Tooltip("Uniform localScale used while in super speed.")]
    [SerializeField] private float holeScaleSuper  = 0.0026f;
    [Tooltip("Seconds to shrink from super → normal. Grow to super is instant.")]
    [SerializeField] private float holeScaleShrinkDuration = 1f;

    [Header("Thruster hole colors")]
    [Tooltip("Color while NOT in super (e.g., yellow-orange).")]
    [SerializeField] private Color holeColorNormal = new Color(1f, 0.78f, 0.25f, 1f);
    [Tooltip("Color while in super (e.g., red-orange).")]
    [SerializeField] private Color holeColorSuper  = new Color(1f, 0.35f, 0.05f, 1f);
    [Tooltip("Multiply emission (if shader supports _EmissionColor).")]
    [SerializeField] private float holeEmissionMultiplier = 1.0f;

    // ── cached sets ──────────────────────────────────────────────
    private readonly List<Transform> _normalRoots = new();
    private readonly List<Transform> _superRoots  = new();

    private readonly List<ParticleSystem> _normalFlames = new();
    private readonly List<ParticleSystem> _superFlames  = new();

    private sealed class HoleData
    {
        public Transform t;
        public Renderer  r;
        public Material  mat;        // runtime instance (safe per-instance)
        public Color     curColor;
        public Color     targetColor;
    }
    private readonly List<HoleData> _holes = new();

    // state
    private bool  _wasFlying = false;
    private bool  _visualsInSuper = false;
    private float _curLifetime;

    // hole tween (scale)
    private float _holeScaleCurrent;
    private float _holeScaleTarget;

    // reflection cache
    private FieldInfo _fiFast, _fiSuper, _fiSuperActive, _fiIdleAscending;

    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (!playerFlight) playerFlight = GetComponentInParent<PlayerFlight>();
        if (!playerBody)   playerBody   = GetComponentInParent<Rigidbody>();

        BuildSetCaches();

        PrepareParticleList(_normalFlames, false, true);
        PrepareParticleList(_superFlames,  false, true);

        if (readFromPlayerFlight && playerFlight)
        {
            var t = playerFlight.GetType();
            _fiFast          = t.GetField("_fastSpeed",     BindingFlags.Instance | BindingFlags.NonPublic);
            _fiSuper         = t.GetField("_superSpeed",    BindingFlags.Instance | BindingFlags.NonPublic);
            _fiSuperActive   = t.GetField("_superActive",   BindingFlags.Instance | BindingFlags.NonPublic);
            _fiIdleAscending = t.GetField("_idleAscending", BindingFlags.Instance | BindingFlags.NonPublic);
            TrySyncSpeedsFromFlight();
        }

        _curLifetime = idleLifetime;

        SetRootsActive(_normalRoots, true);
        SetRootsActive(_superRoots,  true);

        // Holes initial state
        _holeScaleCurrent = holeScaleNormal;
        _holeScaleTarget  = holeScaleNormal;
        foreach (var h in _holes)
        {
            if (h.mat == null && h.r != null) h.mat = h.r.material;
            h.curColor    = holeColorNormal;
            h.targetColor = holeColorNormal;
            ApplyHoleColor(h, h.curColor);
        }
        SetHolesActive(false);
        SetHolesScale(_holeScaleCurrent);
    }

    void Update()
    {
        if (!playerFlight) return;

        TrySyncSpeedsFromFlight();

        bool isFlying = playerFlight.IsFlying;

        // Enter/exit flight
        if (isFlying && !_wasFlying)
        {
            // show holes at normal size & color
            SetHolesActive(true);
            SetHolesInstant(holeScaleNormal, holeColorNormal);

            _curLifetime = idleLifetime;
            ApplyLifetimeList(_normalFlames, _curLifetime, force:true);
            EnableEmissionList(_normalFlames, true,  clear:true);
            EnableEmissionList(_superFlames,  false, clear:true);
            _visualsInSuper = false;
        }
        else if (!isFlying && _wasFlying)
        {
            SetHolesActive(false);
            SetHolesInstant(holeScaleNormal, holeColorNormal);

            EnableEmissionList(_normalFlames, false, clear:true);
            EnableEmissionList(_superFlames,  false, clear:true);
            _visualsInSuper = false;
        }
        _wasFlying = isFlying;

        if (!isFlying) return;

        // read current speed + states
        float speed         = playerBody ? playerBody.velocity.magnitude : 0f;
        bool  superActive   = IsSuperActive(speed);
        bool  idleAscending = IsIdleAscending();

        // SUPER: instant swap
        if (superActive)
        {
            if (!_visualsInSuper)
            {
                EnableEmissionList(_normalFlames, false, clear:false);

                ApplyLifetimeList(_superFlames, superLifetime, force:true);
                EnableEmissionList(_superFlames, true, clear:true);

                // Holes: instant growth + instant color change
                SetHolesInstant(holeScaleSuper, holeColorSuper);

                _visualsInSuper = true;
            }
            return; // no normal blending while in super
        }

        // Exiting super: fade super, resume normal, and shrink/ recolor holes smoothly
        if (_visualsInSuper)
        {
            EnableEmissionList(_normalFlames, true,  clear:false);
            EnableEmissionList(_superFlames,  false, clear:false);

            SetHoleScaleTarget(holeScaleNormal);
            SetHoleColorTarget(holeColorNormal);

            _visualsInSuper = false;
        }

        // NORMAL mode: target lifetime
        float target;
        if (idleAscending) // Space with no WASD ⇒ force fast
        {
            target       = fastLifetime;
            _curLifetime = MoveToward(_curLifetime, target, lifetimeLerpSpeed * idleAscendToFastMultiplier);
        }
        else
        {
            float t      = Mathf.InverseLerp(0f, Mathf.Max(0.01f, fastSpeed), speed);
            target       = Mathf.Lerp(idleLifetime, fastLifetime, t);
            float rate   = lifetimeLerpSpeed * (_curLifetime < target ? speedUpMultiplier : 1f);
            _curLifetime = MoveToward(_curLifetime, target, rate);
        }

        ApplyLifetimeList(_normalFlames, _curLifetime, force:false);

        // Hole tweens (scale shrinks smoothly; color blends with same duration)
        TickHoleScale();
        TickHoleColor();
    }

    // ───────────────── helpers ─────────────────

    static float MoveToward(float current, float target, float unitsPerSecond)
        => Mathf.MoveTowards(current, target, unitsPerSecond * Time.deltaTime);

    void BuildSetCaches()
    {
        _normalRoots.Clear(); _superRoots.Clear();
        _normalFlames.Clear(); _superFlames.Clear();
        _holes.Clear();

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (n == "set_normal") _normalRoots.Add(t);
            else if (n == "set_super") _superRoots.Add(t);
            else if (n == "thrusterhole_l" || n == "thrusterhole_r" || n.Contains("thrusterhole"))
            {
                var r = t.GetComponent<Renderer>();
                if (r != null) _holes.Add(new HoleData { t = t, r = r });
            }
        }

        void Collect(Transform root, List<ParticleSystem> flames)
        {
            if (!root) return;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.name.ToLowerInvariant().Contains("flame"))
                    flames.Add(ps);
            }
        }

        foreach (var r in _normalRoots) Collect(r, _normalFlames);
        foreach (var r in _superRoots)  Collect(r, _superFlames);
    }

    static void PrepareParticleList(List<ParticleSystem> list, bool playOnAwake, bool emissionOff)
    {
        foreach (var ps in list)
        {
            if (!ps) continue;
            var m = ps.main;
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            m.playOnAwake     = playOnAwake;
            var em = ps.emission;
            em.enabled = !emissionOff;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    static void ApplyLifetimeList(List<ParticleSystem> list, float value, bool force)
    {
        foreach (var ps in list)
        {
            if (!ps) continue;
            var m = ps.main;
            if (force || m.startLifetime.mode != ParticleSystemCurveMode.Constant ||
                Mathf.Abs(m.startLifetime.constant - value) > 0.0001f)
            {
                m.startLifetime = value; // constant
            }
        }
    }

    static void EnableEmissionList(List<ParticleSystem> list, bool enable, bool clear)
    {
        foreach (var ps in list)
        {
            if (!ps) continue;
            var em = ps.emission;
            em.enabled = enable;
            if (enable)
            {
                if (clear) ps.Clear(true);
                ps.Play(true);
            }
            else
            {
                ps.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear
                                    : ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    static void SetRootsActive(List<Transform> roots, bool active)
    {
        foreach (var r in roots) if (r) r.gameObject.SetActive(active);
    }

    // ── holes: visibility/scale/color ────────────────────────────

    void SetHolesActive(bool active)
    {
        foreach (var h in _holes)
        {
            if (h == null) continue;
            if (h.r) h.r.enabled = active;
            else if (h.t) h.t.gameObject.SetActive(active);
        }
    }

    void SetHolesScale(float uniformScale)
    {
        Vector3 s = new Vector3(uniformScale, uniformScale, uniformScale);
        foreach (var h in _holes) if (h?.t) h.t.localScale = s;
    }

    void SetHolesInstant(float scale, Color color)
    {
        _holeScaleCurrent = scale;
        _holeScaleTarget  = scale;
        SetHolesScale(scale);

        foreach (var h in _holes)
        {
            if (h == null) continue;
            if (h.mat == null && h.r != null) h.mat = h.r.material;
            h.curColor    = color;
            h.targetColor = color;
            ApplyHoleColor(h, h.curColor);
        }
    }

    void SetHoleScaleTarget(float target) => _holeScaleTarget = target;

    void SetHoleColorTarget(Color target)
    {
        foreach (var h in _holes)
        {
            if (h == null) continue;
            if (h.mat == null && h.r != null) h.mat = h.r.material;
            h.targetColor = target;
        }
    }

    void TickHoleScale()
    {
        if (Mathf.Approximately(_holeScaleCurrent, _holeScaleTarget)) return;

        if (_holeScaleCurrent > _holeScaleTarget)
        {
            // Shrink smoothly over holeScaleShrinkDuration
            float totalDelta = Mathf.Abs(holeScaleSuper - holeScaleNormal);
            float rate = (totalDelta / Mathf.Max(0.0001f, holeScaleShrinkDuration));
            _holeScaleCurrent = Mathf.MoveTowards(_holeScaleCurrent, _holeScaleTarget, rate * Time.deltaTime);
            SetHolesScale(_holeScaleCurrent);
        }
        else
        {
            // Growth is instant
            _holeScaleCurrent = _holeScaleTarget;
            SetHolesScale(_holeScaleCurrent);
        }
    }

    void TickHoleColor()
    {
        // Color blends over the same duration as shrink; growth is instant
        foreach (var h in _holes)
        {
            if (h == null || h.mat == null) continue;

            bool growingToSuper = ColorsApproximately(h.curColor, holeColorSuper) == false &&
                                  ColorsApproximately(h.targetColor, holeColorSuper);

            if (growingToSuper)
            {
                h.curColor = h.targetColor; // instant to super color
            }
            else
            {
                // Linear blend to target over holeScaleShrinkDuration
                float k = (holeScaleShrinkDuration <= 0f) ? 1f : Mathf.Clamp01(Time.deltaTime / holeScaleShrinkDuration);
                h.curColor = new Color(
                    Mathf.Lerp(h.curColor.r, h.targetColor.r, k),
                    Mathf.Lerp(h.curColor.g, h.targetColor.g, k),
                    Mathf.Lerp(h.curColor.b, h.targetColor.b, k),
                    Mathf.Lerp(h.curColor.a, h.targetColor.a, k)
                );
            }

            ApplyHoleColor(h, h.curColor);
        }
    }

    static bool ColorsApproximately(Color a, Color b)
        => Mathf.Abs(a.r - b.r) < 0.001f &&
           Mathf.Abs(a.g - b.g) < 0.001f &&
           Mathf.Abs(a.b - b.b) < 0.001f &&
           Mathf.Abs(a.a - b.a) < 0.001f;

    void ApplyHoleColor(HoleData h, Color c)
    {
        if (h.mat == null) return;

        if (h.mat.HasProperty("_BaseColor")) h.mat.SetColor("_BaseColor", c);
        if (h.mat.HasProperty("_Color"))     h.mat.SetColor("_Color",     c);

        if (h.mat.HasProperty("_EmissionColor"))
        {
            // Ensure emission contributes; harmless if shader ignores it
            h.mat.EnableKeyword("_EMISSION");
            h.mat.SetColor("_EmissionColor", c * holeEmissionMultiplier);
        }
    }

    // ── PlayerFlight reflection & fallbacks ─────────────────

    void TrySyncSpeedsFromFlight()
    {
        if (!readFromPlayerFlight || !playerFlight) return;
        try
        {
            if (_fiFast  != null) fastSpeed  = (float)_fiFast.GetValue(playerFlight);
            if (_fiSuper != null) superSpeed = (float)_fiSuper.GetValue(playerFlight);
        } catch { /* ignore */ }
    }

    bool IsSuperActive(float currentSpeed)
    {
        if (readFromPlayerFlight && playerFlight && _fiSuperActive != null)
        {
            try { return (bool)_fiSuperActive.GetValue(playerFlight); }
            catch { /* fall through */ }
        }
        return currentSpeed >= superSpeed * 0.98f; // fallback
    }

    bool IsIdleAscending()
    {
        if (readFromPlayerFlight && playerFlight && _fiIdleAscending != null)
        {
            try { return playerFlight.IsFlying && (bool)_fiIdleAscending.GetValue(playerFlight); }
            catch { /* fall through */ }
        }

        if (!playerFlight || !playerFlight.IsFlying) return false;
        bool noWASD = Mathf.Abs(Input.GetAxis("Horizontal")) < 0.001f &&
                      Mathf.Abs(Input.GetAxis("Vertical"))   < 0.001f;
        return noWASD && Input.GetKey(KeyCode.Space);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        fastSpeed  = Mathf.Max(0f, fastSpeed);
        superSpeed = Mathf.Max(fastSpeed, superSpeed);

        lifetimeLerpSpeed          = Mathf.Max(0f, lifetimeLerpSpeed);
        speedUpMultiplier          = Mathf.Max(0f, speedUpMultiplier);
        idleAscendToFastMultiplier = Mathf.Max(0f, idleAscendToFastMultiplier);

        idleLifetime  = Mathf.Max(0f, idleLifetime);
        fastLifetime  = Mathf.Max(0f, fastLifetime);
        superLifetime = Mathf.Max(0f, superLifetime);

        holeScaleNormal         = Mathf.Max(0f, holeScaleNormal);
        holeScaleSuper          = Mathf.Max(0f, holeScaleSuper);
        holeScaleShrinkDuration = Mathf.Max(0.0001f, holeScaleShrinkDuration);

        holeEmissionMultiplier  = Mathf.Max(0f, holeEmissionMultiplier);
    }
#endif
}