using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class GravityBox : GravityArea
{
    public enum FaceDirection
    {
        Up,
        Down,
        Left,
        Right,
        Forward,
        Back
    }

    [Header("Choose Which Local Face Acts as 'Down'")]
    public FaceDirection gravityFace = FaceDirection.Down;
    
    [Header("Gravity Change Delay")]
    [Tooltip("Time in seconds before gravity change is applied after entering/exiting")]
    public float gravityChangeDelay = 0.5f;
    
    [Header("Debug Visualization")]
    public bool showDebugVisuals = true;
    public Color boxColor = new Color(0.2f, 0.4f, 0.8f, 0.3f);
    public Color gravityFaceColor = new Color(1f, 0.3f, 0.3f, 0.5f);

    private BoxCollider boxCollider;
    private Vector3 lastGravityDirection;
    private Transform playerTransform = null;
    private Collider playerCollider = null;
    
    // For tracking players within the trigger volume
    private System.Collections.Generic.Dictionary<GravityBody, float> playersEnteringTime = 
        new System.Collections.Generic.Dictionary<GravityBody, float>();
    private System.Collections.Generic.Dictionary<GravityBody, float> playersExitingTime = 
        new System.Collections.Generic.Dictionary<GravityBody, float>();

    private void Awake()
    {
        boxCollider = GetComponent<BoxCollider>();
        if (boxCollider)
        {
            boxCollider.isTrigger = true;
        }
        else
        {
            Debug.LogError("GravityBox requires a BoxCollider component.");
        }
    }
    
    private void Update()
    {
        // Process delayed gravity changes for entering players
        ProcessDelayedGravityChanges();
    }
    
    private void ProcessDelayedGravityChanges()
    {
        // Track players that need to be processed
        System.Collections.Generic.List<GravityBody> bodiesToProcess = new System.Collections.Generic.List<GravityBody>();
        System.Collections.Generic.List<GravityBody> bodiesToRemove = new System.Collections.Generic.List<GravityBody>();
        
        // Check all entering players
        foreach (var entry in playersEnteringTime)
        {
            GravityBody body = entry.Key;
            float entryTime = entry.Value;
            
            // If the delay time has passed, apply gravity change
            if (Time.time - entryTime >= gravityChangeDelay)
            {
                bodiesToProcess.Add(body);
            }
        }
        
        // Apply delayed gravity changes for entering players
        foreach (var body in bodiesToProcess)
        {
            // Apply gravity change now that delay has passed
            ApplyGravityChange(body);
            playersEnteringTime.Remove(body);
        }
        
        // Process exiting players
        bodiesToProcess.Clear();
        
        foreach (var entry in playersExitingTime)
        {
            GravityBody body = entry.Key;
            float exitTime = entry.Value;
            
            // If the delay time has passed or the body is null, remove it from tracking
            if (body == null || Time.time - exitTime >= gravityChangeDelay)
            {
                bodiesToProcess.Add(body);
                
                if (body != null)
                {
                    ApplyGravityExit(body);
                }
            }
        }
        
        // Remove processed players from exit tracking
        foreach (var body in bodiesToProcess)
        {
            playersExitingTime.Remove(body);
        }
    }

    public override Vector3 GetGravityDirection(GravityBody gravityBody)
    {
        // Get the direction based on the chosen face
        Vector3 gravityDirection = GetGravityDirectionFromFace();
        
        // Store the direction for debug visualization
        lastGravityDirection = gravityDirection;
        
        return gravityDirection;
    }
    
    private Vector3 GetGravityDirectionFromFace()
    {
        // Determine direction based on the chosen face (in local space)
        switch (gravityFace)
        {
            case FaceDirection.Up:
                return transform.up;
            case FaceDirection.Down:
                return -transform.up;
            case FaceDirection.Left:
                return -transform.right;
            case FaceDirection.Right:
                return transform.right;
            case FaceDirection.Forward:
                return transform.forward;
            case FaceDirection.Back:
                return -transform.forward;
            default:
                return -transform.up; // Default to Down direction
        }
    }
    
    // Called when a player object enters the trigger zone
    private void OnTriggerEnter(Collider other)
    {
        GravityBody gravityBody = other.GetComponentInParent<GravityBody>();
        if (gravityBody)
        {
            Debug.Log($"Player entered gravity box trigger: {name} - Applying after {gravityChangeDelay} seconds");
            
            // Store reference to player transform
            playerTransform = gravityBody.transform;
            playerCollider = other;
            
            // Instead of immediately applying gravity change, store the entry time
            // If player is already in the transition list, update the time
            if (!playersEnteringTime.ContainsKey(gravityBody))
            {
                playersEnteringTime.Add(gravityBody, Time.time);
            }
            else
            {
                playersEnteringTime[gravityBody] = Time.time;
            }
            
            // Remove from exiting list if present (player re-entered before exit completed)
            if (playersExitingTime.ContainsKey(gravityBody))
            {
                playersExitingTime.Remove(gravityBody);
            }
        }
    }
    
    // Called when player leaves the trigger completely
    private void OnTriggerExit(Collider other)
    {
        GravityBody gravityBody = other.GetComponentInParent<GravityBody>();
        if (gravityBody && gravityBody.transform == playerTransform)
        {
            Debug.Log($"Player exited gravity box trigger: {name} - Applying after {gravityChangeDelay} seconds");
            
            // Add to exiting players list with current time
            if (!playersExitingTime.ContainsKey(gravityBody))
            {
                playersExitingTime.Add(gravityBody, Time.time);
            }
            else
            {
                playersExitingTime[gravityBody] = Time.time;
            }
            
            // Remove from entering list if present (player exited before entry completed)
            if (playersEnteringTime.ContainsKey(gravityBody))
            {
                playersEnteringTime.Remove(gravityBody);
            }
        }
    }
    
    // Apply gravity change after delay
    private void ApplyGravityChange(GravityBody gravityBody)
    {
        if (gravityBody == null) return;
        
        // Notify player camera about the transition
        NotifyCameraOfTransition(gravityBody.transform);
        
        // Add this gravity area and force alignment
        gravityBody.AddGravityArea(this);
        
        Debug.Log($"Delayed gravity change applied for {name}");
    }
    
    // Apply gravity exit after delay
    private void ApplyGravityExit(GravityBody gravityBody)
    {
        if (gravityBody == null) return;
        
        // Notify camera before removing the gravity area
        NotifyCameraOfTransition(gravityBody.transform);
        
        // Remove this gravity area
        gravityBody.RemoveGravityArea(this);
        
        // Force an alignment with the new gravity direction
        gravityBody.ForceAlignWithGravity(true);
        
        playerTransform = null;
        playerCollider = null;
        
        Debug.Log($"Delayed gravity exit applied for {name}");
    }
    
    // GravityBox.cs — replace NotifyCameraOfTransition
private void NotifyCameraOfTransition(Transform playerTransform)
{
    var gravityBody = playerTransform.GetComponent<GravityBody>();
    var playerFlight = playerTransform.GetComponent<PlayerFlight>();
    var playerCamera = playerTransform.GetComponentInChildren<PlayerCamera>();
    if (gravityBody == null) return;

    const float SAME_GRAVITY_DOT = 0.99985f; // ~1°
    Vector3 oldUp = -gravityBody.GetEffectiveGravityDirection().normalized;
    Vector3 newUp = -GetGravityDirection(gravityBody).normalized;
    bool same = Vector3.Dot(oldUp, newUp) >= SAME_GRAVITY_DOT;

    // Always register area (priority bookkeeping)
    gravityBody.AddGravityArea(this);

    if (same)
    {
        // No visual/input transition; don’t spam Started
        return;
    }

    // Real change → inform camera/flight
    if (playerCamera != null) playerCamera.OnGravityTransitionStarted();
    if (playerFlight != null) playerFlight.OnGravityTransitionStarted(oldUp, newUp, 0f);
}

    private bool AreDirectionsSimilar(Vector3 a, Vector3 b, float threshold = 0.01f)
    {
        return Vector3.Angle(a, b) < (threshold * 180f); // ~0.01 means ~1.8° difference allowed
    }

    // Visualize the gravity area in the editor
    private void OnDrawGizmos()
    {
        if (!showDebugVisuals)
            return;
        
        // Ensure we have a valid box collider
        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol == null)
            return;
            
        // Save original matrix
        Matrix4x4 originalMatrix = Gizmos.matrix;
        Matrix4x4 rotationMatrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        
        // Draw the box (outer bounds)
        Gizmos.color = boxColor;
        Gizmos.matrix = rotationMatrix;
        Gizmos.DrawCube(boxCol.center, boxCol.size);
        
        // Calculate the face position and size
        Vector3 faceCenter = boxCol.center;
        Vector3 faceSize = boxCol.size;
        Vector3 offset = Vector3.zero;
        float faceDepth = 0.01f;
        
        switch (gravityFace)
        {
            case FaceDirection.Up:
                offset = new Vector3(0, boxCol.size.y * 0.5f, 0);
                faceSize = new Vector3(boxCol.size.x, faceDepth, boxCol.size.z);
                break;
            case FaceDirection.Down:
                offset = new Vector3(0, -boxCol.size.y * 0.5f, 0);
                faceSize = new Vector3(boxCol.size.x, faceDepth, boxCol.size.z);
                break;
            case FaceDirection.Left:
                offset = new Vector3(-boxCol.size.x * 0.5f, 0, 0);
                faceSize = new Vector3(faceDepth, boxCol.size.y, boxCol.size.z);
                break;
            case FaceDirection.Right:
                offset = new Vector3(boxCol.size.x * 0.5f, 0, 0);
                faceSize = new Vector3(faceDepth, boxCol.size.y, boxCol.size.z);
                break;
            case FaceDirection.Forward:
                offset = new Vector3(0, 0, boxCol.size.z * 0.5f);
                faceSize = new Vector3(boxCol.size.x, boxCol.size.y, faceDepth);
                break;
            case FaceDirection.Back:
                offset = new Vector3(0, 0, -boxCol.size.z * 0.5f);
                faceSize = new Vector3(boxCol.size.x, boxCol.size.y, faceDepth);
                break;
        }
        
        // Draw the selected face
        Gizmos.color = gravityFaceColor;
        Gizmos.DrawCube(faceCenter + offset, faceSize);
        
        // Restore original matrix
        Gizmos.matrix = originalMatrix;
    }
}