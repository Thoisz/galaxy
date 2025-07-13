using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GravityBody : MonoBehaviour
{
    [Header("Gravity Settings")]
    [Tooltip("Force of gravity applied to the object")]
    [SerializeField] private float gravityForce = 800f;

    [Header("Rotation Settings")]
    [Tooltip("Speed of rotation when gradually aligning with gravity")]
    [SerializeField] private float gradualAlignmentSpeed = 15f;

    [Tooltip("Speed of the transition between gravity areas (higher = faster)")]
    [Range(0.5f, 5f)]
    [SerializeField] private float gravityTransitionSpeed = 1f;

    [Tooltip("Use smooth transitions between gravity areas instead of instant rotation")]
    [SerializeField] private bool useGradualGravityTransitions = true;

    // First-person transition properties
    [Header("First-Person Settings")]
    [Tooltip("Should camera direction be preserved during gravity transitions in first-person?")]
    [SerializeField] private bool preserveFirstPersonView = true;

    // New settings for dash interactions
    [Header("Dash Interaction")]
    [Tooltip("Should gravity processing continue in the background when dash is active?")]
    [SerializeField] private bool processGravityDuringDash = true;

    // New settings for space behavior
    [Header("Space Behavior")]
    [Tooltip("Should the last gravity direction be maintained when leaving all gravity zones?")]
    [SerializeField] private bool maintainOrientationInSpace = true;

    [Tooltip("Gravity force in space (set to 0 for no gravity)")]
    [SerializeField] private float spaceGravityForce = 0f;

    // Public reference to the most recent gravity direction
    public Vector3 GravityDirection { get; private set; } = Vector3.zero;

    /// <summary>
/// Gets the effective gravity direction, falling back to last valid direction if in space
/// </summary>
public Vector3 GetEffectiveGravityDirection()
{
    return (_isInSpace && maintainOrientationInSpace)
        ? _lastValidGravityDirection
        : GravityDirection;
}

public Vector3 GetPlayerWorldForward()
{
    return transform.forward;
}

    // Added public properties to help with first-person transition
    public bool IsTransitioningGravity => _isTransitioningGravity;
    public float TransitionProgress => _transitionTimer / _transitionDuration;

    // New property to check if in space (outside all gravity areas)
    public bool IsInSpace => _isInSpace;

    private Rigidbody _rigidbody;
    private List<GravityArea> _gravityAreas;
    private List<GravityArea> _fixedDirectionGravityAreas; // New list for fixed direction areas
    private Vector3 _lastGravityDirection = Vector3.down; // Default to world down
    private bool _forceInstantRotation = false;
    private bool _isTransitioningGravity = false;
    private Quaternion _startRotation;
    private Quaternion _targetRotation;
    private float _transitionTimer = 0f;
    private float _transitionDuration = 0f;
    private Vector3 _transitionForwardDirection = Vector3.forward;

    // Reference to player camera for first-person checks
    private PlayerCamera _playerCamera;
    private bool _wasInFirstPerson = false;

    // Reference to player dash for checking state
    private PlayerDash _playerDash;
    private bool _wasDormantDueToDash = false;

    // Reference to PlayerFlight for notifying about transitions
    private PlayerFlight _playerFlight;

    // New variables for tracking space state
    private bool _isInSpace = false;
    private bool _wasInSpace = false;
    private Vector3 _lastValidGravityDirection = Vector3.down; // Direction to use when leaving all gravity zones

    // Event system for gravity transitions
    public delegate void GravityTransitionEvent(Vector3 oldDirection, Vector3 newDirection, float transitionDuration);
    public event GravityTransitionEvent OnGravityTransitionStarted;
    public event GravityTransitionEvent OnGravityTransitionCompleted;

    // New event for entering/exiting space
    public delegate void SpaceTransitionEvent(bool enteringSpace, Vector3 gravityDirection);
    public event SpaceTransitionEvent OnSpaceTransition;

    // Delayed gravity processing variables
    private bool _hasPendingGravityChange = false;
    private float _pendingGravityChangeTimer = 0f;
    private GravityArea _pendingGravityArea = null;
    private bool _isPendingGravityAdd = false; // true = add, false = remove
    private Vector3 _spaceUpVector = Vector3.up;
    private Vector3 _lastPlanetUpVector = Vector3.up;
    private Vector3 _upVector = Vector3.up;

    private void Start()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _gravityAreas = new List<GravityArea>();
        _fixedDirectionGravityAreas = new List<GravityArea>(); // Initialize the new list

        // Find player camera component
        _playerCamera = GetComponentInChildren<PlayerCamera>();
        if (_playerCamera == null)
        {
            _playerCamera = FindObjectOfType<PlayerCamera>();
        }

        // Find player dash component
        _playerDash = GetComponent<PlayerDash>();

        // Find player flight component
        _playerFlight = GetComponent<PlayerFlight>();

        // Initialize last valid gravity direction to world down if not otherwise set
        if (_lastValidGravityDirection == Vector3.zero)
        {
            _lastValidGravityDirection = Vector3.down;
        }
    }

    private void Update()
{
    // Remove gravity transition handling since we snap instantly
    // Only process pending gravity changes now
    if (_hasPendingGravityChange)
    {
        ProcessPendingGravityChange();
    }
}

    private void ProcessPendingGravityChange()
    {
        // Increment timer
        _pendingGravityChangeTimer += Time.deltaTime;

        // The actual delay is handled by GravityBox, this is just a safeguard
        // in case something goes wrong with the delayed application
        if (_pendingGravityChangeTimer >= 2.0f) // Safety timeout
        {
            // Apply the change
            if (_isPendingGravityAdd && _pendingGravityArea != null)
            {
                Debug.Log($"GravityBody safety timeout - applying delayed add for {_pendingGravityArea.name}");

                // Add the area and clear the pending flag
                if (!_gravityAreas.Contains(_pendingGravityArea))
                {
                    _gravityAreas.Add(_pendingGravityArea);
                    ForceAlignWithGravity(true);
                }
            }
            else if (!_isPendingGravityAdd && _pendingGravityArea != null)
            {
                Debug.Log($"GravityBody safety timeout - applying delayed remove for {_pendingGravityArea.name}");

                // Remove the area and clear the pending flag
                if (_gravityAreas.Contains(_pendingGravityArea))
                {
                    Vector3 previousGravity = GravityDirection;
                    _gravityAreas.Remove(_pendingGravityArea);

                    UpdateGravityDirection();

                    if (GravityDirection != Vector3.zero && Vector3.Dot(previousGravity, GravityDirection) < 0.9f)
                    {
                        StartGravityTransition();
                    }
                }
            }

            // Clear pending change state
            ClearPendingGravityChange();
        }
    }

    private void ClearPendingGravityChange()
    {
        _hasPendingGravityChange = false;
        _pendingGravityChangeTimer = 0f;
        _pendingGravityArea = null;
    }

    private void FixedUpdate()
{
    // Check if player is dashing
    bool isDashing = _playerDash != null && _playerDash.IsDashing();

    if (!isDashing || processGravityDuringDash)
    {
        // Process fixed direction gravity (if any)
        Vector3 fixedGravityDirection = UpdateFixedDirectionGravity();

        if (fixedGravityDirection != Vector3.zero)
        {
            GravityDirection = fixedGravityDirection;

            if (_isInSpace)
            {
                _isInSpace = false;
                OnSpaceTransition?.Invoke(false, GravityDirection);
            }

            _lastValidGravityDirection = GravityDirection;

            ApplyGravityForce(fixedGravityDirection);
        }
        else
        {
            UpdateGravityDirection();

            bool oldIsInSpace = _isInSpace;
            _isInSpace = (GravityDirection == Vector3.zero);

            if (_isInSpace && !oldIsInSpace)
            {
                Vector3 exitDirection = _lastValidGravityDirection.normalized;

                if (maintainOrientationInSpace)
                {
                    GravityDirection = exitDirection;

                    if (_playerCamera != null)
                    {
                        Vector3 cameraUp = -exitDirection;
                        _playerCamera.ForceOrientationUpdate(cameraUp);
                        _playerCamera.SetGravityFrozenInSpace(true, cameraUp);
                    }

                    OnSpaceTransition?.Invoke(true, exitDirection);

                    if (spaceGravityForce > 0)
                    {
                        ApplyGravityForce(exitDirection * (spaceGravityForce / gravityForce));
                    }

                    Debug.Log($"Entering space from planet. Exit direction: {exitDirection}, Camera up: {-exitDirection}");
                }
                else
                {
                    GravityDirection = Vector3.zero;
                    OnSpaceTransition?.Invoke(true, Vector3.zero);
                }
            }
            else if (!_isInSpace && oldIsInSpace)
            {
                _lastValidGravityDirection = GravityDirection;
                OnSpaceTransition?.Invoke(false, GravityDirection);
            }
            else if (!_isInSpace)
            {
                _lastValidGravityDirection = GravityDirection;
            }

            // Apply gravity if needed
            if (!isDashing && ((GravityDirection != Vector3.zero && !_isInSpace) || (_isInSpace && maintainOrientationInSpace && spaceGravityForce > 0)))
            {
                float forceMultiplier = _isInSpace ? (spaceGravityForce / gravityForce) : 1f;
                ApplyGravityForce(GravityDirection * forceMultiplier);

                // INSTANT SNAP: Check for gravity changes and apply immediately
                Vector3 currentDirection = GravityDirection.normalized;
                Vector3 previousDirection = _lastGravityDirection.normalized;

                bool gravityChanged = (Vector3.Dot(currentDirection, previousDirection) < 0.999f);

                if (gravityChanged || _forceInstantRotation)
                {
                    // ALWAYS snap instantly, ignore useGradualGravityTransitions setting
                    _rigidbody.rotation = CalculateTargetRotation();
                    OnGravityTransitionStarted?.Invoke(_lastGravityDirection, GravityDirection, 0f);
                    OnGravityTransitionCompleted?.Invoke(_lastGravityDirection, GravityDirection, 0f);

                    // Notify camera immediately
                    if (_playerCamera != null)
                    {
                        _playerCamera.OnGravityTransitionCompleted();
                    }

                    // Notify player flight immediately
                    if (_playerFlight != null)
                    {
                        _playerFlight.OnGravityTransitionStarted(_lastGravityDirection, GravityDirection, 0f);
                        _playerFlight.OnGravityTransitionCompleted(_lastGravityDirection, GravityDirection, 0f);
                    }

                    _forceInstantRotation = false;
                }
                else
                {
                    AlignWithGravity(false);
                }
            }
            else if (isDashing)
            {
                Vector3 currentDirection = GravityDirection.normalized;
                Vector3 previousDirection = _lastGravityDirection.normalized;

                bool gravityChanged = (Vector3.Dot(currentDirection, previousDirection) < 0.999f);

                if (gravityChanged)
                {
                    _lastGravityDirection = GravityDirection;
                    _forceInstantRotation = true;
                }
            }
        }

        // Update this last so comparison above is accurate
        _lastGravityDirection = GravityDirection;
    }
}

    private void ApplyGravityForce(Vector3 direction)
    {
        _rigidbody.AddForce(direction * (gravityForce * Time.fixedDeltaTime), 
                           ForceMode.Acceleration);
    }

    // Handle fixed direction gravity areas (no rotation, just force)
    private Vector3 UpdateFixedDirectionGravity()
    {
        // If no fixed direction areas, return zero
        if (_fixedDirectionGravityAreas == null || _fixedDirectionGravityAreas.Count == 0)
        {
            return Vector3.zero;
        }

        // Sort by priority, highest wins
        _fixedDirectionGravityAreas.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Use direction of the highest-priority area
        return _fixedDirectionGravityAreas[_fixedDirectionGravityAreas.Count - 1].GetGravityDirection(this).normalized;
    }

    private void UpdateGravityDirection()
    {
        // If no gravity areas, direction is zero - we'll be in space
        if (_gravityAreas == null || _gravityAreas.Count == 0)
        {
            GravityDirection = Vector3.zero;
            return;
        }

        // Sort by priority, highest wins
        _gravityAreas.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Use direction of the highest-priority area
        GravityDirection = _gravityAreas[_gravityAreas.Count - 1].GetGravityDirection(this).normalized;
    }

    private void StartGravityTransition()
{
    if (_playerDash != null && _playerDash.IsDashing() && !processGravityDuringDash)
    {
        return;
    }

    Vector3 oldGravityDirection = _lastGravityDirection;
    
    // CRITICAL: Store the player's current world forward direction BEFORE changing orientation
    Vector3 playerWorldForward = transform.forward;

    // INSTANT SNAP: Apply rotation immediately, but preserve world direction
    Vector3 newUp = -GravityDirection.normalized;
    
    // Project player's current world forward onto the new horizontal plane
    Vector3 preservedHorizontalForward = Vector3.ProjectOnPlane(playerWorldForward, newUp).normalized;
    
    if (preservedHorizontalForward.sqrMagnitude > 0.0001f)
    {
        // Create rotation that faces the same world direction with new up vector
        Quaternion targetRotation = Quaternion.LookRotation(preservedHorizontalForward, newUp);
        _rigidbody.rotation = targetRotation;
        
        Debug.Log($"[GravityBody] Preserved world direction: {playerWorldForward} -> {preservedHorizontalForward}");
    }
    else
    {
        // Fallback to normal calculation if projection fails
        _rigidbody.rotation = CalculateTargetRotation();
    }
    
    // Notify events immediately since there's no transition
    OnGravityTransitionStarted?.Invoke(oldGravityDirection, GravityDirection, 0f);
    OnGravityTransitionCompleted?.Invoke(oldGravityDirection, GravityDirection, 0f);

    // Notify camera immediately
    if (_playerCamera != null)
    {
        _playerCamera.OnGravityTransitionCompleted();
    }

    // Notify player flight immediately
    if (_playerFlight != null)
    {
        _playerFlight.OnGravityTransitionStarted(oldGravityDirection, GravityDirection, 0f);
        _playerFlight.OnGravityTransitionCompleted(oldGravityDirection, GravityDirection, 0f);
    }
}

    private void AlignWithGravity(bool instantly)
    {
        // Calculate target rotation (up vector opposite to gravity)
        Quaternion targetRotation = CalculateTargetRotation();

        if (instantly)
        {
            // Apply rotation instantly
            _rigidbody.rotation = targetRotation;
        }
        else
        {
            // Apply rotation smoothly
            _rigidbody.rotation = Quaternion.Slerp(
                _rigidbody.rotation,
                targetRotation,
                Time.fixedDeltaTime * gradualAlignmentSpeed
            );
        }
    }

    private Quaternion CalculateTargetRotation()
    {
        // Calculate how to rotate from current up to the new up (opposite of gravity)
        Quaternion fromToRotation = Quaternion.FromToRotation(transform.up, -GravityDirection);

        // Combine with current rotation to get target rotation
        return fromToRotation * transform.rotation;
    }

    public void ForceAlignWithGravity(bool useTransition = false)
{
    Vector3 comparisonDirection;

    if (_isInSpace && maintainOrientationInSpace)
    {
        // We're in space and maintaining orientation — compare with last valid direction
        comparisonDirection = _lastValidGravityDirection;
    }
    else
    {
        comparisonDirection = GravityDirection;
    }

    // Avoid transition if gravity direction didn't change significantly
    if (Vector3.Dot(_lastGravityDirection.normalized, comparisonDirection.normalized) > 0.999f)
    {
        Debug.Log("Skipping rotation alignment: entering space or gravity unchanged");
        return;
    }

    if (useTransition && useGradualGravityTransitions)
    {
        StartGravityTransition();
    }
    else
    {
        _forceInstantRotation = true;
    }
}

