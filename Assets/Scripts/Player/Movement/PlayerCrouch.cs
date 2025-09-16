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
    [SerializeField] private PlayerFlight   playerFlight;

    public bool IsCrouching { get; private set; }

    // we track exactly what delta we applied so we can remove it cleanly
    private float _appliedModifier = 0f;
    private bool  _modifierActive  = false;

    void Awake()
    {
        if (!playerMovement) playerMovement = GetComponent<PlayerMovement>();
        if (!playerFlight)   playerFlight   = GetComponent<PlayerFlight>();
        if (!animator)       animator       = GetComponentInChildren<Animator>(true);
    }

    void OnDisable()
    {
        TryRemoveModifier();
        SetCrouch(false);
    }

    void Update()
    {
        bool groundedOk = !requireGrounded || (playerMovement && playerMovement.IsGrounded());
        bool notFlying  = !(playerFlight && playerFlight.IsFlying);
        bool want = Input.GetKey(crouchKey) && groundedOk && notFlying;

        if (want && !IsCrouching)
        {
            // entering crouch
            ApplyCrouchModifier();
            SetCrouch(true);
        }
        else if (!want && IsCrouching)
        {
            // exiting crouch
            TryRemoveModifier();
            SetCrouch(false);
        }

        // safety: if flight begins while crouching, cancel crouch
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

    private void ApplyCrouchModifier()
    {
        if (!playerMovement) return;

        // Make crouchSpeed an absolute target:
        // delta = desiredAbsolute - currentAbsolute (usually negative)
        float current = playerMovement.GetCurrentMoveSpeed();
        float delta = crouchSpeed - current;

        // Only apply if it actually slows (avoid speeding up if current < crouchSpeed)
        if (delta < 0f)
        {
            playerMovement.AddSpeedModifier(delta);
            _appliedModifier = delta;
            _modifierActive  = true;
        }
        else
        {
            _appliedModifier = 0f;
            _modifierActive  = false;
        }
    }

    private void TryRemoveModifier()
    {
        if (!playerMovement) return;
        if (!_modifierActive) return;

        playerMovement.RemoveSpeedModifier(_appliedModifier);
        _appliedModifier = 0f;
        _modifierActive  = false;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        crouchSpeed = Mathf.Max(0.1f, crouchSpeed);
    }
#endif
}