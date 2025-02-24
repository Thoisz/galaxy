using UnityEngine;

public class GravityZone : MonoBehaviour
{
    public Transform groundObject; // Assign this in the Inspector to the corresponding ground object

    public Vector3 GetGravityDirection()
    {
        if (groundObject == null)
        {
            Debug.LogWarning("Ground object not assigned to GravityZone: " + gameObject.name);
            return Vector3.down; // Default gravity if no ground assigned
        }

        // Gravity direction is the opposite of the ground's normal
        return -groundObject.up;
    }
}