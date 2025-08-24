using UnityEngine;
using UnityEngine.UI;

public class PlayerStamina : MonoBehaviour
{
    [Header("Stamina Settings")]
    public float maxStamina = 100f;
    public float currentStamina;
    
    [Header("Stamina Costs")]
    public float dashStaminaCost = 26f;
    public float jumpStaminaCost = 26f; // For jumps after the first
    
    [Header("Stamina Regeneration")]
    public float staminaRegenTime = 5f; // Time in seconds to fully regenerate from 0 to max
    public float staminaRegenDelay = 1f; // Delay before regen starts
    
    [Header("UI References")]
    public Image staminaBarFill;
    public TMPro.TextMeshProUGUI staminaBarText;
    
    [Header("UI Animation")]
    public float barAnimationSpeed = 8f; // How fast the bar animates (higher = faster)
    
    // Private variables
    private float lastStaminaUseTime;
    private bool isRegenerating = false;
    private float displayedStamina; // What the bar currently shows
    private float targetStamina; // What the bar should show
    private float actualRegenRate; // Calculated regen rate based on max stamina
    
    // Component references
    private PlayerDash playerDash;
    private PlayerJump playerJump;
    
    void Start()
    {
        currentStamina = maxStamina;
        displayedStamina = maxStamina;
        targetStamina = maxStamina;
        lastStaminaUseTime = -staminaRegenDelay; // Allow immediate regen at start
        
        // Calculate the actual regen rate based on max stamina
        // This ensures the bar fills in the same time regardless of max stamina
        actualRegenRate = maxStamina / staminaRegenTime;
        
        // Get component references
        playerDash = GetComponent<PlayerDash>();
        playerJump = GetComponent<PlayerJump>();
        
        UpdateStaminaBar();
    }
    
    void Update()
    {
        // Recalculate regen rate if max stamina changes (for leveling up)
        actualRegenRate = maxStamina / staminaRegenTime;
        
        // Handle stamina regeneration
        if (Time.time - lastStaminaUseTime >= staminaRegenDelay)
        {
            if (currentStamina < maxStamina)
            {
                isRegenerating = true;
                RegenerateStamina();
            }
            else
            {
                isRegenerating = false;
            }
        }
        else
        {
            isRegenerating = false;
        }
        
        // Smoothly animate the bar towards the target
        AnimateStaminaBar();
    }
    
    private void RegenerateStamina()
    {
        currentStamina += actualRegenRate * Time.deltaTime;
        currentStamina = Mathf.Clamp(currentStamina, 0, maxStamina);
        targetStamina = currentStamina; // Update target for smooth animation
    }
    
    private void AnimateStaminaBar()
    {
        // Smoothly move displayed stamina towards target
        if (Mathf.Abs(displayedStamina - targetStamina) > 0.1f)
        {
            displayedStamina = Mathf.Lerp(displayedStamina, targetStamina, barAnimationSpeed * Time.deltaTime);
            UpdateStaminaBarVisual();
        }
        else
        {
            // Snap to target when very close to avoid infinite lerping
            displayedStamina = targetStamina;
            UpdateStaminaBarVisual();
        }
    }
    
    public bool CanDash()
    {
        return currentStamina >= dashStaminaCost;
    }
    
    public bool CanJump()
    {
        return currentStamina >= jumpStaminaCost;
    }
    
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
        currentStamina -= amount;
        currentStamina = Mathf.Clamp(currentStamina, 0, maxStamina);
        targetStamina = currentStamina; // Update target for smooth animation
        lastStaminaUseTime = Time.time;
    }
    
    public void RestoreStamina(float amount)
    {
        currentStamina += amount;
        currentStamina = Mathf.Clamp(currentStamina, 0, maxStamina);
        targetStamina = currentStamina; // Update target for smooth animation
    }
    
    private void UpdateStaminaBar()
    {
        targetStamina = currentStamina;
    }
    
    private void UpdateStaminaBarVisual()
    {
        if (staminaBarFill != null)
        {
            staminaBarFill.fillAmount = displayedStamina / maxStamina;
        }
        
        // Update stamina text
        if (staminaBarText != null)
        {
            staminaBarText.text = $"{Mathf.RoundToInt(displayedStamina)}/{Mathf.RoundToInt(maxStamina)}";
        }
    }
    
    // Public method to update max stamina (for leveling up)
    public void SetMaxStamina(float newMaxStamina)
    {
        float staminaPercentage = currentStamina / maxStamina; // Save current percentage
        maxStamina = newMaxStamina;
        currentStamina = maxStamina * staminaPercentage; // Maintain same percentage
        actualRegenRate = maxStamina / staminaRegenTime; // Recalculate regen rate
        UpdateStaminaBar();
    }
    
    // Public getters for UI or other systems
    public float GetCurrentStamina() { return currentStamina; }
    public float GetMaxStamina() { return maxStamina; }
    public float GetStaminaPercentage() { return currentStamina / maxStamina; }
    public bool IsRegenerating() { return isRegenerating; }
}