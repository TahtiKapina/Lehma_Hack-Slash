using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// Send attack movement changes before PlayerMovement.Update runs.
[DefaultExecutionOrder(-100)]
public class PlayerCombat : MonoBehaviour
{
    [System.Serializable]
    public class Attack
    {
        [Header("Animation")]
        public string stateName;

        [Min(0f)]
        public float comboResetTime = 1f;

        [Min(0.05f)]
        public float speedMultiplier = 1f;


        [Header("Movement")]
        [Tooltip("Peak push speed at 1x attack speed. The push eases in and out.")]
        [Min(0f)]
        public float forwardPushSpeed = 2f;

        [Tooltip("Seconds before the push, measured at 1x attack speed.")]
        [Min(0f)]
        public float forwardPushDelay = 0.04f;

        [Tooltip("Push duration at 1x attack speed. Does not include recovery.")]
        [Min(0.01f)]
        public float forwardPushDuration = 0.35f;

        [Header("Damage")]
        [Min(0)] public int damage = 3;
        public bool isHeavy;
        [Range(0f, 1f)] public float hitStartNormalized = 0.15f;
        [Range(0f, 1f)] public float hitEndNormalized = 0.65f;
        [Min(0f)] public float hitStun = 0.3f;
        [Tooltip("1 matches the full forward-push distance of this swing.")]
        [Min(0f)] public float knockbackMultiplier = 1f;


        [Header("Sound")]
        public AudioClip swingSound;

        [Min(0f)]
        public float swingSoundDelay = 0.1f;

        public float swingPitch = 1f;

        [Range(0f, 1f)]
        public float swingVolume = 1f;


        public Attack(string name)
        {
            stateName = name;
            isHeavy = name == "Attack4";
        }
    }


    [Header("References")]
    public Animator animator;
    public PlayerMovement playerMovement;
    public AudioSource swordAudio;
    public PlayerStinger stinger;
    public PlayerUdderGun udderGun;


    [Header("Animator")]
    public string idleState = "Idle";


    [Header("Combo")]
    [Min(0f)]
    public float pressCooldown = 0.5f;

    [Min(0.05f)]
    public float attackSpeed = 1f;

    [Min(0f)]
    public float transitionTime = 0.06f;


    [Header("Attacks")]
    public Attack[] attacks =
    {
        new Attack("Attack1"),
        new Attack("Attack2"),
        new Attack("Attack3"),
        new Attack("Attack4")
    };


    public bool IsAttacking
    {
        get
        {
            return currentAttack >= 0 || (stinger != null && stinger.IsActive)
                || (udderGun != null && udderGun.IsBusy);
        }
    }


    public int CurrentAttackNumber
    {
        get
        {
            return currentAttack + 1;
        }
    }


    const string SpeedParameter = "AttackSpeed";

    int speedHash;
    int idleHash;

    int[] attackHashes;

    int currentAttack = -1;
    int swingId;
    int attackStartedFrame;

    float elapsed;
    float nextPressTime;


    void Start()
    {
        if (playerMovement == null)
        {
            // Combat can live on the animated child while movement lives
            // on the parent containing the CharacterController.
            playerMovement = GetComponentInParent<PlayerMovement>();
        }

        if (playerMovement == null)
        {
            Fail(
                "Could not find PlayerMovement on this object or its parents. " +
                "Assign the moving player's PlayerMovement component in References."
            );
            return;
        }

        SetupAnimator();

        if (stinger == null)
            stinger = playerMovement.GetComponentInChildren<PlayerStinger>();
        if (udderGun == null)
            udderGun = playerMovement.GetComponentInChildren<PlayerUdderGun>();
    }


    void Update()
    {
        if (udderGun != null && udderGun.IsBusy) return;
        if (stinger != null && stinger.IsActive)
        {
            if (stinger.CanInterrupt) HandleAttackInput();
            return;
        }

        if (animator == null)
        {
            return;
        }

        if (!animator.isActiveAndEnabled)
        {
            return;
        }


        if (IsAttacking)
        {
            UpdateAttack();
        }


        HandleAttackInput();
    }


