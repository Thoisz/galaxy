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

        // 1) Find ground
        Vector3 start = origin + up * rayStartUpOffset;
        Vector3 dir = -up;

        if (Physics.Raycast(start, dir, out RaycastHit hit, rayDownDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            // Place & orient at ground
            transform.position = hit.point;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);

            if (debugLogs)
            {
                Debug.DrawLine(start, hit.point, Color.green, 1.5f);
                Debug.Log($"[GroundbreakFXRoot] Hit '{hit.collider.name}' at {hit.point}", this);
            }

            // 2) Inherit material from ground
            if (inheritGroundMaterial)
            {
                var srcMat = TryGetGroundMaterial(hit);
                if (srcMat != null)
                {
                    ApplyMaterialToSlabs(srcMat);
                }
                else if (debugLogs)
                {
                    Debug.LogWarning("[GroundbreakFXRoot] Could not find ground material to inherit; keeping current.", this);
                }
            }
        }
        else
        {
            if (debugLogs)
            {
                Debug.DrawRay(start, dir * rayDownDistance, Color.red, 1.5f);
                Debug.LogWarning("[GroundbreakFXRoot] No ground hit — using current transform/material.", this);
            }
        }
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

    private void ApplyMaterialToSlabs(Material src)
    {
        if (src == null) return;

        // If we have the FX driver, let it make per-instance copies + set transparent flags so fading works.
        if (slabFx != null)
        {
            Material toAssign = src;

            if (forceTransparentForURP && IsURPLit(src))
            {
                toAssign = new Material(src) { name = src.name + " (FX Transparent)" };
                ForceURPLitTransparent(toAssign);
            }

            slabFx.ApplyMaterial(toAssign);
            if (debugLogs)
                Debug.Log($"[GroundbreakFXRoot] Applied via SlabRingSimpleFX: '{toAssign.name}'", this);
            return;
        }

        // Fallback: replace on the single renderer we know about
        if (slabRenderer == null) return;

        Material toAssignFallback = src;
        if (forceTransparentForURP && IsURPLit(src))
        {
            toAssignFallback = new Material(src) { name = src.name + " (FX Transparent)" };
            ForceURPLitTransparent(toAssignFallback);
        }

        var mats = slabRenderer.materials; // instances
        if (mats == null || mats.Length == 0) return;

        if (slabMaterialIndex < 0)
        {
            for (int i = 0; i < mats.Length; i++) mats[i] = toAssignFallback;
        }
        else if (slabMaterialIndex < mats.Length)
        {
            mats[slabMaterialIndex] = toAssignFallback;
        }
        slabRenderer.materials = mats;

        if (debugLogs)
            Debug.Log($"[GroundbreakFXRoot] Applied on fallback renderer: '{toAssignFallback.name}' (index {(slabMaterialIndex < 0 ? "ALL" : slabMaterialIndex.ToString())})", this);
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
}