using UnityEngine;
using UnityEngine.InputSystem;

// Start/stop before movement; LateUpdate uses the animated muzzle pose.
[DefaultExecutionOrder(-50)]
public class PlayerUdderGun : MonoBehaviour
{
    [Header("References")]
    public PlayerMovement playerMovement;
    public PlayerCombat combat;
    public Animator animator;
    public Transform spinTarget;
    public Transform muzzle;
    public UdderPellet pelletPrefab;
    [Tooltip("Use a dedicated AudioSource, separate from sword/flicker audio.")]
    public AudioSource gunAudio;

    [Header("Animation (base layer)")]
    public string shootingState = "Shoot";
    public string freeExitState = "FreeMovement";
    public string lockedExitState = "LockOnMovement";
    [Min(0f)] public float blendTime = 0.06f;

    [Header("Timing")]
    [Min(0f)] public float spinUpDuration = 1f;
    [Min(0f)] public float spinDownDuration = 0.35f;
    [Min(0f)] public float cooldown = 0.4f;

    [Header("Spin")]
    public Vector3 localSpinAxis = Vector3.forward;
    [Min(0f)] public float maximumSpinSpeed = 1800f;
    [Tooltip("Enable for a dedicated animated udder bone. Leave off for an unanimated mesh pivot.")]
    public bool spinTargetIsAnimated;

    [Header("Pellets")]
    [Min(0.1f)] public float pelletsPerSecond = 16f;
    [Min(0.1f)] public float pelletSpeed = 25f;
    [Range(0f, 45f)] public float coneHalfAngle = 6f;
    [Min(0f)] public float pelletDamage = 1f;
    [Min(0f)] public float pelletHitStun = 0.05f;
    [Min(0f)] public float pelletKnockback = 0.03f;

    [Header("Audio")]
    public AudioClip powerUpSound;
    public AudioClip firingSound;
    [Tooltip("Start of the clean firing section, in seconds.")]
    [Min(0f)] public float firingLoopStart = 0f;
    [Tooltip("Only this many seconds are copied and looped; the rest of the clip never plays.")]
    [Min(0.01f)] public float firingLoopLength = 2f;
    public AudioClip powerDownSound;

    enum GunPhase { Idle, SpinningUp, Firing }
    GunPhase phase;
    public bool IsBusy => phase != GunPhase.Idle;

    float phaseStarted;
    float nextShotAt;
    float nextAllowedAt;
    float spinSpeed;
    float spinAngle;
    bool waitForRelease;
    Quaternion baseSpinRotation;
    Quaternion lastAnimatedBase;
    bool appliedAnimatedSpin;
    int shootHash;
    int freeHash;
    int lockedHash;
    bool ready;
    AudioClip croppedFiringLoop;
    AudioClip cachedFiringSource;
    int cachedStartSample;
    int cachedSampleCount;

    void Start()
    {
        if (playerMovement == null) playerMovement = GetComponentInParent<PlayerMovement>();
        if (combat == null && playerMovement != null)
            combat = playerMovement.GetComponentInChildren<PlayerCombat>();
        if (animator == null && combat != null) animator = combat.animator;
        if (combat != null) combat.udderGun = this;
        if (spinTarget != null) baseSpinRotation = spinTarget.localRotation;
        if (playerMovement == null || combat == null || animator == null || muzzle == null || pelletPrefab == null)
        {
            Debug.LogError("PlayerUdderGun: assign Movement, Combat, Animator, Muzzle, and Pellet Prefab.", this);
            return;
        }
        if (spinTarget == playerMovement.transform || spinTarget == animator.transform)
        {
            Debug.LogError("PlayerUdderGun: Spin Target must be an udder bone or mesh pivot, not the player/Animator root.", this);
            return;
        }
        if (animator.runtimeAnimatorController == null) return;
        shootHash = StateHash(shootingState);
        freeHash = StateHash(freeExitState);
        lockedHash = StateHash(lockedExitState);
        if (!animator.HasState(0, shootHash) || !animator.HasState(0, freeHash) || !animator.HasState(0, lockedHash))
        {
            Debug.LogError("PlayerUdderGun: check Shoot, Free Exit State and Locked Exit State names in the Animator base layer.", this);
            return;
        }
        ready = true;
    }

    int StateHash(string name)
    {
        string prefix = animator.GetLayerName(0) + ".";
        name = name ?? "";
        return Animator.StringToHash(name.StartsWith(prefix) ? name : prefix + name);
    }

