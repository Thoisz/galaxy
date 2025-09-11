using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

#region Data Types

public enum EquipmentCategory
{
    Weapon,
    Accessory
}

public enum EquipmentRarity
{
    Common,    // Blue
    Rare,      // Orange
    Legendary, // Purple
    Exotic     // Rainbow
}

public enum EquipmentStyle
{
    Spook,
    Cyborg,
    Elemental,
    Bloom
}

public enum WeaponSubtype
{
    Ranged,
    Melee
}

[Serializable]
public class AttachmentPoint
{
    [Tooltip("Just a label to help you tell entries apart in the inspector")]
    public string label = "Left Shoe";

    [Header("Prefab")]
    public GameObject prefab;

    [Header("Bone (drag a scene Transform here)")]
    public Transform bone;

    public enum RotationSpace { BoneLocal, CharacterRoot, World }

    [Header("Local Offsets (relative to the bone)")]
    public Vector3 localPosition = Vector3.zero;
    public Vector3 localEulerAngles = Vector3.zero;
    public Vector3 localScale = Vector3.one;

    [Header("Rotation Offset Space")]
    [Tooltip("BoneLocal = behaves like before.\nCharacterRoot = offset held relative to the character's facing.\nWorld = offset held in world space.")]
    public RotationSpace rotationSpace = RotationSpace.BoneLocal;

    [Tooltip("Used when RotationSpace = CharacterRoot. If left empty, the player's Animator root is used.")]
    public Transform referenceRoot;
}

[Serializable]
public class EquipableItem
{
    [Header("Basic Info")]
    public string itemName;
    [Tooltip("Unique key used by PlayerEquipment inventory. Leave blank to fall back to Item Name.")]
    public string inspectorName;
    [TextArea(2, 6)] public string itemDescription;
    public EquipmentCategory category;       // Weapon | Accessory
    public EquipmentRarity rarity;
    public EquipmentStyle style;

    [Header("UI Display")]
    public Sprite itemIcon;

    // ---- Weapon Stats (shown only if category == Weapon) ----
    [Header("Weapon Stats")]
    public WeaponSubtype weaponSubtype;      // Ranged | Melee

    // RANGED
    [Tooltip("Ranged: damage per mouse click shot")]
    public int ranged_MBCDamage = 0;
    [Tooltip("Ranged: cooldown between click shots")]
    public float ranged_MBCCooldown = 0.2f;
    [Tooltip("Ranged: cursor sprite while this weapon is active")]
    public Sprite ranged_CursorSprite;
    [Tooltip("Ranged: name only (for future)")]
    public string ranged_XMove;
    [Tooltip("Ranged: name only (for future)")]
    public string ranged_ZMove;

    // MELEE
    [Tooltip("Melee: number of clicks in the combo")]
    public int melee_MBCAmount = 1;
    [Tooltip("Melee: damage per click in combo (size equals MBC amount)")]
    public List<int> melee_MBCDamages = new List<int> { 0 };
    [Tooltip("Melee: cooldown after completing the combo")]
    public float melee_MBCComboCooldown = 0.5f;
    [Tooltip("Melee: name only (for future)")]
    public string melee_XMove;
    [Tooltip("Melee: name only (for future)")]
    public string melee_ZMove;

    [Header("Equipment Bonuses")]
    public int healthBonus = 0;
    public int staminaBonus = 0;
    public float speedBonus = 0f;

    [Header("3D Attachments (optional, for Accessories/Weapons etc.)")]
    public List<AttachmentPoint> attachments = new List<AttachmentPoint>();

    // ---- Legacy compatibility (so old scripts still compile) ----
    [Header("Legacy Tweaks (compat)")]
    public Vector3 appliedScaleTweak = Vector3.one;
    public Vector3 appliedPositionTweak = Vector3.zero;
    public Vector3 appliedRotationTweak = Vector3.zero;

    // Keep the melee list in sync with the amount
    public void EnsureMeleeListSize()
    {
        if (melee_MBCAmount < 0) melee_MBCAmount = 0;
        if (melee_MBCDamages == null) melee_MBCDamages = new List<int>();
        while (melee_MBCDamages.Count < melee_MBCAmount) melee_MBCDamages.Add(0);
        while (melee_MBCDamages.Count > melee_MBCAmount && melee_MBCDamages.Count > 0) melee_MBCDamages.RemoveAt(melee_MBCDamages.Count - 1);
    }
}

#endregion

public class AttachmentRuntime : MonoBehaviour
{
    [Header("Driven by EquipmentManager")]
    public Transform bone;
    public Vector3 pos, eul, scl;
    public AttachmentPoint.RotationSpace space;
    public Transform referenceRoot; // usually animator root

    [Header("Live Tuning")]
    [Tooltip("When ON: you can grab/move/rotate/scale this object in Play Mode.\n" +
             "Your changes are captured and converted back into pos/eul/scl in the chosen space.")]
    public bool editMode = false;

    void LateUpdate()
    {
        if (!bone) return;

        if (editMode)
        {
            // 1) Read your current hand-tweaked transform…
            pos = transform.localPosition;
            scl = transform.localScale;

            if (space == AttachmentPoint.RotationSpace.BoneLocal)
            {
                eul = transform.localEulerAngles;
            }
            else
            {
                // Convert current local rotation back into the offset defined in the chosen space
                Quaternion L = transform.localRotation;         // current (user) local rot
                Quaternion worldNow = bone.rotation * L;        // world rot of this object
                Quaternion spaceRot = GetSpaceRotation();       // world rot of the space basis
                Quaternion offsetWorld = worldNow * Quaternion.Inverse(bone.rotation);
                Quaternion offsetInSpace = Quaternion.Inverse(spaceRot) * offsetWorld * spaceRot;
                eul = offsetInSpace.eulerAngles;
            }
            // Do not early-return; we still apply below so the object keeps following while you edit.
        }

        // 2) Apply from stored pos/eul/scl (so it keeps following bones/root while you edit)
        transform.localPosition = pos;
        transform.localScale    = scl;

        Quaternion offset = Quaternion.Euler(eul);
        if (space == AttachmentPoint.RotationSpace.BoneLocal)
        {
            transform.localRotation = offset;
        }
        else
        {
            Quaternion spaceRot = GetSpaceRotation();
            Quaternion offsetWorld = spaceRot * offset * Quaternion.Inverse(spaceRot);
            Quaternion local = Quaternion.Inverse(bone.rotation) * (offsetWorld * bone.rotation);
            transform.localRotation = local;
        }
    }

    Quaternion GetSpaceRotation()
    {
        if (space == AttachmentPoint.RotationSpace.World) return Quaternion.identity;
        if (referenceRoot) return referenceRoot.rotation;
        return Quaternion.identity;
    }

#if UNITY_EDITOR
    [ContextMenu("Log offsets (pos/eul/scl)")]
    void LogOffsets()
    {
        Debug.Log($"{name} pos={pos} eul={eul} scl={scl}");
        UnityEditor.EditorGUIUtility.systemCopyBuffer =
            $"pos={pos}  eul={eul}  scl={scl}";
    }
#endif
}

public class EquipmentManager : MonoBehaviour
{
    [Header("Listing Options")]
    public bool showOnlyOwnedItems = false; // toggle in inspector if you want the list to show only owned items
    // =========================================================
    //                  EQUIPMENT DATABASE
    // =========================================================
    [Header("Equipment Database (ALL available items)")]
    public List<EquipableItem> equipmentDatabase = new List<EquipableItem>();