    void HandleAttackInput()
    {
        bool attackPressed = false;


        // Xbox Y button
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonNorth.wasPressedThisFrame)
            {
                attackPressed = true;
            }
        }


        // Keyboard Y
        if (Keyboard.current != null)
        {
            if (Keyboard.current.yKey.wasPressedThisFrame)
            {
                attackPressed = true;
            }
        }


        if (attackPressed)
        {
            RequestAttack();
        }
    }


    void RequestAttack()
    {
        if (udderGun != null && udderGun.IsBusy) return;
        bool interruptedStinger = false;
        if (stinger != null && stinger.IsActive)
        {
            if (!stinger.CanInterrupt) return;
            stinger.Cancel();
            interruptedStinger = true;
        }
        // Stinger can interrupt a slash during its normal input cooldown.
        // Interrupting its recovery starts a slash instead of having the same
        // press consumed by Stinger's post-finish cooldown.
        if (!interruptedStinger && stinger != null && stinger.TryHandleAttackInput(this)) return;
        if (playerMovement.MovementLocked && !playerMovement.IsAttackMovementActive) return;

        // Prevent attacks from being spammed too quickly.
        if (Time.time < nextPressTime)
        {
            return;
        }


        int nextAttack = 0;


        if (IsAttacking)
        {
            Attack attack = attacks[currentAttack];

            bool comboStillActive =
                elapsed < Mathf.Max(0f, attack.comboResetTime);


            if (comboStillActive)
            {
                nextAttack = currentAttack + 1;


                // After Attack 4, loop back to Attack 1.
                if (nextAttack >= attacks.Length)
                {
                    nextAttack = 0;
                }
            }
        }


        BeginAttack(nextAttack);
    }


    void BeginAttack(int attackIndex)
    {
        swingId++;
        currentAttack = attackIndex;

        elapsed = 0f;

        attackStartedFrame = Time.frameCount;

        nextPressTime =
            Time.time + Mathf.Max(0f, pressCooldown);


        Attack attack = attacks[attackIndex];


        float animationSpeed =
            attackSpeed * attack.speedMultiplier;

        animationSpeed =
            Mathf.Max(0.05f, animationSpeed);


        animator.SetFloat(
            speedHash,
            animationSpeed
        );


        animator.CrossFadeInFixedTime(
            attackHashes[attackIndex],
            Mathf.Max(0f, transitionTime),
            0,
            0f
        );


        // Start one smooth push for this swing, with restricted steering.
        if (playerMovement != null)
        {
            playerMovement.BeginAttackMovement(
                attack.forwardPushSpeed,
                attack.forwardPushDelay,
                attack.forwardPushDuration,
                animationSpeed
            );
        }


        StartCoroutine(
            PlaySwingSound(attackIndex)
        );
    }


    void UpdateAttack()
    {
        elapsed += Time.deltaTime;


        Attack attack = attacks[currentAttack];


        float animationSpeed =
            attackSpeed * attack.speedMultiplier;

        animationSpeed =
            Mathf.Max(0.05f, animationSpeed);


        animator.SetFloat(
            speedHash,
            animationSpeed
        );


        // Keep push timing aligned when the attack speed changes.
        // PlayerMovement owns rotation, the push, and gravity.
        if (playerMovement != null)
        {
            playerMovement.SetAttackMovementSpeed(animationSpeed);
        }


        // Don't check for animation completion
        // on the exact frame it started.
        if (Time.frameCount <= attackStartedFrame)
        {
            return;
        }


        // Don't check the outgoing animation
        // while transitioning between attacks.
        if (animator.IsInTransition(0))
        {
            return;
        }


        AnimatorStateInfo state =
            animator.GetCurrentAnimatorStateInfo(0);


        bool correctAttack =
            state.fullPathHash ==
            attackHashes[currentAttack];


        bool animationFinished =
            state.normalizedTime >= 1f;


        if (correctAttack && animationFinished)
        {
            FinishCombo();
        }
    }


    // SwordHitbox samples the animated state after the Animator has updated.
    public bool TryGetSwordAttack(out Attack attack, out int id, out float normalizedTime)
    {
        attack = null;
        id = swingId;
        normalizedTime = 0f;
        if (!isActiveAndEnabled || currentAttack < 0 || animator == null ||
            (udderGun != null && udderGun.IsBusy) ||
            !animator.isActiveAndEnabled || (stinger != null && stinger.IsActive)) return false;

        AnimatorStateInfo state = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0)
            : animator.GetCurrentAnimatorStateInfo(0);
        if (state.fullPathHash != attackHashes[currentAttack]) return false;
        attack = attacks[currentAttack];
        normalizedTime = state.normalizedTime;
        return true;
    }


    public void InterruptComboForStinger()
    {
        InterruptComboForSpecialMove();
    }

    public void InterruptComboForSpecialMove()
    {
        StopAllCoroutines();
        currentAttack = -1;
        elapsed = 0f;
        nextPressTime = 0f;
        playerMovement.UnlockMovement();
    }


    IEnumerator PlaySwingSound(int attackIndex)
    {
        Attack attack =
            attacks[attackIndex];


        float animationSpeed =
            attackSpeed * attack.speedMultiplier;

        animationSpeed =
            Mathf.Max(0.05f, animationSpeed);


        // Adjust sound timing automatically
        // when animation speed changes.
        float delay =
            attack.swingSoundDelay / animationSpeed;


        yield return new WaitForSeconds(delay);


        // Don't play the old swing sound if another
        // attack has already interrupted this one.
        if (currentAttack != attackIndex)
        {
            yield break;
        }


        if (swordAudio == null)
        {
            yield break;
        }


        if (attack.swingSound == null)
        {
            yield break;
        }


        swordAudio.pitch =
            attack.swingPitch;


        swordAudio.PlayOneShot(
            attack.swingSound,
            attack.swingVolume
        );
    }


    void FinishCombo()
    {
        currentAttack = -1;


        // Give movement back to the player.
        if (playerMovement != null)
        {
            playerMovement.UnlockMovement();
        }


        animator.CrossFadeInFixedTime(
            idleHash,
            Mathf.Max(0f, transitionTime),
            0,
            0f
        );
    }


    void SetupAnimator()
    {
        if (animator == null)
        {
            animator =
                GetComponentInChildren<Animator>();
        }


        if (animator == null)
        {
            Fail(
                "Could not find the cow Animator."
            );

            return;
        }


        if (animator.runtimeAnimatorController == null)
        {
            Fail(
                "The Animator does not have an Animator Controller."
            );

            return;
        }


        if (attacks == null || attacks.Length != 4)
        {
            Fail(
                "Set Attacks to exactly four entries."
            );

            return;
        }


        speedHash =
            Animator.StringToHash(
                SpeedParameter
            );


        bool hasAttackSpeed = false;


        foreach (
            AnimatorControllerParameter parameter
            in animator.parameters
        )
        {
            if (
                parameter.nameHash == speedHash &&
                parameter.type ==
                AnimatorControllerParameterType.Float
            )
            {
                hasAttackSpeed = true;

                break;
            }
        }


        if (!hasAttackSpeed)
        {
            Fail(
                "Add a Float Animator parameter called AttackSpeed."
            );

            return;
        }


        idleHash =
            GetStateHash(idleState);


        if (!animator.HasState(0, idleHash))
        {
            Fail(
                "Could not find Animator state: " +
                idleState
            );

            return;
        }


        attackHashes =
            new int[attacks.Length];


        for (
            int i = 0;
            i < attacks.Length;
            i++
        )
        {
            if (attacks[i] == null)
            {
                Fail(
                    "Attack " +
                    (i + 1) +
                    " is missing."
                );

                return;
            }


            if (
                string.IsNullOrWhiteSpace(
                    attacks[i].stateName
                )
            )
            {
                Fail(
                    "Attack " +
                    (i + 1) +
                    " needs a state name."
                );

                return;
            }


            attackHashes[i] =
                GetStateHash(
                    attacks[i].stateName
                );


            if (
                !animator.HasState(
                    0,
                    attackHashes[i]
                )
            )
            {
                Fail(
                    "Could not find Animator state: " +
                    attacks[i].stateName
                );

                return;
            }
        }


        animator.SetFloat(
            speedHash,
            1f
        );
    }


    int GetStateHash(string stateName)
    {
        string layerName =
            animator.GetLayerName(0);


        return Animator.StringToHash(
            layerName +
            "." +
            stateName
        );
    }


    void OnDisable()
    {
        if (udderGun != null) udderGun.Cancel();
        if (stinger != null) stinger.Cancel();

        StopAllCoroutines();


        if (playerMovement != null)
        {
            playerMovement.UnlockMovement();
        }


        if (
            IsAttacking &&
            animator != null &&
            animator.isActiveAndEnabled
        )
        {
            animator.CrossFadeInFixedTime(
                idleHash,
                Mathf.Max(
                    0f,
                    transitionTime
                ),
                0,
                0f
            );
        }


        currentAttack = -1;

        nextPressTime = 0f;
    }


    void Fail(string message)
    {
        Debug.LogError(
            "PlayerCombat: " +
            message,
            this
        );


        enabled = false;
    }
}