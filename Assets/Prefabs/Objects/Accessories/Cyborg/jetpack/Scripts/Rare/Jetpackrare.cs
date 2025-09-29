using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Jetpack thruster FX driver for a prefab with two visual sets per nozzle:
///   Nozzle_[L/R]/Set_Normal/(coreflame_normal, outerflame_normal)
///   Nozzle_[L/R]/Set_Super /(coreflame_super,  outerflame_super)
///
/// Also auto-finds flat glow planes named "Thrusterhole_*":
///   • Visible only while flying
///   • Instantly scales to holeScaleSuper while in super speed
///   • Smoothly shrinks back to holeScaleNormal over holeScaleShrinkDuration seconds
///
/// Plus: drives "EnergyBall" meshes under an optional "charges" parent.
/// </summary>
[DisallowMultipleComponent]
public class JetpackRare : MonoBehaviour
{
    [Header("Auto-find (optional)")]
    [SerializeField] private PlayerFlight playerFlight;      // auto from parents if null
    [SerializeField] private Rigidbody    playerBody;        // auto from parents if null

    [Header("Speeds (fallbacks)")]
    [Tooltip("Read _fastSpeed/_superSpeed/_superActive/_idleAscending from PlayerFlight via reflection.")]
    [SerializeField] private bool  readFromPlayerFlight = true;
    [SerializeField] private float fastSpeed  = 20f;         // used if reflection off/unavailable
    [SerializeField] private float superSpeed = 40f;

    [Header("Normal set lifetimes (seconds)")]
    [SerializeField] private float idleCore   = 0.5f;
    [SerializeField] private float idleOuter  = 0.2f;
    [SerializeField] private float fastCore   = 0.7f;
    [SerializeField] private float fastOuter  = 0.5f;

    [Header("Super set lifetimes (seconds)")]
    [Tooltip("Applied to Set_Super cores when super turns on.")]
    [SerializeField] private float superCore  = 1.5f;
    [Tooltip("Applied to Set_Super outers when super turns on.")]
    [SerializeField] private float superOuter = 1.5f;

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

    [Header("Energy balls (optional)")]
    [Tooltip("If assigned, EnergyBall components are searched under this root; otherwise searched under the jetpack.")]
    [SerializeField] private Transform chargesRoot;

    [SerializeField] private bool holesForced = false;

    // ── cached sets ──────────────────────────────────────────────────────────────
    private readonly List<Transform> _normalRoots = new();
    private readonly List<Transform> _superRoots  = new();

    private readonly List<ParticleSystem> _normalCores  = new();
    private readonly List<ParticleSystem> _normalOuters = new();
    private readonly List<ParticleSystem> _superCores   = new();
    private readonly List<ParticleSystem> _superOuters  = new();
    private readonly List<Animator> _manualFlashAnims = new();
private readonly List<Animation> _manualFlashLegacies = new();
private readonly List<SpriteRenderer> _manualFlashRenderers = new();

    // Thruster hole glow planes
    private readonly List<GameObject> _thrusterHoles = new();

    // Energy balls
    private readonly List<EnergyBall> _energyBalls = new();

    // state
    private bool  _wasFlying = false;
    private bool  _visualsInSuper = false;   // which set is actively emitting
    private float _curCore, _curOuter;       // current lifetimes for the NORMAL set only

    // hole scale tween state
    private float _holeScaleCurrent;
    private float _holeScaleTarget;

    // reflection cache
    private FieldInfo _fiFast, _fiSuper, _fiSuperActive, _fiIdleAscending;

    private EnergyBallFlash[] _flashes;

    private readonly List<MeshRenderer> _energyBallMeshRenderers = new();

    private readonly List<ParticleSystem> _chargeDust = new();

    // True only while BoostJump (or anything) is actively requesting charge visuals.
// When false, JetpackRare will force-hide all charge VFX every frame.
private bool _chargeVisualsRequested = false;

    // ─────────────────────────────────────────────────────────────────────────────