    // =========================================================
    //                     UI REFERENCES
    // =========================================================

    [Header("UI References")]
public Transform itemsScrollViewContent;     // previously Items Container

// Item card prefabs
public GameObject itemCardPrefabC;           // Common
public GameObject itemCardPrefabR;           // Rare
public GameObject itemCardPrefabL;           // Legendary
public GameObject itemCardPrefabE;           // Exotic

// Per-rarity cards + their label texts
public GameObject rarityCardC; public TMP_Text rarityCardCText;
public GameObject typeCardC;   public TMP_Text typeCardCText;

public GameObject rarityCardR; public TMP_Text rarityCardRText;
public GameObject typeCardR;   public TMP_Text typeCardRText;

public GameObject rarityCardL; public TMP_Text rarityCardLText;
public GameObject typeCardL;   public TMP_Text typeCardLText;

public GameObject rarityCardE; public TMP_Text rarityCardEText;
public GameObject typeCardE;   public TMP_Text typeCardEText;

// List / search
public TMP_Dropdown filterDropdown;
public TMP_InputField searchInputField;
public ScrollRect itemsScrollRect;
public Button equipPanelCloseButton;
public TMP_Dropdown raceDropdown;

// Detail panels
public GameObject itemDetailPanelC;
public GameObject itemDetailPanelR;
public GameObject itemDetailPanelL;
public GameObject itemDetailPanelE;
public GameObject exoticDetailRainbowBackground;

// Equip buttons
public Button equipButtonC;
public Button equipButtonR;
public Button equipButtonL;
public Button equipButtonE;

// Bound at runtime via RebindDetailTextsTo()
TMP_Text xMoveText;
TMP_Text zMoveText;
TMP_Text descriptionText;

private ItemDetailPanelUI _currentPanelUI;

// ===== EQUIPPING =====
private PlayerMovement _playerMove;
private EquipableItem _equippedAccessory;                     // single accessory slot
private readonly Dictionary<EquipableItem, float> _appliedSpeedMods = new(); // item -> absolute delta applied

// 3D attachment management
private Animator _playerAnimator;
private readonly Dictionary<EquipableItem, List<GameObject>> _spawnedAttachmentInstances = new();

    // =========================================================
    //             DETAIL PANEL ANIMATION SETTINGS
    // =========================================================
    [Header("Detail Panel Animation Settings")]
    public float slideAnimationDuration = 0.3f;
    public float slideStartOffset = 500f;     // start from right
    public Vector2 positionOffset = Vector2.zero;
    public AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // =========================================================
    //                    INTERNAL STATE
    // =========================================================
    private List<EquipableItem> _filtered = new List<EquipableItem>();
    private readonly List<GameObject> _spawnedCards = new List<GameObject>();

    private EquipableItem _currentDetailItem;
    private GameObject _currentActiveDetailPanel;
    private Coroutine _anim;
    private bool _isAnimating;

    private readonly Dictionary<GameObject, Vector2> _originalDetailPositions = new();

    // =========================================================
    //                        LIFECYCLE
    // =========================================================
    void OnValidate()
    {
        // Keep melee list sizes in sync in the editor
        if (equipmentDatabase != null)
            foreach (var it in equipmentDatabase) it?.EnsureMeleeListSize();
    }

    void Start()
{
    TryResolvePlayerRefs();

    CacheOriginalDetailPositions();
    EnsureDetailPanelsUnderMainPanels();
    WireUI();

    var pe = PlayerEquipment.Instance;
    if (pe) pe.InventoryChanged += RefreshDisplay;

    RefreshDisplay();
    CloseAllDetailPanelsImmediate();
}

void OnEnable()
{
    var pe = PlayerEquipment.Instance;
    if (pe != null) pe.InventoryChanged += RefreshDisplay;
}

void OnDisable()
{
    var pe = PlayerEquipment.Instance;
    if (pe != null) pe.InventoryChanged -= RefreshDisplay;
}

    // =========================================================
    //                        UI WIRING
    // =========================================================
    void WireUI()
{
    if (filterDropdown != null)
    {
        filterDropdown.onValueChanged.RemoveAllListeners();
        filterDropdown.ClearOptions();
        filterDropdown.AddOptions(new List<string> { "All", "Weapons", "Accessories" });
        filterDropdown.onValueChanged.AddListener(_ => RefreshDisplay());
    }

    if (searchInputField != null)
    {
        searchInputField.onValueChanged.RemoveAllListeners();
        searchInputField.onValueChanged.AddListener(_ => RefreshDisplay());
    }

    if (equipPanelCloseButton != null)
    {
        equipPanelCloseButton.onClick.RemoveAllListeners();
        equipPanelCloseButton.onClick.AddListener(CloseWholeEquipTab);
    }

if (equipButtonC) { equipButtonC.onClick.RemoveAllListeners(); equipButtonC.onClick.AddListener(OnEquipButtonPressed); }
if (equipButtonR) { equipButtonR.onClick.RemoveAllListeners(); equipButtonR.onClick.AddListener(OnEquipButtonPressed); }
if (equipButtonL) { equipButtonL.onClick.RemoveAllListeners(); equipButtonL.onClick.AddListener(OnEquipButtonPressed); }
if (equipButtonE) { equipButtonE.onClick.RemoveAllListeners(); equipButtonE.onClick.AddListener(OnEquipButtonPressed); }

}

