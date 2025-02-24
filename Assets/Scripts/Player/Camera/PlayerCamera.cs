using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCamera : MonoBehaviour
{
    [Header("Camera Components")]
    [SerializeField] private Transform target;
    [SerializeField] private Transform cameraTransform;

    [Header("Zoom Settings")]
    [SerializeField] private float minZoomDistance = 0.1f;
    [SerializeField] private float maxZoomDistance = 50f;
    [SerializeField] private int zoomInTicks = 15;
    [SerializeField] private int zoomOutTicks = 20;
    [SerializeField] private float zoomSmoothing = 10f;

    [Header("Pan Settings")]
    [SerializeField] private float panSensitivity = 2f;
    [SerializeField] private float panSmoothing = 15f;

    [Header("Collision Settings")]
    [SerializeField] private LayerMask collisionMask;
    [SerializeField] private float collisionBuffer = 0.2f;

    [Header("Target Offset")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0, 1.1f, 0);

    [Header("Layer Settings")]
    [SerializeField] private Renderer[] characterRenderers;
    [SerializeField] private int localPlayerLayer = 8;
    [SerializeField] private int invisibleLayer = 9;

    private float currentZoomDistance;
    private float targetZoomDistance;
    private Vector3 currentRotation;
    private Vector3 targetRotation;
    private bool isPanning = false;

    private float currentZoomPercentage = 0.5f; // Between 0 and 1
    private bool isInFirstPerson = false;

    private Camera playerCamera;

    private PlayerMovement playerMovement;

    private Vector3 initialCursorPosition; // To save the cursor position when panning starts

    private void Start()
    {
        UpdateZoomDistanceFromPercentage();

        currentRotation = transform.eulerAngles;
        targetRotation = currentRotation;

        playerCamera = cameraTransform.GetComponent<Camera>();
        if (playerCamera == null)
        {
            Debug.LogError("No Camera component found on the cameraTransform.");
        }

        playerMovement = target.GetComponent<PlayerMovement>();
        if (playerMovement == null)
        {
            Debug.LogError("No PlayerMovement script found on the target.");
        }
    }

    private void Update()
    {
        HandleInput();
        UpdateCameraPosition();
        HandleCharacterLayer();

        if (isInFirstPerson)
        {
            if (playerMovement != null)
            {
                if (playerMovement.IsMoving)
                {
                    // If moving, let the player's rotation follow their movement direction
                    // (Handled in the PlayerMovement script)
                }
                else
                {
                    // If standing still, sync player's rotation with the camera's horizontal rotation
                    target.rotation = Quaternion.Euler(0, targetRotation.y, 0);
                }
            }
        }
    }

    private void HandleInput()
    {
        float scrollInput = Mouse.current.scroll.ReadValue().y;
        if (!Mathf.Approximately(scrollInput, 0f))
        {
            float zoomStep = 1f / ((scrollInput > 0) ? zoomInTicks : zoomOutTicks);
            currentZoomPercentage += (scrollInput > 0) ? -zoomStep : zoomStep;

            // Clamp zoom percentage between 0 and 1
            currentZoomPercentage = Mathf.Clamp01(currentZoomPercentage);

            UpdateZoomDistanceFromPercentage();
        }

        if (isInFirstPerson)
        {
            LockCursor(true);
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            targetRotation.x -= mouseDelta.y * panSensitivity * Time.deltaTime;
            targetRotation.x = Mathf.Clamp(targetRotation.x, -89f, 89f); // Adjusted clamp for first-person
            targetRotation.y += mouseDelta.x * panSensitivity * Time.deltaTime;
        }
        else
        {
            if (Mouse.current.rightButton.isPressed)
            {
                if (!isPanning)
                {
                    isPanning = true;
                    initialCursorPosition = Mouse.current.position.ReadValue(); // Save the cursor position
                    LockCursor(true);
                }

                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                targetRotation.x -= mouseDelta.y * panSensitivity * Time.deltaTime;
                targetRotation.x = Mathf.Clamp(targetRotation.x, -89f, 89f); // Clamping for third-person as well
                targetRotation.y += mouseDelta.x * panSensitivity * Time.deltaTime;
            }
            else if (isPanning)
            {
                isPanning = false;
                LockCursor(false);
                Mouse.current.WarpCursorPosition(initialCursorPosition); // Restore the cursor position
            }
        }
    }

    private void UpdateZoomDistanceFromPercentage()
    {
        float zoomRange = maxZoomDistance - minZoomDistance;

        // Properly map zoom percentage to the zoom range
        float nonLinearZoomFactor = Mathf.Pow(currentZoomPercentage, 1.5f); // Adjust exponent for more control
        targetZoomDistance = Mathf.Lerp(minZoomDistance, maxZoomDistance, nonLinearZoomFactor);
    }

    private void UpdateCameraPosition()
    {
        currentZoomDistance = Mathf.Lerp(currentZoomDistance, targetZoomDistance, Time.deltaTime * zoomSmoothing);

        // Apply clamped rotation
        currentRotation.x = Mathf.Clamp(currentRotation.x, -89f, 89f); 
        currentRotation = Vector3.Lerp(currentRotation, targetRotation, Time.deltaTime * panSmoothing);

        Quaternion rotation = Quaternion.Euler(currentRotation.x, currentRotation.y, 0);
        Vector3 targetPosition = target.position + targetOffset;
        Vector3 desiredCameraPosition = targetPosition + rotation * new Vector3(0, 0, -currentZoomDistance);

        // Collision Handling: Check if there's an obstacle between the target and the desired camera position
        Vector3 finalCameraPosition = desiredCameraPosition;
        
        if (Physics.Linecast(targetPosition, desiredCameraPosition, out RaycastHit hit, collisionMask))
        {
        finalCameraPosition = hit.point + (hit.normal * collisionBuffer); // Push camera slightly away from obstacle
        }

    // Apply final camera position and orientation
    cameraTransform.position = finalCameraPosition;
    cameraTransform.LookAt(targetPosition);
}

    private void HandleCharacterLayer()
    {
        bool shouldBeInFirstPerson = currentZoomDistance <= minZoomDistance + 0.1f;

        if (shouldBeInFirstPerson != isInFirstPerson)
        {
            isInFirstPerson = shouldBeInFirstPerson;

            if (isInFirstPerson)
            {
                SetCharacterLayer(invisibleLayer);
                playerCamera.cullingMask &= ~(1 << invisibleLayer);
                LockCursor(true); // Lock cursor when entering first-person mode
            }
            else
            {
                SetCharacterLayer(localPlayerLayer);
                playerCamera.cullingMask |= (1 << invisibleLayer);
                LockCursor(false); // Unlock cursor when exiting first-person mode
            }
        }
    }

    private void SetCharacterLayer(int layer)
    {
        foreach (Renderer renderer in characterRenderers)
        {
            renderer.gameObject.layer = layer;

            Transform[] children = renderer.GetComponentsInChildren<Transform>();
            foreach (Transform child in children)
            {
                child.gameObject.layer = layer;
            }
        }
    }

    private void LockCursor(bool lockCursor)
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked; // Lock the cursor to the center of the screen
            Cursor.visible = false; // Hide the cursor
        }
        else
        {
            Cursor.lockState = CursorLockMode.None; // Unlock the cursor
            Cursor.visible = true; // Show the cursor
        }
    }
}
