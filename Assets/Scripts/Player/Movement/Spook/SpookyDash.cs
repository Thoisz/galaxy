using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class SpookyDash : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Will auto-grab from same GameObject if left empty.")]
    [SerializeField] private PlayerDash _playerDash;

    [Header("Phase Settings")]
    [Tooltip("Layer your player switches to during dash.")]
    [SerializeField] private string _playerPhaseLayerName = "PlayerPhase";

    [Tooltip("Keep phasing this long after dash ends to avoid popping inside colliders.")]
    [SerializeField] private float _phaseGraceAfterDash = 0.12f;

    [Header("Debug")]
    [SerializeField] private bool _log = false;

    // cached
    private int _playerPhaseLayer = -1;
    private Collider[] _allCols;
    private int[] _originalLayers;
    private bool _isPhasing = false;
    private Coroutine _graceRoutine;

    private void Awake()
    {
        if (_playerDash == null)
            _playerDash = GetComponent<PlayerDash>();

        if (_playerDash == null)
        {
            Debug.LogError("[SpookyDash] No PlayerDash found on this object. Add SpookyDash to the same GO as PlayerDash.");
            enabled = false;
            return;
        }

        _playerPhaseLayer = LayerMask.NameToLayer(_playerPhaseLayerName);
        if (_playerPhaseLayer < 0)
            Debug.LogError($"[SpookyDash] Layer '{_playerPhaseLayerName}' not found. Create it in Project Settings > Tags & Layers.");

        RefreshPlayerColliders();
    }

    private void OnEnable()
    {
        // safety: if something hot-reloads, ensure we’re in a restored state
        SetPhase(false, force: true);
    }

    private void OnDisable()
    {
        SetPhase(false, force: true);
    }

    private void Update()
    {
        if (_playerDash == null) return;

        bool wantPhase = _playerDash.IsDashing();

        if (wantPhase)
        {
            // cancel any pending restore and phase immediately
            if (_graceRoutine != null) { StopCoroutine(_graceRoutine); _graceRoutine = null; }
            SetPhase(true);
        }
        else
        {
            // start/refresh small grace window after dash ends
            if (_isPhasing && _graceRoutine == null)
                _graceRoutine = StartCoroutine(GraceThenRestore());
        }
    }

    private IEnumerator GraceThenRestore()
    {
        yield return new WaitForSeconds(_phaseGraceAfterDash);
        SetPhase(false);
        _graceRoutine = null;
    }

    private void RefreshPlayerColliders()
    {
        _allCols = GetComponentsInChildren<Collider>(true);
        if (_allCols == null) _allCols = new Collider[0];

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
            if (_playerPhaseLayer < 0)
            {
                // no valid layer, nothing to do
                return;
            }

            for (int i = 0; i < _allCols.Length; i++)
            {
                var c = _allCols[i];
                if (!c) continue;
                c.gameObject.layer = _playerPhaseLayer;
            }
            _isPhasing = true;
            if (_log) Debug.Log("[SpookyDash] PHASE ON");
        }
        else
        {
            for (int i = 0; i < _allCols.Length; i++)
            {
                var c = _allCols[i];
                if (!c) continue;
                int original = (_originalLayers != null && i < _originalLayers.Length) ? _originalLayers[i] : c.gameObject.layer;
                c.gameObject.layer = original;
            }
            _isPhasing = false;
            if (_log) Debug.Log("[SpookyDash] PHASE OFF");
        }
    }
}