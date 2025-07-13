using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkyboxTransparency : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the object that contains the collider we're checking against.")]
    public Transform referenceObject;
    
    [Tooltip("Reference to the player's transform. If left empty, script will try to find the player automatically.")]
    public Transform playerTransform;

    [Header("Outside Collider Distance Settings")]
    [Tooltip("The distance from the collider's edge at which the material begins to fade from transparent to opaque (outside).")]
    public float fadeStartDistanceOUT = 10f;
    
    [Tooltip("The distance from the collider's edge at which the material becomes fully opaque (outside).")]
    public float fadeEndDistanceOUT = 50f;

    [Header("Inside Collider Distance Settings")]
    [Tooltip("The distance from the collider's edge at which the material begins to fade from transparent to opaque (inside).")]
    public float fadeStartDistanceIN = 5f;
    
    [Tooltip("The distance from the collider's edge at which the material becomes fully opaque (inside).")]
    public float fadeEndDistanceIN = 25f;

    [Header("Transparency Settings")]
    [Tooltip("The alpha value at the edge of the reference object (0.0 = fully transparent).")]
    [Range(0.0f, 1.0f)]
    public float edgeAlpha = 0.0f;
    
    [Tooltip("The maximum alpha value (maximum opacity) when player is inside or far from the object.")]
    [Range(0.0f, 1.0f)]
    public float maxAlpha = 1.0f;
    
    [Tooltip("How the transparency changes with distance (0 = linear, 1 = exponential).")]
    [Range(0.0f, 1.0f)]
    public float transparencyCurve = 0.0f;

    [Header("Hybrid Rendering Settings")]
    [Tooltip("When true, switches between opaque and transparent rendering modes for better performance.")]
    public bool useHybridRendering = true;
    
    [Tooltip("Alpha threshold above which to switch to opaque mode (usually 0.99 or higher).")]
    [Range(0.9f, 1.0f)]
    public float opaqueThreshold = 0.99f;

    [Header("Performance Settings")]
    [Tooltip("How quickly the transparency changes when distance changes (higher = faster).")]
    public float fadeSpeed = 3f;
    
    [Tooltip("How often to check distance/update transparency (in seconds, 0 = every frame).")]
    public float updateFrequency = 0.0f;

    [Header("Debug Settings")]
    [Tooltip("Enable to show debug information in the console and scene view.")]
    public bool debug = false;

    [Header("Material Settings")]
    [Tooltip("Which materials should be affected if the object has multiple materials. Leave empty to affect all.")]
    public int[] materialIndices;

    // Private variables
    private Renderer objectRenderer;
    private Material[] materials;
    private Color[] originalColors;
    private float currentAlpha;
    private float updateTimer = 0.0f;
    private bool initialized = false;
    private Collider referenceCollider;
    private BoxCollider boxCollider;
    private bool[] materialIsCurrentlyTransparent;
    
    // Original material states
    private int[] originalRenderQueue;
    private int[] originalSrcBlend;
    private int[] originalDstBlend;
    private int[] originalZWrite;
    private Color[] fullyOpaqueColors;
    
    // Debug variables
    private float debugDistanceOut = 0f;
    private float debugDistanceIn = 0f;

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
            Debug.LogError("SkyboxTransparency: No Renderer component found on this GameObject!");
            enabled = false;
            return;
        }

        // Check reference object
        if (referenceObject == null)
        {
            Debug.LogError("SkyboxTransparency: No reference object assigned!");
            enabled = false;
            return;
        }

        // Get collider from reference object
        referenceCollider = referenceObject.GetComponent<Collider>();
        if (referenceCollider == null)
        {
            Debug.LogError("SkyboxTransparency: No Collider found on reference object!");
            enabled = false;
            return;
        }

        // Check if it's a box collider
        boxCollider = referenceCollider as BoxCollider;

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
                Debug.LogWarning("SkyboxTransparency: No player transform assigned and cannot find GameObject with tag 'Player'!");
            }
        }

        // Cache materials and their original properties
        materials = objectRenderer.materials;
        originalColors = new Color[materials.Length];
        fullyOpaqueColors = new Color[materials.Length];
        originalRenderQueue = new int[materials.Length];
        originalSrcBlend = new int[materials.Length];
        originalDstBlend = new int[materials.Length];
        originalZWrite = new int[materials.Length];
        materialIsCurrentlyTransparent = new bool[materials.Length];
        
        for (int i = 0; i < materials.Length; i++)
        {
            // Store original properties
            originalColors[i] = materials[i].color;
            // Create a copy for fully opaque colors
            fullyOpaqueColors[i] = new Color(
                originalColors[i].r, 
                originalColors[i].g, 
                originalColors[i].b, 
                1.0f
            );
            
            // Store original render properties
            originalRenderQueue[i] = materials[i].renderQueue;
            originalSrcBlend[i] = materials[i].GetInt("_SrcBlend");
            originalDstBlend[i] = materials[i].GetInt("_DstBlend");
            originalZWrite[i] = materials[i].GetInt("_ZWrite");
            
            // Initialize as transparent if we're using hybrid rendering
            if (ShouldAffectMaterial(i) && useHybridRendering)
            {
                SetMaterialRenderMode(materials[i], true);
                materialIsCurrentlyTransparent[i] = true;
            }
        }

        // Initialize alpha
        currentAlpha = edgeAlpha;
        UpdateMaterialsAlpha(currentAlpha);
        
        initialized = true;
    }

    void Update()
    {
        if (!initialized || playerTransform == null || referenceCollider == null)
            return;

        // Handle update frequency
        if (updateFrequency > 0)
        {
            updateTimer += Time.deltaTime;
            if (updateTimer < updateFrequency)
                return;
            
            updateTimer = 0;
        }

        // Calculate target alpha
        float targetAlpha = CalculateTargetAlpha();
        
        // Smoothly interpolate to target alpha
        if (fadeSpeed > 0 && !Mathf.Approximately(currentAlpha, targetAlpha))
        {
            currentAlpha = Mathf.Lerp(currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);
            
            // If we're very close to the target, snap to it
            if (Mathf.Abs(currentAlpha - targetAlpha) < 0.01f)
            {
                currentAlpha = targetAlpha;
            }
            
            UpdateMaterialsAlpha(currentAlpha);
        }
        else if (fadeSpeed <= 0 || Mathf.Abs(currentAlpha - targetAlpha) > 0.5f)
        {
            // Immediate update if fadeSpeed is 0 or if change is very large
            currentAlpha = targetAlpha;
            UpdateMaterialsAlpha(currentAlpha);
        }
    }

    float CalculateTargetAlpha()
    {
        // Check if player is inside the collider
        bool isInside = referenceCollider.bounds.Contains(playerTransform.position);
        
        if (isInside)
        {
            // Calculate the closest distance to any face from inside
            float distanceFromEdge = CalculateDistanceFromInsideEdge();
            debugDistanceIn = distanceFromEdge;
            
            // Debug visualization
            if (debug)
            {
                Debug.DrawRay(playerTransform.position, Vector3.up * distanceFromEdge, Color.green);
                Debug.Log($"Inside - Distance from edge: {distanceFromEdge}, fadeStart: {fadeStartDistanceIN}, fadeEnd: {fadeEndDistanceIN}");
            }
            
            // Apply inside distance settings
            if (distanceFromEdge <= fadeStartDistanceIN)
            {
                // Close to the edge - transparent
                return edgeAlpha;
            }
            else if (distanceFromEdge >= fadeEndDistanceIN)
            {
                // Far from the edge - opaque
                return maxAlpha;
            }
            else
            {
                // In the fading zone
                float t = (distanceFromEdge - fadeStartDistanceIN) / (fadeEndDistanceIN - fadeStartDistanceIN);
                
                // Apply curve
                if (transparencyCurve > 0)
                {
                    t = Mathf.Pow(t, 1 + (transparencyCurve * 3));
                }
                
                return Mathf.Lerp(edgeAlpha, maxAlpha, t);
            }
        }
        else
        {
            // Outside - calculate distance to the closest point on collider
            Vector3 closestPoint = referenceCollider.ClosestPoint(playerTransform.position);
            float distanceToEdge = Vector3.Distance(playerTransform.position, closestPoint);
            debugDistanceOut = distanceToEdge;
            
            // Debug visualization
            if (debug)
            {
                Debug.DrawLine(playerTransform.position, closestPoint, Color.yellow);
                Debug.Log($"Outside - Distance to edge: {distanceToEdge}, fadeStart: {fadeStartDistanceOUT}, fadeEnd: {fadeEndDistanceOUT}");
            }
            
            // Apply outside distance settings
            if (distanceToEdge <= fadeStartDistanceOUT)
            {
                // Close to the edge - transparent
                return edgeAlpha;
            }
            else if (distanceToEdge >= fadeEndDistanceOUT)
            {
                // Far from the edge - opaque
                return maxAlpha;
            }
            else
            {
                // In the fading zone
                float t = (distanceToEdge - fadeStartDistanceOUT) / (fadeEndDistanceOUT - fadeStartDistanceOUT);
                
                // Apply curve
                if (transparencyCurve > 0)
                {
                    t = Mathf.Pow(t, 1 + (transparencyCurve * 3));
                }
                
                return Mathf.Lerp(edgeAlpha, maxAlpha, t);
            }
        }
    }

    float CalculateDistanceFromInsideEdge()
    {
        if (boxCollider != null)
        {
            return CalculateDistanceInsideBoxCollider();
        }
        else
        {
            return CalculateDistanceInsideGenericCollider();
        }
    }

    float CalculateDistanceInsideBoxCollider()
    {
        // Transform player position to local space of the box collider
        Vector3 localPoint = boxCollider.transform.InverseTransformPoint(playerTransform.position) - boxCollider.center;
        Vector3 halfSize = boxCollider.size * 0.5f;
        
        // Calculate distance to each face in local space
        float[] distances = new float[6];
        
        // X-axis faces
        distances[0] = halfSize.x - Mathf.Abs(localPoint.x); // Distance to closer X face
        
        // Y-axis faces
        distances[1] = halfSize.y - Mathf.Abs(localPoint.y); // Distance to closer Y face
        
        // Z-axis faces
        distances[2] = halfSize.z - Mathf.Abs(localPoint.z); // Distance to closer Z face
        
        // Find minimum distance
        float minDistance = Mathf.Min(distances[0], distances[1], distances[2]);
        
        // Convert back to world space by multiplying by the appropriate scale
        float worldScale = 1.0f;
        
        if (minDistance == distances[0])
        {
            worldScale = Mathf.Abs(boxCollider.transform.lossyScale.x);
        }
        else if (minDistance == distances[1])
        {
            worldScale = Mathf.Abs(boxCollider.transform.lossyScale.y);
        }
        else
        {
            worldScale = Mathf.Abs(boxCollider.transform.lossyScale.z);
        }
        
        return minDistance * worldScale;
    }

    float CalculateDistanceInsideGenericCollider()
    {
        // Use raycasting in 6 directions to find distance to collider surface
        Vector3[] directions = new Vector3[]
        {
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down,
            Vector3.forward,
            Vector3.back
        };
        
        float minDistance = float.MaxValue;
        RaycastHit hit;
        
        foreach (Vector3 dir in directions)
        {
            if (Physics.Raycast(playerTransform.position, dir, out hit))
            {
                if (hit.collider == referenceCollider)
                {
                    minDistance = Mathf.Min(minDistance, hit.distance);
                }
            }
        }
        
        return minDistance == float.MaxValue ? 0f : minDistance;
    }

    void UpdateMaterialsAlpha(float alpha)
    {
        for (int i = 0; i < materials.Length; i++)
        {
            if (ShouldAffectMaterial(i))
            {
                // With hybrid rendering, we switch between opaque and transparent modes
                if (useHybridRendering)
                {
                    bool shouldBeTransparent = alpha < opaqueThreshold;
                    
                    // Switch render mode if needed
                    if (materialIsCurrentlyTransparent[i] != shouldBeTransparent)
                    {
                        SetMaterialRenderMode(materials[i], shouldBeTransparent);
                        materialIsCurrentlyTransparent[i] = shouldBeTransparent;
                    }
                    
                    // Update color based on render mode
                    if (shouldBeTransparent)
                    {
                        // Transparent mode - use alpha
                        Color newColor = originalColors[i];
                        newColor.a = alpha;
                        materials[i].color = newColor;
                    }
                    else
                    {
                        // Opaque mode - use fully opaque color
                        materials[i].color = fullyOpaqueColors[i];
                    }
                }
                else
                {
                    // Standard transparent-only mode
                    Color newColor = originalColors[i];
                    newColor.a = alpha;
                    materials[i].color = newColor;
                }
            }
        }
    }

    void SetMaterialRenderMode(Material material, bool transparent)
    {
        if (transparent)
        {
            // Switch to transparent mode
            material.SetFloat("_Mode", 3); // Transparent mode
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }
        else
        {
            // Switch to opaque mode
            material.SetFloat("_Mode", 0); // Opaque mode
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = -1; // Default
        }
    }

    bool ShouldAffectMaterial(int index)
    {
        if (materialIndices == null || materialIndices.Length == 0)
            return true;
            
        for (int i = 0; i < materialIndices.Length; i++)
        {
            if (materialIndices[i] == index)
                return true;
        }
        
        return false;
    }

    void OnDisable()
    {
        if (initialized && materials != null)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                if (ShouldAffectMaterial(i) && i < originalColors.Length)
                {
                    // Restore original material settings
                    materials[i].color = originalColors[i];
                    materials[i].renderQueue = originalRenderQueue[i];
                    materials[i].SetInt("_SrcBlend", originalSrcBlend[i]);
                    materials[i].SetInt("_DstBlend", originalDstBlend[i]);
                    materials[i].SetInt("_ZWrite", originalZWrite[i]);
                }
            }
        }
    }
    
    void OnDrawGizmos()
    {
        if (!debug || !Application.isPlaying || playerTransform == null || referenceCollider == null)
            return;
            
        // Draw a sphere at player position
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(playerTransform.position, 0.5f);
        
        // Draw inside distance visualization
        if (referenceCollider.bounds.Contains(playerTransform.position) && debugDistanceIn > 0)
        {
            Gizmos.color = new Color(0, 1, 0, 0.3f);
            Gizmos.DrawSphere(playerTransform.position, debugDistanceIn);
        }
        
        // Draw fade distance visualizations
        if (boxCollider != null)
        {
            // Draw inside fade zones
            Matrix4x4 originalMatrix = Gizmos.matrix;
            Gizmos.matrix = boxCollider.transform.localToWorldMatrix;
            
            Vector3 center = boxCollider.center;
            Vector3 size = boxCollider.size;
            
            // Draw the inner fade zone
            if (fadeStartDistanceIN > 0)
            {
                Gizmos.color = new Color(1, 0, 0, 0.2f);
                Gizmos.DrawWireCube(center, size - new Vector3(fadeStartDistanceIN * 2, fadeStartDistanceIN * 2, fadeStartDistanceIN * 2));
            }
            
            // Draw the outer fade zone
            if (fadeEndDistanceIN > fadeStartDistanceIN)
            {
                Gizmos.color = new Color(0, 0, 1, 0.2f);
                Gizmos.DrawWireCube(center, size - new Vector3(fadeEndDistanceIN * 2, fadeEndDistanceIN * 2, fadeEndDistanceIN * 2));
            }
            
            Gizmos.matrix = originalMatrix;
        }
    }
}