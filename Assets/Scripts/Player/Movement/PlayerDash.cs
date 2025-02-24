// Updated PlayerDash.cs
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerDash : MonoBehaviour
{
    [SerializeField] private InputActionReference dashControl;
    [SerializeField] private float dashSpeed = 20.0f;
    [SerializeField] private float dashDuration = 0.25f;
    [SerializeField] private float dashCooldownTime = 1.0f;

    private CharacterController controller;
    private Transform cameraMainTransform;
    private Animator animator;
    private bool isDashing = false;
    private float lastDashTime = 0f;
    private Vector3 currentDashDirection = Vector3.zero;
    private int dashAnimIndex = 0; // Alternates between 0 and 1 for dash animation

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        cameraMainTransform = Camera.main.transform;
        animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        dashControl?.action.Enable();
    }

    private void OnDisable()
    {
        dashControl?.action.Disable();
    }

    public void HandleDash()
    {
        if (dashControl.action.triggered && !isDashing && Time.time >= lastDashTime + dashCooldownTime)
        {
            StartCoroutine(Dash());
        }
    }

   private IEnumerator Dash()
{
    isDashing = true;
    animator.SetBool("IsDashing", true);
    lastDashTime = Time.time;
    float startTime = Time.time;

    // Determine initial dash direction (consistent for all states)
    Vector3 inputDirection = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));

    // If no input is provided, simulate forward input
    if (inputDirection.sqrMagnitude <= 0.1f)
    {
        inputDirection = Vector3.forward; // Simulate pressing W key
    }

    // Transform the direction to camera space
    inputDirection = cameraMainTransform.TransformDirection(inputDirection);
    inputDirection = Vector3.ProjectOnPlane(inputDirection, Vector3.up).normalized;

    currentDashDirection = inputDirection;
    transform.rotation = Quaternion.LookRotation(currentDashDirection); // Face dash direction

    // Alternate dash animation
    dashAnimIndex = (dashAnimIndex == 0) ? 1 : 0;
    animator.SetInteger("DashAnimIndex", dashAnimIndex);

    while (Time.time < startTime + dashDuration)
    {
        Vector3 inputCheck = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));

        // Check for manual input mid-dash
        if (inputCheck.sqrMagnitude > 0.1f)
        {
            // Transform input direction to world space
            inputCheck = cameraMainTransform.TransformDirection(inputCheck);
            inputCheck = Vector3.ProjectOnPlane(inputCheck, Vector3.up).normalized;

            // Update dash direction and stop simulating W key
            currentDashDirection = inputCheck;
            transform.rotation = Quaternion.LookRotation(currentDashDirection);
        }

        // Move the player at a fixed dash speed
        Vector3 dashMovement = currentDashDirection * dashSpeed * Time.deltaTime;
        controller.Move(dashMovement);
        yield return null;
    }

    isDashing = false;
    animator.SetBool("IsDashing", false);
}


    private void UpdateDashDirection()
    {
        Vector3 inputDirection = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));

        if (inputDirection.sqrMagnitude > 0.1f) // If there's input
        {
            // Transform input direction to world space
            inputDirection = cameraMainTransform.TransformDirection(inputDirection);
            inputDirection = Vector3.ProjectOnPlane(inputDirection, Vector3.up).normalized;

            // Update current dash direction and snap player to face it
            currentDashDirection = inputDirection;
            transform.rotation = Quaternion.LookRotation(currentDashDirection);
        }
    }

    private void Update()
    {
        HandleDash();
    }
}
