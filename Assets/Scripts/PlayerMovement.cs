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

    [Range(0f, 1f)]
    public float airControlFactor = 0.75f;

    [Header("Dash Options")]
    public float dashSpeed = 22f;
    public float dashDuration = 0.18f;
    public float dashCooldown = 0.4f;

    // Effects listen to accepted dashes, never raw button presses.
    public event System.Action DashStarted;

    [Header("Attack Movement")]
    [Tooltip("Fraction of normal walking speed available during attacks.")]
    [Range(0f, 1f)]
    public float attackMoveMultiplier = 0.08f;

    [Tooltip("Maximum turning speed without a target, in degrees per second.")]
    [Min(0f)]
    public float attackFreeTurnSpeed = 65f;

    [Tooltip("Maximum turning speed toward a locked target, in degrees per second.")]
    [Min(0f)]
    public float attackLockOnTurnSpeed = 360f;

    [Header("Camera Reference")]
    public Transform cameraTransform;

    [Header("Lock-On")]
    public DMCStyleCamera lockOnCamera;

    [Range(0f, 1f)]
    public float lockOnSpeedMultiplier = 0.75f;


    public Vector2 AnimationMove
    {
        get;
        private set;
    }

    public bool IsLockedOn
    {
        get
        {
            return lockOnCamera != null &&
                   lockOnCamera.LockedTarget != null;
        }
    }

    public bool IsDashing
    {
        get
        {
            return isDashing;
        }
    }

    public bool MovementLocked
    {
        get;
        private set;
    }


    // MovementLocked still indicates that normal locomotion is restricted.
    // Attack movement is a steerable mode within that restriction.
    public bool IsAttackMovementActive => attackMovementActive;

    bool attackMovementActive;
    float attackPushElapsed;
    float attackPushSpeed;
    float attackPushDelay;
    float attackPushDuration;
    float attackAnimationSpeed = 1f;

    CharacterController controller;

    Vector3 verticalVelocity;
    Vector3 moveDirection;

    Vector2 rawInput;

    bool isDashing;
    bool hasAirDashed;

    float dashTimer;
    float nextDashTime;


    void Start()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (lockOnCamera == null && cameraTransform != null)
        {
            lockOnCamera =
                cameraTransform.GetComponent<DMCStyleCamera>();
        }

        if (lockOnCamera == null)
        {
            lockOnCamera =
                FindFirstObjectByType<DMCStyleCamera>();
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }


    void Update()
    {
        AnimationMove = Vector2.zero;

        HandleGroundingAndGravity();


        // Preserve the hard lock API for non-attack callers.
        if (MovementLocked && !attackMovementActive)
        {
            ApplyGravityOnly();
            return;
        }


        ReadInput();


        if (isDashing)
        {
            HandleDashExecution();
            return;
        }


        HandleJumpAndDashInputs();

        // An accepted dash takes priority on the very frame it starts.
        if (isDashing)
        {
            HandleDashExecution();
            return;
        }

        if (attackMovementActive)
        {
            HandleAttackMovement();
            return;
        }

        HandleMovement();
    }


    public void LockMovement()
    {
        ClearAttackMovement();
        MovementLocked = true;

        rawInput = Vector2.zero;
        moveDirection = Vector3.zero;

        AnimationMove = Vector2.zero;

        isDashing = false;
        dashTimer = 0f;
    }


    public void UnlockMovement()
    {
        ClearAttackMovement();
        MovementLocked = false;
    }


    public void BeginAttackMovement(
        float peakPushSpeed,
        float pushDelay,
        float pushDuration,
        float animationSpeed)
    {
        // A new swing replaces the previous push without cancelling a dash.
        ClearAttackMovement();
        MovementLocked = true;
        AnimationMove = Vector2.zero;
        attackMovementActive = true;
        attackPushSpeed = Mathf.Max(0f, peakPushSpeed);
        attackPushDelay = Mathf.Max(0f, pushDelay);
        attackPushDuration = Mathf.Max(0.01f, pushDuration);
        SetAttackMovementSpeed(animationSpeed);
    }


    public void SetAttackMovementSpeed(float animationSpeed)
    {
        attackAnimationSpeed = Mathf.Max(0.05f, animationSpeed);
    }


    void ClearAttackMovement()
    {
        attackMovementActive = false;
        attackPushElapsed = 0f;
        attackPushSpeed = 0f;
    }


    void HandleAttackMovement()
    {
        float dt = Time.deltaTime;
        Transform target = lockOnCamera != null ? lockOnCamera.LockedTarget : null;
        if (target != null && !target.gameObject.activeInHierarchy)
        {
            target = null;
        }

        bool hasTarget = target != null;
        Vector3 inputDirection = Vector3.zero;

        if (rawInput.magnitude >= 0.1f)
        {
            float cameraYaw = cameraTransform != null
                ? cameraTransform.eulerAngles.y
                : transform.eulerAngles.y;

            float inputYaw = Mathf.Atan2(rawInput.x, rawInput.y)
                * Mathf.Rad2Deg + cameraYaw;

            inputDirection = Quaternion.Euler(0f, inputYaw, 0f) * Vector3.forward;
        }

        // Without input or a target, retain the current facing direction.
        Vector3 desiredFacing = hasTarget
            ? target.position - transform.position
            : inputDirection;
        desiredFacing.y = 0f;

        if (desiredFacing.sqrMagnitude > 0.001f)
        {
            float turnSpeed = hasTarget
                ? attackLockOnTurnSpeed
                : attackFreeTurnSpeed;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(desiredFacing),
                Mathf.Max(0f, turnSpeed) * dt
            );
        }

        float walkSpeed = controller.isGrounded
            ? moveSpeed
            : moveSpeed * airControlFactor;

        if (hasTarget)
        {
            walkSpeed *= lockOnSpeedMultiplier;
        }

        Vector3 walkingVelocity = inputDirection * walkSpeed
            * Mathf.Clamp01(attackMoveMultiplier)
            * Mathf.Clamp01(rawInput.magnitude);

        // Integrate a smooth bell-shaped pulse across this frame. This also
        // captures short pushes correctly when a frame crosses a pulse boundary.
        float previousTime = attackPushElapsed;
        attackPushElapsed += dt * attackAnimationSpeed;

        float previousProgress = Mathf.Clamp01(
            (previousTime - attackPushDelay) / attackPushDuration);
        float progress = Mathf.Clamp01(
            (attackPushElapsed - attackPushDelay) / attackPushDuration);

        float pushDistance = attackPushSpeed * attackPushDuration
            * (PushIntegral(progress) - PushIntegral(previousProgress));

        Vector3 pushDirection = Vector3.ProjectOnPlane(
            transform.forward, Vector3.up).normalized;

        // One move owns attack walking, push, and gravity for this frame.
        controller.Move(
            (walkingVelocity + verticalVelocity) * dt
            + pushDirection * pushDistance
        );

        // Keep locomotion animation input at zero during attack animations.
        // Update() already resets AnimationMove before entering this method.
    }


    static float PushIntegral(float progress)
    {
        // Integral of 0.5 * (1 - cos(2*pi*t)): zero speed at both ends,
        // peak speed at the midpoint, and total area of 0.5.
        float angle = 2f * Mathf.PI * progress;
        return 0.5f * progress - Mathf.Sin(angle) / (4f * Mathf.PI);
    }


    // Legacy immediate push API; PlayerCombat no longer uses this method.
    public void AttackPush(float speed)
    {
        if (controller == null)
        {
            return;
        }

        Vector3 push =
            transform.forward *
            speed *
            Time.deltaTime;

        controller.Move(push);
    }


    void ApplyGravityOnly()
    {
        Vector3 gravityMovement =
            verticalVelocity *
            Time.deltaTime;

        controller.Move(gravityMovement);
    }


    void ReadInput()
    {
        rawInput = Vector2.zero;


        if (Gamepad.current != null)
        {
            rawInput =
                Gamepad.current.leftStick.ReadValue();
        }


        if (
            rawInput.magnitude < 0.1f &&
            Keyboard.current != null
        )
        {
            if (Keyboard.current.wKey.isPressed)
            {
                rawInput.y += 1f;
            }

            if (Keyboard.current.sKey.isPressed)
            {
                rawInput.y -= 1f;
            }

            if (Keyboard.current.aKey.isPressed)
            {
                rawInput.x -= 1f;
            }

            if (Keyboard.current.dKey.isPressed)
            {
                rawInput.x += 1f;
            }

            rawInput = rawInput.normalized;
        }
    }


    void HandleJumpAndDashInputs()
    {
        bool jumpPressed = false;
        bool dashPressed = false;


        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame)
            {
                jumpPressed = true;
            }

            if (Gamepad.current.buttonEast.wasPressedThisFrame)
            {
                dashPressed = true;
            }
        }


        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                jumpPressed = true;
            }

            if (Keyboard.current.leftShiftKey.wasPressedThisFrame)
            {
                dashPressed = true;
            }
        }


        if (
            jumpPressed &&
            !attackMovementActive &&
            controller.isGrounded
        )
        {
            verticalVelocity.y =
                Mathf.Sqrt(
                    jumpHeight *
                    -2f *
                    gravity
                );
        }


        if (dashPressed)
        {
            TryStartDash();
        }
    }


    public bool TryStartDash()
    {
        if (!isActiveAndEnabled || controller == null || !controller.enabled ||
            isDashing || Time.time < nextDashTime ||
            (MovementLocked && !attackMovementActive) || dashDuration <= 0f)
        {
            return false;
        }

        // A jump requested this frame already counts as leaving the ground.
        bool airborne = !controller.isGrounded || verticalVelocity.y > 0f;
        if (airborne && hasAirDashed)
        {
            return false;
        }

        if (airborne)
        {
            hasAirDashed = true;
        }

        ReadInput();
        StartDash();
        return true;
    }


    void HandleMovement()
    {
        Vector3 horizontalMove =
            Vector3.zero;


        Transform lockedTarget = null;

        if (lockOnCamera != null)
        {
            lockedTarget =
                lockOnCamera.LockedTarget;
        }


        bool isLockedOn =
            lockedTarget != null;


        if (isLockedOn)
        {
            Vector3 enemyDirection =
                lockedTarget.position -
                transform.position;

            enemyDirection.y = 0f;


            if (
                enemyDirection.sqrMagnitude >
                0.001f
            )
            {
                Quaternion targetRotation =
                    Quaternion.LookRotation(
                        enemyDirection
                    );

                transform.rotation =
                    Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        rotationSpeed *
                        Time.deltaTime
                    );
            }
        }


        if (rawInput.magnitude >= 0.1f)
        {
            float targetAngle =
                Mathf.Atan2(
                    rawInput.x,
                    rawInput.y
                ) *
                Mathf.Rad2Deg +
                cameraTransform.eulerAngles.y;


            if (!isLockedOn)
            {
                Quaternion targetRotation =
                    Quaternion.Euler(
                        0f,
                        targetAngle,
                        0f
                    );

                transform.rotation =
                    Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        rotationSpeed *
                        Time.deltaTime
                    );
            }


            moveDirection =
                Quaternion.Euler(
                    0f,
                    targetAngle,
                    0f
                ) *
                Vector3.forward;


            float currentSpeed =
                controller.isGrounded
                ? moveSpeed
                : moveSpeed *
                  airControlFactor;


            if (isLockedOn)
            {
                currentSpeed *=
                    lockOnSpeedMultiplier;
            }


            horizontalMove =
                moveDirection *
                currentSpeed *
                Mathf.Clamp01(
                    rawInput.magnitude
                );
        }


        Vector3 finalVelocity =
            horizontalMove +
            verticalVelocity;


        controller.Move(
            finalVelocity *
            Time.deltaTime
        );


        float referenceSpeed =
            moveSpeed;


        if (isLockedOn)
        {
            referenceSpeed *=
                lockOnSpeedMultiplier;
        }


        Vector3 localVelocity =
            transform.InverseTransformDirection(
                controller.velocity
            );


        if (
            !isDashing &&
            referenceSpeed > 0.001f
        )
        {
            AnimationMove =
                Vector2.ClampMagnitude(
                    new Vector2(
                        localVelocity.x,
                        localVelocity.z
                    ) /
                    referenceSpeed,
                    1f
                );
        }
    }


    void StartDash()
    {
        isDashing = true;

        dashTimer =
            dashDuration;

        nextDashTime =
            Time.time +
            dashCooldown;


        if (rawInput.magnitude < 0.1f)
        {
            moveDirection =
                transform.forward;
        }
        else
        {
            // Use this press's input, not the last walking frame's direction.
            float cameraYaw = cameraTransform != null
                ? cameraTransform.eulerAngles.y
                : transform.eulerAngles.y;
            float angle = Mathf.Atan2(rawInput.x, rawInput.y)
                * Mathf.Rad2Deg + cameraYaw;
            moveDirection = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        }

        // Dash replaces the current swing's remaining push, not the combo.
        attackPushSpeed = 0f;
        DashStarted?.Invoke();
    }


    void HandleDashExecution()
    {
        verticalVelocity.y = 0f;

        // Animation continues during a dash; never pause and replay its push.
        if (attackMovementActive)
        {
            attackPushElapsed += Time.deltaTime * attackAnimationSpeed;
        }

        float dashStep = Mathf.Min(Time.deltaTime, Mathf.Max(0f, dashTimer));


        controller.Move(
            moveDirection *
            dashSpeed *
            dashStep
        );


        dashTimer -=
            Time.deltaTime;


        if (dashTimer <= 0f)
        {
            isDashing = false;
        }
    }


    void HandleGroundingAndGravity()
    {
        if (controller.isGrounded)
        {
            hasAirDashed = false;


            if (verticalVelocity.y < 0f)
            {
                verticalVelocity.y = -2f;
            }
        }
        else
        {
            verticalVelocity.y +=
                gravity *
                Time.deltaTime;
        }
    }
}