using UnityEngine;
using UnityEngine.UI;

public class ItemDetailPager : MonoBehaviour
{
    public RectTransform content;       // The "Content" RectTransform
    public float pageWidth = 400f;      // Width of one page
    public float transitionSpeed = 8f;  // How fast the slide happens

    private int currentPage = 0;        // 0 = MoveSet, 1 = Description
    private Vector2 targetPos;

    void Start()
    {
        // Set initial position
        targetPos = Vector2.zero;
    }

    void Update()
    {
        // Smoothly slide the content
        content.anchoredPosition = Vector2.Lerp(content.anchoredPosition, targetPos, Time.deltaTime * transitionSpeed);
    }

    public void GoLeft()
    {
        currentPage = Mathf.Max(currentPage - 1, 0);
        UpdateTargetPosition();
    }

    public void GoRight()
    {
        currentPage = Mathf.Min(currentPage + 1, 1);
        UpdateTargetPosition();
    }

    void UpdateTargetPosition()
    {
        // Negative X because we move content left to reveal right page
        targetPos = new Vector2(-currentPage * pageWidth, 0);
    }
}