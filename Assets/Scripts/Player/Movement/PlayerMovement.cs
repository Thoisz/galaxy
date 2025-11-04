using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class PlayerMovement : MonoBehaviour
{
    public static PlayerMovement instance; // kept for other systems

    [Header("References")]
    [SerializeField] private Transform _groundCheck;
    [SerializeField] private Animator  _animator;
    [SerializeField] private Transform _cameraTransform;

    [Header("Movement")]
    [SerializeField] private LayerMask _groundMask;
    [SerializeField] private float _groundCheckRadius = 0.3f;
    [SerializeField] private float _moveSpeed = 8f;
    [SerializeField] private float _groundFrictionDamp = 10f;

    [Header("Slope (Binary)")]
    [Tooltip("Max angle (deg) that is walkable. Anything steeper will slide.")]
    [SerializeField, Range(0f, 89f)] private float _maxSlopeAngleDeg = 55f;

    [Tooltip("How far sideways from the ground-check we still consider 'under the feet'.")]
    [SerializeField] private float _underfootHorizontalTolerance = 0.22f;

    [Tooltip("Deg of hysteresis to stop flicker right at the threshold.")]
    [SerializeField] private float _slopeHysteresisDeg = 2f;

    [Tooltip("Acceleration applied along unwalkable slopes (m/s^2).")]
    [SerializeField] private float _slideAccel = 30f;

    [Tooltip("Maximum slide speed along the slope (m/s).")]
    [SerializeField] private float _slideMaxSpeed = 14f;

    [Header("Boost/Charge Integration")]
    [SerializeField] private float _postBoostMoveLockSeconds = 0.18f;

    [Header("Anti-Slide (Collider Friction)")]
    [SerializeField] private float _groundStaticFriction  = 1.5f;
    [SerializeField] private float _groundDynamicFriction = 0.8f;
    [SerializeField] private PhysicMaterialCombine _groundFrictionCombine = PhysicMaterialCombine.Maximum;

    [Header("External Hold (API compat)")]
    [SerializeField] private float _externalHorizHoldDefault = 0.35f;

    // -------- internals --------
    private Rigidbody _rb;
    private GravityBody _grav;
    private PlayerCamera _cam;
    private PlayerDash _dash;

    private Vector3 _moveInput;           // local (x,z)
    private Vector3 _worldMoveDir;        // camera-relative, horizontal to gravity
    private bool _hasMoveInput;

    private float _lastJumpTime = -10f;
    private bool _isGrounded;

    private float _baseMoveSpeed;
    private readonly List<float> _speedModifiers = new();

    // keep a "last valid" move dir for API consumers
    private Vector3 _lastValidMoveDir = Vector3.forward;

    // free-look lock (kept because other systems rely on it)
    private bool _freeLookActive, _waitingRealign, _wasLeftPan;
    private Vector3 _lockedForward, _lockedRight, _lockedUp, _lockedWorldMove;

    // materials
    private PhysicMaterial _matGround, _matAir;
    private Collider[] _selfCols;

    // ground probe result
    private struct GroundInfo
    {
        public bool hasHit;
        public bool walkable;
        public float slopeDeg;
        public RaycastHit hit;
    }
    private GroundInfo _gi;

    // External control (API compatibility with BoostJump, etc.)
    private bool   _externalStopMovement = false;
    private bool   _externalHoldActive = false;
    private float  _externalHoldUntil = 0f;
    private Vector3 _externalHeldHorizVel = Vector3.zero;

    void Awake()
    {
        if (instance && instance != this) { Destroy(gameObject); return; }
        instance = this;

        _rb   = GetComponent<Rigidbody>();
        _grav = GetComponent<GravityBody>();
        _cam  = FindObjectOfType<PlayerCamera>();
        _dash = GetComponent<PlayerDash>();

        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        _baseMoveSpeed = _moveSpeed;

        InitMaterials();

        if (!_cameraTransform && Camera.main) _cameraTransform = Camera.main.transform;
        if (!_groundCheck) Debug.LogWarning("PlayerMovement: GroundCheck not set.");
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        // Probe (for animator/state — physics is in FixedUpdate)
        _gi = ProbeGround();
        _isGrounded = _gi.walkable;

        // Input
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        bool bothMouseButtons = Input.GetMouseButton(0) && Input.GetMouseButton(1);
        if (bothMouseButtons) { h = 0f; v = 1f; _freeLookActive = false; _waitingRealign = false; }

        // If external stop is active, zero user intent
        if (_externalStopMovement) { h = 0f; v = 0f; }

        // free-look capture
        bool leftPan = _cam != null && _cam.IsLeftPanningActive();
        if (leftPan && !_wasLeftPan && !bothMouseButtons)
        {
            Vector3 up = -GetGravityDir();
            Vector3 fwd = Vector3.ProjectOnPlane(_cameraTransform.forward, up).normalized;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, fwd).normalized;
            _lockedUp = up; _lockedForward = fwd; _lockedRight = right;
            _freeLookActive = true;
        }

        // decide world move dir (no rotation will be applied later)
        if (_waitingRealign)
        {
            _worldMoveDir = _lockedWorldMove;
        }
        else if (_freeLookActive && leftPan)
        {
            CalculateMoveDirectionLocked(h, v);
            if (_worldMoveDir.sqrMagnitude > 0.001f) _lockedWorldMove = _worldMoveDir;
        }
        else
        {
            CalculateMoveDirection(h, v);
        }

        _hasMoveInput = _worldMoveDir.sqrMagnitude > 0.01f;

        if (_wasLeftPan && !leftPan)
        {
            if (_freeLookActive && _hasMoveInput && _cam != null)
            {
                _waitingRealign = true;
                _freeLookActive = false;
                if (_lockedWorldMove.sqrMagnitude < 0.001f)
                    _lockedWorldMove = (_worldMoveDir.sqrMagnitude > 0.001f) ? _worldMoveDir : _lockedForward;
                _cam.StartAutoAlignBehindPlayer(0.35f, () => _waitingRealign = false);
            }
            else _freeLookActive = false;
        }

        if (!_hasMoveInput || bothMouseButtons) { _freeLookActive = false; _waitingRealign = false; }
        _wasLeftPan = leftPan;

        UpdateAnimator();
    }

    void FixedUpdate()
    {
        // Physics probe
        _gi = ProbeGround();
        _isGrounded = _gi.walkable;

        // If on an unwalkable slope → slide (inputs won't change horizontal)
        if (_gi.hasHit && !_gi.walkable)
        {
            ApplySteepSlide(_gi);
        }
        else
        {
            ApplyMovementOrFriction();
        }

        UpdateColliderMaterial();
    }

    // ─────────────────────────────────────────────────────────────
    // Ground probe with "underfoot" filter + hysteresis (stops twitch)
    // ─────────────────────────────────────────────────────────────
    private GroundInfo ProbeGround()
    {
        GroundInfo gi = default;
        if (!_groundCheck) return gi;

        Vector3 gDir = GetGravityDir();
        Vector3 up = -gDir;

        // brief unground after jump so the takeoff frame doesn't instantly re-ground
        if (Time.time - _lastJumpTime < 0.20f) return gi;

        bool Underfoot(RaycastHit hit)
        {
            Vector3 toHit = hit.point - _groundCheck.position;
            float lateral = Vector3.ProjectOnPlane(toHit, gDir).magnitude;
            float alongG  = Vector3.Dot(toHit, gDir); // >0 means “below” along gravity
            return alongG > -0.01f && lateral <= Mathf.Max(_underfootHorizontalTolerance, _groundCheckRadius * 0.8f);
        }

        void Fill(RaycastHit hit)
        {
            gi.hasHit = true;
            gi.hit = hit;
            float cos = Vector3.Dot(hit.normal.normalized, up);
            gi.slopeDeg = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;

            // hysteresis (prevents flicker right at the limit)
            float limit = _maxSlopeAngleDeg + (_isGrounded ? _slopeHysteresisDeg : -_slopeHysteresisDeg);
            gi.walkable = gi.slopeDeg <= limit;
        }

        // Prefer capsule cast
        if (TryGetComponent(out CapsuleCollider capsule))
        {
            float radius = Mathf.Max(0.01f, capsule.radius * Mathf.Abs(transform.lossyScale.x));
            float height = Mathf.Max(radius * 2f + 0.01f, capsule.height * Mathf.Abs(transform.lossyScale.y));
            Vector3 cWS = transform.TransformPoint(capsule.center);
            Vector3 bottom = cWS - up * (height * 0.5f - radius);
            Vector3 top    = cWS + up * (height * 0.5f - radius);

            const float lift = 0.02f;
            Vector3 b0 = bottom + up * lift;
            Vector3 t0 = top    + up * lift;

            float dist = _groundCheckRadius + 0.15f;

            if (Physics.CapsuleCast(b0, t0, radius * 0.98f, -up, out RaycastHit hit, dist, _groundMask, QueryTriggerInteraction.Ignore)
                && Underfoot(hit))
            {
                Fill(hit);
                return gi;
            }
        }

        // Fallback ray
        float rayDistance = _groundCheckRadius + 0.15f;
        if (Physics.Raycast(_groundCheck.position, gDir, out RaycastHit rh, rayDistance, _groundMask, QueryTriggerInteraction.Ignore)
            && Underfoot(rh))
        {
            Fill(rh);
        }

        return gi;
    }

    // ─────────────────────────────────────────────────────────────
    // Sliding (input has NO effect on horizontal while sliding)
    // ─────────────────────────────────────────────────────────────
    private void ApplySteepSlide(GroundInfo gi)
    {
        Vector3 gDir = GetGravityDir();
        Vector3 downhill = Vector3.ProjectOnPlane(gDir, gi.hit.normal);
        if (downhill.sqrMagnitude < 0.0001f) downhill = gDir; // near-vertical fallback
        downhill.Normalize();

        // accelerate along the slope
        _rb.AddForce(downhill * _slideAccel, ForceMode.Acceleration);

        // cap slide speed along the slope
        float vAlong = Vector3.Dot(_rb.velocity, downhill);
        if (vAlong > _slideMaxSpeed)
        {
            _rb.velocity -= downhill * (vAlong - _slideMaxSpeed);
        }

        // NOTE: no rotation here (we keep yaw exactly as-is)
    }

    // ─────────────────────────────────────────────────────────────
    // Normal move/friction + external hold/stop compatibility
    // ─────────────────────────────────────────────────────────────
    private void ApplyMovementOrFriction()
    {
        // Dash can own velocity
        if (_dash && _dash.IsDashing()) return;

        // Hard stop from external systems (e.g., charging)
        if (_externalStopMovement)
        {
            // kill horizontal only (keep vertical for gravity/jumps)
            Vector3 gDir = GetGravityDir();
            Vector3 v = _rb.velocity;
            Vector3 vert = Vector3.Project(v, gDir);
            _rb.velocity = vert;
            return;
        }

        // External horizontal hold (preserve a provided horizontal velocity)
        if (_externalHoldActive)
        {
            if (Time.time >= _externalHoldUntil || _externalHeldHorizVel.sqrMagnitude <= 0.0001f)
            {
                _externalHoldActive = false;
                _externalHeldHorizVel = Vector3.zero;
            }
            else
            {
                Vector3 gDir = GetGravityDir();
                Vector3 v = _rb.velocity;
                Vector3 vert = Vector3.Project(v, gDir);
                _rb.velocity = vert + _externalHeldHorizVel;
                // NOTE: no rotation while holding either
                return;
            }
        }

        // If there’s no input, damp horizontal on ground
        if (_worldMoveDir.sqrMagnitude < 0.01f)
        {
            if (_isGrounded) HorizontalFriction(_groundFrictionDamp);
            return;
        }

        // brief window after jump/boost where we don't overwrite horizontal
        if (Time.time - _lastJumpTime < _postBoostMoveLockSeconds) return;

        float speed = GetCurrentMoveSpeed();
        Vector3 targetVel = _worldMoveDir * speed;
        Vector3 horiz = GetHorizontalVelocity();
        _rb.AddForce(targetVel - horiz, ForceMode.VelocityChange);

        // NOTE: no facing/rotation towards move direction
    }

    private void HorizontalFriction(float damp)
    {
        Vector3 gDir = GetGravityDir();
        Vector3 v = _rb.velocity;
        Vector3 vert = Vector3.Project(v, gDir);
        Vector3 horiz = v - vert;

        horiz = Vector3.Lerp(horiz, Vector3.zero, Time.fixedDeltaTime * Mathf.Max(1f, damp));
        _rb.velocity = vert + horiz;
    }

    // ─────────────────────────────────────────────────────────────
    // Input → world mapping (camera-relative on gravity plane)
    // ─────────────────────────────────────────────────────────────
    private void CalculateMoveDirection(float h, float v)
    {
        _moveInput = new Vector3(h, 0f, v).normalized;
        if (_cameraTransform && _moveInput.sqrMagnitude > 0.01f)
        {
            Vector3 up = -GetGravityDir();
            Vector3 fwd = Vector3.ProjectOnPlane(_cameraTransform.forward, up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(_cameraTransform.right,  up).normalized;
            _worldMoveDir = (fwd * _moveInput.z + right * _moveInput.x).normalized;

            if (_worldMoveDir.sqrMagnitude > 0.1f) _lastValidMoveDir = _worldMoveDir;
        }
        else _worldMoveDir = Vector3.zero;
    }

    private void CalculateMoveDirectionLocked(float h, float v)
    {
        _moveInput = new Vector3(h, 0f, v).normalized;
        if (_moveInput.sqrMagnitude > 0.01f)
        {
            _worldMoveDir = (_lockedForward * _moveInput.z + _lockedRight * _moveInput.x).normalized;
            if (_worldMoveDir.sqrMagnitude > 0.1f) _lastValidMoveDir = _worldMoveDir;
        }
        else _worldMoveDir = Vector3.zero;
    }

    // ─────────────────────────────────────────────────────────────
    // Animator + helpers
    // ─────────────────────────────────────────────────────────────
    private void UpdateAnimator()
    {
        if (!_animator) return;
        _animator.SetBool("isGrounded", _isGrounded);
        _animator.SetBool("isRunning", _hasMoveInput && (_gi.hasHit && _gi.walkable));
        float horizSpeed = GetHorizontalVelocity().magnitude;
        _animator.SetFloat("moveSpeed", horizSpeed / Mathf.Max(0.1f, GetCurrentMoveSpeed()));
    }

    public bool IsGrounded() => _isGrounded;

    public void NotifyJumped()
    {
        _lastJumpTime = Time.time;
        _isGrounded = false;
    }

    // === PUBLIC API (compat with your other scripts) ===

    public void CancelExternalHorizontalHold()
    {
        _externalHoldActive = false;
        _externalHeldHorizVel = Vector3.zero;
        _externalHoldUntil = 0f;
    }

    public void SetExternalStopMovement(bool on)
    {
        _externalStopMovement = on;
        if (on)
        {
            Vector3 gDir = GetGravityDir();
            Vector3 v = _rb.velocity;
            Vector3 vert = Vector3.Project(v, gDir);
            _rb.velocity = vert; // keep vertical, kill horizontal
        }
    }

    public bool HasMovementInput() => _hasMoveInput;

    public Vector3 GetMoveDirection()
    {
        if (_worldMoveDir.sqrMagnitude > 0.1f) return _worldMoveDir;
        if (_grav && _grav.IsTransitioningGravity) return _lastValidMoveDir;
        return _worldMoveDir;
    }

    public void HoldExternalHorizontal(Vector3 worldHorizontalVelocity, float seconds)
    {
        Vector3 gDir = GetGravityDir();
        _externalHeldHorizVel = Vector3.ProjectOnPlane(worldHorizontalVelocity, gDir);

        float dur = (seconds > 0f) ? seconds : _externalHorizHoldDefault;
        _externalHoldActive = _externalHeldHorizVel.sqrMagnitude > 0.0001f && dur > 0f;
        _externalHoldUntil  = Time.time + dur;
    }

    /// <summary>Nudge up slightly and clear downward velocity before a jump impulse.</summary>
    public void PreJumpSeparation(float lift = 0.03f)
    {
        Vector3 gDir = GetGravityDir();
        Vector3 up = -gDir;

        _rb.position += up * Mathf.Max(0f, lift);

        Vector3 v = _rb.velocity;
        float vAlongDown = Vector3.Dot(v, gDir);
        if (vAlongDown > 0f)
            _rb.velocity = v - gDir * vAlongDown;

        _isGrounded = false;
    }

    // Speed API
    public float GetCurrentMoveSpeed()
    {
        float total = _baseMoveSpeed;
        for (int i = 0; i < _speedModifiers.Count; i++) total += _speedModifiers[i];
        return Mathf.Max(0.1f, total);
    }
    public void AddSpeedModifier(float m)    => _speedModifiers.Add(m);
    public void RemoveSpeedModifier(float m) => _speedModifiers.Remove(m);

    private Vector3 GetGravityDir()
    {
        return _grav ? _grav.GetEffectiveGravityDirection().normalized : Vector3.down;
    }

    private Vector3 GetHorizontalVelocity()
    {
        Vector3 gDir = GetGravityDir();
        Vector3 v = _rb.velocity;
        return v - Vector3.Project(v, gDir);
    }

    // ─────────────────────────────────────────────────────────────
    // Materials (sticky on walkable, frictionless otherwise)
    // ─────────────────────────────────────────────────────────────
    private void InitMaterials()
    {
        _selfCols = GetComponents<Collider>();

        _matAir = new PhysicMaterial("PM_Air")
        {
            staticFriction = 0f, dynamicFriction = 0f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounceCombine = PhysicMaterialCombine.Minimum
        };
        _matGround = new PhysicMaterial("PM_Ground")
        {
            staticFriction = Mathf.Max(0f, _groundStaticFriction),
            dynamicFriction = Mathf.Max(0f, _groundDynamicFriction),
            frictionCombine = _groundFrictionCombine,
            bounceCombine = PhysicMaterialCombine.Minimum
        };

        ApplyMat(_matAir); // start airborne
    }

    private void ApplyMat(PhysicMaterial m)
    {
        if (_selfCols == null) return;
        for (int i = 0; i < _selfCols.Length; i++)
            if (_selfCols[i]) _selfCols[i].material = m;
    }

    private void UpdateColliderMaterial()
    {
        bool onSteep = _gi.hasHit && !_gi.walkable;
        PhysicMaterial target = (_isGrounded && !onSteep) ? _matGround : _matAir;
        if (_selfCols == null) return;
        for (int i = 0; i < _selfCols.Length; i++)
        {
            var c = _selfCols[i];
            if (c && c.material != target) c.material = target;
        }
    }

    // gizmos
    private void OnDrawGizmosSelected()
    {
        if (!_groundCheck) return;
        Gizmos.color = (_isGrounded ? Color.green : Color.red);
        Gizmos.DrawWireSphere(_groundCheck.position, _groundCheckRadius);
        Vector3 gDir = Application.isPlaying ? GetGravityDir() : Vector3.down;
        Gizmos.DrawRay(_groundCheck.position, gDir * (_groundCheckRadius + 0.15f));
    }
}