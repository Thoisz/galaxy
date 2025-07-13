using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class GravityArea : MonoBehaviour
{
    [SerializeField] private int _priority;
    public int Priority => _priority;
    
    void Start()
    {
        // Ensure this is a trigger collider
        Collider collider = GetComponent<Collider>();
        if (collider)
        {
            collider.isTrigger = true;
        }
        else
        {
            Debug.LogError("No Collider found on GravityArea!");
        }
    }
    
    public abstract Vector3 GetGravityDirection(GravityBody _gravityBody);
    
    // Make these methods virtual so derived classes can override them
    protected virtual void OnTriggerEnter(Collider other)
    {
        GravityBody gravityBody = other.GetComponentInParent<GravityBody>();
        if (gravityBody)
        {
            // Add this gravity area and force immediate alignment
            gravityBody.AddGravityArea(this);
        }
    }
    
    protected virtual void OnTriggerExit(Collider other)
    {
        GravityBody gravityBody = other.GetComponentInParent<GravityBody>();
        if (gravityBody)
        {
            // If this is a GravityBox, let it handle its own exit logic
            if (this is GravityBox)
            {
                // GravityBox will handle this with its hysteresis system
                return;
            }
            
            // Otherwise, handle normally
            gravityBody.RemoveGravityArea(this);
        }
    }
}