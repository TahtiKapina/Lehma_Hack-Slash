using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttackTest : MonoBehaviour
{
    [Header("Combo Configuration")]
    public int maxComboSteps = 4;
    public float comboResetWindow = 1.0f;
    public float attackMovementLockDuration = 0.35f;

    [Header("Air Combat Tuning")]
    public float airAttackGravityPauseTime = 0.25f;

    [Header("Hitbox & Damage Settings")]
    public Transform attackPoint;
    public float attackRadius = 1.5f;
    public LayerMask enemyLayers;
    public float baseDamage = 20f;

    private PlayerMovement movementScript;
    private CharacterController controller;
    private Animator animator;

    private int comboStep = 0;
    private float lastAttackTime = 0f;
    private float lockTimer = 0f;
    private bool isAttacking = false;

    private void Start()
    {
        movementScript = GetComponent<PlayerMovement>();
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();

        if (attackPoint == null)
        {
            GameObject point = new GameObject("AttackPoint");
            point.transform.SetParent(transform);
            point.transform.localPosition = new Vector3(0f, 1f, 1.2f);
            attackPoint = point.transform;
        }
    }

    private void Update()
    {
        if (Time.time - lastAttackTime > comboResetWindow && comboStep > 0)
        {
            comboStep = 0;
        }

        if (isAttacking)
        {
            lockTimer -= Time.deltaTime;
            if (lockTimer <= 0f)
            {
                isAttacking = false;
                if (movementScript != null) movementScript.canMove = true;
            }
        }

        HandleAttackInput();
    }

    private void HandleAttackInput()
    {
        bool attackPressed = false;

        if (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame)
        {
            attackPressed = true;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            attackPressed = true;
        }

        if (Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame)
        {
            attackPressed = true;
        }

        if (attackPressed && !isAttacking)
        {
            ExecuteAttack();
        }
    }

    private void ExecuteAttack()
    {
        isAttacking = true;
        lockTimer = attackMovementLockDuration;
        lastAttackTime = Time.time;

        comboStep++;
        if (comboStep > maxComboSteps)
        {
            comboStep = 1;
        }

        if (movementScript != null)
        {
            movementScript.canMove = false;

            // Reset vertical gravity build-up & suspend fall briefly during air swings
            if (controller != null && !controller.isGrounded)
            {
                movementScript.PauseGravity(airAttackGravityPauseTime);
            }
        }

        Debug.Log($"<b>LEHMÄ ATTACK!</b> Combo Hit: #{comboStep} / {maxComboSteps}");

        if (animator != null)
        {
            animator.SetTrigger($"Attack{comboStep}");
        }

        DealDamageInHitbox();
    }

    private void DealDamageInHitbox()
    {
        Collider[] hitEnemies = Physics.OverlapSphere(attackPoint.position, attackRadius, enemyLayers);

        foreach (Collider enemy in hitEnemies)
        {
            float currentDamage = baseDamage * comboStep;
            Debug.Log($"Hit enemy: {enemy.name} for {currentDamage} damage!");
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRadius);
    }
}