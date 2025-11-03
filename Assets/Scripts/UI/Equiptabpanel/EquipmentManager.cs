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

#region Attachment Runtime (unchanged behavior)
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
            pos = transform.localPosition;
            scl = transform.localScale;

            if (space == AttachmentPoint.RotationSpace.BoneLocal)
            {
                eul = transform.localEulerAngles;
            }
            else
            {
                Quaternion L = transform.localRotation;
                Quaternion worldNow = bone.rotation * L;
                Quaternion spaceRot = GetSpaceRotation();
                Quaternion offsetWorld = worldNow * Quaternion.Inverse(bone.rotation);
                Quaternion offsetInSpace = Quaternion.Inverse(spaceRot) * offsetWorld * spaceRot;
                eul = offsetInSpace.eulerAngles;
            }
        }

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
#endregion

#region Per-Style UI Bucket
[Serializable]
public class StyleUIRefs
{
    [Tooltip("Which race/art style this UI set belongs to.")]
    public EquipmentStyle style = EquipmentStyle.Spook;

    [Header("Item Card Prefabs (per rarity)")]
    public GameObject itemCardPrefabC;  // Common
    public GameObject itemCardPrefabR;  // Rare
    public GameObject itemCardPrefabL;  // Legendary
    public GameObject itemCardPrefabE;  // Exotic

    [Header("Per-Rarity Cards + Label Texts")]
    public GameObject rarityCardC; public TMP_Text rarityCardCText;
    public GameObject typeCardC;   public TMP_Text typeCardCText;

    public GameObject rarityCardR; public TMP_Text rarityCardRText;
    public GameObject typeCardR;   public TMP_Text typeCardRText;

    public GameObject rarityCardL; public TMP_Text rarityCardLText;
    public GameObject typeCardL;   public TMP_Text typeCardLText;

    public GameObject rarityCardE; public TMP_Text rarityCardEText;
    public GameObject typeCardE;   public TMP_Text typeCardEText;

    [Header("Detail Panels")]
    public GameObject itemDetailPanelC;
    public GameObject itemDetailPanelR;
    public GameObject itemDetailPanelL;
    public GameObject itemDetailPanelE;
    public GameObject exoticDetailRainbowBackground;

    [Header("Equip Buttons")]
    public Button equipButtonC;
    public Button equipButtonR;
    public Button equipButtonL;
    public Button equipButtonE;
}
#endregion

public class EquipmentManager : MonoBehaviour
{
    [Header("Listing Options")]
    public bool showOnlyOwnedItems = false;

    // =========================================================
    //                  EQUIPMENT DATABASE
    // =========================================================
    [Header("Equipment Database (ALL available items)")]
    public List<EquipableItem> equipmentDatabase = new List<EquipableItem>();

    // =========================================================
    //                     UI REFERENCES
    // =========================================================

    [Header("UI References (Shared)")]
    public Transform itemsScrollViewContent;     // previously Items Container
    public TMP_Dropdown filterDropdown;
    public TMP_InputField searchInputField;
    public ScrollRect itemsScrollRect;
    public Button equipPanelCloseButton;
    public TMP_Dropdown raceDropdown;

    [Header("UI References (Per Style)")]
    public List<StyleUIRefs> styleUI = new List<StyleUIRefs>
    {
        new StyleUIRefs{ style = EquipmentStyle.Spook },
        new StyleUIRefs{ style = EquipmentStyle.Cyborg }
    };

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

// === Portrait / external sync hooks ===
public event System.Action<EquipableItem> AccessoryEquipped;
public event System.Action<EquipableItem> AccessoryUnequipped;

// Optional getter for current accessory (portrait can pull on start)
public EquipableItem GetCurrentlyEquippedAccessory() => _equippedAccessory;

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

    // Current active per-style UI context (resolved for the open detail panel)
    private StyleUIRefs _currentUI;

    // =========================================================
    //                        LIFECYCLE
    // =========================================================
    void OnValidate()
    {
        if (equipmentDatabase != null)
            foreach (var it in equipmentDatabase) it?.EnsureMeleeListSize();
    }

    void Start()
    {
        TryResolvePlayerRefs();

        CacheOriginalDetailPositions_AllStyles();
        EnsureDetailPanelsUnderMainPanels_AllStyles();
        WireUI();

        var pe = PlayerEquipment.Instance;
        if (pe) pe.InventoryChanged += RefreshDisplay;

        RefreshDisplay();
        CloseAllDetailPanelsImmediate_AllStyles();
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

        // equip buttons are wired when a style-panel is opened (per-style)
    }

    // =========================================================
    //                    STYLE UI HELPERS
    // =========================================================
    StyleUIRefs GetUI(EquipmentStyle style)
    {
        for (int i = 0; i < styleUI.Count; i++)
        {
            var s = styleUI[i];
            if (s != null && s.style == style) return s;
        }
        return styleUI.Count > 0 ? styleUI[0] : null;
    }

