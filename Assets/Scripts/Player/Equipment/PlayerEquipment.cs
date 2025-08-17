using System.Collections.Generic;
using UnityEngine;

public enum AttachmentType
{
    Sword,
    Gun,
    Cape,
    Hat,
    ChestPiece,
    Shoes
}

[System.Serializable]
public class EquipmentAttachment
{
    [Header("Attachment Point")]
    public Transform attachmentPoint;
    
    [Header("Position Tweaks")]
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 rotationOffset = Vector3.zero;
    public Vector3 scaleMultiplier = Vector3.one;
    
    [Header("Currently Equipped")]
    public GameObject equippedObject;
}

[System.Serializable]
public class DualFootAttachment
{
    [Header("Foot Attachment Points")]
    public Transform leftFootAttach;
    public Transform rightFootAttach;
    
    [Header("Left Foot Tweaks")]
    public Vector3 leftPositionOffset = Vector3.zero;
    public Vector3 leftRotationOffset = Vector3.zero;
    public Vector3 leftScaleMultiplier = Vector3.one;
    
    [Header("Right Foot Tweaks")]
    public Vector3 rightPositionOffset = Vector3.zero;
    public Vector3 rightRotationOffset = Vector3.zero;
    public Vector3 rightScaleMultiplier = Vector3.one;
    
    [Header("Currently Equipped")]
    public GameObject leftFootObject;
    public GameObject rightFootObject;
}

public class PlayerEquipment : MonoBehaviour
{
    public static PlayerEquipment instance;
    
    [Header("Single Attachment Points")]
    public EquipmentAttachment swordAttach;      // Weapon hand
    public EquipmentAttachment gunAttach;        // Weapon hand
    public EquipmentAttachment capeAttach;       // Back/torso
    public EquipmentAttachment hatAttach;        // Head
    public EquipmentAttachment chestPieceAttach; // Chest/torso
    
    [Header("Dual Foot Attachment")]
    public DualFootAttachment shoesAttach;
    
    [Header("Equipment Overrides")]
    [Tooltip("Add specific equipment names here to override their default attachment settings")]
    public List<EquipmentOverride> equipmentOverrides = new List<EquipmentOverride>();
    
