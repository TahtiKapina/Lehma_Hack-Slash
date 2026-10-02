using UnityEngine;

// Attach to the movement root (Player2). Assign the cow's Animator and combat script.
// Uses the existing PlayerCombat.IsAttacking flag; attack logic stays in PlayerCombat.
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
public class CowMovement : MonoBehaviour
{
    public PlayerMovement movement;
    public Animator animator;
    public PlayerCombat combat;

    [Header("Animator state names on the first layer")]
    public string freeState = "FreeMovement";
    public string lockedState = "LockOnMovement";
    [Min(0f)] public float transitionTime = 0.15f;
    [Min(0f)] public float inputSmoothTime = 0.1f;

    static readonly int MoveX = Animator.StringToHash("MoveX");
    static readonly int MoveZ = Animator.StringToHash("MoveZ");
    static readonly int MoveAmount = Animator.StringToHash("MoveAmount");
    int freeHash, lockedHash;
    int lastRequestedHash;
    bool wasAttacking;

    void Start()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (combat == null) combat = GetComponentInChildren<PlayerCombat>();
        if (movement == null || animator == null || animator.runtimeAnimatorController == null)
        {
            Fail("Assign PlayerMovement and the cow's Animator.");
            return;
        }
        if (combat == null || combat.animator != animator)
        {
            Fail("Assign PlayerCombat and ensure it uses this same Animator.");
            return;
        }
        string prefix = animator.GetLayerName(0) + ".";
        freeHash = Animator.StringToHash(prefix + freeState);
        lockedHash = Animator.StringToHash(prefix + lockedState);
        if (!animator.HasState(0, freeHash) || !animator.HasState(0, lockedHash))
        {
            Fail("Create FreeMovement and LockOnMovement blend-tree states directly on Base Layer.");
            return;
        }
        if (!HasFloat(MoveX) || !HasFloat(MoveZ) || !HasFloat(MoveAmount))
        {
            Fail("Add Float Animator parameters MoveX, MoveZ and MoveAmount.");
            return;
        }
    }

    bool HasFloat(int hash)
    {
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.nameHash == hash && p.type == AnimatorControllerParameterType.Float) return true;
        return false;
    }

    void Update()
    {
        if (Time.deltaTime <= 0f || animator == null || !animator.isActiveAndEnabled) return;
        Vector2 move = movement.AnimationMove;
        float smoothing = Mathf.Max(0f, inputSmoothTime);
        animator.SetFloat(MoveX, move.x, smoothing, Time.deltaTime);
        animator.SetFloat(MoveZ, move.y, smoothing, Time.deltaTime);
        animator.SetFloat(MoveAmount, move.magnitude, smoothing, Time.deltaTime);

        // Keep blend parameters ready, but never interrupt a combat animation.
        if (combat != null && combat.IsAttacking)
        {
            wasAttacking = true;
            return;
        }

        int desired = movement.IsLockedOn ? lockedHash : freeHash;
        if (desired != lastRequestedHash || wasAttacking)
        {
            animator.CrossFadeInFixedTime(desired, Mathf.Max(0f, transitionTime), 0, 0f);
            lastRequestedHash = desired;
            wasAttacking = false;
        }
    }

    void OnEnable()
    {
        lastRequestedHash = 0;
        wasAttacking = false;
    }

    void Fail(string message)
    {
        Debug.LogError("CowLocomotion: " + message, this);
        enabled = false;
    }
}