    GameObject GetCardPrefab(EquipmentStyle style, EquipmentRarity r)
    {
        var ui = GetUI(style);
        if (ui == null) return null;
        switch (r)
        {
            case EquipmentRarity.Common:    return ui.itemCardPrefabC;
            case EquipmentRarity.Rare:      return ui.itemCardPrefabR;
            case EquipmentRarity.Legendary: return ui.itemCardPrefabL;
            case EquipmentRarity.Exotic:    return ui.itemCardPrefabE;
            default:                        return ui.itemCardPrefabC;
        }
    }

    GameObject GetPanelForItem(EquipableItem item)
    {
        var ui = GetUI(item.style);
        if (ui == null) return null;

        switch (item.rarity)
        {
            case EquipmentRarity.Common:    return ui.itemDetailPanelC;
            case EquipmentRarity.Rare:      return ui.itemDetailPanelR;
            case EquipmentRarity.Legendary: return ui.itemDetailPanelL;
            case EquipmentRarity.Exotic:    return ui.itemDetailPanelE;
            default:                        return ui.itemDetailPanelC;
        }
    }

    GameObject GetAnimObj(StyleUIRefs ui, GameObject panelKey)
    {
        if (panelKey == ui.itemDetailPanelE && ui.exoticDetailRainbowBackground)
            return ui.exoticDetailRainbowBackground;
        return panelKey;
    }

    void EnsureDetailPanelsUnderMainPanels(StyleUIRefs ui)
    {
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

        if (ui == null) return;

        SetOrder(ui.itemDetailPanelC, -20);
        SetOrder(ui.itemDetailPanelR, -20);
        SetOrder(ui.itemDetailPanelL, -20);
        SetOrder(ui.itemDetailPanelE, -20);
        if (ui.exoticDetailRainbowBackground) SetOrder(ui.exoticDetailRainbowBackground, -25);

        var menuManager = FindObjectOfType<MenuManager>();
        if (menuManager != null && menuManager.equipTabPanel != null)
        {
            var equipCv = menuManager.equipTabPanel.GetComponent<Canvas>();
            if (!equipCv)
            {
                equipCv = menuManager.equipTabPanel.AddComponent<Canvas>();
                equipCv.overrideSorting = true;
            }
            equipCv.sortingOrder = 1;
            if (!menuManager.equipTabPanel.GetComponent<GraphicRaycaster>())
                menuManager.equipTabPanel.AddComponent<GraphicRaycaster>();
        }
    }

    void EnsureDetailPanelsUnderMainPanels_AllStyles()
    {
        for (int i = 0; i < styleUI.Count; i++)
            EnsureDetailPanelsUnderMainPanels(styleUI[i]);
    }

    void CacheOriginalDetailPositions_AllStyles()
    {
        _originalDetailPositions.Clear();

        Vector2 Pos(GameObject go) =>
            go ? go.GetComponent<RectTransform>().anchoredPosition + positionOffset : Vector2.zero;

        foreach (var ui in styleUI)
        {
            if (ui == null) continue;
            if (ui.itemDetailPanelC) _originalDetailPositions[ui.itemDetailPanelC] = Pos(ui.itemDetailPanelC);
            if (ui.itemDetailPanelR) _originalDetailPositions[ui.itemDetailPanelR] = Pos(ui.itemDetailPanelR);
            if (ui.itemDetailPanelL) _originalDetailPositions[ui.itemDetailPanelL] = Pos(ui.itemDetailPanelL);

            if (ui.itemDetailPanelE)
            {
                if (ui.exoticDetailRainbowBackground)
                    _originalDetailPositions[ui.itemDetailPanelE] = Pos(ui.exoticDetailRainbowBackground);
                else
                    _originalDetailPositions[ui.itemDetailPanelE] = Pos(ui.itemDetailPanelE);
            }
        }
    }

    void CloseAllDetailPanelsImmediate_AllStyles()
    {
        foreach (var ui in styleUI)
        {
            if (ui == null) continue;

            void ResetPanel(GameObject p, GameObject animKey = null)
            {
                if (!p) return;
                p.SetActive(false);
                if (_originalDetailPositions.TryGetValue(p, out var orig))
                {
                    var ak = animKey ? animKey : p;
                    if (ak)
                    {
                        var rt = ak.GetComponent<RectTransform>();
                        if (rt) rt.anchoredPosition = orig;
                    }
                }
            }

            ResetPanel(ui.itemDetailPanelC);
            ResetPanel(ui.itemDetailPanelR);
            ResetPanel(ui.itemDetailPanelL);

            if (ui.itemDetailPanelE)
            {
                if (ui.exoticDetailRainbowBackground)
                {
                    ui.exoticDetailRainbowBackground.SetActive(false);
                    ResetPanel(ui.itemDetailPanelE, ui.exoticDetailRainbowBackground);
                }
                else
                {
                    ResetPanel(ui.itemDetailPanelE);
                }
            }
        }

        _currentActiveDetailPanel = null;
        _currentDetailItem = null;
        _currentUI = null;
        _anim = null;
        _isAnimating = false;
    }

// ---- Back-compat for MenuManager ----
// Old signature expected by MenuManager: make it public & no args.
public void EnsureDetailPanelsUnderMainPanels()
{
    // Re-apply layering for all style-specific detail panels
    EnsureDetailPanelsUnderMainPanels_AllStyles();
}

