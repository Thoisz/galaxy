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
    
    [Header("3D Model")]
    public GameObject item3DModel; // The 3D model for spinning render
    
    [Header("Weapon Stats (if applicable)")]
    public int damage = 0;
    public float attackSpeed = 1f;
    public string[] moveset; // Array of move names
    
    [Header("Accessory Bonuses (if applicable)")]
    public int healthBonus = 0;
    public int staminaBonus = 0;
    public string[] specialAbilities; // Array of special bonus descriptions
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
    public GameObject itemDetailPanelE; // Exotic rarity detail panel (changed from M)
    public Image detailPanelBackground; // The background image of the detail panel
    public Transform detailPanel3DContainer; // Where to show the 3D model
    public TextMeshProUGUI detailItemName;
    public TextMeshProUGUI detailItemStats;
    public Button equipButton;
    
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
        
        // Add some sample equipment for testing
        AddSampleEquipment();
        
        RefreshEquipmentDisplay();
    }
    
    void OnEnable()
    {
        // Call this whenever the EquipmentManager GameObject becomes active
        // This ensures we reset detail panels when the equip tab opens
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(ResetDetailPanelsAfterFrame());
        }
    }
    
    IEnumerator ResetDetailPanelsAfterFrame()
    {
        // Wait a frame to ensure any ongoing animations are processed
        yield return null;
        yield return null; // Wait an extra frame to be safe
        
        // Only reset if we don't currently have an active detail panel
        if (currentActiveDetailPanel == null || !currentActiveDetailPanel.activeSelf)
        {
            ResetAllDetailPanels();
            
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
        filterDropdown.ClearOptions();
        
        List<string> filterOptions = new List<string> { "All" };
        foreach (EquipmentCategory category in System.Enum.GetValues(typeof(EquipmentCategory)))
        {
            filterOptions.Add(category.ToString());
        }
        
        filterDropdown.AddOptions(filterOptions);
        filterDropdown.onValueChanged.AddListener(OnFilterChanged);
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
        if (itemDetailPanelE != null)
        {
            originalDetailPanelPositions[itemDetailPanelE] = itemDetailPanelE.GetComponent<RectTransform>().anchoredPosition + positionOffset;
            itemDetailPanelE.SetActive(false);
        }
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
        else
        {
            Debug.LogWarning("MenuManager not found! Cannot close equipment panel properly.");
        }
    }
    
    // PUBLIC METHOD: Call this when all menus are being closed (B key, menu button, etc.)
    public void OnAllMenusClosed()
    {
        // When all menus are closed, use coordinated movement for detail panels
        if (currentActiveDetailPanel != null && currentActiveDetailPanel.activeSelf)
        {
            CloseDetailPanelCoordinated();
        }
    }
    
    // NEW METHOD: Close detail panel with coordinated movement
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
    
    // COROUTINE: Slide panel right + down with hardcoded optimal settings
    IEnumerator SlideCoordinatedDetailPanel(GameObject panel)
    {
        isDetailPanelAnimating = true;
        
        RectTransform rectTransform = panel.GetComponent<RectTransform>();
        Vector2 startPos = rectTransform.anchoredPosition; // Current position
        
        // Use hardcoded optimal settings
        float animDuration = 0.5f; // Custom Coordinated Duration
        AnimationCurve animCurve = slideCurve; // Use our slide curve
        
        // Calculate end position
        Vector2 originalPos = originalDetailPanelPositions[panel];
        
        // Use hardcoded optimal down speed settings
        Canvas canvas = GetComponentInParent<Canvas>();
        float canvasHeight = canvas.GetComponent<RectTransform>().rect.height;
        MenuManager menuManager = FindObjectOfType<MenuManager>();
        float menuManagerDownDistance = (canvasHeight / 2) + (menuManager != null ? menuManager.offScreenOffset : 200f);
        float downMovement = startPos.y - (menuManagerDownDistance * 2.2f); // Custom Down Speed Multiplier
        
        Vector2 endPos = new Vector2(
            originalPos.x + slideStartOffset,  // Right movement (same as normal)
            downMovement // Down movement with multiplier
        );
        
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
        panel.SetActive(false);
        
        // DON'T reset panel position here - it will be reset when the panel is properly opened next time
        // This prevents the ghost sliding effect when switching between different rarity panels
        
        // Animation completed successfully
        currentActiveDetailPanel = null;
        isDetailPanelAnimating = false;
        currentDetailAnimation = null;
        
        Debug.Log($"Detail panel closed with coordinated movement (Duration: {animDuration}, Down multiplier: 2.2)");
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
    }
    
    void FilterEquipment()
    {
        int selectedIndex = filterDropdown.value;
        string searchText = searchInputField.text.ToLower();
        
        filteredItems = playerEquipment.Where(item =>
        {
            // Filter by category
            bool categoryMatch = selectedIndex == 0; // "All" is index 0
            if (!categoryMatch && selectedIndex > 0)
            {
                EquipmentCategory selectedCategory = (EquipmentCategory)(selectedIndex - 1);
                categoryMatch = item.category == selectedCategory;
            }
            
            // Filter by search text
            bool searchMatch = string.IsNullOrEmpty(searchText) || 
                             item.itemName.ToLower().Contains(searchText);
            
            return categoryMatch && searchMatch;
        }).ToList();
    }
    
    void DisplayFilteredEquipment()
    {
        for (int i = 0; i < filteredItems.Count; i++)
        {
            GameObject prefabToUse = GetPrefabForRarity(filteredItems[i].rarity);
            
            // For exotic items, create a wrapper to hold both background and button
            if (filteredItems[i].rarity == EquipmentRarity.Exotic && rainbowBackgroundPrefab != null)
            {
                // Create wrapper GameObject
                GameObject wrapper = new GameObject("ExoticWrapper");
                wrapper.transform.SetParent(itemsContainer, false); // Important: worldPositionStays = false
                
                // Add RectTransform first
                RectTransform wrapperRect = wrapper.AddComponent<RectTransform>();
                
                // Copy layout properties from your button prefab
                RectTransform buttonRect = prefabToUse.GetComponent<RectTransform>();
                if (buttonRect != null)
                {
                    wrapperRect.sizeDelta = buttonRect.sizeDelta;
                    
                    // Copy anchor settings
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
                currentItemCards.Add(wrapper); // Track the wrapper, not the individual card
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
        
        Debug.Log($"Equipped: {item.itemName}");
        
        // Close detail panel and refresh display
        CloseItemDetail();
        RefreshEquipmentDisplay();
        
        // Here you would spawn the actual item model on the player
        SpawnEquippedItem(item);
    }
    
    public void UnequipItem(EquipableItem item)
    {
        item.isEquipped = false;
        
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
        
        Debug.Log($"Unequipped: {item.itemName}");
        CloseItemDetail();
        RefreshEquipmentDisplay();
        
        // Here you would remove the item model from the player
        RemoveEquippedItem(item);
    }
    
    public void ShowItemDetail(EquipableItem item)
    {
        // FIRST: Always reset ALL detail panels before opening any new one
        // This prevents ghost panels from previous coordinated closes
        ForceResetAllDetailPanels();
        
        // Get the correct detail panel based on rarity
        GameObject targetDetailPanel = GetDetailPanelForRarity(item.rarity);
        
        // If the same item is clicked during animation, reverse/cancel the slide
        if (currentDetailItem == item && isDetailPanelAnimating && currentDetailAnimation != null)
        {
            StopCoroutine(currentDetailAnimation);
            isDetailPanelAnimating = false;
            currentDetailAnimation = null;
            
            // Reverse slide - close the panel that was sliding in
            CloseItemDetail();
            return;
        }
        
        // If the same item is clicked when panel is fully open, close it
        if (currentDetailItem == item && currentActiveDetailPanel != null && currentActiveDetailPanel.activeSelf && !isDetailPanelAnimating)
        {
            CloseItemDetail();
            return;
        }
        
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
            // Since we reset everything above, we can always just slide in normally
            currentActiveDetailPanel = targetDetailPanel;
            currentDetailAnimation = StartCoroutine(SlideInDetailPanel(targetDetailPanel, item));
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
        
        // Clear all state
        currentDetailItem = null;
        currentActiveDetailPanel = null;
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
        
        // IMPORTANT: Reset to original position BEFORE activating to prevent ghost sliding
        RectTransform rectTransform = panel.GetComponent<RectTransform>();
        Vector2 finalPos = originalDetailPanelPositions[panel];
        rectTransform.anchoredPosition = finalPos;
        
        // NOW activate panel - it's already in the correct position
        panel.SetActive(true);
        
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
        
        // Populate detail panel with item info (for testing, just log)
        Debug.Log($"Detail panel opened for {item.itemName} (Rarity: {item.rarity})");
    }
    
    IEnumerator SlideOutDetailPanel(GameObject panel)
    {
        isDetailPanelAnimating = true;
        
        RectTransform rectTransform = panel.GetComponent<RectTransform>();
        Vector2 startPos = rectTransform.anchoredPosition; // Current position
        
        // Get the original position to calculate the proper offset
        Vector2 originalPos = originalDetailPanelPositions[panel];
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
        panel.SetActive(false);
        
        // Animation completed successfully
        currentActiveDetailPanel = null;
        isDetailPanelAnimating = false;
        currentDetailAnimation = null;
        
        Debug.Log("Detail panel closed and hidden");
    }
    
    IEnumerator SwitchDetailPanels(GameObject oldPanel, GameObject newPanel, EquipableItem item)
    {
        isDetailPanelAnimating = true;
        
        // Activate new panel and position it off-screen to the right
        newPanel.SetActive(true);
        RectTransform newRect = newPanel.GetComponent<RectTransform>();
        RectTransform oldRect = oldPanel.GetComponent<RectTransform>();
        
        // Get positions
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
        oldPanel.SetActive(false);
        currentActiveDetailPanel = newPanel;
        
        // Animation completed successfully
        isDetailPanelAnimating = false;
        currentDetailAnimation = null;
        
        // Populate detail panel with item info (for testing, just log)
        Debug.Log($"Switched to detail panel for {item.itemName} (Rarity: {item.rarity})");
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
        string statsText = "";
        
        if (item.category == EquipmentCategory.MeleeWeapon || item.category == EquipmentCategory.RangedWeapon)
        {
            statsText += $"Damage: {item.damage}\n";
            statsText += $"Attack Speed: {item.attackSpeed}\n\n";
            
            if (item.moveset != null && item.moveset.Length > 0)
            {
                statsText += "Moveset:\n";
                foreach (string move in item.moveset)
                {
                    statsText += $"• {move}\n";
                }
            }
        }
        else if (item.category == EquipmentCategory.Accessory)
        {
            if (item.healthBonus > 0)
                statsText += $"Health: +{item.healthBonus}\n";
            if (item.staminaBonus > 0)
                statsText += $"Stamina: +{item.staminaBonus}\n";
            
            if (item.specialAbilities != null && item.specialAbilities.Length > 0)
            {
                statsText += "\nSpecial Abilities:\n";
                foreach (string ability in item.specialAbilities)
                {
                    statsText += $"• {ability}\n";
                }
            }
        }
        
        return statsText;
    }
    
    void Show3DModelInDetail(EquipableItem item)
    {
        // Clear existing model in detail panel
        if (detailPanel3DContainer != null)
        {
            foreach (Transform child in detailPanel3DContainer)
            {
                DestroyImmediate(child.gameObject);
            }
        }
        
        // Instantiate new model (we'll implement this properly later)
        if (item.item3DModel != null && detailPanel3DContainer != null)
        {
            GameObject detailModel = Instantiate(item.item3DModel, detailPanel3DContainer);
            detailModel.transform.localPosition = Vector3.zero;
            detailModel.transform.localScale = Vector3.one;
            // Add spinning animation here too
        }
    }
    
    void SpawnEquippedItem(EquipableItem item)
    {
        // This is where you'd instantiate the actual 3D model on your player
        // For now, just debug log
        Debug.Log($"Would spawn {item.itemName} on player");
    }
    
    void RemoveEquippedItem(EquipableItem item)
    {
        // This is where you'd remove the 3D model from your player
        Debug.Log($"Would remove {item.itemName} from player");
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