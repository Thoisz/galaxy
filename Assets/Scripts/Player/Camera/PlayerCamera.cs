using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class PlayerCamera : MonoBehaviour
{
    [Header("Camera Components")]
    [SerializeField] private Transform target;           // The player character
    [SerializeField] private Transform cameraTransform;  // The actual camera

    [Header("Zoom Settings")]
    [SerializeField] private float minZoomDistance = 0.1f;
    [SerializeField] private float maxZoomDistance = 50f;
    [SerializeField] private int zoomInTicks = 15;
    [SerializeField] private int zoomOutTicks = 20;
    [SerializeField] private float zoomSmoothing = 10f;

    [Header("Pan Settings")]
    [SerializeField] private float panSensitivity = 2f;   // Mouse sensitivity

    [Header("Pitch Clamp (Degrees)")]
    [SerializeField] private float maxGravityRelativePitch = 60f;
    public float MinPitch => -maxGravityRelativePitch;
    public float MaxPitch =>  maxGravityRelativePitch;

    [Header("Collision Settings")]
    [SerializeField] private LayerMask collisionMask;
    [SerializeField] private float collisionBuffer = 0.2f;
    [SerializeField] private int collisionQuality = 10;   // Number of raycasts for better detection

    [Header("Target Offset (Local Space)")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0, 1.5f, 0);

    [Header("Layer Settings")]
    [SerializeField] private Renderer[] characterRenderers;
    [SerializeField] private int localPlayerLayer = 8;
    [SerializeField] private int invisibleLayer = 9;

    [Header("First Person Settings")]
    [SerializeField] private float characterRotationSpeed = 10f;  // Only used if smoothing is enabled
    [SerializeField] private bool rotateCharacterInFirstPerson = true;  // Toggle for character rotation in first person
    [SerializeField] private bool pauseRotationDuringGravityTransition = true;  // New setting

    [Header("Gravity Transition Settings")]
    [SerializeField] private bool preserveDirectionDuringTransition = true;
    [SerializeField] private float directionBlendTime = 0.5f;
    [SerializeField] private float transitionStabilizationTime = 0.2f;

    private bool _isRecentering = false;
    [SerializeField] private float _recenterSpeed = 240f; // deg/sec

    // Component references
    private Camera playerCamera;
    private Rigidbody playerRigidbody;
    private Transform playerTransform;

    // Zoom state
    private float currentZoomDistance;
    private float targetZoomDistance;
    private float currentZoomPercentage = 0.5f; // (0 => min distance, 1 => max distance)

    // Orbit angles
    private float _yawDegrees = 0f;
    private float _pitchDegrees = 0f;
    private float _basePitchDegrees = 0f;
    private float _spaceYawDegrees = 0f;
    private float _spacePitchDegrees = 0f;
    private Vector3 frozenSpaceUp = Vector3.up;
    private bool wasPreviouslyPanningInSpace = false;


    // Camera state
    private bool isInFirstPerson = false;
    private bool isPanning = false;
    private bool isChangingGravity = false;
    private bool isExternallyControlledPanning = false; // Flag for when dash script controls panning
    private Vector3 initialCursorPosition;
    private Quaternion finalRotation;
    private Vector3 cameraForwardHorizontal;
    private GravityBody gravityBody;

    // For first-person transitions
    private Vector3 cameraRelativePosition;
    private Quaternion cameraRelativeRotation;
    
    // Player dash component
    private PlayerDash playerDash;
    
    // Player flight component - NEW
    private PlayerFlight playerFlight;

    // Gravity zone reference
    private Vector3 currentGravityZoneUp = Vector3.up;
    private Vector3 previousGravityZoneUp = Vector3.up;

    // For preserving direction during gravity transitions
    private Vector3 _preTransitionWorldForward;
    private Vector3 _preTransitionWorldRight; // NEW: Store right vector for lateral movement
    private Quaternion _preTransitionCameraRotation;
    private Vector3 _preTransitionCameraForward;
    private float _preTransitionYawDegrees;
    private float _preTransitionTime; // NEW: For timing transition effects
    private bool _stabilizingAfterTransition; // NEW: Flag to stabilize camera after transition
    private float _stabilizationTimer; // NEW: Timer for stabilization

    // Public properties
    public bool IsInFirstPerson => isInFirstPerson;
    public Vector3 CameraForwardHorizontal => cameraForwardHorizontal;
    
    // NEW: Public property to check panning state for other components
    public bool IsPanning => isPanning;

    // Free-look (LMB-only) tracking
    private bool isFreeLook = false;

    private void Start()
{
    // Get component references
    playerCamera = cameraTransform.GetComponent<Camera>();
    if (!playerCamera)
        Debug.LogError("No Camera component found on cameraTransform.");

    playerRigidbody = target.GetComponent<Rigidbody>();
    if (!playerRigidbody)
        Debug.LogError("No Rigidbody found on the target.");

    playerTransform = target;

    // Get the GravityBody component
    gravityBody = target.GetComponent<GravityBody>();

    // Get the PlayerDash component
    playerDash = target.GetComponent<PlayerDash>();

    // Get the PlayerFlight component - NEW
    playerFlight = target.GetComponent<PlayerFlight>();

    // Initialize zoom distances
    UpdateZoomDistanceFromPercentage();
    currentZoomDistance = targetZoomDistance;  // Start with no lag

    // Initialize gravity zone up direction
    if (gravityBody != null && gravityBody.GetEffectiveGravityDirection()
 != Vector3.zero)
    {
        currentGravityZoneUp = -gravityBody.GetEffectiveGravityDirection()
.normalized;
        previousGravityZoneUp = currentGravityZoneUp;
    }
    else
    {
        currentGravityZoneUp = Vector3.up;
        previousGravityZoneUp = Vector3.up;
    }

    _preTransitionCameraForward = cameraTransform.forward;

    // Initialize pitch based on current camera angle
    _pitchDegrees = GetPitch();
    _basePitchDegrees = _pitchDegrees;
}

    private void Update()
    {
        UpdateGravityZoneUp();
        
        // NEW: Handle stabilization after gravity transitions
        if (_stabilizingAfterTransition)
        {
            _stabilizationTimer += Time.deltaTime;
            if (_stabilizationTimer >= transitionStabilizationTime)
            {
                _stabilizingAfterTransition = false;
                // Allow normal camera controls again
            }
        }
        
        // Only process input if not in stabilization phase
        if (!_stabilizingAfterTransition)
        {
            HandleInput();
        }
        
        UpdateCameraPosition();
        HandleCharacterLayer();
        
        // Apply character rotation in first person mode,
        // but only if not transitioning gravity
        bool inGravityTransition = gravityBody != null && gravityBody.IsTransitioningGravity;
        bool isDashing = playerDash != null && playerDash.IsDashing();
        
        if (isInFirstPerson && rotateCharacterInFirstPerson && 
            !(inGravityTransition && pauseRotationDuringGravityTransition) &&
            !isDashing) // Don't rotate character if dashing - let dash script handle it
        {
            RotateCharacterWithCamera();
        }
        
        // Keep panning active when either mouse button is down.
// Track free-look (LMB-only) separately for other scripts.
if (!isExternallyControlledPanning)
{
    bool rightMouseDown = Mouse.current.rightButton.isPressed;
    bool leftMouseDown  = Mouse.current.leftButton.isPressed;
    isPanning  = rightMouseDown || leftMouseDown;
    isFreeLook = leftMouseDown && !rightMouseDown;
}
    }
    
    public void OnPanningRestored(Vector3 storedForward)
    {
        // If we're not already panning, this is a no-op
        if (!IsPanningActive())
            return;
            
        // Get your camera component
        Camera cam = GetComponentInChildren<Camera>();
        if (cam == null)
            return;
        
        // Create a rotation that matches this direction
        Quaternion targetRotation = Quaternion.LookRotation(storedForward);
        
        // Apply this rotation to the camera
        cam.transform.rotation = targetRotation;
    }

public bool IsFreeLookOnlyActive()
{
    // true when LMB-only (RMB not pressed). While both are down, this is false.
    return isFreeLook;
}

    public void TransformInputDirection(Vector2 inputAxis, out Vector3 worldMoveDirection)
    {
        // Default output
        worldMoveDirection = Vector3.zero;
        
        // Get camera orientation data
        Vector3 cameraUp = currentGravityZoneUp;
        Vector3 cameraForward = Vector3.ProjectOnPlane(cameraTransform.forward, cameraUp).normalized;
        
        // If we can't get a valid forward direction, use the player's forward
        if (cameraForward.sqrMagnitude < 0.001f)
        {
            cameraForward = Vector3.ProjectOnPlane(playerTransform.forward, cameraUp).normalized;
            if (cameraForward.sqrMagnitude < 0.001f)
            {
                // Last resort - use any valid forward
                cameraForward = Vector3.ProjectOnPlane(Vector3.forward, cameraUp).normalized;
            }
        }
        
        // Calculate right vector from up and forward
        Vector3 cameraRight = Vector3.Cross(cameraUp, cameraForward).normalized;
        
        // Combine input with camera directions
        worldMoveDirection = (cameraRight * inputAxis.x + cameraForward * inputAxis.y).normalized;
    }

    public void UpdateGravityUpVector(Vector3 upVector)
    {
        if (upVector.magnitude > 0.01f)
        {
            // Use Lerp for smoother transitions, especially important in space
            // to prevent sudden orientation changes that can cause spinning
            currentGravityZoneUp = Vector3.Lerp(currentGravityZoneUp, upVector.normalized, Time.deltaTime * 5f);
        }
    }

    public void SetSpaceDriftMode(bool isDrifting)
    {
        // Set camera to look at player when drifting in space
        if (isDrifting)
        {
            // TODO: Implement a special camera mode for drifting in space
            // This could include making the camera orbit the player
            // or just ensuring it stays focused on the player while they drift
        }
        else
        {
            // Exit drift mode, return to normal camera behavior
        }
    }

    public void StartRecenteringBehindPlayer(float speedDegPerSec = -1f)
{
    if (speedDegPerSec > 0f) _recenterSpeed = speedDegPerSec;
    // Only recenter when not actively panning; if panning, caller can try again later
    if (!isPanning) _isRecentering = true;
}

public bool IsRecenteringActive()
{
    return _isRecentering;
}

    public void ForceOrientationUpdate(Vector3 upDirection)
{
    if (upDirection.magnitude > 0.01f)
    {
        // Save the frozen direction for space (used in orbiting and panning)
        frozenSpaceUp = upDirection.normalized;

        // Immediately update camera orientation state
        currentGravityZoneUp = upDirection.normalized;
        previousGravityZoneUp = currentGravityZoneUp;

        // Get camera's current forward
        Vector3 cameraForward = cameraTransform.forward;

        // Project camera forward onto the new up plane
        Vector3 horizontalForward = Vector3.ProjectOnPlane(cameraForward, upDirection).normalized;

        if (horizontalForward.sqrMagnitude < 0.01f)
        {
            // Fallbacks in case camera was looking directly up/down
            horizontalForward = Vector3.ProjectOnPlane(Vector3.forward, upDirection).normalized;
            if (horizontalForward.sqrMagnitude < 0.01f)
            {
                horizontalForward = Vector3.ProjectOnPlane(Vector3.right, upDirection).normalized;
            }
        }

        // Create and apply the new orientation
        Quaternion newRotation = Quaternion.LookRotation(horizontalForward, upDirection);
        cameraTransform.rotation = newRotation;

        Debug.Log($"[Camera] ForceOrientationUpdate → up: {upDirection}, forward: {horizontalForward}");
    }
}
    
    private void UpdateGravityZoneUp()
{
    if (gravityBody != null)
    {
        if (gravityBody.IsInSpace)
        {
            // In space: do NOT update the gravity up vector — keep it frozen
            return;
        }

        Vector3 newUp = -gravityBody.GetEffectiveGravityDirection().normalized;

        if (Vector3.Dot(newUp, currentGravityZoneUp) < 0.9999f)
        {
            previousGravityZoneUp = currentGravityZoneUp;
            currentGravityZoneUp = newUp;
        }
    }
}

    private void HandleInput()
{
    bool inGravityTransition = gravityBody != null && gravityBody.IsTransitioningGravity;
    bool isDashing = playerDash != null && playerDash.IsDashing();

    if (inGravityTransition && pauseRotationDuringGravityTransition)
        return;

    if (!isDashing)
        HandleZoomInput();

    Vector2 mouseDelta = Mouse.current.delta.ReadValue();
    mouseDelta = Vector2.Scale(mouseDelta, new Vector2(0.97f, 0.97f));

    // Read both buttons
    bool rightMouseDown = Mouse.current.rightButton.isPressed;
    bool leftMouseDown  = Mouse.current.leftButton.isPressed;

    // LMB-only free-look: true when LMB is pressed and RMB is NOT pressed
    isFreeLook = leftMouseDown && !rightMouseDown;

    if (isInFirstPerson)
    {
        HandleFirstPersonLook(mouseDelta);
    }
    else
    {
        HandleThirdPersonLook(mouseDelta, rightMouseDown, leftMouseDown);
    }
}

private void HandleZoomInput()
{
    // Check if mouse is over UI element - don't zoom if it is
    if (UnityEngine.EventSystems.EventSystem.current != null && 
        UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
    {
        return; // Don't process zoom if mouse is over UI
    }

    float scrollInput = Mouse.current.scroll.ReadValue().y;
    if (!Mathf.Approximately(scrollInput, 0f))
    {
        float zoomStep = 1f / (scrollInput > 0 ? zoomInTicks : zoomOutTicks);
        currentZoomPercentage += (scrollInput > 0) ? -zoomStep : zoomStep;
        currentZoomPercentage = Mathf.Clamp01(currentZoomPercentage);
        UpdateZoomDistanceFromPercentage();
    }
}

private void HandleFirstPersonLook(Vector2 mouseDelta)
{
    LockCursor(true);
    _yawDegrees += mouseDelta.x * panSensitivity * Time.deltaTime;
    _pitchDegrees += mouseDelta.y * panSensitivity * Time.deltaTime;

bool isInSpace = gravityBody != null && gravityBody.IsInSpace;

if (!isInSpace)
{
    _pitchDegrees = Mathf.Clamp(_pitchDegrees, MinPitch, MaxPitch);
}
}

private void HandleThirdPersonLook(Vector2 mouseDelta, bool rightMouseDown, bool leftMouseDown)
{
    bool anyMouseDown = rightMouseDown || leftMouseDown;
    bool isInSpace = gravityBody != null && gravityBody.IsInSpace;

    // Cancel recenter as soon as the player manually pans again
    if (anyMouseDown) _isRecentering = false;

    if (anyMouseDown)
    {
        if (!isPanning && !isExternallyControlledPanning)
        {
            isPanning = true;
            initialCursorPosition = Mouse.current.position.ReadValue();
            LockCursor(true);
        }

        if (isInSpace)
        {
            if (isPanning && !wasPreviouslyPanningInSpace && (_spaceYawDegrees == 0f && _spacePitchDegrees == 0f))
            {
                Vector3 cameraForward = cameraTransform.forward;
                Vector3 up = currentGravityZoneUp;

                Vector3 flatForward = Vector3.ProjectOnPlane(cameraForward, up).normalized;
                if (flatForward.sqrMagnitude < 0.01f)
                    flatForward = Vector3.forward;

                _spaceYawDegrees = Quaternion.LookRotation(flatForward, up).eulerAngles.y;
                _spacePitchDegrees = -Vector3.SignedAngle(flatForward, cameraForward, Vector3.Cross(flatForward, up));
            }

            _spaceYawDegrees  += mouseDelta.x * panSensitivity * Time.deltaTime;
            _spacePitchDegrees += mouseDelta.y * panSensitivity * Time.deltaTime;
            _spacePitchDegrees  = Mathf.Clamp(_spacePitchDegrees, -90f, 90f);
        }
        else
        {
            _yawDegrees   += mouseDelta.x * panSensitivity * Time.deltaTime;
            _pitchDegrees += mouseDelta.y * panSensitivity * Time.deltaTime;
            _pitchDegrees  = Mathf.Clamp(_pitchDegrees, MinPitch, MaxPitch);
        }
    }
    else if (isPanning && !isExternallyControlledPanning)
    {
        EndThirdPersonPanning();
    }

    wasPreviouslyPanningInSpace = isPanning && isInSpace;
}

private void EndThirdPersonPanning()
{
    bool wasInSpace = gravityBody != null && gravityBody.IsInSpace;
    Vector3 finalCameraForward = cameraTransform.forward;
    Vector3 finalCameraUp = cameraTransform.up;

    isPanning = false;
    LockCursor(false);

    if (wasInSpace)
    {
        // ✅ DO NOT reset gravity based on current camera up (it could be world-up)
        Debug.Log("[Camera] Pan ended in space. Preserving orientation.");

        // Optional: Log effective direction for sanity check
        Debug.Log("[Camera] Effective gravity direction (space): " + gravityBody.GetEffectiveGravityDirection());

        // Optionally update camera to maintain visual stability
        finalRotation = Quaternion.LookRotation(finalCameraForward, finalCameraUp);
        cameraTransform.rotation = finalRotation;

        // 🛑 DO NOT: gravityBody.SetSpaceGravityDirection(-finalCameraUp);

        // Notify flight controller if needed
        if (playerFlight != null && playerFlight.IsFlying())
        {
            playerFlight.SendMessage("OnCameraPanning", finalCameraForward, SendMessageOptions.DontRequireReceiver);
        }
    }
    else if (Mouse.current != null)
    {
        Mouse.current.WarpCursorPosition(initialCursorPosition);
    }
}

    private void UpdateCameraPosition()
{
    currentZoomDistance = Mathf.Lerp(currentZoomDistance, targetZoomDistance, Time.deltaTime * zoomSmoothing);

    Vector3 zoneUp = (gravityBody != null && gravityBody.IsInSpace)
        ? -gravityBody.GetEffectiveGravityDirection()
        : currentGravityZoneUp;
    bool isInSpace = gravityBody != null && gravityBody.IsInSpace;

    if (!isInSpace)
    {
        _pitchDegrees = Mathf.Clamp(_pitchDegrees, MinPitch, MaxPitch);
    }

    // ===== Rotation calculation =====
    if (isInSpace && isPanning)
    {
        Quaternion yawRotation = Quaternion.Euler(0f, _spaceYawDegrees, 0f);
        Quaternion pitchRotation = Quaternion.Euler(-_spacePitchDegrees, 0f, 0f);
        finalRotation = yawRotation * pitchRotation;
    }
    else if (isInSpace)
    {
        finalRotation = cameraTransform.rotation;
    }
    else
    {
        // Normal gravity branch with optional recenter toward player forward
        Vector3 baseForward = Vector3.ProjectOnPlane(Vector3.forward, zoneUp).normalized;
        if (baseForward.sqrMagnitude < 0.001f)
            baseForward = Vector3.ProjectOnPlane(Vector3.right, zoneUp).normalized;

        // If recentering and not panning, gently steer yaw toward player forward
        if (_isRecentering && !isPanning)
        {
            Vector3 playerFwd = Vector3.ProjectOnPlane(playerTransform.forward, zoneUp).normalized;
            if (playerFwd.sqrMagnitude > 0.0001f && baseForward.sqrMagnitude > 0.0001f)
            {
                float targetYaw = SignedAngle(baseForward, playerFwd, zoneUp);
                // Smoothly move yaw toward target
                _yawDegrees = Mathf.MoveTowardsAngle(_yawDegrees, targetYaw, _recenterSpeed * Time.deltaTime);

                // Stop recenter once close enough
                if (Mathf.Abs(Mathf.DeltaAngle(_yawDegrees, targetYaw)) < 1.5f)
                    _isRecentering = false;
            }
            else
            {
                // Nothing sensible to recenter to
                _isRecentering = false;
            }
        }

        Quaternion yawRotation = Quaternion.AngleAxis(_yawDegrees, zoneUp);
        Vector3 yawForward = yawRotation * baseForward;

        Vector3 pitchAxis = Vector3.Cross(yawForward, zoneUp).normalized;
        Quaternion pitchRotation = Quaternion.AngleAxis(_pitchDegrees, pitchAxis);

        Vector3 finalForward = pitchRotation * yawForward;
        finalRotation = Quaternion.LookRotation(finalForward, zoneUp);
    }

    // ===== Position from pivot/zoom =====
    Vector3 localOffset = target.TransformDirection(targetOffset);
    Vector3 pivotPos = target.position + localOffset;
    Vector3 zoomDirection = finalRotation * Vector3.back;
    Vector3 desiredPos = pivotPos + zoomDirection * currentZoomDistance;

    // Horizontal forward for other systems
    Vector3 cameraForward = finalRotation * Vector3.forward;
    cameraForwardHorizontal = Vector3.ProjectOnPlane(cameraForward, zoneUp).normalized;

    // Collision
    desiredPos = HandleCameraCollision(pivotPos, desiredPos);

    // Apply
    cameraTransform.position = desiredPos;
    cameraTransform.rotation = finalRotation;

    // Store
    _preTransitionCameraForward = cameraTransform.forward;

    // Flight notifications
    if (isPanning && playerFlight != null && playerFlight.IsFlying())
        playerFlight.SendMessage("OnCameraPanning", cameraForward, SendMessageOptions.DontRequireReceiver);

    if (playerFlight != null && playerFlight.IsFlying())
        playerFlight.OnCameraPanning(cameraTransform.forward);
}

    private void RotateCharacterWithCamera()
    {
        if (cameraForwardHorizontal.sqrMagnitude < 0.001f) return;

        // Only rotate with camera in first person mode
        if (isInFirstPerson && rotateCharacterInFirstPerson)
        {
            // Create rotation that points character in camera's forward direction while maintaining player's up vector
            Quaternion targetRotation = Quaternion.LookRotation(cameraForwardHorizontal, playerTransform.up);
            
            // Apply the rotation with a slight smoothing
            playerTransform.rotation = Quaternion.Slerp(
                playerTransform.rotation,
                targetRotation,
                Time.deltaTime * characterRotationSpeed
            );
        }
    }

    private Vector3 HandleCameraCollision(Vector3 pivotPos, Vector3 desiredPos)
    {
        // Use gravity zone up for collision detection
        Vector3 zoneUp = currentGravityZoneUp;
        
        // Cache the distance for efficiency
        float distance = Vector3.Distance(pivotPos, desiredPos);
        
        // If we're in first person, skip collision detection
        if (distance < minZoomDistance + 0.2f)
        {
            return desiredPos;
        }
        
        // Main ray check
        if (Physics.Linecast(pivotPos, desiredPos, out RaycastHit hit, collisionMask))
        {
            return hit.point + hit.normal * collisionBuffer;
        }

        // Sphere cast for better collision detection
        float radius = 0.2f;
        Vector3 direction = (desiredPos - pivotPos).normalized;
        
        if (Physics.SphereCast(pivotPos, radius, direction, 
                              out hit, distance, collisionMask))
        {
            return hit.point + hit.normal * collisionBuffer;
        }

        // Additional raycasts for better edge detection - only use if needed
        if (distance > 5f) // Only do this for longer distances to save performance
        {
            // Create orthogonal basis for multi-directional raycasts
            Vector3 right = Vector3.Cross(direction, zoneUp).normalized;
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.Cross(direction, Vector3.right).normalized;
                if (right.sqrMagnitude < 0.001f)
                    right = Vector3.Cross(direction, Vector3.forward).normalized;
            }
            Vector3 up = Vector3.Cross(right, direction).normalized;
            
            // Cast rays in a circle around the main ray - but fewer for performance
            int reducedQuality = Mathf.Min(collisionQuality, 6); // Cap at 6 for performance
            for (int i = 0; i < reducedQuality; i++)
            {
                float angle = (i / (float)reducedQuality) * 2 * Mathf.PI;
                Vector3 offset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
                Vector3 rayStart = pivotPos + offset;
                
                if (Physics.Raycast(rayStart, direction, out hit, distance, collisionMask))
                {
                    return hit.point + hit.normal * collisionBuffer;
                }
            }
        }

        return desiredPos;
    }

    private void UpdateZoomDistanceFromPercentage()
    {
        float zoomRange = maxZoomDistance - minZoomDistance;
        float nonLinearZoomFactor = Mathf.Pow(currentZoomPercentage, 1.5f);
        targetZoomDistance = Mathf.Lerp(minZoomDistance, maxZoomDistance, nonLinearZoomFactor);
    }

    private void HandleCharacterLayer()
    {
        bool shouldBeInFirstPerson = currentZoomDistance <= (minZoomDistance + 0.1f);
        if (shouldBeInFirstPerson != isInFirstPerson)
        {
            isInFirstPerson = shouldBeInFirstPerson;
            
            if (isInFirstPerson)
            {
                // Hide character in first person
                SetCharacterLayer(invisibleLayer);
                playerCamera.cullingMask &= ~(1 << invisibleLayer);
                LockCursor(true);
            }
            else
            {
                // Show character in third person
                SetCharacterLayer(localPlayerLayer);
                playerCamera.cullingMask |= (1 << invisibleLayer);
                LockCursor(false);
            }
        }
    }

    private void SetCharacterLayer(int layer)
    {
        foreach (Renderer renderer in characterRenderers)
        {
            if (renderer != null)
            {
                renderer.gameObject.layer = layer;
                foreach (Transform child in renderer.GetComponentsInChildren<Transform>())
                {
                    child.gameObject.layer = layer;
                }
            }
        }
    }

    private void LockCursor(bool lockCursor)
    {
        if (!isExternallyControlledPanning) // Don't change cursor lock if externally controlled
        {
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible   = !lockCursor;
        }
    }
    
    public void OnGravityTransitionCompleted()
{
    if (gravityBody != null)
    {
        currentGravityZoneUp = -gravityBody.GetEffectiveGravityDirection().normalized;
    }

    // Check if this is a significant gravity change (like flipping to opposite gravity)
    Vector3 oldGravityUp = previousGravityZoneUp;
    Vector3 newGravityUp = currentGravityZoneUp;
    
    // Calculate the dot product to see how different the gravity directions are
    float gravityAlignment = Vector3.Dot(oldGravityUp, newGravityUp);
    bool isSignificantGravityChange = gravityAlignment < 0.5f; // Any major gravity change (not just opposite)
    
    Debug.Log($"[Camera] Gravity alignment: {gravityAlignment}, isSignificantChange: {isSignificantGravityChange}");

    if (isSignificantGravityChange)
    {
        // For ANY significant gravity change, position camera behind player's back
        Debug.Log("[Camera] Significant gravity change - positioning camera behind player's back");
        
        // Get the player's forward direction in the new gravity orientation
        Vector3 playerForward = Vector3.ProjectOnPlane(playerTransform.forward, newGravityUp).normalized;
        
        // Calculate base forward for the new gravity orientation
        Vector3 baseForward = Vector3.ProjectOnPlane(Vector3.forward, newGravityUp).normalized;
        if (baseForward.sqrMagnitude < 0.0001f)
        {
            baseForward = Vector3.ProjectOnPlane(Vector3.right, newGravityUp).normalized;
        }
        
        if (baseForward.sqrMagnitude > 0.0001f && playerForward.sqrMagnitude > 0.0001f)
        {
            // Calculate yaw to position camera behind player (same as player's forward)
            Vector3 cameraDirection = playerForward; // Behind the player
            _yawDegrees = SignedAngle(baseForward, cameraDirection, newGravityUp);
            
            Debug.Log($"[Camera] Set yaw to position behind player: {_yawDegrees}");
        }
        
        // Set a default pitch to look slightly down at the player
        _pitchDegrees = -10f; // Slightly looking down
        _pitchDegrees = Mathf.Clamp(_pitchDegrees, MinPitch, MaxPitch);
        
        Debug.Log($"[Camera] Set pitch for behind view: {_pitchDegrees}");
    }
    else
    {
        // PRESERVE CAMERA'S ORBITAL POSITION for normal gravity transitions
        PreserveCameraOrbitalPosition(oldGravityUp, newGravityUp);
    }
    
    _stabilizingAfterTransition = true;
    _stabilizationTimer = 0f;
    isChangingGravity = false;

    Debug.Log($"[Camera] Final yaw: {_yawDegrees}, pitch: {_pitchDegrees}");

    // Notify PlayerFlight
    if (playerFlight != null)
    {
        playerFlight.OnGravityTransitionCompleted(previousGravityZoneUp, currentGravityZoneUp, 0f);
    }
}

private void PreserveCameraOrbitalPosition(Vector3 oldGravityUp, Vector3 newGravityUp)
{
    Debug.Log("[Camera] Preserving camera orbital position for normal gravity transition");
    
    // Calculate the camera's position relative to the player in the old gravity frame
    Vector3 playerToCamera = cameraTransform.position - target.position;
    
    // Remove any target offset to get pure orbital vector
    Vector3 localOffset = target.TransformDirection(targetOffset);
    Vector3 orbitalVector = playerToCamera - localOffset;
    
    // Calculate the rotation needed to transform from old gravity up to new gravity up
    Quaternion gravityRotation = Quaternion.FromToRotation(oldGravityUp, newGravityUp);
    
    // Rotate the orbital vector to the new gravity orientation
    Vector3 newOrbitalVector = gravityRotation * orbitalVector;
    
    // Project the rotated orbital vector onto the new gravity plane to get horizontal components
    Vector3 horizontalOrbital = Vector3.ProjectOnPlane(newOrbitalVector, newGravityUp).normalized;
    
    if (horizontalOrbital.sqrMagnitude > 0.0001f)
    {
        // Calculate base forward for the new gravity orientation
        Vector3 baseForward = Vector3.ProjectOnPlane(Vector3.forward, newGravityUp).normalized;
        if (baseForward.sqrMagnitude < 0.0001f)
        {
            baseForward = Vector3.ProjectOnPlane(Vector3.right, newGravityUp).normalized;
        }
        
        if (baseForward.sqrMagnitude > 0.0001f)
        {
            // Calculate yaw needed to maintain the camera's orbital position
            float targetYaw = SignedAngle(baseForward, horizontalOrbital, newGravityUp);
            _yawDegrees = targetYaw;
            
            Debug.Log($"[Camera] Preserved orbital position - target yaw: {_yawDegrees}");
        }
    }
    
    // Calculate pitch based on the orbital vector's vertical component
    float orbitalDistance = newOrbitalVector.magnitude;
    if (orbitalDistance > 0.0001f && horizontalOrbital.sqrMagnitude > 0.0001f)
    {
        // Calculate pitch from the angle between horizontal orbital and full orbital vector
        Vector3 rightVector = Vector3.Cross(newGravityUp, horizontalOrbital).normalized;
        float verticalComponent = Vector3.Dot(newOrbitalVector, newGravityUp);
        float horizontalComponent = Vector3.ProjectOnPlane(newOrbitalVector, newGravityUp).magnitude;
        
        // Calculate pitch angle (negative because camera looks toward player)
        float targetPitch = -Mathf.Atan2(verticalComponent, horizontalComponent) * Mathf.Rad2Deg;
        _pitchDegrees = Mathf.Clamp(targetPitch, MinPitch, MaxPitch);
    }
}

private void KeepCameraOnSameSide(Vector3 oldGravityUp, Vector3 newGravityUp)
{
    Debug.Log("[Camera] === DO NOTHING - KEEP CAMERA IN WORLD POSITION ===");
    
    // The key insight: Don't change the camera at all!
    // Just let the player flip underneath the camera
    // The camera should maintain its world position and orientation
    
    Debug.Log($"[Camera] NOT changing yaw (keeping: {_yawDegrees}) or pitch (keeping: {_pitchDegrees})");
    Debug.Log("[Camera] Camera will stay in same world position, player will flip underneath");
    
    // DO NOTHING - this is the fix!
    // The existing camera system will handle everything else properly
}
    
    //Helper method to notify PlayerMovement about gravity transitions
    private void SendGravityTransitionEventToPlayerMovement(bool isStarting)
    {
        // Try to find PlayerMovement component
        PlayerMovement playerMovement = target.GetComponent<PlayerMovement>();
        if (playerMovement != null)
        {
            if (isStarting)
            {
                playerMovement.SendMessage("OnGravityTransitionStarted", SendMessageOptions.DontRequireReceiver);
            }
            else
            {
                playerMovement.SendMessage("OnGravityTransitionCompleted", SendMessageOptions.DontRequireReceiver);
            }
        }
        
        // For PlayerFlight, use direct method calls with parameters
        PlayerFlight playerFlight = target.GetComponent<PlayerFlight>();
        if (playerFlight != null)
        {
            Vector3 oldDirection = previousGravityZoneUp;
            Vector3 newDirection = currentGravityZoneUp;
            float duration = 0.5f; // Use a reasonable default duration
            
            if (isStarting)
            {
                // Directly call the method with parameters
                playerFlight.OnGravityTransitionStarted(oldDirection, newDirection, duration);
            }
            else
            {
                // Directly call the method with parameters
                playerFlight.OnGravityTransitionCompleted(oldDirection, newDirection, duration);
            }
        }
    }

    private void RealignPitchAfterGravityChange()
{
    _pitchDegrees = GetPitch(); // Sync pitch to current camera angle after gravity flip
}

public void OnGravityTransitionStarted()
{
    isChangingGravity = true;
    _preTransitionTime = Time.time;
    
    // Store the ACTUAL world forward direction the camera was looking
    _preTransitionWorldForward = cameraTransform.forward;
    _preTransitionWorldRight = cameraTransform.right;
    _preTransitionCameraRotation = cameraTransform.rotation;
    _preTransitionYawDegrees = _yawDegrees;
    
    // Store gravity directions
    previousGravityZoneUp = currentGravityZoneUp;
    if (gravityBody != null)
    {
        currentGravityZoneUp = -gravityBody.GetEffectiveGravityDirection().normalized;
    }
    
    // Store relative camera position for first-person
    if (isInFirstPerson)
    {
        cameraRelativePosition = target.InverseTransformPoint(cameraTransform.position);
        cameraRelativeRotation = Quaternion.Inverse(target.rotation) * cameraTransform.rotation;
    }
    
    Debug.Log($"[Camera] Storing world forward: {_preTransitionWorldForward}, old up: {previousGravityZoneUp}, new up: {currentGravityZoneUp}");
    Debug.Log($"[Camera] Current yaw before transition: {_yawDegrees}");
    
    // Notify PlayerFlight
    if (playerFlight != null)
    {
        playerFlight.OnGravityTransitionStarted(previousGravityZoneUp, currentGravityZoneUp, 0f);
    }
}
    
    // Calculate signed angle between two vectors on a plane defined by normal
    private float SignedAngle(Vector3 from, Vector3 to, Vector3 normal)
    {
        float unsignedAngle = Vector3.Angle(from, to);
        float sign = Mathf.Sign(Vector3.Dot(normal, Vector3.Cross(from, to)));
        return unsignedAngle * sign;
    }
    
    // Method for PlayerDash to control panning state
    public void SetPanningActive(bool active, bool forceChange = false)
    {
        // Set external control flag
        isExternallyControlledPanning = active;
        
        if (active)
        {
            isPanning = true;
            initialCursorPosition = Mouse.current.position.ReadValue();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (isPanning || forceChange)
        {
            // Only reset if we were panning or force is requested
            isExternallyControlledPanning = false;
            isPanning = false;
            
            // Don't change cursor state if right mouse is still pressed
            if (!Mouse.current.rightButton.isPressed || forceChange)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                
                if (Mouse.current != null)
                {
                    Mouse.current.WarpCursorPosition(initialCursorPosition);
                }
            }
        }
    }
    
    // FIXED: Improved method to check if panning is active
    public bool IsPanningActive()
    {
        return isPanning;
    }

    public bool AreBothMouseButtonsDown()
{
    // Safe check in case Mouse.current is null (rare)
    return UnityEngine.InputSystem.Mouse.current != null &&
           UnityEngine.InputSystem.Mouse.current.leftButton.isPressed &&
           UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;
}
    
    // This helps get smooth camera data during dash
    public void GetCameraData(out Vector3 forward, out Vector3 right, out Vector3 up)
    {
        // Use gravity zone up instead of player up
        up = currentGravityZoneUp;
        
        // Get camera's actual forward and project it onto the plane perpendicular to the gravity zone up
        forward = Vector3.ProjectOnPlane(cameraTransform.forward, up).normalized;
        
        // Calculate right based on forward and up
        right = Vector3.Cross(up, forward).normalized;
    }
    
    // FIXED: New public method to get camera's raw forward vector
    public Vector3 GetCameraForward()
    {
        return cameraTransform.forward;
    }
    
    // FIXED: New public method to get camera's raw right vector
    public Vector3 GetCameraRight()
    {
        return cameraTransform.right;
    }
    
    // NEW: Method to get current gravity direction
    public Vector3 GetCurrentGravityUp()
    {
        return currentGravityZoneUp;
    }

    public Vector3 GetCameraUp()
    {
        return cameraTransform.up;
    }

    public float GetPitch()
{
    Vector3 gravityUp = currentGravityZoneUp;
    Vector3 camForward = cameraTransform.forward;

    // Project camera forward onto the gravity-relative horizontal plane
    Vector3 flatForward = Vector3.ProjectOnPlane(camForward, gravityUp).normalized;

    // Calculate signed angle: + up, - down
    float pitchAngle = Vector3.SignedAngle(flatForward, camForward, Vector3.Cross(flatForward, gravityUp));
    return pitchAngle;
}

public void SetGravityFrozenInSpace(bool isFrozen, Vector3 frozenUp)
{
    if (isFrozen && frozenUp != Vector3.zero)
    {
        currentGravityZoneUp = frozenUp.normalized;
        previousGravityZoneUp = currentGravityZoneUp;
        frozenSpaceUp = currentGravityZoneUp;
    }
}

public void StartRecenteringToRotation(Quaternion targetRotation, float speedDegPerSec = -1f)
{
    // Store the exact rotation we want to recenter to
    _preTransitionCameraRotation = targetRotation;

    if (speedDegPerSec > 0f)
        _recenterSpeed = speedDegPerSec;

    // If user is currently panning, let them finish; otherwise start recentering now
    if (!isPanning)
        _isRecentering = true;
}

    public void ReleaseExternalPanControl()
{
    // Store current camera orientation before changing state
    Vector3 currentCameraForward = cameraTransform.forward;
    Vector3 currentCameraUp = cameraTransform.up;

    // Stop external panning control
    isExternallyControlledPanning = false;

    // Declare once here
    bool isInSpace = gravityBody != null && gravityBody.IsInSpace;

    if (Mouse.current.rightButton.isPressed)
    {
        // User is still holding right mouse, so maintain panning state
        isPanning = true;
    }
    else
    {
        // Right mouse button isn't pressed, so end panning completely
        isPanning = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (isInSpace)
        {
            // Lock camera orientation to prevent drift
            finalRotation = Quaternion.LookRotation(currentCameraForward, currentCameraUp);
            cameraTransform.rotation = finalRotation;

            if (gravityBody != null)
            {
                gravityBody.SetSpaceGravityDirection(-currentCameraUp);
            }

            PlayerFlight playerFlight = target.GetComponent<PlayerFlight>();
            if (playerFlight != null)
            {
                playerFlight.OnCameraPanning(currentCameraForward);
            }
        }
    }
}
}