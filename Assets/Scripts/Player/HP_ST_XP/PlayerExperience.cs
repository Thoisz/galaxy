using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

[System.Serializable]
public class XPChangedEvent : UnityEvent<int, float, float> { } // (level, xpInLevel, xpToNext)
[System.Serializable]
public class LevelUpEvent   : UnityEvent<int> { }                 // (newLevel)

public class PlayerExperience : MonoBehaviour
{
    [Header("Level & XP")]
    [Tooltip("Current player level (starts at 0).")]
    public int level = 0;

    [Tooltip("XP within the current level.")]
    public float xpInLevel = 0f;

    [Tooltip("XP required to go from this level to the next.")]
    public float xpToNextLevel = 100f;

    [Header("Progression Tuning (Polynomial)")]
    [Tooltip("XP needed at level 0 (base requirement).")]
    public float baseXPAtLevel0 = 100f;

    [Tooltip("Maximum level cap. XP stops advancing past this level.")]
    public int levelCap = 200;

    [Space(6)]
    [Tooltip("Linear coefficient for XP requirement (multiplies level).")]
    public float linearPerLevel = 150f;

    [Tooltip("Quadratic coefficient for XP requirement (multiplies level^2).")]
    public float quadraticPerLevel = 8f;

    [Tooltip("Cubic coefficient for XP requirement (multiplies level^3). Set 0 for pure quadratic.")]
    public float cubicPerLevel = 0f;

    [Header("Skill Points")]
    [Tooltip("How many skill points the player earns per level-up.")]
    public int skillPointsPerLevel = 3;
    public UnityEvent<int> OnSkillPointsEarned; // argument = points earned

    [Header("UI (Radial Fill)")]
    [Tooltip("Assign your radial Image (Fill Method = Radial).")]
    public Image xpFillImage;

    [Header("Texts")]
    [Tooltip("TMP that shows 'Lv. X'. This fades OUT on hover.")]
    public TextMeshProUGUI levelText;

    [Tooltip("TMP that shows 'currentXP / neededXP'. This fades IN on hover.")]
    public TextMeshProUGUI xpDetailText;

    [Header("Hover Target")]
    [Tooltip("Set this to the 'XP Border Label' RectTransform (the level label area).")]
    public RectTransform xpHoverTarget;

    [Tooltip("Leave null for Screen Space - Overlay; assign UI Camera for Screen Space - Camera canvases.")]
    public Camera uiCamera;

    [Header("Crossfade / Fill Animation")]
    [Tooltip("How quickly the two texts crossfade.")]
    public float crossfadeSpeed = 10f;

    [Tooltip("How fast the bar fills, in 'bars per second' (1 = one full circle per second).")]
    public float fillSpeedBarsPerSec = 2.0f;

    [Tooltip("Animate XP gain instead of applying instantly.")]
    public bool animateXP = true;

    [Header("Events")]
    public XPChangedEvent OnXPChanged;
    public LevelUpEvent OnLevelUp;

    // --- internals for XP animation ---
    private float _pendingXP = 0f;
    private Coroutine _animRoutine;

    // --- internals for crossfade ---
    private float _fadeT = 0f;
    private Color _levelBaseColor = Color.white;
    private Color _detailBaseColor = Color.white;

    void Awake()
    {
        xpToNextLevel = CalculateXPToNextLevel(level);

        if (levelText != null)  _levelBaseColor  = levelText.color;
        if (xpDetailText != null) _detailBaseColor = xpDetailText.color;

        // Start state: show level, hide detail
        ApplyCrossfade(0f, force: true);

        UpdateUI();
        RaiseXPChanged();
    }

    void Update()
    {
        bool hovering = IsPointerOver(xpHoverTarget);

        float target = hovering ? 1f : 0f;
        _fadeT = Mathf.MoveTowards(_fadeT, target, crossfadeSpeed * Time.unscaledDeltaTime);
        ApplyCrossfade(_fadeT, force: false);
    }

    /// <summary>
    /// Public API: add XP (queued & animated).
    /// </summary>
    public void AddXP(float amount)
    {
        if (amount <= 0f) return;
        if (level >= levelCap) return; // no more XP gain past cap

        if (animateXP)
        {
            _pendingXP += amount;
            if (_animRoutine == null)
                _animRoutine = StartCoroutine(AnimatePendingXP());
        }
        else
        {
            ApplyXPInstant(amount);
            UpdateUI();
            RaiseXPChanged();
        }
    }