public void PreservePlayerWorldDirection(Vector3 worldForward)
{
    // Calculate what rotation is needed to face the desired world direction
    // while maintaining the new gravity up direction
    Vector3 newUp = -GravityDirection.normalized;
    
    // Project the desired world forward onto the horizontal plane
    Vector3 horizontalForward = Vector3.ProjectOnPlane(worldForward, newUp).normalized;
    
    if (horizontalForward.sqrMagnitude > 0.0001f)
    {
        // Create rotation that faces the desired direction with correct up vector
        Quaternion targetRotation = Quaternion.LookRotation(horizontalForward, newUp);
        _rigidbody.rotation = targetRotation;
        
        Debug.Log($"[GravityBody] Preserved player world direction: {worldForward} -> horizontal: {horizontalForward}");
    }
}

    public void AddGravityArea(GravityArea gravityArea)
{
    Debug.Log($"Adding gravity area: {gravityArea.name}, count before: {_gravityAreas.Count}");

    if (!_gravityAreas.Contains(gravityArea))
    {
        // Save old gravity direction before updating
        Vector3 oldDirection = GravityDirection;

        _gravityAreas.Add(gravityArea);
        Debug.Log($"Gravity area added, new count: {_gravityAreas.Count}");

        // Update the gravity direction based on new highest-priority area
        UpdateGravityDirection();

        Vector3 newDirection = GravityDirection;

        // Only align if gravity direction has significantly changed
        bool gravityChanged = (Vector3.Dot(oldDirection.normalized, newDirection.normalized) < 0.999f);

        if (gravityChanged)
        {
            ForceAlignWithGravity(true);
        }
        else
        {
            Debug.Log("Same gravity direction - skipping transition");
        }
    }

    // Set pending change anyway for safety in case of edge cases
    _hasPendingGravityChange = true;
    _pendingGravityArea = gravityArea;
    _pendingGravityChangeTimer = 0f;
    _isPendingGravityAdd = true;
}

    public void RemoveGravityArea(GravityArea gravityArea)
    {
        // Store this as the pending gravity area change
        _hasPendingGravityChange = true;
        _pendingGravityArea = gravityArea;
        _pendingGravityChangeTimer = 0f;
        _isPendingGravityAdd = false;

        // The actual remove operation is performed by the GravityBox with delay,
        // but we'll keep this logic as a backup/safety mechanism

        if (_gravityAreas.Contains(gravityArea))
        {
            // Store previous gravity direction before removing the area
            Vector3 previousGravity = GravityDirection;

            _gravityAreas.Remove(gravityArea);

            // Force recalculation
            UpdateGravityDirection();

            // If gravity direction changed significantly after removing this area
            if (GravityDirection != Vector3.zero && Vector3.Dot(previousGravity, GravityDirection) < 0.9f)
            {
                // This indicates we need a significant transition, so trigger it
                StartGravityTransition();
            }
        }
    }

    // New methods for fixed direction gravity areas (no rotation)
    public void AddFixedDirectionGravityArea(GravityArea gravityArea)
    {
        if (!_fixedDirectionGravityAreas.Contains(gravityArea))
        {
            _fixedDirectionGravityAreas.Add(gravityArea);
            // No rotation change needed for fixed direction areas
        }
    }

    public void RemoveFixedDirectionGravityArea(GravityArea gravityArea)
    {
        if (_fixedDirectionGravityAreas.Contains(gravityArea))
        {
            _fixedDirectionGravityAreas.Remove(gravityArea);
            // No rotation change needed when exiting a fixed direction area
        }
    }

    // Method to clear any pending gravity changes (can be called externally)
    public void CancelPendingGravityChanges()
    {
        ClearPendingGravityChange();
    }

    // Method to manually set the space gravity direction (for use with PlayerFlight)
    public void SetSpaceGravityDirection(Vector3 direction)
    {
        if (direction.magnitude > 0)
        {
            _lastValidGravityDirection = direction.normalized;

            // If we're in space, update active gravity direction too
            if (_isInSpace && maintainOrientationInSpace)
            {
                GravityDirection = _lastValidGravityDirection;
            }
        }
    }

    // Add this method to GravityBody.cs
public void OnSpaceTransitionEvent(bool enteringSpace, Vector3 gravityDirection)
{
    bool wasInSpace = _isInSpace;
    _isInSpace = enteringSpace;

    if (enteringSpace && !wasInSpace)
    {
        // Store the last planet up vector when entering space
        _lastPlanetUpVector = -gravityDirection;
        _spaceUpVector = _lastPlanetUpVector;

        // Update camera immediately to prevent flip
        if (_playerCamera != null)
        {
            _playerCamera.ForceOrientationUpdate(_spaceUpVector);
        }

        Debug.Log("Entered space! Maintaining orientation: " + _spaceUpVector);
    }
    else if (!enteringSpace && wasInSpace)
    {
        // Update the up vector when entering a planet
        _upVector = -gravityDirection;

        Debug.Log("Exited space! New gravity direction: " + gravityDirection);
    }
}
}