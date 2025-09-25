using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnergyBallFlash : MonoBehaviour
{
    [Header("Sprite mode (use this OR material mode)")]
    [SerializeField] private SpriteRenderer spriteRenderer;       // auto-find
    [SerializeField] private List<Sprite> frames = new();         // drag sliced sprites in order

    [Header("Playback")]
    [SerializeField] private float baseFps = 12f;
    [SerializeField] private float chargedSpeedMultiplier = 1.5f;
    [SerializeField] private bool  hideWhenNotCharged = true;

    [Header("Billboarding")]
    [SerializeField] private bool billboardToCamera = true;
    [SerializeField] private Camera cameraOverride;               // optional

    float _t; int _i; bool _charged;

    void Awake()
    {
        if (!spriteRenderer) spriteRenderer = GetComponent<SpriteRenderer>();
        if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);

        if (!cameraOverride)
            cameraOverride = Camera.main ? Camera.main : FindObjectOfType<Camera>(true);

        ApplyVisibility();
        if (frames.Count > 0 && spriteRenderer) spriteRenderer.sprite = frames[0];
        _t = 0f; _i = 0;
    }

    void Update()
    {
        if (!_charged || !spriteRenderer || frames.Count == 0) return;

        float fps = Mathf.Max(0.01f, baseFps * chargedSpeedMultiplier);
        _t += fps * Time.deltaTime;
        while (_t >= 1f)
        {
            _t -= 1f;
            _i = (_i + 1) % frames.Count;
            spriteRenderer.sprite = frames[_i];
        }
    }

    void LateUpdate()
    {
        if (!billboardToCamera || !cameraOverride) return;
        Vector3 toCam = cameraOverride.transform.position - transform.position;
        if (toCam.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(-toCam.normalized, cameraOverride.transform.up);
    }

    public void SetCharged(bool on)
    {
        _charged = on;
        if (!_charged) { _t = 0f; _i = 0; if (frames.Count > 0 && spriteRenderer) spriteRenderer.sprite = frames[0]; }
        ApplyVisibility();
    }

    void ApplyVisibility()
    {
        if (!spriteRenderer) return;
        spriteRenderer.enabled = !hideWhenNotCharged || _charged;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        baseFps = Mathf.Max(0.01f, baseFps);
        chargedSpeedMultiplier = Mathf.Max(0.01f, chargedSpeedMultiplier);
    }
#endif
}