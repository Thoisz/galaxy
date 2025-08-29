using UnityEngine;

public class ItemDetailPager : MonoBehaviour
{
    public RectTransform content;       // The "Content" RectTransform
    public float pageWidth = 400f;      // Width of one page
    public float transitionSpeed = 8f;  // How fast the slide happens

    // Page state
    private int currentPage = 0;        // visible page index
    private Vector2 targetPos;

    // Clamp navigation between these (inclusive)
    private int minPage = 0;
    private int maxPage = 1;

    void Awake()
    {
        if (!content) content = GetComponent<RectTransform>();
    }

    void OnEnable()
    {
        // Snap to whatever our current page is when re-enabled
        GoToPage(currentPage, true);
    }

    void Start()
    {
        targetPos = new Vector2(-currentPage * pageWidth, 0f);
        if (content) content.anchoredPosition = targetPos;
    }

    void Update()
    {
        if (!content) return;
        content.anchoredPosition = Vector2.Lerp(content.anchoredPosition, targetPos, Time.deltaTime * transitionSpeed);
    }

    public void GoLeft()
    {
        GoToPage(Mathf.Clamp(currentPage - 1, minPage, maxPage));
    }

    public void GoRight()
    {
        GoToPage(Mathf.Clamp(currentPage + 1, minPage, maxPage));
    }

public void ForceToFirstPage(bool instant = true)
{
    // “First” means the lowest allowed page (minPage), which is 0 for weapons and 1 for accessories
    GoToPage(minPage, instant);
}

    public void SetBounds(int minInclusive, int maxInclusive)
    {
        minPage = Mathf.Min(minInclusive, maxInclusive);
        maxPage = Mathf.Max(minInclusive, maxInclusive);
        currentPage = Mathf.Clamp(currentPage, minPage, maxPage);
        UpdateTargetPosition();
    }

    public void GoToPage(int pageIndex, bool instant = false)
    {
        currentPage = Mathf.Clamp(pageIndex, minPage, maxPage);
        UpdateTargetPosition();
        if (instant && content) content.anchoredPosition = targetPos;
    }

    private void UpdateTargetPosition()
    {
        targetPos = new Vector2(-currentPage * pageWidth, 0f);
    }
}
