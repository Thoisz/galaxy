using System.Linq;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class JetpackWingsController : MonoBehaviour
{
    [Header("Animator")]
    [Tooltip("Animator on the shared armature that plays IdleIn/FoldOut/IdleOut/FoldIn.")]
    [SerializeField] private Animator armatureAnimator;

    [Tooltip("Bool parameter that drives your Animator transitions.")]
    [SerializeField] private string isFlyingParam = "IsFlying";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private PlayerFlight _playerFlight;
    private Rigidbody _rb;
    private bool _lastFlying = false;

    // reflection cache (fallback path if your API differs)
    PropertyInfo _piIsFlying;
    MethodInfo   _miIsFlying;
    FieldInfo    _fiIsFlying;

    void Awake()
    {
        if (!armatureAnimator) armatureAnimator = GetComponentInChildren<Animator>(true);
        ResolvePlayerFlight(true);

        // prime animator once
        bool flying = ReadIsFlying();
        SetAnimatorBool(flying, immediate:true);
        _lastFlying = flying;
    }

    void OnEnable()
    {
        // re-bind in case prefab just got parented under player
        ResolvePlayerFlight(true);

        bool flying = ReadIsFlying();
        SetAnimatorBool(flying, immediate:true);
        _lastFlying = flying;
    }

    void Update()
    {
        if (!_playerFlight || !_rb)
            ResolvePlayerFlight(false);

        bool flying = ReadIsFlying();
        if (flying != _lastFlying)
        {
            if (debugLogs) Debug.Log($"[Wings] IsFlying changed -> {flying}", this);
            SetAnimatorBool(flying, immediate:false);
            _lastFlying = flying;
        }
    }

    // ───────────────────────── Bind helpers ─────────────────────────
    void ResolvePlayerFlight(bool log)
    {
        var before = _playerFlight;

        // 1) Prefer parent chain (typical when the jetpack prefab is attached to a player bone)
        if (!_playerFlight)
            _playerFlight = GetComponentInParent<PlayerFlight>(true);

        // 2) Try PlayerEquipment anchor (your project uses a singleton)
        if (!_playerFlight && PlayerEquipment.Instance)
            _playerFlight = PlayerEquipment.Instance.GetComponentInChildren<PlayerFlight>(true);

        // 3) Try tag "Player" (and search under it)
        if (!_playerFlight)
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged)
                _playerFlight = tagged.GetComponentInChildren<PlayerFlight>(true);
        }

        // 4) Last resort: find the closest active PlayerFlight in the scene
#if UNITY_2022_2_OR_NEWER
        if (!_playerFlight)
            _playerFlight = Object.FindAnyObjectByType<PlayerFlight>(FindObjectsInactive.Exclude);
#else
        if (!_playerFlight)
            _playerFlight = Object.FindObjectOfType<PlayerFlight>();
#endif

        // cache rigidbody for potential “idle” heuristics if needed later
        _rb = _playerFlight ? _playerFlight.GetComponentInParent<Rigidbody>() : null;

        // build reflection fallbacks
        _piIsFlying = null; _miIsFlying = null; _fiIsFlying = null;
        if (_playerFlight)
        {
            var t = _playerFlight.GetType();
            _piIsFlying = t.GetProperty("IsFlying", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_piIsFlying == null)
                _miIsFlying = t.GetMethod("IsFlying", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null);
            if (_piIsFlying == null && _miIsFlying == null)
            {
                // common private field names we’ll try
                var names = new[] { "_isFlying", "isFlying", "_inFlight", "inFlight" };
                foreach (var n in names)
                {
                    var f = t.GetField(n, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (f != null && f.FieldType == typeof(bool)) { _fiIsFlying = f; break; }
                }
            }
        }

        if (log || debugLogs)
        {
            if (_playerFlight && before != _playerFlight)
                Debug.Log($"[Wings] Bound to PlayerFlight on: {_playerFlight.gameObject.name}", this);
            if (!_playerFlight)
                Debug.LogWarning("[Wings] Could not find PlayerFlight. Animator won’t update.", this);
        }
    }

    bool ReadIsFlying()
    {
        if (_playerFlight == null) return false;

        // primary path: public property
        try
        {
            if (_piIsFlying != null)
            {
                var v = _piIsFlying.GetValue(_playerFlight, null);
                if (v is bool b) return b;
            }
        }
        catch {}

        // method path (IsFlying())
        try
        {
            if (_miIsFlying != null)
            {
                var v = _miIsFlying.Invoke(_playerFlight, null);
                if (v is bool b) return b;
            }
        }
        catch {}

        // field path
        try
        {
            if (_fiIsFlying != null)
            {
                var v = _fiIsFlying.GetValue(_playerFlight);
                if (v is bool b) return b;
            }
        }
        catch {}

        // if all else fails but your class exposes a public property (as your fuel script suggests),
        // read it directly to catch compile-time API:
        try
        {
            // If your PlayerFlight actually has a public bool IsFlying {get;}
            return (bool)_playerFlight.GetType().GetProperty("IsFlying")?.GetValue(_playerFlight, null);
        }
        catch {}

        return false;
    }

    void SetAnimatorBool(bool isFlying, bool immediate)
    {
        if (!armatureAnimator || string.IsNullOrEmpty(isFlyingParam)) return;

        // If the parameter doesn’t exist, Unity will log a warning.
        // Make sure your Animator Controller defines a Bool named exactly like isFlyingParam.
        armatureAnimator.SetBool(isFlyingParam, isFlying);

        // If you also want to “force” a particular state instantly on enable,
        // you can crossfade directly here (optional):
        // if (immediate) armatureAnimator.Play(isFlying ? "IdleOut" : "IdleIn", 0, 0f);
    }
}