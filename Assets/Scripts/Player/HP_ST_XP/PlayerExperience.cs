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

    [Header("Progression Tuning")]
    [Tooltip("XP needed at level 0.")]
    public float baseXPAtLevel0 = 100f;

    [Tooltip("Multiplier per level. 1.0 = always baseXP; 1.2 = +20% each level.")]
    public float levelXPGrowth = 1.20f;

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
    // _fadeT = 0 → show levelText, hide detail; 1 → hide levelText, show detail
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
        // Hover detection on the EXACT label area ("XP Border Label")
        bool hovering = IsPointerOver(xpHoverTarget);

        float target = hovering ? 1f : 0f; // 1 = show detail, 0 = show level
        _fadeT = Mathf.MoveTowards(_fadeT, target, crossfadeSpeed * Time.unscaledDeltaTime);
        ApplyCrossfade(_fadeT, force: false);
    }

    /// <summary>
    /// Public API: add XP (queued & animated).
    /// </summary>
    public void AddXP(float amount)
    {
        if (amount <= 0f) return;

        if (animateXP)
        {
            _pendingXP += amount;
            if (_animRoutine == null)
                _animRoutine = StartCoroutine(AnimatePendingXP());
        }
        else
        {
            // Instant application (no animation)
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
        // Handle overflow
        while (xpInLevel >= xpToNextLevel)
        {
            xpInLevel -= xpToNextLevel;
            level++;
            xpToNextLevel = CalculateXPToNextLevel(level);
            OnLevelUp?.Invoke(level);
        }
        UpdateUI();
        RaiseXPChanged();
    }

    private System.Collections.IEnumerator AnimatePendingXP()
    {
        while (_pendingXP > 0f)
        {
            // If this level is already complete (edge case), level up first
            if (xpInLevel >= xpToNextLevel)
            {
                xpInLevel -= xpToNextLevel;
                level++;
                xpToNextLevel = CalculateXPToNextLevel(level);
                OnLevelUp?.Invoke(level);
                UpdateUI();
                RaiseXPChanged();
            }

            // Determine how much XP to pour this frame based on bar speed
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

        while (xpInLevel >= xpToNextLevel)
        {
            xpInLevel -= xpToNextLevel;
            level++;
            xpToNextLevel = CalculateXPToNextLevel(level);
            OnLevelUp?.Invoke(level);
        }
    }

    /// <summary>
    /// base * growth^level. Set growth=1.0 to keep the same XP each level.
    /// </summary>
    private float CalculateXPToNextLevel(int lvl)
    {
        return Mathf.Max(1f, baseXPAtLevel0 * Mathf.Pow(levelXPGrowth, Mathf.Max(0, lvl)));
    }

    private void UpdateUI()
    {
        if (xpFillImage != null)
        {
            float fill = (xpToNextLevel <= 0f) ? 0f : Mathf.Clamp01(xpInLevel / xpToNextLevel);
            xpFillImage.fillAmount = fill;
        }

        if (levelText != null)
            levelText.text = $"Lv. {level}";

        if (xpDetailText != null)
            xpDetailText.text = $"{Mathf.FloorToInt(xpInLevel)} / {Mathf.FloorToInt(xpToNextLevel)}";
    }

    private void RaiseXPChanged()
    {
        OnXPChanged?.Invoke(level, xpInLevel, xpToNextLevel);
    }

    // ---------- hover/crossfade helpers ----------
    private bool IsPointerOver(RectTransform target)
    {
        if (target == null) return false;
        Vector2 mousePos = Input.mousePosition;
        return RectTransformUtility.RectangleContainsScreenPoint(target, mousePos, uiCamera);
    }

    private void ApplyCrossfade(float t, bool force)
    {
        t = Mathf.Clamp01(t);

        // Detail text alpha = t
        if (xpDetailText != null)
        {
            var c = _detailBaseColor;
            c.a = t;
            xpDetailText.color = c;
        }

        // Level text alpha = 1 - t
        if (levelText != null)
        {
            var c = _levelBaseColor;
            c.a = 1f - t;
            levelText.color = c;
        }
    }

    // Convenience getters
    public float GetFill01() => (xpToNextLevel <= 0f) ? 0f : Mathf.Clamp01(xpInLevel / xpToNextLevel);
    public float GetXPNeededThisLevel() => xpToNextLevel;
    public float GetXPInLevel() => xpInLevel;
    public int   GetLevel() => level;
}