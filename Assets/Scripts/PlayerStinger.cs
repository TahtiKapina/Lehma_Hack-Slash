using UnityEngine;
using UnityEngine.Events;

// Combat (-100) chooses the move; movement (0) updates gravity;
// Stinger (10) applies the only controller displacement for this frame.
[DefaultExecutionOrder(10)]
public class PlayerStinger : MonoBehaviour
{
    [Header("References")]
    public PlayerMovement playerMovement;
    public PlayerCombat combat;
    public Animator animator;

    [Header("Input: lock-on + toward enemy + attack")]
    [Range(0.1f, 1f)] public float minimumStickAmount = 0.5f;
    [Range(0f, 90f)] public float inputConeAngle = 45f;
    [Range(0f, 180f)] public float facingConeAngle = 70f;

    [Header("Travel")]
    [Min(0.01f)] public float travelDistance = 8f;
    [Min(0.01f)] public float thrustSpeed = 30f;
    [Min(0f)] public float cooldownAfterFinish = 0.35f;

    [Header("On-hit recovery")]
    [Tooltip("Seconds of normal attack walking/turning restrictions after hitting an enemy. Attacks, jumps and dashes are blocked during this time.")]
    [Min(0f)] public float hitSlowDuration = 1f;

    [Header("Damage")]
    [Min(0)] public int damage = 5;
    public bool isHeavy = true;
    [Min(0f)] public float hitStun = 0.4f;
    [Tooltip("Used only when Is Heavy is off. Heavy launch is tuned on EnemyHitReaction.")]
    [Min(0f)] public float lightKnockbackDistance = 0.75f;

    [Header("Animation (base layer)")]
    public string stateName = "Stinger";
    [Tooltip("Fraction of the animation at which to hold the thrust pose. 0.35 = 35%.")]
    [Range(0f, 0.99f)] public float holdNormalizedTime = 0.35f;
    [Min(0f)] public float exitBlendTime = 0.06f;

    [Header("Thrust hitbox (world units, relative to player facing)")]
    public Vector3 hitboxOffset = new Vector3(0f, 0.9f, 1f);
    public Vector3 hitboxSize = new Vector3(0.6f, 0.6f, 1f);
    [Tooltip("Layers containing enemy colliders, including child hurtboxes.")]
    public LayerMask enemyLayers = ~0;
    [Tooltip("Enemy root or one of the collider's parents must use this tag.")]
    public string enemyTag = "Enemy";
    [Tooltip("Environment layers that stop the thrust. Exclude Player. Triggers are ignored as obstacles.")]
    public LayerMask obstacleLayers = ~0;

    [System.Serializable]
    public class EnemyHitEvent : UnityEvent<GameObject> { }

    [Header("Optional hit hook (no damage system assumed)")]
    public EnemyHitEvent onEnemyHit = new EnemyHitEvent();

    public bool IsActive { get; private set; }
    public bool IsRecovering { get; private set; }
    public bool CanInterrupt => IsActive && IsRecovering && Time.time >= recoveryEndsAt;

    float recoveryEndsAt;
    PlayerMovement subscribedMovement;

    CharacterController controller;
    Vector3 direction;
    Quaternion facing;
    float distanceRemaining;
    float nextAllowedTime;
    float savedAnimatorSpeed;
    bool holdingPose;
    int stateHash;
    int exitHash;