    void ResetPanelPosition(StyleUIRefs ui, GameObject panelKey)
    {
        if (!_originalDetailPositions.TryGetValue(panelKey, out var orig)) return;
        var animObj = GetAnimObj(ui, panelKey);
        if (!animObj) return;
        var rt = animObj.GetComponent<RectTransform>();
        rt.anchoredPosition = orig;
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

    public void ShowItemDetail(EquipableItem item) => OnCardClicked(item);

    public void OnAllMenusClosed()
    {
        if (_currentActiveDetailPanel != null && _currentActiveDetailPanel.activeSelf && _currentUI != null)
        {
            StartCoroutine(SlideDownWithEquipPanel(_currentUI, _currentActiveDetailPanel));
        }
        else
        {
            CloseAllDetailPanelsImmediate_AllStyles();
        }
    }

    void FilterDatabase()
    {
        string search = (searchInputField ? searchInputField.text : "").Trim().ToLower();
        int categoryFilter = filterDropdown ? filterDropdown.value : 0;

        var pe = PlayerEquipment.Instance;
        if (pe == null) { _filtered = new List<EquipableItem>(); return; }

        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var inv = pe.Inventory;
        for (int i = 0; i < inv.Count; i++)
            if (!order.ContainsKey(inv[i])) order[inv[i]] = i;

        int IndexFor(EquipableItem it)
        {
            string key = !string.IsNullOrWhiteSpace(it.inspectorName) ? it.inspectorName : it.itemName;
            if (key == null) return int.MaxValue;

            if (order.TryGetValue(key, out int idx)) return idx;
            if (!string.Equals(key, it.itemName, StringComparison.Ordinal) &&
                order.TryGetValue(it.itemName ?? "", out idx)) return idx;

            return int.MaxValue;
        }

        _filtered = equipmentDatabase
            .Where(it =>
            {
                if (it == null) return false;
                if (!PlayerOwns(it)) return false;

                if (categoryFilter == 1 && it.category != EquipmentCategory.Weapon) return false;
                if (categoryFilter == 2 && it.category != EquipmentCategory.Accessory) return false;

                if (!string.IsNullOrEmpty(search))
                {
                    string display = (it.itemName ?? "").ToLower();
                    string key     = (it.inspectorName ?? "").ToLower();
                    if (!display.Contains(search) && !key.Contains(search)) return false;
                }

                return true;
            })
            .OrderBy(it => IndexFor(it))
            .ThenBy(it => it.itemName ?? string.Empty)
            .ToList();
    }

    void SpawnCards()
    {
        if (itemsScrollViewContent == null) return;

        foreach (var item in _filtered)
        {
            var prefab = GetCardPrefab(item.style, item.rarity);
            if (prefab == null) continue;

            var card = Instantiate(prefab, itemsScrollViewContent);
            _spawnedCards.Add(card);

            TryPopulateCardUI(card, item);

            var btn = card.GetComponentInChildren<Button>(true);
            if (btn == null) btn = card.AddComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnCardClicked(item));
        }
    }

    void TryPopulateCardUI(GameObject card, EquipableItem item)
    {
        if (card == null || item == null) return;

        UnityEngine.UI.Image icon = null;
        var images = card.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        if (images != null && images.Length > 0)
        {
            icon = images.FirstOrDefault(i => i && i.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0);
            if (icon == null) icon = images.FirstOrDefault(i => i && i.gameObject != card);
            if (icon == null) icon = images[0];
        }

        if (icon != null)
        {
            icon.sprite = item.itemIcon;
            icon.enabled = (item.itemIcon != null);
            icon.preserveAspect = true;
        }

        var nameText = card.GetComponentsInChildren<TMPro.TMP_Text>(true)
                           .FirstOrDefault(t => t && (t.name.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                      t.name.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0));
        if (nameText != null)
            nameText.text = string.IsNullOrWhiteSpace(item.itemName) ? item.inspectorName : item.itemName;
    }

    void ClearCards()
    {
        foreach (var go in _spawnedCards) if (go) Destroy(go);
        _spawnedCards.Clear();
    }

