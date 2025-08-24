using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    [Header("Health Settings")]
    public float maxHealth = 100f;
    public float currentHealth;
    
    [Header("Health Regeneration")]
    public float healthRegenTime = 10f; // Time in seconds to fully regenerate from 0 to max
    public float healthRegenDelay = 3f; // Delay before regen starts after taking damage
    public bool canRegenerateHealth = true; // Toggle for health regen
    
    [Header("UI References")]
    public Image healthBarFill;
    public TMPro.TextMeshProUGUI healthBarText;
    
    [Header("UI Animation")]
    public float barAnimationSpeed = 6f; // How fast the bar animates
    
    [Header("Damage Effects")]
    public Color damageFlashColor = Color.red;
    public float damageFlashDuration = 0.2f;

    [Header("Portrait (UI)")]
    public PortraitDriver portrait;

    // Private variables
    private float lastDamageTime;
    private bool isRegenerating = false;
    private float displayedHealth; // What the bar currently shows
    private float targetHealth; // What the bar should show
    private float actualRegenRate; // Calculated regen rate based on max health
    private bool isDead = false;
    
    // Flash effect variables
    private Image healthBarImage;
    private Color originalHealthBarColor;
    private bool isFlashing = false;
    private float flashTimer = 0f;
    
    void Start()
    {
        currentHealth = maxHealth;
        displayedHealth = maxHealth;
        targetHealth = maxHealth;
        lastDamageTime = -healthRegenDelay; // Allow immediate regen at start
        
        // Calculate the actual regen rate based on max health
        actualRegenRate = maxHealth / healthRegenTime;
        
        // Store original health bar color for flash effect
        if (healthBarFill != null)
        {
            healthBarImage = healthBarFill;
            originalHealthBarColor = healthBarFill.color;
        }
        
        UpdateHealthBar();
    }
    
    void Update()
    {
        // Recalculate regen rate if max health changes
        actualRegenRate = maxHealth / healthRegenTime;
        
        // Handle health regeneration
        if (canRegenerateHealth && !isDead && Time.time - lastDamageTime >= healthRegenDelay)
        {
            if (currentHealth < maxHealth)
            {
                isRegenerating = true;
                RegenerateHealth();
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
        
        // Handle damage flash effect
        if (isFlashing)
        {
            UpdateDamageFlash();
        }
        
        // Smoothly animate the bar towards the target
        AnimateHealthBar();
    }
    
    private void RegenerateHealth()
    {
        currentHealth += actualRegenRate * Time.deltaTime;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        targetHealth = currentHealth;
    }
    
    private void AnimateHealthBar()
    {
        // Smoothly move displayed health towards target
        if (Mathf.Abs(displayedHealth - targetHealth) > 0.1f)
        {
            displayedHealth = Mathf.Lerp(displayedHealth, targetHealth, barAnimationSpeed * Time.deltaTime);
            UpdateHealthBarVisual();
        }
        else
        {
            // Snap to target when very close to avoid infinite lerping
            displayedHealth = targetHealth;
            UpdateHealthBarVisual();
        }
    }
    
    public void TakeDamage(float damage)
    {
        if (isDead) return;
        
        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        targetHealth = currentHealth;
        lastDamageTime = Time.time;
        
        // Trigger damage flash effect
        TriggerDamageFlash();

        if (portrait != null && damage > 0f)
    {
        portrait.PlayHurt();   // <-- this is the entire call from PlayerHealth
    }

        
        // Check for death
        if (currentHealth <= 0)
        {
            Die();
        }
        
        Debug.Log($"Player took {damage} damage. Health: {currentHealth}/{maxHealth}");
    }

    public void TakeTickDamage(float damage)
    {
        // Re-use existing logic
        TakeDamage(damage);

        // Portrait only reacts to tick damage
        if (portrait != null && damage > 0f)
        portrait.PlayHurt();
    }

    
    public void Heal(float healAmount)
    {
        if (isDead) return;
        
        currentHealth += healAmount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        targetHealth = currentHealth;
        
        Debug.Log($"Player healed for {healAmount}. Health: {currentHealth}/{maxHealth}");
    }
    
    private void TriggerDamageFlash()
    {
        if (healthBarImage != null)
        {
            isFlashing = true;
            flashTimer = 0f;
        }
    }
    
    private void UpdateDamageFlash()
    {
        flashTimer += Time.deltaTime;
        
        if (flashTimer >= damageFlashDuration)
        {
            // End flash
            isFlashing = false;
            healthBarImage.color = originalHealthBarColor;
        }
        else
        {
            // Flash between original and damage color
            float flashProgress = flashTimer / damageFlashDuration;
            Color currentColor = Color.Lerp(damageFlashColor, originalHealthBarColor, flashProgress);
            healthBarImage.color = currentColor;
        }
    }
    
    private void Die()
    {
        isDead = true;
        isRegenerating = false;
        
        Debug.Log("Player died!");
        
        // You can add death effects here later
        // For now, let's respawn after a delay
        Invoke("Respawn", 2f);
    }
    
    private void Respawn()
    {
        isDead = false;
        currentHealth = maxHealth;
        targetHealth = maxHealth;
        displayedHealth = maxHealth;
        lastDamageTime = Time.time;
        
        Debug.Log("Player respawned!");
    }
    
    private void UpdateHealthBar()
    {
        targetHealth = currentHealth;
    }
    
    private void UpdateHealthBarVisual()
    {
        if (healthBarFill != null)
        {
            healthBarFill.fillAmount = displayedHealth / maxHealth;
        }
        
        // Update health text
        if (healthBarText != null)
        {
            healthBarText.text = $"{Mathf.RoundToInt(displayedHealth)}/{Mathf.RoundToInt(maxHealth)}";
        }
    }
    
    // Public method to update max health (for leveling up)
    public void SetMaxHealth(float newMaxHealth)
    {
        float healthPercentage = currentHealth / maxHealth; // Save current percentage
        maxHealth = newMaxHealth;
        currentHealth = maxHealth * healthPercentage; // Maintain same percentage
        actualRegenRate = maxHealth / healthRegenTime; // Recalculate regen rate
        UpdateHealthBar();
    }
    
    // Public getters
    public float GetCurrentHealth() { return currentHealth; }
    public float GetMaxHealth() { return maxHealth; }
    public float GetHealthPercentage() { return currentHealth / maxHealth; }
    public bool IsRegenerating() { return isRegenerating; }
    public bool IsDead() { return isDead; }
}