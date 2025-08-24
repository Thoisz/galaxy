using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using System.Collections;

[System.Serializable]
public class EquipableItem
{
    [Header("Basic Info")]
    public string itemName;
    public string itemDescription;
    public EquipmentCategory category;
    public EquipmentRarity rarity;
    public bool isEquipped = false;
    
    [Header("UI Display")]
    public Sprite itemIcon; // 2D image for the item card UI
    
    [Header("3D Model")]
    public GameObject item3DModel; // The 3D model that spawns on player when equipped
    
    [Header("Weapon Stats (if applicable)")]
    public int damage = 0;
    public float attackSpeed = 1f;
    public string[] moveset; // Array of move names
    
    [Header("Accessory Bonuses (if applicable)")]
    public int healthBonus = 0;
    public int staminaBonus = 0;
    public float speedBonus = 0f; // Speed bonus for items like booster shoes
    public string[] specialAbilities; // Array of special bonus descriptions
    
    [Header("Equipment Tweaks (Applied Permanently)")]
    public Vector3 appliedScaleTweak = Vector3.one;
    public Vector3 appliedPositionTweak = Vector3.zero;
    public Vector3 appliedRotationTweak = Vector3.zero;
}

public enum EquipmentCategory
{
    MeleeWeapon,
    RangedWeapon,
    Accessory
}

public enum EquipmentRarity
{
    Common,    // Blue
    Rare,      // Orange  
    Legendary, // Purple (changed from Legend)
    Exotic     // Animated rainbow (changed from Mythical)
}

public class EquipmentManager : MonoBehaviour
{
    [Header("Equipment Database")]
    public List<EquipableItem> playerEquipment = new List<EquipableItem>();

    [Header("Equipment Tweaks (Applied Permanently)")]
    public Vector3 appliedScaleTweak = Vector3.one;
    public Vector3 appliedPositionTweak = Vector3.zero;
    public Vector3 appliedRotationTweak = Vector3.zero;
    
    [Header("UI References")]
    public Transform itemsContainer; // The Content object of the ScrollView
    public GameObject equipItemCardPrefabC; // Common rarity prefab
    public GameObject equipItemCardPrefabR; // Rare rarity prefab
    public GameObject equipItemCardPrefabL; // Legendary rarity prefab
    public GameObject equipItemCardPrefabE; // Exotic rarity prefab (changed from M)
    public GameObject rainbowBackgroundPrefab; // Standalone rainbow background prefab
    public TMP_Dropdown filterDropdown;
    public TMP_InputField searchInputField;
    public ScrollRect itemsScrollRect;
    public Button equipPanelCloseButton; // Close button for this specific panel
    
    [Header("Currently Equipped")]
    public EquipableItem equippedMeleeWeapon;
    public EquipableItem equippedRangedWeapon;
    public EquipableItem equippedAccessory;
    
     [Header("Detail Panel References")]
    public GameObject itemDetailPanelC; // Common rarity detail panel
    public GameObject itemDetailPanelR; // Rare rarity detail panel
    public GameObject itemDetailPanelL; // Legendary rarity detail panel
    public GameObject itemDetailPanelE; // Exotic rarity detail panel

    [Header("Rainbow Background for Exotic Detail Panel")]
    public GameObject exoticDetailRainbowBackground;
    
    [Header("Detail Panel Equip Buttons")]
    public Button equipButtonC; // Common panel equip button
    public Button equipButtonR; // Rare panel equip button
    public Button equipButtonL; // Legendary panel equip button
    public Button equipButtonE; // Exotic panel equip button
    
    [Header("Animation Settings")]
    public float slideAnimationDuration = 0.3f;
    public float slideStartOffset = 500f; // How far right to start the slide from
    public Vector2 positionOffset = Vector2.zero; // Manual position adjustment if needed
    public AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    
    private List<EquipableItem> filteredItems;
    private List<GameObject> currentItemCards = new List<GameObject>();
    private List<GameObject> currentRainbowBackgrounds = new List<GameObject>(); // Track rainbow backgrounds
    private EquipableItem currentDetailItem; // Track which item's detail is open
    private GameObject currentActiveDetailPanel = null;
    private bool isDetailPanelAnimating = false;
    private Coroutine currentDetailAnimation = null; // Track current animation coroutine
    
    // Store original positions for each detail panel
    private Dictionary<GameObject, Vector2> originalDetailPanelPositions = new Dictionary<GameObject, Vector2>();
    
    void Start()
{
    SetupFilterDropdown();
    SetupSearchField();
    SetupDetailPanel();
    SetupEquipPanelCloseButton();
    SetupEquipButtons();

    RefreshEquipmentDisplay();
}


