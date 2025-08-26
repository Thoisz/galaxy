using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class StatsManager : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI availablePointsText;
    public TextMeshProUGUI vitalityLevelText; // stamina points
    public TextMeshProUGUI healthLevelText;   // HP points
    public TextMeshProUGUI meleeLevelText;
    public TextMeshProUGUI rangedLevelText;
    public TextMeshProUGUI magicLevelText;

    [Header("Plus Buttons")]
    public Button vitalityPlusButton;
    public Button healthPlusButton;
    public Button meleePlusButton;
    public Button rangedPlusButton;
    public Button magicPlusButton;

    [Header("Menu Controls")]
    public Button menuCloseButton;

    [Header("Point Economy")]
    [Tooltip("How many points each stat can take at most (200 fits your design).")]
    public int maxPointsPerStat = 200;

    [Tooltip("Unspent points the player currently has.")]
    public int availablePoints = 0;

    [Header("Allocated Points (per stat)")]
    [Tooltip("Skill points invested into Stamina (Vitality).")]
    public int vitalityLevel = 0;

    [Tooltip("Skill points invested into Health.")]
    public int healthLevel = 0;

    [Tooltip("Skill points invested into Melee.")]
    public int meleeLevel = 0;

    [Tooltip("Skill points invested into Ranged.")]
    public int rangedLevel = 0;

    [Tooltip("Skill points invested into Magic.")]
    public int magicLevel = 0;

    [Header("Player References (drag your player components here)")]
    public PlayerExperience playerExperience;
    public PlayerHealth playerHealth;
    public PlayerStamina playerStamina;
    // public PlayerAttackStats playerAttackStats; // optional future hook

    // Keep XP event subscriptions even when the panel is closed
    private bool _subscribedToXP = false;

    // --- Hold-to-add support ---
    [Header("Hold-to-Add Tuning")]
    [Tooltip("Delay before auto-repeat starts when holding a + button.")]
    public float holdInitialDelay = 0.35f;
    [Tooltip("Starting interval between repeats, after the initial delay.")]
    public float holdStartInterval = 0.12f;
    [Tooltip("Minimum interval reached as it speeds up.")]
    public float holdMinInterval = 0.03f;
    [Tooltip("Per-step multiplier to speed up (lower = faster acceleration).")]
    public float holdAccelMultiplier = 0.88f;

    // one coroutine per button
    private readonly Dictionary<Button, Coroutine> _holdRoutines = new Dictionary<Button, Coroutine>(5);

    private void OnEnable()
    {
        // Subscribe to XP system events (kept even when this panel is closed)
        TrySubscribeToXPEvents();

        // Wire buttons (click + press-and-hold)
        WirePlusButton(vitalityPlusButton, "Vitality");
        WirePlusButton(healthPlusButton,   "Health");
        WirePlusButton(meleePlusButton,    "Melee");
        WirePlusButton(rangedPlusButton,   "Ranged");
        WirePlusButton(magicPlusButton,    "Magic");

        if (menuCloseButton) menuCloseButton.onClick.AddListener(CloseAllMenus);

        // Push current allocations into components at start
        ApplyAllAllocations();
        UpdateAllDisplays();
    }

    // NOTE: Do NOT unsubscribe from PlayerExperience events here.
    // Keep subscriptions alive so points are awarded even when the panel is closed.
    private void OnDisable()
    {
        // Stop any active hold coroutines
        StopAllHolds();

        // It's fine to clear UI button listeners when the panel is disabled.
        UnwirePlusButton(vitalityPlusButton);
        UnwirePlusButton(healthPlusButton);
        UnwirePlusButton(meleePlusButton);
        UnwirePlusButton(rangedPlusButton);
        UnwirePlusButton(magicPlusButton);

        if (menuCloseButton) menuCloseButton.onClick.RemoveAllListeners();
    }

    // Clean up XP event subscriptions only when this component is destroyed.
    private void OnDestroy()
    {
        if (playerExperience != null && _subscribedToXP)
        {
            playerExperience.OnSkillPointsEarned.RemoveListener(OnPointsEarnedFromLevelUp);
            playerExperience.OnLevelUp.RemoveListener(OnLevelUp);
            _subscribedToXP = false;
        }
    }

    private void TrySubscribeToXPEvents()
    {
        if (playerExperience != null && !_subscribedToXP)
        {
            playerExperience.OnSkillPointsEarned.AddListener(OnPointsEarnedFromLevelUp);
            playerExperience.OnLevelUp.AddListener(OnLevelUp);
            _subscribedToXP = true;
        }
    }

    // -------- Points flow from PlayerExperience --------
    private void OnPointsEarnedFromLevelUp(int points)
    {
        AddAvailablePoints(points);
    }

    private void OnLevelUp(int newLevel)
    {
        // Optional: play SFX/VFX, show toast, etc.
        // Debug.Log($"Level up! New level: {newLevel}");
    }

    // -------- Spend points --------
    public void AddStatPoint(string statName)
    {
        if (availablePoints <= 0) return;

        switch (statName)
        {
            case "Vitality":
                if (vitalityLevel >= maxPointsPerStat) return;
                vitalityLevel++;
                availablePoints--;
                ApplyVitality(); // stamina
                break;

            case "Health":
                if (healthLevel >= maxPointsPerStat) return;
                healthLevel++;
                availablePoints--;
                ApplyHealth();   // HP
                break;

            case "Melee":
                if (meleeLevel >= maxPointsPerStat) return;
                meleeLevel++;
                availablePoints--;
                ApplyMelee();
                break;

            case "Ranged":
                if (rangedLevel >= maxPointsPerStat) return;
                rangedLevel++;
                availablePoints--;
                ApplyRanged();
                break;

            case "Magic":
                if (magicLevel >= maxPointsPerStat) return;
                magicLevel++;
                availablePoints--;
                ApplyMagic();
                break;

            default:
                return;
        }

        UpdateAllDisplays();
    }

    // -------- Apply allocations to components --------
    private void ApplyVitality()
    {
        if (playerStamina != null)
        {
            playerStamina.SetStaminaSkillPoints(vitalityLevel);
            // Costs & regen already scale off max in PlayerStamina
        }
    }

    private void ApplyHealth()
    {
        if (playerHealth != null)
        {
            playerHealth.SetHealthSkillPoints(healthLevel);
            // Regen rate auto-updates in PlayerHealth
        }
    }

    private void ApplyMelee()
    {
        // TODO: hook to your damage system
        // if (playerAttackStats) playerAttackStats.SetMeleePoints(meleeLevel);
    }

    private void ApplyRanged()
    {
        // TODO: hook to your damage system
        // if (playerAttackStats) playerAttackStats.SetRangedPoints(rangedLevel);
    }

    private void ApplyMagic()
    {
        // TODO: hook to your damage system
        // if (playerAttackStats) playerAttackStats.SetMagicPoints(magicLevel);
    }

    private void ApplyAllAllocations()
    {
        ApplyVitality();
        ApplyHealth();
        ApplyMelee();
        ApplyRanged();
        ApplyMagic();
    }

    // -------- Add/Grant points (from events, quests, etc.) --------
    public void AddAvailablePoints(int points)
    {
        availablePoints = Mathf.Max(0, availablePoints + points);
        UpdateAllDisplays();
    }

    // -------- UI refresh --------
    private void UpdateAllDisplays()
    {
        if (availablePointsText) availablePointsText.text = availablePoints.ToString();

        if (vitalityLevelText) vitalityLevelText.text = $"Lv.{vitalityLevel}";
        if (healthLevelText)   healthLevelText.text   = $"Lv.{healthLevel}";
        if (meleeLevelText)    meleeLevelText.text    = $"Lv.{meleeLevel}";
        if (rangedLevelText)   rangedLevelText.text   = $"Lv.{rangedLevel}";
        if (magicLevelText)    magicLevelText.text    = $"Lv.{magicLevel}";

        bool canSpend = availablePoints > 0;
        if (vitalityPlusButton) vitalityPlusButton.interactable = canSpend && vitalityLevel < maxPointsPerStat;
        if (healthPlusButton)   healthPlusButton.interactable   = canSpend && healthLevel   < maxPointsPerStat;
        if (meleePlusButton)    meleePlusButton.interactable    = canSpend && meleeLevel    < maxPointsPerStat;
        if (rangedPlusButton)   rangedPlusButton.interactable   = canSpend && rangedLevel   < maxPointsPerStat;
        if (magicPlusButton)    magicPlusButton.interactable    = canSpend && magicLevel    < maxPointsPerStat;
    }

    private void CloseAllMenus()
    {
        MenuManager menuManager = FindObjectOfType<MenuManager>();
        if (menuManager != null)
        {
            menuManager.CloseMenu();
        }
        else
        {
            Debug.LogWarning("MenuManager not found! Cannot close menus.");
        }
    }

    // ===============================
    // Hold-to-Add implementation
    // ===============================
    private void WirePlusButton(Button btn, string statName)
    {
        if (!btn) return;

        // Keep single-click behavior
        btn.onClick.AddListener(() => AddStatPoint(statName));

        // Ensure EventTrigger exists
        var trigger = btn.GetComponent<EventTrigger>();
        if (!trigger) trigger = btn.gameObject.AddComponent<EventTrigger>();

        // PointerDown -> start hold
        var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        down.callback.AddListener(_ => StartHold(btn, statName));
        trigger.triggers.Add(down);

        // PointerUp -> stop hold
        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener(_ => StopHold(btn));
        trigger.triggers.Add(up);

        // PointerExit -> also stop (dragging off the button)
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => StopHold(btn));
        trigger.triggers.Add(exit);

        // Cancel (touch cancel, etc.)
        var cancel = new EventTrigger.Entry { eventID = EventTriggerType.Cancel };
        cancel.callback.AddListener(_ => StopHold(btn));
        trigger.triggers.Add(cancel);
    }

    private void UnwirePlusButton(Button btn)
    {
        if (!btn) return;

        btn.onClick.RemoveAllListeners();

        var trigger = btn.GetComponent<EventTrigger>();
        if (trigger) trigger.triggers.Clear();

        StopHold(btn);
    }

    private void StartHold(Button btn, string statName)
    {
        if (!btn) return;
        StopHold(btn); // ensure one routine per button

        var co = StartCoroutine(HoldRoutine(btn, statName));
        _holdRoutines[btn] = co;
    }

    private void StopHold(Button btn)
    {
        if (!btn) return;
        if (_holdRoutines.TryGetValue(btn, out var co) && co != null)
        {
            StopCoroutine(co);
        }
        _holdRoutines.Remove(btn);
    }

    private void StopAllHolds()
    {
        foreach (var kvp in _holdRoutines)
        {
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        }
        _holdRoutines.Clear();
    }

    private IEnumerator HoldRoutine(Button btn, string statName)
    {
        // Wait for initial delay (so a short click doesn't trigger auto-repeat)
        float t = 0f;
        while (t < holdInitialDelay)
        {
            // Abort if button becomes non-interactable (out of points/capped) or panel gets disabled
            if (!btn || !btn.interactable) yield break;
            if (availablePoints <= 0) yield break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // Start repeating
        float interval = holdStartInterval;
        while (true)
        {
            if (!btn || !btn.interactable) yield break;
            if (availablePoints <= 0) yield break;

            // Attempt to add a point
            AddStatPoint(statName);

            // Accelerate repeat rate
            interval = Mathf.Max(holdMinInterval, interval * holdAccelMultiplier);

            // Wait for next tick
            float w = 0f;
            while (w < interval)
            {
                if (!btn || !btn.interactable) yield break;
                if (availablePoints <= 0) yield break;
                w += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}