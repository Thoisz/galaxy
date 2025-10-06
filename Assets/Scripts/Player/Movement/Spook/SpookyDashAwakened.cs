using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpookyDashAwakened : MonoBehaviour
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

    // ─────────────────────────────────────────────────────────────────────────────
    // SPOOKY MODEL SWAP
    [Header("Spooky Model")]
    [Tooltip("Your ghost/whisp FBX prefab with its own Animator.")]
    [SerializeField] private GameObject _spookyModelPrefab;

    [Tooltip("Optional parent for the spooky model. If empty, we’ll use the normal visual root’s parent.")]
    [SerializeField] private Transform _spookySpawnParent;

    [Tooltip("The state name inside the spooky model’s Animator to play on dash start.")]
    [SerializeField] private string _spookyDashStateName = "SpookyDash";

    [Tooltip("The layer index for the spooky dash state (usually 0).")]
    [SerializeField] private int _spookyDashLayer = 0;

    [Header("Visual Roots")]
    [Tooltip("Root of the NORMAL player visuals (the kid model). If unset, auto-detected from a child Animator.")]
    [SerializeField] private Transform _normalVisualRoot;

    [Header("Model Crossfade")]
    [SerializeField] private bool _useModelCrossfade = true;
    [SerializeField, Range(0f, 0.3f)] private float _modelCrossfadeDuration = 0.12f;

    [Header("Transparency")]
    [Tooltip("If enabled, the spooky model fades to this alpha instead of fully opaque.")]
    [SerializeField] private bool _enableTransparency = false;
    [Tooltip("Final alpha for the spooky model during dash (0 = invisible, 1 = opaque).")]
    [SerializeField, Range(0f, 1f)] private float _transparencyAlpha = 0.6f;

    // ─────────────────────────────────────────────────────────────────────────────
    // ANIMATION CLIP OVERRIDES (on your NORMAL animator graph)
    [Header("Spooky Animation Overrides (on Normal Animator)")]
    [Tooltip("Replace clips in your normal controller while this spooky dash is active (and optionally through phase grace).")]
    [SerializeField] private ClipPair[] _overridesToApply;

    [Tooltip("Keep overrides active through the phase-grace window so blends out stay smooth.")]
    [SerializeField] private bool _includePhaseGrace = true;

    [Tooltip("If something gets stuck, force restore after this many seconds post spooky end. 0 = no timeout.")]
    [SerializeField] private float _postDashRestoreTimeout = 1.0f;

    [Tooltip("Optionally scale the Normal Animator's speed while spooky overrides are active. Uncheck to leave speed alone.")]
    [SerializeField] private bool _overrideAnimatorSpeed = false;

    [Tooltip("Animator.speed while spooky overrides are applied. (1 = normal)")]
    [SerializeField] private float _spookyAnimatorSpeed = 1f;

    [System.Serializable]
    public struct ClipPair
    {
        public AnimationClip baseClip;   // clip referenced by your existing states
        public AnimationClip spookyClip; // the spooky variant to play instead
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Afterimage (ectoplasm wisp trail)
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

    // ── Spooky model instance & anim
    private GameObject _spookyInstance;
    private Animator _spookyAnimator;

    // ── Normal & Spooky renderer caches for crossfade
    private Renderer[] _normalRenderers;
    private Renderer[] _spookyRenderers;

    // ── Normal animator + overrides
    private Animator _normalAnimator;
    private AnimatorOverrideController _aoc;
    private readonly Dictionary<AnimationClip, AnimationClip> _originalMap = new();
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> _scratchPairs = new();
    private HashSet<AnimationClip> _spookyClipSet;
    private bool _overridesApplied;
    private bool _restoreArmed;
    private float _restoreArmTime;
    private float _originalAnimatorSpeed = 1f;

    // cached color property IDs (for afterimages only)
    private static readonly int _ColorID     = Shader.PropertyToID("_Color");
    private static readonly int _BaseColorID = Shader.PropertyToID("_BaseColor");

    // ─────────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (_playerDash == null) _playerDash = GetComponent<PlayerDash>();
        if (_playerDash == null)
        {
            Debug.LogError("[SpookyDashAwakened] No PlayerDash found on this object.");
            enabled = false; return;
        }

        if (_playerCamera == null)
            _playerCamera = FindObjectOfType<PlayerCamera>();

        _playerPhaseLayer = LayerMask.NameToLayer(_playerPhaseLayerName);
        if (_playerPhaseLayer < 0)
            Debug.LogError($"[SpookyDashAwakened] Layer '{_playerPhaseLayerName}' not found. Create it and set PlayerPhase×Phaseable to NOT collide.");

        // Normal visual root auto-detect if not provided
        if (_normalVisualRoot == null)
        {
            var anim = GetComponentInChildren<Animator>();
            _normalVisualRoot = anim ? anim.transform : transform;
        }

        // Normal animator (the one that drives your complex controller)
        _normalAnimator = _normalVisualRoot ? _normalVisualRoot.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();
        if (_normalAnimator != null)
        {
            _originalAnimatorSpeed = _normalAnimator.speed;

            var baseController = _normalAnimator.runtimeAnimatorController;
            _aoc = new AnimatorOverrideController { runtimeAnimatorController = baseController };
            _normalAnimator.runtimeAnimatorController = _aoc;

            // Cache originals for restore
            _scratchPairs.Clear();
            _aoc.GetOverrides(_scratchPairs);
            _originalMap.Clear();
            foreach (var kv in _scratchPairs) _originalMap[kv.Key] = kv.Value;

            // Build spooky set
            _spookyClipSet = new HashSet<AnimationClip>();
            if (_overridesToApply != null)
            {
                for (int i = 0; i < _overridesToApply.Length; i++)
                    if (_overridesToApply[i].spookyClip) _spookyClipSet.Add(_overridesToApply[i].spookyClip);
            }
        }
        else
        {
            Debug.LogWarning("[SpookyDashAwakened] No normal Animator found; animation overrides will be skipped.");
        }

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
        HideSpookyInstanceImmediate(); // safety on re-enable
        ArmRestore(false);             // clear restore state
    }

    private void OnDisable()
    {
        SetPhase(false, force: true);
        StopTrailImmediate();
        StopLingerImmediate();
        KillAllGhostsImmediate();
        HideSpookyInstanceImmediate();
        ForceRestoreNow();             // clean up overrides if any
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
                if (_log) Debug.Log("[SpookyDashAwakened] Special cooldown ready — next dash will be SPOOKY.");
            }
        }

        bool isDashing = _playerDash != null && _playerDash.IsDashing();

        // Edge: dash just started
        if (isDashing && !_wasDashing)
        {
            if (_spookyReady)
            {
                BeginSpookyDash();
            }
            else
            {
                CancelSpookyFXImmediate();
            }
        }
        // Edge: dash just ended
        else if (!isDashing && _wasDashing)
        {
            if (_spookyDashActive)
            {
                EndSpookyDashAndStartCooldown();
            }
            else
            {
                CancelSpookyFXImmediate();
            }
        }

        // Manage override apply/restore window
        HandleAnimationOverrideLifecycle();

        _wasDashing = isDashing;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Spooky dash lifecycle
    private void BeginSpookyDash()
    {
        if (_log) Debug.Log("[SpookyDashAwakened] >>> BEGIN SPOOKY DASH");
        _spookyDashActive = true;

        // Phase & camera
        if (_phaseGraceRoutine != null) { StopCoroutine(_phaseGraceRoutine); _phaseGraceRoutine = null; }
        SetPhase(true);
        SetCameraPhaseIgnore(true);

        // Swap visuals
        ShowSpookyModel();

        // Apply animation overrides now (on the normal animator graph)
        ApplyOverrides();

        // Afterimages
        if (_afterimageEnabled && _trailRoutine == null)
            _trailRoutine = StartCoroutine(SpawnTrailWhileDashing());
    }

    private void EndSpookyDashAndStartCooldown()
    {
        if (_log) Debug.Log("[SpookyDashAwakened] <<< END SPOOKY DASH → cooldown");

        // Stop continuous trail spawning, then do a soft post-dash linger
        StopTrailImmediate();
        if (_afterimageEnabled && _postDashLinger > 0f)
            _lingerRoutine = StartCoroutine(SpawnTrailLinger());

        // Phase grace window, then restore collisions & camera mask and swap visuals back
        if (_phaseGraceRoutine != null) StopCoroutine(_phaseGraceRoutine);
        _phaseGraceRoutine = StartCoroutine(PhaseGraceThenRestore());

        // Arm a restore for overrides (they may remain during grace, depending on setting)
        ArmRestore(true);

        _spookyDashActive = false;
        _spookyReady = false;
        _cooldownRemaining = Mathf.Max(0.01f, _specialCooldown);
    }

    private void CancelSpookyFXImmediate()
    {
        if (_phaseGraceRoutine != null) { StopCoroutine(_phaseGraceRoutine); _phaseGraceRoutine = null; }
        SetPhase(false);
        SetCameraPhaseIgnore(false);

        StopTrailImmediate();
        StopLingerImmediate();

        // Snap visuals back to normal immediately
        HideSpookyInstanceImmediate();
        SetRenderersEnabled(_normalRenderers, true);

        // Force override restore immediately
        ForceRestoreNow();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Phase helpers
    private IEnumerator PhaseGraceThenRestore()
    {
        yield return new WaitForSeconds(_phaseGraceAfterDash);
        SetPhase(false);
        SetCameraPhaseIgnore(false);
        _phaseGraceRoutine = null;

        // Swap back to normal visuals
        HideSpookyModel();

        // If we opted NOT to keep overrides during grace, we may already be arming/finished.
        // If we DID keep them during grace, now is also a good moment to arm restore.
        if (_includePhaseGrace)
            ArmRestore(true);
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
        if (_log) Debug.Log($"[SpookyDashAwakened] Found {_allCols.Length} colliders to swap.");
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
            if (_log) Debug.Log("[SpookyDashAwakened] PHASE ON");
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
            if (_log) Debug.Log("[SpookyDashAwakened] PHASE OFF");
        }
    }

    private void SetCameraPhaseIgnore(bool on)
    {
        if (_playerCamera != null)
        {
            _playerCamera.SetCollisionMaskPhaseIgnore(on);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Spooky model swap logic
    private void ShowSpookyModel()
    {
        if (_spookyModelPrefab == null)
        {
            if (_log) Debug.LogWarning("[SpookyDashAwakened] No spooky model prefab assigned—visual swap skipped.");
            return;
        }

        // Cache normal renderers if needed
        if (_normalRenderers == null || _normalRenderers.Length == 0)
        {
            _normalRenderers = _normalVisualRoot
                ? _normalVisualRoot.GetComponentsInChildren<Renderer>(true)
                : GetComponentsInChildren<Renderer>(true);
        }

        // Spawn or reuse spooky instance
        if (_spookyInstance == null)
        {
            Transform parent = _spookySpawnParent != null ? _spookySpawnParent :
                               (_normalVisualRoot != null ? _normalVisualRoot.parent : transform);

            _spookyInstance = Instantiate(_spookyModelPrefab, parent ? parent : transform);
            MatchTransformToNormal(_spookyInstance.transform);
            _spookyAnimator = _spookyInstance.GetComponentInChildren<Animator>();
            _spookyRenderers = _spookyInstance.GetComponentsInChildren<Renderer>(true);

            // Start hidden until we crossfade
            SetRenderersAlpha(_spookyRenderers, 0f);
            SetRenderersEnabled(_spookyRenderers, true);
        }
        else
        {
            MatchTransformToNormal(_spookyInstance.transform);
            if (_spookyRenderers == null || _spookyRenderers.Length == 0)
                _spookyRenderers = _spookyInstance.GetComponentsInChildren<Renderer>(true);

            SetRenderersEnabled(_spookyRenderers, true);
            SetRenderersAlpha(_spookyRenderers, 0f); // ensure start alpha is 0 before fade
        }

        // Determine target alpha for the spooky model
        float targetSpookyAlpha = _enableTransparency ? Mathf.Clamp01(_transparencyAlpha) : 1f;

        // Hide normal + show spooky (optionally via alpha crossfade)
        if (_useModelCrossfade && _modelCrossfadeDuration > 0f)
            StartCoroutine(CrossfadeRenderers(_normalRenderers, _spookyRenderers, _modelCrossfadeDuration, thenDisableA: true, targetAlphaB: targetSpookyAlpha));
        else
        {
            SetRenderersEnabled(_normalRenderers, false);
            SetRenderersEnabled(_spookyRenderers, true);
            SetRenderersAlpha(_spookyRenderers, targetSpookyAlpha);
        }

        // Play spooky dash state
        if (_spookyAnimator != null && !string.IsNullOrEmpty(_spookyDashStateName))
        {
            _spookyAnimator.Update(0f); // ensure animator is initialized
            _spookyAnimator.CrossFadeInFixedTime(_spookyDashStateName, 0.02f, _spookyDashLayer, 0f);
        }
    }

    private void HideSpookyModel()
    {
        if (_spookyInstance == null)
        {
            // Just ensure normals are visible and opaque
            SetRenderersEnabled(_normalRenderers, true);
            SetRenderersAlpha(_normalRenderers, 1f);
            return;
        }

        // Crossfade back to normal or snap
        if (_useModelCrossfade && _modelCrossfadeDuration > 0f)
            StartCoroutine(CrossfadeRenderers(_spookyRenderers, _normalRenderers, _modelCrossfadeDuration, thenDisableA: true, targetAlphaB: 1f));
        else
        {
            SetRenderersEnabled(_spookyRenderers, false);
            SetRenderersEnabled(_normalRenderers, true);
            SetRenderersAlpha(_normalRenderers, 1f);
        }
    }

    private void HideSpookyInstanceImmediate()
    {
        if (_spookyRenderers != null && _spookyRenderers.Length > 0)
            SetRenderersEnabled(_spookyRenderers, false);
    }

    private void MatchTransformToNormal(Transform ghost)
    {
        if (_normalVisualRoot == null || ghost == null) return;
        ghost.position = _normalVisualRoot.position;
        ghost.rotation = _normalVisualRoot.rotation;
        ghost.localScale = _normalVisualRoot.lossyScale; // lossy; parent to same parent for exact match
    }

    private void SetRenderersEnabled(Renderer[] rends, bool on)
    {
        if (rends == null) return;
        for (int i = 0; i < rends.Length; i++)
            if (rends[i]) rends[i].enabled = on;
    }

    // Alpha helper: only affects materials that have _BaseColor/_Color. Leaves others alone (they'll pop).
    private void SetRenderersAlpha(Renderer[] rends, float a)
    {
        if (rends == null) return;
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (!r) continue;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            bool setAny = false;
                       if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(_BaseColorID))
            {
                var c = r.sharedMaterial.GetColor(_BaseColorID);
                c.a = a;
                mpb.SetColor(_BaseColorID, c);
                setAny = true;
            }
            else if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(_ColorID))
            {
                var c = r.sharedMaterial.GetColor(_ColorID);
                c.a = a;
                mpb.SetColor(_ColorID, c);
                setAny = true;
            }

            if (setAny) r.SetPropertyBlock(mpb);
        }
    }

    private IEnumerator CrossfadeRenderers(Renderer[] fromA, Renderer[] toB, float duration, bool thenDisableA = false, float targetAlphaB = 1f)
    {
        float t = 0f;
        targetAlphaB = Mathf.Clamp01(targetAlphaB);

        // Ensure both groups are enabled while fading
        SetRenderersEnabled(fromA, true);
        SetRenderersEnabled(toB, true);

        // Initialize: A=1, B=0
        SetRenderersAlpha(fromA, 1f);
        SetRenderersAlpha(toB, 0f);

        while (t < duration)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / duration);

            float aA = 1f - n;                 // A goes to 0
            float aB = targetAlphaB * n;       // B goes to targetAlphaB

            SetRenderersAlpha(fromA, aA);
            SetRenderersAlpha(toB, aB);
            yield return null;
        }

        // Finalize
        SetRenderersAlpha(fromA, 0f);
        SetRenderersAlpha(toB, targetAlphaB);

        if (thenDisableA)
            SetRenderersEnabled(fromA, false);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Animation override lifecycle
    private void HandleAnimationOverrideLifecycle()
    {
        if (_normalAnimator == null || _aoc == null || _overridesToApply == null || _overridesToApply.Length == 0)
            return;

        // Should overrides be applied right now?
        bool spookyOverrideActiveNow = _spookyDashActive || (_includePhaseGrace && _isPhasing);

        if (spookyOverrideActiveNow && !_overridesApplied)
        {
            ApplyOverrides();
            ArmRestore(false); // clear any restore timer
        }

        // If we just left spooky window, arm restore (let blends finish)
        if (!spookyOverrideActiveNow && _overridesApplied && !_restoreArmed)
        {
            ArmRestore(true);
        }

        // If armed, wait until no spooky clips are playing (or timeout), then restore
        if (_restoreArmed)
        {
            bool anySpookyPlaying = IsAnyOfClipsPlaying(_spookyClipSet);

            bool timedOut = (_postDashRestoreTimeout > 0f) &&
                            (Time.time - _restoreArmTime >= _postDashRestoreTimeout);

            if (!anySpookyPlaying || timedOut)
            {
                RestoreOverrides();
                ArmRestore(false);
            }
        }
    }

    private void ApplyOverrides()
    {
        if (_overridesApplied || _aoc == null) return;

        // Start from whatever is currently applied
        _scratchPairs.Clear();
        _aoc.GetOverrides(_scratchPairs);

        var working = new Dictionary<AnimationClip, AnimationClip>(_scratchPairs.Count);
        foreach (var kv in _scratchPairs) working[kv.Key] = kv.Value;

        // Swap only requested base clips
        for (int i = 0; i < _overridesToApply.Length; i++)
        {
            var baseClip = _overridesToApply[i].baseClip;
            var spooky   = _overridesToApply[i].spookyClip;
            if (!baseClip || !spooky) continue;

            if (working.ContainsKey(baseClip)) working[baseClip] = spooky;
            else                                working.Add(baseClip, spooky);
        }

        _scratchPairs.Clear();
        foreach (var kv in working) _scratchPairs.Add(kv);
        _aoc.ApplyOverrides(_scratchPairs);

        // Optional animator speed override
        if (_overrideAnimatorSpeed)
        {
            _originalAnimatorSpeed = _normalAnimator.speed;
            _normalAnimator.speed  = _spookyAnimatorSpeed;
        }

        _overridesApplied = true;
        // Optional: _normalAnimator.Update(0f);
        if (_log) Debug.Log("[SpookyDashAwakened] Applied spooky animation overrides.");
    }

    private void RestoreOverrides()
    {
        if (!_overridesApplied || _aoc == null) return;

        _scratchPairs.Clear();
        foreach (var kv in _originalMap) _scratchPairs.Add(kv);
        _aoc.ApplyOverrides(_scratchPairs);

        // Restore animator speed
        if (_overrideAnimatorSpeed)
            _normalAnimator.speed = _originalAnimatorSpeed;

        _overridesApplied = false;
        // Optional: _normalAnimator.Update(0f);
        if (_log) Debug.Log("[SpookyDashAwakened] Restored original animation overrides.");
    }

    private void ForceRestoreNow()
    {
        ArmRestore(false);
        RestoreOverrides();
    }

    private void ArmRestore(bool arm)
    {
        _restoreArmed   = arm;
        _restoreArmTime = Time.time;
    }

    /// <summary>
    /// Returns true if any of the given clips are playing on any layer,
    /// considering both the current state and the next state (during blends).
    /// </summary>
    private bool IsAnyOfClipsPlaying(HashSet<AnimationClip> clipSet)
    {
        if (_normalAnimator == null || clipSet == null || clipSet.Count == 0) return false;

        int layers = _normalAnimator.layerCount;
        for (int layer = 0; layer < layers; layer++)
        {
            var arr = _normalAnimator.GetCurrentAnimatorClipInfo(layer);
            for (int i = 0; i < arr.Length; i++)
            {
                var clip = arr[i].clip;
                if (clip && clipSet.Contains(clip)) return true;
            }

            var next = _normalAnimator.GetNextAnimatorClipInfo(layer);
            for (int i = 0; i < next.Length; i++)
            {
                var clip = next[i].clip;
                if (clip && clipSet.Contains(clip)) return true;
            }
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Afterimage trail
    private void CacheRenderers()
    {
        _skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _staticMeshes = GetComponentsInChildren<MeshRenderer>(true);

        // Also cache normal visual renderers for swap
        if (_normalVisualRoot != null)
            _normalRenderers = _normalVisualRoot.GetComponentsInChildren<Renderer>(true);
        else
            _normalRenderers = GetComponentsInChildren<Renderer>(true);
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