using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Collections;

public class PlayerCamera : MonoBehaviour
{
    [Header("Camera Components")]
    [SerializeField] private Transform target; // The player character
    [SerializeField] private Transform cameraTransform; // The actual camera

    [Header("Other References")]
    [SerializeField] private EquipmentManager equipmentManager;

    [Header("Zoom Settings")]
    [SerializeField] private float minZoomDistance = 0.1f;
    [SerializeField] private float maxZoomDistance = 50f;
    [SerializeField] private int zoomInTicks = 15;
    [SerializeField] private int zoomOutTicks = 20;
    [SerializeField] private float zoomSmoothing = 10f;

    [Header("Pan Settings")]
    [SerializeField] private float panSensitivity = 2f; // Mouse sensitivity

    [Header("Pitch Clamp (Degrees)")]
    [SerializeField] private float maxGravityRelativePitch = 60f;
    public float MinPitch => -maxGravityRelativePitch;
    public float MaxPitch => maxGravityRelativePitch;

    [Header("Collision Settings")]
    [SerializeField] private LayerMask collisionMask;
    [SerializeField] private float collisionBuffer = 0.2f;
    [SerializeField] private int collisionQuality = 10; // Number of raycasts for better detection

    [Header("Target Offset (Local Space)")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0, 1.5f, 0);

    [Header("Layer Settings")]
    [SerializeField] private Renderer[] characterRenderers;
    [SerializeField] private int localPlayerLayer = 8;
    [SerializeField] private int invisibleLayer = 9;

    [Header("First Person Settings")]
    [SerializeField] private float characterRotationSpeed = 10f; // Only used if smoothing is enabled
    [SerializeField] private bool rotateCharacterInFirstPerson = true; // Toggle for character rotation in first person
    [SerializeField] private bool pauseRotationDuringGravityTransition = true; // New setting

    [Header("Gravity Transition Settings")]
    [SerializeField] private bool preserveDirectionDuringTransition = true;
    [SerializeField] private float directionBlendTime = 0.5f;
    [SerializeField] private float transitionStabilizationTime = 0.2f; // New setting
    

    // Spooky Dash / Camera-phase integration
[Header("Spooky Dash / Phase Camera")]
[SerializeField] private string phaseableLayerName = "Phaseable";
private int _phaseableLayerIndex = -1;

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

    private bool isLeftPanning = false;
    private Coroutine _autoAlignRoutine;

    private const float SAME_GRAVITY_DOT = 0.99985f; // cos(1°) ≈ 0.99985

    // LMB snapshot so we can restore relative-to-player pose
    private bool _hasLmbSnapshot = false;
    private float _lmbSnapYawDeltaToPlayer = 0f;   // camYaw - playerYaw (on the gravity plane) at LMB-down
    private float _lmbSnapPitch = 0f;              // camera pitch (relative to up) at LMB-down
    private Quaternion _lmbSnapLocalCamRot;        // player-local camera rotation at LMB-down (used in space)

    private Vector3 _externalCamOffsetWS = Vector3.zero;

    private int  _collisionMaskBeforePhase;
private bool _phaseableIgnoreActive = false;

