using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class MenuManager : MonoBehaviour
{
    [Header("Menu References")]
    public GameObject tabSelectionPanel;
    public GameObject statsTabPanel;
    public GameObject itemsTabPanel;
    public GameObject equipTabPanel;
    public GameObject settingsTabPanel;
    
    [Header("Tab Buttons")]
    public Button statsTabButton;
    public Button itemsTabButton;
    public Button equipTabButton;
    public Button settingsTabButton;
    
    [Header("Menu Controls")]
    public Button menuOpenButton;
    public Button menuCloseButton;
    public KeyCode menuKey = KeyCode.B;
    
    [Header("Animation Settings")]
    public float animationDuration = 0.5f;
    public float offScreenOffset = 200f;
    public AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    
    private bool isMenuOpen = false;
    private GameObject currentActiveTab;
    private Canvas canvas;
    private RectTransform canvasRect;
    private float canvasHeight;
    
    // Animation tracking
    private bool isAnimating = false;
    private Coroutine currentAnimation = null;
    private bool isTabSelectionSlideIn = false; // Track if TabSelection is sliding in
    
    void Start()
    {
        canvas = GetComponentInParent<Canvas>();
        canvasRect = canvas.GetComponent<RectTransform>();
        StartCoroutine(InitializeAfterFrame());
    }
    
    IEnumerator InitializeAfterFrame()
    {
        yield return null;
        canvasHeight = canvasRect.rect.height;
        SetupInitialPositions();
        SetupButtonListeners();
    }
    
    void SetupButtonListeners()
    {
        if (menuOpenButton != null)
        {
            menuOpenButton.onClick.RemoveAllListeners();
            menuOpenButton.onClick.AddListener(ToggleMenu);
        }
        
        if (menuCloseButton != null)
        {
            menuCloseButton.onClick.RemoveAllListeners();
            menuCloseButton.onClick.AddListener(CloseMenu);
        }
        
        if (statsTabButton != null && statsTabPanel != null)
        {
            statsTabButton.onClick.RemoveAllListeners();
            statsTabButton.onClick.AddListener(() => OpenTab(statsTabPanel));
        }
        if (itemsTabButton != null && itemsTabPanel != null)
        {
            itemsTabButton.onClick.RemoveAllListeners();
            itemsTabButton.onClick.AddListener(() => OpenTab(itemsTabPanel));
        }
        if (equipTabButton != null && equipTabPanel != null)
        {
            equipTabButton.onClick.RemoveAllListeners();
            equipTabButton.onClick.AddListener(() => OpenTab(equipTabPanel));
        }
        if (settingsTabButton != null && settingsTabPanel != null)
        {
            settingsTabButton.onClick.RemoveAllListeners();
            settingsTabButton.onClick.AddListener(() => OpenTab(settingsTabPanel));
        }
    }
    
    void Update()
    {
        if (Input.GetKeyDown(menuKey))
        {
            ToggleMenu();
        }
    }
    
    void SetupInitialPositions()
    {
        PositionPanelOffScreen(tabSelectionPanel);
        PositionPanelOffScreen(statsTabPanel);
        PositionPanelOffScreen(itemsTabPanel);
        PositionPanelOffScreen(equipTabPanel);
        PositionPanelOffScreen(settingsTabPanel);
        
        // Deactivate all panels
        tabSelectionPanel.SetActive(false);
        if (statsTabPanel != null) statsTabPanel.SetActive(false);
        if (itemsTabPanel != null) itemsTabPanel.SetActive(false);
        if (equipTabPanel != null) equipTabPanel.SetActive(false);
        if (settingsTabPanel != null) settingsTabPanel.SetActive(false);
    }
    
    void PositionPanelOffScreen(GameObject panel)
    {
        if (panel != null)
        {
            RectTransform rectTransform = panel.GetComponent<RectTransform>();
            Vector2 offScreenPosition = rectTransform.anchoredPosition;
            offScreenPosition.y = -(canvasHeight / 2) - offScreenOffset;
            rectTransform.anchoredPosition = offScreenPosition;
        }
    }
    
    public void ToggleMenu()
    {
        // Only allow interruption if TabSelection is sliding in
        if (isTabSelectionSlideIn && currentAnimation != null)
        {
            StopCoroutine(currentAnimation);
            isAnimating = false;
            currentAnimation = null;
            isTabSelectionSlideIn = false;
        }
        // Don't interrupt other animations - wait for them to finish
        else if (isAnimating)
        {
            return;
        }
        
        // If any menu is open, close it. If none open, open tab selection.
        if (isMenuOpen)
            CloseMenu();
        else
            OpenMenu();
    }
    
    public void OpenMenu()
    {
        if (isAnimating) return;
        
        isMenuOpen = true;
        isTabSelectionSlideIn = true; // Mark that TabSelection is sliding in
        currentAnimation = StartCoroutine(SlideInPanel(tabSelectionPanel));
    }
    
    public void CloseMenu()
    {
        if (isAnimating) return;
        
        isMenuOpen = false;
        isTabSelectionSlideIn = false; // Clear the flag
        
        // Close whichever panel is currently active
        GameObject panelToClose = currentActiveTab != null ? currentActiveTab : tabSelectionPanel;
        currentAnimation = StartCoroutine(SlideOutPanel(panelToClose, () => {
            currentActiveTab = null;
        }));
    }
    
    public void OpenTab(GameObject tabToOpen)
    {
        if (isAnimating || tabToOpen == null) return;
        
        GameObject currentPanel = currentActiveTab != null ? currentActiveTab : tabSelectionPanel;
        
        if (currentPanel == tabToOpen) return; // Don't animate to same panel
        
        currentActiveTab = tabToOpen;
        isTabSelectionSlideIn = false; // Clear the flag when switching tabs
        
        // Slide current panel down and new panel up simultaneously
        currentAnimation = StartCoroutine(SwitchPanels(currentPanel, tabToOpen));
    }
    
    public void BackToTabSelection()
    {
        if (isAnimating) return;
        
        if (currentActiveTab != null)
        {
            isTabSelectionSlideIn = false; // Clear the flag
            currentAnimation = StartCoroutine(SwitchPanels(currentActiveTab, tabSelectionPanel));
            currentActiveTab = null;
        }
    }
    
    IEnumerator SlideInPanel(GameObject panel)
    {
        isAnimating = true;
        panel.SetActive(true);
        
        RectTransform rectTransform = panel.GetComponent<RectTransform>();
        Vector2 startPos = new Vector2(rectTransform.anchoredPosition.x, -(canvasHeight / 2) - offScreenOffset);
        Vector2 endPos = new Vector2(rectTransform.anchoredPosition.x, 0);
        
        // Ensure starting position is correct
        rectTransform.anchoredPosition = startPos;
        
        float elapsedTime = 0f;
        
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            float curveValue = slideCurve.Evaluate(progress);
            
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, curveValue);
            yield return null;
        }
        
        rectTransform.anchoredPosition = endPos;
        isAnimating = false;
        currentAnimation = null;
        isTabSelectionSlideIn = false; // Clear the flag when animation completes
    }
    
    IEnumerator SlideOutPanel(GameObject panel, System.Action onComplete = null)
    {
        isAnimating = true;
        
        RectTransform rectTransform = panel.GetComponent<RectTransform>();
        Vector2 startPos = rectTransform.anchoredPosition;
        Vector2 endPos = new Vector2(startPos.x, -(canvasHeight / 2) - offScreenOffset);
        
        float elapsedTime = 0f;
        
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            float curveValue = slideCurve.Evaluate(progress);
            
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, curveValue);
            yield return null;
        }
        
        rectTransform.anchoredPosition = endPos;
        panel.SetActive(false);
        
        onComplete?.Invoke();
        isAnimating = false;
        currentAnimation = null;
    }
    
    IEnumerator SwitchPanels(GameObject currentPanel, GameObject newPanel)
    {
        isAnimating = true;
        
        // Activate new panel and position it off-screen
        newPanel.SetActive(true);
        
        RectTransform currentRect = currentPanel.GetComponent<RectTransform>();
        RectTransform newRect = newPanel.GetComponent<RectTransform>();
        
        // Set starting positions explicitly
        Vector2 currentStartPos = currentRect.anchoredPosition;
        Vector2 currentEndPos = new Vector2(currentStartPos.x, -(canvasHeight / 2) - offScreenOffset);
        
        Vector2 newStartPos = new Vector2(newRect.anchoredPosition.x, -(canvasHeight / 2) - offScreenOffset);
        Vector2 newEndPos = new Vector2(newRect.anchoredPosition.x, 0);
        
        // Ensure new panel starts in the correct off-screen position
        newRect.anchoredPosition = newStartPos;
        
        float elapsedTime = 0f;
        
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            float curveValue = slideCurve.Evaluate(progress);
            
            // Animate both panels simultaneously
            currentRect.anchoredPosition = Vector2.Lerp(currentStartPos, currentEndPos, curveValue);
            newRect.anchoredPosition = Vector2.Lerp(newStartPos, newEndPos, curveValue);
            yield return null;
        }
        
        // Finalize positions
        currentRect.anchoredPosition = currentEndPos;
        newRect.anchoredPosition = newEndPos;
        
        // Deactivate the old panel
        currentPanel.SetActive(false);
        
        isAnimating = false;
        currentAnimation = null;
    }
}