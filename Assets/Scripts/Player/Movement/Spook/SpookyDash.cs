using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpookyDash : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-grabbed if left null.")]
    [SerializeField] private PlayerDash _playerDash;
    [Tooltip("Optional: if assigned, we’ll toggle its camera-collision mask to ignore Phaseable while phasing.")]
    [SerializeField] private PlayerCamera _playerCamera;

    [Header("Phase (collision)")]
    [SerializeField] private string _playerPhaseLayerName = "PlayerPhase";
    [Tooltip("Remain intangible this long after a spooky dash ends to avoid snagging in colliders.")]
    [SerializeField] private float _phaseGraceAfterDash = 0.12f;

    [Header("Spooky Cooldown")]
    [Tooltip("Seconds before the next spooky dash becomes available again.")]
    [SerializeField] private float _specialCooldown = 15f;

    [Header("Afterimage (Ectoplasm Wisp Trail) — ONLY for spooky dash")]
    [SerializeField] private bool _afterimageEnabled = true;
    [Tooltip("Unlit Transparent/Additive material. Its color & alpha set the max tint/opacity.")]
    [SerializeField] private Material _afterimageMaterial;
    [SerializeField] private float _spawnInterval = 0.03f;
    [SerializeField] private float _ghostLifetime = 0.25f;
    [SerializeField] private AnimationCurve _alphaOverLife = null; // 0..1
    [SerializeField, Range(0f, 0.25f)] private float _scaleJitter = 0.04f;
    [SerializeField, Range(0f, 0.10f)] private float _posJitter = 0.02f;

    [Header("Post-Dash Linger (only for spooky dash)")]
    [Tooltip("Keep spawning faint ghosts after dash ends for a smoother fade-out.")]
    [SerializeField] private float _postDashLinger = 0.18f;
    [Tooltip("Global alpha multiplier during linger (1→0). Multiplies your material alpha.")]
    [SerializeField] private AnimationCurve _postDashAlphaOverTime = null;
    [Tooltip("How much to slow the spawn rate by the end (1 = same as during dash).")]
    [SerializeField] private float _postDashSpawnIntervalScale = 1.6f;

    [Header("Debug")]
    [SerializeField] private bool _log = false;

    // ── Phase state
    private int _playerPhaseLayer = -1;
    private Collider[] _allCols;
    private int[] _originalLayers;
    private bool _isPhasing = false;
    private Coroutine _phaseGraceRoutine;

    // ── Afterimage state
    private Coroutine _trailRoutine;
    private Coroutine _lingerRoutine;
    private SkinnedMeshRenderer[] _skinned;
    private MeshRenderer[] _staticMeshes;
    private readonly List<GhostFade> _liveGhosts = new List<GhostFade>();

    // ── Spooky cooldown gating
    private bool _spookyReady = true;          // off cooldown → next dash will be spooky
    private float _cooldownRemaining = 0f;     // counts down after a spooky dash
    private bool _spookyDashActive = false;    // currently in a spooky dash (or grace)
    private bool _wasDashing = false;          // edge detect PlayerDash state

    // cached color property IDs
    private static readonly int _ColorID     = Shader.PropertyToID("_Color");
    private static readonly int _BaseColorID = Shader.PropertyToID("_BaseColor");

    // ─────────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (_playerDash == null) _playerDash = GetComponent<PlayerDash>();
        if (_playerDash == null)
        {
            Debug.LogError("[SpookyDash] No PlayerDash found on this object.");
            enabled = false; return;
        }

        if (_playerCamera == null)
            _playerCamera = FindObjectOfType<PlayerCamera>();

        _playerPhaseLayer = LayerMask.NameToLayer(_playerPhaseLayerName);
        if (_playerPhaseLayer < 0)
            Debug.LogError($"[SpookyDash] Layer '{_playerPhaseLayerName}' not found. Create it and set PlayerPhase×Phaseable to NOT collide.");

        RefreshPlayerColliders();
        CacheRenderers();

        if (_alphaOverLife == null || _alphaOverLife.length == 0)
            _alphaOverLife = AnimationCurve.EaseInOut(0, 1, 1, 0);

        if (_postDashAlphaOverTime == null || _postDashAlphaOverTime.length == 0)
            _postDashAlphaOverTime = AnimationCurve.Linear(0, 1, 1, 0);
    }

    private void OnEnable()
    {
        SetPhase(false, force: true);
        _wasDashing = false;
        _spookyDashActive = false;
    }

    private void OnDisable()
    {
        SetPhase(false, force: true);
        StopTrailImmediate();
        StopLingerImmediate();
        KillAllGhostsImmediate();
    }

    private void Update()
    {
        // Cooldown tick
        if (!_spookyReady && _cooldownRemaining > 0f)
        {
            _cooldownRemaining -= Time.deltaTime;
            if (_cooldownRemaining <= 0f)
            {
                _cooldownRemaining = 0f;
                _spookyReady = true;
                if (_log) Debug.Log("[SpookyDash] Special cooldown ready — next dash will be SPOOKY.");
            }
        }

        bool isDashing = _playerDash != null && _playerDash.IsDashing();

        // Edge: dash just started
        if (isDashing && !_wasDashing)
        {
            if (_spookyReady)
            {
                // This dash is the spooky one
                BeginSpookyDash();
            }
            else
            {
                // Regular dash — ensure spooky effects are off
                CancelSpookyFXImmediate();
            }
        }
        // Edge: dash just ended
        else if (!isDashing && _wasDashing)
        {
            if (_spookyDashActive)
            {
                EndSpookyDashAndStartCooldown();  // starts phase grace + linger, then cooldown
            }
            else
            {
                // Regular dash end — ensure no trail/phase lingering from any previous state
                CancelSpookyFXImmediate();
            }
        }

        _wasDashing = isDashing;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Spooky dash lifecycle
    private void BeginSpookyDash()
    {
        if (_log) Debug.Log("[SpookyDash] >>> BEGIN SPOOKY DASH");
        _spookyDashActive = true;

        // Phase immediately (and keep camera ignoring Phaseable while phasing)
        if (_phaseGraceRoutine != null) { StopCoroutine(_phaseGraceRoutine); _phaseGraceRoutine = null; }
        SetPhase(true);
        SetCameraPhaseIgnore(true);

        // Afterimages only for spooky dash
        if (_afterimageEnabled && _trailRoutine == null)
            _trailRoutine = StartCoroutine(SpawnTrailWhileDashing());
    }

    private void EndSpookyDashAndStartCooldown()
    {
        if (_log) Debug.Log("[SpookyDash] <<< END SPOOKY DASH → cooldown");

        // Stop continuous trail spawning, then do a soft post-dash linger
        StopTrailImmediate();
        if (_afterimageEnabled && _postDashLinger > 0f)
            _lingerRoutine = StartCoroutine(SpawnTrailLinger());

        // Phase grace window, then restore collisions and camera mask
        if (_phaseGraceRoutine != null) StopCoroutine(_phaseGraceRoutine);
        _phaseGraceRoutine = StartCoroutine(PhaseGraceThenRestore());

        // Flip state so subsequent dashes are regular during cooldown
        _spookyDashActive = false;
        _spookyReady = false;
        _cooldownRemaining = Mathf.Max(0.01f, _specialCooldown);
    }

    private void CancelSpookyFXImmediate()
    {
        // No spooky effects allowed (used on regular dash, or if something got stuck)
        if (_phaseGraceRoutine != null) { StopCoroutine(_phaseGraceRoutine); _phaseGraceRoutine = null; }
        SetPhase(false);
        SetCameraPhaseIgnore(false);

        StopTrailImmediate();
        StopLingerImmediate();
        // (We do NOT kill live ghosts instantly—let them finish their own lifetime fade if any were left)
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Phase helpers
    private IEnumerator PhaseGraceThenRestore()
    {
        yield return new WaitForSeconds(_phaseGraceAfterDash);
        SetPhase(false);
        SetCameraPhaseIgnore(false);
        _phaseGraceRoutine = null;
    }

    private void RefreshPlayerColliders()
    {
        _allCols = GetComponentsInChildren<Collider>(true) ?? new Collider[0];
        _originalLayers = new int[_allCols.Length];
        for (int i = 0; i < _allCols.Length; i++)
        {
            var go = _allCols[i] ? _allCols[i].gameObject : null;
            _originalLayers[i] = go ? go.layer : 0;
        }
        if (_log) Debug.Log($"[SpookyDash] Found {_allCols.Length} colliders to swap.");
    }

    private void SetPhase(bool on, bool force = false)
    {
        if (!force && _isPhasing == on) return;
        if (_allCols == null || _allCols.Length == 0) RefreshPlayerColliders();

        if (on)
        {
            if (_playerPhaseLayer < 0) return;
            foreach (var c in _allCols) if (c) c.gameObject.layer = _playerPhaseLayer;
            _isPhasing = true;
            if (_log) Debug.Log("[SpookyDash] PHASE ON");
        }
        else
        {
            for (int i = 0; i < _allCols.Length; i++)
            {
                var c = _allCols[i]; if (!c) continue;
                int original = (_originalLayers != null && i < _originalLayers.Length) ? _originalLayers[i] : c.gameObject.layer;
                c.gameObject.layer = original;
            }
            _isPhasing = false;
            if (_log) Debug.Log("[SpookyDash] PHASE OFF");
        }
    }

    private void SetCameraPhaseIgnore(bool on)
    {
        if (_playerCamera != null)
        {
            // This toggles the camera collision mask to ignore Phaseable during phasing/grace.
            _playerCamera.SetCollisionMaskPhaseIgnore(on);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Afterimage trail (ONLY for spooky dash)
    private void CacheRenderers()
    {
        _skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _staticMeshes = GetComponentsInChildren<MeshRenderer>(true);
    }

    private IEnumerator SpawnTrailWhileDashing()
    {
        // Only run while the *dash is active* (spooky dash path)
        while (_playerDash != null && _playerDash.IsDashing() && _spookyDashActive)
        {
            SpawnOneGhostFrame(1f); // full strength while dashing
            yield return new WaitForSeconds(_spawnInterval);
        }
        _trailRoutine = null;
    }

    private IEnumerator SpawnTrailLinger()
    {
        float t = 0f;
        while (t < _postDashLinger)
        {
            float n = Mathf.Clamp01(t / _postDashLinger);
            float alphaMul = Mathf.Clamp01(_postDashAlphaOverTime.Evaluate(n));

            // spawn a fainter ghost
            SpawnOneGhostFrame(alphaMul);

            // progressively slow the spawn rate
            float interval = _spawnInterval * Mathf.Lerp(1f, Mathf.Max(1f, _postDashSpawnIntervalScale), n);
            yield return new WaitForSeconds(interval);

            t += interval;
        }
        _lingerRoutine = null;
    }

    private void StopTrailImmediate()
    {
        if (_trailRoutine != null)
        {
            StopCoroutine(_trailRoutine);
            _trailRoutine = null;
        }
    }

    private void StopLingerImmediate()
    {
        if (_lingerRoutine != null)
        {
            StopCoroutine(_lingerRoutine);
            _lingerRoutine = null;
        }
    }

    private void SpawnOneGhostFrame(float globalAlphaMul)
    {
        if (!_afterimageEnabled || _afterimageMaterial == null) return;

        if ((_skinned == null || _skinned.Length == 0) &&
            (_staticMeshes == null || _staticMeshes.Length == 0))
            CacheRenderers();

        // pick correct color property and read material color INCLUDING alpha
        int colorProp = _afterimageMaterial.HasProperty(_BaseColorID) ? _BaseColorID : _ColorID;
        Color baseCol = _afterimageMaterial.HasProperty(colorProp)
            ? _afterimageMaterial.GetColor(colorProp)
            : (_afterimageMaterial.HasProperty(_ColorID) ? _afterimageMaterial.color : Color.white);

        // apply global alpha multiplier for linger
        baseCol.a *= Mathf.Clamp01(globalAlphaMul);

        // Skinned meshes
        if (_skinned != null)
        {
            foreach (var s in _skinned)
            {
                if (!s || !s.gameObject.activeInHierarchy) continue;
                var baked = new Mesh();
#if UNITY_2020_3_OR_NEWER
                s.BakeMesh(baked, true);
#else
                s.BakeMesh(baked);
#endif
                CreateGhostObject(baked, s.transform, baseCol, colorProp);
            }
        }

        // Static meshes
        if (_staticMeshes != null)
        {
            foreach (var mr in _staticMeshes)
            {
                if (!mr || !mr.enabled || mr is SkinnedMeshRenderer) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;

                var cloned = Instantiate(mf.sharedMesh);
                CreateGhostObject(cloned, mr.transform, baseCol, colorProp);
            }
        }
    }

    private void CreateGhostObject(Mesh mesh, Transform source, Color baseCol, int colorPropId)
    {
        var go = new GameObject("SpookyAfterimage");
        go.transform.SetPositionAndRotation(source.position, source.rotation);
        go.transform.localScale = source.lossyScale;

        if (_posJitter > 0f)
            go.transform.position += new Vector3(
                Random.Range(-_posJitter, _posJitter),
                Random.Range(-_posJitter, _posJitter),
                Random.Range(-_posJitter, _posJitter)
            );
        if (_scaleJitter > 0f)
        {
            float j = 1f + Random.Range(-_scaleJitter, _scaleJitter);
            go.transform.localScale *= j;
        }

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = _afterimageMaterial; // same material, we’ll override color via MPB
        go.layer = source.gameObject.layer;

        var fade = go.AddComponent<GhostFade>();
        fade.Initialize(baseCol, colorPropId, _ghostLifetime, _alphaOverLife, mr, mf);

        _liveGhosts.Add(fade);
        fade.onDestroyed += () => _liveGhosts.Remove(fade);
    }

    private void KillAllGhostsImmediate()
    {
        foreach (var g in _liveGhosts) if (g) g.KillNow();
        _liveGhosts.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Per-ghost fade (alpha only). Uses detected color property.
    private class GhostFade : MonoBehaviour
    {
        public System.Action onDestroyed;

        private Color _baseColor = Color.white; // includes material alpha (and linger alpha)
        private int _colorPropId = 0;
        private float _life;
        private float _t;
        private AnimationCurve _curve;
        private MaterialPropertyBlock _mpb;
        private MeshRenderer _mr;
        private MeshFilter _mf;

        public void Initialize(Color baseColor, int colorPropId, float life, AnimationCurve curve,
                               MeshRenderer mr, MeshFilter mf)
        {
            _baseColor = baseColor;
            _colorPropId = colorPropId;
            _life = Mathf.Max(0.01f, life);
            _curve = (curve != null && curve.length > 0) ? curve : AnimationCurve.EaseInOut(0, 1, 1, 0);
            _mr = mr;
            _mf = mf;
            _mpb = new MaterialPropertyBlock();
        }

        public void KillNow()
        {
            if (_mf && _mf.sharedMesh) Destroy(_mf.sharedMesh);
            Destroy(gameObject);
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float n = Mathf.Clamp01(_t / _life);
            float curveAlpha = Mathf.Clamp01(_curve.Evaluate(n));

            // Keep chroma; only scale alpha (don’t desaturate)
            Color c = _baseColor;
            c.a = _baseColor.a * curveAlpha;

            if (_mr != null)
            {
                _mr.GetPropertyBlock(_mpb);
                _mpb.SetColor(_colorPropId, c);
                _mr.SetPropertyBlock(_mpb);
            }

            if (_t >= _life)
            {
                if (_mf && _mf.sharedMesh) Destroy(_mf.sharedMesh);
                onDestroyed?.Invoke();
                Destroy(gameObject);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
// Public helpers (optional UI/debug)
public bool IsSpookyReady() => _spookyReady;
public float GetSpecialCooldownRemaining() => _cooldownRemaining;

// True while the current dash is the spooky one OR while we're still in phase-grace.
public bool IsCurrentlySpookyDashing() => _spookyDashActive || _isPhasing;

// Expose raw phase state (true during dash phasing AND the grace window).
public bool IsPhasing() => _isPhasing;
}