using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FuelBarUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text percentText;

    [Header("Look")]
    [SerializeField] private float lerpSpeed = 10f;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color lowFuelColor = Color.red;

    private Jetpack _jetpack;
    private float _target01 = 1f;
    private float _display01 = 1f;

    public void Initialize(Jetpack jetpack)
    {
        _jetpack = jetpack;
        if (_jetpack != null)
        {
            _jetpack.FuelChanged += OnFuelChanged;
            _jetpack.LowFuelChanged += OnLowFuelChanged;

            // Prime once
            OnFuelChanged(_jetpack.CurrentFuel, _jetpack.MaxFuel);
            OnLowFuelChanged(_jetpack.IsLowFuel);
        }
    }

    void OnDestroy()
    {
        if (_jetpack != null)
        {
            _jetpack.FuelChanged -= OnFuelChanged;
            _jetpack.LowFuelChanged -= OnLowFuelChanged;
        }
    }

    void Update()
    {
        if (!fillImage) return;

        _display01 = Mathf.MoveTowards(_display01, _target01, Time.deltaTime * lerpSpeed);
        fillImage.fillAmount = _display01;

        if (percentText) percentText.text = Mathf.RoundToInt(_display01 * 100f) + "%";
    }

    void OnFuelChanged(float current, float max)
    {
        _target01 = max > 0f ? Mathf.Clamp01(current / max) : 0f;

        // Snap immediately on first frame if fill is uninitialized
        if (fillImage && Mathf.Approximately(_display01, 1f) && !Application.isPlaying)
            fillImage.fillAmount = _target01;
    }

    void OnLowFuelChanged(bool low)
    {
        if (fillImage) fillImage.color = low ? lowFuelColor : normalColor;
    }
}
