using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

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
        // Don't process menu key if typing in any input field
        if (IsTypingInInputField())
        {
            return;
        }
        
        ToggleMenu();
    }
}

// Helper method to check if user is typing
bool IsTypingInInputField()
{
    if (UnityEngine.EventSystems.EventSystem.current == null)
        return false;
        
    GameObject selectedObject = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
    
    if (selectedObject == null)
        return false;
    
    // Check for TextMeshPro input field
    TMP_InputField tmpInputField = selectedObject.GetComponent<TMP_InputField>();
    if (tmpInputField != null && tmpInputField.isFocused)
        return true;
    
    // Check for legacy Unity input field
    InputField legacyInputField = selectedObject.GetComponent<InputField>();
    if (legacyInputField != null && legacyInputField.isFocused)
        return true;
    
    return false;
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
    // Allow interruption in two cases:
    // 1. If TabSelection is sliding in (existing functionality)
    // 2. If any panel is sliding out and we want to bring TabSelection back up
    if ((isTabSelectionSlideIn || (!isMenuOpen && isAnimating)) && currentAnimation != null)
    {
        // Don't stop the current animation - let it continue and start coordinated animation
        if (!isMenuOpen && isAnimating && currentActiveTab != null)
        {
            // Panel is sliding out, start coordinated animation
            OpenMenuWithInterruption();
            return;
        }
        else
        {
            // TabSelection sliding in case - stop and restart
            StopCoroutine(currentAnimation);
            isAnimating = false;
            currentAnimation = null;
            isTabSelectionSlideIn = false;
        }
    }
    // Don't interrupt tab-to-tab switching animations
    else if (isAnimating && isMenuOpen)
    {
        return;
    }
    
    // If any menu is open, close it. If none open, open tab selection.
    if (isMenuOpen)
        CloseMenu();
    else
        OpenMenuWithInterruption();
}
    
    public void OpenMenu()
    {
        if (isAnimating) return;
        
        isMenuOpen = true;
        isTabSelectionSlideIn = true; // Mark that TabSelection is sliding in
        currentAnimation = StartCoroutine(SlideInPanel(tabSelectionPanel));
    }

    public void OpenMenuWithInterruption()
{
    // This method handles opening TabSelection even if another panel is sliding out
    isMenuOpen = true;
    isTabSelectionSlideIn = true;
    
    // If there's currently a panel sliding out, we need to handle the coordination
    if (isAnimating && currentActiveTab != null)
    {
        // Ensure proper layering before coordinated animation
        EnsureProperPanelLayering();
        
        // Start coordinated animation: old panel continues sliding down, TabSelection slides up
        // Set TabSelection to render above other panels temporarily
        SetPanelSortingOrder(tabSelectionPanel, 5);
        currentAnimation = StartCoroutine(CoordinatedPanelSwitch(currentActiveTab, tabSelectionPanel));
        currentActiveTab = null;
    }
    else
    {
        // Normal TabSelection slide in
        SetPanelSortingOrder(tabSelectionPanel, 0);
        currentAnimation = StartCoroutine(SlideInPanel(tabSelectionPanel));
    }
}

