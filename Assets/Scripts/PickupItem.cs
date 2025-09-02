using UnityEngine;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Attach to a pickup root GameObject. 
/// Works when colliders are on this object OR on children.
/// Requires a kinematic Rigidbody on the root so trigger messages route here.
/// Name the GameObject exactly like the Equipment's Inspector Name when kind = Equipment.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Items/Pickup Item")]
[RequireComponent(typeof(Rigidbody))]
public class PickupItem : MonoBehaviour
{
    public enum PickupKind { Collectible, Item, Equipment }

    [Header("Pickup Type")]
    public PickupKind kind = PickupKind.Equipment;

    [Header("Common")]
    [Tooltip("Player must carry this tag to trigger the pickup.")]
    public string playerTag = "Player";

    // -------- Collectible (currency) placeholders (not used yet) --------
    [Header("Collectible Settings (future)")]
    public string collectibleId;         // e.g., "coins", "shards"
    public int collectibleAmount = 1;

    // -------- Item (consumables, etc.) placeholders (not used yet) --------
    [Header("Item Settings (future)")]
    public string itemId;                // e.g., "health_potion"
    public int itemAmount = 1;

    // ===== N64-style spin/bob (private – not tweakable in inspector) =====
    const float SpinSpeedCollectible = 120f;
    const float SpinSpeedItem        = 160f;
    const float SpinSpeedEquipment   = 200f;

    const float BobAmplitude = 0.08f;   // gentle float
    const float BobFrequency = 2.0f;    // cycles/sec

    Vector3 _basePos;
    float   _spinSpeed;
    Rigidbody _rb;

    void Reset()
    {
        // Ensure there's a kinematic rigidbody on the ROOT so triggers bubble here.
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Make any existing colliders (self or children) triggers.
        // If none exist, add a small BoxCollider on the root.
        var cols = GetComponentsInChildren<Collider>(true);
        if (cols.Length == 0)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
        }
        else
        {
            foreach (var c in cols) c.isTrigger = true;
        }
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        // Safety (in case Reset didn't run): set RB for trigger routing
        if (_rb)
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        // Ensure at least one trigger collider exists somewhere under this root
        var anyCollider = GetComponentsInChildren<Collider>(true);
        if (anyCollider.Length == 0)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
        }

        _basePos   = transform.position;
        _spinSpeed = GetSpinSpeed(kind);
    }

    void Update()
    {
        // Spin around world up
        transform.Rotate(Vector3.up, _spinSpeed * Time.deltaTime, Space.World);

        // Gentle bob (classic collectible vibe)
        float bob = Mathf.Sin(Time.time * (Mathf.PI * 2f) * BobFrequency) * BobAmplitude;
        var p = _basePos;
        p.y += bob;
        transform.position = p;
    }

    // NOTE: With a Rigidbody on THIS root, any Trigger colliders on children will
    // route their trigger events to this script's OnTriggerEnter.
    void OnTriggerEnter(Collider other)
    {
        if (!other || !other.CompareTag(playerTag)) return;

        switch (kind)
        {
            case PickupKind.Equipment:
                TryPickupEquipment();
                break;

            case PickupKind.Collectible:
                // Future: add collectible to a currency system, play VFX/SFX, then destroy.
                Debug.Log($"[PickupItem] Collectible '{collectibleId}' ({collectibleAmount}) touched – handler not implemented yet.");
                break;

            case PickupKind.Item:
                // Future: add item to an Items inventory, then destroy.
                Debug.Log($"[PickupItem] Item '{itemId}' x{itemAmount} touched – handler not implemented yet.");
                break;
        }
    }

    void TryPickupEquipment()
    {
        // Use the GameObject name as the Equipment *Inspector Name* key.
        // Example: "boostershoesexotic"
        string key = gameObject.name;

        var pe = PlayerEquipment.Instance;
        if (pe == null)
        {
            Debug.LogWarning("[PickupItem] No PlayerEquipment in scene – cannot add equipment.");
            return;
        }

        // If already owned, do nothing (leave the pickup visible)
        if (pe.HasInInventory(key))
        {
            // Optional: feedback here (little bounce/flash) to show it's already owned.
            return;
        }

        // Try to add; on success, disappear
        if (pe.TryAdd(key))
        {
            // Optional: VFX/SFX hook here
            Destroy(gameObject);
        }
        else
        {
            Debug.LogWarning($"[PickupItem] Failed to add equipment '{key}' to inventory.");
        }
    }

    static float GetSpinSpeed(PickupKind k)
    {
        switch (k)
        {
            case PickupKind.Collectible: return SpinSpeedCollectible;
            case PickupKind.Item:        return SpinSpeedItem;
            case PickupKind.Equipment:   return SpinSpeedEquipment;
            default:                     return SpinSpeedEquipment;
        }
    }
}

#if UNITY_EDITOR
// -------- Tiny custom inspector to show only relevant settings per type. --------
[CustomEditor(typeof(PickupItem))]
public class PickupItemEditor : Editor
{
    SerializedProperty kindProp, playerTagProp;
    SerializedProperty collectibleIdProp, collectibleAmountProp;
    SerializedProperty itemIdProp, itemAmountProp;

    void OnEnable()
    {
        kindProp              = serializedObject.FindProperty("kind");
        playerTagProp         = serializedObject.FindProperty("playerTag");
        collectibleIdProp     = serializedObject.FindProperty("collectibleId");
        collectibleAmountProp = serializedObject.FindProperty("collectibleAmount");
        itemIdProp            = serializedObject.FindProperty("itemId");
        itemAmountProp        = serializedObject.FindProperty("itemAmount");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(kindProp);
        EditorGUILayout.PropertyField(playerTagProp);

        var kind = (PickupItem.PickupKind)kindProp.enumValueIndex;

        switch (kind)
        {
            case PickupItem.PickupKind.Collectible:
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Collectible Settings (future)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(collectibleIdProp,     new GUIContent("Collectible Id"));
                EditorGUILayout.PropertyField(collectibleAmountProp, new GUIContent("Amount"));
                EditorGUILayout.HelpBox("Placeholder for your future currency system.", MessageType.Info);
                break;

            case PickupItem.PickupKind.Item:
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Item Settings (future)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(itemIdProp,     new GUIContent("Item Id"));
                EditorGUILayout.PropertyField(itemAmountProp, new GUIContent("Amount"));
                EditorGUILayout.HelpBox("Placeholder for your future items system.", MessageType.Info);
                break;

            case PickupItem.PickupKind.Equipment:
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Equipment Settings", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "No options here: the equipment key comes from this GameObject's name.\n" +
                    "Name it exactly like the Equipment's Inspector Name (e.g., boostershoesexotic).",
                    MessageType.None);
                break;
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif