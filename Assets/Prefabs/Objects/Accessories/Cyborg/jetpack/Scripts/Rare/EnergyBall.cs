using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a flipbook on a mesh by sliding UVs over a texture atlas,
/// and optionally changes the mesh scale per frame.
/// Intended for a 3-frame intro (once) then a 4-frame loop while charging.
/// </summary>
[DisallowMultipleComponent]
public class EnergyBall : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Renderer targetRenderer;   // if null, auto-get on this GO
    [SerializeField] private int materialIndex = 0;     // which material slot

    [Header("Atlas layout")]
    [Tooltip("Number of columns (cells across) in the sprite sheet.")]
    [SerializeField] private int columns = 4;
    [Tooltip("Number of rows (cells down) in the sprite sheet.")]
    [SerializeField] private int rows    = 2; // set to 2 if you actually have >=5 frames

    [Tooltip("URP uses _BaseMap, legacy uses _MainTex. We'll auto-pick.")]
    [SerializeField] private string urpBaseMapName = "_BaseMap";
    [SerializeField] private string legacyMainTex  = "_MainTex";
    [Tooltip("If your sheet’s first row is at the top of the image, enable this.")]
    [SerializeField] private bool flipV = false;

    [Header("Timings")]
    [SerializeField] private float framesPerSecond = 12f;

    [Header("Frame order")]
    [Tooltip("Intro: played once, then loop part begins.")]
    [SerializeField] private List<int> introFrames = new() { 0, 1, 2 };
    [Tooltip("Loop: repeats while charging.")]
    [SerializeField] private List<int> loopFrames  = new() { 3, 4, 5, 6 };

    [Header("Per-frame scales (optional)")]
    [Tooltip("Per-intro-frame local scale; if missing, falls back to defaultScale.")]
    [SerializeField] private List<Vector3> introScales = new() {
        new Vector3(35,35,45),
        new Vector3(42,42,50),
        new Vector3(50,50,55),
    };

    [Tooltip("Per-loop-frame local scale; if missing, falls back to defaultScale.")]
    [SerializeField] private List<Vector3> loopScales  = new() {
        new Vector3(40,40,50), // frame index 3
        new Vector3(49,49,51), // 4
        new Vector3(45,45,55), // 5
        new Vector3(40,40,50), // 6
    };

    [SerializeField] private Vector3 defaultScale = new Vector3(50,50,50);

    // ───────── state ─────────
    private Material _instancedMat;
    private int _texPropId = -1;
    private bool _charging  = false;
    private Coroutine _playCo;

    void Awake()
    {
        if (!targetRenderer) targetRenderer = GetComponent<Renderer>();
        EnsureMaterialInstance();
        HideRenderer();
    }

    void OnDisable()
    {
        if (_playCo != null) StopCoroutine(_playCo);
        _playCo = null;
        _charging = false;
    }

    // Called by JetpackRare
    public void SetCharging(bool on)
    {
        if (on == _charging) return;
        _charging = on;

        if (_charging)
        {
            EnsureMaterialInstance();
            ShowRenderer();
            if (_playCo != null) StopCoroutine(_playCo);
            _playCo = StartCoroutine(Co_Play());
        }
        else
        {
            if (_playCo != null) StopCoroutine(_playCo);
            _playCo = null;
            HideRenderer();
        }
    }

    IEnumerator Co_Play()
    {
        float dt = 1f / Mathf.Max(0.0001f, framesPerSecond);

        // 1) Intro (once)
        for (int i = 0; i < introFrames.Count; i++)
        {
            int frame = introFrames[i];
            ApplyFrame(frame);
            ApplyIntroScale(i);
            yield return new WaitForSeconds(dt);
        }

        // 2) Loop (repeat while charging)
        int li = 0;
        while (_charging)
        {
            if (loopFrames == null || loopFrames.Count == 0)
            {
                yield return null; // nothing to do, avoid tight loop
                continue;
            }

            int frame = loopFrames[li];
            ApplyFrame(frame);
            ApplyLoopScale(li);

            li++;
            if (li >= loopFrames.Count) li = 0;

            yield return new WaitForSeconds(dt);
        }
    }

    // ───────── helpers ─────────

    void EnsureMaterialInstance()
    {
        if (!targetRenderer) return;

        // pick material slot
        var mats = targetRenderer.materials; // creates instances (good)
        if (materialIndex < 0 || materialIndex >= mats.Length)
            materialIndex = 0;

        _instancedMat = mats[materialIndex];

        // choose texture property
        if (_instancedMat != null)
        {
            if (_instancedMat.HasProperty(urpBaseMapName))
                _texPropId = Shader.PropertyToID(urpBaseMapName);
            else if (_instancedMat.HasProperty(legacyMainTex))
                _texPropId = Shader.PropertyToID(legacyMainTex);
            else
                _texPropId = -1;

            // Set the per-cell tiling once (assuming evenly spaced grid)
            Vector2 tiling = new Vector2(1f / Mathf.Max(1, columns),
                                         1f / Mathf.Max(1, rows));
            if (_texPropId != -1)
                _instancedMat.SetTextureScale(_texPropId, tiling);
        }

        // reassign to ensure the instance is used
        mats[materialIndex] = _instancedMat;
        targetRenderer.materials = mats;
    }

    void ApplyFrame(int frameIndex)
    {
        if (_instancedMat == null || _texPropId == -1) return;

        int total = Mathf.Max(1, columns * rows);
        if (frameIndex < 0 || frameIndex >= total)
        {
            // guard: wrap into range and warn once
            int wrapped = Mathf.FloorToInt(Mathf.Repeat(frameIndex, total));
            Debug.LogWarning($"[EnergyBall] Frame {frameIndex} out of atlas range (total {total}). Wrapping to {wrapped}.", this);
            frameIndex = wrapped;
        }

        int col = frameIndex % columns;
        int row = frameIndex / columns;

        float u = (float)col / Mathf.Max(1, columns);
        float v = (float)row / Mathf.Max(1, rows);
        if (flipV)
        {
            // flip vertically: count rows from top
            v = 1f - (1f / Mathf.Max(1, rows)) - v;
        }

        // apply offset
        _instancedMat.SetTextureOffset(_texPropId, new Vector2(u, v));
    }

    void ApplyIntroScale(int introIndex)
    {
        Vector3 s = (introIndex >= 0 && introIndex < introScales.Count)
            ? introScales[introIndex]
            : defaultScale;
        transform.localScale = s;
    }

    void ApplyLoopScale(int loopIndex)
    {
        Vector3 s = (loopIndex >= 0 && loopIndex < loopScales.Count)
            ? loopScales[loopIndex]
            : defaultScale;
        transform.localScale = s;
    }

    void ShowRenderer()
    {
        if (!targetRenderer) return;
        targetRenderer.enabled = true;
    }

    void HideRenderer()
    {
        if (!targetRenderer) return;
        targetRenderer.enabled = false;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        columns = Mathf.Max(1, columns);
        rows    = Mathf.Max(1, rows);
        framesPerSecond = Mathf.Max(0.01f, framesPerSecond);
        if (!targetRenderer) targetRenderer = GetComponent<Renderer>();
    }
#endif
}