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
        // REPLACE the default booster shoes with this item's model
        
        // First, clear any existing children (the default booster shoes)
        for (int i = model3DContainer.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(model3DContainer.GetChild(i).gameObject);
            else
                DestroyImmediate(model3DContainer.GetChild(i).gameObject);
        }
        
        // Now instantiate the item's actual 3D model
        instantiated3DModel = Instantiate(associatedItem.item3DModel, model3DContainer);
        
        // Make sure it's positioned correctly
        instantiated3DModel.transform.localPosition = Vector3.zero;
        instantiated3DModel.transform.localRotation = Quaternion.identity;
        instantiated3DModel.transform.localScale = Vector3.one;
        
        // Make sure the container is visible
        model3DContainer.gameObject.SetActive(true);
    }
    else
    {
        // NO MODEL ASSIGNED - Hide the entire container so no booster shoes show
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
        if (instantiated3DModel != null && instantiated3DModel != null) // Check twice to prevent error
        {
            if (Application.isPlaying)
                Destroy(instantiated3DModel);
            else
                DestroyImmediate(instantiated3DModel);
        }
        
        // Clean up rainbow background when card is destroyed
        if (rainbowBackground != null && rainbowBackground != null) // Check twice to prevent error
        {
            if (Application.isPlaying)
                Destroy(rainbowBackground);
            else
                DestroyImmediate(rainbowBackground);
        }
    }
}