using UnityEngine;

/// <summary>
/// Post-processes a Rigidbody's velocity so it respects slope limits and feels sticky to the ground:
/// - Uses GravityBody.GravityUp to define "ground"
/// - Cancels slow sliding on gentle slopes
/// - Prevents walking up steep faces above MaxSlopeAngle
/// - Keeps the body glued to ground over small bumps
///
/// Add to the same GameObject as Rigidbody + GravityBody. Keep Rigidbody.UseGravity = OFF.
/// This script does not read input; it only adjusts the velocity produced by your mover.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public class SlopeGrounder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GravityBody gravityBody;
    [SerializeField] private Rigidbody rb;

    [Header("Ground Check")]
    [Tooltip("Layers considered 'ground'. Leave as default if not using special layers.")]
    [SerializeField] private LayerMask groundMask = ~0;
    [Tooltip("Radius for sphere ground probe.")]
    [SerializeField, Min(0.01f)] private float probeRadius = 0.25f;
    [Tooltip("Extra distance to search below the feet along gravity.")]
    [SerializeField, Min(0.02f)] private float probeDistance = 0.6f;
    [Tooltip("Offset above the feet to start probing from (prevents clipping the ground).")]
    [SerializeField, Min(0f)] private float probeStartOffset = 0.05f;

    [Header("Slope Rules")]
    [Tooltip("Maximum angle (deg) you can stand/walk on relative to gravity up.")]
    [SerializeField, Range(0f, 89.9f)] private float maxSlopeAngle = 46f;
    [Tooltip("If true, on non-walkable slopes your uphill velocity is removed so you can't climb.")]
    [SerializeField] private bool blockClimbOnSteep = true;

    [Header("Stick To Ground")]
    [Tooltip("Extra downward (along gravity) acceleration while grounded to stay planted.")]
    [SerializeField] private float stickAccel = 25f;
    [Tooltip("Always remove any tiny upward velocity when grounded to avoid micro-bounces.")]
    [SerializeField] private bool killUpwardWhenGrounded = true;

    [Header("Anti-Slide on Gentle Slopes")]
    [Tooltip("Damp velocity that points down-slope on walkable ground to stop slow sliding.")]
    [SerializeField] private bool preventGentleSlide = true;
    [Tooltip("How strongly we counter downhill drift (units/s² as an accel).")]
    [SerializeField] private float downhillBrakeAccel = 20f;
    [Tooltip("Ignore braking below this speed (m/s).")]
    [SerializeField] private float downhillDeadzone = 0.05f;

    // state
    public bool IsGrounded { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public float GroundAngleDeg { get; private set; }

    void Reset()
    {
        rb = GetComponent<Rigidbody>();
        gravityBody = GetComponent<GravityBody>();
    }

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!gravityBody) gravityBody = GetComponent<GravityBody>();
        rb.useGravity = false; // GravityBody drives gravity
    }

    void FixedUpdate()
    {
        if (!rb || !gravityBody) return;

        Vector3 up = gravityBody.GravityUp;                   // opposite of gravity
        Vector3 down = -up;

        // --- Ground probe (spherecast along gravity) ---
        Vector3 origin = rb.worldCenterOfMass + up * probeStartOffset;
        float castDist = probeDistance + probeRadius;

        IsGrounded = SphereProbe(origin, down, castDist, out RaycastHit hit);

        if (IsGrounded)
        {
            GroundNormal = hit.normal.normalized;
            GroundAngleDeg = Vector3.Angle(GroundNormal, up);
        }
        else
        {
            GroundNormal = up;
            GroundAngleDeg = 180f;
        }

        // --- Velocity shaping ---
        Vector3 v = rb.velocity;

        if (IsGrounded)
        {
            bool walkable = gravityBody.IsNormalWalkable(GroundNormal, maxSlopeAngle);

            // Remove tiny upward velocity when grounded (prevents pogo effect on bumps)
            if (killUpwardWhenGrounded)
            {
                float upVel = Vector3.Dot(v, up);
                if (upVel > 0f) v -= up * upVel;
            }

            if (walkable)
            {
                // Re-project velocity to the ground plane to keep motion hugging the surface
                v = Vector3.ProjectOnPlane(v, GroundNormal);

                // Apply a constant 'stick' accel pushing into the ground to hold contact
                v += down * (stickAccel * Time.fixedDeltaTime);

                // Cancel gentle downhill drift if present (and if no big input, this still works)
                if (preventGentleSlide && downhillBrakeAccel > 0f)
                {
                    // Downhill direction is the gravity projected onto the surface
                    Vector3 downhillDir = Vector3.ProjectOnPlane(down, GroundNormal).normalized;
                    if (downhillDir.sqrMagnitude > 0.0001f)
                    {
                        float downhillSpeed = Vector3.Dot(v, downhillDir);
                        if (downhillSpeed > downhillDeadzone)
                        {
                            float brake = downhillBrakeAccel * Time.fixedDeltaTime;
                            float newSpeed = Mathf.Max(0f, downhillSpeed - brake);
                            v += downhillDir * (newSpeed - downhillSpeed); // reduce only the downhill component
                        }
                    }
                }
            }
            else // too steep
            {
                // Treat as 'contact but not walkable'
                // Optionally stop uphill motion so you can't climb steep faces
                if (blockClimbOnSteep)
                {
                    Vector3 uphillDir = Vector3.ProjectOnPlane(up, GroundNormal).normalized;
                    float uphillSpeed = Vector3.Dot(v, uphillDir);
                    if (uphillSpeed > 0f)
                        v -= uphillDir * uphillSpeed; // remove uphill component entirely
                }

                // Lightly bias into the surface so we don't jitter off immediately
                v += down * (0.5f * stickAccel * Time.fixedDeltaTime);
            }
        }

        rb.velocity = v;
    }

    private bool SphereProbe(Vector3 origin, Vector3 dir, float distance, out RaycastHit hit)
    {
        // sphere cast for robustness over uneven ground
        bool hitAny = Physics.SphereCast(origin, probeRadius, dir, out hit, distance, groundMask, QueryTriggerInteraction.Ignore);

        // If SphereCast misses right at contact, do a small raycast as a fallback
        if (!hitAny)
        {
            hitAny = Physics.Raycast(origin, dir, out hit, distance, groundMask, QueryTriggerInteraction.Ignore);
        }

        // Reject backface hits (shouldn't happen with default colliders, but just in case)
        if (hitAny)
        {
            // Ensure the normal points generally away from gravity (i.e., 'up' relative to the surface)
            Vector3 up = gravityBody ? gravityBody.GravityUp : Vector3.up;
            if (Vector3.Dot(hit.normal, up) < 0f) return false;
        }

        return hitAny;
    }

    // --- visual debug ---
    void OnDrawGizmosSelected()
    {
        if (!gravityBody) return;
        Vector3 up = gravityBody.GravityUp;
        Vector3 down = -up;
        Vector3 origin = (rb ? rb.worldCenterOfMass : transform.position) + up * probeStartOffset;

        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(origin + down * probeRadius, probeRadius);
        Gizmos.DrawLine(origin, origin + down * (probeDistance + probeRadius));
        if (IsGrounded)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay((rb ? rb.worldCenterOfMass : transform.position), GroundNormal * 0.8f);
        }
    }
}