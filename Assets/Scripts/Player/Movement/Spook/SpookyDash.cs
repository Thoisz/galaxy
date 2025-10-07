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
    [Tooltip("Unlit Transparent or Additive material. Color *and alpha* drive max opacity.")]
    [SerializeField] private Material _afterimageMaterial;
    [SerializeField] private float _spawnInterval = 0.03f;
    [SerializeField] private float _ghostLifetime = 0.25f;
    [SerializeField] private AnimationCurve _alphaOverLife = null; // 0..1; multiplied by material alpha
    [SerializeField, Range(0f, 0.25f)] private float _scaleJitter = 0.04f;
    [SerializeField, Range(0f, 0.10f)] private float _posJitter = 0.02f;

    [Header("Afterimage Window")]
    [Tooltip("Only emit afterimages during this early portion of the spooky window.")]
    [SerializeField, Range(0f, 1f)] private float _trailFirstPortion = 0.30f;

    [Header("Color Preservation")]
    [SerializeField] private bool _preserveColorWhenFading = true;
    [SerializeField] private float _colorBoostClamp = 1.75f;

    [Header("Post-Dash Linger")]
    [SerializeField] private float _postDashLinger = 0.18f;
    [SerializeField] private AnimationCurve _postDashAlphaOverTime = null;
    [SerializeField] private float _postDashSpawnIntervalScale = 1.6f;

    [Header("Player Transparency During Spooky Dash")]
    [Tooltip("If enabled, the player fades while spooky dash (incl. grace) is active.")]
    [SerializeField] private bool _transparencyEnabled = true;

    [Tooltip("Overall opacity while spooky window is early (0=invisible, 1=opaque).")]
    [SerializeField, Range(0f, 1f)] private float _transparencyAlpha = 0.4f;

    [Header("Single-Shader Opacity Control")]
    [Tooltip("Opacity property Reference name from your Shader Graph (include underscore).")]
    [SerializeField] private string _opacityPropertyName = "_Opacity";
    [SerializeField, Min(0f)] private float _fadeInDuration = 0.12f;
    [SerializeField, Min(0f)] private float _fadeOutDuration = 0.12f;

    [Header("Dash→Opaque Timing")]
    [SerializeField, Range(0f, 1f)]
    private float _fadeBackStartNormalized = 0.70f;  // start fade-back at 70% of spooky window
    [SerializeField]
    private float _dashDurationHint = 0.35f;         // used if we can't read real duration

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

    // ── Optional events to gate Animator externally (fires ONCE per spooky window)
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

    // ── Transparency / material state
    private MaterialPropertyBlock _mpbTrans;
    private int _opacityID;
    private static readonly int _ColorID      = Shader.PropertyToID("_Color");
    private static readonly int _BaseColorID  = Shader.PropertyToID("_BaseColor");
    private static readonly int _SurfaceID    = Shader.PropertyToID("_Surface");        // 0 Opaque, 1 Transparent
    private static readonly int _QueueCtrlID  = Shader.PropertyToID("_QueueControl");   // 1 = override queue
    private static readonly int _QueueOffID   = Shader.PropertyToID("_QueueOffset");    // 0/whatever
    private static readonly int _ZWriteCtrlID = Shader.PropertyToID("_ZWriteControl");  // 2 = ForceOff (Shader Graph)
    private static readonly string _KW_SURFACE_TRANSPARENT = "_SURFACE_TYPE_TRANSPARENT";

    private bool _materialsInstanced;
    private float _currentOpacity = 1f;
    private float _opacityVel = 0f;

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

        if (_alphaOverLife == null || _alphaOverLife.length == 0)
            _alphaOverLife = AnimationCurve.EaseInOut(0, 1, 1, 0);
        if (_postDashAlphaOverTime == null || _postDashAlphaOverTime.length == 0)
            _postDashAlphaOverTime = AnimationCurve.Linear(0, 1, 1, 0);

        _mpbTrans  = new MaterialPropertyBlock();
        _opacityID = Shader.PropertyToID(_opacityPropertyName);
    }

    private void OnEnable()
    {
        SetPhase(false, force: true);
        StopTrailImmediate();
        StopLingerImmediate();
        KillAllGhostsImmediate();

        _currentOpacity = 1f;
        _opacityVel = 0f;
        RestoreOpacityOnly();
        SetTransparentStateOnAll(false);

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

        RestoreOpacityOnly();
        SetTransparentStateOnAll(false);
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
        bool spookyActiveForCamera = (_spookyActiveThisDash && (spookyMainActive || _phaseGraceRoutine != null));
        SetCameraPhaseIgnore(spookyActiveForCamera);

        // Transparency: fade back over tail of spooky window; opaque in grace/off
        UpdateTransparencyBlock(spookyMainActive, (spookyMainActive || _phaseGraceRoutine != null));

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
    // Transparency logic (single shader)
    private void UpdateTransparencyBlock(bool spookyMainActive, bool spookyActiveInclGrace)
    {
        if (!_transparencyEnabled)
        {
            _currentOpacity = 1f;
            _opacityVel = 0f;
            SetTransparentStateOnAll(false);
            RestoreOpacityOnly();
            return;
        }

        float targetOpacity;
        float smoothTime;

        if (spookyMainActive)
        {
            float dashT = GetSpookyProgress01();
            if (dashT < _fadeBackStartNormalized)
            {
                targetOpacity = _transparencyAlpha;
                smoothTime    = _fadeInDuration;
            }
            else
            {
                float u = Mathf.InverseLerp(_fadeBackStartNormalized, 1f, dashT); // 0..1
                targetOpacity = Mathf.Lerp(_transparencyAlpha, 1f, u);
                smoothTime    = _fadeOutDuration;
            }
        }
        else if (spookyActiveInclGrace) // finish to opaque in grace
        {
            targetOpacity = 1f;
            smoothTime    = _fadeOutDuration;
        }
        else
        {
            targetOpacity = 1f;
            smoothTime    = _fadeOutDuration;
        }

        if (smoothTime <= 0f)
        {
            _currentOpacity = targetOpacity;
            _opacityVel = 0f;
        }
        else
        {
            _currentOpacity = Mathf.SmoothDamp(_currentOpacity, targetOpacity, ref _opacityVel, smoothTime);
        }

        bool wantTransparent = _currentOpacity < 0.999f;
        SetTransparentStateOnAll(wantTransparent);
        ApplyOpacityToAll(_currentOpacity);
    }

    /// <summary>0..1 progress through the *spooky* window (1.5× the dash). 1 if not active.</summary>
    private float GetSpookyProgress01()
    {
        if (!_spookyActiveThisDash) return 1f;
        float start = _dashStartTime;
        float end   = _spookyEndTime;
        if (end <= start) return 1f;
        return Mathf.Clamp01((Time.time - start) / (end - start));
    }

    // Optional: allow PlayerDash to report the precise duration (perfect timing)
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

    public void OnDashEnded()
    {
        _wasDashingLastFrame = false;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Renderer/material helpers (single shader)
    private void CacheRenderers()
    {
        _skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _staticMeshes = GetComponentsInChildren<MeshRenderer>(true);
    }

    private void EnsureInstancedMaterials()
    {
        if (_materialsInstanced) return;

        if (_skinned != null)
            foreach (var r in _skinned) { if (!r) continue; var mats = r.materials; r.materials = mats; }
        if (_staticMeshes != null)
            foreach (var r in _staticMeshes) { if (!r || r is SkinnedMeshRenderer) continue; var mats = r.materials; r.materials = mats; }

        _materialsInstanced = true;
    }

    /// <summary>Flip **the same shader** between opaque/transparent via properties & keyword.</summary>
    private void SetTransparentStateOnAll(bool transparent)
    {
        EnsureInstancedMaterials();

        void Apply(Material m)
        {
            if (!m) return;

            // Shader Graph common toggles
            if (m.HasProperty(_SurfaceID))    m.SetFloat(_SurfaceID, transparent ? 1f : 0f);
            if (m.HasProperty(_ZWriteCtrlID)) m.SetFloat(_ZWriteCtrlID, transparent ? 2f : 0f);
            if (m.HasProperty(_QueueCtrlID))  m.SetFloat(_QueueCtrlID, 1f);
            if (m.HasProperty(_QueueOffID))   m.SetFloat(_QueueOffID, 0f);
            m.renderQueue = transparent ? 3000 : 2000;

            if (transparent) { m.EnableKeyword(_KW_SURFACE_TRANSPARENT); m.DisableKeyword("_ALPHATEST_ON"); }
            else             { m.DisableKeyword(_KW_SURFACE_TRANSPARENT); }
        }

        if (_skinned != null)
        {
            foreach (var r in _skinned)
            {
                if (!r) continue;
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++) Apply(mats[i]);
            }
        }

        if (_staticMeshes != null)
        {
            foreach (var r in _staticMeshes)
            {
                if (!r || r is SkinnedMeshRenderer) continue;
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++) Apply(mats[i]);
            }
        }
    }

    private void SetCameraPhaseIgnore(bool on)
    {
        if (_playerCamera == null) return;
        _playerCamera.SetCollisionMaskPhaseIgnore(on);
    }

    private void ApplyOpacityToAll(float opacity01)
    {
        opacity01 = Mathf.Clamp01(opacity01);

        if (_skinned != null) foreach (var r in _skinned) ApplyOpacity(r, opacity01);
        if (_staticMeshes != null)
            foreach (var r in _staticMeshes) if (!(r is SkinnedMeshRenderer)) ApplyOpacity(r, opacity01);
    }

    private void ApplyOpacity(Renderer r, float opacity01)
    {
        if (!r || !r.enabled || !r.gameObject.activeInHierarchy) return;

        if (_mpbTrans == null) _mpbTrans = new MaterialPropertyBlock();

        r.GetPropertyBlock(_mpbTrans);
        _mpbTrans.SetFloat(_opacityID, opacity01);
        r.SetPropertyBlock(_mpbTrans);

        var mats = r.materials;
        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i]; if (!m) continue;

            if (m.HasProperty(_opacityID)) m.SetFloat(_opacityID, opacity01);
            else
            {
                int colorProp = m.HasProperty(_BaseColorID) ? _BaseColorID :
                                (m.HasProperty(_ColorID) ? _ColorID : -1);
                if (colorProp != -1)
                {
                    var c = m.GetColor(colorProp);
                    c.a = opacity01;
                    m.SetColor(colorProp, c);
                }
            }
        }
    }

    private void RestoreOpacityOnly()
    {
        if (_skinned != null) foreach (var r in _skinned) if (r) r.SetPropertyBlock(null);
        if (_staticMeshes != null) foreach (var r in _staticMeshes) if (r && !(r is SkinnedMeshRenderer)) r.SetPropertyBlock(null);

        if (_skinned != null)
            foreach (var r in _skinned)
                foreach (var m in r.materials) if (m && m.HasProperty(_opacityID)) m.SetFloat(_opacityID, 1f);

        if (_staticMeshes != null)
            foreach (var r in _staticMeshes)
                foreach (var m in r.materials) if (m && m.HasProperty(_opacityID)) m.SetFloat(_opacityID, 1f);
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

        int colorProp = _afterimageMaterial.HasProperty(_BaseColorID) ? _BaseColorID : _ColorID;
        Color baseCol = _afterimageMaterial.HasProperty(colorProp)
            ? _afterimageMaterial.GetColor(colorProp)
            : (_afterimageMaterial.HasProperty(_ColorID) ? _afterimageMaterial.color : Color.white);

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
        mr.sharedMaterial = _afterimageMaterial;
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
}