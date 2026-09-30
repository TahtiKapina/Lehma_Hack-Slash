using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Attach to Main Camera. Keep CameraHolder stationary and outside the Player.
// The anchor is a persistent WORLD position, separate from the collision camera.
[DisallowMultipleComponent]
[RequireComponent(typeof(UnityEngine.Camera))]
[DefaultExecutionOrder(100)]
public class DMCStyleCamera : MonoBehaviour
{
    public enum CameraState { Free, LockedOn }
    public CameraState State => lockedTarget != null ? CameraState.LockedOn : CameraState.Free;
    public Transform LockedTarget => lockedTarget;
    public Vector3 AnchorPosition => anchor;

    [Header("References")]
    [Tooltip("The transform that actually moves. Its forward direction is used on lock-on.")]
    public Transform player;
    [Tooltip("Optional starting camera-position marker. Read once; not a live parent/pivot.")]
    public Transform startingAnchor;
    public Vector3 focusOffset = new Vector3(0f, 1f, 0f);

    [Header("Free camera: horizontal distance band")]
    [Min(0.5f)] public float minimumDistance = 5f;
    [Min(0.6f)] public float maximumDistance = 8f;
    [Min(0.02f)] public float anchorSmoothTime = 0.45f;
    [Min(0.02f)] public float aimSmoothTime = 0.12f;
    [Min(1f)] public float maximumAnchorSpeed = 20f;

    [Header("Manual camera")]
    public float orbitSpeed = 140f;
    public float pitchSpeed = 75f;
    public float minimumPitch = 8f;
    public float maximumPitch = 65f;
    [Range(0f, 0.9f)] public float stickDeadzone = 0.15f;
    public bool invertY;
    [Tooltip("Disable to supply input through SetCameraInput instead.")]
    public bool readTemporaryInput = true;
    [Tooltip("Legacy Input Manager only: create these joystick axes yourself.")]
    public bool useLegacyStickAxes;
    public string legacyHorizontalAxis = "CameraHorizontal";
    public string legacyVerticalAxis = "CameraVertical";

    [Header("Obstacles")]
    [Tooltip("Select environment layers ONLY. Exclude Player and enemies.")]
    public LayerMask obstacleMask;
    [Min(0.05f)] public float collisionRadius = 0.3f;
    [Min(0.01f)] public float wallPadding = 0.08f;
    [Min(0f)] public float softWallMargin = 0.5f;
    [Min(0.02f)] public float collisionReturnTime = 0.3f;

    [Header("Lock-on (hold RB or Q)")]
    public bool enableLockOn = true;
    public string enemyTag = "Enemy";
    [Min(1f)] public float lockRange = 25f;
    [Min(1f)] public float lockDistance = 7f;
    public float lockPitch = 22f;
    public Vector3 enemyFocusOffset = new Vector3(0f, 1f, 0f);
    [Range(0f, 0.5f)] public float targetFramingWeight = 0.35f;

    Vector3 anchor, anchorVelocity, aim, aimVelocity;
    Vector2 lookInput;
    Transform lockedTarget;
    float pitch, boomDistance, boomVelocity;
    bool lockHeld, previousLockHeld, initialized;

    void Start()
    {
        if (player == null)
        {
            Debug.LogError("DMCStyleCamera: assign the moving Player transform.", this);
            enabled = false;
            return;
        }
        ResetAnchor();
    }

    // Can also be called after teleporting the player.
    public void ResetAnchor()
    {
        if (player == null) return;
        aim = player.position + focusOffset;
        anchor = startingAnchor != null ? startingAnchor.position : transform.position;
        Vector3 offset = anchor - aim;
        float horizontal = new Vector2(offset.x, offset.z).magnitude;
        if (horizontal < 0.1f)
        {
            Vector3 back = -Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
            if (back.sqrMagnitude < 0.01f) back = Vector3.back;
            anchor = aim + back * minimumDistance + Vector3.up * 3f;
            offset = anchor - aim;
            horizontal = minimumDistance;
        }
        pitch = Mathf.Clamp(Mathf.Atan2(offset.y, horizontal) * Mathf.Rad2Deg,
            minimumPitch, maximumPitch);
        anchorVelocity = aimVelocity = Vector3.zero;
        boomDistance = Vector3.Distance(aim, anchor);
        boomVelocity = 0f;
        lockedTarget = null;
        previousLockHeld = false;
        initialized = true;
    }

    public void SetCameraInput(Vector2 stick, bool holdLock)
    {
        lookInput = Vector2.ClampMagnitude(stick, 1f);
        lockHeld = holdLock;
    }