    void DiagnosticCheck()
{
    // Method removed since it was only for debugging
}

void SetDetailPanelSortingOrder(GameObject panel, int sortingOrder)
{
    if (panel == null) return;
    
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
    
    // NEW: Setup equip buttons for each detail panel
    void SetupEquipButtons()
    {
        if (equipButtonC != null)
        {
            equipButtonC.onClick.RemoveAllListeners();
            equipButtonC.onClick.AddListener(() => OnEquipButtonClicked());
        }
        
        if (equipButtonR != null)
        {
            equipButtonR.onClick.RemoveAllListeners();
            equipButtonR.onClick.AddListener(() => OnEquipButtonClicked());
        }
        
        if (equipButtonL != null)
        {
            equipButtonL.onClick.RemoveAllListeners();
            equipButtonL.onClick.AddListener(() => OnEquipButtonClicked());
        }
        
        if (equipButtonE != null)
        {
            equipButtonE.onClick.RemoveAllListeners();
            equipButtonE.onClick.AddListener(() => OnEquipButtonClicked());
        }
    }
    
    void OnEquipButtonClicked()
{
    if (currentDetailItem != null)
    {
        if (currentDetailItem.isEquipped)
        {
            UnequipItem(currentDetailItem);
        }
        else
        {
            EquipItem(currentDetailItem);
        }
    }
}
    
    // NEW: Get the appropriate equip button for the current detail panel
    Button GetCurrentEquipButton()
    {
        if (currentActiveDetailPanel == itemDetailPanelC) return equipButtonC;
        if (currentActiveDetailPanel == itemDetailPanelR) return equipButtonR;
        if (currentActiveDetailPanel == itemDetailPanelL) return equipButtonL;
        if (currentActiveDetailPanel == itemDetailPanelE) return equipButtonE;
        return null;
    }
    
      void UpdateEquipButton()
    {
        // Button stays exactly as designed - no text or color changes
        // The button functionality is handled purely through OnEquipButtonClicked()
    }
    
    void OnEnable()
    {
        // Call this whenever the EquipmentManager GameObject becomes active
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(AggressiveResetAfterFrame());
        }
    }
    
    IEnumerator AggressiveResetAfterFrame()
{
    // Wait a frame to ensure any ongoing animations are processed
    yield return null;
    yield return null; // Wait an extra frame to be safe
    
    // ALWAYS reset all panels when equip tab opens
    ForceResetAllDetailPanelsToOriginalPositions();
    
    // Clear any current detail item state
    currentDetailItem = null;
    currentActiveDetailPanel = null;
    
    // Stop any animations
    if (currentDetailAnimation != null)
    {
        StopCoroutine(currentDetailAnimation);
        currentDetailAnimation = null;
    }
    isDetailPanelAnimating = false;
    
    // Update card states
    UpdateAllCardStates();
}

private void ApplyItemBonuses(EquipableItem item)
{
    // Apply speed bonus
    if (item.speedBonus > 0)
    {
        if (PlayerMovement.instance != null)
        {
            PlayerMovement.instance.AddSpeedModifier(item.speedBonus);
            Debug.Log($"Applied speed bonus: +{item.speedBonus} from {item.itemName}");
        }
    }
    
    // Here you can add other bonuses like:
    // - Health bonus
    // - Stamina bonus
    // - Special abilities
}

private void RemoveItemBonuses(EquipableItem item)
{
    // Remove speed bonus
    if (item.speedBonus > 0)
    {
        if (PlayerMovement.instance != null)
        {
            PlayerMovement.instance.RemoveSpeedModifier(item.speedBonus);
            Debug.Log($"Removed speed bonus: -{item.speedBonus} from {item.itemName}");
        }
    }
    
    // Here you can remove other bonuses
}
    
    // NEW METHOD: Reset all detail panels to original positions
    void ResetAllDetailPanels()
    {
        if (itemDetailPanelC != null)
        {
            itemDetailPanelC.SetActive(false);
            itemDetailPanelC.GetComponent<RectTransform>().anchoredPosition = originalDetailPanelPositions[itemDetailPanelC];
        }
        if (itemDetailPanelR != null)
        {
            itemDetailPanelR.SetActive(false);
            itemDetailPanelR.GetComponent<RectTransform>().anchoredPosition = originalDetailPanelPositions[itemDetailPanelR];
        }
        if (itemDetailPanelL != null)
        {
            itemDetailPanelL.SetActive(false);
            itemDetailPanelL.GetComponent<RectTransform>().anchoredPosition = originalDetailPanelPositions[itemDetailPanelL];
        }
        if (itemDetailPanelE != null)
        {
            itemDetailPanelE.SetActive(false);
            itemDetailPanelE.GetComponent<RectTransform>().anchoredPosition = originalDetailPanelPositions[itemDetailPanelE];
        }
    }
    
    void SetupFilterDropdown()
{
    if (filterDropdown == null) return;

    filterDropdown.ClearOptions();
    filterDropdown.AddOptions(new List<string> { "All", "Melee", "Ranged", "Accessory" });

    filterDropdown.onValueChanged.RemoveAllListeners();
    filterDropdown.onValueChanged.AddListener(OnFilterChanged);

    // Initial populate + caption update
    RefreshEquipmentDisplay();
}

    void SetupSearchField()
    {
        searchInputField.onValueChanged.AddListener(OnSearchChanged);
    }
    
