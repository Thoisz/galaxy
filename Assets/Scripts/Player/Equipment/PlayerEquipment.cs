using System.Collections.Generic;
using UnityEngine;
using System;

public class PlayerEquipment : MonoBehaviour
{
    public static PlayerEquipment Instance { get; private set; }

    [SerializeField] private List<string> inventory = new();   // owned item names
    public IReadOnlyList<string> Inventory => inventory;

    // >>> Add this event so EquipmentManager can refresh when the list changes
    public event Action InventoryChanged;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // Optional: DontDestroyOnLoad(gameObject);
    }

#if UNITY_EDITOR
    // >>> Fires when you edit the Inventory list in the Inspector (edit OR play mode)
    void OnValidate()
    {
        InventoryChanged?.Invoke();
    }
#endif

    public bool HasInInventory(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return false;
        return inventory.Contains(itemName);
    }

    // >>> REPLACE your TryAdd with this
    public bool TryAdd(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return false;
        if (inventory.Contains(itemName)) return false;

        inventory.Add(itemName);
        InventoryChanged?.Invoke();
        return true;
    }

    // >>> REPLACE your TryRemove with this
    public bool TryRemove(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return false;
        bool removed = inventory.Remove(itemName);
        if (removed) InventoryChanged?.Invoke();
        return removed;
    }
}