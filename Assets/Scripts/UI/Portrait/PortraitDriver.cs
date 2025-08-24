using UnityEngine;

public class PortraitDriver : MonoBehaviour
{
    public Animator anim;              // assign your PlayerProfile Animator in inspector
    [Header("Param Names")]
    public string hurtTrigger = "Hurt";

    public void PlayHurt()
    {
        if (!anim) return;
        // Reset first so repeated hits fire even if already mid-animation
        anim.ResetTrigger(hurtTrigger);
        anim.SetTrigger(hurtTrigger);

        Debug.Log("PortraitDriver: PlayHurt() called");
    }
}
