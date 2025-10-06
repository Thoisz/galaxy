using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpookyDashAnimationSwapper : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerDash playerDash;   // your dash component
    [SerializeField] private SpookyDash spookyDash;   // exposes IsCurrentlySpookyDashing()

    [Header("Clip Overrides (Base -> Spooky)")]
    [SerializeField] private ClipPair[] overridesToApply;

    [Tooltip("Keep spooky overrides active through phase-grace so blends out stay smooth.")]
    [SerializeField] private bool includePhaseGrace = false;   // default OFF for anti-spam

    [Tooltip("Safety: force-restore after this many seconds post spooky end. 0 = no timeout.")]
    [SerializeField] private float postDashRestoreTimeout = 0.05f; // short timeout

    [Header("Spooky Dash Animation Speed")]
    [Tooltip("Enable to boost Animator speed only while spooky dash clips are playing.")]
    [SerializeField] private bool controlAnimatorSpeed = true;

    [Tooltip("If normal dash feels like 5 and spooky should feel ~8, use 8/5 ≈ 1.6.")]
    [SerializeField] private float spookySpeedMultiplier = 1.6f;

    [Tooltip("How quickly to ease Animator.speed back to normal after spooky fully ends.")]
    [SerializeField] private float speedBlendOutTime = 0.14f;

    [Header("Pre-Exit Ease (smooth the tail of spooky clip)")]
    [Tooltip("Enable gradual slowdown during the LAST portion of the spooky clip.")]
    [SerializeField] private bool preExitEaseEnabled = false; // default OFF while debugging

    [Tooltip("Percent of the spooky clip tail to ease (0.0–0.9). e.g. 0.25 = last 25%.")]
    [SerializeField, Range(0f, 0.9f)] private float preExitEaseWindow = 0.25f;

    [Tooltip("Target speed multiplier at the very end of the spooky clip (≥1).")]
    [SerializeField] private float preExitLandingMultiplier = 1.1f;

    [Header("Soft Landing Crossfade")]
    [Tooltip("If a next state is already queued (Run/Idle), we re-crossfade with this duration for a smoother handoff.")]
    [SerializeField] private bool softLandingEnabled = false; // default OFF for anti-spam

    [Tooltip("Crossfade duration (seconds) applied once when spooky ends and next state is detected.")]
    [SerializeField] private float softLandingCrossfade = 0.18f;

    [Serializable]
    public struct ClipPair
    {
        public AnimationClip baseClip;   // clip used in your controller
        public AnimationClip spookyClip; // replacement during spooky dash
    }

    private RuntimeAnimatorController _originalController;
    private AnimatorOverrideController _aoc;           // stays bound the whole time
    private bool _applied;

    private readonly Dictionary<AnimationClip, AnimationClip> _originalMap = new();
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> _scratchPairs = new();
    private HashSet<AnimationClip> _spookyClipSet;     // quick membership checks

    // Restore flow control
    private bool  _restoreArmed;
    private float _restoreArmTime;

    // Speed control
    private float _baseAnimatorSpeed = 1f;
    private bool  _speedDriverActive;
    private float _speedRestoreT;

    // Soft landing guard
    private bool _didSoftLandingThisCycle;

    private void Awake()
    {
        if (!animator)   animator   = GetComponent<Animator>();
        if (!playerDash) playerDash = GetComponent<PlayerDash>();
        if (!spookyDash) spookyDash = GetComponent<SpookyDash>();

        if (!animator || !playerDash || !spookyDash)
        {
            Debug.LogError("[SpookyDashAnimationSwapper] Missing Animator/PlayerDash/SpookyDash.");
            enabled = false; 
            return;
        }

        _baseAnimatorSpeed = animator.speed;

        // Build the spooky clip lookup
        _spookyClipSet = new HashSet<AnimationClip>();
        for (int i = 0; i < overridesToApply.Length; i++)
            if (overridesToApply[i].spookyClip) _spookyClipSet.Add(overridesToApply[i].spookyClip);

        _originalController = animator.runtimeAnimatorController;

        // Wrap once and keep it bound so transitions remain intact
        _aoc = new AnimatorOverrideController { runtimeAnimatorController = _originalController };
        animator.runtimeAnimatorController = _aoc;

        // Snapshot current overrides so we can restore exactly
        _scratchPairs.Clear();
        _aoc.GetOverrides(_scratchPairs);
        _originalMap.Clear();
        foreach (var kv in _scratchPairs) _originalMap[kv.Key] = kv.Value;
    }

    private void OnDisable()
    {
        ForceRestoreNow();
        if (controlAnimatorSpeed) animator.speed = _baseAnimatorSpeed;
        _speedDriverActive = false;
        _didSoftLandingThisCycle = false;
    }

    private void Update()
    {
        bool spookyGate = spookyDash.IsCurrentlySpookyDashing();
        bool spookyActiveNow = includePhaseGrace
            ? spookyGate
            : (playerDash.IsDashing() && spookyGate);

        // 🔒 CRITICAL ANTI-SPAM GUARD:
        // If a dash is happening but it's NOT a spooky dash for this attempt,
        // immediately restore base clips so re-entering dash cannot play spooky.
        bool dashNow = playerDash.IsDashing();
        bool nonSpookyDashNow = dashNow && !spookyDash.IsCurrentlySpookyDashing();
        if (nonSpookyDashNow && _applied)
        {
            ForceRestoreNow();
            _restoreArmed = false;
        }

        // Apply exactly when spooky dash begins
        if (spookyActiveNow && !_applied)
        {
            ApplyOverrides();
            _restoreArmed = false;
            _didSoftLandingThisCycle = false;
        }

        // When spookyActive ends, ARM a restore (allow short blend-out window)
        if (!spookyActiveNow && _applied && !_restoreArmed)
        {
            _restoreArmed   = true;
            _restoreArmTime = Time.time;
        }

        // If armed, we’re in the blend-out zone
        if (_restoreArmed)
        {
            if (softLandingEnabled && !_didSoftLandingThisCycle)
            {
                TrySoftLandingCrossfade();
            }

            bool anySpookyPlaying = IsAnyOfClipsPlaying(_spookyClipSet);
            bool timedOut = (postDashRestoreTimeout > 0f) &&
                            (Time.time - _restoreArmTime >= postDashRestoreTimeout);

            if (!anySpookyPlaying || timedOut)
            {
                RestoreOverrides();
                _restoreArmed = false;
            }
        }

        // Drive animator speed (boost while spooky, pre-exit easing, then ease back)
        if (controlAnimatorSpeed)
            DriveAnimatorSpeed(spookyActiveNow);
    }

    private void ApplyOverrides()
    {
        if (_applied) return;

        // Start from whatever is currently applied
        _scratchPairs.Clear();
        _aoc.GetOverrides(_scratchPairs);

        var working = new Dictionary<AnimationClip, AnimationClip>(_scratchPairs.Count);
        foreach (var kv in _scratchPairs) working[kv.Key] = kv.Value;

        // Swap only requested base clips
        for (int i = 0; i < overridesToApply.Length; i++)
        {
            var baseClip = overridesToApply[i].baseClip;
            var spooky   = overridesToApply[i].spookyClip;
            if (!baseClip || !spooky) continue;

            if (working.ContainsKey(baseClip)) working[baseClip] = spooky;
            else                                working.Add(baseClip, spooky);
        }

        _scratchPairs.Clear();
        foreach (var kv in working) _scratchPairs.Add(kv);
        _aoc.ApplyOverrides(_scratchPairs);

        _applied = true;

        // Speed driver ON immediately
        if (controlAnimatorSpeed)
        {
            _speedDriverActive = true;
            _speedRestoreT = 0f;
        }
    }

    private void RestoreOverrides()
    {
        if (!_applied) return;

        _scratchPairs.Clear();
        foreach (var kv in _originalMap) _scratchPairs.Add(kv);
        _aoc.ApplyOverrides(_scratchPairs);

        _applied = false;

        // Start easing animator.speed back to base
        if (controlAnimatorSpeed)
        {
            _speedDriverActive = true;
            _speedRestoreT = 0f;
        }
    }

    private void ForceRestoreNow()
    {
        _restoreArmed = false;
        if (_applied) RestoreOverrides();
    }

    /// <summary>Returns true if any of the given clips are playing (current or next on any layer).</summary>
    private bool IsAnyOfClipsPlaying(HashSet<AnimationClip> clipSet)
    {
        if (clipSet == null || clipSet.Count == 0) return false;

        int layers = animator.layerCount;
        for (int layer = 0; layer < layers; layer++)
        {
            var arr = animator.GetCurrentAnimatorClipInfo(layer);
            for (int i = 0; i < arr.Length; i++)
            {
                var clip = arr[i].clip;
                if (clip && clipSet.Contains(clip)) return true;
            }

            var next = animator.GetNextAnimatorClipInfo(layer);
            for (int i = 0; i < next.Length; i++)
            {
                var clip = next[i].clip;
                if (clip && clipSet.Contains(clip)) return true;
            }
        }
        return false;
    }

    /// <summary>Re-crossfades once to the controller’s chosen next state with a longer duration.</summary>
    private void TrySoftLandingCrossfade()
    {
        int layers = animator.layerCount;
        bool didAnything = false;

        for (int layer = 0; layer < layers; layer++)
        {
            var nextInfo = animator.GetNextAnimatorStateInfo(layer);
            if (nextInfo.fullPathHash != 0 && nextInfo.length > 0f)
            {
                animator.CrossFade(nextInfo.fullPathHash, Mathf.Max(0.01f, softLandingCrossfade), layer, 0f);
                didAnything = true;
            }
        }

        if (didAnything) _didSoftLandingThisCycle = true;
    }

    /// <summary>Boosts speed while spooky clip plays; eases to landing near clip end; restores after.</summary>
    private void DriveAnimatorSpeed(bool spookyActiveNow)
    {
        if (!controlAnimatorSpeed) return;

        bool spookyPlayingNow = _applied && IsAnyOfClipsPlaying(_spookyClipSet);

        if (spookyPlayingNow)
        {
            float target = _baseAnimatorSpeed * Mathf.Max(0.01f, spookySpeedMultiplier);

            if (preExitEaseEnabled && preExitLandingMultiplier >= 1f)
            {
                int layers = animator.layerCount;
                for (int layer = 0; layer < layers; layer++)
                {
                    var infos = animator.GetCurrentAnimatorClipInfo(layer);
                    var st    = animator.GetCurrentAnimatorStateInfo(layer);

                    if (animator.GetLayerWeight(layer) < 0.5f) continue;

                    bool hasSpookyCurrent = false;
                    foreach (var ci in infos)
                    {
                        if (ci.clip && _spookyClipSet.Contains(ci.clip)) { hasSpookyCurrent = true; break; }
                    }
                    if (!hasSpookyCurrent) continue;

                    float norm = st.normalizedTime;
                    float frac = norm - Mathf.Floor(norm);

                    float startEase = 1f - Mathf.Clamp01(preExitEaseWindow);
                    if (frac >= startEase)
                    {
                        float t = Mathf.InverseLerp(startEase, 1f, frac);
                        float eased = Mathf.SmoothStep(0f, 1f, t);
                        float landing = _baseAnimatorSpeed * preExitLandingMultiplier;
                        target = Mathf.Lerp(target, landing, eased);
                        break;
                    }
                }
            }

            animator.speed = target;
            _speedRestoreT = 0f; // hold while spooky
            _speedDriverActive = true;
            return;
        }

        // No spooky clips: if we’ve recently changed speed, ease back
        if (_speedDriverActive)
        {
            if (speedBlendOutTime <= 0f)
            {
                animator.speed = _baseAnimatorSpeed;
                _speedDriverActive = false;
                return;
            }

            _speedRestoreT += Time.deltaTime / Mathf.Max(0.0001f, speedBlendOutTime);
            float t = Mathf.Clamp01(_speedRestoreT);

            float from = animator.speed;
            float to   = _baseAnimatorSpeed;
            animator.speed = Mathf.Lerp(from, to, t);

            if (t >= 1f)
            {
                animator.speed = _baseAnimatorSpeed;
                _speedDriverActive = false;
            }
        }
    }
}