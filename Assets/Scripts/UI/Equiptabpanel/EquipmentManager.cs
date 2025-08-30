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
public class EquipableItem
{
    [Header("Basic Info")]
    public string itemName;
    [TextArea(2, 6)] public string itemDescription;
    public EquipmentCategory category;       // Weapon | Accessory
    public EquipmentRarity rarity;
    public EquipmentStyle style;

    [Header("UI Display")]
    public Sprite itemIcon;

    [Header("Item Prefab")]
    public GameObject itemPrefab;

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

    // ---- Legacy compatibility (so old scripts still compile) ----
    [Header("Legacy Tweaks (compat)")]
    public Vector3 appliedScaleTweak = Vector3.one;
    public Vector3 appliedPositionTweak = Vector3.zero;
    public Vector3 appliedRotationTweak = Vector3.zero;

    [Obsolete("Use itemPrefab instead")]
    public GameObject item3DModel { get => itemPrefab; set => itemPrefab = value; }

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

public class EquipmentManager : MonoBehaviour
{
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
        CacheOriginalDetailPositions();
        EnsureDetailPanelsUnderMainPanels();
        WireUI();
        RefreshDisplay();
        CloseAllDetailPanelsImmediate();
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
        // CLOSE THE WHOLE EQUIP TAB (like B key), not the detail panel.
        equipPanelCloseButton.onClick.AddListener(CloseWholeEquipTab);
    }

    // Equip button clicks just a placeholder for now
    if (equipButtonC) { equipButtonC.onClick.RemoveAllListeners(); equipButtonC.onClick.AddListener(() => Debug.Log("Equip (C)")); }
    if (equipButtonR) { equipButtonR.onClick.RemoveAllListeners(); equipButtonR.onClick.AddListener(() => Debug.Log("Equip (R)")); }
    if (equipButtonL) { equipButtonL.onClick.RemoveAllListeners(); equipButtonL.onClick.AddListener(() => Debug.Log("Equip (L)")); }
    if (equipButtonE) { equipButtonE.onClick.RemoveAllListeners(); equipButtonE.onClick.AddListener(() => Debug.Log("Equip (E)")); }
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

