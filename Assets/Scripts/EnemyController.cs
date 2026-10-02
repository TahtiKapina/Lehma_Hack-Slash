
using UnityEngine;
using System.Collections;

public class EnemyController : MonoBehaviour
{
    [Header("Player")]
    public Transform player;

    [Header("Movement")]
    public float moveSpeed = 3f;
    public float detectionRange = 10f;
    public float attackRange = 2f;

    [Header("Attack")]
    public int damage = 10;
    public float attackPreparationTime = 3f;
    public float attackCooldown = 5f;

    private bool isAttacking = false;
    private bool canAttack = true;

    private PlayerHealth playerHealth;

    private void Start()
    {
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
        }
    }

    private void Update()
    {
        if (player == null)
            return;

        Vector3 direction = player.position - transform.position;
        direction.y = 0f;

        float distance = direction.magnitude;

        if (distance > detectionRange)
            return;

        // Kääntyy pelaajaa kohti
        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        // Hyökkäysetäisyydellä
        if (distance <= attackRange)
        {
            if (!isAttacking && canAttack)
            {
                StartCoroutine(Attack());
            }

            return;
        }

        // Seuraa pelaajaa
        if (!isAttacking)
        {
            transform.position +=
                direction.normalized * moveSpeed * Time.deltaTime;
        }
    }

    private IEnumerator Attack()
    {
        isAttacking = true;
        canAttack = false;

        Debug.Log("Enemy valmistautuu hyökkäykseen!");

        // Pelaajalla on 3 sekuntia aikaa väistää
        yield return new WaitForSeconds(attackPreparationTime);

        if (player != null && playerHealth != null)
        {
            Vector3 direction = player.position - transform.position;
            direction.y = 0f;

            float distance = direction.magnitude;

            // Jos pelaaja on vielä lähellä, osuu
            if (distance <= attackRange)
            {
                playerHealth.TakeDamage(damage);

                Debug.Log("Enemy osui pelaajaan!");
            }
            else
            {
                Debug.Log("Pelaaja väisti hyökkäyksen!");
            }
        }

        isAttacking = false;

        // Odottaa 5 sekuntia ennen seuraavaa hyökkäystä
        yield return new WaitForSeconds(attackCooldown);

        canAttack = true;
    }
}