    void SetupDetailPanel()
{
    // Store original positions and make sure all detail panels start closed
    if (itemDetailPanelC != null)
    {
        originalDetailPanelPositions[itemDetailPanelC] = itemDetailPanelC.GetComponent<RectTransform>().anchoredPosition + positionOffset;
        itemDetailPanelC.SetActive(false);
    }
    if (itemDetailPanelR != null)
    {
        originalDetailPanelPositions[itemDetailPanelR] = itemDetailPanelR.GetComponent<RectTransform>().anchoredPosition + positionOffset;
        itemDetailPanelR.SetActive(false);
    }
    if (itemDetailPanelL != null)
    {
        originalDetailPanelPositions[itemDetailPanelL] = itemDetailPanelL.GetComponent<RectTransform>().anchoredPosition + positionOffset;
        itemDetailPanelL.SetActive(false);
    }
    
    // Special handling for Exotic panel - store the rainbow background position instead
    if (itemDetailPanelE != null)
    {
        if (exoticDetailRainbowBackground != null)
        {
            // FORCE deactivate first to ensure clean state
            exoticDetailRainbowBackground.SetActive(false);
            
            // Store the rainbow background's position since that's what we'll be moving
            RectTransform rainbowRect = exoticDetailRainbowBackground.GetComponent<RectTransform>();
            if (rainbowRect != null)
            {
                originalDetailPanelPositions[itemDetailPanelE] = rainbowRect.anchoredPosition + positionOffset;
            }
            else
            {
                originalDetailPanelPositions[itemDetailPanelE] = itemDetailPanelE.GetComponent<RectTransform>().anchoredPosition + positionOffset;
                itemDetailPanelE.SetActive(false);
            }
        }
        else
        {
            // Fallback if no rainbow background is set
            originalDetailPanelPositions[itemDetailPanelE] = itemDetailPanelE.GetComponent<RectTransform>().anchoredPosition + positionOffset;
            itemDetailPanelE.SetActive(false);
        }
    }
    
    // IMMEDIATE reset to ensure clean state
    ForceResetAllDetailPanelsToOriginalPositions();
    
    // NEW: Ensure detail panels start with proper sorting order
    EnsureDetailPanelsUnderMainPanels();
}

    void SetupEquipPanelCloseButton()
    {
        if (equipPanelCloseButton != null)
        {
            equipPanelCloseButton.onClick.AddListener(CloseEquipPanel);
        }
    }
    
    void CloseEquipPanel()
{
    // Close detail panel with coordinated movement (right + down)
    if (currentActiveDetailPanel != null && currentActiveDetailPanel.activeSelf)
    {
        CloseDetailPanelCoordinated();
    }
    
    // Find and call MenuManager's CloseMenu method
    MenuManager menuManager = FindObjectOfType<MenuManager>();
    if (menuManager != null)
    {
        menuManager.CloseMenu();
    }
}
    
    public void OnAllMenusClosed()
{
    // When all menus are closed, use coordinated movement for detail panels
    if (currentActiveDetailPanel != null && currentActiveDetailPanel.activeSelf)
    {
        // IMPORTANT: Ensure detail panels are under main panels before animating
        EnsureDetailPanelsUnderMainPanels();
        CloseDetailPanelCoordinated();
    }
}
    
       void CloseDetailPanelCoordinated()
    {
        // Stop any current animation
        if (currentDetailAnimation != null)
        {
            StopCoroutine(currentDetailAnimation);
            currentDetailAnimation = null;
        }
        
        if (currentActiveDetailPanel != null)
        {
            currentDetailAnimation = StartCoroutine(SlideCoordinatedDetailPanel(currentActiveDetailPanel));
        }
        
        currentDetailItem = null;
        UpdateAllCardStates();
    }
    
