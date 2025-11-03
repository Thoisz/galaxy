using System.Collections;
using UnityEngine;

public class PlayerJump : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private Animator _animator;
    [SerializeField] private PlayerDash _playerDash; // Add reference to PlayerDash

    [Header("First Jump Settings")]
    [SerializeField] private float _firstJumpForce = 8f;
    [SerializeField] private float _firstJumpCooldown = 0.1f;

    [Header("Double Jump Settings")]
    [SerializeField] private float _doubleJumpForce = 6f;
    [SerializeField] private float _doubleJumpCooldown = 0.1f;
    [SerializeField] private GameObject _doubleJumpVFXPrefab;

    [Header("Air Jump Settings")]
    [SerializeField] private int _maxAirJumps = 10;
    [SerializeField] private float _airJumpForce = 5f;
    [SerializeField] private float _airJumpCooldown = 0.1f;
    [SerializeField] private GameObject _airJumpVFXPrefab;
    [SerializeField] private Color _airJumpVFXColor = Color.cyan;
    [SerializeField] private float _airJumpVFXScale = 1f;

    [Header("Jump Sound")]
    [SerializeField] private AudioClip _firstJumpSound;
    [SerializeField] private AudioClip _doubleJumpSound;
    [SerializeField] private AudioClip _airJumpSound;
    [SerializeField] private float _jumpSoundVolume = 0.5f;

    // ── External suppression gate (set by JetpackBoostJump)
    [SerializeField] private bool _jumpSuppressed = false;
    public  void SetJumpSuppressed(bool value) => _jumpSuppressed = value;
    public  bool IsJumpSuppressed => _jumpSuppressed;

    [SerializeField] private PlayerCrouch _playerCrouch;     // to read IsCrouching
    private BoostJump _boostJump;

    // Private fields
    private Rigidbody _rigidbody;
    private GravityBody _gravityBody;
    private AudioSource _audioSource;
    private PlayerStamina _playerStamina;
    
    private int _currentJumpCount = 0;
    private bool _canJump = true;
    private float _lastJumpTime = -10f;

    // Jump state tracking
    private bool _hasDoubleJumped = false;
    private bool _wasGroundedLastFrame = false;
    private bool _hasResetJumpsAfterGrounded = false;
    private bool _jumpedFromGround = false;
    private int _remainingAirJumps = 0;
    private bool _jumpTriggerActive = false;

    // Fall tracking
    private float _fallStartTime = 0f;
    private float _currentFallTime = 0f;
    private bool _isFalling = false;
    private bool _wasActuallyFallingLastFrame = false;
    private float _lastFallTime = 0f; // Stores the fall time for animator after landing
    
    // Air jump animation tracking
    private int _airJumpSide = 0; // 0 = left, 1 = right

    private void Start()
{
    // Get components
    _rigidbody = GetComponent<Rigidbody>();
    _gravityBody = GetComponent<GravityBody>();

    if (_playerCrouch == null) _playerCrouch = GetComponent<PlayerCrouch>();
    if (_boostJump == null)    _boostJump    = GetComponentInChildren<BoostJump>(true);
    
    // Find player movement script if not assigned
    if (_playerMovement == null)
        _playerMovement = GetComponent<PlayerMovement>();
        
    // Check for animator if not assigned
    if (_animator == null)
        _animator = GetComponent<Animator>();
        
    // Find player dash script if not assigned
    if (_playerDash == null)
        _playerDash = GetComponent<PlayerDash>();
        
    _playerStamina = GetComponent<PlayerStamina>();
        
    // Create audio source if sounds are assigned
    if (_firstJumpSound != null || _doubleJumpSound != null || _airJumpSound != null)
    {
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 1.0f;
        _audioSource.volume = _jumpSoundVolume;
    }
    
    // Initialize remaining air jumps
    _remainingAirJumps = _maxAirJumps;
}

    private void Update()
{
    // Ensure booster ref if equipped/unequipped at runtime
    if (_boostJump == null)
        _boostJump = GetComponentInChildren<BoostJump>(true);

    bool isGrounded = _playerMovement.IsGrounded();

    // === HARD GATE (jetpack + Shift on ground swallows Space) ===
    bool jetpackEquipped =
        _boostJump != null &&
        _boostJump.isActiveAndEnabled &&
        _boostJump.gameObject.activeInHierarchy;

    bool shiftHeld   = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    bool blockJumpNow = jetpackEquipped && isGrounded && shiftHeld;

    // Fall/anim bookkeeping (updates _currentFallTime, _lastFallTime, etc.)
    UpdateFallTracking(isGrounded);

    // Landing bookkeeping
    if (isGrounded && !_wasGroundedLastFrame)
    {
        // _lastFallTime has already been captured by UpdateFallTracking()
        ResetJumps();
        _hasResetJumpsAfterGrounded = true;
    }

    if (!isGrounded)
    {
        _hasResetJumpsAfterGrounded = false;

        // Walked off a ledge → skip first/double jump & move to air-jumps
        if (_wasGroundedLastFrame && _currentJumpCount == 0)
        {
            _jumpedFromGround  = false;
            _currentJumpCount  = 2;
            _hasDoubleJumped   = true;
            _remainingAirJumps = _maxAirJumps;
        }
    }

    // If jump is blocked this frame, bail before reading Space
    if (blockJumpNow)
    {
        // Animator: IMMEDIATE zero on ground, otherwise count while truly falling
        if (_animator != null)
        {
            if (isGrounded)                    _animator.SetFloat("fallTime", 0f);
            else if (_isFalling)               _animator.SetFloat("fallTime", _currentFallTime);
            else                               _animator.SetFloat("fallTime", 0f);
        }

        _wasGroundedLastFrame = isGrounded;
        return;
    }

    // Normal jump input (also allowed during dash), plus optional external suppression
    if (Input.GetKeyDown(KeyCode.Space) && _canJump && !_jumpSuppressed)
    {
        bool isDashing = _playerDash != null && _playerDash.IsDashing();
        if (isDashing) TryJumpFromDash();
        else           TryJump();
    }

    // Animator: IMMEDIATE zero on ground, otherwise count while truly falling
    if (_animator != null)
    {
        if (isGrounded)                        _animator.SetFloat("fallTime", 0f);
        else if (_isFalling)                   _animator.SetFloat("fallTime", _currentFallTime);
        else                                   _animator.SetFloat("fallTime", 0f);
    }

    _wasGroundedLastFrame = isGrounded;
}

private void OnDisable()
{
    _jumpSuppressed = false;
}

    private void TryJumpFromDash()
{
    // Same safety gate as elsewhere (jetpack + Shift on ground)
    bool jetpackEquipped =
        _boostJump != null &&
        _boostJump.isActiveAndEnabled &&
        _boostJump.gameObject.activeInHierarchy;

    bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    if (jetpackEquipped && _playerMovement.IsGrounded() && shiftHeld)
    {
#if UNITY_EDITOR
        Debug.Log("[PlayerJump] TryJumpFromDash() blocked by jetpack+Shift gate.");
#endif
        return;
    }

    bool isGrounded = _playerMovement.IsGrounded();

    // We will NOT end the dash in any branch below. We only notify the dash that we jumped.
    // Clear/prepare shared fall bookkeeping (like in other jump paths)
    _lastFallTime = 0f;
    _currentFallTime = 0f;
    _isFalling = false;
    _wasActuallyFallingLastFrame = false;

    // --- 1) Grounded: perform a FULL first jump, same height as normal ground jump ---
    if (isGrounded)
    {
        if (_playerDash != null && _playerDash.IsDashing())
            _playerDash.NotifyJumpedDuringDash();

        // Use full first jump force (no 0.6x nerf)
        PerformJumpWithoutEndingDash(_firstJumpForce, _firstJumpCooldown, _firstJumpSound);

        _currentJumpCount = 1;
        _hasDoubleJumped = false;
        _jumpedFromGround = true;
        _remainingAirJumps = _maxAirJumps;

        _wasGroundedLastFrame = false;

        if (_animator != null)
        {
            _animator.SetTrigger("jump");
            _jumpTriggerActive = true;
        }

        return;
    }

    // --- 2) Airborne while dashing: allow double jump (if eligible), else air jump (if remaining) ---
    // Double jump (only if we originally left ground via a first jump and haven't double-jumped yet)
    if (!_hasDoubleJumped && _jumpedFromGround)
    {
        if (_playerStamina != null && !_playerStamina.TryUseJumpStamina())
            return;

        if (_playerDash != null && _playerDash.IsDashing())
            _playerDash.NotifyJumpedDuringDash();

        PerformJumpWithoutEndingDash(_doubleJumpForce, _doubleJumpCooldown, _doubleJumpSound);

        _hasDoubleJumped = true;
        _currentJumpCount = 2;

        if (_doubleJumpVFXPrefab != null)
            Instantiate(_doubleJumpVFXPrefab, transform.position, Quaternion.identity);

        if (_animator != null)
        {
            _animator.SetTrigger("doubleJump");
            _jumpTriggerActive = true;
        }

        return;
    }

    // Air jump(s)
    if (_remainingAirJumps > 0)
    {
        if (_playerStamina != null && !_playerStamina.TryUseJumpStamina())
            return;

        if (_playerDash != null && _playerDash.IsDashing())
            _playerDash.NotifyJumpedDuringDash();

        PerformJumpWithoutEndingDash(_airJumpForce, _airJumpCooldown, _airJumpSound);

        _currentJumpCount++;
        _remainingAirJumps--;

        // VFX parity with regular air jump
        if (_airJumpVFXPrefab != null)
        {
            GameObject vfx = Instantiate(_airJumpVFXPrefab, transform.position, Quaternion.identity);
            float progress = 1f - (float)_remainingAirJumps / _maxAirJumps;
            float scale = _airJumpVFXScale * (1f - progress * 0.5f);
            vfx.transform.localScale = new Vector3(scale, scale, scale);
            var r = vfx.GetComponent<Renderer>();
            if (r != null) r.material.color = Color.Lerp(_airJumpVFXColor, Color.white, progress);
        }

        if (_animator != null)
        {
            _animator.SetInteger("airJumpSide", _airJumpSide);
            _animator.SetTrigger("airJump");
            _jumpTriggerActive = true;
            _airJumpSide = (_airJumpSide == 0) ? 1 : 0;
        }

        return;
    }

    // --- 3) No valid jump while dashing: do nothing ---
    // (Keeps dash going, just no jump available)
}