    void RebindDetailTextsTo(GameObject panelKey)
{
    _currentPanelUI = panelKey ? panelKey.GetComponentInChildren<ItemDetailPanelUI>(true) : null;

    xMoveText       = _currentPanelUI ? _currentPanelUI.xMoveText       : null;
    zMoveText       = _currentPanelUI ? _currentPanelUI.zMoveText       : null;

    if (_currentPanelUI != null && _currentPanelUI.descriptionInput != null)
        descriptionText = _currentPanelUI.descriptionInput.textComponent;
    else
        descriptionText = _currentPanelUI ? _currentPanelUI.descriptionText : null;
}

void ConfigurePagerFor(EquipableItem item, GameObject panelKey)
{
    if (_currentPanelUI == null) return;

    var pager = _currentPanelUI.pager;
    bool isAccessory = (item.category == EquipmentCategory.Accessory);

    // Hide/show arrows
    if (_currentPanelUI.leftArrow)  _currentPanelUI.leftArrow.SetActive(!isAccessory);
    if (_currentPanelUI.rightArrow) _currentPanelUI.rightArrow.SetActive(!isAccessory);

    if (pager != null)
    {
        if (isAccessory)
        {
            // Lock to Description page (index 1), no navigation
            pager.SetBounds(1, 1);
            pager.GoToPage(1, true); // snap so there’s no one-frame flash
        }
        else
        {
            // Normal 2-page mode, start on MoveSet page
            pager.SetBounds(0, 1);
            pager.GoToPage(0, true);
        }
    }
}

void OnEquipButtonPressed()
{
    if (_currentDetailItem == null) return;

    // Block if not owned
    if (!PlayerOwns(_currentDetailItem))
    {
        Debug.Log($"'{_currentDetailItem.itemName}' is locked. Acquire it before equipping.");
        // (Optional) flash the button, play a sound, etc.
        return;
    }

    if (_currentDetailItem.category == EquipmentCategory.Accessory)
    {
        if (IsAccessoryEquipped(_currentDetailItem))
            UnequipAccessory(_currentDetailItem);
        else
            EquipAccessory(_currentDetailItem);

        UpdateEquipButtonLabelFor(_currentDetailItem);
    }
    else
    {
        Debug.Log($"Equip pressed on non-accessory: {_currentDetailItem.itemName}");
    }
}

bool IsAccessoryEquipped(EquipableItem item) => _equippedAccessory == item;

void EquipAccessory(EquipableItem item)
{
    if (item == null) return;
    if (!PlayerOwns(item))
    {
        Debug.LogWarning($"Tried to equip '{item.itemName}' but it's not owned.");
        return;
    }

    // Resolve refs on-demand (harmless if nothing is found)
    TryResolvePlayerRefs();

    // Unequip previous if different
    if (_equippedAccessory != null && _equippedAccessory != item)
        UnequipAccessory(_equippedAccessory);

    // Only handle visual attachments now
    AttachItemPrefabs(item);

    _equippedAccessory = item;
    Debug.Log($"Equipped accessory '{item.itemName}'.");
}

void UnequipAccessory(EquipableItem item)
{
    if (item == null) return;

    TryResolvePlayerRefs();

    // Clear track of equipped accessory
    if (_equippedAccessory == item) _equippedAccessory = null;

    // Remove visuals only
    DetachItemPrefabs(item);

    Debug.Log($"Unequipped accessory '{item.itemName}'.");
}

void AttachItemPrefabs(EquipableItem item)
{
    if (item == null || item.attachments == null) return;

    // In case we re-equip the same item without unequipping
    DetachItemPrefabs(item);

    var spawned = new List<GameObject>();
    _spawnedAttachmentInstances[item] = spawned;

    foreach (var ap in item.attachments)
    {
        if (ap == null || ap.prefab == null)
            continue;

        Transform bone = ResolveAttachmentBone(ap);
        if (bone == null)
        {
            Debug.LogWarning(
                $"Attachment '{ap.label}': Bone reference is missing. " +
                $"Drag the correct foot/hand/etc. Transform into the 'Bone' field on the item.");
            continue;
        }

        var inst = Instantiate(ap.prefab, bone, false);
        inst.name = $"Equipped_{item.itemName}_{ap.label}";

        var t = inst.transform;
        t.localPosition = ap.localPosition;
        t.localRotation = Quaternion.Euler(ap.localEulerAngles);
        t.localScale    = ap.localScale;

        // Remove legacy driver if present
        var oldRuntime = inst.GetComponent<AttachmentRuntime>();
        if (oldRuntime) Destroy(oldRuntime);

        spawned.Add(inst);
    }
}

// --- Auto-resolve player refs without inspector wiring ---
void TryResolvePlayerRefs()
{
    if (_playerMove != null && _playerAnimator != null) return;

    // Prefer the PlayerEquipment singleton as our anchor
    Transform root = PlayerEquipment.Instance ? PlayerEquipment.Instance.transform : null;

    // Fallbacks
    if (root == null)
    {
        var playerTagged = GameObject.FindGameObjectWithTag("Player");
        if (playerTagged) root = playerTagged.transform;
    }
    if (root == null)
    {
        var pmAny = FindObjectOfType<PlayerMovement>();
        if (pmAny) root = pmAny.transform;
    }

    // Fill _playerMove
    if (_playerMove == null)
    {
        _playerMove = root ? root.GetComponentInChildren<PlayerMovement>(true) : null;
        if (_playerMove == null) _playerMove = FindObjectOfType<PlayerMovement>();
    }

    // Fill _playerAnimator (used for future bone lookups; not required to attach to explicit bones)
    if (_playerAnimator == null)
    {
        _playerAnimator = root ? root.GetComponentInChildren<Animator>(true) : null;
        if (_playerAnimator == null) _playerAnimator = FindObjectOfType<Animator>();
    }
}

void DetachItemPrefabs(EquipableItem item)
{
    if (_spawnedAttachmentInstances.TryGetValue(item, out var spawned))
    {
        for (int i = 0; i < spawned.Count; i++)
            if (spawned[i]) Destroy(spawned[i]);
        spawned.Clear();
        _spawnedAttachmentInstances.Remove(item);
    }
}

// --- Bone resolver (kept tiny & explicit) ---
Transform ResolveAttachmentBone(AttachmentPoint ap)
{
    return ap != null ? ap.bone : null;
}

    void UpdatePerCardTexts(string rarityLabel, string typeLabel)
{
    if (rarityCardCText) rarityCardCText.text = rarityLabel;
    if (typeCardCText)   typeCardCText.text   = typeLabel;

    if (rarityCardRText) rarityCardRText.text = rarityLabel;
    if (typeCardRText)   typeCardRText.text   = typeLabel;

    if (rarityCardLText) rarityCardLText.text = rarityLabel;
    if (typeCardLText)   typeCardLText.text   = typeLabel;

    if (rarityCardEText) rarityCardEText.text = rarityLabel;
    if (typeCardEText)   typeCardEText.text   = typeLabel;
}

    // =========================================================
    //                    LIST / FILTER / CARDS
    // =========================================================
    public void RefreshDisplay()
    {
        ClearCards();
        FilterDatabase();
        SpawnCards();

        if (itemsScrollRect != null)
            itemsScrollRect.verticalNormalizedPosition = 1f;
    }

    // === Legacy method shims for existing code ===
    public void ShowItemDetail(EquipableItem item) => OnCardClicked(item);

    public void OnAllMenusClosed()
{
    // When all menus close, if a detail panel is open, do NOT fold it in.
    // Let it slide down together with the Equip tab, then reset for next open.
    if (_currentActiveDetailPanel != null && _currentActiveDetailPanel.activeSelf)
    {
        StartCoroutine(SlideDownWithEquipPanel(_currentActiveDetailPanel));
    }
    else
    {
        // No panel open—just ensure everything is reset for next time.
        ResetAllDetailPanelsToOriginalPositions();
    }
}