    // =========================================================
    //                      DETAIL PANELS
    // =========================================================
    void RebindDetailTextsTo(StyleUIRefs ui, GameObject panelKey)
    {
        _currentPanelUI = panelKey ? panelKey.GetComponentInChildren<ItemDetailPanelUI>(true) : null;

        xMoveText       = _currentPanelUI ? _currentPanelUI.xMoveText       : null;
        zMoveText       = _currentPanelUI ? _currentPanelUI.zMoveText       : null;

        if (_currentPanelUI != null && _currentPanelUI.descriptionInput != null)
            descriptionText = _currentPanelUI.descriptionInput.textComponent;
        else
            descriptionText = _currentPanelUI ? _currentPanelUI.descriptionText : null;

        // Wire equip buttons for this style set
        void Wire(Button b) { if (b) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(OnEquipButtonPressed); } }
        Wire(ui.equipButtonC);
        Wire(ui.equipButtonR);
        Wire(ui.equipButtonL);
        Wire(ui.equipButtonE);
    }

    void ConfigurePagerFor(EquipableItem item, GameObject panelKey)
    {
        if (_currentPanelUI == null) return;

        var pager = _currentPanelUI.pager;
        bool isAccessory = (item.category == EquipmentCategory.Accessory);

        if (_currentPanelUI.leftArrow)  _currentPanelUI.leftArrow.SetActive(!isAccessory);
        if (_currentPanelUI.rightArrow) _currentPanelUI.rightArrow.SetActive(!isAccessory);

        if (pager != null)
        {
            if (isAccessory)
            {
                pager.SetBounds(1, 1);
                pager.GoToPage(1, true);
            }
            else
            {
                pager.SetBounds(0, 1);
                pager.GoToPage(0, true);
            }
        }
    }

    void OnEquipButtonPressed()
    {
        if (_currentDetailItem == null) return;

        if (!PlayerOwns(_currentDetailItem))
        {
            Debug.Log($"'{_currentDetailItem.itemName}' is locked. Acquire it before equipping.");
            return;
        }

        if (_currentDetailItem.category == EquipmentCategory.Accessory)
        {
            if (IsAccessoryEquipped(_currentDetailItem))
                UnequipAccessory(_currentDetailItem);
            else
                EquipAccessory(_currentDetailItem);

            UpdateEquipButtonLabelFor(_currentUI, _currentDetailItem);
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

    TryResolvePlayerRefs();

    if (_equippedAccessory != null && _equippedAccessory != item)
        UnequipAccessory(_equippedAccessory);

    AttachItemPrefabs(item);

    _equippedAccessory = item;
    Debug.Log($"Equipped accessory '{item.itemName}'.");

    // 🔔 Notify listeners (portrait sync, etc.)
    AccessoryEquipped?.Invoke(item);
}

    static void SetRenderersEnabled(GameObject go, bool enabled)
    {
        if (!go) return;
        var rends = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
            rends[i].enabled = enabled;
    }

    public void SetAccessoriesVisible(bool visible)
    {
        foreach (var kvp in _spawnedAttachmentInstances)
        {
            var item = kvp.Key;
            if (item == null || item.category != EquipmentCategory.Accessory) continue;

            var list = kvp.Value;
            if (list == null) continue;

            for (int i = 0; i < list.Count; i++)
                SetRenderersEnabled(list[i], visible);
        }
    }

    void UnequipAccessory(EquipableItem item)
{
    if (item == null) return;

    TryResolvePlayerRefs();

    if (_equippedAccessory == item) _equippedAccessory = null;

    DetachItemPrefabs(item);

    Debug.Log($"Unequipped accessory '{item.itemName}'.");

    // 🔔 Notify listeners
    AccessoryUnequipped?.Invoke(item);
}

    void AttachItemPrefabs(EquipableItem item)
    {
        if (item == null || item.attachments == null) return;

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

            var oldRuntime = inst.GetComponent<AttachmentRuntime>();
            if (oldRuntime) Destroy(oldRuntime);

            spawned.Add(inst);
        }

        var rig = FindObjectOfType<PlayerCamera>();
        if (rig != null && rig.IsInFirstPerson && item.category == EquipmentCategory.Accessory)
        {
            var list = _spawnedAttachmentInstances[item];
            for (int i = 0; i < list.Count; i++)
                SetRenderersEnabled(list[i], false);
        }
    }

    void TryResolvePlayerRefs()
    {
        if (_playerMove != null && _playerAnimator != null) return;

        Transform root = PlayerEquipment.Instance ? PlayerEquipment.Instance.transform : null;

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

        if (_playerMove == null)
        {
            _playerMove = root ? root.GetComponentInChildren<PlayerMovement>(true) : null;
            if (_playerMove == null) _playerMove = FindObjectOfType<PlayerMovement>();
        }

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

    Transform ResolveAttachmentBone(AttachmentPoint ap)
    {
        return ap != null ? ap.bone : null;
    }

    void UpdatePerCardTexts(StyleUIRefs ui, string rarityLabel, string typeLabel)
    {
        if (ui == null) return;
        if (ui.rarityCardCText) ui.rarityCardCText.text = rarityLabel;
        if (ui.typeCardCText)   ui.typeCardCText.text   = typeLabel;

        if (ui.rarityCardRText) ui.rarityCardRText.text = rarityLabel;
        if (ui.typeCardRText)   ui.typeCardRText.text   = typeLabel;

        if (ui.rarityCardLText) ui.rarityCardLText.text = rarityLabel;
        if (ui.typeCardLText)   ui.typeCardLText.text   = typeLabel;

        if (ui.rarityCardEText) ui.rarityCardEText.text = rarityLabel;
        if (ui.typeCardEText)   ui.typeCardEText.text   = typeLabel;
    }

    public void CloseWholeEquipTab()
    {
        var menuManager = FindObjectOfType<MenuManager>();

        if (_currentActiveDetailPanel != null && _currentActiveDetailPanel.activeSelf && _currentUI != null)
        {
            EnsureDetailPanelsUnderMainPanels(_currentUI);
            StartCoroutine(SlideDownWithEquipPanel(_currentUI, _currentActiveDetailPanel));
        }

        if (menuManager != null)
        {
            menuManager.CloseMenu();
        }
    }

    IEnumerator SlideDownWithEquipPanel(StyleUIRefs ui, GameObject panelKey)
    {
        var animObj = GetAnimObj(ui, panelKey);
        if (!animObj) yield break;

        EnsureDetailPanelsUnderMainPanels(ui);

        var menuManager = FindObjectOfType<MenuManager>();
        var equipRT = (menuManager != null && menuManager.equipTabPanel != null)
            ? menuManager.equipTabPanel.GetComponent<RectTransform>()
            : null;

        var rt = animObj.GetComponent<RectTransform>();
        if (!rt || !equipRT) yield break;

        Vector2 detailStart = rt.anchoredPosition;
        Vector2 equipStart  = equipRT.anchoredPosition;

        float duration = 0.18f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Vector2 equipDelta = equipRT.anchoredPosition - equipStart;
            rt.anchoredPosition = detailStart + equipDelta;
            yield return null;
        }

        Vector2 finalEquipDelta = equipRT.anchoredPosition - equipStart;
        rt.anchoredPosition = detailStart + finalEquipDelta;

        CloseAllDetailPanelsImmediate_AllStyles();
    }

    bool PlayerOwns(EquipableItem item)
    {
        if (item == null) return false;
        var pe = PlayerEquipment.Instance;
        if (pe == null) return false;

        string key = !string.IsNullOrWhiteSpace(item.inspectorName) ? item.inspectorName : item.itemName;
        if (string.IsNullOrWhiteSpace(key)) return false;

        if (pe.HasInInventory(key)) return true;
        if (!string.Equals(key, item.itemName, StringComparison.Ordinal) && pe.HasInInventory(item.itemName)) return true;

        return false;
    }

    void OnCardClicked(EquipableItem item)
    {
        var ui = GetUI(item.style);
        if (ui == null) return;

        var targetPanel = GetPanelForItem(item);

        if (_currentDetailItem == item && _currentActiveDetailPanel && _currentActiveDetailPanel.activeSelf)
        {
            CloseDetailPanel();
            return;
        }

        if (_anim != null) StopCoroutine(_anim);

        if (_currentActiveDetailPanel != null && _currentActiveDetailPanel != targetPanel && _currentActiveDetailPanel.activeSelf)
        {
            _anim = StartCoroutine(SwitchPanels(ui, _currentActiveDetailPanel, targetPanel, item));
        }
        else
        {
            _currentUI = ui;
            _currentActiveDetailPanel = targetPanel;
            _anim = StartCoroutine(SlideInPanel(ui, targetPanel, item));
        }
    }

    public void CloseDetailPanel()
    {
        if (_currentActiveDetailPanel == null || _currentUI == null) return;
        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(SlideOutPanel(_currentUI, _currentActiveDetailPanel));
    }

    IEnumerator SlideInPanel(StyleUIRefs ui, GameObject panelKey, EquipableItem item)
    {
        EnsureDetailPanelsUnderMainPanels(ui);
        _isAnimating = true;

        RebindDetailTextsTo(ui, panelKey);
        ConfigurePagerFor(item, panelKey);
        PopulateDetailTexts(ui, item);

        var animObj = GetAnimObj(ui, panelKey);
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
        _currentUI = ui;
    }

    IEnumerator SwitchPanels(StyleUIRefs ui, GameObject oldKey, GameObject newKey, EquipableItem item)
    {
        _isAnimating = true;

        var oldObj = GetAnimObj(ui, oldKey);
        var newObj = GetAnimObj(ui, newKey);

        var oldRT = oldObj.GetComponent<RectTransform>();
        var newRT = newObj.GetComponent<RectTransform>();

        Vector2 oldStart = oldRT.anchoredPosition;
        Vector2 oldEnd   = new Vector2(_originalDetailPositions[oldKey].x + slideStartOffset, _originalDetailPositions[oldKey].y);

        Vector2 newFinal = _originalDetailPositions[newKey];
        Vector2 newStart = new Vector2(newFinal.x + slideStartOffset, newFinal.y);

        RebindDetailTextsTo(ui, newKey);
        ConfigurePagerFor(item, newKey);
        PopulateDetailTexts(ui, item);

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
        _currentUI = ui;
    }

    IEnumerator SlideOutPanel(StyleUIRefs ui, GameObject panelKey)
    {
        _isAnimating = true;

        var animObj = GetAnimObj(ui, panelKey);
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

        var pager = panelKey ? panelKey.GetComponentInChildren<ItemDetailPager>(true) : null;
        if (pager) pager.ForceToFirstPage();

        animObj.SetActive(false);

        _currentActiveDetailPanel = null;
        _currentDetailItem = null;
        _isAnimating = false;
        _anim = null;
        _currentUI = null;
    }

    void PopulateDetailTexts(StyleUIRefs ui, EquipableItem item)
    {
        if (item == null) return;

        bool hasDesc = !string.IsNullOrWhiteSpace(item.itemDescription);

        if (_currentPanelUI != null && _currentPanelUI.descriptionInput != null)
        {
            if (hasDesc)
                _currentPanelUI.descriptionInput.SetTextWithoutNotify(item.itemDescription);
            else
                _currentPanelUI.descriptionInput.SetTextWithoutNotify(string.Empty);

            descriptionText = _currentPanelUI.descriptionInput.textComponent;
        }
        else if (descriptionText != null)
        {
            if (hasDesc)
                descriptionText.text = item.itemDescription;
        }

        string rarityLabel = item.rarity.ToString().ToUpperInvariant();
        string typeLabel = (item.category == EquipmentCategory.Weapon)
            ? item.weaponSubtype.ToString().ToUpperInvariant()
            : item.category.ToString().ToUpperInvariant();

        UpdatePerCardTexts(ui, rarityLabel, typeLabel);
        ToggleRarityTypeCards(ui, item);

        const string dash = "—";
        string x = dash, z = dash;

        if (item.category == EquipmentCategory.Weapon)
        {
            if (item.weaponSubtype == WeaponSubtype.Ranged)
            {
                if (!string.IsNullOrWhiteSpace(item.ranged_XMove)) x = item.ranged_XMove;
                if (!string.IsNullOrWhiteSpace(item.ranged_ZMove)) z = item.ranged_ZMove;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(item.melee_XMove)) x = item.melee_XMove;
                if (!string.IsNullOrWhiteSpace(item.melee_ZMove)) z = item.melee_ZMove;
            }
        }

        if (xMoveText) xMoveText.text = x;
        if (zMoveText) zMoveText.text = z;

        UpdateEquipButtonLabelFor(ui, item);
    }

    void UpdateEquipButtonLabelFor(StyleUIRefs ui, EquipableItem item)
    {
        void SetState(Button b, string text, bool interactable)
        {
            if (!b) return;
            var t = b.GetComponentInChildren<TMP_Text>(true);
            if (t) t.text = text;
            b.interactable = interactable;
        }

        if (item == null)
        {
            SetState(ui.equipButtonC, "Equip", false);
            SetState(ui.equipButtonR, "Equip", false);
            SetState(ui.equipButtonL, "Equip", false);
            SetState(ui.equipButtonE, "Equip", false);
            return;
        }

        bool owns = PlayerOwns(item);
        if (!owns)
        {
            SetState(ui.equipButtonC, "Locked", false);
            SetState(ui.equipButtonR, "Locked", false);
            SetState(ui.equipButtonL, "Locked", false);
            SetState(ui.equipButtonE, "Locked", false);
            return;
        }

        string label = IsAccessoryEquipped(item) ? "Unequip" : "Equip";
        SetState(ui.equipButtonC, label, true);
        SetState(ui.equipButtonR, label, true);
        SetState(ui.equipButtonL, label, true);
        SetState(ui.equipButtonE, label, true);
    }

    void ToggleRarityTypeCards(StyleUIRefs ui, EquipableItem item)
    {
        if (ui.rarityCardC) ui.rarityCardC.SetActive(false);
        if (ui.rarityCardR) ui.rarityCardR.SetActive(false);
        if (ui.rarityCardL) ui.rarityCardL.SetActive(false);
        if (ui.rarityCardE) ui.rarityCardE.SetActive(false);

        if (ui.typeCardC) ui.typeCardC.SetActive(false);
        if (ui.typeCardR) ui.typeCardR.SetActive(false);
        if (ui.typeCardL) ui.typeCardL.SetActive(false);
        if (ui.typeCardE) ui.typeCardE.SetActive(false);

        switch (item.rarity)
        {
            case EquipmentRarity.Common:
                if (ui.rarityCardC) ui.rarityCardC.SetActive(true);
                if (ui.typeCardC)   ui.typeCardC.SetActive(true);
                break;
            case EquipmentRarity.Rare:
                if (ui.rarityCardR) ui.rarityCardR.SetActive(true);
                if (ui.typeCardR)   ui.typeCardR.SetActive(true);
                break;
            case EquipmentRarity.Legendary:
                if (ui.rarityCardL) ui.rarityCardL.SetActive(true);
                if (ui.typeCardL)   ui.typeCardL.SetActive(true);
                break;
            case EquipmentRarity.Exotic:
                if (ui.rarityCardE) ui.rarityCardE.SetActive(true);
                if (ui.typeCardE)   ui.typeCardE.SetActive(true);
                break;
        }
    }
}

