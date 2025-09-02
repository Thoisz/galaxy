using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EquipItemCardUI : MonoBehaviour
{
    [Header("UI Components")]
    public Button cardButton;
    public Image itemIconImage;          // 2D icon
    public Transform model3DContainer;   // Optional 3D preview parent

    private EquipableItem associatedItem;
    private EquipmentManager equipmentManager;
    private GameObject instantiated3DModel;
    private bool isDetailPanelOpen = false;
    private GameObject rainbowBackground;

    public void SetupItemCard(EquipableItem item, EquipmentManager manager)
    {
        associatedItem = item;
        equipmentManager = manager;

        Setup2DIcon();
        Setup3DModel();

        if (cardButton != null)
        {
            cardButton.onClick.RemoveAllListeners();
            cardButton.onClick.AddListener(OnCardClicked);
        }
    }

    void Setup2DIcon()
    {
        if (!itemIconImage) return;

        if (associatedItem != null && associatedItem.itemIcon != null)
        {
            itemIconImage.sprite = associatedItem.itemIcon;
            itemIconImage.gameObject.SetActive(true);
        }
        else
        {
            itemIconImage.gameObject.SetActive(false);
        }
    }

    // --- NEW: use the first attachment prefab instead of the removed item3DModel ---
    void Setup3DModel()
    {
        // Clear any existing preview
        if (instantiated3DModel != null)
        {
            DestroyImmediate(instantiated3DModel);
            instantiated3DModel = null;
        }

        if (!model3DContainer) return;

        GameObject previewPrefab = GetPrimaryAttachmentPrefab(associatedItem);

        if (previewPrefab != null)
        {
            // Clear placeholder children
            for (int i = model3DContainer.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(model3DContainer.GetChild(i).gameObject);
                else
                    DestroyImmediate(model3DContainer.GetChild(i).gameObject);
            }

            instantiated3DModel = Instantiate(previewPrefab, model3DContainer);
            instantiated3DModel.transform.localPosition = Vector3.zero;
            instantiated3DModel.transform.localRotation = Quaternion.identity;
            instantiated3DModel.transform.localScale    = Vector3.one;

            // If a prefab accidentally has AttachmentRuntime on it, nuke it for preview
            var rt = instantiated3DModel.GetComponent<AttachmentRuntime>();
            if (rt) DestroyImmediate(rt);

            model3DContainer.gameObject.SetActive(true);
        }
        else
        {
            model3DContainer.gameObject.SetActive(false);
        }
    }

    // Returns the first non-null attachment prefab (or null if none)
    GameObject GetPrimaryAttachmentPrefab(EquipableItem item)
    {
        if (item == null || item.attachments == null) return null;
        foreach (var ap in item.attachments)
        {
            if (ap != null && ap.prefab != null) return ap.prefab;
        }
        return null;
        // (If you prefer LINQ: return item.attachments?.FirstOrDefault(a => a?.prefab != null)?.prefab;)
    }

    void OnCardClicked()
    {
        equipmentManager.ShowItemDetail(associatedItem);
    }

    public void SetDetailPanelState(bool isOpen) => isDetailPanelOpen = isOpen;
    public EquipableItem GetAssociatedItem() => associatedItem;

    public void SetRainbowBackground(GameObject rainbowBG)
    {
        rainbowBackground = rainbowBG;

        if (rainbowBackground != null)
        {
            var rainbowRect = rainbowBackground.GetComponent<RectTransform>();
            var cardRect    = GetComponent<RectTransform>();

            if (rainbowRect && cardRect)
            {
                rainbowRect.anchorMin        = cardRect.anchorMin;
                rainbowRect.anchorMax        = cardRect.anchorMax;
                rainbowRect.anchoredPosition = cardRect.anchoredPosition;
                rainbowRect.sizeDelta        = cardRect.sizeDelta;

                rainbowBackground.transform.SetSiblingIndex(transform.GetSiblingIndex() - 1);
            }
        }
    }

    void Update()
    {
        if (rainbowBackground != null)
        {
            var rainbowRect = rainbowBackground.GetComponent<RectTransform>();
            var cardRect    = GetComponent<RectTransform>();

            if (rainbowRect && cardRect)
            {
                rainbowRect.anchoredPosition = cardRect.anchoredPosition;
                rainbowRect.sizeDelta        = cardRect.sizeDelta;
            }
        }
    }

    void OnDestroy()
    {
        if (instantiated3DModel != null)
        {
            if (Application.isPlaying) Destroy(instantiated3DModel);
            else DestroyImmediate(instantiated3DModel);
        }

        if (rainbowBackground != null)
        {
            if (Application.isPlaying) Destroy(rainbowBackground);
            else DestroyImmediate(rainbowBackground);
        }
    }
}
