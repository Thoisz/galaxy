using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpookyDash : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerDash _playerDash;      // auto-grabbed if null
    [SerializeField] private PlayerCamera _playerCamera;  // optional; auto-found if null

    [Header("Phase (collision)")]
    [SerializeField] private string _playerPhaseLayerName = "PlayerPhase";
    [SerializeField] private float _phaseGraceAfterDash = 0.12f;

    [Header("Afterimage (Ectoplasm Wisp Trail)")]
    [SerializeField] private bool _afterimageEnabled = true;
    [Tooltip("UNLIT material used for afterimages AND for the temp player override during spooky dash.")]
    [SerializeField] private Material _afterimageMaterial;
    [SerializeField] private float _spawnInterval = 0.03f;
    [SerializeField] private float _ghostLifetime = 0.25f;
    [SerializeField] private AnimationCurve _alphaOverLife = null; // 0..1; multiplied by material alpha
    [SerializeField, Range(0f, 0.25f)] private float _scaleJitter = 0.04f;
    [SerializeField, Range(0f, 0.10f)] private float _posJitter = 0.02f;

    [Header("Afterimage Window")]
    [Tooltip("Only emit afterimages during this early portion of the spooky window.")]
    [SerializeField, Range(0f, 1f)] private float _trailFirstPortion = 0.30f;

    [Header("Color Preservation (afterimages only)")]
    [SerializeField] private bool _preserveColorWhenFading = true;
    [SerializeField] private float _colorBoostClamp = 1.75f;

    [Header("Post-Dash Linger")]
    [SerializeField] private float _postDashLinger = 0.18f;
    [SerializeField] private AnimationCurve _postDashAlphaOverTime = null;
    [SerializeField] private float _postDashSpawnIntervalScale = 1.6f;

    [Header("Dash Timing")]
    [SerializeField] private float _dashDurationHint = 0.35f; // used if we can't read real duration

    [Header("Spooky Duration & Cooldown")]
    [Tooltip("Spooky dash lasts this multiple of the regular dash (effects window).")]
    [SerializeField] private float _spookyDurationMultiplier = 1.5f; // 1.5×
    [Tooltip("Cooldown between spooky dashes (seconds).")]
    [SerializeField] private float _cooldownSeconds = 15f;           // default 15s

    [Header("Bubble Pop")]
    [Tooltip("One-shot bubble pop spawned at dash start (no follow).")]
    [SerializeField] private ParticleSystem _bubbleFXPrefab;
    [Tooltip("Bone used to place the bubble pop (e.g., hips). Not parented.")]
    [SerializeField] private Transform _hipBone;

    [Header("Debug")]
    [SerializeField] private bool _log = false;

    // ── Events
    public event Action OnSpookyWindowBegan;
    public event Action OnSpookyWindowEnded;

    // ── Phase state
    private int _playerPhaseLayer = -1;
    private Collider[] _allCols;
    private int[] _originalLayers;
    private bool _isPhasing = false;
    private Coroutine _phaseGraceRoutine;

    // ── Afterimage state
    private Coroutine _trailRoutine;
    private Coroutine _lingerRoutine;

    // ── Renderers
    private SkinnedMeshRenderer[] _skinned;
    private MeshRenderer[] _staticMeshes;

    // ── Track spawned ghosts
    private readonly List<GhostFade> _liveGhosts = new List<GhostFade>();

    // ── Material swap (player override during spooky)
    private struct MatSnapshot
    {
        public Renderer renderer;
        public Material[] originals; // sharedMaterials snapshot
    }
    private List<MatSnapshot> _matSnapshots = new List<MatSnapshot>();
    private bool _materialsOverridden = false;

    // ── Dash timing/progress (spooky window)
    private bool  _wasDashingLastFrame;
    private float _dashStartTime;
    private float _dashDurationKnown;             // regular dash duration we know/hint
    private bool  _spookyActiveThisDash;          // accepted for this dash
    private bool  _spookySuppressedThisDash;      // denied due to cooldown
    private float _spookyEndTime;                 // end of spooky window (1.5×)
    private bool  _spookyAnimFired;

    // ── Cooldown
    private float _cooldownReadyTime = 0f;
    public bool  IsCooldownReady   => Time.time >= _cooldownReadyTime;
    public float CooldownRemaining => Mathf.Max(0f, _cooldownReadyTime - Time.time);

    // ─────────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (_playerDash == null) _playerDash = GetComponent<PlayerDash>();
        if (_playerDash == null)
        {
            Debug.LogError("[SpookyDash] No PlayerDash found on this object.");
            enabled = false;
            return;
        }

        if (_playerCamera == null)
            _playerCamera = FindObjectOfType<PlayerCamera>();

        _playerPhaseLayer = LayerMask.NameToLayer(_playerPhaseLayerName);
        if (_playerPhaseLayer < 0)
            Debug.LogError($"[SpookyDash] Layer '{_playerPhaseLayerName}' not found. Configure it & collision matrix PlayerPhase×Phaseable = false.");

        RefreshPlayerColliders();
        CacheRenderers();
        SnapshotOriginalMaterials();

        if (_alphaOverLife == null || _alphaOverLife.length == 0)
            _alphaOverLife = AnimationCurve.EaseInOut(0, 1, 1, 0);
        if (_postDashAlphaOverTime == null || _postDashAlphaOverTime.length == 0)
            _postDashAlphaOverTime = AnimationCurve.Linear(0, 1, 1, 0);
    }

    private void OnEnable()
    {
        SetPhase(false, force: true);
        StopTrailImmediate();
        StopLingerImmediate();
        KillAllGhostsImmediate();

        RestorePlayerMaterials(); // safety

        SetCameraPhaseIgnore(false);

        _cooldownReadyTime = Time.time; // allow first spooky immediately
        _spookyActiveThisDash = false;
        _spookySuppressedThisDash = false;
        _spookyAnimFired = false;
    }

    private void OnDisable()
    {
        SetPhase(false, force: true);
        StopTrailImmediate();
        StopLingerImmediate();
        KillAllGhostsImmediate();

        RestorePlayerMaterials();
        SetCameraPhaseIgnore(false);
    }

    private void Update()
    {
        bool isDashing = _playerDash != null && _playerDash.IsDashing();

        // Edge-detect: dash started
        if (isDashing && !_wasDashingLastFrame)
        {
            _dashStartTime     = Time.time;
            _dashDurationKnown = Mathf.Max(0.01f, _dashDurationHint);

            if (IsCooldownReady)
            {
                _spookyActiveThisDash     = true;
                _spookySuppressedThisDash = false;
                _spookyAnimFired          = false;

                float spookyDur = _dashDurationKnown * Mathf.Max(1f, _spookyDurationMultiplier);
                _spookyEndTime  = _dashStartTime + spookyDur;

                // Start cooldown now (exactly one spooky per cooldown)
                _cooldownReadyTime = Time.time + Mathf.Max(0f, _cooldownSeconds);

                OnSpookyWindowBegan?.Invoke();
                if (_log) Debug.Log($"[SpookyDash] Spooky OPEN ({spookyDur:0.###}s). Cooldown until {_cooldownReadyTime:0.###}");

                SpawnBubbleFXOnce(); // one-shot bubble pop
            }
            else
            {
                _spookyActiveThisDash     = false;
                _spookySuppressedThisDash = true;
                if (_log) Debug.Log("[SpookyDash] Spooky DENIED (cooldown).");
            }
        }
        _wasDashingLastFrame = isDashing;

        // Window flags
        float spookyProgress = GetSpookyProgress01();
        bool withinSpookyWindow = _spookyActiveThisDash && Time.time < _spookyEndTime;
        bool spookyMainActive   = _spookyActiveThisDash && (isDashing || withinSpookyWindow);

        // === Player material override while spooky (and phase grace) ===
        bool spookyActiveInclGrace = _spookyActiveThisDash && (spookyMainActive || _phaseGraceRoutine != null);
        if (spookyActiveInclGrace) ApplyPlayerMaterialOverride();
        else                       RestorePlayerMaterials();

        // === Afterimage: only on early portion of spooky window ===
        if (_afterimageEnabled)
        {
            bool inTrailPortion = spookyMainActive && (spookyProgress <= Mathf.Clamp01(_trailFirstPortion));
            if (inTrailPortion && _trailRoutine == null)
            {
                StopLingerImmediate();
                _trailRoutine = StartCoroutine(SpawnTrailWhileWindowPortion(
                    () => GetSpookyProgress01() <= Mathf.Clamp01(_trailFirstPortion)));
            }
            else if (!inTrailPortion && _trailRoutine != null)
            {
                StopTrailImmediate();
                if (_postDashLinger > 0f && _spookyActiveThisDash)
                    _lingerRoutine = StartCoroutine(SpawnTrailLinger());
            }
        }
        else
        {
            StopTrailImmediate();
        }

        // === Phase: keyed to spooky window + grace ===
        if (spookyMainActive)
        {
            if (_phaseGraceRoutine != null) { StopCoroutine(_phaseGraceRoutine); _phaseGraceRoutine = null; }
            SetPhase(true);
        }
        else if (_spookyActiveThisDash && !_spookySuppressedThisDash)
        {
            if (_isPhasing && _phaseGraceRoutine == null)
                _phaseGraceRoutine = StartCoroutine(PhaseGraceThenRestore());
        }
        else
        {
            if (_isPhasing && _phaseGraceRoutine == null)
                SetPhase(false);
        }

        // Camera mask ignore: active during window OR grace
        SetCameraPhaseIgnore(spookyActiveInclGrace);

        // Close window end signal when fully over & no grace running
        if (_spookyActiveThisDash && Time.time >= _spookyEndTime && _phaseGraceRoutine == null)
        {
            OnSpookyWindowEnded?.Invoke();
            _spookyActiveThisDash = false;
            _spookyAnimFired = true; // lock anyway
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Bubble pop (one-shot at hip; no parenting; no follow)
    private void SpawnBubbleFXOnce()
    {
        if (_bubbleFXPrefab == null || _hipBone == null) return;

        ParticleSystem ps = Instantiate(_bubbleFXPrefab, _hipBone.position, _bubbleFXPrefab.transform.rotation);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        if (!ps.isPlaying) ps.Play();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Renderer/material helpers
    private void CacheRenderers()
    {
        _skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _staticMeshes = GetComponentsInChildren<MeshRenderer>(true);
    }

    private void SnapshotOriginalMaterials()
    {
        _matSnapshots.Clear();

        if (_skinned != null)
        {
            foreach (var r in _skinned)
            {
                if (!r) continue;
                _matSnapshots.Add(new MatSnapshot
                {
                    renderer = r,
                    originals = r.sharedMaterials != null ? (Material[])r.sharedMaterials.Clone() : Array.Empty<Material>()
                });
            }
        }

        if (_staticMeshes != null)
        {
            foreach (var r in _staticMeshes)
            {
                if (!r || r is SkinnedMeshRenderer) continue;
                _matSnapshots.Add(new MatSnapshot
                {
                    renderer = r,
                    originals = r.sharedMaterials != null ? (Material[])r.sharedMaterials.Clone() : Array.Empty<Material>()
                });
            }
        }
    }

    private void ApplyPlayerMaterialOverride()
    {
        if (_materialsOverridden) return;
        if (_afterimageMaterial == null) return;

        for (int i = 0; i < _matSnapshots.Count; i++)
        {
            var snap = _matSnapshots[i];
            if (!snap.renderer) continue;

            int slots = Mathf.Max(1, snap.originals?.Length ?? 1);
            var arr = new Material[slots];
            for (int m = 0; m < slots; m++) arr[m] = _afterimageMaterial;
            snap.renderer.sharedMaterials = arr;
        }
        _materialsOverridden = true;
    }

    private void RestorePlayerMaterials()
    {
        if (!_materialsOverridden) return;

        for (int i = 0; i < _matSnapshots.Count; i++)
        {
            var snap = _matSnapshots[i];
            if (!snap.renderer) continue;
            if (snap.originals == null) continue;
            snap.renderer.sharedMaterials = snap.originals;
        }
        _materialsOverridden = false;
    }

    private void SetCameraPhaseIgnore(bool on)
    {
        if (_playerCamera == null) return;
        _playerCamera.SetCollisionMaskPhaseIgnore(on);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Phase helpers
    private IEnumerator PhaseGraceThenRestore()
    {
        yield return new WaitForSeconds(_phaseGraceAfterDash);

        SetPhase(false);
        _phaseGraceRoutine = null;

        SetCameraPhaseIgnore(false);

        OnSpookyWindowEnded?.Invoke(); // fully over after grace
        _spookyActiveThisDash = false;
        _spookyAnimFired = true;

        // Ensure player materials are restored at the end of grace.
        RestorePlayerMaterials();
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Afterimage trail (only during early portion of the spooky window)
    private IEnumerator SpawnTrailWhileWindowPortion(Func<bool> keepSpawning)
    {
        while (keepSpawning != null && keepSpawning())
        {
            SpawnOneGhostFrame(1f);
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

            SpawnOneGhostFrame(alphaMul);

            float interval = _spawnInterval * Mathf.Lerp(1f, Mathf.Max(1f, _postDashSpawnIntervalScale), n);
            yield return new WaitForSeconds(interval);

            t += interval;
        }
        _lingerRoutine = null;
    }

    private void StopTrailImmediate()
    {
        if (_trailRoutine != null) { StopCoroutine(_trailRoutine); _trailRoutine = null; }
    }

    private void StopLingerImmediate()
    {
        if (_lingerRoutine != null) { StopCoroutine(_lingerRoutine); _lingerRoutine = null; }
    }

    private void SpawnOneGhostFrame(float globalAlphaMul)
    {
        if (!_afterimageEnabled || _afterimageMaterial == null) return;

        if ((_skinned == null || _skinned.Length == 0) &&
            (_staticMeshes == null || _staticMeshes.Length == 0))
            CacheRenderers();

        int colorProp = Shader.PropertyToID("_BaseColor");
        bool hasBase = _afterimageMaterial.HasProperty(colorProp);
        if (!hasBase) colorProp = Shader.PropertyToID("_Color");

        Color baseCol = _afterimageMaterial.HasProperty(colorProp)
            ? _afterimageMaterial.GetColor(colorProp)
            : (_afterimageMaterial.HasProperty(Shader.PropertyToID("_Color")) ? _afterimageMaterial.color : Color.white);

        baseCol.a *= Mathf.Clamp01(globalAlphaMul);

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
                UnityEngine.Random.Range(-_posJitter, _posJitter),
                UnityEngine.Random.Range(-_posJitter, _posJitter),
                UnityEngine.Random.Range(-_posJitter, _posJitter)
            );
        if (_scaleJitter > 0f)
        {
            float j = 1f + UnityEngine.Random.Range(-_scaleJitter, _scaleJitter);
            go.transform.localScale *= j;
        }

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = _afterimageMaterial; // same unlit material
        go.layer = source.gameObject.layer;

        var fade = go.AddComponent<GhostFade>();
        fade.Initialize(
            baseCol, colorPropId, _ghostLifetime, _alphaOverLife,
            mr, mf, _preserveColorWhenFading, Mathf.Max(1f, _colorBoostClamp)
        );

        _liveGhosts.Add(fade);
        fade.onDestroyed += () => _liveGhosts.Remove(fade);
    }

    private void KillAllGhostsImmediate()
    {
        foreach (var g in _liveGhosts) if (g) g.KillNow();
        _liveGhosts.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Per-ghost fade
    private class GhostFade : MonoBehaviour
    {
        public System.Action onDestroyed;

        private Color _baseColor = Color.white;
        private int _colorPropId = 0;
        private float _life;
        private float _t;
        private AnimationCurve _curve;
        private MaterialPropertyBlock _mpb;
        private MeshRenderer _mr;
        private MeshFilter _mf;

        private bool _preserveColor;
        private float _boostClamp;

        public void Initialize(
            Color baseColor, int colorPropId, float life, AnimationCurve curve,
            MeshRenderer mr, MeshFilter mf, bool preserveColor, float boostClamp)
        {
            _baseColor = baseColor;
            _colorPropId = colorPropId;
            _life = Mathf.Max(0.01f, life);
            _curve = (curve != null && curve.length > 0) ? curve : AnimationCurve.EaseInOut(0, 1, 1, 0);
            _mr = mr;
            _mf = mf;
            _mpb = new MaterialPropertyBlock();

            _preserveColor = preserveColor;
            _boostClamp = Mathf.Max(1f, boostClamp);
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

            float newA = _baseColor.a * curveAlpha;

            Color c = _baseColor;

            if (_preserveColor && newA > 0.0001f && _baseColor.a > 0.0001f)
            {
                float k = Mathf.Clamp(_baseColor.a / newA, 1f, _boostClamp);
                c.r = Mathf.Clamp01(c.r * k);
                c.g = Mathf.Clamp01(c.g * k);
                c.b = Mathf.Clamp01(c.b * k);
            }

            c.a = newA;

            if (_mr != null)
            {
                var mpb = _mpb ??= new MaterialPropertyBlock();
                _mr.GetPropertyBlock(mpb);
                _mpb.SetColor(_colorPropId, c);
                _mr.SetPropertyBlock(mpb);
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
    // PUBLIC HELPERS
    public bool IsCurrentlySpookyDashing()
    {
        bool withinWindow = _spookyActiveThisDash && Time.time < _spookyEndTime;
        bool inGrace      = _phaseGraceRoutine != null;
        return _spookyActiveThisDash && (withinWindow || inGrace);
    }

    public bool IsPhasing => _isPhasing;
    public bool IsInPhaseGrace => (_phaseGraceRoutine != null) && !_wasDashingLastFrame;

    /// <summary>0..1 progress through the *spooky* window (1.5× the dash). 1 if not active.</summary>
    private float GetSpookyProgress01()
    {
        if (!_spookyActiveThisDash) return 1f;
        float start = _dashStartTime;
        float end   = _spookyEndTime;
        if (end <= start) return 1f;
        return Mathf.Clamp01((Time.time - start) / (end - start));
    }

    // Hook points from PlayerDash if available
    public void OnDashStarted(float durationSeconds)
    {
        _dashDurationKnown = Mathf.Max(0.01f, durationSeconds);
        _dashStartTime = Time.time;

        if (IsCooldownReady)
        {
            _spookyActiveThisDash     = true;
            _spookySuppressedThisDash = false;
            _spookyAnimFired          = false;

            float spookyDur = _dashDurationKnown * Mathf.Max(1f, _spookyDurationMultiplier);
            _spookyEndTime  = _dashStartTime + spookyDur;
            _cooldownReadyTime = Time.time + Mathf.Max(0f, _cooldownSeconds);

            OnSpookyWindowBegan?.Invoke();
            if (_log) Debug.Log($"[SpookyDash] (OnDashStarted) Spooky {spookyDur:0.###}s; cooldown ticking.");
            SpawnBubbleFXOnce();
        }
        else
        {
            _spookyActiveThisDash     = false;
            _spookySuppressedThisDash = true;
        }

        _wasDashingLastFrame = true;
    }

    public void OnDashEnded() { _wasDashingLastFrame = false; }
}