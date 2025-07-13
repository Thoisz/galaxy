using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class AtmosphereTransparency : MonoBehaviour
{
    [Header("Distance Settings (Inside)")]
    [Tooltip("Distance from box edge inward where atmosphere becomes fully opaque")]
    public float opaqueDistanceIN = 10f;
    
    [Tooltip("Distance from box edge inward where atmosphere becomes fully transparent")]
    public float transparentDistanceIN = 50f;
    
    [Header("Distance Settings (Outside)")]
    [Tooltip("Distance from box edge outward where atmosphere becomes fully opaque")]
    public float opaqueDistanceOUT = 5f;
    
    [Tooltip("Distance from box edge outward where atmosphere becomes fully transparent")]
    public float transparentDistanceOUT = 100f;
    
    [Header("References")]
    public Transform player;
    private Renderer atmosphereRenderer;
    private BoxCollider boxCollider;
    private Material atmosphereMaterial;
    
    // For optimization
    private static readonly int AlphaPropertyID = Shader.PropertyToID("_Alpha");
    
    void Start()
    {
        // Get necessary components
        atmosphereRenderer = GetComponent<Renderer>();
        boxCollider = GetComponent<BoxCollider>();
        
        // Cache the material
        atmosphereMaterial = atmosphereRenderer.material;
        
        // Make sure we're not sharing materials between instances
        atmosphereMaterial = new Material(atmosphereMaterial);
        atmosphereRenderer.material = atmosphereMaterial;
        
        if (player == null)
        {
            player = GameObject.FindGameObjectWithTag("Player")?.transform;
            if (player == null)
            {
                Debug.LogError("Player reference not set and could not be found automatically!");
            }
        }
    }
    
    void Update()
    {
        if (player == null || boxCollider == null || atmosphereMaterial == null)
            return;
            
        UpdateTransparency();
    }
    
    void UpdateTransparency()
    {
        // Get the player's position in local space
        Vector3 localPlayerPos = transform.InverseTransformPoint(player.position);
        
        // Get the box's local extents (half-size)
        Vector3 boxSize = boxCollider.size * 0.5f;
        
        // Calculate distance from player to nearest point on box
        Vector3 distanceVector = new Vector3(
            Mathf.Max(0, Mathf.Abs(localPlayerPos.x) - boxSize.x),
            Mathf.Max(0, Mathf.Abs(localPlayerPos.y) - boxSize.y),
            Mathf.Max(0, Mathf.Abs(localPlayerPos.z) - boxSize.z)
        );
        
        float distanceToBox = distanceVector.magnitude;
        
        // Check if player is inside or outside the box
        bool isInside = Mathf.Abs(localPlayerPos.x) <= boxSize.x && 
                        Mathf.Abs(localPlayerPos.y) <= boxSize.y && 
                        Mathf.Abs(localPlayerPos.z) <= boxSize.z;
        
        float alpha;
        
        if (isInside)
        {
            // Calculate minimum distance to box edge
            Vector3 distanceToEdge = new Vector3(
                boxSize.x - Mathf.Abs(localPlayerPos.x),
                boxSize.y - Mathf.Abs(localPlayerPos.y),
                boxSize.z - Mathf.Abs(localPlayerPos.z)
            );
            
            float minDistanceToEdge = Mathf.Min(distanceToEdge.x, distanceToEdge.y, distanceToEdge.z);
            
            // Calculate alpha based on inside settings
            if (transparentDistanceIN > opaqueDistanceIN)
            {
                // Transparent deeper inside, opaque near edge
                alpha = Mathf.InverseLerp(transparentDistanceIN, opaqueDistanceIN, minDistanceToEdge);
            }
            else
            {
                // Opaque deeper inside, transparent near edge
                alpha = Mathf.InverseLerp(opaqueDistanceIN, transparentDistanceIN, minDistanceToEdge);
                alpha = 1 - alpha; // Invert the alpha
            }
        }
        else
        {
            // Calculate alpha based on outside settings
            if (transparentDistanceOUT > opaqueDistanceOUT)
            {
                // Opaque near edge, transparent far away
                alpha = Mathf.InverseLerp(transparentDistanceOUT, opaqueDistanceOUT, distanceToBox);
            }
            else
            {
                // Transparent near edge, opaque far away
                alpha = Mathf.InverseLerp(opaqueDistanceOUT, transparentDistanceOUT, distanceToBox);
                alpha = 1 - alpha; // Invert the alpha
            }
        }
        
        // Clamp alpha between 0 and 1
        alpha = Mathf.Clamp01(alpha);
        
        // Apply transparency to material
        if (atmosphereMaterial.HasProperty(AlphaPropertyID))
        {
            atmosphereMaterial.SetFloat(AlphaPropertyID, alpha);
        }
        else if (atmosphereMaterial.HasProperty("_Color"))
        {
            Color color = atmosphereMaterial.GetColor("_Color");
            color.a = alpha;
            atmosphereMaterial.SetColor("_Color", color);
        }
        else
        {
            Debug.LogWarning("Material doesn't have a standard alpha property! Make sure your shader supports transparency.");
        }
    }
    
    // Optional: visualize the distances in the editor
    private void OnDrawGizmosSelected()
    {
        if (boxCollider == null)
            boxCollider = GetComponent<BoxCollider>();
            
        if (boxCollider == null)
            return;
            
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0, 1, 0, 0.2f);
        Gizmos.DrawWireCube(boxCollider.center, boxCollider.size);
    }
}