    void Awake()
    { 
        _flashes = GetComponentsInChildren<EnergyBallFlash>(true);
        if (!playerFlight) playerFlight = GetComponentInParent<PlayerFlight>();
        if (!playerBody)   playerBody   = GetComponentInParent<Rigidbody>();

        BuildSetCaches();

        // Ensure local sim & emission off initially
        PrepareParticleList(_normalCores,  false, true);
        PrepareParticleList(_normalOuters, false, true);
        PrepareParticleList(_superCores,   false, true);
        PrepareParticleList(_superOuters,  false, true);

        // reflection hooks (optional)
        if (readFromPlayerFlight && playerFlight)
        {
            var t = playerFlight.GetType();
            _fiFast          = t.GetField("_fastSpeed",     BindingFlags.Instance | BindingFlags.NonPublic);
            _fiSuper         = t.GetField("_superSpeed",    BindingFlags.Instance | BindingFlags.NonPublic);
            _fiSuperActive   = t.GetField("_superActive",   BindingFlags.Instance | BindingFlags.NonPublic);
            _fiIdleAscending = t.GetField("_idleAscending", BindingFlags.Instance | BindingFlags.NonPublic);
            TrySyncSpeedsFromFlight();
        }

        // start at idle values but not emitting until flight begins
        _curCore  = idleCore;
        _curOuter = idleOuter;

        // Keep particle roots active (so super can fade), but hide glow planes until flight.
        SetRootsActive(_normalRoots, true);
        SetRootsActive(_superRoots,  true);

        _holeScaleCurrent = holeScaleNormal;
        _holeScaleTarget  = holeScaleNormal;
        SetHolesActive(false);
        SetHolesScale(_holeScaleCurrent);

        // Energy balls: cache and ensure OFF by default
        BuildEnergyBallCache();
        SetChargingVisuals(false);
    }

    void Update()
    {
        if (!playerFlight) return;

        TrySyncSpeedsFromFlight();

        bool isFlying = playerFlight.IsFlying;

        // handle enter/exit
        if (isFlying && !_wasFlying)
        {
            // Show glow planes in flight (at normal size)
            SetHolesActive(true);
            SetHolesInstant(holeScaleNormal);

            // Normal set on at idle values
            _curCore  = idleCore;
            _curOuter = idleOuter;
            ApplyLifetimeList(_normalCores,  _curCore,  force:true);
            ApplyLifetimeList(_normalOuters, _curOuter, force:true);

            EnableEmissionList(_normalCores,  enable:true, clear:true);
            EnableEmissionList(_normalOuters, enable:true, clear:true);

            // Make sure super isn't emitting
            EnableEmissionList(_superCores,   enable:false, clear:true);
            EnableEmissionList(_superOuters,  enable:false, clear:true);
            _visualsInSuper = false;
        }
        else if (!isFlying && _wasFlying)
        {
            // Hide glow planes when not flying, reset size
            SetHolesActive(false);
            SetHolesInstant(holeScaleNormal);

            // turn everything off cleanly
            EnableEmissionList(_normalCores,  enable:false, clear:true);
            EnableEmissionList(_normalOuters, enable:false, clear:true);
            EnableEmissionList(_superCores,   enable:false, clear:true);
            EnableEmissionList(_superOuters,  enable:false, clear:true);
            _visualsInSuper = false;
        }
        _wasFlying = isFlying;

        if (!isFlying)
        {
            // No flight, but keep tween book-keeping sane
            return;
        }

        // read current speed + states
        float speed = playerBody ? playerBody.velocity.magnitude : 0f;
        bool  superActive    = IsSuperActive(speed);
        bool  idleAscending  = IsIdleAscending();

        // ── SUPER: instant swap to super set
        if (superActive)
        {
            if (!_visualsInSuper)
            {
                // Hard-kill normal emission (let it fade naturally)
                EnableEmissionList(_normalCores,  enable:false, clear:false);
                EnableEmissionList(_normalOuters, enable:false, clear:false);

                // Apply inspector lifetimes to super and emit immediately
                ApplyLifetimeList(_superCores,  superCore,  force:true);
                ApplyLifetimeList(_superOuters, superOuter, force:true);
                EnableEmissionList(_superCores,  enable:true, clear:true);
                EnableEmissionList(_superOuters, enable:true, clear:true);

                // SNAP thruster-hole scale to super size
                SetHolesInstant(holeScaleSuper);

                _visualsInSuper = true;
            }
            // no normal lifetime blending while in super
        }
        else
        {
            // ── Exit super: smoothly restore normal set (super fades out)
            if (_visualsInSuper)
            {
                // restart normal where it left off (cur values keep blending)
                EnableEmissionList(_normalCores,  enable:true, clear:false);
                EnableEmissionList(_normalOuters, enable:true, clear:false);

                // stop new super particles, let existing ones die
                EnableEmissionList(_superCores,   enable:false, clear:false);
                EnableEmissionList(_superOuters,  enable:false, clear:false);

                // Start shrinking glow planes back to normal
                SetHoleTarget(holeScaleNormal);

                _visualsInSuper = false;
            }

            // ── NORMAL mode: compute targets
            float tgtCore, tgtOuter;
            if (idleAscending) // Space with no WASD ⇒ force “fast” look
            {
                tgtCore  = fastCore;
                tgtOuter = fastOuter;

                _curCore  = MoveToward(_curCore,  tgtCore,  lifetimeLerpSpeed * idleAscendToFastMultiplier);
                _curOuter = MoveToward(_curOuter, tgtOuter, lifetimeLerpSpeed * idleAscendToFastMultiplier);
            }
            else
            {
                float t = Mathf.InverseLerp(0f, Mathf.Max(0.01f, fastSpeed), speed);
                tgtCore  = Mathf.Lerp(idleCore,  fastCore,  t);
                tgtOuter = Mathf.Lerp(idleOuter, fastOuter, t);

                float coreSpeed  = lifetimeLerpSpeed * (_curCore  < tgtCore  ? speedUpMultiplier : 1f);
                float outerSpeed = lifetimeLerpSpeed * (_curOuter < tgtOuter ? speedUpMultiplier : 1f);

                _curCore  = MoveToward(_curCore,  tgtCore,  coreSpeed);
                _curOuter = MoveToward(_curOuter, tgtOuter, outerSpeed);
            }

            // apply to normal set only
            ApplyLifetimeList(_normalCores,  _curCore,  force:false);
            ApplyLifetimeList(_normalOuters, _curOuter, force:false);
        }

if (holesForced) SetHolesActive(true); // override wins

        // ── Hole scale tween (only smooth when shrinking)
        TickHoleScale();
    }

