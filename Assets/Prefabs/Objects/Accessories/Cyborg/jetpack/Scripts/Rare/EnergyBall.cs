using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One-stop EnergyBall controller:
/// - Flipbook on a MeshRenderer material (Intro -> Loop)
/// - Optional per-frame local scales (Intro + Loop + default fallback)
/// - Optional charged flash as a SpriteRenderer that billboards to the camera
/// - Visibility handled internally (ball on while charging or charged; off otherwise)
///
/// Call:
///   SetCharging(true)  -> starts Intro, then Loop until SetCharging(false)
///   SetCharged(true)   -> shows + animates flash sprite at 1.5× (configurable)
/// </summary>
[DisallowMultipleComponent]
public class EnergyBall : MonoBehaviour
{
    [Header("Target (Mesh Flipbook)")]
    [SerializeField] private MeshRenderer targetRenderer;   // auto-find if empty
    [SerializeField] private int          materialIndex = 0;

    [Header("Atlas layout (grid counts)")]
    [Tooltip("Columns (tiles across) in the flipbook atlas.")]
    [SerializeField] private int columns = 4;
    [Tooltip("Rows (tiles down) in the flipbook atlas.")]
    [SerializeField] private int rows    = 1;

    [Tooltip("Row 0 is at the TOP of the atlas. If off, row 0 is at bottom.")]
    [SerializeField] private bool rowZeroAtTop = true;

    [Tooltip("Flip V (rarely needed). Leave OFF unless your atlas appears vertically inverted.")]
    [SerializeField] private bool flipV = false;

    [Header("Shader texture slots")]
    [Tooltip("URP Lit/BaseMap property name (used if present).")]
    [SerializeField] private string urpBaseMapName = "_BaseMap";
    [Tooltip("Legacy/BiRP _MainTex name (used if BaseMap is not present).")]
    [SerializeField] private string legacyMainTexName = "_MainTex";

    [Header("Timing")]
    [Tooltip("Flipbook FPS for Intro and Loop while CHARGING.")]
    [SerializeField] private float framesPerSecond = 10f;

    [Header("Frame order (1-based tile indices)")]
    [Tooltip("Intro plays once (indices are 1..columns*rows).")]
    [SerializeField] private List<int> introFrames = new();
    [Tooltip("Loop repeats while charging (indices are 1..columns*rows).")]
    [SerializeField] private List<int> loopFrames  = new();

    [Header("Per-frame scales (optional)")]
    [Tooltip("If provided, overrides local scale per Intro frame.")]
    [SerializeField] private List<Vector3> introScales = new();
    [Tooltip("If provided, overrides local scale per Loop frame.")]
    [SerializeField] private List<Vector3> loopScales  = new();
    [Tooltip("Fallback local scale when a per-frame scale is not specified.")]
    [SerializeField] private Vector3 defaultScale = new Vector3(50, 70, 60);

    [Header("Visibility")]
    [Tooltip("Disable mesh when neither charging nor charged.")]
    [SerializeField] private bool hideMeshWhenIdle = true;

    // ───────────────── Flash sprite (optional) ─────────────────
    [Header("Charged Flash (optional Sprite)")]
    [SerializeField] private SpriteRenderer flashRenderer;      // auto-find in children
    [SerializeField] private List<Sprite>   flashFrames = new();
    [Tooltip("Base playback rate for the flash sprite while charged.")]
    [SerializeField] private float flashBaseFps = 12f;
    [Tooltip("Speed multiplier applied while charged (e.g., 1.5×).")]
    [SerializeField] private float chargedSpeedMultiplier = 1.5f;
    [Tooltip("Hide the flash sprite when not charged.")]
    [SerializeField] private bool hideFlashWhenNotCharged = true;

    [Header("Billboarding")]
    [SerializeField] private bool   billboardFlashToCamera = true;
    [SerializeField] private Camera cameraOverride;            // auto-find

    // ── runtime state ──────────────────────────────────────────
    Material _mat;                // dedicated instance
    int _texId = -1;              // cached property id

