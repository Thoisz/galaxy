using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnergyBall : MonoBehaviour
{
    [Header("Target (Mesh Flipbook)")]
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private int materialIndex = 0;

    [Header("Atlas layout (grid counts)")]
    [SerializeField] private int  columns = 4;
    [SerializeField] private int  rows    = 1;
    [SerializeField] private bool rowZeroAtTop = true;
    [SerializeField] private bool flipV = false;

    [Header("Shader texture slots")]
    [SerializeField] private string urpBaseMapName   = "_BaseMap";
    [SerializeField] private string legacyMainTexName= "_MainTex";

    [Header("Timing")]
    [SerializeField] private float framesPerSecond = 10f;

    [Header("Frame order (1-based tile indices)")]
    [SerializeField] private List<int> introFrames = new();
    [SerializeField] private List<int> loopFrames  = new();

    [Header("Per-frame scales (optional)")]
    [SerializeField] private List<Vector3> introScales = new();
    [SerializeField] private List<Vector3> loopScales  = new();
    [SerializeField] private Vector3 defaultScale = new Vector3(50, 70, 60);

    [Header("Visibility")]
    [SerializeField] private bool hideMeshWhenIdle = true;

    // ── Thruster-hole gate & hookup ───────────────────────────
    [SerializeField] private bool        notifyThrusterHoles = true;
    [SerializeField] private int         thrusterHoleGateIntroIndex = 11; // 0-based into introScales
    [SerializeField] private JetpackRare jetpackRare; // auto-found
    bool _holesSignaled;

    // ── runtime state ─────────────────────────────────────────
    Material _mat;
    int      _texId = -1;
    Vector2  _tileScale;

    float _meshClock;
    int   _introCursor;
    int   _loopCursor;
    bool  _introDone;

    bool _charging;

    void Awake()
    {
        if (!targetRenderer) targetRenderer = GetComponent<MeshRenderer>();

        if (targetRenderer && targetRenderer.sharedMaterials != null)
        {
            var mats = targetRenderer.materials;                   // instances
            if (materialIndex >= 0 && materialIndex < mats.Length)
            {
                _mat = mats[materialIndex];
                targetRenderer.materials = mats;
            }
        }

        if (_mat)
        {
            if      (_mat.HasProperty(urpBaseMapName))    _texId = Shader.PropertyToID(urpBaseMapName);
            else if (_mat.HasProperty(legacyMainTexName)) _texId = Shader.PropertyToID(legacyMainTexName);

            _tileScale = new Vector2(columns > 0 ? 1f/columns : 1f,
                                     rows    > 0 ? 1f/rows    : 1f);
            ApplyTiling(_tileScale);
        }

        if (!jetpackRare) jetpackRare = GetComponentInParent<JetpackRare>(true);

        // initial visibility & reset
        EnsureMeshVisible(!_charging ? !hideMeshWhenIdle : true);
        ResetMeshAnim();
    }

    void Update()
    {
        if (!_charging || _mat == null || _texId == -1) return;

        float step = Mathf.Max(0.01f, framesPerSecond) * Time.deltaTime;
        _meshClock += step;

        while (_meshClock >= 1f)
        {
            _meshClock -= 1f;

            if (!_introDone && introFrames.Count > 0)
            {
                _introCursor++;
                MaybeNotifyThrusterHoles(_introCursor);

                if (_introCursor >= introFrames.Count)
                {
                    _introDone  = true;
                    _loopCursor = 0;
                    if (loopFrames.Count > 0)
                        ApplyMeshFrame(loopFrames[_loopCursor], GetLoopScale(_loopCursor));
                    else
                        ApplyMeshFrame(introFrames[^1], defaultScale);
                    break;
                }
                ApplyMeshFrame(introFrames[_introCursor], GetIntroScale(_introCursor));
            }
            else
            {
                if (loopFrames.Count == 0) break;
                _loopCursor = (_loopCursor + 1) % loopFrames.Count;
                ApplyMeshFrame(loopFrames[_loopCursor], GetLoopScale(_loopCursor));
            }
        }
    }

    // ── public API ────────────────────────────────────────────
    public void SetCharging(bool on)
    {
        if (on == _charging) return;
        _charging = on;

        if (_charging)
        {
            EnsureMeshVisible(true);
            ResetMeshAnim();

            if (introFrames.Count > 0)
            {
                ApplyMeshFrame(introFrames[0], GetIntroScale(0));
                MaybeNotifyThrusterHoles(0);
            }
            else if (loopFrames.Count > 0)
            {
                ApplyMeshFrame(loopFrames[0], GetLoopScale(0));
            }
        }
        else
        {
            // release forced thruster holes when charging ends
            if (notifyThrusterHoles) jetpackRare?.SetThrusterHolesForced(false);

            ResetMeshAnim();
            if (hideMeshWhenIdle) EnsureMeshVisible(false);
        }
    }

    // ── internals ─────────────────────────────────────────────
    void ResetMeshAnim()
    {
        _meshClock = 0f;
        _introCursor = 0;
        _loopCursor  = 0;
        _introDone   = introFrames.Count == 0;
        _holesSignaled = false;
    }

    void EnsureMeshVisible(bool on)
    {
        if (targetRenderer) targetRenderer.enabled = on;
    }

    void MaybeNotifyThrusterHoles(int introIndex)
    {
        if (!notifyThrusterHoles || _holesSignaled) return;
        if (introScales == null || introScales.Count <= thrusterHoleGateIntroIndex) return;

        if (introIndex >= thrusterHoleGateIntroIndex)
        {
            jetpackRare?.SetThrusterHolesForced(true);
            _holesSignaled = true;
        }
    }

    Vector3 GetIntroScale(int idx) => (idx >= 0 && idx < introScales.Count) ? introScales[idx] : defaultScale;
    Vector3 GetLoopScale (int idx) => (idx >= 0 && idx < loopScales.Count)  ? loopScales[idx]  : defaultScale;

    void ApplyMeshFrame(int oneBasedTileIndex, Vector3 scale)
    {
        if (_mat == null || _texId == -1 || columns <= 0 || rows <= 0) return;

        int total   = columns * rows;
        int clamped = Mathf.Clamp(oneBasedTileIndex, 1, Mathf.Max(1, total)) - 1;

        int col = clamped % columns;
        int row = clamped / columns;

        float u = col * _tileScale.x;
        float v = rowZeroAtTop ? (1f - _tileScale.y) - (row * _tileScale.y)
                               : row * _tileScale.y;

        if (flipV) v = 1f - _tileScale.y - v;

        _mat.SetTextureScale (_texId, _tileScale);
        _mat.SetTextureOffset(_texId, new Vector2(u, v));

        transform.localScale = scale;
    }

    void ApplyTiling(Vector2 tiling)
    {
        if (_mat == null || _texId == -1) return;
        _mat.SetTextureScale (_texId, tiling);
        _mat.SetTextureOffset(_texId, Vector2.zero);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        columns         = Mathf.Max(1, columns);
        rows            = Mathf.Max(1, rows);
        framesPerSecond = Mathf.Max(0.01f, framesPerSecond);
    }
#endif
}