using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mirrors equipped ACCESSORY attachments from the player onto a portrait rig
/// by matching attachment bone names. Works with your existing EquipableItem
/// + AttachmentPoint data — no data changes required.
/// </summary>
public class PortraitAccessorySync : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EquipmentManager equipmentManager;
    [SerializeField] private Animator portraitAnimator;   // the portrait's Animator (rig root)

    [Header("Bone Remaps (optional)")]
    [Tooltip("Use if some portrait bone names differ from the player's bone names.\n" +
             "Key = player's bone name (AttachmentPoint.bone.name), Value = portrait bone name.")]
    [SerializeField] private List<NameRemap> boneNameRemaps = new();

    [System.Serializable]
    public struct NameRemap
    {
        public string playerBoneName;
        public string portraitBoneName;
    }

    // Internal
    private readonly Dictionary<string, Transform> _portraitBones = new();
    private readonly Dictionary<string, string> _remap = new();
    private readonly Dictionary<EquipableItem, List<GameObject>> _spawnedOnPortrait = new();

    private void Awake()
    {
        if (portraitAnimator == null)
            portraitAnimator = GetComponentInChildren<Animator>(true);

        BuildBoneCaches();
    }

    private void OnEnable()
    {
        if (equipmentManager != null)
        {
            equipmentManager.AccessoryEquipped += OnAccessoryEquipped;
            equipmentManager.AccessoryUnequipped += OnAccessoryUnequipped;

            // Initial full sync (covers already-equipped item on scene load)
            var equipped = equipmentManager.GetCurrentlyEquippedAccessory();
            if (equipped != null)
            {
                // Re-spawn cleanly
                OnAccessoryUnequipped(equipped);
                OnAccessoryEquipped(equipped);
            }
        }
    }

    private void OnDisable()
    {
        if (equipmentManager != null)
        {
            equipmentManager.AccessoryEquipped   -= OnAccessoryEquipped;
            equipmentManager.AccessoryUnequipped -= OnAccessoryUnequipped;
        }
        ClearAll();
    }

    private void BuildBoneCaches()
    {
        _portraitBones.Clear();
        _remap.Clear();

        if (portraitAnimator == null) return;

        // Cache all portrait bones by name (quick lookup)
        foreach (var tr in portraitAnimator.GetComponentsInChildren<Transform>(true))
        {
            if (!_portraitBones.ContainsKey(tr.name))
                _portraitBones.Add(tr.name, tr);
        }

        // Optional explicit remaps
        foreach (var nm in boneNameRemaps)
        {
            if (!string.IsNullOrWhiteSpace(nm.playerBoneName) && !string.IsNullOrWhiteSpace(nm.portraitBoneName))
                _remap[nm.playerBoneName] = nm.portraitBoneName;
        }
    }

    private void OnAccessoryEquipped(EquipableItem item)
    {
        if (item == null || item.category != EquipmentCategory.Accessory)
            return;

        // Remove any stale instances for safety
        OnAccessoryUnequipped(item);

        if (item.attachments == null || item.attachments.Count == 0)
            return;

        var list = new List<GameObject>();
        _spawnedOnPortrait[item] = list;

        for (int i = 0; i < item.attachments.Count; i++)
        {
            var ap = item.attachments[i];
            if (ap == null || ap.prefab == null || ap.bone == null)
                continue;

            // Find matching portrait bone by name (with optional remap)
            string playerBoneName = ap.bone.name;
            string lookFor = _remap.TryGetValue(playerBoneName, out var mapped) ? mapped : playerBoneName;

            if (!_portraitBones.TryGetValue(lookFor, out var portraitBone) || portraitBone == null)
            {
                Debug.LogWarning($"[PortraitAccessorySync] Could not find portrait bone '{lookFor}' for attachment '{ap.label}'.");
                continue;
            }

            // Instantiate under portrait bone
            var inst = Instantiate(ap.prefab, portraitBone, false);
            inst.name = $"Portrait_{item.itemName}_{ap.label}";

            // Ensure an AttachmentRuntime exists to apply/hold offsets in the same way
            var rt = inst.GetComponent<AttachmentRuntime>();
            if (rt == null) rt = inst.AddComponent<AttachmentRuntime>();

            rt.bone          = portraitBone;
            rt.pos           = ap.localPosition;
            rt.eul           = ap.localEulerAngles;
            rt.scl           = ap.localScale;
            rt.space         = ap.rotationSpace;
            // If item didn't specify a reference root, use the portrait animator root
            rt.referenceRoot = (ap.rotationSpace == AttachmentPoint.RotationSpace.BoneLocal)
                                ? null
                                : (ap.referenceRoot != null ? portraitAnimator.transform : portraitAnimator.transform);

            // Apply initial transform (AttachmentRuntime will maintain in LateUpdate)
            var t = inst.transform;
            t.localPosition = ap.localPosition;
            t.localRotation = Quaternion.Euler(ap.localEulerAngles);
            t.localScale    = ap.localScale;

            list.Add(inst);
        }
    }

    private void OnAccessoryUnequipped(EquipableItem item)
    {
        if (item == null) return;
        if (_spawnedOnPortrait.TryGetValue(item, out var list))
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i]) Destroy(list[i]);
            list.Clear();
            _spawnedOnPortrait.Remove(item);
        }
    }

    private void ClearAll()
    {
        foreach (var kv in _spawnedOnPortrait)
        {
            var list = kv.Value;
            for (int i = 0; i < list.Count; i++)
                if (list[i]) Destroy(list[i]);
        }
        _spawnedOnPortrait.Clear();
    }
}