    public void EnsureDetailPanelsUnderMainPanels()
{
    // Ensure detail panels render under the main equip panel during the coordinated slide.
    void SetOrder(GameObject go, int order)
    {
        if (!go) return;
        var cv = go.GetComponent<Canvas>();
        if (!cv)
        {
            cv = go.AddComponent<Canvas>();
            cv.overrideSorting = true;
        }
        cv.sortingOrder = order;
        if (!go.GetComponent<GraphicRaycaster>()) go.AddComponent<GraphicRaycaster>();
    }

    SetOrder(itemDetailPanelC, -20);
    SetOrder(itemDetailPanelR, -20);
    SetOrder(itemDetailPanelL, -20);
    SetOrder(itemDetailPanelE, -20);
    if (exoticDetailRainbowBackground) SetOrder(exoticDetailRainbowBackground, -25);

    var menuManager = FindObjectOfType<MenuManager>();
    if (menuManager != null && menuManager.equipTabPanel != null)
    {
        var equipCv = menuManager.equipTabPanel.GetComponent<Canvas>();
        if (!equipCv)
        {
            equipCv = menuManager.equipTabPanel.AddComponent<Canvas>();
            equipCv.overrideSorting = true;
        }
        equipCv.sortingOrder = 1; // make sure the main panel is above
        if (!menuManager.equipTabPanel.GetComponent<GraphicRaycaster>())
            menuManager.equipTabPanel.AddComponent<GraphicRaycaster>();
    }
}

public void CloseWholeEquipTab()
{
    var menuManager = FindObjectOfType<MenuManager>();

    // If a detail panel is open, start its coordinated slide (rides down with the tab)
    if (_currentActiveDetailPanel != null && _currentActiveDetailPanel.activeSelf)
    {
        EnsureDetailPanelsUnderMainPanels();
        StartCoroutine(SlideDownWithEquipPanel(_currentActiveDetailPanel));
    }

    // Trigger the same close behavior as your B key
    if (menuManager != null)
    {
        menuManager.CloseMenu();
    }
}

IEnumerator SlideDownWithEquipPanel(GameObject panelKey)
{
    var animObj = GetAnimObj(panelKey);
    if (!animObj) yield break;

    EnsureDetailPanelsUnderMainPanels();

    var menuManager = FindObjectOfType<MenuManager>();
    var equipRT = (menuManager != null && menuManager.equipTabPanel != null)
        ? menuManager.equipTabPanel.GetComponent<RectTransform>()
        : null;

    var rt = animObj.GetComponent<RectTransform>();
    if (!rt || !equipRT) yield break;

    // Cache starting positions
    Vector2 detailStart = rt.anchoredPosition;
    Vector2 equipStart  = equipRT.anchoredPosition;

    // Follow the equip panel exactly so timing never drifts
    float duration = 0.18f; // keep in sync with your MenuManager close duration
    float t = 0f;
    while (t < duration)
    {
        t += Time.deltaTime;
        Vector2 equipDelta = equipRT.anchoredPosition - equipStart;
        rt.anchoredPosition = detailStart + equipDelta;
        yield return null;
    }

    // Final snap
    Vector2 finalEquipDelta = equipRT.anchoredPosition - equipStart;
    rt.anchoredPosition = detailStart + finalEquipDelta;

    // Reset for next open
    ResetAllDetailPanelsToOriginalPositions();
}

bool PlayerOwns(EquipableItem item)
{
    if (item == null) return false;

    var pe = PlayerEquipment.Instance;
    if (pe == null) return false;

    // Prefer inspectorName, fall back to itemName
    string key = !string.IsNullOrWhiteSpace(item.inspectorName) ? item.inspectorName : item.itemName;
    if (string.IsNullOrWhiteSpace(key)) return false;

    // Accept either key or old itemName (backward compat)
    if (pe.HasInInventory(key)) return true;
    if (!string.Equals(key, item.itemName, StringComparison.Ordinal) && pe.HasInInventory(item.itemName)) return true;

    return false;
}

void ResetAllDetailPanelsToOriginalPositions()
{
    void ResetOne(GameObject key)
    {
        if (!key) return;
        if (!_originalDetailPositions.TryGetValue(key, out var orig)) return;
        var obj = GetAnimObj(key);
        if (!obj) return;
        obj.SetActive(false);
        var rt = obj.GetComponent<RectTransform>();
        if (rt) rt.anchoredPosition = orig;
    }

    ResetOne(itemDetailPanelC);
    ResetOne(itemDetailPanelR);
    ResetOne(itemDetailPanelL);
    ResetOne(itemDetailPanelE);

    _currentActiveDetailPanel = null;
    _currentDetailItem = null;
    _anim = null;
    _isAnimating = false;
}

    // Add this once somewhere in the class (near your other public fields):
// [Header("Listing Options")]
// public bool showOnlyOwnedItems = false;

void FilterDatabase()
{
    string search = (searchInputField ? searchInputField.text : "").Trim().ToLower();
    int categoryFilter = filterDropdown ? filterDropdown.value : 0;

    var pe = PlayerEquipment.Instance;
    if (pe == null) { _filtered = new List<EquipableItem>(); return; }

    // Build order map from PlayerEquipment inventory
    // Use inspectorName when available; fall back to itemName
    var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var inv = pe.Inventory;
    for (int i = 0; i < inv.Count; i++)
    {
        // first occurrence wins
        if (!order.ContainsKey(inv[i])) order[inv[i]] = i;
    }

    // Helper to get the key we store in inventory for an item
    int IndexFor(EquipableItem it)
    {
        string key = !string.IsNullOrWhiteSpace(it.inspectorName) ? it.inspectorName : it.itemName;
        if (key == null) return int.MaxValue;

        // Prefer inspector key; also accept legacy itemName position if present
        if (order.TryGetValue(key, out int idx)) return idx;
        if (!string.Equals(key, it.itemName, StringComparison.Ordinal) &&
            order.TryGetValue(it.itemName ?? "", out idx)) return idx;

        return int.MaxValue; // should not happen because we only keep owned items, but safe fallback
    }

    _filtered = equipmentDatabase
        .Where(it =>
        {
            if (it == null) return false;

            // Only show items the player OWNS
            if (!PlayerOwns(it)) return false;

            // Category filter (0=All,1=Weapons,2=Accessories)
            if (categoryFilter == 1 && it.category != EquipmentCategory.Weapon) return false;
            if (categoryFilter == 2 && it.category != EquipmentCategory.Accessory) return false;

            // Search matches Item Name OR Inspector Name
            if (!string.IsNullOrEmpty(search))
            {
                string display = (it.itemName ?? "").ToLower();
                string key     = (it.inspectorName ?? "").ToLower();
                if (!display.Contains(search) && !key.Contains(search)) return false;
            }

            return true;
        })
        .OrderBy(it => IndexFor(it))                   // <-- sort by PlayerEquipment order
        .ThenBy(it => it.itemName ?? string.Empty)     // stable fallback if needed
        .ToList();
}