    void Update()
    {
        if (!readTemporaryInput) return;
        Vector2 stick = Vector2.zero;
        bool held = false;
#if ENABLE_INPUT_SYSTEM
        if (Gamepad.current != null)
        {
            stick = Gamepad.current.rightStick.ReadValue();
            held = Gamepad.current.rightShoulder.isPressed;
        }
        if (Keyboard.current != null)
        {
            var k = Keyboard.current;
            stick += new Vector2((k.rightArrowKey.isPressed ? 1 : 0) - (k.leftArrowKey.isPressed ? 1 : 0),
                (k.upArrowKey.isPressed ? 1 : 0) - (k.downArrowKey.isPressed ? 1 : 0));
            held |= k.qKey.isPressed;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        stick = new Vector2((Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
            (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0));
        held = Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.JoystickButton5);
        if (useLegacyStickAxes)
            stick += new Vector2(Input.GetAxisRaw(legacyHorizontalAxis), Input.GetAxisRaw(legacyVerticalAxis));
#endif
        SetCameraInput(stick, held);
    }

    void LateUpdate()
    {
        if (!initialized || player == null || Time.deltaTime <= 0f) return;
        float dt = Time.deltaTime;
        Vector3 focus = player.position + focusOffset;
        bool wantsLock = enableLockOn && lockHeld;
        if (wantsLock && !previousLockHeld) AcquireTarget();
        if (!wantsLock || (lockedTarget != null && !lockedTarget.gameObject.activeInHierarchy))
            lockedTarget = null;
        // A destroyed/disabled target ends this lock. Release and press to acquire again.
        previousLockHeld = wantsLock;

        Vector3 desiredAim = focus;
        Vector3 goal;
        if (lockedTarget != null)
        {
            desiredAim = Vector3.Lerp(focus, lockedTarget.position + enemyFocusOffset, targetFramingWeight);
            Vector3 back = -Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
            if (back.sqrMagnitude < 0.01f) back = Vector3.back;
            goal = focus + back * lockDistance + Vector3.up * (lockDistance * Mathf.Tan(lockPitch * Mathf.Deg2Rad));
        }
        else
        {
            Vector3 radial = Vector3.ProjectOnPlane(anchor - focus, Vector3.up);
            float distance = radial.magnitude;
            Vector3 direction = distance > 0.001f ? radial / distance : Vector3.back;
            float length = Mathf.Clamp(distance, minimumDistance, maximumDistance);
            Vector2 input = lookInput.magnitude > stickDeadzone ? lookInput : Vector2.zero;
            if (input != Vector2.zero)
            {
                direction = Quaternion.AngleAxis(input.x * orbitSpeed * dt, Vector3.up) * direction;
                pitch = Mathf.Clamp(pitch + input.y * (invertY ? -1f : 1f) * pitchSpeed * dt,
                    minimumPitch, maximumPitch);
                // Move the stored anchor directly: manual input takes priority over follow lag.
                anchor = focus + direction * length + Vector3.up * (length * Mathf.Tan(pitch * Mathf.Deg2Rad));
                anchorVelocity = Vector3.zero;
            }
            // Inside the band X/Z remains exactly anchored when there is no input.
            goal = focus + direction * length + Vector3.up * (length * Mathf.Tan(pitch * Mathf.Deg2Rad));
        }
        anchor = Vector3.SmoothDamp(anchor, goal, ref anchorVelocity, anchorSmoothTime, maximumAnchorSpeed, dt);
        aim = Vector3.SmoothDamp(aim, desiredAim, ref aimVelocity, aimSmoothTime, Mathf.Infinity, dt);

        // Do not feed collision offsets back into the anchor: this avoids wall feedback jitter.
        Vector3 boom = anchor - focus;
        float wantedDistance = boom.magnitude;
        Vector3 boomDirection = wantedDistance > 0.001f ? boom / wantedDistance : Vector3.back;
        float safeDistance = wantedDistance;
        float comfortableDistance = wantedDistance;
        if (Physics.SphereCast(focus, collisionRadius, boomDirection, out RaycastHit hit,
            wantedDistance, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            safeDistance = Mathf.Max(0f, hit.distance - wallPadding);
            comfortableDistance = Mathf.Max(0f, safeDistance - softWallMargin);
        }
        boomDistance = Mathf.SmoothDamp(boomDistance, comfortableDistance, ref boomVelocity, collisionReturnTime, Mathf.Infinity, dt);
        // Immediate inward safety clamp; smooth outward recovery prevents clipping through a wall.
        if (boomDistance > safeDistance) { boomDistance = safeDistance; boomVelocity = 0f; }
        transform.position = focus + boomDirection * boomDistance;
        Vector3 lookDirection = aim - transform.position;
        if (lookDirection.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
    }

    void AcquireTarget()
    {
        lockedTarget = null;
        float closest = lockRange * lockRange;
        GameObject[] candidates;
        try { candidates = GameObject.FindGameObjectsWithTag(enemyTag); }
        catch (UnityException)
        {
            Debug.LogWarning("DMCStyleCamera: create the Enemy tag (or change Enemy Tag).", this);
            return;
        }
        foreach (GameObject candidate in candidates)
        {
            if (candidate.transform == player || candidate.transform.IsChildOf(player)) continue;
            float distance = (candidate.transform.position - player.position).sqrMagnitude;
            if (distance < closest) { closest = distance; lockedTarget = candidate.transform; }
        }
    }

    void OnValidate()
    {
        minimumDistance = Mathf.Max(0.5f, minimumDistance);
        maximumDistance = Mathf.Max(minimumDistance + 0.1f, maximumDistance);
        minimumPitch = Mathf.Clamp(minimumPitch, -60f, 75f);
        maximumPitch = Mathf.Clamp(maximumPitch, minimumPitch, 80f);
        lockPitch = Mathf.Clamp(lockPitch, -60f, 75f);
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !initialized) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(anchor, 0.25f);
        Gizmos.DrawLine(anchor, transform.position);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(aim, 0.15f);
    }
}
