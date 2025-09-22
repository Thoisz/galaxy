using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering; // for RenderQueue enum

[DisallowMultipleComponent]
public class SlabRingSimpleFX : MonoBehaviour
{
    [Header("Scale In")]
    [Tooltip("Final uniform scale the ring reaches.")]
    public float targetScale = 70f;

    [Tooltip("Seconds from 0 → targetScale, and also for shrink back down.")]
    public float scaleDuration = 0.32f;

    [Tooltip("Easing for the scale-in/out.")]
    public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Hold")]
    [Tooltip("How long the ring stays visible at full size before fading.")]
    public float holdDuration = 2.0f;

    [Header("Fade Out")]
    [Tooltip("Seconds to fade alpha 1 → 0 before shrinking back down.")]
    public float fadeDuration = 0.35f;

    [Header("Play Options")]
    [Tooltip("Play automatically on enable.")]
    public bool playOnEnable = true;

    [Tooltip("Destroy GameObject after it finishes.")]
    public bool destroyOnFinish = true;

    // OPTIONAL: who spawned/owns this FX (used by your GroundbreakFXRoot)
    private GroundbreakFXRoot _owner;
    public void SetOwner(GroundbreakFXRoot owner) => _owner = owner;

    // cached renderers & MPBs
    private readonly List<Renderer> _renderers = new List<Renderer>();
    private readonly List<MaterialPropertyBlock> _mpbs = new List<MaterialPropertyBlock>();
    private readonly List<Material[]> _instancedMats = new List<Material[]>();

    // common property ids
    private static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ID_Color     = Shader.PropertyToID("_Color");
    private static readonly int ID_Surface   = Shader.PropertyToID("_Surface"); // URP Lit: 0=Opaque, 1=Transparent

    private void Awake()
    {
        CacheRenderers();
    }

    private void OnEnable()
    {
        SetUniformScale(0f);
        SetAlpha(1f);

        if (playOnEnable)
            Play();
    }

    /// <summary>Give all slab renderers a per-instance copy of 'mat' (same shader, same params).</summary>
    public void ApplyMaterial(Material mat)
    {
        if (mat == null) return;

        if (_renderers.Count == 0)
            CacheRenderers();

        _instancedMats.Clear();

        for (int i = 0; i < _renderers.Count; i++)
        {
            var r = _renderers[i];
            if (!r) { _instancedMats.Add(null); continue; }

            // Using .materials/.material creates instances (OK for an FX burst).
            var shared = r.sharedMaterials;

            if (shared == null || shared.Length == 0)
            {
                var inst = new Material(mat) { name = mat.name + " (SlabInst)" };
                r.material = inst;
                _instancedMats.Add(r.materials);
            }
            else
            {
                var arr = new Material[shared.Length];
                for (int s = 0; s < arr.Length; s++)
                    arr[s] = new Material(mat) { name = mat.name + " (SlabInst)" };

                r.materials = arr;
                _instancedMats.Add(arr);
            }

            var matsNow = r.materials;
            for (int m = 0; m < matsNow.Length; m++)
                TryMakeTransparent(matsNow[m]);
        }

        RefreshMPBs();
    }

    /// <summary>Starts the effect (scale-in → hold → fade → shrink → optional destroy).</summary>
    public void Play()
    {
        StopAllCoroutines();
        StartCoroutine(Co_Play());
    }

    private IEnumerator Co_Play()
    {
        // Scale in 0 → targetScale
        float t = 0f;
        while (t < scaleDuration)
        {
            t += Time.deltaTime;
            float k = scaleDuration <= 0f ? 1f : Mathf.Clamp01(t / scaleDuration);
            float eased = Mathf.Clamp01(scaleCurve.Evaluate(k));
            SetUniformScale(Mathf.LerpUnclamped(0f, targetScale, eased));
            yield return null;
        }
        SetUniformScale(targetScale);

        // Hold
        if (holdDuration > 0f)
            yield return new WaitForSeconds(holdDuration);

        // Fade 1 → 0
        t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float k = fadeDuration <= 0f ? 1f : Mathf.Clamp01(t / fadeDuration);
            SetAlpha(1f - k);
            yield return null;
        }
        SetAlpha(0f);

        // Shrink targetScale → 0
        t = 0f;
        while (t < scaleDuration)
        {
            t += Time.deltaTime;
            float k = scaleDuration <= 0f ? 1f : Mathf.Clamp01(t / scaleDuration);
            float eased = Mathf.Clamp01(scaleCurve.Evaluate(k));
            SetUniformScale(Mathf.LerpUnclamped(targetScale, 0f, eased));
            yield return null;
        }
        SetUniformScale(0f);

        if (destroyOnFinish)
            Destroy(gameObject);
    }

    // ───────── helpers ─────────

    private void CacheRenderers()
    {
        _renderers.Clear();
        _mpbs.Clear();
        _instancedMats.Clear();

        // ✅ Use the generic overload with type parameter
        GetComponentsInChildren<Renderer>(true, _renderers);

        for (int i = 0; i < _renderers.Count; i++)
        {
            var r = _renderers[i];
            var mpb = new MaterialPropertyBlock();
            if (r != null) r.GetPropertyBlock(mpb);
            _mpbs.Add(mpb);

            // Prime with current material instances (this will allocate instances; fine for FX)
            _instancedMats.Add(r ? r.materials : null);
        }
    }

    private void RefreshMPBs()
    {
        for (int i = 0; i < _renderers.Count; i++)
        {
            if (_mpbs[i] == null) _mpbs[i] = new MaterialPropertyBlock();
            var r = _renderers[i];
            if (r) r.GetPropertyBlock(_mpbs[i]);
        }
    }

    private void SetUniformScale(float s)
    {
        transform.localScale = new Vector3(s, s, s);
    }

    private void SetAlpha(float a)
    {
        // Uses MPB tint so we don’t overwrite material colors
        for (int i = 0; i < _renderers.Count; i++)
        {
            var r   = _renderers[i];
            if (!r) continue;

            var mpb = _mpbs[i];
            var mats = _instancedMats[i];

            bool wrote = false;

            // Prefer URP Lit _BaseColor if present on first material
            if (mats != null && mats.Length > 0 && mats[0] != null)
            {
                var m = mats[0];
                if (m.HasProperty(ID_BaseColor))
                {
                    Color c = m.GetColor(ID_BaseColor); c.a = a;
                    mpb.SetColor(ID_BaseColor, c);
                    wrote = true;
                }
                else if (m.HasProperty(ID_Color))
                {
                    Color c = m.GetColor(ID_Color); c.a = a;
                    mpb.SetColor(ID_Color, c);
                    wrote = true;
                }
            }

            if (!wrote)
            {
                // Fallback tint if material has neither property
                mpb.SetColor(ID_Color, new Color(1f, 1f, 1f, a));
            }

            r.SetPropertyBlock(mpb);
        }
    }

    private void TryMakeTransparent(Material m)
    {
        if (m == null) return;

        // URP Lit support
        if (m.HasProperty(ID_Surface))
        {
            m.SetFloat(ID_Surface, 1f); // Transparent
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_ZWrite", 0);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            return;
        }

        // Legacy Standard fallback
        if (m.HasProperty("_Mode"))
        {
            m.SetFloat("_Mode", 2f); // Fade
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetInt("_ZWrite", 0);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        }
    }
}