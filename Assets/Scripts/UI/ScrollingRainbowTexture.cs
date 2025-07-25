using UnityEngine;
using UnityEngine.UI;

public class ScrollingRainbowTexture : MonoBehaviour
{
    [Header("Scroll Settings")]
    public Vector2 scrollSpeed = new Vector2(0.5f, 0f);
    
    private Image rainbowImage;
    private Material materialInstance;
    private Vector2 currentOffset = Vector2.zero;
    
    void Start()
    {
        rainbowImage = GetComponent<Image>();
        if (rainbowImage != null)
        {
            // Create a UNIQUE instance so we don't affect other UI elements
            materialInstance = new Material(rainbowImage.material);
            rainbowImage.material = materialInstance;
        }
    }
    
    void Update()
    {
        if (materialInstance != null)
        {
            currentOffset += scrollSpeed * Time.deltaTime;
            currentOffset.x = currentOffset.x % 1f;
            currentOffset.y = currentOffset.y % 1f;
            
            materialInstance.SetTextureOffset("_MainTex", currentOffset);
        }
    }
    
    void OnDestroy()
    {
        // Clean up the material instance
        if (materialInstance != null)
        {
            DestroyImmediate(materialInstance);
        }
    }
}