using System;
using UnityEngine;

[DisallowMultipleComponent]
public class Jetpack : MonoBehaviour
{
    // ───────────────────────── Gameplay toggles ─────────────────────────
    [Header("Flight Gating")]
    [Tooltip("Unlock PlayerFlight while this Jetpack is equipped/enabled.")]
    [SerializeField] private bool unlockFlightOnEnable = true;

    [Tooltip("Re-lock PlayerFlight when this Jetpack is unequipped/disabled.")]
    [SerializeField] private bool relockFlightOnDisable = true;

    [Tooltip("If fuel hits 0 while flying, Jetpack locks flight (PlayerFlight will exit).")]
    [SerializeField] private bool lockFlightWhenEmpty = true;

    [Tooltip("If we locked flight because fuel hit 0, automatically unlock again once fuel > 0.")]
    [SerializeField] private bool autoUnlockWhenRefueled = true;

    // ───────────────────────── Fuel settings ─────────────────────────
    [Header("Fuel")]
    [Min(0f)] [SerializeField] private float maxFuel = 100f;
    [Min(0f)] [SerializeField] private float startFuel = 100f;

    [Tooltip("Fuel consumed per second while flying (normal).")]
    [Min(0f)] [SerializeField] private float consumePerSecond = 10f;

    [Tooltip("Fuel consumed per second while FLYING but not moving.")]
    [Min(0f)] [SerializeField] private float consumePerSecondIdle = 5f;

    [Tooltip("Extra-hungry rate while SUPER SPEED is active.")]
    [Min(0f)] [SerializeField] private float consumePerSecondSuper = 20f;

    [Tooltip("Fuel regenerated per second while NOT flying (grounded or not).")]
    [Min(0f)] [SerializeField] private float regenPerSecond = 12f;

    [Tooltip("UI/logic threshold that marks 'low fuel' (0.15 = 15%).")]
    [Range(0f, 1f)] [SerializeField] private float lowFuelPercent = 0.15f;

    // ───────────────────────── Super-speed detection (optional) ─────────────────────────
    [Header("Super Speed Detection (optional)")]
    [Tooltip("Best-effort: read PlayerFlight's private '_superActive' via reflection if present.")]
    [SerializeField] private bool detectSuperByReflection = true;

    [Tooltip("Fallback: treat as 'super' if player Rigidbody speed exceeds this (m/s). Set 0 to disable.")]
    [Min(0f)] [SerializeField] private float superSpeedVelocityThreshold = 0f;

    // ───────────────────────── HUD spawning (optional) ─────────────────────────
    [Header("HUD Spawning (optional)")]
    [Tooltip("UI prefab containing a FuelBarUI script. If assigned, the bar is auto-spawned under HUD.")]
    [SerializeField] private FuelBarUI fuelBarPrefab;

    [Tooltip("Target Canvas for the fuel bar. Leave empty to auto-find a Canvas tagged 'HUD' or any Canvas.")]
    [SerializeField] private Canvas hudCanvas;

    [Tooltip("Destroy the spawned bar when the Jetpack disables/unequips.")]
    [SerializeField] private bool destroyBarOnDisable = true;

    // ───────────────────────── Runtime ─────────────────────────
    public float CurrentFuel { get; private set; }
    public float MaxFuel => maxFuel;
    public bool IsLowFuel => (maxFuel > 0f) && (CurrentFuel / maxFuel <= lowFuelPercent);

    public event Action<float, float> FuelChanged; // (current, max)
    public event Action<bool> LowFuelChanged;      // low?

    private bool _lastLowFuel;
    private PlayerFlight _playerFlight;
    private Rigidbody _playerRb;
    private FuelBarUI _spawnedBar;

    // did we lock flight because fuel hit 0?
    private bool _lockedByEmpty;

    // external super notify (optional)
    private bool _superNotified;
    private float _superNotifiedUntilTime;

    // reflection cache
    private System.Reflection.FieldInfo _fiSuperActive;
    private System.Reflection.PropertyInfo _piIsInSuper;