#if UNITY_EDITOR
// ============================================================================
//                              CUSTOM INSPECTOR
// ============================================================================
// NOTE: This editor keeps your database tooling and adds collapsible per-style UI
[UnityEditor.CustomEditor(typeof(EquipmentManager))]
public class EquipmentManagerEditor : UnityEditor.Editor
{
    // --- inspector-only filter state for database ---
    private static int _inspectorStyleIndex = 0;   // 0 = All, then EquipmentStyle order
    private static string _inspectorSearch = "";
    private static List<string> _styleOptions;

    // Serialized fields
    SerializedProperty equipmentDatabase;

    SerializedProperty itemsScrollViewContent;
    SerializedProperty filterDropdown, searchInputField, itemsScrollRect, equipPanelCloseButton, raceDropdown;

    SerializedProperty slideAnimationDuration, slideStartOffset, positionOffset, slideCurve;

    SerializedProperty styleUI; // the per-style list

    void OnEnable()
    {
        // Database
        equipmentDatabase = serializedObject.FindProperty(nameof(EquipmentManager.equipmentDatabase));

        // Shared UI
        itemsScrollViewContent = serializedObject.FindProperty(nameof(EquipmentManager.itemsScrollViewContent));
        filterDropdown        = serializedObject.FindProperty(nameof(EquipmentManager.filterDropdown));
        searchInputField      = serializedObject.FindProperty(nameof(EquipmentManager.searchInputField));
        itemsScrollRect       = serializedObject.FindProperty(nameof(EquipmentManager.itemsScrollRect));
        equipPanelCloseButton = serializedObject.FindProperty(nameof(EquipmentManager.equipPanelCloseButton));
        raceDropdown          = serializedObject.FindProperty(nameof(EquipmentManager.raceDropdown));

        // Animation settings
        slideAnimationDuration = serializedObject.FindProperty(nameof(EquipmentManager.slideAnimationDuration));
        slideStartOffset       = serializedObject.FindProperty(nameof(EquipmentManager.slideStartOffset));
        positionOffset         = serializedObject.FindProperty(nameof(EquipmentManager.positionOffset));
        slideCurve             = serializedObject.FindProperty(nameof(EquipmentManager.slideCurve));

        // Per-style list
        styleUI = serializedObject.FindProperty(nameof(EquipmentManager.styleUI));

        // Build inspector filter options
        _styleOptions = new List<string> { "All" };
        _styleOptions.AddRange(System.Enum.GetNames(typeof(EquipmentStyle)));
    }

