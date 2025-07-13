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
        // Track grounded state
        bool isGrounded = _playerMovement.IsGrounded();
        
        // Update fall tracking
        UpdateFallTracking(isGrounded);
        
        // Reset jumps when landing
        if (isGrounded && !_wasGroundedLastFrame)
        {            
            // Store the fall time when landing
            if (_currentFallTime > 0)
            {
                _lastFallTime = _currentFallTime;
            }
            
            ResetJumps();
            _hasResetJumpsAfterGrounded = true;
        }
        
        // Make sure we're not grounded during jumps
        if (!isGrounded)
        {
            _hasResetJumpsAfterGrounded = false;
            
            // Track when player walks off a ledge (was grounded but now isn't, without jumping)
            if (_wasGroundedLastFrame && _currentJumpCount == 0)
            {
                // Mark that we didn't jump from ground
                _jumpedFromGround = false;
                
                // Skip straight to air jumps when walking off a ledge
                _currentJumpCount = 2; // Start at 2 since we're skipping first and double jump
                _hasDoubleJumped = true; // Skip double jump phase
                
                // We get all air jumps when walking off a ledge
                _remainingAirJumps = _maxAirJumps;
            }
        }
        
        // Jump input - allow jumping during dash
        if (Input.GetKeyDown(KeyCode.Space) && _canJump)
        {
            bool isDashing = _playerDash != null && _playerDash.IsDashing();
            
            if (isDashing)
            {
                // When dashing, force jump!
                TryJumpFromDash();
            }
            else
            {
                // Normal jump
                TryJump();
            }
        }
        
        // Update animator with fall time
        if (_animator != null)
        {
            // If in the air and falling, use current fall time
            if (!isGrounded && _isFalling)
            {
                _animator.SetFloat("fallTime", _currentFallTime);
            }
            // When grounded, use the last recorded fall time
            else if (isGrounded)
            {
                _animator.SetFloat("fallTime", _lastFallTime);
            }
            // For other cases (like rising during a jump)
            else
            {
                _animator.SetFloat("fallTime", 0f);
            }
        }
        
        // Store grounded state for next frame
        _wasGroundedLastFrame = isGrounded;
    }

    private void TryJumpFromDash()
{
    bool isGrounded = _playerMovement.IsGrounded();
    
    // Only allow jumping from dash if grounded
    if (!isGrounded)
    {
        return;
    }
    
    // Reset fall time when jumping from ground
    _lastFallTime = 0f;
    _currentFallTime = 0f;
    _isFalling = false;
    _wasActuallyFallingLastFrame = false;
    
    // CHANGED: Don't end the dash - just notify it that we're jumping
    if (_playerDash != null && _playerDash.IsDashing())
    {
        _playerDash.NotifyJumpedDuringDash();
    }
    
    // FIXED: Use half the normal jump force to compensate for dash momentum
    float reducedJumpForce = _firstJumpForce * 0.6f;
    PerformJumpWithoutEndingDash(reducedJumpForce, _firstJumpCooldown, _firstJumpSound);
    
    // Set jump state for next jump to be a double jump
    _currentJumpCount = 1;
    _hasDoubleJumped = false;
    _jumpedFromGround = true;
    _remainingAirJumps = _maxAirJumps;
    
    // Force the grounded state to false right away
    _wasGroundedLastFrame = false;
    
    // Trigger animation and set flag
    if (_animator != null)
    {
        _animator.SetTrigger("jump");
        _jumpTriggerActive = true; // NEW: Track that jump trigger is active
    }
}

