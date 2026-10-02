using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float moveSpeed = 9f;
    public float rotationSpeed = 15f;

    [Header("Jump & Air Control")]
    public float jumpHeight = 3.5f;
    public float gravity = -30f;
    [Range(0f, 1f)] public float airControlFactor = 0.75f;

    [Header("Dash Options")]
    public float dashSpeed = 22f;
    public float dashDuration = 0.18f;
    public float dashCooldown = 0.4f;

    [Header("Camera Reference")]
    public Transform cameraTransform;

    [Header("Lock-On")]
    public DMCStyleCamera lockOnCamera;
    [Range(0f, 1f)] public float lockOnSpeedMultiplier = 0.75f;

    // Read by CowLocomotion; directions are relative to this movement root.
    public Vector2 AnimationMove { get; private set; }
    public bool IsLockedOn => lockOnCamera != null && lockOnCamera.LockedTarget != null;
    public bool IsDashing => isDashing;

    private CharacterController controller;
    private Vector3 verticalVelocity;
    private Vector3 moveDirection;

    // State Tracking
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

        // Prefer the movement camera; fall back to the scene's camera script.
        if (lockOnCamera == null && cameraTransform != null)
        {
            lockOnCamera = cameraTransform.GetComponent<DMCStyleCamera>();
        }
        if (lockOnCamera == null)
        {
            lockOnCamera = FindFirstObjectByType<DMCStyleCamera>();
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        AnimationMove = Vector2.zero;
        ReadInput();

        HandleGroundingAndGravity();

        if (isDashing)
        {
            HandleDashExecution();
            return;
        }

        if (canMove)
        {
            HandleJumpAndDashInputs();
            HandleMovement();
        }
    }

    private void ReadInput()
    {
        rawInput = Vector2.zero;

        // Gamepad
        if (Gamepad.current != null)
        {
            rawInput = Gamepad.current.leftStick.ReadValue();
        }

        // WASD
        if (rawInput.magnitude < 0.1f && Keyboard.current != null)
        {
            rawInput = Vector2.zero;
            if (Keyboard.current.wKey.isPressed) rawInput.y += 1f;
            if (Keyboard.current.sKey.isPressed) rawInput.y -= 1f;
            if (Keyboard.current.aKey.isPressed) rawInput.x -= 1f;
            if (Keyboard.current.dKey.isPressed) rawInput.x += 1f;
            rawInput = rawInput.normalized;
        }
    }

    private void HandleJumpAndDashInputs()
    {
        bool jumpPressed = false;
        bool dashPressed = false;

        // Gamepad (A = Jump / B = Dash)
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame) jumpPressed = true;
            if (Gamepad.current.buttonEast.wasPressedThisFrame) dashPressed = true;
        }

        // Keyboard (Space = Jump / LeftShift = Dash)
        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame) jumpPressed = true;
            if (Keyboard.current.leftShiftKey.wasPressedThisFrame) dashPressed = true;
        }

        if (jumpPressed && controller.isGrounded)
        {
            verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // Dash Logic
        if (dashPressed && Time.time >= nextDashTime)
        {
            if (controller.isGrounded || !hasAirDashed)
            {
                if (!controller.isGrounded) hasAirDashed = true;
                StartDash();
            }
        }
    }

    private void HandleMovement()
    {
        Vector3 horizontalMove = Vector3.zero;
        Transform lockedTarget = lockOnCamera != null ? lockOnCamera.LockedTarget : null;
        bool isLockedOn = lockedTarget != null;

        // Face the enemy even when the movement stick is neutral.
        if (isLockedOn)
        {
            Vector3 enemyDirection = lockedTarget.position - transform.position;
            enemyDirection.y = 0f;
            if (enemyDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(enemyDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }

        if (rawInput.magnitude >= 0.1f)
        {
            float targetAngle = Mathf.Atan2(rawInput.x, rawInput.y) * Mathf.Rad2Deg + cameraTransform.eulerAngles.y;

            // When locked on, strafe while continuing to face the enemy.
            if (!isLockedOn)
            {
                Quaternion targetRotation = Quaternion.Euler(0f, targetAngle, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }

            moveDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            float currentSpeed = controller.isGrounded ? moveSpeed : moveSpeed * airControlFactor;
            if (isLockedOn) currentSpeed *= lockOnSpeedMultiplier;
            horizontalMove = moveDirection * currentSpeed * Mathf.Clamp01(rawInput.magnitude);
        }

        Vector3 finalVelocity = horizontalMove + verticalVelocity;
        controller.Move(finalVelocity * Time.deltaTime);

        // Actual motion prevents walking in place when completely blocked by a wall.
        float referenceSpeed = moveSpeed * (isLockedOn ? lockOnSpeedMultiplier : 1f);
        Vector3 localVelocity = transform.InverseTransformDirection(controller.velocity);
        if (!isDashing && referenceSpeed > 0.001f)
            AnimationMove = Vector2.ClampMagnitude(
                new Vector2(localVelocity.x, localVelocity.z) / referenceSpeed, 1f);
    }

    private void StartDash()
    {
        isDashing = true;
        dashTimer = dashDuration;
        nextDashTime = Time.time + dashCooldown;

        if (rawInput.magnitude < 0.1f)
        {
            moveDirection = transform.forward;
        }
    }

    private void HandleDashExecution()
    {
        verticalVelocity.y = 0f;
        controller.Move(moveDirection * dashSpeed * Time.deltaTime);

        dashTimer -= Time.deltaTime;
        if (dashTimer <= 0f)
        {
            isDashing = false;
        }
    }

    private void HandleGroundingAndGravity()
    {
        if (controller.isGrounded)
        {
            hasAirDashed = false;
            if (verticalVelocity.y < 0)
            {
                verticalVelocity.y = -2f;
            }
        }
        else
        {
            verticalVelocity.y += gravity * Time.deltaTime;
        }
    }
}
