using UnityEngine;
using UnityEngine.UI;

public class UVScroller : MonoBehaviour
{
    public Vector2 scrollSpeed = new Vector2(0.2f, 0f);
    private RawImage rawImage;
    private Vector2 currentOffset;

    void Awake()
    {
        rawImage = GetComponent<RawImage>();
        rawImage.material = new Material(rawImage.material); // clone to avoid shared offset
    }

    void Update()
    {
        currentOffset += scrollSpeed * Time.deltaTime;
        rawImage.material.mainTextureOffset = currentOffset;
    }
}