    // Small internal epsilon to decide "not moving" without exposing a slider
    private const float IDLE_SPEED_EPS = 0.08f; // m/s

    // ───────────────────────── Unity lifecycle ─────────────────────────
    void Awake()
    {
        maxFuel = Mathf.Max(0f, maxFuel);
        startFuel = Mathf.Clamp(startFuel, 0f, maxFuel);
        CurrentFuel = startFuel;

        TryBindPlayerRefs();
    }

    void OnEnable()
    {
        _lockedByEmpty = false; // fresh start

        TryBindPlayerRefs();

        if (unlockFlightOnEnable)
            _playerFlight?.UnlockFlight();

        // Spawn fuel HUD
        SpawnFuelBar();

        // Prime UI state
        _lastLowFuel = IsLowFuel;
        FuelChanged?.Invoke(CurrentFuel, maxFuel);
        LowFuelChanged?.Invoke(_lastLowFuel);
    }

    void OnDisable()
    {
        if (relockFlightOnDisable)
            _playerFlight?.LockFlight();

        if (destroyBarOnDisable)
            DespawnFuelBar();
    }

    void Update()
{
    TryBindPlayerRefs(); // safe/no-op if already bound

    bool flying = _playerFlight != null && _playerFlight.IsFlying;
    float dt = Time.deltaTime;

    // ── Drain / Regen ──────────────────────────────────────────────────────
    if (flying)
    {
        // decide which drain to use
        float drain;
        if (IsSuperActive())
            drain = consumePerSecondSuper;
        else if (IsIdleWhileFlying())
            drain = consumePerSecondIdle;
        else
            drain = consumePerSecond;

        if (drain > 0f) ApplyFuelDelta(-drain * dt);

        // hit empty? force a lock (so flight exits)
        if (lockFlightWhenEmpty && CurrentFuel <= 0.0001f && !_lockedByEmpty)
        {
            _playerFlight.LockFlight();
            _lockedByEmpty = true;
        }
    }
    else
    {
        // regen whenever NOT flying (grounded or not)
        if (regenPerSecond > 0f && CurrentFuel < maxFuel)
            ApplyFuelDelta(regenPerSecond * dt);
    }

    // ── Re-entry gating (the fix) ──────────────────────────────────────────
    // Only allow re-entering flight once we have at least 10% fuel.
    // While NOT flying and below threshold → keep flight LOCKED.
    // At/above threshold → (optionally) unlock again.
    const float MIN_REENTER_PERCENT = 0.10f;       // 10%
    float thresholdFuel = maxFuel * MIN_REENTER_PERCENT;

    if (_playerFlight != null && !flying)
    {
        if (CurrentFuel + 0.0001f < thresholdFuel)
        {
            // below 10%: keep locked so you can't start flying yet
            _playerFlight.LockFlight();
            _lockedByEmpty = true; // reuse flag so any other logic knows we're locked
        }
        else if (autoUnlockWhenRefueled)
        {
            // ≥ 10%: permit flight again
            _playerFlight.UnlockFlight();
            _lockedByEmpty = false;
        }
    }

    // ── Expire external "super" notify ─────────────────────────────────────
    if (_superNotified && Time.time >= _superNotifiedUntilTime)
        _superNotified = false;
}

    // ───────────────────────── Public hooks ─────────────────────────
    public void NotifySuperSpeed(bool active, float holdForSeconds = 0.25f)
    {
        _superNotified = active;
        _superNotifiedUntilTime = active ? (Time.time + Mathf.Max(0.05f, holdForSeconds)) : Time.time;
    }

    public void SetFuel(float value)
    {
        float clamped = Mathf.Clamp(value, 0f, maxFuel);
        if (!Mathf.Approximately(clamped, CurrentFuel))
            ApplyFuelSet(clamped);
    }

    public void AddFuel(float delta) => ApplyFuelDelta(delta);

    // ───────────────────────── Internals ─────────────────────────
    void ApplyFuelDelta(float delta)
    {
        if (Mathf.Abs(delta) <= float.Epsilon) return;
        ApplyFuelSet(Mathf.Clamp(CurrentFuel + delta, 0f, maxFuel));
    }