    IEnumerator SlideCoordinatedDetailPanel(GameObject panel)
{
    isDetailPanelAnimating = true;
    
    // Get the correct GameObject to animate
    GameObject animatablePanel = GetAnimatablePanel(panel);
    
    // FORCE detail panel to render behind main panels during coordinated animation
    SetDetailPanelSortingOrder(animatablePanel, -50);
    if (panel == itemDetailPanelE && exoticDetailRainbowBackground != null)
    {
        SetDetailPanelSortingOrder(exoticDetailRainbowBackground, -50);
    }
    
    // Also try to force the main equip panel to render above
    MenuManager menuManager = FindObjectOfType<MenuManager>();
    if (menuManager != null && menuManager.equipTabPanel != null)
    {
        Canvas equipCanvas = menuManager.equipTabPanel.GetComponent<Canvas>();
        if (equipCanvas == null)
        {
            equipCanvas = menuManager.equipTabPanel.AddComponent<Canvas>();
            equipCanvas.overrideSorting = true;
        }
        equipCanvas.sortingOrder = 10; // Force equip panel to render above detail panels
        
        // Add GraphicRaycaster if needed
        if (menuManager.equipTabPanel.GetComponent<GraphicRaycaster>() == null)
        {
            menuManager.equipTabPanel.AddComponent<GraphicRaycaster>();
        }
    }
    
    RectTransform rectTransform = animatablePanel.GetComponent<RectTransform>();
    Vector2 startPos = rectTransform.anchoredPosition; // Current position
    
    // GET THE EXACT SAME MOVEMENT AS THE EQUIP PANEL
    RectTransform equipPanelRect = menuManager.equipTabPanel.GetComponent<RectTransform>();
    Vector2 equipStartPos = equipPanelRect.anchoredPosition;
    
    Canvas canvas = GetComponentInParent<Canvas>();
    float canvasHeight = canvas.GetComponent<RectTransform>().rect.height;
    float menuManagerDownDistance = (canvasHeight / 2) + (menuManager != null ? menuManager.offScreenOffset : 200f);
    Vector2 equipEndPos = new Vector2(equipStartPos.x, equipStartPos.y - menuManagerDownDistance);
    
    // Calculate the EXACT SAME movement vector that the equip panel will use
    Vector2 equipMovementVector = equipEndPos - equipStartPos;
    
    // Apply the SAME movement vector to the detail panel
    Vector2 endPos = startPos + equipMovementVector;
    
    // USE SAME EXACT SETTINGS AS MENU MANAGER
    float animDuration = 0.18f;
    AnimationCurve animCurve = menuManager != null ? menuManager.slideCurve : slideCurve;
    
    float elapsedTime = 0f;
    
    while (elapsedTime < animDuration)
    {
        elapsedTime += Time.deltaTime;
        float progress = elapsedTime / animDuration;
        float curveValue = animCurve.Evaluate(progress);
        
        rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, curveValue);
        yield return null;
    }
    
    rectTransform.anchoredPosition = endPos;
    animatablePanel.SetActive(false);
    
    // FORCE RESET ALL PANELS to original positions after coordinated close
    ForceResetAllDetailPanelsToOriginalPositions();
    
    // Reset sorting orders after animation
    SetDetailPanelSortingOrder(animatablePanel, -5);
    if (panel == itemDetailPanelE && exoticDetailRainbowBackground != null)
    {
        SetDetailPanelSortingOrder(exoticDetailRainbowBackground, -6);
    }
    
    // Reset equip panel sorting order
    if (menuManager != null && menuManager.equipTabPanel != null)
    {
        Canvas equipCanvas = menuManager.equipTabPanel.GetComponent<Canvas>();
        if (equipCanvas != null)
        {
            equipCanvas.sortingOrder = 1;
        }
    }
    
    // Animation completed successfully
    currentActiveDetailPanel = null;
    isDetailPanelAnimating = false;
    currentDetailAnimation = null;
}

public EquipableItem FindEquippedItemByName(string itemName)
{
    if (equippedMeleeWeapon != null && equippedMeleeWeapon.itemName == itemName)
        return equippedMeleeWeapon;
    if (equippedRangedWeapon != null && equippedRangedWeapon.itemName == itemName)
        return equippedRangedWeapon;
    if (equippedAccessory != null && equippedAccessory.itemName == itemName)
        return equippedAccessory;
    
    return null;
}