IEnumerator CoordinatedPanelSwitch(GameObject panelGoingDown, GameObject panelComingUp)
{
    isAnimating = true;
    
    // Activate the panel coming up and position it off-screen below
    panelComingUp.SetActive(true);
    
    RectTransform downRect = panelGoingDown.GetComponent<RectTransform>();
    RectTransform upRect = panelComingUp.GetComponent<RectTransform>();
    
    // Get current position of the panel going down (it might already be mid-animation)
    Vector2 downCurrentPos = downRect.anchoredPosition;
    
    // Calculate end positions
    Vector2 downEndPos = new Vector2(downCurrentPos.x, -(canvasHeight / 2) - offScreenOffset);
    Vector2 upStartPos = new Vector2(upRect.anchoredPosition.x, -(canvasHeight / 2) - offScreenOffset);
    Vector2 upEndPos = new Vector2(upRect.anchoredPosition.x, 0);
    
    // Position the incoming panel below screen
    upRect.anchoredPosition = upStartPos;
    
    // Calculate how much of the down animation is already complete
    float downAnimationProgress = 0f;
    if (downCurrentPos.y < 0)
    {
        // Panel is already sliding down, calculate progress
        float totalDownDistance = Mathf.Abs(downEndPos.y);
        float currentDownDistance = Mathf.Abs(downCurrentPos.y);
        downAnimationProgress = currentDownDistance / totalDownDistance;
    }
    
    float elapsedTime = downAnimationProgress * animationDuration; // Start from where the down animation left off
    
    while (elapsedTime < animationDuration)
    {
        elapsedTime += Time.deltaTime;
        float progress = elapsedTime / animationDuration;
        float curveValue = slideCurve.Evaluate(progress);
        
        // Continue the down animation from where it was
        float downProgress = Mathf.Clamp01((progress - downAnimationProgress) / (1f - downAnimationProgress));
        downRect.anchoredPosition = Vector2.Lerp(downCurrentPos, downEndPos, downProgress);
        
        // Panel coming up slides up from below
        upRect.anchoredPosition = Vector2.Lerp(upStartPos, upEndPos, curveValue);
        
        yield return null;
    }
    
    // Finalize positions
    downRect.anchoredPosition = downEndPos;
    upRect.anchoredPosition = upEndPos;
    
    // Deactivate the panel that went down and reset its sorting order
    panelGoingDown.SetActive(false);
    SetPanelSortingOrder(panelGoingDown, 0);
    
    // Reset TabSelection sorting order to normal
    SetPanelSortingOrder(panelComingUp, 0);
    
    isAnimating = false;
    currentAnimation = null;
    isTabSelectionSlideIn = false;
}
    
    public void CloseMenu()
{
    // Allow interruption if we're not already switching between tabs
    if (isAnimating && currentActiveTab != null && !isTabSelectionSlideIn) 
    {
        return;
    }
    
    // NEW: Notify EquipmentManager about all menus closing BEFORE starting animations
    EquipmentManager equipmentManager = FindObjectOfType<EquipmentManager>();
    if (equipmentManager != null)
    {
        equipmentManager.OnAllMenusClosed();
    }
    
    isMenuOpen = false;
    isTabSelectionSlideIn = false;
    
    // Close whichever panel is currently active
    GameObject panelToClose = currentActiveTab != null ? currentActiveTab : tabSelectionPanel;
    
    // Reset sorting order before closing and ensure proper layering
    SetPanelSortingOrder(panelToClose, 0);
    EnsureProperPanelLayering();
    
    currentAnimation = StartCoroutine(SlideOutPanel(panelToClose, () => {
        currentActiveTab = null;
    }));
}

void EnsureProperPanelLayering()
{
    // Make sure all main panels have the same base sorting order
    SetPanelSortingOrder(tabSelectionPanel, 0);
    if (statsTabPanel != null) SetPanelSortingOrder(statsTabPanel, 0);
    if (itemsTabPanel != null) SetPanelSortingOrder(itemsTabPanel, 0);
    if (equipTabPanel != null) SetPanelSortingOrder(equipTabPanel, 0);
    if (settingsTabPanel != null) SetPanelSortingOrder(settingsTabPanel, 0);
    
    // Find EquipmentManager and ensure detail panels are below main panels
    EquipmentManager equipmentManager = FindObjectOfType<EquipmentManager>();
    if (equipmentManager != null)
    {
        equipmentManager.EnsureDetailPanelsUnderMainPanels();
    }
}

void SetPanelSortingOrder(GameObject panel, int sortingOrder)
{
    Canvas panelCanvas = panel.GetComponent<Canvas>();
    if (panelCanvas == null)
    {
        panelCanvas = panel.AddComponent<Canvas>();
        panelCanvas.overrideSorting = true;
    }
    panelCanvas.sortingOrder = sortingOrder;
    
    // Also add GraphicRaycaster if it doesn't exist
    if (panel.GetComponent<GraphicRaycaster>() == null)
    {
        panel.AddComponent<GraphicRaycaster>();
    }
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