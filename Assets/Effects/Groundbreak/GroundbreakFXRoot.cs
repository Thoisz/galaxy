using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering; // for RenderQueue, BlendMode

[DisallowMultipleComponent]
public class GroundbreakFXRoot : MonoBehaviour
{
    [Header("Optional: assign explicitly (otherwise auto-find)")]
    [SerializeField] private MeshRenderer      slabRenderer; // fallback single renderer on the slab ring
    [SerializeField] private SlabRingSimpleFX  slabFx;       // preferred: driver that can apply/fade material

    [Header("Extra Particles (optional)")]
    [SerializeField] private List<ParticleSystem> extraParticles = new List<ParticleSystem>();

    [Header("Material Inheritance")]
    [Tooltip("Try to copy the ground's material at the contact point.")]
    [SerializeField] private bool inheritGroundMaterial = true;

    [Tooltip("If ON and ground uses URP/Lit, we’ll clone it and force Transparent so the slab fade works.")]
    [SerializeField] private bool forceTransparentForURP = true;

    [Tooltip("Which slab material slot to replace on the fallback renderer. -1 = ALL slots.")]
    [SerializeField] private int slabMaterialIndex = -1;

    [Header("Raycast Sampling")]
    [Tooltip("Layers to consider as ground.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Tooltip("How far down to raycast to find ground from spawn point.")]
    [SerializeField] private float rayDownDistance = 6f;

    [Tooltip("Vertical offset upward before we cast down (helps when spawning very close to the floor).")]
    [SerializeField] private float rayStartUpOffset = 0.25f;

    [Tooltip("If ON, draw a debug ray and log what we hit.")]
    [SerializeField] private bool debugLogs = false;

    [Header("Rubble Color (optional)")]
    [SerializeField] private List<ParticleSystem> rubbleParticles = new List<ParticleSystem>();
    [SerializeField] private List<Renderer>       rubbleRenderers = new List<Renderer>();
    [SerializeField] private bool                 rubbleAffectsMaterialColor = true; // sets _BaseColor/_Color/_TintColor on particle materials too

    // cache
    private Material[] _defaultSlabMats;

    private void Awake()
    {
        AutoBindIfNeeded();
        if (slabRenderer != null)
            _defaultSlabMats = slabRenderer.sharedMaterials;
    }

    private void OnEnable()
    {
        // Drive FX play
        if (slabFx != null) slabFx.Play();
        foreach (var ps in extraParticles) if (ps) ps.Play(true);
    }

    /// <summary>
    /// Call this right after Instantiate. 'origin' ~ player feet; 'up' is your gravity up.
    /// This also tries to inherit the hit material.
    /// </summary>
    public void ConfigureFromContact(Vector3 origin, Vector3 up)
    {
        AutoBindIfNeeded();

        // 1) Find ground directly beneath the given origin
        Vector3 start = origin + up * rayStartUpOffset;
        Vector3 dir   = -up;

        if (Physics.Raycast(start, dir, out RaycastHit hit, rayDownDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            // Place the FX on the contact point and align its up to the ground normal
            transform.position = hit.point;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);

            if (debugLogs)
            {
                Debug.DrawLine(start, hit.point, Color.green, 1.5f);
                Debug.Log($"[GroundbreakFXRoot] Hit '{hit.collider.name}' at {hit.point}", this);
            }

            // 2) Inherit the ground's material for the slab ring (optional)
            if (inheritGroundMaterial && slabRenderer != null)
            {
                var srcMat = TryGetGroundMaterial(hit);
                if (srcMat != null)
                {
                    ApplyMaterialToSlabs(srcMat);
                }
                else if (debugLogs)
                {
                    Debug.LogWarning("[GroundbreakFXRoot] Could not find a ground material to inherit.", this);
                }
            }

            // 3) Sample a representative color from the ground and tint rubble with it (optional)
            Color groundCol;
            if (TrySampleGroundColor(out groundCol))
            {
                ApplyColorToRubble(groundCol);
                if (debugLogs) Debug.Log($"[GroundbreakFXRoot] Rubble color set to {groundCol}", this);
            }
        }
        else
        {
            // No ground below; keep current transform but log/debug draw
            if (debugLogs)
            {
                Debug.DrawRay(start, dir * rayDownDistance, Color.red, 1.5f);
                Debug.LogWarning("[GroundbreakFXRoot] No ground hit. Using current slab material/transform.", this);
            }
        }

        // 4) Kick off the visual effects
        if (slabFx != null) slabFx.Play();
        foreach (var ps in extraParticles)
            if (ps) ps.Play(true);
    }

    // ─────────────────────────── helpers ───────────────────────────

    private void AutoBindIfNeeded()
    {
        if (!slabFx)       slabFx       = GetComponentInChildren<SlabRingSimpleFX>(true);
        if (!slabRenderer) slabRenderer = GetComponentInChildren<MeshRenderer>(true);
    }

    private Material TryGetGroundMaterial(in RaycastHit hit)
    {
        // Prefer a Renderer up the hierarchy
        var rend = hit.collider.GetComponentInParent<Renderer>();
        if (rend != null)
        {
            var shared = rend.sharedMaterials;
            for (int i = 0; shared != null && i < shared.Length; i++)
            {
                if (shared[i] != null)
                {
                    if (debugLogs)
                        Debug.Log($"[GroundbreakFXRoot] Using Renderer '{rend.name}' material slot {i}: '{shared[i].name}'", this);
                    return shared[i];
                }
            }
        }

        // Terrain special case
        var terrain = hit.collider.GetComponentInParent<Terrain>();
        if (terrain != null && terrain.materialTemplate != null)
        {
            if (debugLogs)
                Debug.Log($"[GroundbreakFXRoot] Using Terrain material '{terrain.materialTemplate.name}'", this);
            return terrain.materialTemplate;
        }

        return null;
    }

    void ApplyMaterialToSlabs(Material src)
    {
        if (slabRenderer == null || src == null) return;

        // Clone if we need a transparent URP Lit for fading.
        Material toAssign = src;
        bool isURPLit = src.shader != null && src.shader.name.Contains("Universal Render Pipeline/Lit");
        if (forceTransparentForURP && isURPLit)
        {
            toAssign = new Material(src) { name = src.name + " (FX Clone)" };
            ForceURPLitTransparent(toAssign);
        }

        // Preferred path: let the FX script handle instance + transparency + MPB refresh
        if (slabFx != null)
        {
            slabFx.ApplyMaterial(toAssign);
            if (debugLogs)
                Debug.Log($"[GroundbreakFXRoot] Routed ground material '{toAssign.name}' via SlabRingSimpleFX.ApplyMaterial().", this);
            return;
        }

        // Fallback: assign directly on the renderer…
        var mats = slabRenderer.materials; // instances
        if (mats == null || mats.Length == 0) return;

        if (slabMaterialIndex < 0)
        {
            for (int i = 0; i < mats.Length; i++) mats[i] = toAssign;
        }
        else if (slabMaterialIndex < mats.Length)
        {
            mats[slabMaterialIndex] = toAssign;
        }
        slabRenderer.materials = mats;

        // …and clear ALL property blocks so old _BaseColor MPB tints don’t override the new material
        slabRenderer.SetPropertyBlock(null);
#if UNITY_2021_2_OR_NEWER
        for (int s = 1; s < slabRenderer.sharedMaterials.Length; s++)
            slabRenderer.SetPropertyBlock(null, s);
#endif

        if (debugLogs)
            Debug.Log($"[GroundbreakFXRoot] Applied '{toAssign.name}' directly to slabRenderer (fallback path).", this);
    }

    private static bool IsURPLit(Material m)
    {
        var sh = m.shader;
        if (!sh) return false;
        // Covers “Universal Render Pipeline/Lit” and variants
        return sh.name.IndexOf("Universal Render Pipeline", System.StringComparison.OrdinalIgnoreCase) >= 0
            && sh.name.IndexOf("Lit", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Make a URP/Lit material transparent so alpha fading works
    private static void ForceURPLitTransparent(Material m)
    {
        if (m.HasProperty("_Surface"))   m.SetFloat("_Surface", 1f); // 1 = Transparent
        if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 0f);
        if (m.HasProperty("_ZWrite"))    m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_SrcBlend"))  m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend"))  m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
    }

    // Try to get a representative color from the ground under this FX root.
    // Prefers material color; if there’s a base/diffuse texture and UVs, samples it too.
    bool TrySampleGroundColor(out Color result)
    {
        result = Color.white;

        // Cast straight down from our FX root to re-acquire a fresh hit (in case ConfigureFromContact moved us)
        Vector3 up = transform.up;
        Vector3 start = transform.position + up * 0.1f;
        Vector3 dir = -up;

        if (!Physics.Raycast(start, dir, out RaycastHit hit, rayDownDistance, groundMask, QueryTriggerInteraction.Ignore))
            return false;

        // 1) Terrain
        var terrain = hit.collider.GetComponentInParent<Terrain>();
        if (terrain != null && terrain.terrainData != null)
        {
            var td = terrain.terrainData;
            Vector3 local = hit.point - terrain.transform.position;
            float u = Mathf.InverseLerp(0f, td.size.x, local.x);
            float v = Mathf.InverseLerp(0f, td.size.z, local.z);

            int sx = Mathf.Clamp(Mathf.RoundToInt(u * (td.alphamapWidth  - 1)), 0, td.alphamapWidth  - 1);
            int sy = Mathf.Clamp(Mathf.RoundToInt(v * (td.alphamapHeight - 1)), 0, td.alphamapHeight - 1);
            float[,,] alpha = td.GetAlphamaps(sx, sy, 1, 1);

            int best = 0; float bestW = 0f;
            for (int l = 0; l < td.alphamapLayers; l++)
            {
                float w = alpha[0,0,l];
                if (w > bestW) { bestW = w; best = l; }
            }

            var layers = td.terrainLayers;
            if (layers != null && best >= 0 && best < layers.Length)
            {
                var layer = layers[best];
                Color baseTint = Color.white;

                var tmat = terrain.materialTemplate;
                if (tmat != null)
                    baseTint = ReadAnyColorProperty(tmat, baseTint);

                Color texCol = Color.white;
                if (layer.diffuseTexture != null)
                {
                    Vector2 tileSize = layer.tileSize; if (tileSize.x <= 0f) tileSize.x = 1f; if (tileSize.y <= 0f) tileSize.y = 1f;
                    Vector2 uvLayer = new Vector2(local.x / tileSize.x, local.z / tileSize.y) + layer.tileOffset;

                    var tex = layer.diffuseTexture as Texture2D;
                    if (tex != null && tex.isReadable)
                        texCol = tex.GetPixelBilinear(Mathf.Repeat(uvLayer.x, 1f), Mathf.Repeat(uvLayer.y, 1f));
                }

                result = MultiplySRGB(baseTint, texCol);
                return true;
            }

            if (terrain.materialTemplate != null)
            {
                result = ReadAnyColorProperty(terrain.materialTemplate, Color.white);
                return true;
            }

            return false;
        }

        // 2) Mesh / Static objects
        var rend = hit.collider.GetComponentInParent<Renderer>();
        if (rend != null)
        {
            var mat = rend.sharedMaterial;
            if (mat != null)
            {
                Color matCol = ReadAnyColorProperty(mat, Color.white);

                Color texCol = Color.white;
                var tex = GetAnyBaseTexture(mat);
                if (tex != null && hit.textureCoord != Vector2.zero)
                {
                    var t2 = tex as Texture2D;
                    if (t2 != null && t2.isReadable)
                    {
                        texCol = t2.GetPixelBilinear(hit.textureCoord.x, hit.textureCoord.y);
                    }
                }

                result = MultiplySRGB(matCol, texCol);
                return true;
            }
        }

        return false;
    }

    void ApplyColorToRubble(Color c)
    {
        foreach (var ps in rubbleParticles)
        {
            if (!ps) continue;
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(c);
        }

        if (!rubbleAffectsMaterialColor) return;

        foreach (var r in rubbleRenderers)
        {
            if (!r) continue;
            var mats = r.materials; // per-instance
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m) continue;

                bool set = false;
                if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", c); set = true; }
                if (!set && m.HasProperty("_Color")) { m.SetColor("_Color", c); set = true; }
                if (!set && m.HasProperty("_TintColor")) { m.SetColor("_TintColor", c); set = true; }
            }
            r.materials = mats;
        }
    }

    // --- tiny utilities ---

    static Color ReadAnyColorProperty(Material m, Color fallback)
    {
        if (!m) return fallback;
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
        if (m.HasProperty("_Color"))     return m.GetColor("_Color");
        if (m.HasProperty("_TintColor")) return m.GetColor("_TintColor");
        return fallback;
    }

    static Texture GetAnyBaseTexture(Material m)
    {
        if (!m) return null;
        if (m.HasProperty("_BaseMap"))      return m.GetTexture("_BaseMap");
        if (m.HasProperty("_MainTex"))      return m.GetTexture("_MainTex");
        if (m.HasProperty("_BaseColorMap")) return m.GetTexture("_BaseColorMap");
        return null;
    }

    static Color MultiplySRGB(Color a, Color b)
    {
        return new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
    }
}