    private void Start()
    {
        // Get component references
        playerCamera = cameraTransform.GetComponent<Camera>();
        if (!playerCamera) Debug.LogError("No Camera component found on cameraTransform.");

        playerRigidbody = target.GetComponent<Rigidbody>();
        if (!playerRigidbody) Debug.LogError("No Rigidbody found on the target.");

        playerTransform = target;

        // Resolve Phaseable layer once
ResolvePhaseableLayerIndex();

        // Get the GravityBody component
        gravityBody = target.GetComponent<GravityBody>();

        // Get the PlayerDash component
        playerDash = target.GetComponent<PlayerDash>();

        // Get the PlayerFlight component - NEW
        playerFlight = target.GetComponent<PlayerFlight>();

        // Initialize zoom distances
        UpdateZoomDistanceFromPercentage();
        currentZoomDistance = targetZoomDistance; // Start with no lag

        // Initialize gravity zone up direction
        if (gravityBody != null && gravityBody.GetEffectiveGravityDirection() != Vector3.zero)
        {
            currentGravityZoneUp = -gravityBody.GetEffectiveGravityDirection().normalized;
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

        // Resolve EquipmentManager even if it’s not under the player
    if (equipmentManager == null)
        equipmentManager = FindObjectOfType<EquipmentManager>(true);
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

        // FIXED: Ensure isPanning is correctly set when right mouse is held down
        // This ensures other scripts can reliably check the panning state
        if (!isExternallyControlledPanning)
        {
            isPanning = Mouse.current.rightButton.isPressed;
        }
    }

    public void OnPanningRestored(Vector3 storedForward)
    {
        // If we're not already panning, this is a no-op
        if (!IsPanningActive()) return;

        // Get your camera component
        Camera cam = GetComponentInChildren<Camera>();
        if (cam == null) return;

        // Create a rotation that matches this direction
        Quaternion targetRotation = Quaternion.LookRotation(storedForward);

        // Apply this rotation to the camera
        cam.transform.rotation = targetRotation;
    }

    /// <summary>World-space offset added to the camera each frame (after zoom/orbit, before collision).
/// Set this every frame (or zero it) from effects like CameraBoostFX.</summary>
public void SetExternalCameraOffset(Vector3 worldOffset)
{
    _externalCamOffsetWS = worldOffset;
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
        float dot = Vector3.Dot(newUp, currentGravityZoneUp);
        if (dot < SAME_GRAVITY_DOT) // only treat as change if noticeably different
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

        bool rightMouseDown = Mouse.current.rightButton.isPressed;

        if (isInFirstPerson)
        {
            HandleFirstPersonLook(mouseDelta);
        }
        else
        {
            HandleThirdPersonLook(mouseDelta, rightMouseDown);
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

        bool isInSpace = (gravityBody != null && gravityBody.IsInSpace)
                 || (playerFlight != null && playerFlight.IsFlying);
        if (!isInSpace)
        {
            _pitchDegrees = Mathf.Clamp(_pitchDegrees, MinPitch, MaxPitch);
        }
    }

    private void HandleThirdPersonLook(Vector2 mouseDelta, bool rightMouseDown)
{
    bool isInSpace = gravityBody != null && gravityBody.IsInSpace;

    // Don’t LMB-pan when pointer is over UI
    bool pointerOverUI = UnityEngine.EventSystems.EventSystem.current != null
                         && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

    bool leftPhysicallyDown = UnityEngine.InputSystem.Mouse.current != null &&
                              UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;

    bool bothDown     = leftPhysicallyDown && rightMouseDown;
    bool leftMouseDown = leftPhysicallyDown && !rightMouseDown && !pointerOverUI;

    // ── If BOTH are down, LMB must forget any "return to" snapshot.
    //    We also prevent LMB from creating a new snapshot until RMB is released.
    if (bothDown && _hasLmbSnapshot)
    {
        _hasLmbSnapshot = false; // forget the old LMB reference
    }

    // --- RMB pan (existing behavior) ---
    if (rightMouseDown)
    {
        if (!isPanning && !isExternallyControlledPanning)
        {
            isPanning = true;
            initialCursorPosition = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            LockCursor(true);
        }

        if (isInSpace)
        {
            // Initialize space yaw/pitch from current camera at pan start
            if (isPanning && !wasPreviouslyPanningInSpace && (_spaceYawDegrees == 0f && _spacePitchDegrees == 0f))
            {
                Vector3 cameraForward = cameraTransform.forward;
                Vector3 up = currentGravityZoneUp;

                Vector3 flatForward = Vector3.ProjectOnPlane(cameraForward, up).normalized;
                if (flatForward.sqrMagnitude < 0.01f) flatForward = Vector3.forward;

                _spaceYawDegrees   = Quaternion.LookRotation(flatForward, up).eulerAngles.y;
                _spacePitchDegrees = -Vector3.SignedAngle(flatForward, cameraForward, Vector3.Cross(flatForward, up));
                Debug.Log($"[SpacePanFix] Re-initialized yaw/pitch at pan start: yaw={_spaceYawDegrees}, pitch={_spacePitchDegrees}");
            }

            _spaceYawDegrees   += mouseDelta.x * panSensitivity * Time.deltaTime;
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
        // RMB just ended
        EndThirdPersonPanning();
    }

    // --- LMB free-look pan (camera-only; does NOT steer player movement) ---
    if (leftMouseDown)
    {
        if (!isLeftPanning)
        {
            isLeftPanning = true;
            initialCursorPosition = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            LockCursor(true);

            // ===== Snapshot camera pose relative to the player at LMB-down =====
            Vector3 up = currentGravityZoneUp;

            // Player yaw on the gravity plane
            Vector3 playerFwdFlat = Vector3.ProjectOnPlane(target.forward, up).normalized;
            if (playerFwdFlat.sqrMagnitude < 0.0001f)
                playerFwdFlat = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;

            Vector3 baseForward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
            if (baseForward.sqrMagnitude < 0.0001f)
                baseForward = Vector3.ProjectOnPlane(Vector3.right, up).normalized;

            float playerYawAtStart = SignedAngle(baseForward, playerFwdFlat, up);
            _lmbSnapYawDeltaToPlayer = _yawDegrees - playerYawAtStart;                    // keep the cam’s yaw offset to player
            _lmbSnapPitch            = _pitchDegrees;                                     // keep pitch
            _lmbSnapLocalCamRot      = Quaternion.Inverse(target.rotation) * cameraTransform.rotation; // player-local cam rot (for space)
            _hasLmbSnapshot          = true;
            // ====================================================================
        }

        if (isInSpace)
        {
            _spaceYawDegrees   += mouseDelta.x * panSensitivity * Time.deltaTime;
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
    else if (isLeftPanning)
    {
        // LMB just ended
        EndLeftPanning();
    }

    wasPreviouslyPanningInSpace = (isPanning || isLeftPanning) && isInSpace;
}

    private void EndThirdPersonPanning()
{
    bool wasInSpace = gravityBody != null && gravityBody.IsInSpace;
    Vector3 finalCameraForward = cameraTransform.forward;
    Vector3 finalCameraUp      = cameraTransform.up;

    isPanning = false;

    // If LMB is still held at the moment RMB is released:
    //   - Immediately switch to LMB panning
    //   - Take a FRESH LMB snapshot now (this becomes the return target)
    if (UnityEngine.InputSystem.Mouse.current != null &&
        UnityEngine.InputSystem.Mouse.current.leftButton.isPressed)
    {
        if (!isLeftPanning)
        {
            isLeftPanning = true;
            // Keep cursor locked; do NOT warp
            LockCursor(true);

            // ===== Take fresh LMB snapshot now that RMB is up =====
            Vector3 up = currentGravityZoneUp;

            Vector3 playerFwdFlat = Vector3.ProjectOnPlane(target.forward, up).normalized;
            if (playerFwdFlat.sqrMagnitude < 0.0001f)
                playerFwdFlat = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;

            Vector3 baseForward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
            if (baseForward.sqrMagnitude < 0.0001f)
                baseForward = Vector3.ProjectOnPlane(Vector3.right, up).normalized;

            float playerYawAtStart = SignedAngle(baseForward, playerFwdFlat, up);
            _lmbSnapYawDeltaToPlayer = _yawDegrees - playerYawAtStart;
            _lmbSnapPitch            = _pitchDegrees;
            _lmbSnapLocalCamRot      = Quaternion.Inverse(target.rotation) * cameraTransform.rotation;
            _hasLmbSnapshot          = true;
            // ======================================================
        }

        return; // stay locked & continue with LMB pan
    }

    // Fully end panning (no LMB)
    LockCursor(false);

    if (wasInSpace)
    {
        // keep orientation stable in space
        finalRotation = Quaternion.LookRotation(finalCameraForward, finalCameraUp);
        cameraTransform.rotation = finalRotation;
    }
    else if (UnityEngine.InputSystem.Mouse.current != null)
    {
        UnityEngine.InputSystem.Mouse.current.WarpCursorPosition(initialCursorPosition);
    }

    // NEW: after releasing RMB, wait 2s of no panning, then auto-align behind player smoothly.
    StartDelayedAutoAlignBehindPlayer(2f, 0.35f);
}

public void StartDelayedAutoAlignBehindPlayer(float idleSeconds = 2f, float alignDuration = 0.35f)
{
    // Reuse the same slot used by other auto-align routines so we can cancel/replace cleanly
    if (_autoAlignRoutine != null)
        StopCoroutine(_autoAlignRoutine);

    _autoAlignRoutine = StartCoroutine(DelayedAutoAlignBehindPlayerRoutine(idleSeconds, alignDuration));
}

private IEnumerator DelayedAutoAlignBehindPlayerRoutine(float idleSeconds, float alignDuration)
{
    float wait = Mathf.Max(0f, idleSeconds);
    float t = 0f;

    // ── Wait phase: abort if panning resumes or an external controller grabs the camera
    while (t < wait)
    {
        bool rmb = UnityEngine.InputSystem.Mouse.current != null &&
                   UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;
        bool lmb = UnityEngine.InputSystem.Mouse.current != null &&
                   UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;

        if (isExternallyControlledPanning || rmb || lmb || isPanning || isLeftPanning)
        {
            _autoAlignRoutine = null;
            yield break;
        }

        t += Time.deltaTime;
        yield return null;
    }

    // ── Align phase (also aborts if panning resumes mid-blend)
    bool isInSpaceNow = gravityBody != null && gravityBody.IsInSpace;

    // If flying and we have an LMB snapshot, restore that yaw+pitch instead of "behind player"
    if (playerFlight != null && playerFlight.IsFlying && _hasLmbSnapshot)
    {
        float dur = Mathf.Max(0.01f, alignDuration);
        yield return StartCoroutine(AutoAlignBackToPanStartRoutine(dur, null));
        _autoAlignRoutine = null;
        yield break;
    }

    if (isInSpaceNow)
    {
        Vector3 up = currentGravityZoneUp;
        Vector3 playerFwdFlat = Vector3.ProjectOnPlane(target.forward, up).normalized;
        if (playerFwdFlat.sqrMagnitude < 0.0001f)
        {
            _autoAlignRoutine = null;
            yield break;
        }

        Quaternion startRot  = cameraTransform.rotation;
        Quaternion targetRot = Quaternion.LookRotation(playerFwdFlat, up);

        float dur = Mathf.Max(0.01f, alignDuration);
        float s = 0f;
        while (s < 1f)
        {
            bool rmb = UnityEngine.InputSystem.Mouse.current != null &&
                       UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;
            bool lmb = UnityEngine.InputSystem.Mouse.current != null &&
                       UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
            if (isExternallyControlledPanning || rmb || lmb || isPanning || isLeftPanning)
            {
                _autoAlignRoutine = null;
                yield break;
            }

            s += Time.deltaTime / dur;
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s));
            cameraTransform.rotation = Quaternion.Slerp(startRot, targetRot, eased);
            yield return null;
        }

        cameraTransform.rotation = targetRot;
    }
    else
    {
        Vector3 zoneUp = currentGravityZoneUp;
        Vector3 playerForward = Vector3.ProjectOnPlane(playerTransform.forward, zoneUp).normalized;
        if (playerForward.sqrMagnitude < 0.0001f)
        {
            _autoAlignRoutine = null;
            yield break;
        }

        Vector3 baseForward = Vector3.ProjectOnPlane(Vector3.forward, zoneUp).normalized;
        if (baseForward.sqrMagnitude < 0.0001f)
            baseForward = Vector3.ProjectOnPlane(Vector3.right, zoneUp).normalized;

        float startYaw  = _yawDegrees;
        float targetYaw = SignedAngle(baseForward, playerForward, zoneUp);

        float dur = Mathf.Max(0.01f, alignDuration);
        float s = 0f;
        while (s < 1f)
        {
            bool rmb = UnityEngine.InputSystem.Mouse.current != null &&
                       UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;
            bool lmb = UnityEngine.InputSystem.Mouse.current != null &&
                       UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
            if (isExternallyControlledPanning || rmb || lmb || isPanning || isLeftPanning)
            {
                _autoAlignRoutine = null;
                yield break;
            }

            s += Time.deltaTime / dur;
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s));
            _yawDegrees = Mathf.LerpAngle(startYaw, targetYaw, eased);
            yield return null;
        }

        _yawDegrees = targetYaw;
    }

    _autoAlignRoutine = null;
}

// Resolve and cache the Phaseable layer index
private void ResolvePhaseableLayerIndex()
{
    if (_phaseableLayerIndex >= 0) return;

    _phaseableLayerIndex = LayerMask.NameToLayer(phaseableLayerName);
    if (_phaseableLayerIndex < 0)
    {
        Debug.LogWarning($"[PlayerCamera] Layer '{phaseableLayerName}' not found. " +
                         "Create it in Project Settings > Tags & Layers or change 'phaseableLayerName'.");
    }
}

/// <summary>
/// Wrapper used by SpookyDash. Removes the Phaseable bit from camera collision mask while phasing,
/// restores it afterward. Uses your existing SetPhaseableCollisionIgnore under the hood.
/// </summary>
public void SetCollisionMaskPhaseIgnore(bool ignore)
{
    ResolvePhaseableLayerIndex();
    if (_phaseableLayerIndex < 0) return;

    // Reuse your existing API that already backs up/restores the mask
    SetPhaseableCollisionIgnore(ignore, _phaseableLayerIndex);
}

    private void UpdateCameraPosition()
{
    currentZoomDistance = Mathf.Lerp(currentZoomDistance, targetZoomDistance, Time.deltaTime * zoomSmoothing);

    Vector3 zoneUp = (gravityBody != null && gravityBody.IsInSpace)
        ? -gravityBody.GetEffectiveGravityDirection()
        : currentGravityZoneUp;

    bool isInSpace = gravityBody != null && gravityBody.IsInSpace;

    if (!isInSpace)
        _pitchDegrees = Mathf.Clamp(_pitchDegrees, MinPitch, MaxPitch);

    // Calculate final rotation based on current state
    if (isInSpace && (isPanning || isLeftPanning))
    {
        Quaternion yawRotation   = Quaternion.Euler(0f, _spaceYawDegrees, 0f);
        Quaternion pitchRotation = Quaternion.Euler(-_spacePitchDegrees, 0f, 0f);
        finalRotation = yawRotation * pitchRotation;
    }
    else if (isInSpace)
    {
        finalRotation = cameraTransform.rotation;
    }
    else
    {
        // Normal gravity - calculate rotation from yaw and pitch
        Vector3 baseForward = Vector3.ProjectOnPlane(Vector3.forward, zoneUp).normalized;
        if (baseForward.sqrMagnitude < 0.001f)
            baseForward = Vector3.ProjectOnPlane(Vector3.right, zoneUp).normalized;

        // Apply yaw rotation around the gravity up vector
        Quaternion yawRotation = Quaternion.AngleAxis(_yawDegrees, zoneUp);
        Vector3 yawForward = yawRotation * baseForward;

        // Apply pitch rotation around axis perpendicular to both yaw-forward and up
        Vector3 pitchAxis = Vector3.Cross(yawForward, zoneUp).normalized;
        Quaternion pitchRotation = Quaternion.AngleAxis(_pitchDegrees, pitchAxis);
        Vector3 finalForward = pitchRotation * yawForward;

        finalRotation = Quaternion.LookRotation(finalForward, zoneUp);
    }

    // Position camera based on target and zoom
    Vector3 localOffset  = target.TransformDirection(targetOffset);
    Vector3 pivotPos     = target.position + localOffset;
    Vector3 zoomDirection= finalRotation * Vector3.back;
    Vector3 desiredPos   = pivotPos + zoomDirection * currentZoomDistance;

    // >>> ADD: Apply external world-space offset from effects (lag, bob, etc.)
    desiredPos += _externalCamOffsetWS;

    // Calculate horizontal camera forward for movement
    Vector3 cameraForward = finalRotation * Vector3.forward;
    cameraForwardHorizontal = Vector3.ProjectOnPlane(cameraForward, zoneUp).normalized;

    // Handle collision with the *offset* included
    desiredPos = HandleCameraCollision(pivotPos, desiredPos);

    // Apply final position and rotation
    cameraTransform.position = desiredPos;
    cameraTransform.rotation = finalRotation;

    // Store for next frame
    _preTransitionCameraForward = cameraTransform.forward;

    // Notify flight system if needed
    if (playerFlight != null && playerFlight.IsFlying)
        playerFlight.OnCameraPanning(cameraTransform.forward);
}

    private void RotateCharacterWithCamera()
{
    // In third-person we leave your current behavior alone (body turns elsewhere).
    // In first-person we only rotate the *visuals* toward movement input so others see you turning.
    if (!isInFirstPerson || !rotateCharacterInFirstPerson)
        return;

    // Respect dash & gravity-transition guards (same as your Update() gate)
    bool inGravityTransition = gravityBody != null && gravityBody.IsTransitioningGravity;
    bool isDashing = playerDash != null && playerDash.IsDashing();
    if ((inGravityTransition && pauseRotationDuringGravityTransition) || isDashing)
        return;

    // Get movement intent from PlayerMovement (no hard dependency; just skip if missing)
    var pm = PlayerMovement.instance;
    if (pm == null || !pm.HasMovementInput())
        return;

    Vector3 worldMoveDir = pm.GetMoveDirection();
    if (worldMoveDir.sqrMagnitude < 0.0001f)
        return;

    // Visual-only yaw toward input on the current gravity plane
    Vector3 up = currentGravityZoneUp;
    YawVisualToward(worldMoveDir, up, characterRotationSpeed);
}

/// <summary>
/// Temporarily removes the given Phaseable layer bit from the camera's collisionMask while <paramref name="on"/> is true.
/// When turned off, restores the mask to its pre-phase value.
/// </summary>
/// <param name="on">Enable/disable the ignore.</param>
/// <param name="phaseableLayer">Layer index (0..31) for the Phaseable layer.</param>
public void SetPhaseableCollisionIgnore(bool on, int phaseableLayer)
{
    int bit = 1 << phaseableLayer;

    if (on)
    {
        if (!_phaseableIgnoreActive)
        {
            // backup once at activation
            _collisionMaskBeforePhase = collisionMask;
            _phaseableIgnoreActive = true;
        }

        // clear the Phaseable bit
        collisionMask &= ~bit;
    }
    else
    {
        if (_phaseableIgnoreActive)
        {
            // restore exactly what we had before phasing
            collisionMask = _collisionMaskBeforePhase;
            _phaseableIgnoreActive = false;
        }
        else
        {
            // Safety: ensure Phaseable is at least allowed again
            collisionMask |= bit;
        }
    }
}

/// <summary>
/// Yaws the visual model toward a world-space forward direction on the provided up plane,
/// without affecting the Rigidbody or camera yaw.
/// </summary>
private void YawVisualToward(Vector3 worldForward, Vector3 up, float turnSpeed)
{
    Transform visualRoot = ResolveVisualRoot();
    if (visualRoot == null) return;

    // Constrain to yaw by projecting onto the gravity (or zone) plane
    Vector3 targetFwd = Vector3.ProjectOnPlane(worldForward, up).normalized;
    if (targetFwd.sqrMagnitude < 0.0001f) return;

    Vector3 currentFwd = Vector3.ProjectOnPlane(visualRoot.forward, up).normalized;
    if (currentFwd.sqrMagnitude < 0.0001f)
        currentFwd = Vector3.ProjectOnPlane(target.forward, up).normalized;

    Quaternion targetYaw = Quaternion.LookRotation(targetFwd, up);
    visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetYaw, Time.deltaTime * Mathf.Max(1f, turnSpeed));
}

/// <summary>
/// Finds a stable "visual root" to rotate (Animator if present; otherwise first character renderer),
/// avoiding the Rigidbody/physics root so gameplay isn't affected.
/// </summary>
private Transform ResolveVisualRoot()
{
    // Prefer the Animator transform (usually the mesh root under the physics root)
    var anim = target != null ? target.GetComponentInChildren<Animator>() : null;
    if (anim != null && anim.transform != null)
        return anim.transform;

    // Fallback to first provided character renderer
    if (characterRenderers != null)
    {
        for (int i = 0; i < characterRenderers.Length; i++)
        {
            if (characterRenderers[i] != null && characterRenderers[i].transform != null)
                return characterRenderers[i].transform;
        }
    }

    // Last resort: do nothing rather than rotating the physics root
    return null;
}