    void BuildEnergyBallCache()
{
    _energyBalls.Clear();
    _energyBallMeshRenderers.Clear();

    Transform root = chargesRoot ? chargesRoot : transform;

    // EnergyBall components (if any still exist)
    root.GetComponentsInChildren(true, _energyBalls);

    // MeshRenderers that look like the orb meshes
    foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
    {
        var n = mr.gameObject.name.ToLowerInvariant();
        if (n.Contains("energyball")) _energyBallMeshRenderers.Add(mr);
    }
}

/// <summary>
/// Show/hide the visible orb meshes while charging.
/// Works whether or not the old EnergyBall component exists.
/// </summary>
public void SetEnergyBallMeshesVisible(bool on)
{
    // This is the authoritative flag we’ll enforce even if other systems
    // re-enable the renderers (e.g., EquipmentManager on TP return).
    _chargeVisualsRequested = on;

    if (_energyBallMeshRenderers.Count == 0 || _energyBalls.Count == 0)
        BuildEnergyBallCache();

    // Mesh renderers
    for (int i = 0; i < _energyBallMeshRenderers.Count; i++)
    {
        var mr = _energyBallMeshRenderers[i];
        if (!mr) continue;
        mr.enabled = on;
    }

    // EnergyBall components (if present)
    for (int i = 0; i < _energyBalls.Count; i++)
    {
        var eb = _energyBalls[i];
        if (!eb) continue;
        eb.SetCharging(on);
    }

    // Be defensive: when turning OFF charge, also hard-stop any dust/flash
    // so they can’t linger if accessories get re-enabled later.
    if (!on)
    {
        SetChargeDustVisible(false);
        SetChargedFlash(false);
    }
}

void BuildChargeDustCache()
{
    _chargeDust.Clear();

    Transform root = chargesRoot ? chargesRoot : transform;

    foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
    {
        string n = ps.gameObject.name.ToLowerInvariant();
        if (n.Contains("dust")) _chargeDust.Add(ps); // matches FX_dust_L / FX_dust_R / etc.
    }
}

/// <summary>Show/Hide the charge dust FX. Safe to call anytime.</summary>
public void SetChargeDustVisible(bool on)
{
    if (_chargeDust.Count == 0) BuildChargeDustCache();

    for (int i = 0; i < _chargeDust.Count; i++)
    {
        var ps = _chargeDust[i];
        if (!ps) continue;

        var go = ps.gameObject;

        if (on)
        {
            // 1) Make sure the GO is active BEFORE touching the particle system.
            if (!go.activeSelf) go.SetActive(true);

            // 2) Enable emission and restart clean.
            var em = ps.emission; 
            em.enabled = true;

            ps.Clear(true);
            ps.Play(true);
        }
        else
        {
            // 1) Stop and clear first (while still active).
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // 2) Disable emission so it stays off when re-enabled later.
            var em = ps.emission; 
            em.enabled = false;

            // 3) Optionally deactivate the GO.
            if (go.activeSelf) go.SetActive(false);
        }
    }
}

/// <summary>
/// Force the thruster-hole glow planes ON regardless of flying state.
/// Pass false to release the override (normal flight rules resume).
/// </summary>
public void SetThrusterHolesForced(bool on)
{
    holesForced = on;

    if (on)
    {
        // Ensure visible at normal size immediately
        SetHolesActive(true);
        SetHolesInstant(holeScaleNormal);
    }
    else
    {
        // If we’re not flying, hide; if flying, normal Update will keep them as needed
        if (!playerFlight || !playerFlight.IsFlying)
        {
            SetHolesActive(false);
            SetHolesInstant(holeScaleNormal);
        }
    }
}

