// Assets/Scripts/Player/PlayerStamina.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerStamina : MonoBehaviour
{
    [Header("Stamina Settings")]
    [Tooltip("Base stamina at 0 skill points.")]
    public float baseStamina = 50f;

    [Tooltip("How much stamina each skill point adds.")]
    public float staminaPerPoint = 7f; // 50 + (200 * 7) = 1450 ≈ 1500

    [Tooltip("How many skill points are currently invested into Stamina.")]
    public int staminaSkillPoints = 0;

    [Tooltip("Current maximum stamina after scaling.")]
    public float maxStamina = 50f;
    public float currentStamina;

    [Header("Stamina Costs")]
    public float dashStaminaCost = 26f;
    public float jumpStaminaCost = 26f; // For jumps after the first

    [Header("Stamina Regeneration (Percentage Ticks)")]
    public float staminaRegenDelay = 1f; // Delay before regen starts after last use
    public float ticksPerSecond = 3f;    // 3 ticks per second
    [Tooltip("Each tick restores 1/100th of max stamina.")]
    public float tickFraction = 0.01f;   // 1% of maxStamina per tick

    [Header("UI References")]
    public Image staminaBarFill;
    public TextMeshProUGUI staminaBarText;

    [Header("UI Animation")]
    public float barAnimationSpeed = 8f; // Lerp speed for the bar

    [Header("Portrait (UI)")]
    public PortraitDriver portrait;

    // Private variables
    private float lastStaminaUseTime;
    private bool isRegenerating = false;
    private float displayedStamina;      // What the bar currently shows
    private float targetStamina;         // What the bar should show

    // Discrete regen accumulator
    private float regenAccumulator = 0f;

    // Component references (optional)
    private PlayerDash playerDash;
    private PlayerJump playerJump;

    void Start()
    {
        RecalculateMaxStamina(); // apply scaling at start

        currentStamina = maxStamina;
        displayedStamina = maxStamina;
        targetStamina = maxStamina;
        lastStaminaUseTime = -staminaRegenDelay;

        playerDash = GetComponent<PlayerDash>();
        playerJump = GetComponent<PlayerJump>();

        UpdateStaminaBar();

        // UPDATED: drive portrait with absolute stamina points
        if (portrait != null)
            portrait.SetStaminaPoints(currentStamina); // start with full stamina (points)
    }

    void Update()
    {
        HandleRegenTicks();
        AnimateStaminaBar();

        // UPDATED: always push absolute stamina points to the portrait
        if (portrait != null)
        {
            portrait.SetStaminaPoints(currentStamina);
        }
    }

    private void HandleRegenTicks()
    {
        bool pastDelay = (Time.time - lastStaminaUseTime) >= staminaRegenDelay;

        if (pastDelay && currentStamina < maxStamina)
        {
            isRegenerating = true;

            float interval = (ticksPerSecond > 0f) ? (1f / ticksPerSecond) : 0.3333f;
            regenAccumulator += Time.deltaTime;

            int ticks = Mathf.FloorToInt(regenAccumulator / interval);
            if (ticks > 0)
            {
                float amountPerTick = maxStamina * tickFraction;
                float totalAmount = ticks * amountPerTick;

                currentStamina = Mathf.Min(maxStamina, currentStamina + totalAmount);
                targetStamina = currentStamina;

                regenAccumulator -= ticks * interval;
            }
        }
        else
        {
            isRegenerating = false;
        }
    }

    private void AnimateStaminaBar()
    {
        if (Mathf.Abs(displayedStamina - targetStamina) > 0.1f)
        {
            displayedStamina = Mathf.Lerp(displayedStamina, targetStamina, barAnimationSpeed * Time.deltaTime);
        }
        else
        {
            displayedStamina = targetStamina;
        }

        UpdateStaminaBarVisual();
    }

    public bool CanDash() => currentStamina >= dashStaminaCost;
    public bool CanJump() => currentStamina >= jumpStaminaCost;

    public bool TryUseDashStamina()
    {
        if (CanDash())
        {
            UseStamina(dashStaminaCost);
            return true;
        }
        return false;
    }

    public bool TryUseJumpStamina()
    {
        if (CanJump())
        {
            UseStamina(jumpStaminaCost);
            return true;
        }
        return false;
    }

    private void UseStamina(float amount)
    {
        currentStamina = Mathf.Clamp(currentStamina - amount, 0, maxStamina);
        targetStamina = currentStamina;
        lastStaminaUseTime = Time.time;
        regenAccumulator = 0f;
    }

    public void RestoreStamina(float amount)
    {
        currentStamina = Mathf.Clamp(currentStamina + amount, 0, maxStamina);
        targetStamina = currentStamina;
    }

    private void UpdateStaminaBar()
    {
        targetStamina = currentStamina;
        UpdateStaminaBarVisual();
    }

    private void UpdateStaminaBarVisual()
    {
        if (staminaBarFill != null)
            staminaBarFill.fillAmount = (maxStamina > 0f) ? displayedStamina / maxStamina : 0f;

        if (staminaBarText != null)
            staminaBarText.text = $"{Mathf.RoundToInt(displayedStamina)}/{Mathf.RoundToInt(maxStamina)}";
    }

    /// <summary>
    /// Recalculates max stamina based on invested skill points.
    /// </summary>
    public void RecalculateMaxStamina()
    {
        float staminaPercentage = (maxStamina > 0f) ? currentStamina / maxStamina : 1f;
        maxStamina = baseStamina + staminaPerPoint * staminaSkillPoints;
        currentStamina = Mathf.Clamp(maxStamina * staminaPercentage, 0f, maxStamina);
        targetStamina = currentStamina;
        UpdateStaminaBar();
    }

    // Back-compat for older code paths (e.g., StatsManager)
    [System.Obsolete("Prefer SetStaminaSkillPoints() or RecalculateMaxStamina().")]
    public void SetMaxStamina(float newMaxStamina)
    {
        float pct = (maxStamina > 0f) ? currentStamina / maxStamina : 1f;
        maxStamina = Mathf.Max(1f, newMaxStamina);
        currentStamina = Mathf.Clamp(maxStamina * pct, 0f, maxStamina);
        targetStamina = currentStamina;
        UpdateStaminaBar();
    }

    // Used when player spends points into Stamina
    public void SetStaminaSkillPoints(int points)
    {
        staminaSkillPoints = Mathf.Max(0, points);
        RecalculateMaxStamina();
    }

    // Public getters
    public float GetCurrentStamina() => currentStamina;
    public float GetMaxStamina() => maxStamina;
    public float GetStaminaPercentage() => (maxStamina > 0f) ? currentStamina / maxStamina : 0f;
    public bool IsRegenerating() => isRegenerating;
}
