using UnityEngine;

public class ObstacleDamage : MonoBehaviour
{
    public int damage = 10;

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log("Este törmäsi: " + collision.gameObject.name);

        if (collision.gameObject.CompareTag("Player"))
        {
            PlayerHealth health = collision.gameObject.GetComponent<PlayerHealth>();

            if (health != null)
            {
                health.TakeDamage(damage);
            }
            else
            {
                Debug.LogError("PlayerHealth puuttuu Player-objektilta!");
            }
        }
    }
}