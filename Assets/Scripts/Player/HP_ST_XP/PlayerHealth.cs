// Assets/Scripts/Player/HP_ST/PlayerHealth.cs
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    [Header("Health Settings")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Health Regeneration")]
    public float healthRegenTime = 10f;
    public float healthRegenDelay = 3f;
    public bool canRegenerateHealth = true;

    [Header("UI References")]
    public Image healthBarFill;
    public TMPro.TextMeshProUGUI healthBarText;

    [Header("UI Animation")]
    public float barAnimationSpeed = 6f;

    [Header("Damage Effects")]
    public Color damageFlashColor = Color.red;
    public float damageFlashDuration = 0.2f;

    [Header("Portrait (UI)")]
    public PortraitDriver portrait;

    // Private
    private float lastDamageTime;
    private bool isRegenerating = false;
    private float displayedHealth;
    private float targetHealth;
    private float actualRegenRate;
    private bool isDead = false;

    // Flash effect
    private Image healthBarImage;
    private Color originalHealthBarColor;
    private bool isFlashing = false;
    private float flashTimer = 0f;

    void Start()
    {
        currentHealth = maxHealth;
        displayedHealth = maxHealth;
        targetHealth = maxHealth;
        lastDamageTime = -healthRegenDelay;

        actualRegenRate = maxHealth / healthRegenTime;

        if (healthBarFill != null)
        {
            healthBarImage = healthBarFill;
            originalHealthBarColor = healthBarFill.color;
        }

        UpdateHealthBar();

        // report to portrait once at start
        if (portrait != null)
            portrait.SetHealthPercent(currentHealth / Mathf.Max(1f, maxHealth));
    }

    void Update()
    {
        // Keep regen rate in sync with maxHealth
        actualRegenRate = maxHealth / healthRegenTime;

        // Regen
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

        // Damage flash
        if (isFlashing) UpdateDamageFlash();

        // Health bar animation
        AnimateHealthBar();

        // continuously report health % to portrait
        if (portrait != null)
            portrait.SetHealthPercent(currentHealth / Mathf.Max(1f, maxHealth));
    }

    private void RegenerateHealth()
    {
        currentHealth += actualRegenRate * Time.deltaTime;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        targetHealth = currentHealth;
    }

    private void AnimateHealthBar()
    {
        if (Mathf.Abs(displayedHealth - targetHealth) > 0.1f)
            displayedHealth = Mathf.Lerp(displayedHealth, targetHealth, barAnimationSpeed * Time.deltaTime);
        else
            displayedHealth = targetHealth;

        UpdateHealthBarVisual();
    }

    public void TakeDamage(float damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        targetHealth = currentHealth;
        lastDamageTime = Time.time;

        TriggerDamageFlash();

        // (Hurt plays only for tick/lava via TakeTickDamage)
        if (currentHealth <= 0) Die();

        Debug.Log($"Player took {damage} damage. Health: {currentHealth}/{maxHealth}");
    }

    public void TakeTickDamage(float damage)
    {
        // Apply normal damage logic
        TakeDamage(damage);

        // Portrait reacts only to tick/lava damage
        if (portrait != null && damage > 0f)
            portrait.PlayHurt();
    }

    public void Heal(float healAmount)
    {
        if (isDead) return;

        currentHealth += healAmount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        targetHealth = currentHealth;
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
            isFlashing = false;
            healthBarImage.color = originalHealthBarColor;
        }
        else
        {
            float t = flashTimer / damageFlashDuration;
            healthBarImage.color = Color.Lerp(damageFlashColor, originalHealthBarColor, t);
        }
    }

    private void Die()
    {
        isDead = true;
        isRegenerating = false;
        Debug.Log("Player died!");
        Invoke(nameof(Respawn), 2f);
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

    private void UpdateHealthBar() => targetHealth = currentHealth;

    private void UpdateHealthBarVisual()
    {
        if (healthBarFill != null)
            healthBarFill.fillAmount = displayedHealth / maxHealth;

        if (healthBarText != null)
            healthBarText.text = $"{Mathf.RoundToInt(displayedHealth)}/{Mathf.RoundToInt(maxHealth)}";
    }

    public void SetMaxHealth(float newMaxHealth)
    {
        float healthPercentage = currentHealth / maxHealth;
        maxHealth = newMaxHealth;
        currentHealth = maxHealth * healthPercentage;
        actualRegenRate = maxHealth / healthRegenTime;
        UpdateHealthBar();
    }

    // getters
    public float GetCurrentHealth() => currentHealth;
    public float GetMaxHealth() => maxHealth;
    public float GetHealthPercentage() => currentHealth / maxHealth;
    public bool IsRegenerating() => isRegenerating;
    public bool IsDead() => isDead;
}
