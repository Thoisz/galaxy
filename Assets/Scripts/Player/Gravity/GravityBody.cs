using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Character-style gravity driver that plays nice with a slope/grounding controller and your camera:
/// - Applies custom gravity in a chosen "down" direction
/// - Smoothly (or instantly) aligns the body's up-vector to oppose gravity
/// - Optional priority-based GravityArea sources (highest priority wins)
/// - Utility methods used by slope logic (project-on-gravity-plane, walkable checks, etc.)
/// - Exposes IsTransitioningGravity, IsInSpace, SetSpaceGravityDirection expected by PlayerCamera
///
/// Put this on the same GameObject as your Rigidbody (Use Gravity = OFF).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GravityBody : MonoBehaviour
{
    [Header("Gravity")]
    [Tooltip("Acceleration-like force (m/s²) applied each FixedUpdate in gravity direction.")]
    [SerializeField] private float gravityForce = 30f;

    [Tooltip("Fallback world-down if no GravityArea is active (used only when you *don't* want space mode).")]
    [SerializeField] private Vector3 defaultWorldDown = Vector3.down;

    [Header("Orientation")]
    [Tooltip("How quickly the body turns so its 'up' opposes gravity.")]
    [SerializeField] private float alignSpeed = 12f;

    [Tooltip("If true, instantly snap when gravity direction changes a lot (prevents wobble).")]
    [SerializeField] private bool snapOnBigChange = true;

    [Tooltip("Dot threshold for detecting a 'big change'. Lower = easier to snap. 0.98 ≈ ~11.5° change.")]
    [SerializeField, Range(0.90f, 0.9999f)] private float bigChangeDot = 0.98f;

    [Header("Manual Override (optional)")]
    [Tooltip("If enabled, ignore GravityAreas and force a manual gravity direction.")]
    [SerializeField] private bool useManualGravityDirection = false;

    [Tooltip("Manual gravity direction (world-space). Will be normalized at runtime.")]
    [SerializeField] private Vector3 manualGravityDirection = Vector3.down;

    // Public: current effective gravity direction (normalized, world space).
    public Vector3 GravityDirection { get; private set; } = Vector3.down;

    /// <summary>World-space up opposite to gravity (safe to use for slope checks, raycasts, etc.).</summary>
    public Vector3 GravityUp => -GravityDirection;

    /// <summary>Camera expects this: true while we're snapping/realigning due to area change.</summary>
    public bool IsTransitioningGravity { get; private set; } = false;

    /// <summary>True when no GravityArea is active (space mode). Camera uses this heavily.</summary>
    public bool IsInSpace => _gravityAreas.Count == 0;

    /// <summary>Returns current gravity down; if unset, falls back to last valid or space/default down.</summary>
    public Vector3 GetEffectiveGravityDirection()
    {
        if (GravityDirection.sqrMagnitude > 0.0001f) return GravityDirection;
        if (_lastGravityDir.sqrMagnitude > 0.0001f)  return _lastGravityDir.normalized;
        return (_spaceDown.sqrMagnitude > 0.0001f ? _spaceDown : (defaultWorldDown.sqrMagnitude > 0.0001f ? defaultWorldDown.normalized : Vector3.down));
    }

    /// <summary>Project a vector onto the plane perpendicular to gravity (i.e., the ground plane).</summary>
    public Vector3 ProjectOnGravityPlane(Vector3 v) => Vector3.ProjectOnPlane(v, GravityDirection);

    /// <summary>Returns true if a ground normal is <= maxSlopeAngle from GravityUp.</summary>
    public bool IsNormalWalkable(Vector3 groundNormal, float maxSlopeAngleDeg)
    {
        float cos = Vector3.Dot(groundNormal.normalized, GravityUp);
        float minCos = Mathf.Cos(maxSlopeAngleDeg * Mathf.Deg2Rad);
        return cos >= minCos;
    }

    // --- Internals ---
    private readonly List<GravityArea> _gravityAreas = new();
    private Rigidbody _rb;
    private Vector3 _lastGravityDir = Vector3.down;

    // Space support: when there are no areas, use this for "down" (camera may set it)
    private Vector3 _spaceDown = Vector3.down;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false; // we control gravity
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        // 1) Decide gravity direction
        Vector3 newDir = useManualGravityDirection
            ? (manualGravityDirection.sqrMagnitude > 0.0001f ? manualGravityDirection.normalized : Vector3.down)
            : GetCurrentGravityDirection();

        if (newDir == Vector3.zero)
            newDir = (_spaceDown.sqrMagnitude > 0.0001f ? _spaceDown : (defaultWorldDown.sqrMagnitude > 0.0001f ? defaultWorldDown.normalized : Vector3.down));

        newDir.Normalize();
        GravityDirection = newDir;

        // 2) Apply gravity like acceleration
        _rb.AddForce(newDir * gravityForce, ForceMode.Acceleration);

        // 3) Rotate so 'up' opposes gravity
        AlignToGravity(newDir);

        _lastGravityDir = newDir;
    }

    // Pick direction (highest-priority GravityArea wins; or space default if none)
    private Vector3 GetCurrentGravityDirection()
    {
        if (_gravityAreas.Count == 0)
        {
            // "Space": let camera (or other systems) steer down via SetSpaceGravityDirection
            return _spaceDown.sqrMagnitude > 0.0001f ? _spaceDown.normalized : Vector3.down;
        }

        _gravityAreas.Sort((a, b) => a.Priority.CompareTo(b.Priority)); // highest priority last
        return _gravityAreas[_gravityAreas.Count - 1].GetGravityDirection(this);
    }

    private void AlignToGravity(Vector3 gravityDir)
    {
        // Want: transform.up == -gravityDir
        Vector3 targetUp = -gravityDir;
        Quaternion targetRot = Quaternion.FromToRotation(transform.up, targetUp) * transform.rotation;

        // Snap if gravity changed a lot (prevents long reorientation on abrupt changes)
        float dot = Vector3.Dot(_lastGravityDir, gravityDir);
        bool bigChange = dot < bigChangeDot;

        if (snapOnBigChange && bigChange)
        {
            _rb.MoveRotation(targetRot);
        }
        else
        {
            Quaternion slerped = Quaternion.Slerp(_rb.rotation, targetRot, alignSpeed * Time.fixedDeltaTime);
            _rb.MoveRotation(slerped);
        }
    }

    /// <summary>
    /// Optional external nudge to align immediately (used by areas after priority changes).
    /// Also sets IsTransitioningGravity briefly so cameras/controls can pause, if desired.
    /// </summary>
    public void ForceAlignWithGravity(bool snap)
    {
        Vector3 dir = GetEffectiveGravityDirection().normalized;
        Vector3 targetUp = -dir;
        Quaternion targetRot = Quaternion.FromToRotation(transform.up, targetUp) * transform.rotation;

        if (snap) _rb.MoveRotation(targetRot);
        else      _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRot, alignSpeed * Time.fixedDeltaTime));

        MarkGravityTransition(0.20f);
    }

    /// <summary>
    /// Camera calls this in space to pin a "down" direction (e.g., opposite its up).
    /// </summary>
    public void SetSpaceGravityDirection(Vector3 worldDown)
    {
        if (worldDown.sqrMagnitude > 0.0001f)
            _spaceDown = worldDown.normalized;
    }

    /// <summary>
    /// Briefly raise IsTransitioningGravity so dependent systems can stabilize.
    /// </summary>
    public void MarkGravityTransition(float duration)
    {
        if (!isActiveAndEnabled) { IsTransitioningGravity = false; return; }
        StopAllCoroutines();
        StartCoroutine(CoTransitionFlag(duration));
    }

    private IEnumerator CoTransitionFlag(float duration)
    {
        IsTransitioningGravity = true;
        yield return new WaitForSeconds(Mathf.Max(0.01f, duration));
        IsTransitioningGravity = false;
    }

    /// <summary>Adds a gravity area as an active source.</summary>
    public void AddGravityArea(GravityArea area)
    {
        if (area != null && !_gravityAreas.Contains(area))
            _gravityAreas.Add(area);
    }

    /// <summary>Removes a gravity area from active sources.</summary>
    public void RemoveGravityArea(GravityArea area)
    {
        if (area != null)
            _gravityAreas.Remove(area);
    }

    private void OnValidate()
    {
        if (gravityForce < 0f) gravityForce = 0f;
        if (defaultWorldDown.sqrMagnitude < 0.0001f) defaultWorldDown = Vector3.down;
        if (useManualGravityDirection && manualGravityDirection.sqrMagnitude < 0.0001f)
            manualGravityDirection = Vector3.down;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position + Vector3.up * 0.25f;
        Vector3 down = Application.isPlaying ? GravityDirection :
                       (useManualGravityDirection ? manualGravityDirection.normalized :
                       (_spaceDown.sqrMagnitude > 0.0001f ? _spaceDown : defaultWorldDown.normalized));
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + down * 2f);
        Gizmos.DrawSphere(origin + down * 2f, 0.05f);

        Gizmos.color = Color.green;
        Vector3 up = -down;
        Gizmos.DrawLine(origin, origin + up * 2f);
        Gizmos.DrawSphere(origin + up * 2f, 0.05f);
    }
}