    Vector2 _tileScale;
    float   _meshClock;
    int     _introCursor;
    int     _loopCursor;
    bool    _introDone;

    bool _charging;
    bool _charged;

    // Flash state
    int   _flashIndex;
    float _flashClock;

    void Awake()
    {
        // Mesh renderer
        if (!targetRenderer)
            targetRenderer = GetComponent<MeshRenderer>();

        // Make a safe material instance (no global changes)
        if (targetRenderer && targetRenderer.sharedMaterials != null)
        {
            var mats = targetRenderer.materials; // clones array & instances
            if (materialIndex < 0 || materialIndex >= mats.Length)
            {
                Debug.LogWarning($"[EnergyBall] materialIndex {materialIndex} out of range on '{name}'.", this);
            }
            else
            {
                _mat = mats[materialIndex];
                targetRenderer.materials = mats; // assign back to apply instances
            }
        }

        // Choose texture slot once
        if (_mat)
        {
            if (_mat.HasProperty(urpBaseMapName)) _texId = Shader.PropertyToID(urpBaseMapName);
            else if (_mat.HasProperty(legacyMainTexName)) _texId = Shader.PropertyToID(legacyMainTexName);
            else _texId = -1;

            _tileScale = new Vector2(columns > 0 ? 1f / columns : 1f, rows > 0 ? 1f / rows : 1f);
            ApplyTiling(_tileScale);
        }

        // Flash sprite auto-bind
        if (!flashRenderer)
            flashRenderer = GetComponentInChildren<SpriteRenderer>(true);

        // Camera auto-bind for billboarding
        if (!cameraOverride)
        {
            if (Camera.main) cameraOverride = Camera.main;
            else
            {
                var anyCam = FindObjectOfType<Camera>(true);
                if (anyCam) cameraOverride = anyCam;
            }
        }

        // Initial visibility
        EnsureMeshVisible(!_charging && !_charged ? !hideMeshWhenIdle : true);
        ApplyFlashVisibility();
        ResetMeshAnim();
        ResetFlashAnim();
    }

