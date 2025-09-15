using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FuelBarUI : MonoBehaviour
{
    [Header("Segmented Bar")]
    [SerializeField] private RectTransform blocksContainer;   // assign your "Fill" object
    [SerializeField] private Image blockPrefab;               // optional; uses first Image child if null
    [SerializeField, Min(1)] private int blockCount = 10;

    [Header("Colors")]
    [SerializeField] private Color normalBlockColor = Color.white;
    [SerializeField] private Color criticalBlockColor = new Color(1f, 0.25f, 0.25f);
    [SerializeField, Min(1)] private int criticalTailBlocks = 2; // last N blocks only

    [Header("Optional % Text")]
    [SerializeField] private TMP_Text percentText;

    [Header("(Unused if segments) Legacy continuous fill")]
    [SerializeField] private Image fillImage; // leave NULL when using segments

    private Jetpack _jetpack;
    private readonly List<Image> _blocks = new List<Image>();

    // track last shown segment count so we can do one-way hysteresis on the final block
    private int _lastActiveSegments = 0;

    public void Initialize(Jetpack jetpack)
    {
        _jetpack = jetpack;
        BuildBlocks();

        if (_jetpack != null)
        {
            _jetpack.FuelChanged += OnFuelChanged;
            OnFuelChanged(_jetpack.CurrentFuel, _jetpack.MaxFuel);
        }
    }

    void OnDestroy()
    {
        if (_jetpack != null)
            _jetpack.FuelChanged -= OnFuelChanged;
    }

    void BuildBlocks()
    {
        _blocks.Clear();
        if (!blocksContainer)
        {
            Debug.LogWarning("[FuelBarUI] Blocks Container not set.");
            return;
        }

        Image template = blockPrefab;
        if (!template)
        {
            template = blocksContainer.GetComponentInChildren<Image>(true);
            if (!template)
            {
                Debug.LogWarning("[FuelBarUI] No blockPrefab and no Image child under Fill.");
                return;
            }
        }

        if (template.transform.parent != blocksContainer)
            template = Instantiate(template, blocksContainer);

        template.name = "Block_1";
        _blocks.Add(template);

        while (_blocks.Count < blockCount)
        {
            var dup = Instantiate(template, blocksContainer);
            dup.name = $"Block_{_blocks.Count + 1}";
            _blocks.Add(dup);
        }

        // Hide any extra pre-existing children
        for (int i = 0; i < blocksContainer.childCount; i++)
        {
            var img = blocksContainer.GetChild(i).GetComponent<Image>();
            if (img && !_blocks.Contains(img))
                img.gameObject.SetActive(false);
        }

        foreach (var b in _blocks)
        {
            if (!b) continue;
            b.gameObject.SetActive(false);
            b.color = normalBlockColor;
        }

        _lastActiveSegments = 0;
    }

    void OnFuelChanged(float current, float max)
    {
        float pct = (max > 0f) ? Mathf.Clamp01(current / max) : 0f;

        if (blocksContainer)
        {
            // quantize to segments (left -> right)
            const float tiny = 0.00001f;
            int quantized = Mathf.Clamp(Mathf.FloorToInt(pct * blockCount + tiny), 0, blockCount);
            int active = quantized;

            // ── FINAL-BLOCK HYSTERESIS ───────────────────────────────────────────
            // If we’re between 0% and 1 block of fuel:
            //  - On the way DOWN (lastActive > 0): keep 1 block visible until EXACT 0%.
            //  - On the way UP   (lastActive == 0): keep 0 blocks until we reach the normal 10% threshold.
            if (quantized == 0 && pct > 0f)
            {
                if (_lastActiveSegments > 0)
                    active = 1;     // hold the last block while draining
                else
                    active = 0;     // stay empty until we cross 10% while recharging
            }

            // color rule: only when remaining visible blocks are <= criticalTailBlocks
            bool useCritical = active > 0 && active <= criticalTailBlocks;

            for (int i = 0; i < _blocks.Count; i++)
            {
                var img = _blocks[i];
                if (!img) continue;

                bool on = i < active; // left-to-right fill
                img.gameObject.SetActive(on);
                if (on)
                    img.color = useCritical ? criticalBlockColor : normalBlockColor;
            }

            _lastActiveSegments = active;

            if (percentText) percentText.text = Mathf.RoundToInt(pct * 100f) + "%";
            return;
        }

        // Fallback: continuous image fill (if you’re not using segments)
        if (fillImage)
        {
            fillImage.fillAmount = pct;
            if (percentText) percentText.text = Mathf.RoundToInt(pct * 100f) + "%";
        }
    }
}