    void SpawnCards()
    {
        if (itemsScrollViewContent == null) return;

        foreach (var item in _filtered)
        {
            var prefab = GetCardPrefab(item.rarity);
            if (prefab == null) continue;

            var card = Instantiate(prefab, itemsScrollViewContent);
            _spawnedCards.Add(card);

            // If you have a card script, wire it. Otherwise, add a simple button.
            var btn = card.GetComponentInChildren<Button>();
            if (btn == null) btn = card.AddComponent<Button>();

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnCardClicked(item));
        }
    }

    void ClearCards()
    {
        foreach (var go in _spawnedCards) if (go) Destroy(go);
        _spawnedCards.Clear();
    }

    GameObject GetCardPrefab(EquipmentRarity r) => r switch
    {
        EquipmentRarity.Common => itemCardPrefabC,
        EquipmentRarity.Rare => itemCardPrefabR,
        EquipmentRarity.Legendary => itemCardPrefabL,
        EquipmentRarity.Exotic => itemCardPrefabE,
        _ => itemCardPrefabC
    };

    // =========================================================
    //                      DETAIL PANELS
    // =========================================================
    void CacheOriginalDetailPositions()
    {
        _originalDetailPositions.Clear();

        Vector2 Pos(GameObject go) =>
            go ? go.GetComponent<RectTransform>().anchoredPosition + positionOffset : Vector2.zero;

        if (itemDetailPanelC) _originalDetailPositions[itemDetailPanelC] = Pos(itemDetailPanelC);
        if (itemDetailPanelR) _originalDetailPositions[itemDetailPanelR] = Pos(itemDetailPanelR);
        if (itemDetailPanelL) _originalDetailPositions[itemDetailPanelL] = Pos(itemDetailPanelL);

        if (itemDetailPanelE)
        {
            if (exoticDetailRainbowBackground)
                _originalDetailPositions[itemDetailPanelE] = Pos(exoticDetailRainbowBackground);
            else
                _originalDetailPositions[itemDetailPanelE] = Pos(itemDetailPanelE);
        }
    }

    void CloseAllDetailPanelsImmediate()
    {
        if (itemDetailPanelC) { itemDetailPanelC.SetActive(false); ResetPanelPosition(itemDetailPanelC); }
        if (itemDetailPanelR) { itemDetailPanelR.SetActive(false); ResetPanelPosition(itemDetailPanelR); }
        if (itemDetailPanelL) { itemDetailPanelL.SetActive(false); ResetPanelPosition(itemDetailPanelL); }

        if (itemDetailPanelE)
        {
            if (exoticDetailRainbowBackground)
            {
                exoticDetailRainbowBackground.SetActive(false);
                ResetPanelPosition(itemDetailPanelE);
            }
            else
            {
                itemDetailPanelE.SetActive(false);
                ResetPanelPosition(itemDetailPanelE);
            }
        }

        _currentActiveDetailPanel = null;
        _currentDetailItem = null;
    }

    void ResetPanelPosition(GameObject panelKey)
    {
        if (!_originalDetailPositions.TryGetValue(panelKey, out var orig)) return;
        var animObj = GetAnimObj(panelKey);
        if (!animObj) return;
        var rt = animObj.GetComponent<RectTransform>();
        rt.anchoredPosition = orig;
    }

    GameObject GetAnimObj(GameObject panelKey)
    {
        if (panelKey == itemDetailPanelE && exoticDetailRainbowBackground)
            return exoticDetailRainbowBackground;
        return panelKey;
    }

    GameObject GetPanelForItem(EquipableItem item) => item.rarity switch
    {
        EquipmentRarity.Common => itemDetailPanelC,
        EquipmentRarity.Rare => itemDetailPanelR,
        EquipmentRarity.Legendary => itemDetailPanelL,
        EquipmentRarity.Exotic => itemDetailPanelE,
        _ => itemDetailPanelC
    };

    void OnCardClicked(EquipableItem item)
    {
        // Toggle if same item
        if (_currentDetailItem == item && _currentActiveDetailPanel && _currentActiveDetailPanel.activeSelf)
        {
            CloseDetailPanel();
            return;
        }

        // Open/switch
        var targetPanel = GetPanelForItem(item);

        if (_anim != null) StopCoroutine(_anim);

        if (_currentActiveDetailPanel != null && _currentActiveDetailPanel != targetPanel && _currentActiveDetailPanel.activeSelf)
        {
            _anim = StartCoroutine(SwitchPanels(_currentActiveDetailPanel, targetPanel, item));
        }
        else
        {
            _currentActiveDetailPanel = targetPanel;
            _anim = StartCoroutine(SlideInPanel(targetPanel, item));
        }
    }

    public void CloseDetailPanel()
    {
        if (_currentActiveDetailPanel == null) return;
        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(SlideOutPanel(_currentActiveDetailPanel));
    }

    IEnumerator SlideInPanel(GameObject panelKey, EquipableItem item)
{
    EnsureDetailPanelsUnderMainPanels();
    _isAnimating = true;

    // Bind texts + configure pager/arrows BEFORE activation so nothing flashes in late
    RebindDetailTextsTo(panelKey);
    ConfigurePagerFor(item, panelKey);
    PopulateDetailTexts(item);

    var animObj = GetAnimObj(panelKey);
    var rt = animObj.GetComponent<RectTransform>();
    Vector2 finalPos = _originalDetailPositions[panelKey];
    Vector2 startPos = new Vector2(finalPos.x + slideStartOffset, finalPos.y);

    animObj.SetActive(true);
    rt.anchoredPosition = startPos;

    float t = 0f;
    while (t < slideAnimationDuration)
    {
        t += Time.deltaTime;
        float k = slideCurve.Evaluate(t / slideAnimationDuration);
        rt.anchoredPosition = Vector2.Lerp(startPos, finalPos, k);
        yield return null;
    }
    rt.anchoredPosition = finalPos;

    _isAnimating = false; _anim = null;
    _currentDetailItem = item;
    _currentActiveDetailPanel = panelKey;
}

IEnumerator SwitchPanels(GameObject oldKey, GameObject newKey, EquipableItem item)
{
    _isAnimating = true;

    var oldObj = GetAnimObj(oldKey);
    var newObj = GetAnimObj(newKey);

    var oldRT = oldObj.GetComponent<RectTransform>();
    var newRT = newObj.GetComponent<RectTransform>();

    Vector2 oldStart = oldRT.anchoredPosition;
    Vector2 oldEnd   = new Vector2(_originalDetailPositions[oldKey].x + slideStartOffset, _originalDetailPositions[oldKey].y);

    Vector2 newFinal = _originalDetailPositions[newKey];
    Vector2 newStart = new Vector2(newFinal.x + slideStartOffset, newFinal.y);

    // Pre-bind + configure new panel BEFORE it appears
    RebindDetailTextsTo(newKey);
    ConfigurePagerFor(item, newKey);
    PopulateDetailTexts(item);

    newObj.SetActive(true);
    newRT.anchoredPosition = newStart;

    float t = 0f;
    while (t < slideAnimationDuration)
    {
        t += Time.deltaTime;
        float k = slideCurve.Evaluate(t / slideAnimationDuration);
        oldRT.anchoredPosition = Vector2.Lerp(oldStart, oldEnd, k);
        newRT.anchoredPosition = Vector2.Lerp(newStart, newFinal, k);
        yield return null;
    }

    oldRT.anchoredPosition = oldEnd;
    oldObj.SetActive(false);

    newRT.anchoredPosition = newFinal;

    _isAnimating = false; _anim = null;
    _currentActiveDetailPanel = newKey;
    _currentDetailItem = item;
}

