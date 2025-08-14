using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EquipItemCardUI : MonoBehaviour
{
    [Header("UI Components")]
    public Button cardButton;
    public Transform model3DContainer; // Where the 3D model will be placed (for static image)
    
    private EquipableItem associatedItem;
    private EquipmentManager equipmentManager;
    private GameObject instantiated3DModel;
    private bool isDetailPanelOpen = false;
    private GameObject rainbowBackground; // Reference to the rainbow background
    
    public void SetupItemCard(EquipableItem item, EquipmentManager manager)
    {
        associatedItem = item;
        equipmentManager = manager;
        
        // Setup 3D model for static image capture (implement later)
        Setup3DModel();
        
        // Set up button click
        if (cardButton != null)
        {
            cardButton.onClick.RemoveAllListeners();
            cardButton.onClick.AddListener(OnCardClicked);
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
    
    public void SetRainbowBackground(GameObject rainbowBG)
    {
        rainbowBackground = rainbowBG;
        
        // Position rainbow background to match this card
        if (rainbowBackground != null)
        {
            RectTransform rainbowRect = rainbowBackground.GetComponent<RectTransform>();
            RectTransform cardRect = GetComponent<RectTransform>();
            
            if (rainbowRect != null && cardRect != null)
            {
                // Match position, size, and anchors
                rainbowRect.anchorMin = cardRect.anchorMin;
                rainbowRect.anchorMax = cardRect.anchorMax;
                rainbowRect.anchoredPosition = cardRect.anchoredPosition;
                rainbowRect.sizeDelta = cardRect.sizeDelta;
                
                // Make sure rainbow is behind this card
                rainbowBackground.transform.SetSiblingIndex(transform.GetSiblingIndex() - 1);
            }
        }
    }
    
    void Update()
    {
        // Keep rainbow background synchronized with this card's position
        if (rainbowBackground != null)
        {
            RectTransform rainbowRect = rainbowBackground.GetComponent<RectTransform>();
            RectTransform cardRect = GetComponent<RectTransform>();
            
            if (rainbowRect != null && cardRect != null)
            {
                rainbowRect.anchoredPosition = cardRect.anchoredPosition;
                rainbowRect.sizeDelta = cardRect.sizeDelta;
            }
        }
    }
    
    void OnDestroy()
    {
        // Clean up the 3D model when card is destroyed
        if (instantiated3DModel != null)
        {
            DestroyImmediate(instantiated3DModel);
        }
        
        // Clean up rainbow background when card is destroyed
        if (rainbowBackground != null)
        {
            DestroyImmediate(rainbowBackground);
        }
    }
}