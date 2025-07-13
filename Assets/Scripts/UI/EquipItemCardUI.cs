using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class EquipItemCardUI : MonoBehaviour
{
    [Header("UI Components")]
    public Button cardButton;
    public Transform model3DContainer; // Where the 3D model will be placed (for static image)
    
    [Header("Mythical Animation")]
    public float rainbowSpeed = 2f;
    public float shimmerSpeed = 1f;
    
    private EquipableItem associatedItem;
    private EquipmentManager equipmentManager;
    private GameObject instantiated3DModel;
    private bool isMythical = false;
    private bool isDetailPanelOpen = false;
    
    public void SetupItemCard(EquipableItem item, EquipmentManager manager)
    {
        associatedItem = item;
        equipmentManager = manager;
        
        // Set rarity color effects (you can apply this to button or other elements)
        SetupRarityVisuals();
        
        // Setup 3D model for static image capture (implement later)
        Setup3DModel();
        
        // Set up button click
        if (cardButton != null)
        {
            cardButton.onClick.RemoveAllListeners();
            cardButton.onClick.AddListener(OnCardClicked);
        }
    }
    
    void SetupRarityVisuals()
    {
        Color targetColor = equipmentManager.GetItemRarityColor(associatedItem.rarity);
        
        if (associatedItem.rarity == EquipmentRarity.Mythical)
        {
            isMythical = true;
            StartCoroutine(AnimateRainbowEffect());
        }
        else
        {
            isMythical = false;
            // Apply rarity color to button and disable hover effects
            if (cardButton != null)
            {
                ColorBlock colors = cardButton.colors;
                colors.normalColor = targetColor;
                colors.highlightedColor = targetColor; // Same as normal (no hover effect)
                colors.selectedColor = targetColor;    // Same as normal
                colors.pressedColor = targetColor * 0.8f; // Slightly darker when pressed
                cardButton.colors = colors;
            }
        }
    }
    
    void Setup3DModel()
    {
        // Clear any existing model
        if (instantiated3DModel != null)
        {
            DestroyImmediate(instantiated3DModel);
        }
        
        // For now, this will just be a placeholder for the static image
        // Later we can implement actual 3D model capture for the button image
        if (associatedItem.item3DModel != null && model3DContainer != null)
        {
            // This is where you'd set up the model for image capture
            // For now, we'll leave this empty and use placeholder images
            Debug.Log($"Would setup 3D model image for {associatedItem.itemName}");
        }
    }
    
    IEnumerator AnimateRainbowEffect()
    {
        while (isMythical && cardButton != null)
        {
            float hue = (Time.time * rainbowSpeed) % 1f;
            Color rainbowColor = Color.HSVToRGB(hue, 1f, 1f);
            
            // Add shimmer effect
            float shimmer = Mathf.Sin(Time.time * shimmerSpeed) * 0.3f + 0.7f;
            rainbowColor.a = shimmer;
            
            // Apply to button and disable hover for mythical too
            ColorBlock colors = cardButton.colors;
            colors.normalColor = rainbowColor;
            colors.highlightedColor = rainbowColor; // No hover effect
            colors.selectedColor = rainbowColor;
            colors.pressedColor = rainbowColor * 0.8f;
            cardButton.colors = colors;
            
            yield return null;
        }
    }
    
    void OnCardClicked()
    {
        // Always try to show this item's detail - let EquipmentManager handle the logic
        equipmentManager.ShowItemDetail(associatedItem);
    }
    
    public void SetDetailPanelState(bool isOpen)
    {
        isDetailPanelOpen = isOpen;
    }
    
    public EquipableItem GetAssociatedItem()
    {
        return associatedItem;
    }
    
    void OnDestroy()
    {
        // Clean up the 3D model when card is destroyed
        if (instantiated3DModel != null)
        {
            DestroyImmediate(instantiated3DModel);
        }
    }
}