using UnityEngine;
using UnityEngine.UI;

public class LayeredUVScroller : MonoBehaviour
{
    [System.Serializable]
    public class ScrollLayer
    {
        [Header("Layer Settings")]
        public RawImage rawImage;
        public Vector2 scrollSpeed = new Vector2(0.2f, 0f);
        public float alpha = 1f; // Transparency for blending
        
        [HideInInspector]
        public Material materialInstance;
        [HideInInspector]
        public Vector2 currentOffset;
    }
    
    [Header("Scroll Layers")]
    public ScrollLayer[] scrollLayers = new ScrollLayer[2];

    void Start()
    {
        // Set up each layer
        for (int i = 0; i < scrollLayers.Length; i++)
        {
            SetupLayer(scrollLayers[i], i);
        }
    }
    
    void SetupLayer(ScrollLayer layer, int index)
    {
        if (layer.rawImage == null)
        {
            return;
        }
        
        if (layer.rawImage.texture == null)
        {
            return;
        }
        
        // Create a material instance for this layer
        if (layer.rawImage.material != null)
        {
            layer.materialInstance = new Material(layer.rawImage.material);
        }
        else
        {
            // Create a default UI material
            layer.materialInstance = new Material(Shader.Find("UI/Default"));
            layer.materialInstance.mainTexture = layer.rawImage.texture;
        }
        
        // Set the alpha for blending
        Color color = layer.rawImage.color;
        color.a = layer.alpha;
        layer.rawImage.color = color;
        
        layer.rawImage.material = layer.materialInstance;
    }

    void Update()
    {
        // Update each layer
        for (int i = 0; i < scrollLayers.Length; i++)
        {
            UpdateLayer(scrollLayers[i]);
        }
    }
    
    void UpdateLayer(ScrollLayer layer)
    {
        if (layer.materialInstance == null || layer.rawImage == null) 
            return;
        
        // Update the offset
        layer.currentOffset += layer.scrollSpeed * Time.deltaTime;
        
        // Apply the offset to the material
        layer.materialInstance.mainTextureOffset = layer.currentOffset;
    }
    
    void OnDestroy()
    {
        // Clean up all material instances
        for (int i = 0; i < scrollLayers.Length; i++)
        {
            if (scrollLayers[i].materialInstance != null)
            {
                if (Application.isPlaying)
                    Destroy(scrollLayers[i].materialInstance);
                else
                    DestroyImmediate(scrollLayers[i].materialInstance);
            }
        }
    }
    
    // Helper method to quickly set up common configurations
    [ContextMenu("Setup Default Dual Layer")]
    void SetupDefaultDualLayer()
    {
        if (scrollLayers.Length >= 2)
        {
            // Layer 0: Horizontal scroll
            scrollLayers[0].scrollSpeed = new Vector2(0.3f, 0f);
            scrollLayers[0].alpha = 0.7f;
            
            // Layer 1: Vertical scroll
            scrollLayers[1].scrollSpeed = new Vector2(0f, 0.2f);
            scrollLayers[1].alpha = 0.5f;
        }
    }
}