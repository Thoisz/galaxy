using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TractorBeamGravity : MonoBehaviour
{
    public enum BoxFace
    {
        Up,
        Down,
        Left,
        Right,
        Forward,
        Back
    }
    
    [Header("Force Settings")]
    [Tooltip("Which face of the box applies the pull force")]
    public BoxFace pullFace = BoxFace.Up;
    
    [Tooltip("Strength of the pull force")]
    [Range(0f, 150f)]
    public float pullForce = 50f;
    
    [Header("Visual Settings")]
    [SerializeField] private Color beamColor = new Color(0f, 1f, 0.8f, 0.5f);
    [SerializeField] private Color facePlaneColor = new Color(1f, 0.5f, 0f, 0.7f);
    [SerializeField] private bool showDebugVisuals = true;
    
    private Collider triggerCollider;
    private Rigidbody playerRigidbody;
    
    private void Awake()
    {
        // Get collider and ensure it's a trigger
        triggerCollider = GetComponent<Collider>();
        triggerCollider.isTrigger = true;
        
        Debug.Log("TractorBeamGravity initialized on " + gameObject.name);
    }
    
    private void OnTriggerEnter(Collider other)
    {
        // Check if it's the player
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
        {
            rb = other.GetComponentInParent<Rigidbody>();
        }
        
        if (rb != null && rb.CompareTag("Player"))
        {
            playerRigidbody = rb;
            Debug.Log("Player entered tractor beam: " + gameObject.name);
            
            // Check if player has GravityBody and disable it if needed
            GravityBody gravityBody = rb.GetComponent<GravityBody>();
            if (gravityBody != null)
            {
                gravityBody.enabled = false;
                Debug.Log("Disabled GravityBody while in tractor beam");
            }
            
            // Also disable PlayerPhysicsController gravity if present
            PlayerPhysicsController physicsController = rb.GetComponent<PlayerPhysicsController>();
            if (physicsController != null)
            {
                physicsController.enabled = false;
                Debug.Log("Disabled PlayerPhysicsController while in tractor beam");
            }
        }
    }
    
    private void OnTriggerExit(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
        {
            rb = other.GetComponentInParent<Rigidbody>();
        }
        
        if (rb != null && rb == playerRigidbody)
        {
            Debug.Log("Player exited tractor beam: " + gameObject.name);
            
            // Re-enable GravityBody when leaving
            GravityBody gravityBody = rb.GetComponent<GravityBody>();
            if (gravityBody != null)
            {
                gravityBody.enabled = true;
                Debug.Log("Re-enabled GravityBody after exiting tractor beam");
            }
            
            // Re-enable PlayerPhysicsController when leaving
            PlayerPhysicsController physicsController = rb.GetComponent<PlayerPhysicsController>();
            if (physicsController != null)
            {
                physicsController.enabled = true;
                Debug.Log("Re-enabled PlayerPhysicsController after exiting tractor beam");
            }
            
            playerRigidbody = null;
        }
    }
    
    private void FixedUpdate()
    {
        if (playerRigidbody != null && pullForce > 0)
        {
            // Get the pull direction based on selected face
            Vector3 worldPullDirection = GetPullDirectionFromFace();
            
            // Apply continuous force
            playerRigidbody.AddForce(worldPullDirection * pullForce, ForceMode.Force);
            
            // Log every few frames to avoid spam
            if (Time.frameCount % 30 == 0)
            {
                Debug.Log($"Applied force: {worldPullDirection * pullForce}");
            }
        }
    }
    
    private Vector3 GetPullDirectionFromFace()
    {
        // Convert the face selection to a direction in local space
        Vector3 localDirection = Vector3.zero;
        
        switch (pullFace)
        {
            case BoxFace.Up:
                localDirection = Vector3.up;
                break;
            case BoxFace.Down:
                localDirection = Vector3.down;
                break;
            case BoxFace.Left:
                localDirection = Vector3.left;
                break;
            case BoxFace.Right:
                localDirection = Vector3.right;
                break;
            case BoxFace.Forward:
                localDirection = Vector3.forward;
                break;
            case BoxFace.Back:
                localDirection = Vector3.back;
                break;
        }
        
        // Transform to world space and normalize
        return transform.TransformDirection(localDirection).normalized;
    }
    
    private void OnDrawGizmos()
    {
        if (!showDebugVisuals)
            return;
            
        // Get collider size and center
        Vector3 center = transform.position;
        Vector3 size = Vector3.one;
        Vector3 colliderCenter = Vector3.zero;
        
        // Get specific collider dimensions
        if (GetComponent<BoxCollider>() is BoxCollider box)
        {
            size = box.size;
            colliderCenter = box.center;
        }
        else if (GetComponent<SphereCollider>() is SphereCollider sphere)
        {
            size = new Vector3(sphere.radius * 2, sphere.radius * 2, sphere.radius * 2);
            colliderCenter = sphere.center;
        }
        else
        {
            size = new Vector3(2, 2, 2);
        }
        
        // Transform center to world space
        center = transform.TransformPoint(colliderCenter);
        
        // Ensure proper scale
        size = Vector3.Scale(size, transform.lossyScale);
        
        // Draw collider bounds
        Gizmos.color = new Color(beamColor.r, beamColor.g, beamColor.b, 0.3f);
        Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, size);
        
        // Get direction and face properties
        Vector3 worldPullDirection = GetPullDirectionFromFace();
        
        // Draw the active face with a different color
        Gizmos.color = facePlaneColor;
        
        // Calculate the position and size of the face
        Vector3 faceCenter = Vector3.zero;
        Vector3 faceSize = Vector3.zero;
        float offset = 0;
        
        switch (pullFace)
        {
            case BoxFace.Up:
                offset = size.y * 0.5f;
                faceCenter = new Vector3(0, offset, 0);
                faceSize = new Vector3(size.x, 0.01f, size.z);
                break;
            case BoxFace.Down:
                offset = -size.y * 0.5f;
                faceCenter = new Vector3(0, offset, 0);
                faceSize = new Vector3(size.x, 0.01f, size.z);
                break;
            case BoxFace.Left:
                offset = -size.x * 0.5f;
                faceCenter = new Vector3(offset, 0, 0);
                faceSize = new Vector3(0.01f, size.y, size.z);
                break;
            case BoxFace.Right:
                offset = size.x * 0.5f;
                faceCenter = new Vector3(offset, 0, 0);
                faceSize = new Vector3(0.01f, size.y, size.z);
                break;
            case BoxFace.Forward:
                offset = size.z * 0.5f;
                faceCenter = new Vector3(0, 0, offset);
                faceSize = new Vector3(size.x, size.y, 0.01f);
                break;
            case BoxFace.Back:
                offset = -size.z * 0.5f;
                faceCenter = new Vector3(0, 0, offset);
                faceSize = new Vector3(size.x, size.y, 0.01f);
                break;
        }
        
        // Draw the active face
        Gizmos.DrawCube(faceCenter, faceSize);
        
        // Skip drawing arrows if force is zero
        if (pullForce <= 0)
            return;
            
        // Draw force arrows
        Gizmos.color = beamColor;
        
        // Get the grid dimensions for the active face
        Vector3 gridSize = Vector3.one;
        Vector3 startCorner = Vector3.zero;
        
        switch (pullFace)
        {
            case BoxFace.Up:
            case BoxFace.Down:
                gridSize = new Vector3(size.x, 0, size.z);
                startCorner = new Vector3(-size.x/2, offset, -size.z/2);
                break;
            case BoxFace.Left:
            case BoxFace.Right:
                gridSize = new Vector3(0, size.y, size.z);
                startCorner = new Vector3(offset, -size.y/2, -size.z/2);
                break;
            case BoxFace.Forward:
            case BoxFace.Back:
                gridSize = new Vector3(size.x, size.y, 0);
                startCorner = new Vector3(-size.x/2, -size.y/2, offset);
                break;
        }
        
        // Number of arrows to draw
        int arrows = 5;
        Vector3 step = new Vector3(
            gridSize.x > 0.01f ? gridSize.x / (arrows - 1) : 0,
            gridSize.y > 0.01f ? gridSize.y / (arrows - 1) : 0,
            gridSize.z > 0.01f ? gridSize.z / (arrows - 1) : 0
        );
        
        // Scale arrow length based on force strength (0-150)
        float forceRatio = pullForce / 150f;
        
        for (int i = 0; i < arrows; i++)
        {
            for (int j = 0; j < arrows; j++)
            {
                // Calculate position based on the face
                Vector3 pos = startCorner;
                
                switch (pullFace)
                {
                    case BoxFace.Up:
                    case BoxFace.Down:
                        pos += new Vector3(i * step.x, 0, j * step.z);
                        break;
                    case BoxFace.Left:
                    case BoxFace.Right:
                        pos += new Vector3(0, i * step.y, j * step.z);
                        break;
                    case BoxFace.Forward:
                    case BoxFace.Back:
                        pos += new Vector3(i * step.x, j * step.y, 0);
                        break;
                }
                
                // Transform to world space
                Vector3 worldPos = transform.TransformPoint(pos);
                
                // Draw arrow with length based on force
                float arrowLength = Mathf.Min(size.x, size.y, size.z) * 0.3f * forceRatio;
                Gizmos.DrawRay(worldPos, worldPullDirection * arrowLength);
                
                // Draw arrowhead
                Vector3 arrowEnd = worldPos + worldPullDirection * arrowLength;
                Vector3 right = Vector3.Cross(worldPullDirection, Vector3.up).normalized;
                if (right.magnitude < 0.1f) right = Vector3.Cross(worldPullDirection, Vector3.right).normalized;
                Vector3 up = Vector3.Cross(right, worldPullDirection).normalized;
                
                float headSize = arrowLength * 0.2f;
                Gizmos.DrawRay(arrowEnd, -worldPullDirection * headSize + right * headSize * 0.5f);
                Gizmos.DrawRay(arrowEnd, -worldPullDirection * headSize - right * headSize * 0.5f);
                Gizmos.DrawRay(arrowEnd, -worldPullDirection * headSize + up * headSize * 0.5f);
                Gizmos.DrawRay(arrowEnd, -worldPullDirection * headSize - up * headSize * 0.5f);
            }
        }
        
        // Reset matrix
        Gizmos.matrix = Matrix4x4.identity;
    }
}