    /// <summary>
    /// Debug/convenience: set the xp directly (instant).
    /// </summary>
    public void SetXP(float newXpInLevel)
    {
        xpInLevel = Mathf.Max(0f, newXpInLevel);

        while (xpInLevel >= xpToNextLevel && level < levelCap)
        {
            xpInLevel -= xpToNextLevel;
            LevelUp();
        }
        UpdateUI();
        RaiseXPChanged();
    }

    private System.Collections.IEnumerator AnimatePendingXP()
    {
        while (_pendingXP > 0f)
        {
            if (xpInLevel >= xpToNextLevel && level < levelCap)
            {
                xpInLevel -= xpToNextLevel;
                LevelUp();
                UpdateUI();
                RaiseXPChanged();
            }

            float xpPerSecond = Mathf.Max(0.0001f, fillSpeedBarsPerSec) * xpToNextLevel;
            float stepXP = xpPerSecond * Time.deltaTime;

            float spaceInLevel = xpToNextLevel - xpInLevel;
            float add = Mathf.Min(_pendingXP, stepXP, spaceInLevel);

            xpInLevel += add;
            _pendingXP -= add;

            UpdateUI();
            RaiseXPChanged();

            yield return null;
        }

        _animRoutine = null;
    }

    private void ApplyXPInstant(float amount)
    {
        xpInLevel += amount;

        while (xpInLevel >= xpToNextLevel && level < levelCap)
        {
            xpInLevel -= xpToNextLevel;
            LevelUp();
        }
    }

    private void LevelUp()
    {
        level++;
        xpToNextLevel = CalculateXPToNextLevel(level);
        OnLevelUp?.Invoke(level);

        if (skillPointsPerLevel > 0)
            OnSkillPointsEarned?.Invoke(skillPointsPerLevel);
    }

    /// <summary>
    /// Polynomial XP requirement (no exponential blow-up).
    /// XP(lvl) = base + A*lvl + B*lvl^2 + C*lvl^3
    /// Tune A/B/C in inspector. Set C=0 for quadratic.
    /// </summary>
    private float CalculateXPToNextLevel(int lvl)
    {
        if (lvl >= levelCap) return Mathf.Infinity;

        float L = Mathf.Max(0, lvl);
        float xp = baseXPAtLevel0
                   + (linearPerLevel    * L)
                   + (quadraticPerLevel * L * L)
                   + (cubicPerLevel     * L * L * L);

        return Mathf.Max(1f, xp);
    }

    private void UpdateUI()
    {
        if (xpFillImage != null)
        {
            float fill = (xpToNextLevel <= 0f || float.IsInfinity(xpToNextLevel)) ? 0f : Mathf.Clamp01(xpInLevel / xpToNextLevel);
            xpFillImage.fillAmount = fill;
        }

        if (levelText != null)
            levelText.text = $"Lv. {level}";

        if (xpDetailText != null)
        {
            if (float.IsInfinity(xpToNextLevel))
                xpDetailText.text = $"{Mathf.FloorToInt(xpInLevel)} / —";
            else
                xpDetailText.text = $"{Mathf.FloorToInt(xpInLevel)} / {Mathf.FloorToInt(xpToNextLevel)}";
        }
    }

    private void RaiseXPChanged()
    {
        OnXPChanged?.Invoke(level, xpInLevel, xpToNextLevel);
    }

    private bool IsPointerOver(RectTransform target)
    {
        if (target == null) return false;
        Vector2 mousePos = Input.mousePosition;
        return RectTransformUtility.RectangleContainsScreenPoint(target, mousePos, uiCamera);
    }

    private void ApplyCrossfade(float t, bool force)
    {
        t = Mathf.Clamp01(t);

        if (xpDetailText != null)
        {
            var c = _detailBaseColor;
            c.a = t;
            xpDetailText.color = c;
        }

        if (levelText != null)
        {
            var c = _levelBaseColor;
            c.a = 1f - t;
            levelText.color = c;
        }
    }

    // Convenience getters
    public float GetFill01() => (xpToNextLevel <= 0f || float.IsInfinity(xpToNextLevel)) ? 0f : Mathf.Clamp01(xpInLevel / xpToNextLevel);
    public float GetXPNeededThisLevel() => xpToNextLevel;
    public float GetXPInLevel() => xpInLevel;
    public int   GetLevel() => level;
}