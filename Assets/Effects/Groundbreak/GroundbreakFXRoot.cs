using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering; // for RenderQueue, BlendMode

[DisallowMultipleComponent]
public class GroundbreakFXRoot : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // Basic slab FX & inheritance
    // ─────────────────────────────────────────────────────────────
    [Header("Slab FX (auto-find if empty)")]
    [SerializeField] private MeshRenderer     slabRenderer; // main ring mesh
    [SerializeField] private SlabRingSimpleFX slabFx;       // driver that handles scaling/fading

    [Header("Extra Particles (optional)")]
    [SerializeField] private List<ParticleSystem> extraParticles = new List<ParticleSystem>();

    [Header("Material Inheritance")]
    [Tooltip("Copy the ground's material at the contact point onto the slabs.")]
    [SerializeField] private bool inheritGroundMaterial = true;

    [Tooltip("If the ground is URP/Lit, clone it and force transparent so fading works.")]
    [SerializeField] private bool forceTransparentForURP = true;

    [Tooltip("Which slab material slot to replace on the fallback renderer. -1 = ALL slots.")]
    [SerializeField] private int slabMaterialIndex = -1;

    // ─────────────────────────────────────────────────────────────
    // Spook planet (vertex-control map) support
    // ─────────────────────────────────────────────────────────────
    [System.Serializable]
    public class SpookSettings
    {
        [Tooltip("Enable the special Spook_VertexShading handling for slabs.")]
        public bool enableSpookStyle = true;

        [Tooltip("Assign the 'Shader Graphs/Spook_VertexShading' shader here.")]
        public Shader spookShader;

        [Tooltip("If true, only the selected texture slot stays at strength 1; the others are set to 0.")]
        public bool zeroOtherStrengths = true;
    }

    [Header("Spook Planet Style")]
    [SerializeField] private SpookSettings spook = new SpookSettings();

    // internal constants for Spook shader property names
    private const string SPOOK_SHADER_NAME_FALLBACK = "Shader Graphs/Spook_VertexShading";
    private const string SPOOK_CONTROL_PROP         = "_ControlMap";

    // Base textures & strengths are fixed in your graph, so we hardcode names here
    private static readonly string[] SPOOK_BASE_TEX_PROPS =
    {
        "_BaseTextA", "_BaseTextB", "_BaseTextC", "_BaseTextD", "_BaseTextE"
    };

    private static readonly string[] SPOOK_BASE_STRENGTH_PROPS =
    {
        "_BaseStrengthA", "_BaseStrengthB", "_BaseStrengthC", "_BaseStrengthD", "_BaseStrengthE"
    };

    // ─────────────────────────────────────────────────────────────
    // Raycast + rubble tint
    // ─────────────────────────────────────────────────────────────
    [Header("Raycast Sampling")]
    [Tooltip("Layers to consider as ground.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Tooltip("How far down to raycast to find ground from spawn point.")]
    [SerializeField] private float rayDownDistance = 6f;

    [Tooltip("Vertical offset upward before we cast down (helps when spawning very close to the floor).")]
    [SerializeField] private float rayStartUpOffset = 0.25f;

    [Tooltip("If ON, draw debug rays and log info.")]
    [SerializeField] private bool debugLogs = false;

    [Header("Rubble Tint (optional)")]
    [SerializeField] private List<ParticleSystem> rubbleParticles  = new List<ParticleSystem>();
    [SerializeField] private List<Renderer>       rubbleRenderers  = new List<Renderer>();
    [SerializeField] private bool                 rubbleAffectsMaterialColor = true;

    // cache
    private Material[] _defaultSlabMats;

    // small cached uniform control-map textures for regions
    private static Texture2D _controlRed;
    private static Texture2D _controlGreen;
    private static Texture2D _controlBlue;
    private static Texture2D _controlWhite;
    private static Texture2D _controlBlack;

    private enum Region { Red = 0, Green = 1, Blue = 2, White = 3, Black = 4 }

    // ─────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        AutoBindIfNeeded();
        if (slabRenderer != null)
            _defaultSlabMats = slabRenderer.sharedMaterials;
    }

    private void OnEnable()
    {
        if (slabFx != null) slabFx.Play();
        foreach (var ps in extraParticles) if (ps) ps.Play(true);
    }

    /// <summary>
    /// Call this right after Instantiate. 'origin' ~ player feet; 'up' is your gravity up.
    /// This also tries to inherit the hit material and tint rubble.
    /// </summary>
    public void ConfigureFromContact(Vector3 origin, Vector3 up)
    {
        AutoBindIfNeeded();

        Vector3 start = origin + up * rayStartUpOffset;
        Vector3 dir   = -up;

        if (Physics.Raycast(start, dir, out RaycastHit hit, rayDownDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            // Position/align to ground
            transform.position = hit.point;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);

            if (debugLogs)
            {
                Debug.DrawLine(start, hit.point, Color.green, 1.5f);
                Debug.Log($"[GroundbreakFXRoot] Hit '{hit.collider.name}' at {hit.point}", this);
            }

            // Inherit ground material (with Spook special case)
            if (inheritGroundMaterial && slabRenderer != null)
            {
                var srcMat = TryGetGroundMaterial(hit);
                if (srcMat != null)
                {
                    Material toApply = srcMat;

                    if (spook.enableSpookStyle && IsSpookShader(srcMat))
                    {
                        if (TryCreateSpookSlabMaterial(srcMat, hit, out Material spookMat))
                            toApply = spookMat;
                    }

                    ApplyMaterialToSlabs(toApply);
                }
                else if (debugLogs)
                {
                    Debug.LogWarning("[GroundbreakFXRoot] Could not find a ground material to inherit.", this);
                }
            }

            // Tint rubble from ground colour
            if (TrySampleGroundColor(out Color groundCol))
            {
                ApplyColorToRubble(groundCol);
                if (debugLogs) Debug.Log($"[GroundbreakFXRoot] Rubble color set to {groundCol}", this);
            }
        }
        else
        {
            if (debugLogs)
            {
                Debug.DrawRay(start, dir * rayDownDistance, Color.red, 1.5f);
                Debug.LogWarning("[GroundbreakFXRoot] No ground hit. Using current slab material/transform.", this);
            }
        }

        // Kick FX
        if (slabFx != null) slabFx.Play();
        foreach (var ps in extraParticles) if (ps) ps.Play(true);
    }

    // ─────────────────────────────────────────────────────────────
    // Core helpers
    // ─────────────────────────────────────────────────────────────
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

    // Generic path: assign or clone → slabs
    private void ApplyMaterialToSlabs(Material src)
    {
        if (slabRenderer == null || src == null) return;

        Material toAssign = src;

        if (forceTransparentForURP && IsURPLit(src))
        {
            toAssign = new Material(src) { name = src.name + " (FX Clone)" };
            ForceURPLitTransparent(toAssign);
        }

        // Preferred: route via SlabRingSimpleFX
        if (slabFx != null)
        {
            slabFx.ApplyMaterial(toAssign);
            if (debugLogs)
                Debug.Log($"[GroundbreakFXRoot] Routed ground material '{toAssign.name}' via SlabRingSimpleFX.ApplyMaterial().", this);
            return;
        }

        // Fallback: directly assign to renderer
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

        slabRenderer.SetPropertyBlock(null);
#if UNITY_2021_2_OR_NEWER
        for (int s = 1; s < slabRenderer.sharedMaterials.Length; s++)
            slabRenderer.SetPropertyBlock(null, s);
#endif

        if (debugLogs)
            Debug.Log($"[GroundbreakFXRoot] Applied '{toAssign.name}' directly to slabRenderer (fallback path).", this);
    }

    // ─────────────────────────────────────────────────────────────
    // Spook style support
    // ─────────────────────────────────────────────────────────────
    private bool IsSpookShader(Material m)
    {
        if (!m || m.shader == null) return false;

        // Prefer direct shader reference if provided
        if (spook.spookShader && m.shader == spook.spookShader)
            return true;

        // Fallback by name
        string shaderName = m.shader.name;
        if (!string.IsNullOrEmpty(shaderName) &&
            shaderName.IndexOf(SPOOK_SHADER_NAME_FALLBACK, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private bool TryCreateSpookSlabMaterial(Material src, RaycastHit hit, out Material slabMat)
    {
        slabMat = null;
        if (src == null || src.shader == null) return false;

        var controlTex = src.GetTexture(SPOOK_CONTROL_PROP) as Texture2D;
        if (controlTex == null || !controlTex.isReadable)
        {
            if (debugLogs)
                Debug.LogWarning($"[GroundbreakFXRoot] Spook material '{src.name}' has no readable {SPOOK_CONTROL_PROP}.", this);
            return false;
        }

        // Sample control map at hit UV
        Vector2 uv = hit.textureCoord;
        Color c = controlTex.GetPixelBilinear(uv.x, uv.y);

        Region region = GetNearestRegion(c);
        int regionIndex = (int)region;
        if (regionIndex < 0 || regionIndex >= SPOOK_BASE_TEX_PROPS.Length)
            return false;

        string texProp = SPOOK_BASE_TEX_PROPS[regionIndex];
        Texture baseTex = src.HasProperty(texProp) ? src.GetTexture(texProp) : null;
        if (baseTex == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[GroundbreakFXRoot] Spook material '{src.name}' has no texture assigned on '{texProp}'.", this);
            return false;
        }

        // Clone
        slabMat = new Material(src) { name = src.name + $" (Spook Slab {region})" };

        // Replace control map with uniform map for this channel
        slabMat.SetTexture(SPOOK_CONTROL_PROP, GetUniformControlTexture(region));

        // Optionally zero strengths for non-selected slots
        if (spook.zeroOtherStrengths)
        {
            for (int i = 0; i < SPOOK_BASE_STRENGTH_PROPS.Length; i++)
            {
                string prop = SPOOK_BASE_STRENGTH_PROPS[i];
                if (!slabMat.HasProperty(prop)) continue;
                slabMat.SetFloat(prop, i == regionIndex ? 1f : 0f);
            }
        }

        if (debugLogs)
        {
            Debug.Log($"[GroundbreakFXRoot] Created Spook slab material '{slabMat.name}' from '{src.name}', region={region}, texProp={texProp}.", this);
        }

        return true;
    }

    private Region GetNearestRegion(Color c)
    {
        Vector3 v = new Vector3(c.r, c.g, c.b);

        float best = float.MaxValue;
        Region bestRegion = Region.Red;

        void Consider(Region r, Color target)
        {
            Vector3 t = new Vector3(target.r, target.g, target.b);
            float d = (v - t).sqrMagnitude;
            if (d < best)
            {
                best = d;
                bestRegion = r;
            }
        }

        Consider(Region.Red,   Color.red);
        Consider(Region.Green, Color.green);
        Consider(Region.Blue,  Color.blue);
        Consider(Region.White, Color.white);
        Consider(Region.Black, Color.black);

        return bestRegion;
    }

    private Texture2D GetUniformControlTexture(Region region)
    {
        Color col = region switch
        {
            Region.Red   => Color.red,
            Region.Green => Color.green,
            Region.Blue  => Color.blue,
            Region.White => Color.white,
            Region.Black => Color.black,
            _ => Color.red
        };

        switch (region)
        {
            case Region.Red:
                if (_controlRed == null)   _controlRed   = CreateUniformControlTexture(col);
                return _controlRed;
            case Region.Green:
                if (_controlGreen == null) _controlGreen = CreateUniformControlTexture(col);
                return _controlGreen;
            case Region.Blue:
                if (_controlBlue == null)  _controlBlue  = CreateUniformControlTexture(col);
                return _controlBlue;
            case Region.White:
                if (_controlWhite == null) _controlWhite = CreateUniformControlTexture(col);
                return _controlWhite;
            case Region.Black:
                if (_controlBlack == null) _controlBlack = CreateUniformControlTexture(col);
                return _controlBlack;
        }

        if (_controlRed == null) _controlRed = CreateUniformControlTexture(Color.red);
        return _controlRed;
    }

    private static Texture2D CreateUniformControlTexture(Color c)
    {
        const int size = 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name      = $"SpookControl_{c}",
            wrapMode  = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // ─────────────────────────────────────────────────────────────
    // URP Lit helpers
    // ─────────────────────────────────────────────────────────────
    private static bool IsURPLit(Material m)
    {
        var sh = m.shader;
        if (!sh) return false;

        return sh.name.IndexOf("Universal Render Pipeline", System.StringComparison.OrdinalIgnoreCase) >= 0
            && sh.name.IndexOf("Lit", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void ForceURPLitTransparent(Material m)
    {
        if (m.HasProperty("_Surface"))   m.SetFloat("_Surface", 1f); // Transparent
        if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 0f);
        if (m.HasProperty("_ZWrite"))    m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_SrcBlend"))  m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend"))  m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
    }

    // ─────────────────────────────────────────────────────────────
    // Rubble tinting (same as before, just kept)
    // ─────────────────────────────────────────────────────────────
    private bool TrySampleGroundColor(out Color result)
    {
        result = Color.white;

        Vector3 up    = transform.up;
        Vector3 start = transform.position + up * 0.1f;
        Vector3 dir   = -up;

        if (!Physics.Raycast(start, dir, out RaycastHit hit, rayDownDistance, groundMask, QueryTriggerInteraction.Ignore))
            return false;

        // Terrain
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
                float w = alpha[0, 0, l];
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
                    Vector2 tileSize = layer.tileSize;
                    if (tileSize.x <= 0f) tileSize.x = 1f;
                    if (tileSize.y <= 0f) tileSize.y = 1f;

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

        // Mesh / static objects
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

    private void ApplyColorToRubble(Color c)
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

    // tiny utilities
    private static Color ReadAnyColorProperty(Material m, Color fallback)
    {
        if (!m) return fallback;
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
        if (m.HasProperty("_Color"))     return m.GetColor("_Color");
        if (m.HasProperty("_TintColor")) return m.GetColor("_TintColor");
        return fallback;
    }

    private static Texture GetAnyBaseTexture(Material m)
    {
        if (!m) return null;
        if (m.HasProperty("_BaseMap"))      return m.GetTexture("_BaseMap");
        if (m.HasProperty("_MainTex"))      return m.GetTexture("_MainTex");
        if (m.HasProperty("_BaseColorMap")) return m.GetTexture("_BaseColorMap");
        return null;
    }

    private static Color MultiplySRGB(Color a, Color b)
    {
        return new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
    }
}