IEnumerator SlideOutPanel(GameObject panelKey)
{
    _isAnimating = true;

    var animObj = GetAnimObj(panelKey);
    var rt = animObj.GetComponent<RectTransform>();
    Vector2 orig = _originalDetailPositions[panelKey];
    Vector2 endPos = new Vector2(orig.x + slideStartOffset, orig.y);
    Vector2 startPos = rt.anchoredPosition;

    float t = 0f;
    while (t < slideAnimationDuration)
    {
        t += Time.deltaTime;
        float k = slideCurve.Evaluate(t / slideAnimationDuration);
        rt.anchoredPosition = Vector2.Lerp(startPos, endPos, k);
        yield return null;
    }
    rt.anchoredPosition = endPos;

    // Reset pager so next open starts on page 0 even if this panel is re-enabled without a fresh script load
    var pager = panelKey ? panelKey.GetComponentInChildren<ItemDetailPager>(true) : null;
    if (pager) pager.ForceToFirstPage();

    animObj.SetActive(false);

    _currentActiveDetailPanel = null;
    _currentDetailItem = null;
    _isAnimating = false;
    _anim = null;
}

    void PopulateDetailTexts(EquipableItem item)
{
    if (item == null) return;

    // ----- DESCRIPTION (preserve preview if empty) -----
    bool hasDesc = !string.IsNullOrWhiteSpace(item.itemDescription);

    if (_currentPanelUI != null && _currentPanelUI.descriptionInput != null)
    {
        // Input field: set real text if present, otherwise blank so Placeholder shows
        if (hasDesc)
            _currentPanelUI.descriptionInput.SetTextWithoutNotify(item.itemDescription);
        else
            _currentPanelUI.descriptionInput.SetTextWithoutNotify(string.Empty);

        // Keep our local label pointing at the input’s text component
        descriptionText = _currentPanelUI.descriptionInput.textComponent;
    }
    else if (descriptionText != null)
    {
        // Plain label: only overwrite if we actually have a description.
        // If empty, do nothing so your pre-baked preview stays visible.
        if (hasDesc)
            descriptionText.text = item.itemDescription;
    }

    // ----- RARITY / TYPE LABELS -----
    string rarityLabel = item.rarity.ToString().ToUpperInvariant();
    string typeLabel = (item.category == EquipmentCategory.Weapon)
    ? item.weaponSubtype.ToString().ToUpperInvariant()   // "MELEE" or "RANGED"
    : item.category.ToString().ToUpperInvariant();       // "ACCESSORY", etc.

    UpdatePerCardTexts(rarityLabel, typeLabel);
    ToggleRarityTypeCards(item);

    // ----- X / Z MOVE LABELS -----
    const string dash = "—";
    string x = dash, z = dash;

    if (item.category == EquipmentCategory.Weapon)
    {
        if (item.weaponSubtype == WeaponSubtype.Ranged)
        {
            if (!string.IsNullOrWhiteSpace(item.ranged_XMove)) x = item.ranged_XMove;
            if (!string.IsNullOrWhiteSpace(item.ranged_ZMove)) z = item.ranged_ZMove;
        }
        else // Melee
        {
            if (!string.IsNullOrWhiteSpace(item.melee_XMove)) x = item.melee_XMove;
            if (!string.IsNullOrWhiteSpace(item.melee_ZMove)) z = item.melee_ZMove;
        }
    }

    if (xMoveText) xMoveText.text = x;
    if (zMoveText) zMoveText.text = z;

    UpdateEquipButtonLabelFor(item);
}

void UpdateEquipButtonLabelFor(EquipableItem item)
{
    void SetState(Button b, string text, bool interactable)
    {
        if (!b) return;
        var t = b.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (t) t.text = text;
        b.interactable = interactable;
    }

    if (item == null)
    {
        SetState(equipButtonC, "Equip", false);
        SetState(equipButtonR, "Equip", false);
        SetState(equipButtonL, "Equip", false);
        SetState(equipButtonE, "Equip", false);
        return;
    }

    bool owns = PlayerOwns(item);
    if (!owns)
    {
        SetState(equipButtonC, "Locked", false);
        SetState(equipButtonR, "Locked", false);
        SetState(equipButtonL, "Locked", false);
        SetState(equipButtonE, "Locked", false);
        return;
    }

    string label = IsAccessoryEquipped(item) ? "Unequip" : "Equip";
    SetState(equipButtonC, label, true);
    SetState(equipButtonR, label, true);
    SetState(equipButtonL, label, true);
    SetState(equipButtonE, label, true);
}

    void ToggleRarityTypeCards(EquipableItem item)
{
    // Hide all rarity/type cards first
    if (rarityCardC) rarityCardC.SetActive(false);
    if (rarityCardR) rarityCardR.SetActive(false);
    if (rarityCardL) rarityCardL.SetActive(false);
    if (rarityCardE) rarityCardE.SetActive(false);

    if (typeCardC) typeCardC.SetActive(false);
    if (typeCardR) typeCardR.SetActive(false);
    if (typeCardL) typeCardL.SetActive(false);
    if (typeCardE) typeCardE.SetActive(false);

    // Show the pair that matches THIS ITEM'S RARITY
    switch (item.rarity)
    {
        case EquipmentRarity.Common:
            if (rarityCardC) rarityCardC.SetActive(true);
            if (typeCardC)   typeCardC.SetActive(true);
            break;

        case EquipmentRarity.Rare:
            if (rarityCardR) rarityCardR.SetActive(true);
            if (typeCardR)   typeCardR.SetActive(true);
            break;

        case EquipmentRarity.Legendary:
            if (rarityCardL) rarityCardL.SetActive(true);
            if (typeCardL)   typeCardL.SetActive(true);
            break;

        case EquipmentRarity.Exotic:
            if (rarityCardE) rarityCardE.SetActive(true);
            if (typeCardE)   typeCardE.SetActive(true);
            break;
    }
}
}

#if UNITY_EDITOR
// ============================================================================
//                              CUSTOM INSPECTOR
// ============================================================================
[UnityEditor.CustomEditor(typeof(EquipmentManager))]
public class EquipmentManagerEditor : UnityEditor.Editor
{
    // --- inspector-only filter state ---
    private static int _inspectorStyleIndex = 0;   // 0 = All, then EquipmentStyle order
    private static string _inspectorSearch = "";
    private static System.Collections.Generic.List<string> _styleOptions;

    // Serialized fields (runtime hookups)
    UnityEditor.SerializedProperty equipmentDatabase;

    UnityEditor.SerializedProperty itemsScrollViewContent;
    UnityEditor.SerializedProperty itemCardPrefabC, itemCardPrefabR, itemCardPrefabL, itemCardPrefabE;

    UnityEditor.SerializedProperty rarityCardC, typeCardC, rarityCardR, typeCardR, rarityCardL, typeCardL, rarityCardE, typeCardE;
    UnityEditor.SerializedProperty rarityCardCText, typeCardCText, rarityCardRText, typeCardRText, rarityCardLText, typeCardLText, rarityCardEText, typeCardEText;

    UnityEditor.SerializedProperty filterDropdown, searchInputField, itemsScrollRect, equipPanelCloseButton;

    UnityEditor.SerializedProperty itemDetailPanelC, itemDetailPanelR, itemDetailPanelL, itemDetailPanelE, exoticDetailRainbowBackground;

    UnityEditor.SerializedProperty equipButtonC, equipButtonR, equipButtonL, equipButtonE;

    UnityEditor.SerializedProperty slideAnimationDuration, slideStartOffset, positionOffset, slideCurve;