    void Start()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
    }

    void ResolveReferences()
    {
        if (playerMovement == null) playerMovement = GetComponentInParent<PlayerMovement>();
        if (playerMovement == null) return;
        if (combat == null) combat = playerMovement.GetComponentInChildren<PlayerCombat>();
        if (animator == null && combat != null) animator = combat.animator;
        controller = playerMovement.GetComponent<CharacterController>();
        if (combat != null && combat.stinger == null) combat.stinger = this;
        if (subscribedMovement != playerMovement)
        {
            if (subscribedMovement != null) subscribedMovement.DashStarted -= OnDashStarted;
            subscribedMovement = playerMovement;
            subscribedMovement.DashStarted += OnDashStarted;
        }
    }

    // True consumes a Stinger command even if cooldown rejects it, avoiding
    // an accidental normal slash when the intended special move is unavailable.
    public bool TryHandleAttackInput(PlayerCombat source)
    {
        if (!isActiveAndEnabled) return false;
        if (combat == null) combat = source;
        ResolveReferences();
        if (playerMovement == null || combat != source) return false;
        if (IsActive) return true;

        Transform target = playerMovement.lockOnCamera != null
            ? playerMovement.lockOnCamera.LockedTarget : null;
        if (target == null || !target.gameObject.activeInHierarchy) return false;

        Vector3 towardEnemy = target.position - playerMovement.transform.position;
        towardEnemy.y = 0f;
        if (towardEnemy.sqrMagnitude < 0.0001f) return false;
        towardEnemy.Normalize();

        Vector3 input = playerMovement.ReadWorldMovementInput();
        if (input.magnitude < minimumStickAmount ||
            Vector3.Angle(input, towardEnemy) > inputConeAngle ||
            Vector3.Angle(playerMovement.transform.forward, towardEnemy) > facingConeAngle)
            return false;

        if (Time.time < nextAllowedTime || !playerMovement.isActiveAndEnabled ||
            controller == null || !controller.enabled ||
            (playerMovement.MovementLocked && !playerMovement.IsAttackMovementActive))
            return true;

        if (animator == null || !animator.isActiveAndEnabled ||
            animator.runtimeAnimatorController == null)
        {
            Debug.LogError("PlayerStinger: assign the combat Animator.", this);
            return true;
        }

        string layer = animator.GetLayerName(0) + ".";
        if (string.IsNullOrWhiteSpace(stateName))
        {
            Debug.LogError("PlayerStinger: set the Stinger animation state name.", this);
            return true;
        }
        stateHash = Animator.StringToHash(stateName.StartsWith(layer) ? stateName : layer + stateName);
        exitHash = Animator.StringToHash(layer + combat.idleState);
        if (!animator.HasState(0, stateHash) || !animator.HasState(0, exitHash))
        {
            Debug.LogError("PlayerStinger: check State Name and the combat Idle State in the Animator base layer.", this);
            return true;
        }

        combat.InterruptComboForStinger();
        playerMovement.BeginExclusiveMovement();
        direction = towardEnemy;
        facing = Quaternion.LookRotation(direction);
        playerMovement.transform.rotation = facing;
        distanceRemaining = Mathf.Max(0.01f, travelDistance);
        savedAnimatorSpeed = animator.speed;
        holdingPose = false;
        IsRecovering = false;
        IsActive = true;

        // Dedicated state: no AttackSpeed multiplier on this Animator state.
        animator.Play(stateHash, 0, 0f);
        return true;
    }

    void Update()
    {
        if (!IsActive) return;
        if (playerMovement == null || !playerMovement.isActiveAndEnabled ||
            (!IsRecovering && !playerMovement.ExclusiveMovementActive) || controller == null || !controller.enabled ||
            animator == null || !animator.isActiveAndEnabled || combat == null || !combat.isActiveAndEnabled)
        {
            Cancel();
            return;
        }
        if (Time.deltaTime <= 0f) return;
        // Movement owns recovery walking and rotation. No thrust or further
        // hit queries run after the first successful connection.
        if (IsRecovering) return;

        // Commit to launch direction. Losing lock-on does not cancel the move.
        playerMovement.transform.rotation = facing;
        Vector3 origin = playerMovement.transform.position;
        Vector3 center = origin + facing * hitboxOffset;
        Vector3 halfSize = HalfSize();
        float step = Mathf.Min(Mathf.Max(0.01f, thrustSpeed) * Time.deltaTime, distanceRemaining);
        float allowedStep = step;
        GameObject enemy = null;
        bool contact = false;
        int mask = enemyLayers.value | obstacleLayers.value;

        // Casts need a separate initial-overlap check.
        foreach (Collider collider in Physics.OverlapBox(center, halfSize, facing, mask, QueryTriggerInteraction.Collide))
        {
            if (IsSelf(collider)) continue;
            GameObject candidate = FindEnemy(collider);
            if (candidate != null || IsObstacle(collider))
            {
                allowedStep = 0f;
                contact = true;
                enemy = candidate;
                if (candidate == null) break;
            }
        }

        if (!contact)
        {
            // Sweep the whole step so fast travel cannot skip thin hurtboxes.
            foreach (RaycastHit hit in Physics.BoxCastAll(center, halfSize, direction, facing,
                step, mask, QueryTriggerInteraction.Collide))
            {
                if (IsSelf(hit.collider)) continue;
                GameObject candidate = FindEnemy(hit.collider);
                if (candidate == null && !IsObstacle(hit.collider)) continue;
                if (hit.distance > allowedStep) continue;
                if (contact && Mathf.Approximately(hit.distance, allowedStep) && enemy == null) continue;
                allowedStep = Mathf.Max(0f, hit.distance);
                enemy = candidate;
                contact = true;
            }
        }

        CollisionFlags flags = playerMovement.MoveExclusive(direction * allowedStep);
        float actualTravel = Mathf.Max(0f,
            Vector3.Dot(playerMovement.transform.position - origin, direction));
        distanceRemaining = Mathf.Max(0f, distanceRemaining - actualTravel);

        // The player's body can meet an obstacle before its small thrust box.
        bool reachedContact = contact && actualTravel + 0.01f >= allowedStep;
        if (reachedContact)
        {
            if (enemy != null)
            {
                BeginHitRecovery();
                EnemyHealth health = enemy.GetComponent<EnemyHealth>();
                if (health == null) health = enemy.GetComponentInParent<EnemyHealth>();
                if (health == null) health = enemy.GetComponentInChildren<EnemyHealth>();
                if (health != null)
                    health.ReceiveHit(damage, origin, center, lightKnockbackDistance, hitStun, isHeavy);
                onEnemyHit.Invoke(enemy);
            }
            else Finish();
        }
        else if (distanceRemaining <= 0.001f ||
            (flags & CollisionFlags.Sides) != 0 || actualTravel < step * 0.1f)
        {
            Finish();
        }
    }

    void LateUpdate()
    {
        if (!IsActive || animator == null || !animator.isActiveAndEnabled) return;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.fullPathHash != stateHash || animator.IsInTransition(0))
        {
            Debug.LogWarning("PlayerStinger: animation was interrupted. Remove automatic exits from Stinger and make animation scripts respect combat.IsAttacking.", this);
            Cancel();
            return;
        }

        if (IsRecovering)
        {
            // A non-looping clip keeps its final pose if it finishes before
            // the slow window. Do not release the restriction early.
            if (CanInterrupt && state.normalizedTime >= 1f) Finish();
            return;
        }

        float hold = Mathf.Clamp(holdNormalizedTime, 0f, 0.99f);
        if (!holdingPose && state.normalizedTime >= hold)
        {
            holdingPose = true;
            animator.speed = 0f;
            animator.Play(stateHash, 0, hold);
            animator.Update(0f);
        }
    }

    Vector3 HalfSize()
    {
        return new Vector3(Mathf.Max(0.01f, hitboxSize.x),
            Mathf.Max(0.01f, hitboxSize.y), Mathf.Max(0.01f, hitboxSize.z)) * 0.5f;
    }

    void BeginHitRecovery()
    {
        IsRecovering = true;
        holdingPose = false;
        recoveryEndsAt = Time.time + Mathf.Max(0f, hitSlowDuration);
        animator.speed = savedAnimatorSpeed;
        // Continue the existing animation from its current pose, without
        // restarting it or crossfading to idle.
        playerMovement.BeginStingerRecovery(recoveryEndsAt);
    }

    void OnDashStarted()
    {
        // Fires only for an eligible normal dash; rejected inputs do not cancel.
        if (CanInterrupt) Finish();
    }

    bool IsSelf(Collider collider)
    {
        return collider.transform == playerMovement.transform ||
            collider.transform.IsChildOf(playerMovement.transform);
    }

    GameObject FindEnemy(Collider collider)
    {
        if ((enemyLayers.value & (1 << collider.gameObject.layer)) == 0 || string.IsNullOrEmpty(enemyTag))
            return null;
        for (Transform item = collider.transform; item != null; item = item.parent)
        {
            // No exception if the configured tag has not been created yet.
            if (item.tag == enemyTag) return item.gameObject;
        }
        return null;
    }

    bool IsObstacle(Collider collider)
    {
        return !collider.isTrigger &&
            (obstacleLayers.value & (1 << collider.gameObject.layer)) != 0;
    }

    public void Cancel()
    {
        if (IsActive) Finish();
    }

    void Finish()
    {
        if (!IsActive) return;
        IsActive = false;
        IsRecovering = false;
        holdingPose = false;
        nextAllowedTime = Time.time + Mathf.Max(0f, cooldownAfterFinish);
        if (animator != null)
        {
            animator.speed = savedAnimatorSpeed;
            if (animator.isActiveAndEnabled)
                animator.CrossFadeInFixedTime(exitHash, Mathf.Max(0f, exitBlendTime), 0, 0f);
        }
        if (playerMovement != null &&
            (playerMovement.ExclusiveMovementActive || playerMovement.IsStingerRecoveryMovement))
            playerMovement.UnlockMovement();
    }

    void OnDisable()
    {
        if (subscribedMovement != null)
        {
            subscribedMovement.DashStarted -= OnDashStarted;
            subscribedMovement = null;
        }
        Cancel();
    }

    void OnDrawGizmosSelected()
    {
        Transform player = playerMovement != null ? playerMovement.transform : transform;
        Gizmos.color = IsActive ? Color.red : Color.yellow;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(player.position, player.rotation, Vector3.one);
        Gizmos.DrawWireCube(hitboxOffset, HalfSize() * 2f);
        Gizmos.matrix = previous;
    }
}