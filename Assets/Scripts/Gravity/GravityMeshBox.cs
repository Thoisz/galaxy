using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshCollider))]
public class GravityMeshBox : GravityArea
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

    private MeshCollider meshCollider;
    private Vector3 lastGravityDirection;
    private Transform playerTransform = null;
    private Collider playerCollider = null;

    // Mesh data for processing
    private Bounds meshBounds;
    private Vector3[] meshVertices;
    private int[] meshTriangles;
    
    // For tracking players within the trigger volume
    private Dictionary<GravityBody, float> playersEnteringTime = new Dictionary<GravityBody, float>();
    private Dictionary<GravityBody, float> playersExitingTime = new Dictionary<GravityBody, float>();

    private void Awake()
    {
        // Get the mesh collider
        meshCollider = GetComponent<MeshCollider>();
        if (meshCollider)
        {
            meshCollider.isTrigger = true;
            
            // Store the mesh data for processing
            if (meshCollider.sharedMesh != null)
            {
                meshBounds = meshCollider.sharedMesh.bounds;
                meshVertices = meshCollider.sharedMesh.vertices;
                meshTriangles = meshCollider.sharedMesh.triangles;
            }
            else
            {
                Debug.LogError("MeshCollider has no mesh assigned.");
            }
        }
        else
        {
            Debug.LogError("GravityMeshBox requires a MeshCollider component.");
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
        List<GravityBody> bodiesToProcess = new List<GravityBody>();
        List<GravityBody> bodiesToRemove = new List<GravityBody>();
        
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

    private void OnValidate()
    {
        // Get the mesh collider if we don't have one
        if (meshCollider == null)
        {
            meshCollider = GetComponent<MeshCollider>();
        }
        
        if (meshCollider != null && meshCollider.sharedMesh != null)
        {
            // Update mesh data
            meshBounds = meshCollider.sharedMesh.bounds;
            meshVertices = meshCollider.sharedMesh.vertices;
            meshTriangles = meshCollider.sharedMesh.triangles;
            
            // Force scene view to repaint
            #if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
            #endif
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
    protected override void OnTriggerEnter(Collider other)
    {
        GravityBody gravityBody = other.GetComponentInParent<GravityBody>();
        if (gravityBody)
        {
            Debug.Log($"Player entered mesh gravity box trigger: {name} - Applying after {gravityChangeDelay} seconds");
            
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

    protected override void OnTriggerExit(Collider other)
    {
        GravityBody gravityBody = other.GetComponentInParent<GravityBody>();
        if (gravityBody && gravityBody.transform == playerTransform)
        {
            Debug.Log($"Player exited mesh gravity box trigger: {name} - Applying after {gravityChangeDelay} seconds");
            
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
        
        // Explicitly force alignment to make sure it takes effect
        gravityBody.ForceAlignWithGravity(true);
        
        Debug.Log($"Delayed gravity change applied for mesh box {name}");
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
        
        Debug.Log($"Delayed gravity exit applied for mesh box {name}");
    }
    
    // GravityMeshBox.cs — replace NotifyCameraOfTransition
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
        // Don’t start transitions for “same up”
        return;
    }

    if (playerCamera != null) playerCamera.OnGravityTransitionStarted();
    if (playerFlight != null) playerFlight.OnGravityTransitionStarted(oldUp, newUp, 0f);
}
    
    // Visualize the gravity area in the editor
    private void OnDrawGizmos()
    {
        if (!showDebugVisuals)
            return;
        
        // Ensure we have a valid mesh collider
        MeshCollider meshCol = GetComponent<MeshCollider>();
        if (meshCol == null || meshCol.sharedMesh == null)
            return;
                
        // Save original matrix for proper transformations
        Matrix4x4 originalMatrix = Gizmos.matrix;
        Matrix4x4 rotationMatrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        Gizmos.matrix = rotationMatrix;
        
        // Get mesh data if needed
        if (meshVertices == null || meshTriangles == null)
        {
            meshBounds = meshCol.sharedMesh.bounds;
            meshVertices = meshCol.sharedMesh.vertices;
            meshTriangles = meshCol.sharedMesh.triangles;
        }
        
        // Draw the original mesh in semi-transparent blue
        Gizmos.color = boxColor;
        DrawMeshGizmo(meshCol.sharedMesh.vertices, meshCol.sharedMesh.triangles);
        
        // Draw the gravity direction face
        DrawGravityFace(meshCol.sharedMesh.bounds);
        
        // Restore original matrix
        Gizmos.matrix = originalMatrix;
    }
    
    // Helper method to draw a mesh in the editor
    private void DrawMeshGizmo(Vector3[] vertices, int[] triangles)
    {
        // Draw wireframe of the mesh
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v1 = vertices[triangles[i]];
            Vector3 v2 = vertices[triangles[i + 1]];
            Vector3 v3 = vertices[triangles[i + 2]];
            
            Gizmos.DrawLine(v1, v2);
            Gizmos.DrawLine(v2, v3);
            Gizmos.DrawLine(v3, v1);
        }
    }
    
    // Helper method to draw the gravity face
    private void DrawGravityFace(Bounds bounds)
    {
        // Calculate the face position and size
        Vector3 faceCenter = bounds.center;
        Vector3 faceSize = bounds.size;
        Vector3 offset = Vector3.zero;
        
        // Make this much smaller for a thinner face visualization
        float faceDepth = 0.001f; // Changed from 0.01f to 0.001f
        
        switch (gravityFace)
        {
            case FaceDirection.Up:
                offset = new Vector3(0, bounds.size.y * 0.5f, 0);
                faceSize = new Vector3(bounds.size.x, faceDepth, bounds.size.z);
                break;
            case FaceDirection.Down:
                offset = new Vector3(0, -bounds.size.y * 0.5f, 0);
                faceSize = new Vector3(bounds.size.x, faceDepth, bounds.size.z);
                break;
            case FaceDirection.Left:
                offset = new Vector3(-bounds.size.x * 0.5f, 0, 0);
                faceSize = new Vector3(faceDepth, bounds.size.y, bounds.size.z);
                break;
            case FaceDirection.Right:
                offset = new Vector3(bounds.size.x * 0.5f, 0, 0);
                faceSize = new Vector3(faceDepth, bounds.size.y, bounds.size.z);
                break;
            case FaceDirection.Forward:
                offset = new Vector3(0, 0, bounds.size.z * 0.5f);
                faceSize = new Vector3(bounds.size.x, bounds.size.y, faceDepth);
                break;
            case FaceDirection.Back:
                offset = new Vector3(0, 0, -bounds.size.z * 0.5f);
                faceSize = new Vector3(bounds.size.x, bounds.size.y, faceDepth);
                break;
        }
        
        // Draw the selected face
        Gizmos.color = gravityFaceColor;
        Gizmos.DrawCube(faceCenter + offset, faceSize);
    }
}