private void PerformJumpWithoutEndingDash(float force, float cooldown, AudioClip sound)
{
    // Tell movement we jumped (sets timers/flags and forces ungrounded)
    if (_playerMovement != null)
        _playerMovement.NotifyJumped();

    Vector3 jumpDir = _gravityBody != null ? -_gravityBody.GravityDirection.normalized : transform.up;

    // Break resting contact so the dash-jump always lifts cleanly
    if (_playerMovement != null)
        _playerMovement.PreJumpSeparation(0.03f);

    // Preserve horizontal; clear vertical like a normal jump
    Vector3 currentVelocity = _rigidbody.velocity;
    Vector3 verticalVelocity = Vector3.Project(currentVelocity, jumpDir);
    _rigidbody.velocity = currentVelocity - verticalVelocity;

    // Impulse up
    _rigidbody.AddForce(jumpDir * force, ForceMode.Impulse);

    // SFX
    if (_audioSource != null && sound != null)
    {
        _audioSource.clip = sound;
        _audioSource.Play();
    }

    _lastJumpTime = Time.time;
    StartCoroutine(JumpCooldown(cooldownTime: cooldown));

    // Keep the dash alive; PlayerDash respects _hasJumpedDuringDash for vertical
    // (no additional calls needed here)
}

    private void UpdateFallTracking(bool isGrounded)
{
    // Get gravity direction (down)
    Vector3 gravityDir = _gravityBody != null
        ? _gravityBody.GravityDirection.normalized
        : Vector3.down;

    // Velocity projected onto gravity (positive means moving *with* gravity)
    Vector3 velocity = _rigidbody.velocity;
    float   fallDot  = Vector3.Dot(velocity, gravityDir);

    // Be responsive but not twitchy
    const float FALL_START_THRESHOLD = 0.05f;  // start counting quickly
    const float FALL_CONT_THRESHOLD  = 0.02f;  // keep counting with even smaller motion

    bool isActuallyFallingNow = (!isGrounded) &&
                                (fallDot > (_isFalling ? FALL_CONT_THRESHOLD : FALL_START_THRESHOLD));

    // Apex handling: rising -> falling transition while airborne
    bool wasRisingThenFalling = !_wasActuallyFallingLastFrame && isActuallyFallingNow && !isGrounded;
    if (wasRisingThenFalling && _jumpTriggerActive)
    {
        if (_animator != null)
        {
            _animator.ResetTrigger("jump");
            _animator.ResetTrigger("doubleJump");
            _animator.ResetTrigger("airJump");
        }
        _jumpTriggerActive = false;
    }

    if (!isGrounded)
    {
        if (isActuallyFallingNow && !_isFalling)
        {
            // Just entered falling state
            _fallStartTime = Time.time;
            _isFalling = true;
        }

        // Update fall timer only while truly falling
        if (_isFalling)
        {
            _currentFallTime = Mathf.Max(0f, Time.time - _fallStartTime);
        }
    }
    else
    {
        // On ground: freeze & store last fall duration, but DO NOT feed it to Animator anymore
        if (_isFalling)
        {
            _lastFallTime = _currentFallTime;  // keep for gameplay/landing logic if you need it
        }

        // Reset live counters; Animator is driven to 0 in Update() when grounded
        _currentFallTime = 0f;
        _isFalling = false;

        // If a jump trigger was still armed, clear it on land
        if (_jumpTriggerActive)
        {
            if (_animator != null)
            {
                _animator.ResetTrigger("jump");
                _animator.ResetTrigger("doubleJump");
                _animator.ResetTrigger("airJump");
            }
            _jumpTriggerActive = false;
        }
    }

    _wasActuallyFallingLastFrame = isActuallyFallingNow;
}

    private void TryJump()
{
    // Extra safety: re-check the same gate here in case someone calls TryJump() directly
    bool jetpackEquipped =
        _boostJump != null &&
        _boostJump.isActiveAndEnabled &&
        _boostJump.gameObject.activeInHierarchy;

    bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    if (jetpackEquipped && _playerMovement.IsGrounded() && shiftHeld)
    {
#if UNITY_EDITOR
        Debug.Log("[PlayerJump] TryJump() blocked by jetpack+Shift gate.");
#endif
        return;
    }

    bool isGrounded = _playerMovement.IsGrounded();

    // First jump (from ground)
    if (isGrounded)
    {
        _lastFallTime = 0f;
        _currentFallTime = 0f;
        _isFalling = false;

        PerformJump(_firstJumpForce, _firstJumpCooldown, _firstJumpSound);
        _currentJumpCount = 1;
        _hasDoubleJumped = false;
        _jumpedFromGround = true;
        _remainingAirJumps = _maxAirJumps;

        _wasGroundedLastFrame = false;

        if (_animator != null)
        {
            _animator.SetTrigger("jump");
            _jumpTriggerActive = true;
        }
        return;
    }

    // Double jump
    if (!_hasDoubleJumped && _jumpedFromGround)
    {
        if (_playerStamina != null && !_playerStamina.TryUseJumpStamina())
            return;

        _currentFallTime = 0f;
        _isFalling = false;
        _wasActuallyFallingLastFrame = false;

        PerformJump(_doubleJumpForce, _doubleJumpCooldown, _doubleJumpSound);
        _hasDoubleJumped = true;
        _currentJumpCount = 2;

        if (_doubleJumpVFXPrefab != null)
            Instantiate(_doubleJumpVFXPrefab, transform.position, Quaternion.identity);

        if (_animator != null)
        {
            _animator.SetTrigger("doubleJump");
            _jumpTriggerActive = true;
        }
        return;
    }

    // Air jumps
    if (_remainingAirJumps > 0)
    {
        if (_playerStamina != null && !_playerStamina.TryUseJumpStamina())
            return;

        _currentFallTime = 0f;
        _isFalling = false;
        _wasActuallyFallingLastFrame = false;

        PerformJump(_airJumpForce, _airJumpCooldown, _airJumpSound);
        _currentJumpCount++;
        _remainingAirJumps--;

        if (_airJumpVFXPrefab != null)
        {
            GameObject vfx = Instantiate(_airJumpVFXPrefab, transform.position, Quaternion.identity);
            float progress = 1f - (float)_remainingAirJumps / _maxAirJumps;
            float scale = _airJumpVFXScale * (1f - progress * 0.5f);
            vfx.transform.localScale = new Vector3(scale, scale, scale);
            var r = vfx.GetComponent<Renderer>();
            if (r != null) r.material.color = Color.Lerp(_airJumpVFXColor, Color.white, progress);
        }

        if (_animator != null)
        {
            _animator.SetInteger("airJumpSide", _airJumpSide);
            _animator.SetTrigger("airJump");
            _jumpTriggerActive = true;
            _airJumpSide = (_airJumpSide == 0) ? 1 : 0;
        }
    }
}

    private void PerformJump(float force, float cooldown, AudioClip sound)
{
    // Notify movement first (sets timers/flags and forces ungrounded)
    if (_playerMovement != null)
        _playerMovement.NotifyJumped();

    Vector3 jumpDir = _gravityBody != null ? -_gravityBody.GravityDirection.normalized : transform.up;

    // Break resting contact on uneven ground before the impulse
    if (_playerMovement != null)
        _playerMovement.PreJumpSeparation(0.03f); // ~3cm is enough to avoid pinning

    // Clear vertical velocity (same behavior as before)
    Vector3 currentVelocity = _rigidbody.velocity;
    Vector3 verticalVelocity = Vector3.Project(currentVelocity, jumpDir);
    _rigidbody.velocity = currentVelocity - verticalVelocity;

    // Apply jump impulse
    _rigidbody.AddForce(jumpDir * force, ForceMode.Impulse);

    // Optional SFX
    if (_audioSource != null && sound != null)
    {
        _audioSource.clip = sound;
        _audioSource.Play();
    }

    _lastJumpTime = Time.time;
    StartCoroutine(JumpCooldown(cooldownTime: cooldown));

    // If we were dashing, the normal jump ends the dash early (as you had before)
    if (_playerDash != null && _playerDash.IsDashing())
    {
        _playerDash.EndDashEarly();
    }
}
    
    private IEnumerator JumpCooldown(float cooldownTime)
    {
        _canJump = false;
        yield return new WaitForSeconds(cooldownTime);
        _canJump = true;
    }
    
    private void ResetJumps()
    {
        _currentJumpCount = 0;
        _hasDoubleJumped = false;
        _jumpedFromGround = false;
        _remainingAirJumps = _maxAirJumps;
        // Reset air jump side when landing
        _airJumpSide = 0;
        
        // NOTE: We don't reset _lastFallTime here anymore
        // It will only reset when we explicitly jump again
    }

    // Public method to force-reset jumps (useful for power-ups, etc.)
    public void ForceResetJumps()
    {
        ResetJumps();
    }
    
    // Public method to reset fall time (for external use)
    public void ResetFallTime()
    {
        _lastFallTime = 0f;
        _currentFallTime = 0f;
        _isFalling = false;
    }
    
    // Public method to get current jump count
    public int GetCurrentJumpCount()
    {
        return _currentJumpCount;
    }
    
    // Public method to get remaining air jumps
    public int GetRemainingAirJumps()
    {
        return _remainingAirJumps;
    }
    
    // Public method to get max jumps
    public int GetMaxAirJumps()
    {
        return _maxAirJumps;
    }
    
    // Public method to get current fall time
    public float GetFallTime()
    {
        return _playerMovement.IsGrounded() ? _lastFallTime : _currentFallTime;
    }
    
    // Public method to check if actually falling
    public bool IsActuallyFalling()
    {
        return _wasActuallyFallingLastFrame;
    }
}