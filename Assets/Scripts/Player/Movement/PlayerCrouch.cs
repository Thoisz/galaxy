using UnityEngine;

[DisallowMultipleComponent]
public class PlayerCrouch : MonoBehaviour
{
    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private string crouchParam = "isCrouching";

    [Header("Input")]
    [SerializeField] private KeyCode crouchKey = KeyCode.LeftShift;
    [SerializeField] private bool requireGrounded = true;

    [Header("Crouch Speed")]
    [Tooltip("Absolute move speed while crouching, in m/s (e.g., 2).")]
    [SerializeField] private float crouchSpeed = 2f;

    [Header("Refs (auto if left empty)")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerFlight playerFlight;

    public bool IsCrouching { get; private set; }

    // we track exactly what delta we applied so we can remove it cleanly
    private float _appliedModifier = 0f;
    private bool _modifierActive = false;
    // Optional external override for crouch speed (e.g., jetpack charge)
    // When set, this replaces the inspector crouchSpeed while crouching.
    private float? _externalCrouchSpeedOverride = null;

    void Awake()
    {
        if (!playerMovement) playerMovement = GetComponent<PlayerMovement>();
        if (!playerFlight) playerFlight = GetComponent<PlayerFlight>();
        if (!animator) animator = GetComponentInChildren<Animator>(true);
    }

    void OnDisable()
    {
        TryRemoveModifier();
        _externalCrouchSpeedOverride = null;
        SetCrouch(false);
    }

    void Update()
    {
        bool isGrounded = playerMovement && playerMovement.IsGrounded();
        bool notFlying = !(playerFlight && playerFlight.IsFlying);

        // Determine if crouch key is held
        bool crouchHeld = Input.GetKey(crouchKey);

        // 1. For speed effects, respect requireGrounded
        bool speedEligible = crouchHeld && notFlying && (!requireGrounded || isGrounded);

        // 2. For animation, allow mid-air crouch visual
        bool animShouldCrouch = crouchHeld && notFlying;

        // Apply speed modifier logic (only while grounded if requireGrounded)
        if (speedEligible && !IsCrouching)
        {
            float target = _externalCrouchSpeedOverride.HasValue ? _externalCrouchSpeedOverride.Value : crouchSpeed;
            ApplyCrouchModifier(target);
            SetCrouch(true);
        }
        else if (!speedEligible && IsCrouching)
        {
            TryRemoveModifier();
            SetCrouch(false);
        }

        // Force animation param even mid-air
        if (animator && !string.IsNullOrEmpty(crouchParam))
            animator.SetBool(crouchParam, animShouldCrouch);

        // Safety: cancel crouch if flight begins
        if (IsCrouching && playerFlight && playerFlight.IsFlying)
        {
            TryRemoveModifier();
            SetCrouch(false);
        }
    }

    private void SetCrouch(bool on)
    {
        IsCrouching = on;
        if (animator && !string.IsNullOrEmpty(crouchParam))
            animator.SetBool(crouchParam, IsCrouching);
    }

    private void ApplyCrouchModifier(float targetSpeed)
    {
        if (!playerMovement) return;

        if (_modifierActive) TryRemoveModifier();

        float currentBase = playerMovement.GetCurrentMoveSpeed();
        float delta = targetSpeed - currentBase;

        if (delta < 0f)
        {
            playerMovement.AddSpeedModifier(delta);
            _appliedModifier = delta;
            _modifierActive = true;
        }
        else
        {
            _appliedModifier = 0f;
            _modifierActive = false;
        }
    }

    private void TryRemoveModifier()
    {
        if (!playerMovement) return;
        if (!_modifierActive) return;

        playerMovement.RemoveSpeedModifier(_appliedModifier);
        _appliedModifier = 0f;
        _modifierActive = false;
    }

    public void SetExternalCrouchSpeedOverride(float speed)
    {
        _externalCrouchSpeedOverride = speed;
        if (IsCrouching)
            ApplyCrouchModifier(speed);
    }

    public void ClearExternalCrouchSpeedOverride()
    {
        _externalCrouchSpeedOverride = null;
        if (IsCrouching)
            ApplyCrouchModifier(crouchSpeed);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        crouchSpeed = Mathf.Max(0.1f, crouchSpeed);
    }
#endif
}