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

    // Remember current alpha so re-assigning materials keeps the same fade level
    float _lastAlpha = 1f;


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
    if (_renderers.Count == 0) CacheRenderers();

    _instancedMats.Clear();

    for (int i = 0; i < _renderers.Count; i++)
    {
        var r = _renderers[i];
        var shared = r.sharedMaterials;

        Material[] arr;
        if (shared == null || shared.Length == 0)
        {
            arr = new Material[1];
            arr[0] = new Material(mat) { name = mat.name + " (SlabInst)" };
        }
        else
        {
            arr = new Material[shared.Length];
            for (int s = 0; s < arr.Length; s++)
                arr[s] = new Material(mat) { name = mat.name + " (SlabInst)" };
        }

        // Ensure transparency for fade
        for (int s = 0; s < arr.Length; s++) TryMakeTransparent(arr[s]);

        // Assign and clear any stale MPB so new material tint comes through
        r.materials = arr;
        r.SetPropertyBlock(null);
#if UNITY_2021_2_OR_NEWER
        for (int sub = 1; sub < arr.Length; sub++)
            r.SetPropertyBlock(null, sub);
#endif
        _instancedMats.Add(arr);
    }

    RefreshMPBs();

    // Re-apply current alpha so visual fade remains consistent if we changed the material mid-effect
    SetAlpha(_lastAlpha);
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

    void SetAlpha(float a)
{
    _lastAlpha = Mathf.Clamp01(a);

    for (int i = 0; i < _renderers.Count; i++)
    {
        var r   = _renderers[i];
        var mpb = _mpbs[i];
        if (mpb == null) { mpb = new MaterialPropertyBlock(); _mpbs[i] = mpb; }

        // Pull base color from the CURRENT instanced material so RGB tint matches the new ground mat
        Color baseCol = Color.white;
        var mats = _instancedMats[i];
        if (mats != null && mats.Length > 0 && mats[0] != null)
        {
            var m = mats[0];
            if (m.HasProperty(ID_BaseColor)) baseCol = m.GetColor(ID_BaseColor);
            else if (m.HasProperty(ID_Color)) baseCol = m.GetColor(ID_Color);
        }

        baseCol.a = _lastAlpha;

        if (mats != null && mats.Length > 0 && mats[0] != null && mats[0].HasProperty(ID_BaseColor))
            mpb.SetColor(ID_BaseColor, baseCol);
        else
            mpb.SetColor(ID_Color, baseCol);

        r.SetPropertyBlock(mpb);
    }
}

    void TryMakeTransparent(Material m)
{
    if (m == null) return;

    // --- Make Transparent (URP Lit & common fallbacks) ---
    if (m.HasProperty("_Surface")) // URP Lit
    {
        m.SetFloat("_Surface", 1f); // Transparent
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        if (m.HasProperty("_ZWrite"))   m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
    }
    else if (m.HasProperty("_Mode")) // Legacy Standard
    {
        m.SetFloat("_Mode", 2f); // Fade
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        if (m.HasProperty("_ZWrite"))   m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
    }

    // --- Force Double-Sided (no backface culling) ---
    // URP Lit & Shader Graph commonly expose _Cull (0=Off,1=Front,2=Back).
    // Some shaders use _CullMode. Set both if present, otherwise try standard _Cull int.
    if (m.HasProperty("_Cull"))      m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
    if (m.HasProperty("_CullMode"))  m.SetFloat("_CullMode", 0f); // Off
    m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);   // safe fallback
    m.EnableKeyword("_DOUBLE_SIDED_GI");                           // helps lighting on both sides
}
}