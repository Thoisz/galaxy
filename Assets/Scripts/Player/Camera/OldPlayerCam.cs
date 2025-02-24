// OldPlayerCamera.cs
using UnityEngine;
using Cinemachine;
using UnityEngine.InputSystem;

public class OldPlayerCamera : MonoBehaviour
{
    [Header("Input Settings")]
    [SerializeField] private InputActionAsset inputProvider;

    [Header("Camera Settings")]
    [SerializeField] private CinemachineFreeLook freeLookCamera;
    [SerializeField] private float baseZoomSpeed = 1f;
    [SerializeField] private float zoomSensitivity = 1f;
    [SerializeField] private float zoomAcceleration = 2.5f;
    [SerializeField] private float zoomInnerRange = 3f;
    [SerializeField] private float zoomOuterRange = 50f;

    private float currentMiddleRigRadius = 10f;
    private float targetMiddleRigRadius = 10f;

    private InputAction zoomAction;

    private void Awake()
    {
        zoomAction = inputProvider.FindActionMap("Mouse").FindAction("MouseZoom");
        zoomAction.performed += OnZoomPerformed;
        zoomAction.canceled += OnZoomCanceled;
    }

    private void OnEnable()
    {
        zoomAction?.Enable();
    }

    private void OnDisable()
    {
        zoomAction?.Disable();
    }

    private void LateUpdate()
    {
        SmoothZoom();
    }

    private void OnZoomPerformed(InputAction.CallbackContext context)
    {
        AdjustCameraZoom(context.ReadValue<float>());
    }

    private void OnZoomCanceled(InputAction.CallbackContext context)
    {
        AdjustCameraZoom(0f);
    }

    private void SmoothZoom()
    {
        if (Mathf.Approximately(currentMiddleRigRadius, targetMiddleRigRadius)) return;

        currentMiddleRigRadius = Mathf.Lerp(currentMiddleRigRadius, targetMiddleRigRadius, zoomAcceleration * Time.deltaTime);
        currentMiddleRigRadius = Mathf.Clamp(currentMiddleRigRadius, zoomInnerRange, zoomOuterRange);

        freeLookCamera.m_Orbits[1].m_Radius = currentMiddleRigRadius;
        freeLookCamera.m_Orbits[0].m_Height = currentMiddleRigRadius;
    }

    private void AdjustCameraZoom(float zoomInput)
    {
        if (Mathf.Approximately(zoomInput, 0f)) return;

        float dynamicZoomSpeed = baseZoomSpeed * (currentMiddleRigRadius / zoomOuterRange) * zoomSensitivity;
        targetMiddleRigRadius = currentMiddleRigRadius - zoomInput * dynamicZoomSpeed;
        targetMiddleRigRadius = Mathf.Clamp(targetMiddleRigRadius, zoomInnerRange, zoomOuterRange);
    }
}