    void Update()
    {
        // Remove last frame's procedural offset before animation evaluates.
        // This prevents accumulated rotation if a clip doesn't key this bone.
        if (appliedAnimatedSpin && spinTarget != null)
        {
            spinTarget.localRotation = lastAnimatedBase;
            appliedAnimatedSpin = false;
        }
        bool held = (Gamepad.current != null && Gamepad.current.buttonWest.isPressed)
            || (Keyboard.current != null && Keyboard.current.xKey.isPressed);
        bool pressed = (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame)
            || (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame);
        if (!held) waitForRelease = false;

        if (!IsBusy)
        {
            if (ready && pressed && !waitForRelease && Time.time >= nextAllowedAt) TryBegin();
            return;
        }
        if (!held || !playerMovement.isActiveAndEnabled || !combat.isActiveAndEnabled ||
            !animator.isActiveAndEnabled || !playerMovement.ExclusiveMovementActive)
        {
            StopShooting(true);
            return;
        }
        UpdateAim();
        if (phase == GunPhase.SpinningUp && Time.time - phaseStarted >= Mathf.Max(0f, spinUpDuration))
        {
            phase = GunPhase.Firing;
            nextShotAt = Time.time;
            PlayAudio(GetFiringLoop(), true);
        }
    }

    void TryBegin()
    {
        if (!playerMovement.isActiveAndEnabled || !combat.isActiveAndEnabled || !animator.isActiveAndEnabled) return;
        // Free/lock-on locomotion, ordinary attacks and dashes can enter shooting.
        // Preserve the existing uninterruptible Stinger and on-hit slow window.
        if (combat.stinger != null && combat.stinger.IsActive)
        {
            if (!combat.stinger.CanInterrupt) return;
            combat.stinger.Cancel();
        }
        if (playerMovement.MovementLocked && !playerMovement.IsAttackMovementActive) return;
        combat.InterruptComboForSpecialMove();
        playerMovement.BeginExclusiveMovement();
        phase = GunPhase.SpinningUp;
        phaseStarted = Time.time;
        animator.CrossFadeInFixedTime(shootHash, Mathf.Max(0f, blendTime), 0, 0f);
        PlayAudio(powerUpSound, false);
        UpdateAim();
    }

    Transform Target()
    {
        Transform target = playerMovement.lockOnCamera != null ? playerMovement.lockOnCamera.LockedTarget : null;
        return target != null && target.gameObject.activeInHierarchy ? target : null;
    }

    void UpdateAim()
    {
        Transform target = Target();
        Vector3 desired = target != null
            ? target.position - muzzle.position
            : playerMovement.ReadWorldMovementInput();
        // Left-stick/WASD input steers in camera-relative space. Neutral input
        // preserves facing; rotating the camera alone no longer redirects fire.
        if (desired.sqrMagnitude < 0.0001f) return;
        Vector3 horizontal = Vector3.ProjectOnPlane(desired, Vector3.up);
        if (horizontal.sqrMagnitude > 0.0001f)
        {
            float speed = target != null ? playerMovement.attackLockOnTurnSpeed : playerMovement.attackFreeTurnSpeed;
            playerMovement.transform.rotation = Quaternion.RotateTowards(playerMovement.transform.rotation,
                Quaternion.LookRotation(horizontal), Mathf.Max(0f, speed) * Time.deltaTime);
        }
    }

    void LateUpdate()
    {
        if (!ready) return;
        float targetSpeed = IsBusy ? maximumSpinSpeed : 0f;
        float duration = IsBusy ? spinUpDuration : spinDownDuration;
        spinSpeed = Mathf.MoveTowards(spinSpeed, targetSpeed,
            maximumSpinSpeed * Time.deltaTime / Mathf.Max(0.001f, duration));
        spinAngle = Mathf.Repeat(spinAngle + spinSpeed * Time.deltaTime, 360f);
        if (spinTarget != null)
        {
            Quaternion rotation = spinTargetIsAnimated ? spinTarget.localRotation : baseSpinRotation;
            if (spinTargetIsAnimated)
            {
                lastAnimatedBase = rotation;
                appliedAnimatedSpin = true;
            }
            spinTarget.localRotation = rotation * Quaternion.AngleAxis(spinAngle,
                localSpinAxis.sqrMagnitude > 0.001f ? localSpinAxis.normalized : Vector3.forward);
        }
        if (!IsBusy) return;

        // No walking in either aim mode. Gravity still keeps the player grounded.
        playerMovement.MoveExclusive(Vector3.zero);

        if (phase != GunPhase.Firing) return;
        float interval = 1f / Mathf.Max(0.1f, pelletsPerSecond);
        int emitted = 0;
        while (Time.time >= nextShotAt && emitted < 8)
        {
            FirePellet();
            nextShotAt += interval;
            emitted++;
        }
        // Drop a severe backlog rather than creating hundreds of pellets at once.
        if (emitted == 8 && nextShotAt <= Time.time) nextShotAt = Time.time + interval;
    }