    // PlayerCamera.cs — replace HandleCameraCollision with this filtered version
private Vector3 HandleCameraCollision(Vector3 pivotPos, Vector3 desiredPos)
{
    float distance = Vector3.Distance(pivotPos, desiredPos);
    if (distance < minZoomDistance + 0.2f)
        return desiredPos;

    Vector3 dir = (desiredPos - pivotPos).normalized;

    // 1) RaycastAll and ignore the player
    RaycastHit[] lineHits = Physics.RaycastAll(
        pivotPos, dir, distance, collisionMask, QueryTriggerInteraction.Ignore);

    bool found = false;
    float bestDist = distance;
    RaycastHit bestHit = default;

    foreach (var h in lineHits)
    {
        if (h.collider == null) continue;
        if (h.collider.transform != null && h.collider.transform.IsChildOf(target)) continue; // ignore player

        if (h.distance < bestDist)
        {
            bestDist = h.distance;
            bestHit = h;
            found = true;
        }
    }

    if (found)
        return bestHit.point + bestHit.normal * collisionBuffer;

    // 2) SphereCastAll and ignore the player (better edge handling)
    float radius = 0.2f;
    RaycastHit[] sphereHits = Physics.SphereCastAll(
        pivotPos, radius, dir, distance, collisionMask, QueryTriggerInteraction.Ignore);

    found = false;
    bestDist = distance;

    foreach (var h in sphereHits)
    {
        if (h.collider == null) continue;
        if (h.collider.transform != null && h.collider.transform.IsChildOf(target)) continue; // ignore player

        if (h.distance < bestDist)
        {
            bestDist = h.distance;
            bestHit = h;
            found = true;
        }
    }

    if (found)
        return bestHit.point + bestHit.normal * collisionBuffer;

    // 3) Extra edge rays (also ignore player)
    if (distance > 5f)
    {
        Vector3 right = Vector3.Cross(dir, currentGravityZoneUp).normalized;
        if (right.sqrMagnitude < 0.001f)
        {
            right = Vector3.Cross(dir, Vector3.right).normalized;
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.Cross(dir, Vector3.forward).normalized;
        }
        Vector3 up = Vector3.Cross(right, dir).normalized;

        int reducedQuality = Mathf.Min(collisionQuality, 6);
        for (int i = 0; i < reducedQuality; i++)
        {
            float angle = (i / (float)reducedQuality) * 2 * Mathf.PI;
            Vector3 offset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
            Vector3 rayStart = pivotPos + offset;

            if (Physics.Raycast(rayStart, dir, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && hit.collider.transform.IsChildOf(target)) continue; // ignore player
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

    // ───────── Pitch limit helpers (for PlayerFlight) ─────────
public bool HasPitchLimits()
{
    // In space we intentionally don't clamp pitch; elsewhere we do.
    bool isInSpace = gravityBody != null && gravityBody.IsInSpace;
    return !isInSpace;
}

public float GetPitchDegreesInternal()
{
    // This mirrors what we actually use to build final rotation
    return _pitchDegrees;
}

public void GetPitchLimits(out float minPitch, out float maxPitch)
{
    minPitch = MinPitch;
    maxPitch = MaxPitch;
}

// How much room (in degrees) we have until hitting the clamp.
// If in space (no clamp), we return +Infinity so callers can ignore.
public float GetPitchHeadroomUpDeg()
{
    if (!HasPitchLimits()) return float.PositiveInfinity;
    return Mathf.Max(0f, MaxPitch - _pitchDegrees);
}

public float GetPitchHeadroomDownDeg()
{
    if (!HasPitchLimits()) return float.PositiveInfinity;
    return Mathf.Max(0f, _pitchDegrees - MinPitch);
}

public bool IsAtPitchLimitUp(float epsilonDeg = 0.05f)
{
    return HasPitchLimits() && _pitchDegrees >= (MaxPitch - epsilonDeg);
}

public bool IsAtPitchLimitDown(float epsilonDeg = 0.05f)
{
    return HasPitchLimits() && _pitchDegrees <= (MinPitch + epsilonDeg);
}

    private void HandleCharacterLayer()
{
    bool shouldBeInFirstPerson = currentZoomDistance <= (minZoomDistance + 0.1f);

    if (shouldBeInFirstPerson != isInFirstPerson)
    {
        isInFirstPerson = shouldBeInFirstPerson;

        if (isInFirstPerson)
        {
            // Hide character body (existing behavior)
            SetCharacterLayer(invisibleLayer);
            playerCamera.cullingMask &= ~(1 << invisibleLayer);
            LockCursor(true);
        }
        else
        {
            // Show character body (existing behavior)
            SetCharacterLayer(localPlayerLayer);
            playerCamera.cullingMask |= (1 << invisibleLayer);
            LockCursor(false);
        }

if (equipmentManager != null)
{
    // Accessories are hidden in FP, visible in TP
    equipmentManager.SetAccessoriesVisible(!isInFirstPerson);
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
            Cursor.visible = !lockCursor;
        }
    }

    public void OnGravityTransitionCompleted()
{
    if (gravityBody != null)
        currentGravityZoneUp = -gravityBody.GetEffectiveGravityDirection().normalized;

    Vector3 oldGravityUp = previousGravityZoneUp;
    Vector3 newGravityUp = currentGravityZoneUp;

    float dot = Vector3.Dot(oldGravityUp.normalized, newGravityUp.normalized);

    // If essentially the same up, do absolutely nothing visual and don't stabilize.
    if (dot >= SAME_GRAVITY_DOT)
    {
        isChangingGravity = false;
        _stabilizingAfterTransition = false;

        if (playerFlight != null)
            playerFlight.OnGravityTransitionCompleted(oldGravityUp, newGravityUp, 0f);

        return;
    }

    // Existing logic for real changes:
    float gravityAlignment = dot;
    bool isSignificantGravityChange = gravityAlignment < 0.5f;

    if (isSignificantGravityChange)
    {
        // Your existing "behind the player" block...
        Vector3 playerForward = Vector3.ProjectOnPlane(playerTransform.forward, newGravityUp).normalized;

        Vector3 baseForward = Vector3.ProjectOnPlane(Vector3.forward, newGravityUp).normalized;
        if (baseForward.sqrMagnitude < 0.0001f)
            baseForward = Vector3.ProjectOnPlane(Vector3.right, newGravityUp).normalized;

        if (baseForward.sqrMagnitude > 0.0001f && playerForward.sqrMagnitude > 0.0001f)
        {
            Vector3 cameraDirection = playerForward;
            _yawDegrees = SignedAngle(baseForward, cameraDirection, newGravityUp);
        }

        _pitchDegrees = Mathf.Clamp(-10f, MinPitch, MaxPitch);
    }
    else
    {
        PreserveCameraOrbitalPosition(oldGravityUp, newGravityUp);
    }

    _stabilizingAfterTransition = true;
    _stabilizationTimer = 0f;
    isChangingGravity = false;

    if (playerFlight != null)
        playerFlight.OnGravityTransitionCompleted(oldGravityUp, newGravityUp, 0f);
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
    Vector3 newUp = currentGravityZoneUp;
    if (gravityBody != null)
        newUp = -gravityBody.GetEffectiveGravityDirection().normalized;

    float dot = Vector3.Dot(previousGravityZoneUp.normalized, newUp.normalized);

    // Trivial change? Don't pause input/camera at all.
    if (dot >= SAME_GRAVITY_DOT)
    {
        previousGravityZoneUp = currentGravityZoneUp = newUp;
        // Still tell flight we're "done" so it never locks.
        if (playerFlight != null)
            playerFlight.OnGravityTransitionCompleted(previousGravityZoneUp, currentGravityZoneUp, 0f);
        return;
    }

    // Real transition
    isChangingGravity = true;
    _preTransitionTime = Time.time;

    _preTransitionWorldForward   = cameraTransform.forward;
    _preTransitionWorldRight     = cameraTransform.right;
    _preTransitionCameraRotation = cameraTransform.rotation;
    _preTransitionYawDegrees     = _yawDegrees;

    previousGravityZoneUp = currentGravityZoneUp;
    currentGravityZoneUp  = newUp;

    if (isInFirstPerson)
    {
        cameraRelativePosition = target.InverseTransformPoint(cameraTransform.position);
        cameraRelativeRotation = Quaternion.Inverse(target.rotation) * cameraTransform.rotation;
    }

    if (playerFlight != null)
        playerFlight.OnGravityTransitionStarted(previousGravityZoneUp, currentGravityZoneUp, 0f);
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
    // External control flag
    isExternallyControlledPanning = active;

    if (active)
    {
        // Force-lock and hide
        isPanning = true;
        initialCursorPosition = Mouse.current.position.ReadValue();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        return;
    }

    // Deactivating external control:
    // Keep the cursor LOCKED & HIDDEN if either RMB is currently down OR LMB free-look is active.
    bool rmbDown = Mouse.current != null && Mouse.current.rightButton.isPressed;
    bool lmbPan  = isLeftPanning; // current LMB panning state tracked by this camera

    isExternallyControlledPanning = false;
    isPanning = rmbDown; // reflect RMB panning only

    if (rmbDown || lmbPan)
    {
        // Do NOT unlock/unhide; user is still panning with mouse
        return;
    }

    // Otherwise, actually release the cursor
    Cursor.lockState = CursorLockMode.None;
    Cursor.visible = true;

    // If you warp on release elsewhere, keep it there; we don't do it here.
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

public bool IsLeftPanningActive()
{
    bool pointerOverUI = UnityEngine.EventSystems.EventSystem.current != null
                         && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

    return isLeftPanning
        && !pointerOverUI
        && !(UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.rightButton.isPressed);
}

private void EndLeftPanning()
{
    bool wasInSpace = gravityBody != null && gravityBody.IsInSpace;
    Vector3 finalCameraForward = cameraTransform.forward;
    Vector3 finalCameraUp      = cameraTransform.up;

    isLeftPanning = false;

    // If RMB is still held, don't unlock or auto-align yet.
    // Also forget any LMB snapshot so it never overrides "behind player".
    bool rmbStillDown = UnityEngine.InputSystem.Mouse.current != null &&
                        UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;
    if (rmbStillDown)
    {
        _hasLmbSnapshot = false;
        return;
    }

    // Fully end panning (no RMB)
    LockCursor(false);

    if (wasInSpace)
    {
        // Keep orientation stable for this frame in space
        finalRotation = Quaternion.LookRotation(finalCameraForward, finalCameraUp);
        cameraTransform.rotation = finalRotation;
    }
    else if (UnityEngine.InputSystem.Mouse.current != null)
    {
        UnityEngine.InputSystem.Mouse.current.WarpCursorPosition(initialCursorPosition);
    }

    // IMPORTANT CHANGE:
    // After releasing LMB, always return smoothly to "behind the player"
    // (even when the player is idle and RMB was used to orbit without turning the player).
    // This uses your existing helpers and works in both gravity & space.
if (playerFlight != null && playerFlight.IsFlying && _hasLmbSnapshot)
    StartAutoAlignBackToPanStart(0.35f);
else
    StartAutoAlignBehindPlayer(0.35f);

    // We explicitly ignore any LMB snapshot target now.
    _hasLmbSnapshot = false;
}

public void StartAutoAlignBackToPanStart(float duration = 0.35f, System.Action onComplete = null)
{
    if (_autoAlignRoutine != null)
        StopCoroutine(_autoAlignRoutine);

    _autoAlignRoutine = StartCoroutine(AutoAlignBackToPanStartRoutine(duration, onComplete));
}

private IEnumerator AutoAlignBackToPanStartRoutine(float duration, System.Action onComplete)
{
    float dur = Mathf.Max(0.01f, duration);
    float t = 0f;

    bool isInSpace = gravityBody != null && gravityBody.IsInSpace;

    if (!_hasLmbSnapshot)
    {
        // Fallback: old behavior
        yield return StartCoroutine(isInSpace
            ? SpaceAutoAlignBehindRoutine(dur, null)
            : AutoAlignBehindRoutine(dur, null));
        _autoAlignRoutine = null;
        onComplete?.Invoke();
        yield break;
    }

    if (isInSpace)
    {
        // Restore the *player-relative* camera rotation we had when LMB started
        Quaternion startRot  = cameraTransform.rotation;
        Quaternion targetRot = target.rotation * _lmbSnapLocalCamRot;

        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            cameraTransform.rotation = Quaternion.Slerp(startRot, targetRot, eased);
            yield return null;
        }

        cameraTransform.rotation = targetRot;
    }
    else
    {
        // Recompute player's current yaw (on the current gravity plane)…
        Vector3 up = currentGravityZoneUp;
        Vector3 playerFwdFlat = Vector3.ProjectOnPlane(target.forward, up).normalized;
        if (playerFwdFlat.sqrMagnitude < 0.0001f)
            playerFwdFlat = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;

        Vector3 baseForward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
        if (baseForward.sqrMagnitude < 0.0001f)
            baseForward = Vector3.ProjectOnPlane(Vector3.right, up).normalized;

        float playerYawNow = SignedAngle(baseForward, playerFwdFlat, up);

        // …then aim back to the same yaw *offset from the player* we had at LMB-down,
        // and back to the same pitch we had then.
        float startYaw    = _yawDegrees;
        float startPitch  = _pitchDegrees;
        float targetYaw   = playerYawNow + _lmbSnapYawDeltaToPlayer;
        float targetPitch = Mathf.Clamp(_lmbSnapPitch, MinPitch, MaxPitch);

        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

            _yawDegrees   = Mathf.LerpAngle(startYaw,   targetYaw,   eased);
            _pitchDegrees = Mathf.Lerp(     startPitch, targetPitch, eased);

            yield return null;
        }

        _yawDegrees   = targetYaw;
        _pitchDegrees = targetPitch;
    }

    _autoAlignRoutine = null;
    onComplete?.Invoke();
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

    public void StartAutoAlignBehindPlayer(float duration = 0.35f, System.Action onComplete = null)
{
    if (_autoAlignRoutine != null)
        StopCoroutine(_autoAlignRoutine);

    bool isInSpace = gravityBody != null && gravityBody.IsInSpace;

    _autoAlignRoutine = isInSpace
        ? StartCoroutine(SpaceAutoAlignBehindRoutine(duration, onComplete))
        : StartCoroutine(AutoAlignBehindRoutine(duration, onComplete));
}

// NEW: works when isInSpace == true (the space path ignores _yawDegrees)
private IEnumerator SpaceAutoAlignBehindRoutine(float duration, System.Action onComplete)
{
    Vector3 up = currentGravityZoneUp;
    Vector3 playerFwdFlat = Vector3.ProjectOnPlane(target.forward, up).normalized;
    if (playerFwdFlat.sqrMagnitude < 0.0001f)
    {
        _autoAlignRoutine = null;
        onComplete?.Invoke();
        yield break;
    }

    // We want the camera looking from behind the player on the gravity plane,
    // keeping its current pitch component relative to `up`.
    // Build a target rotation that faces player's flat forward with the same up.
    Quaternion startRot  = cameraTransform.rotation;
    Quaternion targetRot = Quaternion.LookRotation(playerFwdFlat, up);

    float t = 0f;
    float dur = Mathf.Max(0.01f, duration);
    while (t < 1f)
    {
        t += Time.deltaTime / dur;
        float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        cameraTransform.rotation = Quaternion.Slerp(startRot, targetRot, eased);
        yield return null;
    }

    cameraTransform.rotation = targetRot;
    _autoAlignRoutine = null;
    onComplete?.Invoke();
}

private IEnumerator AutoAlignBehindRoutine(float duration, System.Action onComplete)
{
    // Compute target yaw so camera looks from behind the player on the current gravity plane
    Vector3 zoneUp = currentGravityZoneUp;
    Vector3 playerForward = Vector3.ProjectOnPlane(playerTransform.forward, zoneUp).normalized;

    if (playerForward.sqrMagnitude < 0.0001f)
    {
        onComplete?.Invoke();
        yield break;
    }

    Vector3 baseForward = Vector3.ProjectOnPlane(Vector3.forward, zoneUp).normalized;
    if (baseForward.sqrMagnitude < 0.0001f)
        baseForward = Vector3.ProjectOnPlane(Vector3.right, zoneUp).normalized;

    float startYaw  = _yawDegrees;
    float targetYaw = SignedAngle(baseForward, playerForward, zoneUp);

    float t = 0f;
    float dur = Mathf.Max(0.01f, duration);

    while (t < 1f)
    {
        t += Time.deltaTime / dur;
        float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        _yawDegrees = Mathf.LerpAngle(startYaw, targetYaw, eased);
        yield return null;
    }

    _yawDegrees = targetYaw;
    _autoAlignRoutine = null;
    onComplete?.Invoke();
}

    public void ReleaseExternalPanControl()
{
    // Store current camera orientation before changing state
    Vector3 currentCameraForward = cameraTransform.forward;
    Vector3 currentCameraUp = cameraTransform.up;

    // Stop external panning control
    isExternallyControlledPanning = false;

    bool isInSpace = gravityBody != null && gravityBody.IsInSpace;

    bool rmbDown = Mouse.current != null && Mouse.current.rightButton.isPressed;
    bool lmbPan  = isLeftPanning;

    // If user is still panning by either button, KEEP cursor locked & hidden
    if (rmbDown || lmbPan)
    {
        isPanning = rmbDown; // RMB panning flag; LMB pan is tracked separately
        return;
    }

    // Otherwise fully release
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