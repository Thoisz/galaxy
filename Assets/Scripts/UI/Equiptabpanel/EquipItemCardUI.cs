// === REPLACE YOUR EXISTING EquipItemCardUI CLASS ===

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EquipItemCardUI : MonoBehaviour
{
    [Header("UI Components")]
    public Button cardButton;
    public Image itemIconImage; // The Image component that shows the 2D icon
    public Transform model3DContainer; // Where the 3D model will be placed (for 3D preview if needed)
    
    private EquipableItem associatedItem;
    private EquipmentManager equipmentManager;
    private GameObject instantiated3DModel;
    private bool isDetailPanelOpen = false;
    private GameObject rainbowBackground; // Reference to the rainbow background
    
    public void SetupItemCard(EquipableItem item, EquipmentManager manager)
    {
        associatedItem = item;
        equipmentManager = manager;
        
        // Setup 2D icon
        Setup2DIcon();
        
        // Setup 3D model (optional - for 3D preview)
        Setup3DModel();
        
        // Set up button click
        if (cardButton != null)
        {
            cardButton.onClick.RemoveAllListeners();
            cardButton.onClick.AddListener(OnCardClicked);
        }
    }
    
    void Setup2DIcon()
    {
        if (itemIconImage == null) return;
        
        // Check if this item has a 2D icon assigned
        if (associatedItem != null && associatedItem.itemIcon != null)
        {
            // Set the 2D sprite
            itemIconImage.sprite = associatedItem.itemIcon;
            itemIconImage.gameObject.SetActive(true);
        }
        else
        {
            // No icon assigned - hide the image
            itemIconImage.gameObject.SetActive(false);
        }
    }
    
    void Setup3DModel()
    {
        // Clear any existing instantiated model first
        if (instantiated3DModel != null)
        {
            DestroyImmediate(instantiated3DModel);
            instantiated3DModel = null;
        }
        
        if (model3DContainer == null) return;
        
        // Check if this item has a 3D model assigned
        if (associatedItem != null && associatedItem.item3DModel != null)
        {
            // Clear any existing children (placeholder models)
            for (int i = model3DContainer.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(model3DContainer.GetChild(i).gameObject);
                else
                    DestroyImmediate(model3DContainer.GetChild(i).gameObject);
            }
            
            // Instantiate the item's actual 3D model (optional for 3D preview)
            instantiated3DModel = Instantiate(associatedItem.item3DModel, model3DContainer);
            instantiated3DModel.transform.localPosition = Vector3.zero;
            instantiated3DModel.transform.localRotation = Quaternion.identity;
            instantiated3DModel.transform.localScale = Vector3.one;
            
            model3DContainer.gameObject.SetActive(true);
        }
        else
        {
            // NO MODEL ASSIGNED - Hide the 3D container
            if (model3DContainer != null)
                model3DContainer.gameObject.SetActive(false);
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
            if (Application.isPlaying)
                Destroy(instantiated3DModel);
            else
                DestroyImmediate(instantiated3DModel);
        }
        
        // Clean up rainbow background when card is destroyed
        if (rainbowBackground != null)
        {
            if (Application.isPlaying)
                Destroy(rainbowBackground);
            else
                DestroyImmediate(rainbowBackground);
        }
    }
}