    // In EquipmentManagerEditor (the custom inspector), REPLACE OnEnable with this:
void OnEnable()
{
    // Database
    equipmentDatabase = serializedObject.FindProperty(nameof(EquipmentManager.equipmentDatabase));

    // List container + card prefabs
    itemsScrollViewContent = serializedObject.FindProperty(nameof(EquipmentManager.itemsScrollViewContent));
    itemCardPrefabC        = serializedObject.FindProperty(nameof(EquipmentManager.itemCardPrefabC));
    itemCardPrefabR        = serializedObject.FindProperty(nameof(EquipmentManager.itemCardPrefabR));
    itemCardPrefabL        = serializedObject.FindProperty(nameof(EquipmentManager.itemCardPrefabL));
    itemCardPrefabE        = serializedObject.FindProperty(nameof(EquipmentManager.itemCardPrefabE));

    // Per-rarity cards
    rarityCardC = serializedObject.FindProperty(nameof(EquipmentManager.rarityCardC));
    typeCardC   = serializedObject.FindProperty(nameof(EquipmentManager.typeCardC));
    rarityCardR = serializedObject.FindProperty(nameof(EquipmentManager.rarityCardR));
    typeCardR   = serializedObject.FindProperty(nameof(EquipmentManager.typeCardR));
    rarityCardL = serializedObject.FindProperty(nameof(EquipmentManager.rarityCardL));
    typeCardL   = serializedObject.FindProperty(nameof(EquipmentManager.typeCardL));
    rarityCardE = serializedObject.FindProperty(nameof(EquipmentManager.rarityCardE));
    typeCardE   = serializedObject.FindProperty(nameof(EquipmentManager.typeCardE));

    // Per-rarity label texts
    rarityCardCText = serializedObject.FindProperty(nameof(EquipmentManager.rarityCardCText));
    typeCardCText   = serializedObject.FindProperty(nameof(EquipmentManager.typeCardCText));
    rarityCardRText = serializedObject.FindProperty(nameof(EquipmentManager.rarityCardRText));
    typeCardRText   = serializedObject.FindProperty(nameof(EquipmentManager.typeCardRText));
    rarityCardLText = serializedObject.FindProperty(nameof(EquipmentManager.rarityCardLText));
    typeCardLText   = serializedObject.FindProperty(nameof(EquipmentManager.typeCardLText));
    rarityCardEText = serializedObject.FindProperty(nameof(EquipmentManager.rarityCardEText));
    typeCardEText   = serializedObject.FindProperty(nameof(EquipmentManager.typeCardEText));

    // List / search UI
    filterDropdown        = serializedObject.FindProperty(nameof(EquipmentManager.filterDropdown));
    searchInputField      = serializedObject.FindProperty(nameof(EquipmentManager.searchInputField));
    itemsScrollRect       = serializedObject.FindProperty(nameof(EquipmentManager.itemsScrollRect));
    equipPanelCloseButton = serializedObject.FindProperty(nameof(EquipmentManager.equipPanelCloseButton));

    // Detail panels
    itemDetailPanelC              = serializedObject.FindProperty(nameof(EquipmentManager.itemDetailPanelC));
    itemDetailPanelR              = serializedObject.FindProperty(nameof(EquipmentManager.itemDetailPanelR));
    itemDetailPanelL              = serializedObject.FindProperty(nameof(EquipmentManager.itemDetailPanelL));
    itemDetailPanelE              = serializedObject.FindProperty(nameof(EquipmentManager.itemDetailPanelE));
    exoticDetailRainbowBackground = serializedObject.FindProperty(nameof(EquipmentManager.exoticDetailRainbowBackground));

    // Equip buttons
    equipButtonC = serializedObject.FindProperty(nameof(EquipmentManager.equipButtonC));
    equipButtonR = serializedObject.FindProperty(nameof(EquipmentManager.equipButtonR));
    equipButtonL = serializedObject.FindProperty(nameof(EquipmentManager.equipButtonL));
    equipButtonE = serializedObject.FindProperty(nameof(EquipmentManager.equipButtonE));

    // Animation settings
    slideAnimationDuration = serializedObject.FindProperty(nameof(EquipmentManager.slideAnimationDuration));
    slideStartOffset       = serializedObject.FindProperty(nameof(EquipmentManager.slideStartOffset));
    positionOffset         = serializedObject.FindProperty(nameof(EquipmentManager.positionOffset));
    slideCurve             = serializedObject.FindProperty(nameof(EquipmentManager.slideCurve));

    // Build inspector filter options
    _styleOptions = new System.Collections.Generic.List<string> { "All" };
    _styleOptions.AddRange(System.Enum.GetNames(typeof(EquipmentStyle)));
}

private static string GetFoldoutLabel(SerializedProperty element, int index)
{
    // Prefer Item Name, then Inspector Name, else fallback
    string itemName      = element.FindPropertyRelative("itemName")?.stringValue ?? "";
    string inspectorName = element.FindPropertyRelative("inspectorName")?.stringValue ?? "";

    if (!string.IsNullOrWhiteSpace(itemName))      return itemName;
    if (!string.IsNullOrWhiteSpace(inspectorName)) return inspectorName;

    return $"Item {index + 1}";
}

    public override void OnInspectorGUI()
{
    // Safety: if the style list wasn't built (domain reload quirks), rebuild here.
    if (_styleOptions == null || _styleOptions.Count == 0)
    {
        _styleOptions = new System.Collections.Generic.List<string> { "All" };
        _styleOptions.AddRange(System.Enum.GetNames(typeof(EquipmentStyle)));
    }

    serializedObject.Update();

    // Local helper that won't NRE if a binding failed.
    void PF(UnityEditor.SerializedProperty p, string label)
    {
        if (p != null)
            UnityEditor.EditorGUILayout.PropertyField(p, new UnityEngine.GUIContent(label));
        else
            UnityEditor.EditorGUILayout.HelpBox($"Missing SerializedProperty for \"{label}\". " +
                $"Check the field name on EquipmentManager.", UnityEditor.MessageType.Warning);
    }

    // ===== Inspector-only filters =====
    DrawInspectorFilters();

    // ===== Database list (filtered) =====
    UnityEditor.EditorGUILayout.Space(6);
    UnityEditor.EditorGUILayout.LabelField("Equipment Database (ALL available items)", UnityEditor.EditorStyles.boldLabel);
    if (equipmentDatabase != null)
        DrawDatabaseListFiltered(equipmentDatabase);
    else
        UnityEditor.EditorGUILayout.HelpBox("equipmentDatabase property not found.", UnityEditor.MessageType.Warning);
    UnityEditor.EditorGUILayout.Space(8);

    // ===== UI References (runtime hookups) =====
    UnityEditor.EditorGUILayout.LabelField("UI References", UnityEditor.EditorStyles.boldLabel);

    PF(itemsScrollViewContent, "ItemsScrollView Content");
    PF(itemCardPrefabC,       "Item Card Prefab C");
    PF(itemCardPrefabR,       "Item Card Prefab R");
    PF(itemCardPrefabL,       "Item Card Prefab L");
    PF(itemCardPrefabE,       "Item Card Prefab E");

    UnityEditor.EditorGUILayout.Space(4);
    UnityEditor.EditorGUILayout.LabelField("Per-Rarity Cards", UnityEditor.EditorStyles.boldLabel);
    PF(rarityCardC,           "Rarity Card C");
    PF(rarityCardCText,       "Rarity Card C Text");
    PF(typeCardC,             "Type Card C");
    PF(typeCardCText,         "Type Card C Text");

    PF(rarityCardR,           "Rarity Card R");
    PF(rarityCardRText,       "Rarity Card R Text");
    PF(typeCardR,             "Type Card R");
    PF(typeCardRText,         "Type Card R Text");

    PF(rarityCardL,           "Rarity Card L");
    PF(rarityCardLText,       "Rarity Card L Text");
    PF(typeCardL,             "Type Card L");
    PF(typeCardLText,         "Type Card L Text");

    PF(rarityCardE,           "Rarity Card E");
    PF(rarityCardEText,       "Rarity Card E Text");
    PF(typeCardE,             "Type Card E");
    PF(typeCardEText,         "Type Card E Text");

    UnityEditor.EditorGUILayout.Space(4);
    PF(filterDropdown,        "Category Filter Dropdown");
    PF(searchInputField,      "Search Input Field");
    PF(itemsScrollRect,       "Items Scroll Rect");
    PF(equipPanelCloseButton, "Equip Panel Close Button");

    UnityEditor.EditorGUILayout.Space(4);
    PF(itemDetailPanelC,            "Item Detail Panel C");
    PF(itemDetailPanelR,            "Item Detail Panel R");
    PF(itemDetailPanelL,            "Item Detail Panel L");
    PF(itemDetailPanelE,            "Item Detail Panel E");
    PF(exoticDetailRainbowBackground,"Exotic Rainbow BG");

    UnityEditor.EditorGUILayout.Space(4);
    PF(equipButtonC, "Equip Button C");
    PF(equipButtonR, "Equip Button R");
    PF(equipButtonL, "Equip Button L");
    PF(equipButtonE, "Equip Button E");

    UnityEditor.EditorGUILayout.Space(6);
    PF(slideAnimationDuration, "Slide Animation Duration");
    PF(slideStartOffset,       "Slide Start Offset");
    PF(positionOffset,         "Position Offset");
    PF(slideCurve,             "Slide Curve");

    serializedObject.ApplyModifiedProperties();
}

