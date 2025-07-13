using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProximityOpacity : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the player's transform. If left empty, script will try to find the player automatically.")]
    public Transform playerTransform;

    [Header("Distance Settings")]
    [Tooltip("The distance at which the object becomes fully opaque (maximum alpha).")]
    public float minDistance = 400f;
    
    [Tooltip("The distance at which the object is fully transparent (minimum alpha).")]
    public float maxDistance = 800f;

    [Header("Opacity Settings")]
    [Tooltip("The maximum alpha value (minimum transparency) when player is closest or inside.")]
    [Range(0.0f, 1.0f)]
    public float maxAlpha = 1.0f;
    
    [Tooltip("The minimum alpha value (maximum transparency) when player is furthest.")]
    [Range(0.0f, 1.0f)]
    public float minAlpha = 0.0f;
    
    [Tooltip("How the opacity changes with distance (0 = linear, 1 = exponential).")]
    [Range(0.0f, 1.0f)]
    public float opacityCurve = 0.0f;

    [Header("Performance Settings")]
    [Tooltip("How quickly the opacity changes when distance changes (higher = faster).")]
    public float fadeSpeed = 1f;
    
    [Tooltip("How often to check distance/update opacity (in seconds, 0 = every frame).")]
    public float updateFrequency = 0.0f;

    [Header("Material Settings")]
    [Tooltip("Which materials should be affected if the object has multiple materials. Leave empty to affect all.")]
    public int[] materialIndices;
    
    [Header("Collider Settings")]
    [Tooltip("Set to true to make object fully opaque when player is inside its collider.")]
    public bool opaqueWhenInside = true;

    // Private variables
    private Renderer objectRenderer;
    private Material[] materials;
    private Color[] originalColors;
    private float currentAlpha;
    private float targetAlpha;
    private float updateTimer = 0.0f;
    private bool initialized = false;
    private Collider objectCollider;
    private bool playerWasInside = false;

    // Start is called before the first frame update
    void Start()
    {
        Initialize();
    }

    void Initialize()
    {
        // Get renderer component
        objectRenderer = GetComponent<Renderer>();
        if (objectRenderer == null)
        {
            Debug.LogError("ProximityOpacity: No Renderer component found on this GameObject!");
            enabled = false;
            return;
        }

        // Get collider component
        objectCollider = GetComponent<Collider>();
        if (objectCollider == null && opaqueWhenInside)
        {
            Debug.LogWarning("ProximityOpacity: opaqueWhenInside is enabled but no Collider found!");
        }

        // Find player if not assigned
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
            }
            else
            {
                Debug.LogWarning("ProximityOpacity: No player transform assigned and cannot find GameObject with tag 'Player'!");
            }
        }

        // Cache materials and their original colors
        materials = objectRenderer.materials;
        originalColors = new Color[materials.Length];
        
        for (int i = 0; i < materials.Length; i++)
        {
            originalColors[i] = materials[i].color;
            
            // Enable transparency on all materials
            if (ShouldAffectMaterial(i))
            {
                SetupMaterialForTransparency(materials[i]);
            }
        }

        // Initialize alpha to min (fully transparent) or current setting
        currentAlpha = minAlpha;
        targetAlpha = minAlpha;
        UpdateMaterialsAlpha(currentAlpha);
        
        initialized = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (!initialized || playerTransform == null)
            return;

        // Handle update frequency
        if (updateFrequency > 0)
        {
            updateTimer += Time.deltaTime;
            if (updateTimer < updateFrequency)
                return;
            
            updateTimer = 0;
        }

        // Check if player is inside the collider
        bool playerIsInside = false;
        if (opaqueWhenInside && objectCollider != null)
        {
            playerIsInside = objectCollider.bounds.Contains(playerTransform.position);
            
            // Force update on state change (entering or exiting) regardless of updateFrequency
            if (playerIsInside != playerWasInside)
            {
                updateTimer = 0;
                playerWasInside = playerIsInside;
            }
        }

        // If player is inside, set to maximum alpha (maximum opacity)
        if (playerIsInside)
        {
            targetAlpha = maxAlpha;
        }
        else
        {
            // Calculate distance to player
            float distance = Vector3.Distance(transform.position, playerTransform.position);
            
            // Calculate target alpha based on distance
            if (distance <= minDistance)
            {
                targetAlpha = maxAlpha;
            }
            else if (distance >= maxDistance)
            {
                targetAlpha = minAlpha;
            }
            else
            {
                // Normalize distance between min and max
                float t = (distance - minDistance) / (maxDistance - minDistance);
                
                // Apply curve if specified
                if (opacityCurve > 0)
                {
                    t = Mathf.Pow(t, 1 + (opacityCurve * 3)); // Exponential curve
                }
                
                // Calculate alpha (inverse of original - closer = more opaque)
                targetAlpha = Mathf.Lerp(maxAlpha, minAlpha, t);
            }
        }
        
        // Smoothly interpolate to target alpha
        if (fadeSpeed > 0 && currentAlpha != targetAlpha)
        {
            currentAlpha = Mathf.Lerp(currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);
            
            // If we're very close to the target, snap to it
            if (Mathf.Abs(currentAlpha - targetAlpha) < 0.01f)
            {
                currentAlpha = targetAlpha;
            }
            
            UpdateMaterialsAlpha(currentAlpha);
        }
        else if (fadeSpeed <= 0)
        {
            // Immediate update
            currentAlpha = targetAlpha;
            UpdateMaterialsAlpha(currentAlpha);
        }
    }

    void UpdateMaterialsAlpha(float alpha)
    {
        for (int i = 0; i < materials.Length; i++)
        {
            if (ShouldAffectMaterial(i))
            {
                Color newColor = originalColors[i];
                newColor.a = alpha;
                materials[i].color = newColor;
            }
        }
    }

    bool ShouldAffectMaterial(int index)
    {
        // If no specific indices provided, affect all materials
        if (materialIndices == null || materialIndices.Length == 0)
            return true;
            
        // Check if this index is in the list
        for (int i = 0; i < materialIndices.Length; i++)
        {
            if (materialIndices[i] == index)
                return true;
        }
        
        return false;
    }

    void SetupMaterialForTransparency(Material material)
    {
        // Check the current render mode
        if (material.GetFloat("_Mode") != 3) // 3 is Transparent mode
        {
            // Enable transparency on the material
            material.SetFloat("_Mode", 3); // Set to Transparent mode
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }
    }

    // Reset to original state when disabled
    void OnDisable()
    {
        if (initialized && materials != null)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                if (ShouldAffectMaterial(i) && i < originalColors.Length)
                {
                    materials[i].color = originalColors[i];
                }
            }
        }
    }
}