    void ApplyFuelSet(float newValue)
    {
        CurrentFuel = newValue;
        FuelChanged?.Invoke(CurrentFuel, maxFuel);

        bool lowNow = IsLowFuel;
        if (lowNow != _lastLowFuel)
        {
            _lastLowFuel = lowNow;
            LowFuelChanged?.Invoke(lowNow);
        }
    }

    bool IsIdleWhileFlying()
    {
        if (_playerRb == null) return false;
        return _playerRb.velocity.sqrMagnitude < (IDLE_SPEED_EPS * IDLE_SPEED_EPS);
    }

    bool IsSuperActive()
    {
        if (_superNotified) return true;

        if (detectSuperByReflection && _playerFlight != null)
        {
            try
            {
                if (_piIsInSuper == null)
                {
                    _piIsInSuper = _playerFlight.GetType().GetProperty("IsInSuperSpeed",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                }
                if (_piIsInSuper != null)
                {
                    object val = _piIsInSuper.GetValue(_playerFlight, null);
                    if (val is bool pb && pb) return true;
                }

                if (_fiSuperActive == null)
                {
                    _fiSuperActive = _playerFlight.GetType().GetField("_superActive",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                }
                if (_fiSuperActive != null)
                {
                    object val = _fiSuperActive.GetValue(_playerFlight);
                    if (val is bool fb && fb) return true;
                }
            }
            catch { /* ignore */ }
        }

        if (superSpeedVelocityThreshold > 0f && _playerRb != null)
            return _playerRb.velocity.sqrMagnitude >= superSpeedVelocityThreshold * superSpeedVelocityThreshold;

        return false;
    }

    void TryBindPlayerRefs()
    {
        if (_playerFlight == null)
        {
            _playerFlight = GetComponentInParent<PlayerFlight>();
            if (_playerFlight == null)
                _playerFlight = FindObjectOfType<PlayerFlight>();
        }
        if (_playerRb == null && _playerFlight != null)
        {
            _playerRb = _playerFlight.GetComponent<Rigidbody>();
            if (_playerRb == null)
                _playerRb = _playerFlight.GetComponentInParent<Rigidbody>();
        }
    }

    // ───────────────────────── HUD helpers ─────────────────────────
    void SpawnFuelBar()
    {
        if (_spawnedBar != null || fuelBarPrefab == null) return;

        Transform parent = hudCanvas ? hudCanvas.transform : FindHUDCanvas();
        if (parent == null)
        {
            Debug.LogWarning("Jetpack: No Canvas found to parent the FuelBarUI. Assign a HUD Canvas or tag one as 'HUD'.");
            return;
        }

        _spawnedBar = Instantiate(fuelBarPrefab, parent);
        _spawnedBar.name = "FuelBarUI (Jetpack)";
        _spawnedBar.Initialize(this);
    }

    void DespawnFuelBar()
    {
        if (_spawnedBar == null) return;
        Destroy(_spawnedBar.gameObject);
        _spawnedBar = null;
    }

    Transform FindHUDCanvas()
    {
        var tagged = GameObject.FindGameObjectWithTag("HUD");
        if (tagged)
        {
            var cv = tagged.GetComponent<Canvas>();
            if (cv) return cv.transform;
        }

        var any = FindObjectOfType<Canvas>();
        return any ? any.transform : null;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            maxFuel = Mathf.Max(0f, maxFuel);
            startFuel = Mathf.Clamp(startFuel, 0f, maxFuel);

            if (CurrentFuel <= 0f || CurrentFuel > maxFuel)
                CurrentFuel = startFuel;
        }

        consumePerSecond      = Mathf.Max(0f, consumePerSecond);
        consumePerSecondIdle  = Mathf.Max(0f, consumePerSecondIdle);
        consumePerSecondSuper = Mathf.Max(0f, consumePerSecondSuper);
        regenPerSecond        = Mathf.Max(0f, regenPerSecond);
        lowFuelPercent        = Mathf.Clamp01(lowFuelPercent);
    }
#endif
}