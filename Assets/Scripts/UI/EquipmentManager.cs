using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

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
    Legend,    // Purple
    Mythical   // Animated rainbow
}

public class EquipmentManager : MonoBehaviour
{
    [Header("Equipment Database")]
    public List<EquipableItem> playerEquipment = new List<EquipableItem>();
    
    [Header("UI References")]
    public Transform itemsContainer; // The Content object of the ScrollView
    public GameObject equipItemCardPrefabC; // Common rarity prefab
    public GameObject equipItemCardPrefabR; // Rare rarity prefab
    public GameObject equipItemCardPrefabL; // Legend rarity prefab
    public GameObject equipItemCardPrefabM; // Mythical rarity prefab
    public TMP_Dropdown filterDropdown;
    public TMP_InputField searchInputField;
    public ScrollRect itemsScrollRect;
    public Button equipPanelCloseButton; // Close button for this specific panel
    
    [Header("Currently Equipped")]
    public EquipableItem equippedMeleeWeapon;
    public EquipableItem equippedRangedWeapon;
    public EquipableItem equippedAccessory;
    
    [Header("Rarity Colors")]
    public Color commonColor = Color.blue;
    public Color rareColor = new Color(1f, 0.5f, 0f); // Orange
    public Color legendColor = Color.magenta;
    
    [Header("Detail Panel References")]
    public GameObject itemDetailPanelC; // Common rarity detail panel
    public GameObject itemDetailPanelR; // Rare rarity detail panel
    public GameObject itemDetailPanelL; // Legend rarity detail panel
    public GameObject itemDetailPanelM; // Mythical rarity detail panel
    public Image detailPanelBackground; // The background image of the detail panel
    public Transform detailPanel3DContainer; // Where to show the 3D model
    public TextMeshProUGUI detailItemName;
    public TextMeshProUGUI detailItemStats;
    public Button equipButton;
    
    private List<EquipableItem> filteredItems;
    private List<GameObject> currentItemCards = new List<GameObject>();
    private EquipableItem currentDetailItem; // Track which item's detail is open
    
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
        // Make sure all detail panels start closed
        if (itemDetailPanelC != null)
            itemDetailPanelC.SetActive(false);
        if (itemDetailPanelR != null)
            itemDetailPanelR.SetActive(false);
        if (itemDetailPanelL != null)
            itemDetailPanelL.SetActive(false);
        if (itemDetailPanelM != null)
            itemDetailPanelM.SetActive(false);
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
        // Close the entire menu system instead of going back to tab selection
        MenuManager menuManager = FindObjectOfType<MenuManager>();
        if (menuManager != null)
        {
            menuManager.CloseMenu(); // This closes everything
        }
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
            // Get the correct prefab based on rarity
            GameObject prefabToUse = GetPrefabForRarity(filteredItems[i].rarity);
            
            GameObject itemCard = Instantiate(prefabToUse, itemsContainer);
            EquipItemCardUI itemCardUI = itemCard.GetComponent<EquipItemCardUI>();
            
            if (itemCardUI != null)
            {
                itemCardUI.SetupItemCard(filteredItems[i], this);
            }
            
            currentItemCards.Add(itemCard);
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
        // Get the correct detail panel based on rarity
        GameObject currentDetailPanel = GetDetailPanelForRarity(item.rarity);
        
        // If the same item is clicked, close the detail panel
        if (currentDetailItem == item && currentDetailPanel != null && currentDetailPanel.activeSelf)
        {
            CloseItemDetail();
            return;
        }
        
        // Update current detail item
        currentDetailItem = item;
        
        // Update all card states
        UpdateAllCardStates();
        
        // Close all detail panels first
        CloseAllDetailPanels();
        
        if (currentDetailPanel != null)
        {
            currentDetailPanel.SetActive(true);
            
            // Color the detail panel based on item rarity
            if (detailPanelBackground != null)
            {
                Color rarityColor = GetRarityColor(item.rarity);
                detailPanelBackground.color = rarityColor;
            }
            
            // Populate detail panel with item info
            if (detailItemName != null)
                detailItemName.text = item.itemName;
            
            if (detailItemStats != null)
                detailItemStats.text = GenerateStatsText(item);
            
            // Setup equip button
            if (equipButton != null)
            {
                equipButton.onClick.RemoveAllListeners();
                
                if (item.isEquipped)
                {
                    equipButton.onClick.AddListener(() => UnequipItem(item));
                }
                else
                {
                    equipButton.onClick.AddListener(() => EquipItem(item));
                }
                
                // Change button text based on equipped status
                TextMeshProUGUI buttonText = equipButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                {
                    buttonText.text = item.isEquipped ? "UNEQUIP" : "EQUIP";
                }
            }
            
            // Show 3D model in detail panel (implement later)
            Show3DModelInDetail(item);
        }
    }
    
    public void CloseItemDetail()
    {
        currentDetailItem = null;
        UpdateAllCardStates();
        
        CloseAllDetailPanels();
    }
    
    void CloseAllDetailPanels()
    {
        if (itemDetailPanelC != null)
            itemDetailPanelC.SetActive(false);
        if (itemDetailPanelR != null)
            itemDetailPanelR.SetActive(false);
        if (itemDetailPanelL != null)
            itemDetailPanelL.SetActive(false);
        if (itemDetailPanelM != null)
            itemDetailPanelM.SetActive(false);
    }
    
    void UpdateAllCardStates()
    {
        // Update all item cards to reflect which one has detail panel open
        foreach (GameObject cardObj in currentItemCards)
        {
            EquipItemCardUI cardUI = cardObj.GetComponent<EquipItemCardUI>();
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
        // Sample equipment section - remove or modify as needed
        // AddEquipment(new EquipableItem { 
        //     itemName = "Sample Item", 
        //     itemDescription = "Sample description",
        //     category = EquipmentCategory.Accessory, 
        //     rarity = EquipmentRarity.Common
        // });
    }
    
    Color GetRarityColor(EquipmentRarity rarity)
    {
        switch (rarity)
        {
            case EquipmentRarity.Common:
                return commonColor;
            case EquipmentRarity.Rare:
                return rareColor;
            case EquipmentRarity.Legend:
                return legendColor;
            case EquipmentRarity.Mythical:
                // Don't apply any color - leave texture alone
                return Color.white; // White = no tint, shows original texture
            default:
                return commonColor;
        }
    }
    
    // Public method so item cards can get the correct color
    public Color GetItemRarityColor(EquipmentRarity rarity)
    {
        return GetRarityColor(rarity);
    }
    
    GameObject GetPrefabForRarity(EquipmentRarity rarity)
    {
        switch (rarity)
        {
            case EquipmentRarity.Common:
                return equipItemCardPrefabC;
            case EquipmentRarity.Rare:
                return equipItemCardPrefabR;
            case EquipmentRarity.Legend:
                return equipItemCardPrefabL;
            case EquipmentRarity.Mythical:
                return equipItemCardPrefabM;
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
            case EquipmentRarity.Legend:
                return itemDetailPanelL;
            case EquipmentRarity.Mythical:
                return itemDetailPanelM;
            default:
                return itemDetailPanelC; // Fallback to common
        }
    }
}