    /// <summary>Called by BoostJump to show/hide & animate the energy balls while charging.</summary>
    public void SetChargingVisuals(bool charging)
    {
        if (_energyBalls.Count == 0) BuildEnergyBallCache();
        for (int i = 0; i < _energyBalls.Count; i++)
        {
            var eb = _energyBalls[i];
            if (!eb) continue;
            eb.SetCharging(charging);
        }
    }

    // ───────────────────────── helpers ─────────────────────────

    static float MoveToward(float current, float target, float unitsPerSecond)
        => Mathf.MoveTowards(current, target, unitsPerSecond * Time.deltaTime);

    void BuildSetCaches()
    {
        _normalRoots.Clear(); _superRoots.Clear();
        _normalCores.Clear(); _normalOuters.Clear();
        _superCores.Clear();  _superOuters.Clear();
        _thrusterHoles.Clear();

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (n == "set_normal") _normalRoots.Add(t);
            else if (n == "set_super") _superRoots.Add(t);

            if (n.Contains("thrusterhole"))
                _thrusterHoles.Add(t.gameObject);
        }

        void Collect(Transform root, List<ParticleSystem> cores, List<ParticleSystem> outers)
        {
            if (!root) return;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                string pn = ps.name.ToLowerInvariant();
                if (pn.Contains("coreflame")) cores.Add(ps);
                else if (pn.Contains("outerflame")) outers.Add(ps);
            }
        }

        foreach (var r in _normalRoots) Collect(r, _normalCores, _normalOuters);
        foreach (var r in _superRoots)  Collect(r, _superCores,  _superOuters);
    }

    static void PrepareParticleList(List<ParticleSystem> list, bool playOnAwake, bool emissionOff)
    {
        foreach (var ps in list)
        {
            if (!ps) continue;
            var m = ps.main;
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            m.playOnAwake     = playOnAwake;     // we explicitly Play() anyway
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

    void SetHolesActive(bool active)
    {
        for (int i = 0; i < _thrusterHoles.Count; i++)
        {
            var go = _thrusterHoles[i];
            if (!go) continue;

            var r = go.GetComponent<Renderer>();
            if (r != null) r.enabled = active;
            else           go.SetActive(active);
        }
    }

    void SetHolesScale(float uniformScale)
    {
        Vector3 s = new Vector3(uniformScale, uniformScale, uniformScale);
        for (int i = 0; i < _thrusterHoles.Count; i++)
        {
            var go = _thrusterHoles[i];
            if (!go) continue;
            go.transform.localScale = s;
        }
    }

    // Tween helpers for hole scale
    void SetHolesInstant(float scale)
    {
        _holeScaleCurrent = scale;
        _holeScaleTarget  = scale;
        SetHolesScale(scale);
    }

    void SetHoleTarget(float target)
    {
        _holeScaleTarget = target; // current remains as-is; Update will tween toward target
    }

    void TickHoleScale()
    {
        if (Mathf.Approximately(_holeScaleCurrent, _holeScaleTarget)) return;

        // only smooth when shrinking (current > target); grow snaps elsewhere via SetHolesInstant
        if (_holeScaleCurrent > _holeScaleTarget)
        {
            float totalDelta = Mathf.Abs(holeScaleSuper - holeScaleNormal);
            float rate = (totalDelta / Mathf.Max(0.0001f, holeScaleShrinkDuration));
            _holeScaleCurrent = Mathf.MoveTowards(_holeScaleCurrent, _holeScaleTarget, rate * Time.deltaTime);
            SetHolesScale(_holeScaleCurrent);
        }
        else
        {
            // If somehow growing here, just snap
            SetHolesInstant(_holeScaleTarget);
        }
    }

    // ── PlayerFlight reflection & fallbacks ─────────────────────

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
        // fallback: near-super speed threshold
        return currentSpeed >= superSpeed * 0.98f;
    }

    bool IsIdleAscending()
    {
        if (readFromPlayerFlight && playerFlight && _fiIdleAscending != null)
        {
            try { return playerFlight.IsFlying && (bool)_fiIdleAscending.GetValue(playerFlight); }
            catch { /* fall through */ }
        }

        // Fallback heuristic: Space held, no WASD, while flying
        if (!playerFlight || !playerFlight.IsFlying) return false;
        bool noWASD = Mathf.Abs(Input.GetAxis("Horizontal")) < 0.001f &&
                      Mathf.Abs(Input.GetAxis("Vertical"))   < 0.001f;
        return noWASD && Input.GetKey(KeyCode.Space);
    }

    private void BuildManualFlashCache()
{
    _manualFlashAnims.Clear();
    _manualFlashLegacies.Clear();
    _manualFlashRenderers.Clear();

    Transform root = chargesRoot ? chargesRoot : transform;

    // Find any flash objects by name; keep it loose (contains "flash")
    foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
    {
        string n = sr.gameObject.name.ToLowerInvariant();
        if (n.Contains("flash"))
        {
            _manualFlashRenderers.Add(sr);

            var a = sr.GetComponent<Animator>();
            if (a) _manualFlashAnims.Add(a);

            var legacy = sr.GetComponent<Animation>();
            if (legacy) _manualFlashLegacies.Add(legacy);
        }
    }
}

    /// <summary>
/// Turn the charged flash on/off. Works with EnergyBallFlash *and* your manual
/// SHEET_energyball_flash_* sprites that auto-animate in play mode.
/// </summary>
public void SetChargedFlash(bool on)
{
    // Flash ON also implies we’re in a charge state.
    if (on) _chargeVisualsRequested = true;

    // Existing EnergyBallFlash support (kept)
    if (_flashes == null || _flashes.Length == 0)
        _flashes = GetComponentsInChildren<EnergyBallFlash>(true);
    for (int i = 0; i < _flashes.Length; i++)
    {
        var f = _flashes[i];
        if (!f) continue;
        if (on && !f.gameObject.activeSelf) f.gameObject.SetActive(true);
        f.SetCharged(on);
    }

    // Manual flash sprites / animators / legacy animation
    if (_manualFlashAnims.Count == 0 &&
        _manualFlashLegacies.Count == 0 &&
        _manualFlashRenderers.Count == 0)
    {
        BuildManualFlashCache();
    }

    // Renderers
    for (int i = 0; i < _manualFlashRenderers.Count; i++)
    {
        var sr = _manualFlashRenderers[i];
        if (!sr) continue;
        sr.enabled = on;
        if (on && !sr.gameObject.activeSelf)
            sr.gameObject.SetActive(true);
    }

    // Mecanim animators
    for (int i = 0; i < _manualFlashAnims.Count; i++)
    {
        var a = _manualFlashAnims[i];
        if (!a) continue;

        if (on)
        {
            a.enabled = true;
            var st = a.GetCurrentAnimatorStateInfo(0);
            a.Play(st.shortNameHash, 0, 0f);
            a.Update(0f);
            a.speed = 1f;
        }
        else
        {
            a.speed = 0f;
            a.Play(a.GetCurrentAnimatorStateInfo(0).shortNameHash, 0, 0f);
            a.Update(0f);
            a.enabled = false;
        }
    }

    // Legacy Animation
    for (int i = 0; i < _manualFlashLegacies.Count; i++)
    {
        var anim = _manualFlashLegacies[i];
        if (!anim) continue;

        if (on)
        {
            foreach (AnimationState s in anim) s.time = 0f;
            anim.enabled = true;
            anim.Play();
        }
        else
        {
            anim.Stop();
            foreach (AnimationState s in anim) s.time = 0f;
            anim.enabled = false;
        }
    }
}

private void LateUpdate()
{
    // 1) If NO system is requesting charge visuals, enforce OFF every frame.
    //    (Prevents FP↔TP toggles from resurrecting orb/dust/flash.)
    if (!_chargeVisualsRequested)
        EnsureChargeVisualsOff();

    // 2) Thruster holes should only be visible when:
    //      • playerFlight.IsFlying, OR
    //      • holesForced == true (temporary override from EnergyBall intro gate)
    //    Otherwise, enforce them OFF every frame so accessory toggles can’t re-enable.
    EnsureThrusterHolesHiddenIfNotFlying();
}

private void EnsureChargeVisualsOff()
{
    // Mesh renderers
    if (_energyBallMeshRenderers.Count == 0 || _energyBalls.Count == 0)
        BuildEnergyBallCache();

    for (int i = 0; i < _energyBallMeshRenderers.Count; i++)
    {
        var mr = _energyBallMeshRenderers[i];
        if (!mr) continue;
        if (mr.enabled) mr.enabled = false;
    }

    // EnergyBall components (stop animation + hide)
    for (int i = 0; i < _energyBalls.Count; i++)
    {
        var eb = _energyBalls[i];
        if (!eb) continue;
        eb.SetCharging(false);
    }

    // Dust & flashes OFF
    SetChargeDustVisible(false);
    // Don’t call SetChargedFlash(false) indirectly (it sets flags),
    // just hard-force off via the existing method:
    SetChargedFlash(false);
}

private void EnsureThrusterHolesHiddenIfNotFlying()
{
    // If we’re force-showing, or actually flying, let Update() own the state.
    if (holesForced) return;
    if (playerFlight != null && playerFlight.IsFlying) return;

    // Not flying and no override → hard-clamp holes OFF and reset scale.
    SetHolesActive(false);
    SetHolesInstant(holeScaleNormal);
}

#if UNITY_EDITOR
    void OnValidate()
    {
        fastSpeed  = Mathf.Max(0f, fastSpeed);
        superSpeed = Mathf.Max(fastSpeed, superSpeed);

        lifetimeLerpSpeed          = Mathf.Max(0f, lifetimeLerpSpeed);
        speedUpMultiplier          = Mathf.Max(0f, speedUpMultiplier);
        idleAscendToFastMultiplier = Mathf.Max(0f, idleAscendToFastMultiplier);

        idleCore   = Mathf.Max(0f, idleCore);
        idleOuter  = Mathf.Max(0f, idleOuter);
        fastCore   = Mathf.Max(0f, fastCore);
        fastOuter  = Mathf.Max(0f, fastOuter);
        superCore  = Mathf.Max(0f, superCore);
        superOuter = Mathf.Max(0f, superOuter);

        holeScaleNormal        = Mathf.Max(0f, holeScaleNormal);
        holeScaleSuper         = Mathf.Max(0f, holeScaleSuper);
        holeScaleShrinkDuration= Mathf.Max(0.0001f, holeScaleShrinkDuration);
    }
#endif
}