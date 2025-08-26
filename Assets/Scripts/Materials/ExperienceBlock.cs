using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ExperienceBlock : MonoBehaviour
{
    [Header("XP Settings")]
    [Tooltip("How much XP to grant when the player touches this.")]
    public float xpAmount = 25f;

    [Tooltip("Only grant once, then disable/destroy?")]
    public bool grantOnce = true;

    [Tooltip("Destroy this object after granting XP (if grantOnce is true).")]
    public bool destroyOnGrant = true;

    [Header("Filtering")]
    [Tooltip("Leave empty to accept any object with PlayerExperience. Otherwise only objects with this tag will work.")]
    public string requiredTag = "Player";

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag))
            return;

        var pxp = other.GetComponent<PlayerExperience>();
        if (pxp == null)
            return;

        pxp.AddXP(xpAmount);

        if (grantOnce)
        {
            // Disable collider immediately to avoid multiple grants within the same frame
            var col = GetComponent<Collider>();
            if (col) col.enabled = false;

            if (destroyOnGrant)
                Destroy(gameObject);
            else
                enabled = false; // stop this script if not destroying
        }
    }
}