    void Update()
    {
        // ── Mesh flipbook (charging only) ──────────────────────
        if (_charging && _mat && (_texId != -1))
        {
            float step = Mathf.Max(0.01f, framesPerSecond) * Time.deltaTime;
            _meshClock += step;

            while (_meshClock >= 1f)
            {
                _meshClock -= 1f;

                if (!_introDone && introFrames.Count > 0)
                {
                    _introCursor++;
                    if (_introCursor >= introFrames.Count)
                    {
                        // Finished intro → move to loop
                        _introDone = true;
                        _loopCursor = 0;
                        if (loopFrames.Count > 0)
                            ApplyMeshFrame(loopFrames[_loopCursor], GetLoopScale(_loopCursor));
                        else
                            ApplyMeshFrame(introFrames[introFrames.Count - 1], defaultScale);
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

        // ── Charged flash sprite ───────────────────────────────
        if (_charged && flashRenderer && flashFrames.Count > 0)
        {
            float fps = Mathf.Max(0.01f, flashBaseFps * chargedSpeedMultiplier);
            _flashClock += fps * Time.deltaTime;
            while (_flashClock >= 1f)
            {
                _flashClock -= 1f;
                _flashIndex = (_flashIndex + 1) % flashFrames.Count;
                flashRenderer.sprite = flashFrames[_flashIndex];
            }
        }
    }

    void LateUpdate()
    {
        if (!_charged || !billboardFlashToCamera || !flashRenderer || !cameraOverride) return;

        // Billboard the flash sprite to camera
        Vector3 toCam = cameraOverride.transform.position - flashRenderer.transform.position;
        if (toCam.sqrMagnitude > 1e-6f)
        {
            flashRenderer.transform.rotation =
                Quaternion.LookRotation(-toCam.normalized, cameraOverride.transform.up);
        }
    }

    // ───────────────────── public API ─────────────────────────

    /// <summary>Start/stop the charging visuals. When starting, runs Intro then Loop.</summary>
    public void SetCharging(bool on)
    {
        if (on == _charging) return;
        _charging = on;

        if (_charging)
        {
            EnsureMeshVisible(true);
            ResetMeshAnim();

            // Apply first frame immediately for snappy response
            if (introFrames.Count > 0)
                ApplyMeshFrame(introFrames[0], GetIntroScale(0));
            else if (loopFrames.Count > 0)
                ApplyMeshFrame(loopFrames[0], GetLoopScale(0));
        }
        else
        {
            ResetMeshAnim();
            if (!_charged && hideMeshWhenIdle)
                EnsureMeshVisible(false);
        }
    }

    /// <summary>Show/hide & animate the charged flash sprite.</summary>
    public void SetCharged(bool on)
    {
        if (on == _charged) return;
        _charged = on;

        if (_charged)
        {
            ResetFlashAnim();
            ApplyFlashVisibility();

            // Make sure the mesh stays visible while charged,
            // unless you want it hidden—flip this to false if desired:
            EnsureMeshVisible(true);
        }
        else
        {
            ResetFlashAnim();
            ApplyFlashVisibility();

            if (!_charging && hideMeshWhenIdle)
                EnsureMeshVisible(false);
        }
    }

    // ─────────────────── internal helpers ─────────────────────

    void ResetMeshAnim()
    {
        _meshClock   = 0f;
        _introCursor = 0;
        _loopCursor  = 0;
        _introDone   = introFrames.Count == 0;
    }

    void ResetFlashAnim()
    {
        _flashClock = 0f;
        _flashIndex = 0;
        if (flashRenderer && flashFrames.Count > 0)
            flashRenderer.sprite = flashFrames[0];
    }

    void EnsureMeshVisible(bool on)
    {
        if (!targetRenderer) return;
        targetRenderer.enabled = on;
    }

    void ApplyFlashVisibility()
    {
        if (!flashRenderer) return;
        flashRenderer.enabled = _charged || !hideFlashWhenNotCharged;
    }

    Vector3 GetIntroScale(int idx)
    {
        if (idx >= 0 && idx < introScales.Count) return introScales[idx];
        return defaultScale;
    }
    Vector3 GetLoopScale(int idx)
    {
        if (idx >= 0 && idx < loopScales.Count) return loopScales[idx];
        return defaultScale;
    }

    void ApplyMeshFrame(int oneBasedTileIndex, Vector3 scale)
    {
        if (_mat == null || _texId == -1 || columns <= 0 || rows <= 0) return;

        int total = columns * rows;
        int clamped = Mathf.Clamp(oneBasedTileIndex, 1, Mathf.Max(1, total));
        int zero    = clamped - 1;

        int col = zero % columns;
        int row = zero / columns;

        // Convert to UV offset (bottom-left origin in Unity)
        float u = col * _tileScale.x;

        float v;
        if (rowZeroAtTop)
            v = (1f - _tileScale.y) - (row * _tileScale.y); // top row = highest V
        else
            v = row * _tileScale.y;

        if (flipV) v = 1f - _tileScale.y - v;

        _mat.SetTextureScale(_texId, _tileScale);
        _mat.SetTextureOffset(_texId, new Vector2(u, v));

        transform.localScale = scale;
    }

    void ApplyTiling(Vector2 tiling)
    {
        if (_mat == null || _texId == -1) return;
        _mat.SetTextureScale(_texId, tiling);
        _mat.SetTextureOffset(_texId, Vector2.zero);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        columns = Mathf.Max(1, columns);
        rows    = Mathf.Max(1, rows);
        framesPerSecond = Mathf.Max(0.01f, framesPerSecond);
        flashBaseFps    = Mathf.Max(0.01f, flashBaseFps);
        chargedSpeedMultiplier = Mathf.Max(0.01f, chargedSpeedMultiplier);
    }
#endif
}