    // -------- Inspector-only UI --------
    void DrawInspectorFilters()
    {
        UnityEditor.EditorGUILayout.BeginVertical("HelpBox");
        UnityEditor.EditorGUILayout.LabelField("Database Filters (Inspector-only)", UnityEditor.EditorStyles.boldLabel);

        _inspectorStyleIndex = UnityEditor.EditorGUILayout.Popup("Race / Style", _inspectorStyleIndex, _styleOptions.ToArray());
        _inspectorSearch = UnityEditor.EditorGUILayout.TextField("Search Name", _inspectorSearch ?? "");

        UnityEditor.EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Clear Filters", GUILayout.Width(110)))
        {
            _inspectorStyleIndex = 0;
            _inspectorSearch = "";
            GUI.FocusControl(null);
        }
        UnityEditor.EditorGUILayout.EndHorizontal();

        UnityEditor.EditorGUILayout.EndVertical();
    }

    // REPLACE your existing DrawDatabaseListFiltered with this
void DrawDatabaseListFiltered(SerializedProperty listProp)
{
    for (int i = 0; i < listProp.arraySize; i++)
    {
        var element = listProp.GetArrayElementAtIndex(i);
        if (!PassesInspectorFilter(element)) continue;

        EditorGUILayout.BeginVertical("HelpBox");
        EditorGUILayout.BeginHorizontal();

        string label = GetFoldoutLabel(element, i);
        element.isExpanded = EditorGUILayout.Foldout(element.isExpanded, label, true);

        if (GUILayout.Button("X", GUILayout.Width(20)))
        {
            listProp.DeleteArrayElementAtIndex(i);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return; // indices changed; redraw next frame
        }

        EditorGUILayout.EndHorizontal();

        if (element.isExpanded)
        {
            DrawEquipableItem(element);
        }

        EditorGUILayout.EndVertical();
    }

    if (GUILayout.Button("Add New Item"))
    {
        int idx = listProp.arraySize;
        listProp.InsertArrayElementAtIndex(idx);
        var el = listProp.GetArrayElementAtIndex(idx);
        el.FindPropertyRelative("melee_MBCAmount").intValue = 1;
        var dmgList = el.FindPropertyRelative("melee_MBCDamages");
        dmgList.arraySize = 1;
        dmgList.GetArrayElementAtIndex(0).intValue = 0;
    }
}

    bool PassesInspectorFilter(UnityEditor.SerializedProperty element)
{
    if (_inspectorStyleIndex > 0)
    {
        int styleIdx = element.FindPropertyRelative("style").enumValueIndex;
        if (styleIdx != (_inspectorStyleIndex - 1)) return false;
    }

    if (!string.IsNullOrEmpty(_inspectorSearch))
    {
        string display = element.FindPropertyRelative("itemName").stringValue ?? "";
        string key     = element.FindPropertyRelative("inspectorName").stringValue ?? "";
        if (display.IndexOf(_inspectorSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
            key.IndexOf(_inspectorSearch,     StringComparison.OrdinalIgnoreCase) < 0)
            return false;
    }

    return true;
}

    void DrawEquipableItem(UnityEditor.SerializedProperty el)
{
    // Removed the redundant "Basic Info" label
    UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("itemName"));
    UnityEditor.EditorGUILayout.PropertyField(
        el.FindPropertyRelative("inspectorName"),
        new UnityEngine.GUIContent("Inspector Name (key)")
    );
    UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("itemDescription"));
    UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("category"));
    UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("rarity"));
    UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("style"));

    UnityEditor.EditorGUILayout.Space(4);
    UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("itemIcon"));

    // ---- Weapon-only section ----
    var category = (EquipmentCategory)el.FindPropertyRelative("category").enumValueIndex;
    if (category == EquipmentCategory.Weapon)
    {
        UnityEditor.EditorGUILayout.Space(4);
        UnityEditor.EditorGUILayout.LabelField("Weapon Stats", UnityEditor.EditorStyles.boldLabel);
        UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("weaponSubtype"));

        var subtype = (WeaponSubtype)el.FindPropertyRelative("weaponSubtype").enumValueIndex;
        if (subtype == WeaponSubtype.Ranged)
        {
            UnityEditor.EditorGUILayout.LabelField("Ranged", UnityEditor.EditorStyles.miniBoldLabel);
            UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_MBCDamage"),
                new UnityEngine.GUIContent("MBC Damage"));
            UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_MBCCooldown"),
                new UnityEngine.GUIContent("MBC Cooldown"));
            UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_CursorSprite"),
                new UnityEngine.GUIContent("Cursor Sprite"));
            UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_XMove"),
                new UnityEngine.GUIContent("X Move"));
            UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_ZMove"),
                new UnityEngine.GUIContent("Z Move"));
        }
        else // Melee
        {
            UnityEditor.EditorGUILayout.LabelField("Melee", UnityEditor.EditorStyles.miniBoldLabel);
            var amountProp = el.FindPropertyRelative("melee_MBCAmount");
            UnityEditor.EditorGUILayout.PropertyField(amountProp, new UnityEngine.GUIContent("MBC Amount"));

            var dmgList = el.FindPropertyRelative("melee_MBCDamages");
            if (amountProp.intValue < 0) amountProp.intValue = 0;
            while (dmgList.arraySize < amountProp.intValue) dmgList.InsertArrayElementAtIndex(dmgList.arraySize);
            while (dmgList.arraySize > amountProp.intValue && dmgList.arraySize > 0) dmgList.DeleteArrayElementAtIndex(dmgList.arraySize - 1);

            for (int i = 0; i < dmgList.arraySize; i++)
            {
                UnityEditor.EditorGUILayout.PropertyField(
                    dmgList.GetArrayElementAtIndex(i),
                    new UnityEngine.GUIContent($"MBC{i + 1} Damage"));
            }

            UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("melee_MBCComboCooldown"),
                new UnityEngine.GUIContent("MBC Combo Cooldown"));
            UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("melee_XMove"),
                new UnityEngine.GUIContent("X Move"));
            UnityEditor.EditorGUILayout.PropertyField(el.FindPropertyRelative("melee_ZMove"),
                new UnityEngine.GUIContent("Z Move"));
        }
    }

    // ---- Attachments (kept) ----
    UnityEditor.EditorGUILayout.Space(4);
    var attachmentsProp = el.FindPropertyRelative("attachments");
    UnityEditor.EditorGUILayout.PropertyField(attachmentsProp, new UnityEngine.GUIContent("Attachments"), true);
}
}
#endif