string GetFilterLabelText(int selectedIndex)
{
    string baseLabel = selectedIndex switch
    {
        0 => "ALL",
        1 => "MELEE",
        2 => "RANGED",
        3 => "ACCESSORY",
        _ => "ALL"
    };

    int total = playerEquipment != null ? playerEquipment.Count : 0;
    int shown = filteredItems != null ? filteredItems.Count : 0;

    return $"{baseLabel} ({shown}/{total} shown)";
}
    
    void OnFilterChanged(int filterIndex)
{
    RefreshEquipmentDisplay();
}
    
    void OnSearchChanged(string searchText)
    {
        RefreshEquipmentDisplay();
    }
    
    public void RefreshEquipmentDisplay()
{
    ClearCurrentDisplay();
    FilterEquipment();
    DisplayFilteredEquipment();

    if (filterDropdown != null)
    {
        int idx = filterDropdown.value;

        // First let TMP set the default caption from the option...
        filterDropdown.RefreshShownValue();

        // ...then override JUST the caption text with counts.
        if (filterDropdown.captionText != null)
        {
            filterDropdown.captionText.text = GetFilterLabelText(idx);
        }
    }
}
    
    void FilterEquipment()
{
    int selectedIndex = filterDropdown.value;
    string searchText = searchInputField.text.ToLower();

    filteredItems = playerEquipment.Where(item =>
    {
        // Filter by category
        bool categoryMatch = selectedIndex == 0;
        if (!categoryMatch && selectedIndex > 0)
        {
            EquipmentCategory selectedCategory = (EquipmentCategory)(selectedIndex - 1);
            categoryMatch = item.category == selectedCategory;
        }

        // Filter by search
        bool searchMatch = string.IsNullOrEmpty(searchText) || item.itemName.ToLower().Contains(searchText);

        return categoryMatch && searchMatch;
    }).ToList();
}
    
    void DisplayFilteredEquipment()
{
    if (itemsContainer == null)
    {
        return;
    }
    
    for (int i = 0; i < filteredItems.Count; i++)
    {
        GameObject prefabToUse = GetPrefabForRarity(filteredItems[i].rarity);
        
        if (prefabToUse == null)
        {
            continue;
        }
        
        // For exotic items, create a wrapper to hold both background and button
        if (filteredItems[i].rarity == EquipmentRarity.Exotic && rainbowBackgroundPrefab != null)
        {
            // Create wrapper GameObject
            GameObject wrapper = new GameObject("ExoticWrapper");
            wrapper.transform.SetParent(itemsContainer, false);
            
            // Add RectTransform first
            RectTransform wrapperRect = wrapper.AddComponent<RectTransform>();
            
            // Copy layout properties from your button prefab
            RectTransform buttonRect = prefabToUse.GetComponent<RectTransform>();
            if (buttonRect != null)
            {
                wrapperRect.sizeDelta = buttonRect.sizeDelta;
                wrapperRect.anchorMin = buttonRect.anchorMin;
                wrapperRect.anchorMax = buttonRect.anchorMax;
                wrapperRect.pivot = buttonRect.pivot;
            }
            
            // Add LayoutElement AFTER setting up RectTransform
            LayoutElement layoutElement = wrapper.AddComponent<LayoutElement>();
            if (buttonRect != null)
            {
                layoutElement.preferredWidth = buttonRect.sizeDelta.x;
                layoutElement.preferredHeight = buttonRect.sizeDelta.y;
            }
            
            // Create rainbow background as child of wrapper
            GameObject rainbowBG = Instantiate(rainbowBackgroundPrefab, wrapper.transform);
            RectTransform rainbowRect = rainbowBG.GetComponent<RectTransform>();
            rainbowRect.anchorMin = Vector2.zero;
            rainbowRect.anchorMax = Vector2.one;
            rainbowRect.anchoredPosition = Vector2.zero;
            rainbowRect.sizeDelta = Vector2.zero;
            
            // Create button as child of wrapper
            GameObject itemCard = Instantiate(prefabToUse, wrapper.transform);
            RectTransform cardRect = itemCard.GetComponent<RectTransform>();
            cardRect.anchorMin = Vector2.zero;
            cardRect.anchorMax = Vector2.one;
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = Vector2.zero;
            
            // Setup the card
            EquipItemCardUI itemCardUI = itemCard.GetComponent<EquipItemCardUI>();
            if (itemCardUI != null)
            {
                itemCardUI.SetupItemCard(filteredItems[i], this);
                itemCardUI.SetRainbowBackground(rainbowBG);
            }
            
            currentRainbowBackgrounds.Add(rainbowBG);
            currentItemCards.Add(wrapper);
        }
        else
        {
            // Normal items (non-exotic)
            GameObject itemCard = Instantiate(prefabToUse, itemsContainer);
            EquipItemCardUI itemCardUI = itemCard.GetComponent<EquipItemCardUI>();
            
            if (itemCardUI != null)
            {
                itemCardUI.SetupItemCard(filteredItems[i], this);
            }
            
            currentItemCards.Add(itemCard);
        }
    }
    
    // Reset scroll to top
    Canvas.ForceUpdateCanvases();
    itemsScrollRect.verticalNormalizedPosition = 1f;
}
    
    void ClearCurrentDisplay()
    {
        foreach (GameObject card in currentItemCards)
        {
            Destroy(card);
        }
        currentItemCards.Clear();
        
        foreach (GameObject rainbowBG in currentRainbowBackgrounds)
        {
            Destroy(rainbowBG);
        }
        currentRainbowBackgrounds.Clear();
    }

    public void EnsureDetailPanelsUnderMainPanels()
{
    // Set all detail panels to render WELL below main panels
    SetDetailPanelSortingOrder(itemDetailPanelC, -20);
    SetDetailPanelSortingOrder(itemDetailPanelR, -20);
    SetDetailPanelSortingOrder(itemDetailPanelL, -20);
    SetDetailPanelSortingOrder(itemDetailPanelE, -20);
    
    // Special handling for exotic rainbow background - even lower
    if (exoticDetailRainbowBackground != null)
    {
        SetDetailPanelSortingOrder(exoticDetailRainbowBackground, -25);
    }
    
    // Make sure the equip panel itself has a positive sorting order
    MenuManager menuManager = FindObjectOfType<MenuManager>();
    if (menuManager != null && menuManager.equipTabPanel != null)
    {
        Canvas equipCanvas = menuManager.equipTabPanel.GetComponent<Canvas>();
        if (equipCanvas == null)
        {
            equipCanvas = menuManager.equipTabPanel.AddComponent<Canvas>();
            equipCanvas.overrideSorting = true;
        }
        equipCanvas.sortingOrder = 1;
        
        if (menuManager.equipTabPanel.GetComponent<GraphicRaycaster>() == null)
        {
            menuManager.equipTabPanel.AddComponent<GraphicRaycaster>();
        }
    }
}
    
    public void EquipItem(EquipableItem item)
{
    // Unequip current item of same category
    switch (item.category)
    {
        case EquipmentCategory.MeleeWeapon:
            if (equippedMeleeWeapon != null) equippedMeleeWeapon.isEquipped = false;
            equippedMeleeWeapon = item;
            break;
        case EquipmentCategory.RangedWeapon:
            if (equippedRangedWeapon != null) equippedRangedWeapon.isEquipped = false;
            equippedRangedWeapon = item;
            break;
        case EquipmentCategory.Accessory:
            if (equippedAccessory != null) equippedAccessory.isEquipped = false;
            equippedAccessory = item;
            break;
    }
    
    item.isEquipped = true;
    
    // ADD THIS LINE:
    ApplyItemBonuses(item);
    
    // Update equip button after equipping
    UpdateEquipButton();
    
    // DON'T refresh the entire display - just update the card states
    UpdateAllCardStates();
    
    // Here you would spawn the actual item model on the player
    SpawnEquippedItem(item);
}
    
    public void UnequipItem(EquipableItem item)
{
    item.isEquipped = false;
    
    // ADD THIS LINE:
    RemoveItemBonuses(item);
    
    switch (item.category)
    {
        case EquipmentCategory.MeleeWeapon:
            equippedMeleeWeapon = null;
            break;
        case EquipmentCategory.RangedWeapon:
            equippedRangedWeapon = null;
            break;
        case EquipmentCategory.Accessory:
            equippedAccessory = null;
            break;
    }
    
    // Update equip button after unequipping
    UpdateEquipButton();
    
    // DON'T refresh the entire display - just update the card states
    UpdateAllCardStates();
    
    // Here you would remove the item model from the player
    RemoveEquippedItem(item);
}
    
    public void ShowItemDetail(EquipableItem item)
{
    // Check for same item click FIRST, before resetting anything
    bool isSameItemClick = (currentDetailItem == item);
    bool isPanelOpen = (currentActiveDetailPanel != null && currentActiveDetailPanel.activeSelf);
    bool isAnimating = isDetailPanelAnimating;
    
    // If the same item is clicked during animation, reverse/cancel the slide
    if (isSameItemClick && isAnimating && currentDetailAnimation != null)
    {
        StopCoroutine(currentDetailAnimation);
        isDetailPanelAnimating = false;
        currentDetailAnimation = null;
        
        // Reverse slide - close the panel that was sliding in
        CloseItemDetail();
        return;
    }
    
    // If the same item is clicked when panel is fully open, close it smoothly
    if (isSameItemClick && isPanelOpen && !isAnimating)
    {
        CloseItemDetail();
        return;
    }
    
    // ONLY reset panels if we're opening a different item or no panel is open
    if (!isSameItemClick || !isPanelOpen)
    {
        ForceResetAllDetailPanelsToOriginalPositions();
    }
    
    // Get the correct detail panel based on rarity
    GameObject targetDetailPanel = GetDetailPanelForRarity(item.rarity);
    
    // Stop any current animation before starting a new one
    if (currentDetailAnimation != null)
    {
        StopCoroutine(currentDetailAnimation);
        isDetailPanelAnimating = false;
        currentDetailAnimation = null;
    }
    
    // Update current detail item
    currentDetailItem = item;
    
    // Update all card states
    UpdateAllCardStates();
    
    if (targetDetailPanel != null)
    {
        // Ensure proper sorting order before opening
        EnsureDetailPanelsUnderMainPanels();
        
        // Check if we need to switch panels or just open a new one
        if (currentActiveDetailPanel != null && currentActiveDetailPanel != targetDetailPanel && currentActiveDetailPanel.activeSelf)
        {
            currentDetailAnimation = StartCoroutine(SwitchDetailPanels(currentActiveDetailPanel, targetDetailPanel, item));
        }
        else
        {
            currentActiveDetailPanel = targetDetailPanel;
            currentDetailAnimation = StartCoroutine(SlideInDetailPanel(targetDetailPanel, item));
        }
    }
}
    
    // FORCE reset all detail panels - more aggressive than ResetAllDetailPanels
      void ForceResetAllDetailPanels()
    {
        // Stop any ongoing animations first
        if (currentDetailAnimation != null)
        {
            StopCoroutine(currentDetailAnimation);
            currentDetailAnimation = null;
        }
        isDetailPanelAnimating = false;
        
        // Force reset all panels regardless of their current state
        ForceResetAllDetailPanelsToOriginalPositions();
        
        // Clear all state
        currentDetailItem = null;
        currentActiveDetailPanel = null;
    }

    void ForceResetAllDetailPanelsToOriginalPositions()
{
    if (itemDetailPanelC != null)
    {
        itemDetailPanelC.SetActive(false);
        itemDetailPanelC.GetComponent<RectTransform>().anchoredPosition = originalDetailPanelPositions[itemDetailPanelC];
    }
    if (itemDetailPanelR != null)
    {
        itemDetailPanelR.SetActive(false);
        itemDetailPanelR.GetComponent<RectTransform>().anchoredPosition = originalDetailPanelPositions[itemDetailPanelR];
    }
    if (itemDetailPanelL != null)
    {
        itemDetailPanelL.SetActive(false);
        itemDetailPanelL.GetComponent<RectTransform>().anchoredPosition = originalDetailPanelPositions[itemDetailPanelL];
    }
    
    // Special handling for Exotic panel
    if (itemDetailPanelE != null)
    {
        if (exoticDetailRainbowBackground != null)
        {
            // Deactivate the rainbow background (which will hide the panel too)
            exoticDetailRainbowBackground.SetActive(false);
            exoticDetailRainbowBackground.GetComponent<RectTransform>().anchoredPosition = originalDetailPanelPositions[itemDetailPanelE];
        }
        else
        {
            // Fallback
            itemDetailPanelE.SetActive(false);
            itemDetailPanelE.GetComponent<RectTransform>().anchoredPosition = originalDetailPanelPositions[itemDetailPanelE];
        }
    }
}
    
    public void CloseItemDetail()
    {
        // Stop any current animation
        if (currentDetailAnimation != null)
        {
            StopCoroutine(currentDetailAnimation);
            currentDetailAnimation = null;
        }
        
        if (currentActiveDetailPanel != null)
        {
            currentDetailAnimation = StartCoroutine(SlideOutDetailPanel(currentActiveDetailPanel));
        }
        
        currentDetailItem = null;
        UpdateAllCardStates();
    }
    
    IEnumerator SlideInDetailPanel(GameObject panel, EquipableItem item)
{
    isDetailPanelAnimating = true;
    
    // Get the correct GameObject to animate (rainbow background for exotic, panel for others)
    GameObject animatablePanel = GetAnimatablePanel(panel);
    
    // IMPORTANT: Reset to original position BEFORE activating to prevent ghost sliding
    RectTransform rectTransform = animatablePanel.GetComponent<RectTransform>();
    Vector2 finalPos = originalDetailPanelPositions[panel]; // Still use panel as key for position lookup
    rectTransform.anchoredPosition = finalPos;
    
    // NOW activate panel - it's already in the correct position
    animatablePanel.SetActive(true);
    
    // Start position is offset to the right by slideStartOffset
    Vector2 startPos = new Vector2(finalPos.x + slideStartOffset, finalPos.y);
    
    // Set starting position for animation
    rectTransform.anchoredPosition = startPos;
    
    float elapsedTime = 0f;
    
    while (elapsedTime < slideAnimationDuration)
    {
        elapsedTime += Time.deltaTime;
        float progress = elapsedTime / slideAnimationDuration;
        float curveValue = slideCurve.Evaluate(progress);
        
        rectTransform.anchoredPosition = Vector2.Lerp(startPos, finalPos, curveValue);
        yield return null;
    }
    
    rectTransform.anchoredPosition = finalPos;
    
    // Animation completed successfully
    isDetailPanelAnimating = false;
    currentDetailAnimation = null;
    
    // Update the detail panel content and equip button
    PopulateDetailPanel(item);
}
    
    IEnumerator SlideOutDetailPanel(GameObject panel)
{
    isDetailPanelAnimating = true;
    
    // Get the correct GameObject to animate
    GameObject animatablePanel = GetAnimatablePanel(panel);
    
    RectTransform rectTransform = animatablePanel.GetComponent<RectTransform>();
    Vector2 startPos = rectTransform.anchoredPosition; // Current position
    
    // Get the original position to calculate the proper offset
    Vector2 originalPos = originalDetailPanelPositions[panel]; // Still use panel as key
    Vector2 endPos = new Vector2(originalPos.x + slideStartOffset, originalPos.y); // Slide right by offset from original position
    
    float elapsedTime = 0f;
    
    while (elapsedTime < slideAnimationDuration)
    {
        elapsedTime += Time.deltaTime;
        float progress = elapsedTime / slideAnimationDuration;
        float curveValue = slideCurve.Evaluate(progress);
        
        rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, curveValue);
        yield return null;
    }
    
    rectTransform.anchoredPosition = endPos;
    animatablePanel.SetActive(false);
    
    // Animation completed successfully
    currentActiveDetailPanel = null;
    isDetailPanelAnimating = false;
    currentDetailAnimation = null;
}
    
    void PopulateDetailPanel(EquipableItem item)
{
    // Update equip button only
    UpdateEquipButton();
}
    
    IEnumerator SwitchDetailPanels(GameObject oldPanel, GameObject newPanel, EquipableItem item)
{
    isDetailPanelAnimating = true;
    
    // Get the correct GameObjects to animate
    GameObject oldAnimatablePanel = GetAnimatablePanel(oldPanel);
    GameObject newAnimatablePanel = GetAnimatablePanel(newPanel);
    
    // Activate new panel and position it off-screen to the right
    newAnimatablePanel.SetActive(true);
    RectTransform newRect = newAnimatablePanel.GetComponent<RectTransform>();
    RectTransform oldRect = oldAnimatablePanel.GetComponent<RectTransform>();
    
    // Get positions (still use panel as key for position lookup)
    Vector2 newFinalPos = originalDetailPanelPositions[newPanel];
    Vector2 newStartPos = new Vector2(newFinalPos.x + slideStartOffset, newFinalPos.y);
    
    Vector2 oldStartPos = oldRect.anchoredPosition; // Current position of old panel
    Vector2 oldEndPos = new Vector2(originalDetailPanelPositions[oldPanel].x + slideStartOffset, originalDetailPanelPositions[oldPanel].y);
    
    // Set new panel starting position
    newRect.anchoredPosition = newStartPos;
    
    float elapsedTime = 0f;
    
    // Animate both panels simultaneously
    while (elapsedTime < slideAnimationDuration)
    {
        elapsedTime += Time.deltaTime;
        float progress = elapsedTime / slideAnimationDuration;
        float curveValue = slideCurve.Evaluate(progress);
        
        // New panel slides in from right
        newRect.anchoredPosition = Vector2.Lerp(newStartPos, newFinalPos, curveValue);
        
        // Old panel slides out to right
        oldRect.anchoredPosition = Vector2.Lerp(oldStartPos, oldEndPos, curveValue);
        
        yield return null;
    }
    
    // Finalize positions
    newRect.anchoredPosition = newFinalPos;
    oldRect.anchoredPosition = oldEndPos;
    
    // Deactivate old panel and update reference
    oldAnimatablePanel.SetActive(false);
    currentActiveDetailPanel = newPanel; // Still track the actual panel, not the animatable one
    
    // Animation completed successfully
    isDetailPanelAnimating = false;
    currentDetailAnimation = null;
    
    // Update the detail panel content and equip button
    PopulateDetailPanel(item);
}
    
    void UpdateAllCardStates()
    {
        // Update all item cards to reflect which one has detail panel open
        foreach (GameObject cardObj in currentItemCards)
        {
            // For exotic items, the cardObj is the wrapper, so we need to find the actual card inside
            EquipItemCardUI cardUI = cardObj.GetComponent<EquipItemCardUI>();
            if (cardUI == null && cardObj.name == "ExoticWrapper")
            {
                // This is an exotic wrapper, find the card UI inside
                cardUI = cardObj.GetComponentInChildren<EquipItemCardUI>();
            }
            
            if (cardUI != null)
            {
                bool isThisCardOpen = (currentDetailItem != null && cardUI.GetAssociatedItem() == currentDetailItem);
                cardUI.SetDetailPanelState(isThisCardOpen);
            }
        }
    }
    
    string GenerateStatsText(EquipableItem item)
    {
        // Simplified stats for debug only
        return $"Item: {item.itemName}\nCategory: {item.category}\nRarity: {item.rarity}";
    }
    
      void Show3DModelInDetail(EquipableItem item)
{
    // Removed 3D model functionality for now
}
    
    void SpawnEquippedItem(EquipableItem item)
{
    if (PlayerEquipment.instance != null)
    {
        PlayerEquipment.instance.EquipItem(item);
    }
}
    
    void RemoveEquippedItem(EquipableItem item)
{
    if (PlayerEquipment.instance != null)
    {
        PlayerEquipment.instance.UnequipItem(item);
    }
}
    
    public void AddEquipment(EquipableItem newItem)
    {
        playerEquipment.Add(newItem);
        RefreshEquipmentDisplay();
    }
    
    void AddSampleEquipment()
    {
        // Add a sample exotic item for testing
        AddEquipment(new EquipableItem { 
            itemName = "Exotic Test Sword", 
            itemDescription = "A test exotic weapon",
            category = EquipmentCategory.MeleeWeapon, 
            rarity = EquipmentRarity.Exotic,
            damage = 100,
            attackSpeed = 1.5f
        });
        
        // NEW: Add sample booster shoes for testing
        AddEquipment(new EquipableItem {
            itemName = "Booster Shoes",
            itemDescription = "Red blocks that go on your feet and make you go faster",
            category = EquipmentCategory.Accessory,
            rarity = EquipmentRarity.Rare,
            speedBonus = 2.0f,
            specialAbilities = new string[] { "Increased Movement Speed", "Enhanced Jumping" }
        });
    }
    
    GameObject GetPrefabForRarity(EquipmentRarity rarity)
    {
        switch (rarity)
        {
            case EquipmentRarity.Common:
                return equipItemCardPrefabC;
            case EquipmentRarity.Rare:
                return equipItemCardPrefabR;
            case EquipmentRarity.Legendary:
                return equipItemCardPrefabL;
            case EquipmentRarity.Exotic:
                return equipItemCardPrefabE;
            default:
                return equipItemCardPrefabC; // Fallback to common
        }
    }

    GameObject GetAnimatablePanel(GameObject detailPanel)
{
    if (detailPanel == itemDetailPanelE && exoticDetailRainbowBackground != null)
    {
        return exoticDetailRainbowBackground;
    }
    return detailPanel;
}
    
    GameObject GetDetailPanelForRarity(EquipmentRarity rarity)
    {
        switch (rarity)
        {
            case EquipmentRarity.Common:
                return itemDetailPanelC;
            case EquipmentRarity.Rare:
                return itemDetailPanelR;
            case EquipmentRarity.Legendary:
                return itemDetailPanelL;
            case EquipmentRarity.Exotic:
                return itemDetailPanelE;
            default:
                return itemDetailPanelC; // Fallback to common
        }
    }
}