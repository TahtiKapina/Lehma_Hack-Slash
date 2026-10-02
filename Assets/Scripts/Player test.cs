using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerGamepadMovement : MonoBehaviour
{
    public float moveSpeed = 5f;

    [Header("Attack")]
    public int damage = 10;
    public float attackRange = 2f;

    private Rigidbody rb;
    private Vector2 moveInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (Gamepad.current != null)
        {
            moveInput = Gamepad.current.leftStick.ReadValue();

            // A-nappi
            if (Gamepad.current.buttonSouth.wasPressedThisFrame)
            {
                Attack();
            }
        }
        else
        {
            moveInput = Vector2.zero;
        }
    }

    private void FixedUpdate()
    {
        rb.linearVelocity = moveInput * moveSpeed;
    }

    private void Attack()
    {
        Collider[] enemies = Physics.OverlapSphere(transform.position, attackRange);

        foreach (Collider enemy in enemies)
        {
            if (enemy.CompareTag("Enemy"))
            {
                EnemyHealth enemyHealth = enemy.GetComponent<EnemyHealth>();

                if (enemyHealth != null)
                {
                    enemyHealth.TakeDamage(damage);
                }
            }
        }
    }
}