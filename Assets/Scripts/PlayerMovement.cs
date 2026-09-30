using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float moveSpeed = 9f;
    public float rotationSpeed = 15f;

    [Header("Jump & Air Control")]
    public float jumpHeight = 2.5f;
    public float gravity = -28f;
    [Range(0f, 1f)] public float airControlFactor = 0.6f; // Reduced control while airborne

    [Header("Dash Options")]
    public float dashSpeed = 22f;
    public float dashDuration = 0.18f;
    public float dashCooldown = 0.4f;

    [Header("Camera Reference")]
    public Transform cameraTransform;

    private CharacterController controller;
    private Vector3 velocity;
    private Vector3 moveDirection;

    // Movement & State Tracking
    private Vector2 rawInput;
    private bool isDashing;
    private float dashTimer;
    private float nextDashTime;
    private bool hasAirDashed;

    [HideInInspector] public bool canMove = true;

    private void Start()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        // Lock cursor for playtesting
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        ReadInput();
        ApplyGravityAndGrounding();

        if (isDashing)
        {
            HandleDashExecution();
            return;
        }

        if (canMove)
        {
            HandleMovement();
            HandleJumpAndDashInputs();
        }
    }

    private void ReadInput()
    {
        rawInput = Vector2.zero;

        // 1. Controller Input (Left Analog Stick)
        if (Gamepad.current != null)
        {
            rawInput = Gamepad.current.leftStick.ReadValue();
        }

        // 2. Keyboard Fallback (WASD) if stick input is negligible
        if (rawInput.magnitude < 0.1f && Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) rawInput.y += 1f;
            if (Keyboard.current.sKey.isPressed) rawInput.y -= 1f;
            if (Keyboard.current.aKey.isPressed) rawInput.x -= 1f;
            if (Keyboard.current.dKey.isPressed) rawInput.x += 1f;
            rawInput = rawInput.normalized;
        }
    }

    private void HandleMovement()
    {
        if (rawInput.magnitude >= 0.1f)
        {
            // Calculate direction relative to Camera orientation
            float targetAngle = Mathf.Atan2(rawInput.x, rawInput.y) * Mathf.Rad2Deg + cameraTransform.eulerAngles.y;
            Quaternion targetRotation = Quaternion.Euler(0f, targetAngle, 0f);

            // Rotate character toward movement direction
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

            moveDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;

            // Apply different control multiplier whether grounded or airborne
            float currentSpeed = controller.isGrounded ? moveSpeed : moveSpeed * airControlFactor;
            controller.Move(moveDirection * currentSpeed * Time.deltaTime);
        }
        else
        {
            moveDirection = Vector3.zero;
        }
    }

    private void HandleJumpAndDashInputs()
    {
        bool jumpPressed = false;
        bool dashPressed = false;

        // --- Gamepad Mapping (Xbox: South = A / East = B | PlayStation: South = Cross / East = Circle) ---
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame) jumpPressed = true;
            if (Gamepad.current.buttonEast.wasPressedThisFrame) dashPressed = true;
        }

        // --- Keyboard Fallback ---
        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame) jumpPressed = true;
            if (Keyboard.current.leftShiftKey.wasPressedThisFrame) dashPressed = true;
        }

        // Jump Execution
        if (jumpPressed && controller.isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // Dash Execution (Allows 1 ground dash + 1 air dash per jump)
        if (dashPressed && Time.time >= nextDashTime)
        {
            if (controller.isGrounded || !hasAirDashed)
            {
                if (!controller.isGrounded) hasAirDashed = true;
                StartDash();
            }
        }
    }

    private void StartDash()
    {
        isDashing = true;
        dashTimer = dashDuration;
        nextDashTime = Time.time + dashCooldown;

        // Default to facing direction if stick is neutral
        if (rawInput.magnitude < 0.1f)
        {
            moveDirection = transform.forward;
        }
    }

    private void HandleDashExecution()
    {
        // Dash freezes vertical fall briefly for DMC-style snappy air dodging
        velocity.y = 0f;
        controller.Move(moveDirection * dashSpeed * Time.deltaTime);

        dashTimer -= Time.deltaTime;
        if (dashTimer <= 0f)
        {
            isDashing = false;
        }
    }

    private void ApplyGravityAndGrounding()
    {
        if (controller.isGrounded)
        {
            hasAirDashed = false; // Reset air dash when landing
            if (velocity.y < 0)
            {
                velocity.y = -2f; // Snap to ground
            }
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}