private void PerformJumpWithoutEndingDash(float force, float cooldown, AudioClip sound)
{
    // Notify PlayerMovement that a jump has occurred
    if (_playerMovement != null)
    {
        _playerMovement.NotifyJumped();
    }
    
    // Calculate jump direction (opposite to gravity)
    Vector3 jumpDir = _gravityBody != null ? 
        -_gravityBody.GravityDirection.normalized : 
        transform.up;
    
    // DON'T end the dash - let it continue running
    
    // FIXED: Use the exact same method as PerformJump()
    // Clear vertical velocity (same as normal jump)
    Vector3 currentVelocity = _rigidbody.velocity;
    Vector3 verticalVelocity = Vector3.Project(currentVelocity, jumpDir);
    _rigidbody.velocity = currentVelocity - verticalVelocity;
    
    // Apply jump force (same as normal jump) - this preserves horizontal dash velocity
    _rigidbody.AddForce(jumpDir * force, ForceMode.Impulse);
    
    // Play sound if assigned
    if (_audioSource != null && sound != null)
    {
        _audioSource.clip = sound;
        _audioSource.Play();
    }
    
    // Apply cooldown
    _lastJumpTime = Time.time;
    
    // Start cooldown coroutine
    StartCoroutine(JumpCooldown(cooldownTime: cooldown));
}

    private void UpdateFallTracking(bool isGrounded)
{
    // Get gravity direction
    Vector3 gravityDir = _gravityBody != null ? 
        _gravityBody.GravityDirection.normalized : 
        Vector3.down;
        
    // Check if we're actually falling (moving in the direction of gravity)
    Vector3 velocity = _rigidbody.velocity;
    bool isActuallyFalling = Vector3.Dot(velocity, gravityDir) > 0.1f; // Positive dot product means moving in gravity direction
    
    // NEW: Check if we've reached apex of jump (transition from rising to falling)
    bool wasRising = !_wasActuallyFallingLastFrame && !isGrounded;
    bool isNowFalling = isActuallyFalling && !isGrounded;
    
    // If we were rising and now we're falling, we've reached the apex
    if (wasRising && isNowFalling && _jumpTriggerActive)
    {
        // Reset jump trigger at apex
        if (_animator != null)
        {
            _animator.ResetTrigger("jump");
            _animator.ResetTrigger("doubleJump"); 
            _animator.ResetTrigger("airJump");
        }
        _jumpTriggerActive = false;
    }
    
    // Only track fall time when in the air
    if (!isGrounded)
    {
        // If we just started falling
        if (isActuallyFalling && !_wasActuallyFallingLastFrame)
        {
            _fallStartTime = Time.time;
            _isFalling = true;
        }
        
        // Only update fall time if we're actually falling
        if (isActuallyFalling)
        {
            _currentFallTime = Time.time - _fallStartTime;
        }
    }
    else
    {
        // When we land, also reset jump trigger if it's still active
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
    
    // Save state for next frame
    _wasActuallyFallingLastFrame = isActuallyFalling;
}

    private void TryJump()
{
    bool isGrounded = _playerMovement.IsGrounded();
    
    // First jump (from ground)
    if (isGrounded)
    {
        // Reset fall time when jumping from ground
        _lastFallTime = 0f;
        _currentFallTime = 0f;
        _isFalling = false;
        
        PerformJump(_firstJumpForce, _firstJumpCooldown, _firstJumpSound);
        _currentJumpCount = 1;
        _hasDoubleJumped = false;
        _jumpedFromGround = true;
        _remainingAirJumps = _maxAirJumps;
        
        // Force the grounded state to false right away
        _wasGroundedLastFrame = false;
        
        // Trigger animation and set flag
        if (_animator != null)
        {
            _animator.SetTrigger("jump");
            _jumpTriggerActive = true; // NEW: Track that jump trigger is active
        }
        return;
    }
    
    // Double jump (second jump, but only if we jumped from ground first)
    if (!_hasDoubleJumped && _jumpedFromGround)
    {
        // Check stamina for double jump
        if (_playerStamina != null && !_playerStamina.TryUseJumpStamina())
        {
            return; // Not enough stamina
        }
        
        // Reset fall time for double jump
        _currentFallTime = 0f;
        _isFalling = false;
        _wasActuallyFallingLastFrame = false;
        
        PerformJump(_doubleJumpForce, _doubleJumpCooldown, _doubleJumpSound);
        _hasDoubleJumped = true;
        _currentJumpCount = 2;
        
        // Spawn VFX if assigned
        if (_doubleJumpVFXPrefab != null)
            Instantiate(_doubleJumpVFXPrefab, transform.position, Quaternion.identity);
            
        // Trigger animation and set flag
        if (_animator != null)
        {
            _animator.SetTrigger("doubleJump");
            _jumpTriggerActive = true; // NEW: Track that jump trigger is active
        }
        return;
    }
    
    // Air jumps (after double jump or when walked off a ledge)
    if (_remainingAirJumps > 0)
    {
        // Check stamina for air jump
        if (_playerStamina != null && !_playerStamina.TryUseJumpStamina())
        {
            return; // Not enough stamina
        }
        
        // Reset current fall tracking for air jump
        _currentFallTime = 0f;
        _isFalling = false;
        _wasActuallyFallingLastFrame = false;
        
        PerformJump(_airJumpForce, _airJumpCooldown, _airJumpSound);
        _currentJumpCount++;
        _remainingAirJumps--;
        
        // Spawn VFX if assigned
        if (_airJumpVFXPrefab != null)
        {
            GameObject vfx = Instantiate(_airJumpVFXPrefab, transform.position, Quaternion.identity);
            
            // Scale VFX based on remaining jumps
            float progress = 1f - (float)_remainingAirJumps / _maxAirJumps;
            float scale = _airJumpVFXScale * (1f - progress * 0.5f);
            vfx.transform.localScale = new Vector3(scale, scale, scale);
            
            // Change color if possible
            Renderer renderer = vfx.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = Color.Lerp(_airJumpVFXColor, Color.white, progress);
            }
        }
        
        // Use alternating air jump animations and set flag
        if (_animator != null)
        {
            // Set the air jump side parameter (0 = left, 1 = right)
            _animator.SetInteger("airJumpSide", _airJumpSide);
            
            // Trigger the air jump
            _animator.SetTrigger("airJump");
            _jumpTriggerActive = true; // NEW: Track that jump trigger is active
            
            // Toggle the side for next air jump
            _airJumpSide = (_airJumpSide == 0) ? 1 : 0;
        }
    }
}

    private void PerformJump(float force, float cooldown, AudioClip sound)
{
    // Notify PlayerMovement that a jump has occurred
    if (_playerMovement != null)
    {
        _playerMovement.NotifyJumped();
    }
    
    // Calculate jump direction (opposite to gravity)
    Vector3 jumpDir = _gravityBody != null ? 
        -_gravityBody.GravityDirection.normalized : 
        transform.up;
        
    // If we're dashing, end the dash (this is for regular jumps, not dash-jumps)
    if (_playerDash != null && _playerDash.IsDashing())
    {
        // Let dash script know we're ending the dash early for a jump
        _playerDash.EndDashEarly();
    }
        
    // Clear vertical velocity
    Vector3 currentVelocity = _rigidbody.velocity;
    Vector3 verticalVelocity = Vector3.Project(currentVelocity, jumpDir);
    _rigidbody.velocity = currentVelocity - verticalVelocity;
    
    // Apply a stronger jump force
    _rigidbody.AddForce(jumpDir * force, ForceMode.Impulse);
    
    // Play sound if assigned
    if (_audioSource != null && sound != null)
    {
        _audioSource.clip = sound;
        _audioSource.Play();
    }
    
    // Apply cooldown
    _lastJumpTime = Time.time;
    
    // Start cooldown coroutine
    StartCoroutine(JumpCooldown(cooldownTime: cooldown));
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