    void FirePellet()
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(playerMovement.transform.forward, Vector3.up).normalized;
        Vector3 aim = horizontal;
        // Uniform distribution over a solid cone, not a square spread.
        float cosTheta = Mathf.Lerp(1f, Mathf.Cos(coneHalfAngle * Mathf.Deg2Rad), Random.value);
        float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));
        float phi = Random.value * Mathf.PI * 2f;
        Vector3 local = new Vector3(Mathf.Cos(phi) * sinTheta, Mathf.Sin(phi) * sinTheta, cosTheta);
        Vector3 shot = Quaternion.LookRotation(aim) * local;
        UdderPellet pellet = Instantiate(pelletPrefab, muzzle.position, Quaternion.LookRotation(shot));
        pellet.Launch(playerMovement.transform, shot * Mathf.Max(0.1f, pelletSpeed),
            pelletDamage, pelletHitStun, pelletKnockback);
    }

    AudioClip GetFiringLoop()
    {
        if (firingSound == null) return null;
        int start = Mathf.Clamp(Mathf.RoundToInt(firingLoopStart * firingSound.frequency),
            0, Mathf.Max(0, firingSound.samples - 1));
        int count = Mathf.Clamp(Mathf.RoundToInt(firingLoopLength * firingSound.frequency),
            1, firingSound.samples - start);
        if (croppedFiringLoop != null && cachedFiringSource == firingSound &&
            cachedStartSample == start && cachedSampleCount == count) return croppedFiringLoop;

        // Copy only the clean section. AudioSource.loop then wraps precisely
        // inside that section, without frame-based seeking into the unwanted tail.
        float[] samples = new float[count * firingSound.channels];
        if (!firingSound.GetData(samples, start))
        {
            Debug.LogError("PlayerUdderGun: firing audio must use Decompress On Load, Preload Audio Data, and Load In Background off in its import settings. Apply those settings so its loop section can be read.", this);
            return null;
        }
        if (croppedFiringLoop != null) Destroy(croppedFiringLoop);
        croppedFiringLoop = AudioClip.Create(firingSound.name + " (Firing Section)",
            count, firingSound.channels, firingSound.frequency, false);
        croppedFiringLoop.SetData(samples, 0);
        cachedFiringSource = firingSound;
        cachedStartSample = start;
        cachedSampleCount = count;
        return croppedFiringLoop;
    }

    void PlayAudio(AudioClip clip, bool loop)
    {
        if (gunAudio == null) return;
        gunAudio.Stop();
        gunAudio.loop = loop;
        gunAudio.clip = clip;
        if (clip != null) gunAudio.Play();
    }

    void StopShooting(bool playPowerDown)
    {
        if (!IsBusy) return;
        phase = GunPhase.Idle;
        waitForRelease = true;
        nextAllowedAt = Time.time + Mathf.Max(0f, cooldown);
        if (playerMovement != null && playerMovement.ExclusiveMovementActive) playerMovement.UnlockMovement();
        if (animator != null && animator.isActiveAndEnabled)
            animator.CrossFadeInFixedTime(playerMovement != null && playerMovement.IsLockedOn ? lockedHash : freeHash,
                Mathf.Max(0f, blendTime), 0, 0f);
        PlayAudio(playPowerDown ? powerDownSound : null, false);
    }

    public void Cancel()
    {
        StopShooting(false);
    }

    void OnDisable()
    {
        Cancel();
        if (gunAudio != null) gunAudio.Stop();
        if (spinTarget != null)
            spinTarget.localRotation = spinTargetIsAnimated && appliedAnimatedSpin ? lastAnimatedBase : baseSpinRotation;
        appliedAnimatedSpin = false;
        spinSpeed = 0f;
        spinAngle = 0f;
    }

    void OnDestroy()
    {
        if (croppedFiringLoop != null) Destroy(croppedFiringLoop);
    }
}