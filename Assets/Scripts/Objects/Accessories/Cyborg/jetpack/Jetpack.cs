using System;
using UnityEngine;
using UnityEngine.UI;

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

    // ───────────────────────── Fuel settings ─────────────────────────
    [Header("Fuel")]
    [Min(0f)] [SerializeField] private float maxFuel = 100f;
    [Min(0f)] [SerializeField] private float startFuel = 100f;

    [Tooltip("Fuel consumed per second while flying (normal).")]
    [Min(0f)] [SerializeField] private float consumePerSecond = 10f;

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

    // ───────────────────────── HUD spawning ─────────────────────────
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

    // external super notify (optional)
    private bool _superNotified;
    private float _superNotifiedUntilTime;

    // reflection cache
    private System.Reflection.FieldInfo _fiSuperActive;
    private System.Reflection.PropertyInfo _piIsInSuper;

    // ───────────────────────── Unity lifecycle ─────────────────────────
    void Awake()
    {
        // Clamp and seed fuel
        maxFuel = Mathf.Max(0f, maxFuel);
        startFuel = Mathf.Clamp(startFuel, 0f, maxFuel);
        CurrentFuel = startFuel;

        TryBindPlayerRefs();
    }

    void OnEnable()
    {
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

        if (flying)
        {
            // pick correct drain
            float drain = IsSuperActive() ? consumePerSecondSuper : consumePerSecond;
            if (drain > 0f) ApplyFuelDelta(-drain * dt);

            // hit empty?
            if (CurrentFuel <= 0.0001f && lockFlightWhenEmpty)
            {
                // Locking flight forces exit via PlayerFlight.SetFlightUnlocked logic
                _playerFlight.LockFlight();
            }
        }
        else
        {
            // regen whenever NOT flying (grounded or not)
            if (regenPerSecond > 0f && CurrentFuel < maxFuel)
                ApplyFuelDelta(regenPerSecond * dt);
        }

        // expire external super notify after a short grace window
        if (_superNotified && Time.time >= _superNotifiedUntilTime)
            _superNotified = false;
    }

    // ───────────────────────── Public hooks ─────────────────────────

    /// <summary>
    /// Optional: let PlayerFlight (or any script) tell us when super speed toggles.
    /// We'll treat 'active' as true for ~0.25s unless called again.
    /// </summary>
    public void NotifySuperSpeed(bool active, float holdForSeconds = 0.25f)
    {
        _superNotified = active;
        _superNotifiedUntilTime = active ? (Time.time + Mathf.Max(0.05f, holdForSeconds)) : Time.time;
    }

    /// <summary>
    /// Force-set fuel (0..max). Useful for cheats/testing.
    /// </summary>
    public void SetFuel(float value)
    {
        float clamped = Mathf.Clamp(value, 0f, maxFuel);
        if (!Mathf.Approximately(clamped, CurrentFuel))
            ApplyFuelSet(clamped);
    }

    /// <summary>
    /// Add (or subtract) fuel, clamped to 0..max.
    /// </summary>
    public void AddFuel(float delta)
    {
        ApplyFuelDelta(delta);
    }

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

    bool IsSuperActive()
    {
        // 1) external notify (authoritative if recently set)
        if (_superNotified) return true;

        // 2) reflection against PlayerFlight
        if (detectSuperByReflection && _playerFlight != null)
        {
            try
            {
                // Property route first
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

                // Private field route (matches your script name)
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

        // 3) velocity heuristic fallback
        if (superSpeedVelocityThreshold > 0f && _playerRb != null)
        {
            if (_playerRb.velocity.sqrMagnitude >= superSpeedVelocityThreshold * superSpeedVelocityThreshold)
                return true;
        }

        return false;
    }

    void TryBindPlayerRefs()
    {
        if (_playerFlight == null)
        {
            // prefer parent chain (jetpack mounted under player)
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

    // ───────────────────────── Editor niceties ─────────────────────────
#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            maxFuel = Mathf.Max(0f, maxFuel);
            startFuel = Mathf.Clamp(startFuel, 0f, maxFuel);

            // keep preview values sensible in the editor
            if (CurrentFuel <= 0f || CurrentFuel > maxFuel)
                CurrentFuel = startFuel;
        }
    }
#endif
}