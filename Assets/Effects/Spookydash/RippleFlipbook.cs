using UnityEngine;

/// Drop this on the quad that uses your Ghost Border material.
/// Requires your ShaderGraph to expose:
///   float _TopStrength   (0..1 alpha for the overlay)
///   Color _TopTint       (optional – tint; comment out if you don’t have it)
[RequireComponent(typeof(Renderer))]
public class GhostBorderSimple : MonoBehaviour
{
    [Header("Who to react to")]
    public Transform spook;          // your player
    public float nearDistance = 6f;  // start brightening within this range

    [Header("Alpha settings")]
    [Range(0f, 1f)] public float farAlpha  = 0.12f;  // idle, far away
    [Range(0f, 1f)] public float nearAlpha = 0.22f;  // when close
    public float fadeSpeed = 10f;                    // how snappy the fade feels

    [Header("Breathing (subtle)")]
    public bool breathe = true;
    public float breatheAmplitude = 0.10f;   // ±10% of current alpha
    public float breatheSpeed = 1.0f;        // ~1 cycle per second

    [Header("Tint (optional)")]
    public Color tint = new Color(0.27f, 0.84f, 0.85f, 1f); // your ColdGoo-07-ish
    public bool setTint = true;

    Renderer ren;
    MaterialPropertyBlock block;
    int idTopStrength, idTopTint;

    float targetA;   // where alpha wants to go (near/far)
    float currentA;  // smoothed value

    void Awake()
    {
        ren = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        idTopStrength = Shader.PropertyToID("_TopStrength");
        idTopTint     = Shader.PropertyToID("_TopTint");
        currentA = farAlpha;
        Apply();
    }

    void Update()
    {
        // 1) Pick near vs far alpha based on distance to the Spook
        float a = farAlpha;
        if (spook != null)
        {
            float d = Vector3.Distance(spook.position, transform.position);
            float t = Mathf.InverseLerp(nearDistance * 1.2f, nearDistance, d); // 0..1 as you approach
            a = Mathf.Lerp(farAlpha, nearAlpha, t);
        }
        targetA = a;

        // 2) Smooth toward target for nice easing
        currentA = Mathf.Lerp(currentA, targetA, 1f - Mathf.Exp(-fadeSpeed * Time.deltaTime));

        // 3) Optional tiny breathing so it feels alive
        float outA = currentA;
        if (breathe)
        {
            float wobble = 1f + Mathf.Sin(Time.time * Mathf.PI * 2f * breatheSpeed) * breatheAmplitude;
            outA *= wobble;
        }

        // 4) Push values to material
        ren.GetPropertyBlock(block);
        block.SetFloat(idTopStrength, outA);
        if (setTint) block.SetColor(idTopTint, tint);
        ren.SetPropertyBlock(block);
    }

    /// Call this from your dash code when phase starts.
    public void DashPulse(float extraAlpha = 0.08f, float duration = 0.12f)
    {
        StopAllCoroutines();
        StartCoroutine(Pulse(extraAlpha, duration));
    }

    System.Collections.IEnumerator Pulse(float extra, float time)
    {
        ren.GetPropertyBlock(block);
        float baseA = block.GetFloat(idTopStrength);
        float t = 0f;
        while (t < time)
        {
            t += Time.deltaTime;
            float k = 1f - (t / time);  // fade out
            block.SetFloat(idTopStrength, baseA + extra * k);
            ren.SetPropertyBlock(block);
            yield return null;
        }
    }

    void Apply()
    {
        ren.GetPropertyBlock(block);
        block.SetFloat(idTopStrength, currentA);
        if (setTint) block.SetColor(idTopTint, tint);
        ren.SetPropertyBlock(block);
    }
}
