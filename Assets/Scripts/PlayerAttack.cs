using UnityEngine;
using UnityEngine.InputSystem;

// Attach to Player; assign the Animator on the cow model.
// Controls attack animations only, not movement or damage.
// Early presses are ignored; valid presses interrupt immediately (no input queue).
public class PlayerCombat : MonoBehaviour
{
    [System.Serializable]
    public class Attack
    {
        public string stateName;
        [Tooltip("Seconds from this attack starting when the combo expires. Y then restarts Attack1, without automatically stopping this animation.")]
        [Min(0f)] public float comboResetTime = 1f;
        [Min(0.05f)] public float speedMultiplier = 1f;

        public Attack(string name) { stateName = name; }
    }

    [Header("Animator on the cow")]
    public Animator animator;
    public string idleState = "Idle";

    [Header("Combo tuning")]
    [Tooltip("Minimum game seconds between accepted Y presses. Holding Y does not repeat.")]
    [Min(0f)] public float pressCooldown = 0.5f;
    [Tooltip("Playback speed for all attacks. Does not affect Idle or the timers.")]
    [Min(0.05f)] public float attackSpeed = 1f;
    [Tooltip("Blend time in seconds. Keep short for crisp attacks.")]
    [Min(0f)] public float transitionTime = 0.06f;
    public Attack[] attacks =
    {
        new Attack("Attack1"), new Attack("Attack2"),
        new Attack("Attack3"), new Attack("Attack4")
    };

    public bool IsAttacking => currentAttack >= 0;
    public int CurrentAttackNumber => currentAttack + 1;

    const string SpeedParameter = "AttackSpeed";
    int speedHash;
    int idleHash;
    int[] attackHashes;
    int currentAttack = -1;
    float elapsed;
    float nextPressTime;
    int attackStartedFrame;

    void Start()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Fail("Assign the cow's Animator and its Animator Controller.");
            return;
        }
        if (attacks == null || attacks.Length != 4)
        {
            Fail("Set Attacks to exactly four entries.");
            return;
        }
        speedHash = Animator.StringToHash(SpeedParameter);
        bool hasSpeed = false;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.nameHash == speedHash && parameter.type == AnimatorControllerParameterType.Float)
                hasSpeed = true;
        if (!hasSpeed)
        {
            Fail("Add a Float Animator parameter named AttackSpeed.");
            return;
        }
        idleHash = StateHash(idleState);
        if (!animator.HasState(0, idleHash))
        {
            Fail("Cannot find the Idle state on the first Animator layer.");
            return;
        }
        attackHashes = new int[attacks.Length];
        for (int i = 0; i < attacks.Length; i++)
        {
            if (attacks[i] == null || string.IsNullOrWhiteSpace(attacks[i].stateName))
            {
                Fail("Every attack needs an Animator state name.");
                return;
            }
            attackHashes[i] = StateHash(attacks[i].stateName);
            if (!animator.HasState(0, attackHashes[i]))
            {
                Fail("Cannot find Animator state: " + attacks[i].stateName);
                return;
            }
        }
        animator.SetFloat(speedHash, 1f);
    }

    int StateHash(string name)
    {
        return Animator.StringToHash(animator.GetLayerName(0) + "." + name);
    }

    void Update()
    {
        if (Time.deltaTime <= 0f || animator == null || !animator.isActiveAndEnabled) return;

        if (IsAttacking)
        {
            elapsed += Time.deltaTime;
            Attack attack = attacks[currentAttack];
            animator.SetFloat(speedHash, Mathf.Max(0.05f, attackSpeed)
                * Mathf.Max(0.05f, attack.speedMultiplier));

            // End only after the expected animation actually reaches its end.
            // Do not inspect the outgoing state while blending into a new attack.
            if (Time.frameCount > attackStartedFrame && !animator.IsInTransition(0))
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.fullPathHash == attackHashes[currentAttack] && state.normalizedTime >= 1f)
                    FinishCombo();
            }
        }

        // Input is processed AFTER completion checks, so a freshly restarted
        // Attack1 cannot be mistaken for the previous Attack1's completed state.
        bool pressed = (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame)
            || (Keyboard.current != null && Keyboard.current.yKey.wasPressedThisFrame);
        if (pressed) RequestAttack();
    }

    void RequestAttack()
    {
        // This protects every attack, including restarts after the combo expires.
        if (Time.time < nextPressTime) return;

        int nextIndex = 0;
        if (IsAttacking)
        {
            Attack attack = attacks[currentAttack];
            // At the reset boundary or after playback ends, the next press is Attack1.
            bool insideChainWindow = elapsed < Mathf.Max(0f, attack.comboResetTime);
            if (insideChainWindow)
                nextIndex = (currentAttack + 1) % attacks.Length;
        }
        BeginAttack(nextIndex);
    }

    void BeginAttack(int index)
    {
        currentAttack = index;
        elapsed = 0f;
        attackStartedFrame = Time.frameCount;
        nextPressTime = Time.time + Mathf.Max(0f, pressCooldown);
        animator.SetFloat(speedHash, Mathf.Max(0.05f, attackSpeed)
            * Mathf.Max(0.05f, attacks[index].speedMultiplier));
        animator.CrossFadeInFixedTime(attackHashes[index], Mathf.Max(0f, transitionTime), 0, 0f);
    }

    void FinishCombo()
    {
        currentAttack = -1;
        animator.CrossFadeInFixedTime(idleHash, Mathf.Max(0f, transitionTime), 0, 0f);
    }

    void OnDisable()
    {
        if (IsAttacking && animator != null && animator.isActiveAndEnabled)
            animator.CrossFadeInFixedTime(idleHash, Mathf.Max(0f, transitionTime), 0, 0f);
        currentAttack = -1;
        nextPressTime = 0f;
    }

    void Fail(string message)
    {
        Debug.LogError("PlayerCombat: " + message, this);
        enabled = false;
    }
}
