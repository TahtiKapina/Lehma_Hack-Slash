using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class EnemyController : MonoBehaviour
{
    [Header("Player")]
    public Transform player;

    [Header("Movement")]
    public float moveSpeed = 3f;
    public float detectionRange = 10f;
    public float attackRange = 2f;

    [Header("Obstacle Avoidance")]
    public float obstacleCheckDistance = 1.5f;
    public float sideCheckAngle = 60f;

    [Header("Attack")]
    public int damage = 10;
    public float attackPreparationTime = 3f;
    public float attackCooldown = 5f;

    private bool isAttacking = false;
    private bool canAttack = true;

    private PlayerHealth playerHealth;
    private Rigidbody rb;
    private Collider enemyCollider;
    private EnemyHitReaction hitReaction;
    private Coroutine attackRoutine;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        enemyCollider = GetComponent<Collider>();
        hitReaction = GetComponent<EnemyHitReaction>();

        // Enemy ei kaadu
        rb.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;

        if (player == null)
        {
            GameObject playerObject =
                GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        if (player != null)
        {
            playerHealth = player.GetComponent<PlayerHealth>();

            // Player ja Enemy eivät työnnä toisiaan
            Collider playerCollider =
                player.GetComponent<Collider>();

            if (playerCollider != null)
            {
                Physics.IgnoreCollision(
                    enemyCollider,
                    playerCollider
                );
            }
        }
    }

    private void FixedUpdate()
    {
        if (hitReaction != null && hitReaction.IsReacting) return;

        if (player == null)
            return;

        Vector3 direction =
            player.position - transform.position;

        direction.y = 0f;

        float distance = direction.magnitude;

        // Pelaaja liian kaukana
        if (distance > detectionRange)
            return;

        // Hyökkäysalueella
        if (distance <= attackRange)
        {
            if (!isAttacking && canAttack)
            {
                attackRoutine = StartCoroutine(Attack());
            }

            return;
        }

        if (!isAttacking)
        {
            MoveEnemy(direction.normalized);
        }
    }

    private void MoveEnemy(Vector3 direction)
    {
        Vector3 moveDirection = direction;

        // Tarkista suoraan eteen
        if (IsBlocked(direction))
        {
            // Tarkista oikea
            Vector3 right =
                Quaternion.Euler(
                    0f,
                    sideCheckAngle,
                    0f
                ) * direction;

            // Tarkista vasen
            Vector3 left =
                Quaternion.Euler(
                    0f,
                    -sideCheckAngle,
                    0f
                ) * direction;

            bool rightBlocked = IsBlocked(right);
            bool leftBlocked = IsBlocked(left);

            if (!rightBlocked)
            {
                moveDirection = right;
            }
            else if (!leftBlocked)
            {
                moveDirection = left;
            }
            else
            {
                // Ei voi liikkua eteenpäin
                moveDirection = Vector3.zero;
            }
        }

        if (moveDirection == Vector3.zero)
            return;

        moveDirection.y = 0f;
        moveDirection.Normalize();

        // Liikkuu vain jos koko Colliderille on tilaa
        Vector3 movement =
            moveDirection *
            moveSpeed *
            Time.fixedDeltaTime;

        rb.MovePosition(rb.position + movement);

        // Kääntyminen
        Quaternion targetRotation =
            Quaternion.LookRotation(moveDirection);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                targetRotation,
                10f * Time.fixedDeltaTime
            )
        );
    }

    private bool IsBlocked(Vector3 direction)
    {
        direction.y = 0f;
        direction.Normalize();

        Vector3 center = enemyCollider.bounds.center;

        float radius =
            Mathf.Min(
                enemyCollider.bounds.extents.x,
                enemyCollider.bounds.extents.z
            );

        float distance =
            obstacleCheckDistance + radius;

        // Tarkistaa koko Enemyn alueen
        return Physics.SphereCast(
            center,
            radius * 0.9f,
            direction,
            out RaycastHit hit,
            distance
        );
    }

    public void InterruptAttack()
    {
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }
        isAttacking = false;
        canAttack = true;
    }

    private void OnDisable()
    {
        InterruptAttack();
    }

    private IEnumerator Attack()
    {
        isAttacking = true;
        canAttack = false;

        Debug.Log("Enemy valmistautuu hyökkäykseen!");

        // Pelaajalla 3 sekuntia aikaa väistää
        yield return new WaitForSeconds(
            attackPreparationTime
        );

        if (player != null && playerHealth != null)
        {
            Vector3 direction =
                player.position - transform.position;

            direction.y = 0f;

            float distance =
                direction.magnitude;

            if (distance <= attackRange)
            {
                playerHealth.TakeDamage(damage);

                Debug.Log("Enemy osui pelaajaan!");
            }
        }

        isAttacking = false;

        // 3 sekuntia seuraavaan hyökkäykseen
        yield return new WaitForSeconds(
            attackCooldown
        );

        canAttack = true;
        attackRoutine = null;
    }
}