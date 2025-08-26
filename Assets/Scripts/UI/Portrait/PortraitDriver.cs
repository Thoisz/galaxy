// Assets/Scripts/UI/Portrait/PortraitDriver.cs
using UnityEngine;

public class PortraitDriver : MonoBehaviour
{
    [Header("Animator (Hurt trigger only)")]
    public Animator anim;
    public string hurtTrigger = "Hurt";

    [Header("Blendshape Setup")]
    public SkinnedMeshRenderer faceMesh;     // the portrait head SkinnedMeshRenderer
    public string blendShapeName = "EyesHalf";
    public int blendShapeIndex = -1;         // auto-resolved from name at runtime

    [Header("Blendshape Drive")]
    [Range(0f, 20f)] public float blendLerpSpeed = 12f; // how fast weight eases to target

    // inputs from systems
    private float healthPct = 1f;   // 0..1
    private float staminaPct = 1f;  // 0..1

    // internal drive
    private float currentWeight = 0f; // 0..100
    private float targetWeight  = 0f; // 0..100

    void Awake()
    {
        // Resolve blendshape index if using name
        if (faceMesh != null)
        {
            if (blendShapeIndex < 0 && !string.IsNullOrEmpty(blendShapeName))
            {
                int count = faceMesh.sharedMesh != null ? faceMesh.sharedMesh.blendShapeCount : 0;
                for (int i = 0; i < count; i++)
                {
                    if (faceMesh.sharedMesh.GetBlendShapeName(i) == blendShapeName)
                    {
                        blendShapeIndex = i;
                        break;
                    }
                }
            }

            // Ensure neutral at start
            if (blendShapeIndex >= 0)
            {
                currentWeight = 0f;
                targetWeight  = 0f;
                faceMesh.SetBlendShapeWeight(blendShapeIndex, 0f);
            }
        }
    }

    void Update()
    {
        // Pick severity from both sources (the “worst” wins)
        int sevHealth = SeverityFromPct(healthPct);
        int sevStam   = SeverityFromPctForStamina(staminaPct);
        int sev       = Mathf.Max(sevHealth, sevStam);

        targetWeight = sev * 50f; // 0, 50, 100

        // Smoothly ease
        if (faceMesh != null && blendShapeIndex >= 0)
        {
            currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, blendLerpSpeed * 100f * Time.unscaledDeltaTime);
            currentWeight = Mathf.Clamp(currentWeight, 0f, 100f);
            faceMesh.SetBlendShapeWeight(blendShapeIndex, currentWeight);
        }
    }

    // Map 0..1 to 0/1/2 severity for HEALTH
    // 2 = ≤20%, 1 = (20%, 50%], 0 = >50%
    private int SeverityFromPct(float pct01)
    {
        if (pct01 <= 0.20f) return 2;
        if (pct01 <= 0.50f) return 1;
        return 0;
    }

    // For STAMINA we only care about the 20% face (per your spec)
    // 2 = ≤20%, else 0
    private int SeverityFromPctForStamina(float pct01)
    {
        return (pct01 <= 0.10f) ? 2 : 0;
    }

    // -------- public API called by other systems --------
    public void SetHealthPercent(float pct01)
    {
        healthPct = Mathf.Clamp01(pct01);
    }

    public void SetStaminaPercent(float pct01)
    {
        staminaPct = Mathf.Clamp01(pct01);
    }

    public void PlayHurt()
    {
        if (!anim) return;
        anim.ResetTrigger(hurtTrigger);
        anim.SetTrigger(hurtTrigger);
    }
}