    private void Awake()
    {
        // Singleton pattern
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    public void EquipItem(EquipableItem item)
    {
        if (item == null || item.item3DModel == null)
        {
            Debug.LogWarning($"Cannot equip item: {(item == null ? "Item is null" : "3D model is missing")}");
            return;
        }
        
        // Get the attachment type for this item
        AttachmentType attachmentType = GetAttachmentTypeForItem(item);
        
        // Unequip current item of the same attachment type first
        UnequipItemByAttachmentType(attachmentType);
        
        // Check if this is shoes (dual foot attachment)
        if (attachmentType == AttachmentType.Shoes)
        {
            EquipShoes(item);
        }
        else
        {
            // Standard single attachment
            EquipToSingleAttachment(item, attachmentType);
        }
    }
    
    public void UnequipItem(EquipableItem item)
    {
        if (item == null) return;
        
        AttachmentType attachmentType = GetAttachmentTypeForItem(item);
        
        if (attachmentType == AttachmentType.Shoes)
        {
            UnequipShoes();
        }
        else
        {
            UnequipFromSingleAttachment(attachmentType);
        }
    }
    
    private AttachmentType GetAttachmentTypeForItem(EquipableItem item)
    {
        // Check if there's an override that specifies the attachment type
        EquipmentOverride overrideData = GetEquipmentOverride(item.itemName);
        if (overrideData != null)
        {
            return overrideData.attachmentType;
        }
        
        // Default mapping based on equipment category
        switch (item.category)
        {
            case EquipmentCategory.MeleeWeapon:
                return AttachmentType.Sword;
            case EquipmentCategory.RangedWeapon:
                return AttachmentType.Gun;
            case EquipmentCategory.Accessory:
                return AttachmentType.ChestPiece; // Default for accessories
            default:
                return AttachmentType.ChestPiece;
        }
    }
    
    private void EquipShoes(EquipableItem item)
    {
        if (shoesAttach.leftFootAttach == null || shoesAttach.rightFootAttach == null)
        {
            Debug.LogError("Foot attachment points are not assigned!");
            return;
        }
        
        // Get equipment override if it exists
        EquipmentOverride overrideData = GetEquipmentOverride(item.itemName);
        
        // Create left foot object
        GameObject leftFootObj = Instantiate(item.item3DModel, shoesAttach.leftFootAttach);
        ApplyTransformTweaks(leftFootObj, 
            overrideData?.leftFootPosition ?? shoesAttach.leftPositionOffset,
            overrideData?.leftFootRotation ?? shoesAttach.leftRotationOffset,
            overrideData?.leftFootScale ?? shoesAttach.leftScaleMultiplier);
        
        // Create right foot object
        GameObject rightFootObj = Instantiate(item.item3DModel, shoesAttach.rightFootAttach);
        ApplyTransformTweaks(rightFootObj,
            overrideData?.rightFootPosition ?? shoesAttach.rightPositionOffset,
            overrideData?.rightFootRotation ?? shoesAttach.rightRotationOffset,
            overrideData?.rightFootScale ?? shoesAttach.rightScaleMultiplier);
        
        // Store references
        shoesAttach.leftFootObject = leftFootObj;
        shoesAttach.rightFootObject = rightFootObj;
        
        Debug.Log($"Equipped shoes: {item.itemName}");
    }
    
    private void UnequipShoes()
    {
        if (shoesAttach.leftFootObject != null)
        {
            Destroy(shoesAttach.leftFootObject);
            shoesAttach.leftFootObject = null;
        }
        
        if (shoesAttach.rightFootObject != null)
        {
            Destroy(shoesAttach.rightFootObject);
            shoesAttach.rightFootObject = null;
        }
    }
    
    private void EquipToSingleAttachment(EquipableItem item, AttachmentType attachmentType)
    {
        EquipmentAttachment targetAttachment = GetAttachmentForType(attachmentType);
        
        if (targetAttachment?.attachmentPoint == null)
        {
            Debug.LogError($"No attachment point assigned for type: {attachmentType}");
            return;
        }
        
        // Create the equipped object
        GameObject equippedObj = Instantiate(item.item3DModel, targetAttachment.attachmentPoint);
        
        // Get equipment override if it exists
        EquipmentOverride overrideData = GetEquipmentOverride(item.itemName);
        
        // Apply tweaks (override data takes priority)
        Vector3 posOffset = overrideData?.positionOffset ?? targetAttachment.positionOffset;
        Vector3 rotOffset = overrideData?.rotationOffset ?? targetAttachment.rotationOffset;
        Vector3 scaleMultiplier = overrideData?.scaleMultiplier ?? targetAttachment.scaleMultiplier;
        
        // Apply any permanent tweaks from the item itself
        posOffset += item.appliedPositionTweak;
        rotOffset += item.appliedRotationTweak;
        scaleMultiplier = Vector3.Scale(scaleMultiplier, item.appliedScaleTweak);
        
        ApplyTransformTweaks(equippedObj, posOffset, rotOffset, scaleMultiplier);
        
        // Store reference
        targetAttachment.equippedObject = equippedObj;
        
        Debug.Log($"Equipped {attachmentType}: {item.itemName}");
    }
    
    private void UnequipFromSingleAttachment(AttachmentType attachmentType)
    {
        EquipmentAttachment targetAttachment = GetAttachmentForType(attachmentType);
        
        if (targetAttachment?.equippedObject != null)
        {
            Destroy(targetAttachment.equippedObject);
            targetAttachment.equippedObject = null;
        }
    }
    
    private void UnequipItemByAttachmentType(AttachmentType attachmentType)
    {
        if (attachmentType == AttachmentType.Shoes)
        {
            UnequipShoes();
        }
        else
        {
            UnequipFromSingleAttachment(attachmentType);
        }
    }
    
    private EquipmentAttachment GetAttachmentForType(AttachmentType attachmentType)
    {
        switch (attachmentType)
        {
            case AttachmentType.Sword:
                return swordAttach;
            case AttachmentType.Gun:
                return gunAttach;
            case AttachmentType.Cape:
                return capeAttach;
            case AttachmentType.Hat:
                return hatAttach;
            case AttachmentType.ChestPiece:
                return chestPieceAttach;
            default:
                return null;
        }
    }
    
    private void ApplyTransformTweaks(GameObject obj, Vector3 positionOffset, Vector3 rotationOffset, Vector3 scaleMultiplier)
    {
        if (obj == null) return;
        
        // Apply position offset
        obj.transform.localPosition = positionOffset;
        
        // Apply rotation offset
        obj.transform.localRotation = Quaternion.Euler(rotationOffset);
        
        // Apply scale multiplier
        obj.transform.localScale = Vector3.Scale(obj.transform.localScale, scaleMultiplier);
    }
    
    private EquipmentOverride GetEquipmentOverride(string itemName)
    {
        return equipmentOverrides.Find(x => x.itemName.Equals(itemName, System.StringComparison.OrdinalIgnoreCase));
    }
    
    // Utility methods for external access
    public bool IsItemEquipped(AttachmentType attachmentType)
    {
        if (attachmentType == AttachmentType.Shoes)
        {
            return shoesAttach.leftFootObject != null;
        }
        return GetAttachmentForType(attachmentType)?.equippedObject != null;
    }
    
    public GameObject GetEquippedObject(AttachmentType attachmentType)
    {
        return GetAttachmentForType(attachmentType)?.equippedObject;
    }
    
    // Method to help with tweaking equipment positions in the editor
    [ContextMenu("Apply Current Transform as Override")]
    public void ApplyCurrentTransformAsOverride()
    {
        // This can be called from the context menu to help set up overrides
        Debug.Log("Select an equipped object and set up an override in the inspector");
    }
}

[System.Serializable]
public class EquipmentOverride
{
    [Header("Equipment Identity")]
    public string itemName;
    
    [Header("Attachment Type Override")]
    [Tooltip("Override the default attachment type for this specific item")]
    public AttachmentType attachmentType = AttachmentType.ChestPiece;
    
    [Header("Single Attachment Overrides")]
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 rotationOffset = Vector3.zero;
    public Vector3 scaleMultiplier = Vector3.one;
    
    [Header("Foot Attachment Overrides (Only for Shoes)")]
    [Tooltip("Override settings for left foot when attachment type is Shoes")]
    public Vector3 leftFootPosition = Vector3.zero;
    public Vector3 leftFootRotation = Vector3.zero;
    public Vector3 leftFootScale = Vector3.one;
    
    [Tooltip("Override settings for right foot when attachment type is Shoes")]
    public Vector3 rightFootPosition = Vector3.zero;
    public Vector3 rightFootRotation = Vector3.zero;
    public Vector3 rightFootScale = Vector3.one;
}