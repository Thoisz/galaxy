using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private InputActionReference movementControl;
    [SerializeField] private float playerSpeed = 2.0f;
    [SerializeField] private float rotationSpeed = 4f;
    [SerializeField] private float idleBufferTime = 0.2f; // Delay before transitioning to Idle

    private CharacterController controller;
    private Transform cameraMainTransform;
    private Animator animator;
    private Coroutine idleBufferCoroutine;

    // Public property to indicate if the player is moving
    public bool IsMoving { get; private set; } = false;

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        cameraMainTransform = Camera.main.transform;
        animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        movementControl?.action.Enable();
    }

    private void OnDisable()
    {
        movementControl?.action.Disable();
    }

    private void Update()
    {
        HandleMovement();
    }

    public void HandleMovement()
    {
        Vector2 movementInput = movementControl.action.ReadValue<Vector2>();
        Vector3 cameraForward = Vector3.ProjectOnPlane(cameraMainTransform.forward, Vector3.up).normalized;
        Vector3 cameraRight = Vector3.ProjectOnPlane(cameraMainTransform.right, Vector3.up).normalized;
        Vector3 moveDirection = (cameraForward * movementInput.y + cameraRight * movementInput.x).normalized;

        controller.Move(moveDirection * Time.deltaTime * playerSpeed);

        // Update the IsMoving property
        IsMoving = moveDirection != Vector3.zero;

        if (IsMoving)
        {
            // Player is moving, cancel any idle transition
            if (idleBufferCoroutine != null)
            {
                StopCoroutine(idleBufferCoroutine);
                idleBufferCoroutine = null;
            }
            animator.SetBool("IsMoving", true);
        }
        else
        {
            // Player stopped moving, start idle delay
            if (idleBufferCoroutine == null)
            {
                idleBufferCoroutine = StartCoroutine(IdleBuffer());
            }
        }

        // Smoothly rotate the player in the direction of movement
        if (IsMoving)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }
    }

    private IEnumerator IdleBuffer()
    {
        yield return new WaitForSeconds(idleBufferTime);
        animator.SetBool("IsMoving", false);
        idleBufferCoroutine = null;
    }
}
