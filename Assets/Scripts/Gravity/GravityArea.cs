using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class GravityArea : MonoBehaviour
{
    [SerializeField] private int _priority;
    public int Priority => _priority;

    protected virtual void Awake()
    {
        var col = GetComponent<Collider>();
        if (!col)
        {
            Debug.LogError($"{nameof(GravityArea)} requires a Collider.");
            return;
        }
        col.isTrigger = true; // areas should be triggers
    }

    /// <summary>Return a world-space gravity down direction (does not need to be normalized).</summary>
    public abstract Vector3 GetGravityDirection(GravityBody body);

    // Default = no-op so derived classes can fully control add/remove timing.
    protected virtual void OnTriggerEnter(Collider other) { }
    protected virtual void OnTriggerExit(Collider other)  { }
}
