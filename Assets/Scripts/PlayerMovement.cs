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
    public DMCStyleCamera dmcCamera;

    private CharacterController controller;
    private Vector3 verticalVelocity;
    private Vector3 moveDirection;

    private Vector2 rawInput;
    private bool isDashing;
    private float dashTimer;
    private float nextDashTime;
    private bool hasAirDashed;

    private float gravityPauseTimer = 0f;

    [HideInInspector] public bool canMove = true;

    private void Start()
    {
        controller = GetComponent<CharacterController>();

        if (dmcCamera == null)
        {
            dmcCamera = FindFirstObjectByType<DMCStyleCamera>();
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
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

    public void ResetVerticalVelocity(float overrideY = 0f)
    {
        verticalVelocity.y = overrideY;
    }

    public void PauseGravity(float duration)
    {
        gravityPauseTimer = duration;
        verticalVelocity.y = 0f;
    }

    private void ReadInput()
    {
        rawInput = Vector2.zero;

        if (Gamepad.current != null)
        {
            rawInput = Gamepad.current.leftStick.ReadValue();
        }

        if (rawInput.magnitude < 0.1f && Keyboard.current != null)
        {
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

        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame) jumpPressed = true;
            if (Gamepad.current.buttonEast.wasPressedThisFrame) dashPressed = true;
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame) jumpPressed = true;
            if (Keyboard.current.leftShiftKey.wasPressedThisFrame) dashPressed = true;
        }

        if (jumpPressed && controller.isGrounded)
        {
            verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

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
        Transform cameraTransform = dmcCamera != null ? dmcCamera.transform : Camera.main.transform;

        if (rawInput.magnitude >= 0.1f)
        {
            float targetAngle = Mathf.Atan2(rawInput.x, rawInput.y) * Mathf.Rad2Deg + cameraTransform.eulerAngles.y;
            moveDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;

            float currentSpeed = controller.isGrounded ? moveSpeed : moveSpeed * airControlFactor;
            horizontalMove = moveDirection * currentSpeed;

            // Rotate toward stick input ONLY if not locked on
            if (dmcCamera == null || dmcCamera.LockedTarget == null)
            {
                Quaternion targetRotation = Quaternion.Euler(0f, targetAngle, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }

        // Lock-on Override: Always face the locked enemy target
        if (dmcCamera != null && dmcCamera.LockedTarget != null)
        {
            Vector3 targetDir = dmcCamera.LockedTarget.position - transform.position;
            targetDir.y = 0f;

            if (targetDir.sqrMagnitude > 0.001f)
            {
                Quaternion lockRotation = Quaternion.LookRotation(targetDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, lockRotation, rotationSpeed * Time.deltaTime);
            }
        }

        Vector3 finalVelocity = horizontalMove + verticalVelocity;
        controller.Move(finalVelocity * Time.deltaTime);
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
            if (gravityPauseTimer > 0f)
            {
                gravityPauseTimer -= Time.deltaTime;
                verticalVelocity.y = 0f;
            }
            else
            {
                verticalVelocity.y += gravity * Time.deltaTime;
            }
        }
    }
}