    void FilterDatabase()
    {
        string search = searchInputField != null ? searchInputField.text.Trim().ToLower() : string.Empty;
        int filter = filterDropdown != null ? filterDropdown.value : 0; // 0=All,1=Weapons,2=Accessories

        _filtered = equipmentDatabase
            .Where(it =>
            {
                if (it == null) return false;

                // Filter by category
                bool pass = filter switch
                {
                    1 => it.category == EquipmentCategory.Weapon,
                    2 => it.category == EquipmentCategory.Accessory,
                    _ => true
                };

                // Search by name
                if (pass && !string.IsNullOrEmpty(search))
                    pass = (it.itemName ?? "").ToLower().Contains(search);

                return pass;
            })
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
[CustomEditor(typeof(EquipmentManager))]
public class EquipmentManagerEditor : Editor
{
    SerializedProperty equipmentDatabase;

    // UI Refs
SerializedProperty itemsScrollViewContent;
SerializedProperty itemCardPrefabC, itemCardPrefabR, itemCardPrefabL, itemCardPrefabE;

SerializedProperty rarityCardC, typeCardC, rarityCardR, typeCardR, rarityCardL, typeCardL, rarityCardE, typeCardE;
SerializedProperty rarityCardCText, typeCardCText, rarityCardRText, typeCardRText, rarityCardLText, typeCardLText, rarityCardEText, typeCardEText;

SerializedProperty filterDropdown, searchInputField, itemsScrollRect, equipPanelCloseButton;

SerializedProperty itemDetailPanelC, itemDetailPanelR, itemDetailPanelL, itemDetailPanelE, exoticDetailRainbowBackground;

SerializedProperty equipButtonC, equipButtonR, equipButtonL, equipButtonE;

    // Anim
    SerializedProperty slideAnimationDuration, slideStartOffset, positionOffset, slideCurve;

    void OnEnable()
{
    equipmentDatabase = serializedObject.FindProperty("equipmentDatabase");

    itemsScrollViewContent = serializedObject.FindProperty("itemsScrollViewContent");
    itemCardPrefabC = serializedObject.FindProperty("itemCardPrefabC");
    itemCardPrefabR = serializedObject.FindProperty("itemCardPrefabR");
    itemCardPrefabL = serializedObject.FindProperty("itemCardPrefabL");
    itemCardPrefabE = serializedObject.FindProperty("itemCardPrefabE");

    rarityCardC = serializedObject.FindProperty("rarityCardC");
    typeCardC   = serializedObject.FindProperty("typeCardC");
    rarityCardR = serializedObject.FindProperty("rarityCardR");
    typeCardR   = serializedObject.FindProperty("typeCardR");
    rarityCardL = serializedObject.FindProperty("rarityCardL");
    typeCardL   = serializedObject.FindProperty("typeCardL");
    rarityCardE = serializedObject.FindProperty("rarityCardE");
    typeCardE   = serializedObject.FindProperty("typeCardE");

    rarityCardCText = serializedObject.FindProperty("rarityCardCText");
    typeCardCText   = serializedObject.FindProperty("typeCardCText");
    rarityCardRText = serializedObject.FindProperty("rarityCardRText");
    typeCardRText   = serializedObject.FindProperty("typeCardRText");
    rarityCardLText = serializedObject.FindProperty("rarityCardLText");
    typeCardLText   = serializedObject.FindProperty("typeCardLText");
    rarityCardEText = serializedObject.FindProperty("rarityCardEText");
    typeCardEText   = serializedObject.FindProperty("typeCardEText");

    filterDropdown = serializedObject.FindProperty("filterDropdown");
    searchInputField = serializedObject.FindProperty("searchInputField");
    itemsScrollRect = serializedObject.FindProperty("itemsScrollRect");
    equipPanelCloseButton = serializedObject.FindProperty("equipPanelCloseButton");

    itemDetailPanelC = serializedObject.FindProperty("itemDetailPanelC");
    itemDetailPanelR = serializedObject.FindProperty("itemDetailPanelR");
    itemDetailPanelL = serializedObject.FindProperty("itemDetailPanelL");
    itemDetailPanelE = serializedObject.FindProperty("itemDetailPanelE");
    exoticDetailRainbowBackground = serializedObject.FindProperty("exoticDetailRainbowBackground");

    equipButtonC = serializedObject.FindProperty("equipButtonC");
    equipButtonR = serializedObject.FindProperty("equipButtonR");
    equipButtonL = serializedObject.FindProperty("equipButtonL");
    equipButtonE = serializedObject.FindProperty("equipButtonE");

    slideAnimationDuration = serializedObject.FindProperty("slideAnimationDuration");
    slideStartOffset = serializedObject.FindProperty("slideStartOffset");
    positionOffset = serializedObject.FindProperty("positionOffset");
    slideCurve = serializedObject.FindProperty("slideCurve");
}

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ================== DATABASE LIST ==================
        EditorGUILayout.LabelField("Equipment Database (ALL available items)", EditorStyles.boldLabel);
        DrawDatabaseList(equipmentDatabase);
        EditorGUILayout.Space(8);

        // ================== UI References ==================
        EditorGUILayout.LabelField("UI References", EditorStyles.boldLabel);

// Prefabs
EditorGUILayout.PropertyField(itemsScrollViewContent, new GUIContent("ItemsScrollView Content"));
EditorGUILayout.PropertyField(itemCardPrefabC, new GUIContent("Item Card Prefab C"));
EditorGUILayout.PropertyField(itemCardPrefabR, new GUIContent("Item Card Prefab R"));
EditorGUILayout.PropertyField(itemCardPrefabL, new GUIContent("Item Card Prefab L"));
EditorGUILayout.PropertyField(itemCardPrefabE, new GUIContent("Item Card Prefab E"));

// Cards + texts
EditorGUILayout.Space(4);
EditorGUILayout.LabelField("Per-Rarity Cards", EditorStyles.boldLabel);

EditorGUILayout.PropertyField(rarityCardC);
EditorGUILayout.PropertyField(rarityCardCText, new GUIContent("Rarity Card C Text"));
EditorGUILayout.PropertyField(typeCardC);
EditorGUILayout.PropertyField(typeCardCText,   new GUIContent("Type Card C Text"));

EditorGUILayout.PropertyField(rarityCardR);
EditorGUILayout.PropertyField(rarityCardRText, new GUIContent("Rarity Card R Text"));
EditorGUILayout.PropertyField(typeCardR);
EditorGUILayout.PropertyField(typeCardRText,   new GUIContent("Type Card R Text"));

EditorGUILayout.PropertyField(rarityCardL);
EditorGUILayout.PropertyField(rarityCardLText, new GUIContent("Rarity Card L Text"));
EditorGUILayout.PropertyField(typeCardL);
EditorGUILayout.PropertyField(typeCardLText,   new GUIContent("Type Card L Text"));

EditorGUILayout.PropertyField(rarityCardE);
EditorGUILayout.PropertyField(rarityCardEText, new GUIContent("Rarity Card E Text"));
EditorGUILayout.PropertyField(typeCardE);
EditorGUILayout.PropertyField(typeCardEText,   new GUIContent("Type Card E Text"));

// List/search
EditorGUILayout.Space(4);
EditorGUILayout.PropertyField(filterDropdown, new GUIContent("Filter Dropdown"));
EditorGUILayout.PropertyField(searchInputField, new GUIContent("Search Input Field"));
EditorGUILayout.PropertyField(itemsScrollRect, new GUIContent("Items Scroll Rect"));
EditorGUILayout.PropertyField(equipPanelCloseButton, new GUIContent("Equip Panel Close Button"));

// Detail panels
EditorGUILayout.Space(4);
EditorGUILayout.PropertyField(itemDetailPanelC);
EditorGUILayout.PropertyField(itemDetailPanelR);
EditorGUILayout.PropertyField(itemDetailPanelL);
EditorGUILayout.PropertyField(itemDetailPanelE);
EditorGUILayout.PropertyField(exoticDetailRainbowBackground);

// Equip buttons
EditorGUILayout.Space(4);
EditorGUILayout.PropertyField(equipButtonC);
EditorGUILayout.PropertyField(equipButtonR);
EditorGUILayout.PropertyField(equipButtonL);
EditorGUILayout.PropertyField(equipButtonE);

// Moves/description
EditorGUILayout.Space(4);

        // Animation (header comes from [Header] attribute)
EditorGUILayout.Space(6);
EditorGUILayout.PropertyField(slideAnimationDuration, new GUIContent("Slide Animation Duration"));
EditorGUILayout.PropertyField(slideStartOffset, new GUIContent("Slide Start Offset"));
EditorGUILayout.PropertyField(positionOffset, new GUIContent("Position Offset"));
EditorGUILayout.PropertyField(slideCurve, new GUIContent("Slide Curve"));

        serializedObject.ApplyModifiedProperties();
    }

    void DrawDatabaseList(SerializedProperty listProp)
    {
        for (int i = 0; i < listProp.arraySize; i++)
        {
            var element = listProp.GetArrayElementAtIndex(i);
            var box = new GUIStyle("HelpBox");
            EditorGUILayout.BeginVertical(box);

            EditorGUILayout.BeginHorizontal();
            element.isExpanded = EditorGUILayout.Foldout(element.isExpanded, $"Item {i + 1}", true);
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
            // init melee list with 1 value
            el.FindPropertyRelative("melee_MBCAmount").intValue = 1;
            var dmgList = el.FindPropertyRelative("melee_MBCDamages");
            dmgList.arraySize = 1;
            dmgList.GetArrayElementAtIndex(0).intValue = 0;
        }
    }

    void DrawEquipableItem(SerializedProperty el)
    {
        // BASIC INFO
        EditorGUILayout.LabelField("Basic Info", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(el.FindPropertyRelative("itemName"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("itemDescription"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("category"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("rarity"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("style"));

        // UI DISPLAY
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("UI Display", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(el.FindPropertyRelative("itemIcon"));

        // ITEM PREFAB
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Item Prefab", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(el.FindPropertyRelative("itemPrefab"));

        // WEAPON STATS (conditional)
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
                EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_MBCDamage"), new GUIContent("MBC Damage"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_MBCCooldown"), new GUIContent("MBC Cooldown"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_CursorSprite"), new GUIContent("Cursor Sprite"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_XMove"), new GUIContent("X Move"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("ranged_ZMove"), new GUIContent("Z Move"));
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
                    EditorGUILayout.PropertyField(dmgList.GetArrayElementAtIndex(i), new GUIContent($"MBC{i + 1} Damage"));
                }

                EditorGUILayout.PropertyField(el.FindPropertyRelative("melee_MBCComboCooldown"), new GUIContent("MBC Combo Cooldown"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("melee_XMove"), new GUIContent("X Move"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("melee_ZMove"), new GUIContent("Z Move"));
            }
        }

        // EQUIPMENT BONUSES
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Equipment Bonuses", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(el.FindPropertyRelative("healthBonus"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("staminaBonus"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("speedBonus"));
    }
}
#endif