    private static string GetFoldoutLabel(SerializedProperty element, int index)
    {
        string itemName      = element.FindPropertyRelative("itemName")?.stringValue ?? "";
        string inspectorName = element.FindPropertyRelative("inspectorName")?.stringValue ?? "";

        if (!string.IsNullOrWhiteSpace(itemName))      return itemName;
        if (!string.IsNullOrWhiteSpace(inspectorName)) return inspectorName;

        return $"Item {index + 1}";
    }

    public override void OnInspectorGUI()
    {
        if (_styleOptions == null || _styleOptions.Count == 0)
        {
            _styleOptions = new List<string> { "All" };
            _styleOptions.AddRange(System.Enum.GetNames(typeof(EquipmentStyle)));
        }

        serializedObject.Update();

        void PF(SerializedProperty p, string label)
        {
            if (p != null)
                EditorGUILayout.PropertyField(p, new GUIContent(label));
            else
                EditorGUILayout.HelpBox($"Missing SerializedProperty for \"{label}\". Check the field name.", MessageType.Warning);
        }

        // ===== Inspector-only filters (database) =====
        DrawInspectorFilters();

        // ===== Database list (filtered) =====
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Equipment Database (ALL available items)", EditorStyles.boldLabel);
        if (equipmentDatabase != null)
            DrawDatabaseListFiltered(equipmentDatabase);
        else
            EditorGUILayout.HelpBox("equipmentDatabase property not found.", MessageType.Warning);
        EditorGUILayout.Space(8);

        // ===== UI References (Shared) =====
        EditorGUILayout.LabelField("UI References (Shared)", EditorStyles.boldLabel);
        PF(itemsScrollViewContent, "ItemsScrollView Content");
        PF(filterDropdown,        "Category Filter Dropdown");
        PF(searchInputField,      "Search Input Field");
        PF(itemsScrollRect,       "Items Scroll Rect");
        PF(equipPanelCloseButton, "Equip Panel Close Button");
        PF(raceDropdown,          "Race Dropdown");

        EditorGUILayout.Space(6);

        // ===== UI References (Per Style) =====
        EditorGUILayout.LabelField("UI References (Per Style)", EditorStyles.boldLabel);
        if (styleUI != null)
        {
            for (int i = 0; i < styleUI.arraySize; i++)
            {
                var entry = styleUI.GetArrayElementAtIndex(i);
                var styleProp = entry.FindPropertyRelative("style");
                string title = styleProp != null ? styleProp.enumDisplayNames[styleProp.enumValueIndex] : $"Style {i}";
                entry.isExpanded = EditorGUILayout.Foldout(entry.isExpanded, title, true);

                if (entry.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(styleProp);

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Item Card Prefabs (per rarity)", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("itemCardPrefabC"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("itemCardPrefabR"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("itemCardPrefabL"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("itemCardPrefabE"));

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Per-Rarity Cards + Label Texts", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("rarityCardC"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("rarityCardCText"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("typeCardC"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("typeCardCText"));

                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("rarityCardR"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("rarityCardRText"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("typeCardR"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("typeCardRText"));

                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("rarityCardL"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("rarityCardLText"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("typeCardL"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("typeCardLText"));

                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("rarityCardE"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("rarityCardEText"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("typeCardE"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("typeCardEText"));

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Detail Panels", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("itemDetailPanelC"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("itemDetailPanelR"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("itemDetailPanelL"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("itemDetailPanelE"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("exoticDetailRainbowBackground"));

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Equip Buttons", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("equipButtonC"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("equipButtonR"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("equipButtonL"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("equipButtonE"));

                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space(6);
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove Style", GUILayout.Width(110)))
                {
                    styleUI.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(8);
            }

            if (GUILayout.Button("Add Style"))
            {
                styleUI.InsertArrayElementAtIndex(styleUI.arraySize);
            }
        }

        EditorGUILayout.Space(6);
        // Animation settings
        EditorGUILayout.LabelField("Detail Panel Animation Settings", EditorStyles.boldLabel);
        PF(slideAnimationDuration, "Slide Animation Duration");
        PF(slideStartOffset,       "Slide Start Offset");
        PF(positionOffset,         "Position Offset");
        PF(slideCurve,             "Slide Curve");

        serializedObject.ApplyModifiedProperties();
    }

    // -------- Inspector-only UI --------
    void DrawInspectorFilters()
    {
        EditorGUILayout.BeginVertical("HelpBox");
        EditorGUILayout.LabelField("Database Filters (Inspector-only)", EditorStyles.boldLabel);

        _inspectorStyleIndex = EditorGUILayout.Popup("Race / Style", _inspectorStyleIndex, _styleOptions.ToArray());
        _inspectorSearch = EditorGUILayout.TextField("Search Name", _inspectorSearch ?? "");

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Clear Filters", GUILayout.Width(110)))
        {
            _inspectorStyleIndex = 0;
            _inspectorSearch = "";
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

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
                return;
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

    bool PassesInspectorFilter(SerializedProperty element)
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

    void DrawEquipableItem(SerializedProperty el)
    {
        EditorGUILayout.PropertyField(el.FindPropertyRelative("itemName"));
        EditorGUILayout.PropertyField(
            el.FindPropertyRelative("inspectorName"),
            new GUIContent("Inspector Name (key)")
        );
        EditorGUILayout.PropertyField(el.FindPropertyRelative("itemDescription"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("category"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("rarity"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("style"));

        EditorGUILayout.Space(4);
        EditorGUILayout.PropertyField(el.FindPropertyRelative("itemIcon"));

        var category = (EquipmentCategory)el.FindPropertyRelative("category").enumValueIndex;
        if (category == EquipmentCategory.Weapon)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Weapon Stats", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(el.FindPropertyRelative("weaponSubtype"));

            var subtype = (WeaponSubtype)el.FindPropertyRelative("weaponSubtype").enumValueIndex;
            if (subtype == WeaponSubtype.Ranged)
            {
                EditorGUILayout.LabelField("Ranged", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_MBCDamage"),    new GUIContent("MBC Damage"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_MBCCooldown"),  new GUIContent("MBC Cooldown"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_CursorSprite"), new GUIContent("Cursor Sprite"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_XMove"),        new GUIContent("X Move"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_ZMove"),        new GUIContent("Z Move"));
            }
            else
            {
                EditorGUILayout.LabelField("Melee", EditorStyles.miniBoldLabel);
                var amountProp = el.FindPropertyRelative("melee_MBCAmount");
                EditorGUILayout.PropertyField(amountProp, new GUIContent("MBC Amount"));

                var dmgList = el.FindPropertyRelative("melee_MBCDamages");
                if (amountProp.intValue < 0) amountProp.intValue = 0;
                while (dmgList.arraySize < amountProp.intValue) dmgList.InsertArrayElementAtIndex(dmgList.arraySize);
                while (dmgList.arraySize > amountProp.intValue && dmgList.arraySize > 0) dmgList.DeleteArrayElementAtIndex(dmgList.arraySize - 1);

                for (int i = 0; i < dmgList.arraySize; i++)
                {
                    EditorGUILayout.PropertyField(
                        dmgList.GetArrayElementAtIndex(i),
                        new GUIContent($"MBC{i + 1} Damage"));
                }

                EditorGUILayout.PropertyField(el.FindPropertyRelative("melee_MBCComboCooldown"), new GUIContent("MBC Combo Cooldown"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("melee_XMove"), new GUIContent("X Move"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("melee_ZMove"), new GUIContent("Z Move"));
            }
        }

        EditorGUILayout.Space(4);
        var attachmentsProp = el.FindPropertyRelative("attachments");
        EditorGUILayout.PropertyField(attachmentsProp, new GUIContent("Attachments"), true);
    }
}
#endif