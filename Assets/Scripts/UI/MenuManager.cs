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
    public KeyCode menuKey = KeyCode.P;
    
    [Header("Animation Settings")]
    public float animationDuration = 0.5f;
    public float offScreenOffset = 200f; // How far below screen to position panels
    public AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    
    private bool isMenuOpen = false;
    private GameObject currentActiveTab;
    private Canvas canvas;
    private float screenHeight;
    
    // Animation tracking
    private bool isAnimating = false;
    
    void Start()
    {
        // Get canvas reference and screen height
        canvas = GetComponentInParent<Canvas>();
        screenHeight = Screen.height;
        
        // Make sure menu starts closed and positioned off-screen
        SetupInitialPositions();
        
        // Set up button listeners
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
        
        // Only set up tab buttons if the panels exist
        if (statsTabButton != null && statsTabPanel != null)
            statsTabButton.onClick.AddListener(() => OpenTab(statsTabPanel));
        if (itemsTabButton != null && itemsTabPanel != null)
            itemsTabButton.onClick.AddListener(() => OpenTab(itemsTabPanel));
        if (equipTabButton != null && equipTabPanel != null)
            equipTabButton.onClick.AddListener(() => OpenTab(equipTabPanel));
        if (settingsTabButton != null && settingsTabPanel != null)
            settingsTabButton.onClick.AddListener(() => OpenTab(settingsTabPanel));
    }
    
    void Update()
    {
        if (Input.GetKeyDown(menuKey) && !isAnimating)
        {
            ToggleMenu();
        }
    }
    
    void SetupInitialPositions()
    {
        // Position all panels off-screen at the bottom
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
            Vector3 offScreenPosition = rectTransform.anchoredPosition;
            offScreenPosition.y = -screenHeight - offScreenOffset; // Position below screen + extra offset
            rectTransform.anchoredPosition = offScreenPosition;
        }
    }
    
    void PositionPanelOnScreen(GameObject panel)
    {
        if (panel != null)
        {
            RectTransform rectTransform = panel.GetComponent<RectTransform>();
            Vector3 onScreenPosition = rectTransform.anchoredPosition;
            onScreenPosition.y = 0; // Center position
            rectTransform.anchoredPosition = onScreenPosition;
        }
    }
    
    public void ToggleMenu()
    {
        if (isAnimating) return;
        
        if (isMenuOpen)
            CloseMenu();
        else
            OpenMenu();
    }
    
    public void OpenMenu()
    {
        if (isAnimating) return;
        
        isMenuOpen = true;
        StartCoroutine(SlideInPanel(tabSelectionPanel));
    }
    
    public void CloseMenu()
    {
        if (isAnimating) return;
        
        isMenuOpen = false;
        
        // Close whichever panel is currently active
        GameObject panelToClose = currentActiveTab != null ? currentActiveTab : tabSelectionPanel;
        StartCoroutine(SlideOutPanel(panelToClose, () => {
            currentActiveTab = null;
        }));
    }
    
    public void OpenTab(GameObject tabToOpen)
    {
        if (isAnimating || tabToOpen == null) return;
        
        GameObject currentPanel = currentActiveTab != null ? currentActiveTab : tabSelectionPanel;
        
        if (currentPanel == tabToOpen) return; // Don't animate to same panel
        
        currentActiveTab = tabToOpen;
        
        // Slide current panel down and new panel up simultaneously
        StartCoroutine(SwitchPanels(currentPanel, tabToOpen));
    }
    
    public void BackToTabSelection()
    {
        if (isAnimating) return;
        
        if (currentActiveTab != null)
        {
            StartCoroutine(SwitchPanels(currentActiveTab, tabSelectionPanel));
            currentActiveTab = null;
        }
    }
    
    IEnumerator SlideInPanel(GameObject panel)
    {
        isAnimating = true;
        panel.SetActive(true);
        
        RectTransform rectTransform = panel.GetComponent<RectTransform>();
        Vector3 startPos = rectTransform.anchoredPosition;
        Vector3 endPos = startPos;
        endPos.y = 0;
        
        float elapsedTime = 0f;
        
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            float curveValue = slideCurve.Evaluate(progress);
            
            rectTransform.anchoredPosition = Vector3.Lerp(startPos, endPos, curveValue);
            yield return null;
        }
        
        rectTransform.anchoredPosition = endPos;
        isAnimating = false;
    }
    
    IEnumerator SlideOutPanel(GameObject panel, System.Action onComplete = null)
    {
        isAnimating = true;
        
        RectTransform rectTransform = panel.GetComponent<RectTransform>();
        Vector3 startPos = rectTransform.anchoredPosition;
        Vector3 endPos = startPos;
        endPos.y = -screenHeight - offScreenOffset; // Use the custom offset
        
        float elapsedTime = 0f;
        
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            float curveValue = slideCurve.Evaluate(progress);
            
            rectTransform.anchoredPosition = Vector3.Lerp(startPos, endPos, curveValue);
            yield return null;
        }
        
        rectTransform.anchoredPosition = endPos;
        panel.SetActive(false);
        
        onComplete?.Invoke();
        isAnimating = false;
    }
    
    IEnumerator SwitchPanels(GameObject currentPanel, GameObject newPanel)
    {
        isAnimating = true;
        
        // Activate new panel and position it off-screen
        newPanel.SetActive(true);
        PositionPanelOffScreen(newPanel);
        
        RectTransform currentRect = currentPanel.GetComponent<RectTransform>();
        RectTransform newRect = newPanel.GetComponent<RectTransform>();
        
        Vector3 currentStartPos = currentRect.anchoredPosition;
        Vector3 currentEndPos = currentStartPos;
        currentEndPos.y = -screenHeight - offScreenOffset; // Use the custom offset
        
        Vector3 newStartPos = newRect.anchoredPosition;
        Vector3 newEndPos = newStartPos;
        newEndPos.y = 0;
        
        float elapsedTime = 0f;
        
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            float curveValue = slideCurve.Evaluate(progress);
            
            // Animate both panels simultaneously
            currentRect.anchoredPosition = Vector3.Lerp(currentStartPos, currentEndPos, curveValue);
            newRect.anchoredPosition = Vector3.Lerp(newStartPos, newEndPos, curveValue);
            yield return null;
        }
        
        // Finalize positions
        currentRect.anchoredPosition = currentEndPos;
        newRect.anchoredPosition = newEndPos;
        
        // Deactivate the old panel
        currentPanel.SetActive(false);
        
        isAnimating = false;
    }
}