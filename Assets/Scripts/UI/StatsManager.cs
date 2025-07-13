using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StatsManager : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI availablePointsText;
    public TextMeshProUGUI vitalityLevelText;
    public TextMeshProUGUI healthLevelText;
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
    
    [Header("Player Stats")]
    public int availablePoints = 1;
    public int vitalityLevel = 1;
    public int healthLevel = 1;
    public int meleeLevel = 1;
    public int rangedLevel = 1;
    public int magicLevel = 1;
    
    [Header("Stat Bonuses")]
    public int staminaPerVitality = 5;
    public int healthPerHealthStat = 5;
    
    [Header("Player References (drag your player components here)")]
    public PlayerHealth playerHealth; // You'll need to create/reference this
    public PlayerStamina playerStamina; // You'll need to create/reference this
    
    void Start()
    {
        // Set up button listeners
        vitalityPlusButton.onClick.AddListener(() => AddStatPoint("Vitality"));
        healthPlusButton.onClick.AddListener(() => AddStatPoint("Health"));
        meleePlusButton.onClick.AddListener(() => AddStatPoint("Melee"));
        rangedPlusButton.onClick.AddListener(() => AddStatPoint("Ranged"));
        magicPlusButton.onClick.AddListener(() => AddStatPoint("Magic"));
        
        // Set up close button to close all menus
        if (menuCloseButton != null)
        {
            menuCloseButton.onClick.AddListener(CloseAllMenus);
        }
        
        // Initialize display
        UpdateAllDisplays();
    }
    
    public void AddStatPoint(string statName)
    {
        if (availablePoints <= 0) return;
        
        availablePoints--;
        
        switch (statName)
        {
            case "Vitality":
                vitalityLevel++;
                ApplyVitalityBonus();
                break;
            case "Health":
                healthLevel++;
                ApplyHealthBonus();
                break;
            case "Melee":
                meleeLevel++;
                // Add melee bonus later
                break;
            case "Ranged":
                rangedLevel++;
                // Add ranged bonus later
                break;
            case "Magic":
                magicLevel++;
                // Add magic bonus later
                break;
        }
        
        UpdateAllDisplays();
    }
    
    void ApplyVitalityBonus()
    {
        if (playerStamina != null)
        {
            float newMaxStamina = playerStamina.GetMaxStamina() + staminaPerVitality;
            playerStamina.SetMaxStamina(newMaxStamina);
            Debug.Log($"Vitality increased! Added {staminaPerVitality} stamina. New max: {newMaxStamina}");
        }
        else
        {
            Debug.Log($"Vitality increased! Would add {staminaPerVitality} stamina (PlayerStamina component not assigned)");
        }
    }
    
    void ApplyHealthBonus()
    {
        if (playerHealth != null)
        {
            float newMaxHealth = playerHealth.GetMaxHealth() + healthPerHealthStat;
            playerHealth.SetMaxHealth(newMaxHealth);
            Debug.Log($"Health increased! Added {healthPerHealthStat} health. New max: {newMaxHealth}");
        }
        else
        {
            Debug.Log($"Health increased! Would add {healthPerHealthStat} health (PlayerHealth component not assigned)");
        }
    }
    
    public void AddAvailablePoints(int points)
    {
        availablePoints += points;
        UpdateAllDisplays();
    }
    
    void UpdateAllDisplays()
    {
        // Update available points
        availablePointsText.text = availablePoints.ToString();
        
        // Update stat levels with shorter format
        vitalityLevelText.text = $"Lv.{vitalityLevel}";
        healthLevelText.text = $"Lv.{healthLevel}";
        meleeLevelText.text = $"Lv.{meleeLevel}";
        rangedLevelText.text = $"Lv.{rangedLevel}";
        magicLevelText.text = $"Lv.{magicLevel}";
        
        // Enable/disable buttons based on available points
        bool canSpend = availablePoints > 0;
        vitalityPlusButton.interactable = canSpend;
        healthPlusButton.interactable = canSpend;
        meleePlusButton.interactable = canSpend;
        rangedPlusButton.interactable = canSpend;
        magicPlusButton.interactable = canSpend;
    }
    
    void CloseAllMenus()
    {
        // Find the MenuManager and close all menus
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
}