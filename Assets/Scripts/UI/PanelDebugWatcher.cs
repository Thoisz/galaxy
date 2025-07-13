using UnityEngine;

public class PanelDebugWatcher : MonoBehaviour
{
    private bool wasActive = true;
    
    void Update()
    {
        bool isCurrentlyActive = gameObject.activeInHierarchy;
        
        if (wasActive && !isCurrentlyActive)
        {
            Debug.Log($"PANEL {gameObject.name} WAS DEACTIVATED!");
            Debug.Log("DEACTIVATION STACK TRACE: " + System.Environment.StackTrace);
        }
        else if (!wasActive && isCurrentlyActive)
        {
            Debug.Log($"PANEL {gameObject.name} WAS ACTIVATED!");
        }
        
        wasActive = isCurrentlyActive;
    }
    
    void OnDisable()
    {
        Debug.Log($"OnDisable called on {gameObject.name}");
        Debug.Log("OnDisable STACK TRACE: " + System.Environment.StackTrace);
    }
}