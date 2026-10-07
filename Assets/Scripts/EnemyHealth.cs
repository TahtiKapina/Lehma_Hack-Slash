using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    public int maxHealth = 15;
    public float currentHealth = 15f;
    public HealthBar healthBar;

    [Header("Pellet heavy-hit buildup")]
    [Tooltip("This many pellet hits on this enemy trigger its normal heavy launch. Zero disables buildup.")]
    [Min(0)] public int pelletHitsForHeavy = 6;
    [Tooltip("Reset unfinished buildup after this many seconds without a pellet hit. Zero keeps buildup until it triggers.")]
    [Min(0f)] public float pelletBuildupResetTime = 0.8f;

    public bool IsDead => dead;
    int pelletHits;
    float lastPelletHitTime = float.NegativeInfinity;

    bool dead;
    EnemyHitReaction reaction;
    EnemyController controller;

    void Awake()
    {
        currentHealth = Mathf.Max(1, maxHealth);
        reaction = GetComponent<EnemyHitReaction>();
        controller = GetComponent<EnemyController>();
    }

    void Start()
    {
        UpdateBar();
    }

    // Retain the old API for any other damage callers.
    public void TakeDamage(int damage)
    {
        ApplyDamage(damage);
    }

    public void ReceiveHit(float damage, Vector3 attackerPosition, Vector3 contact,
        float knockbackDistance, float stunDuration, bool heavy)
    {
        if (dead) return;
        if (controller != null) controller.InterruptAttack();
        if (reaction != null)
            reaction.React(attackerPosition, contact, knockbackDistance, stunDuration, heavy);
        ApplyDamage(damage);
    }

    public void ReceivePelletHit(float damage, Vector3 attackerPosition, Vector3 contact,
        float knockbackDistance, float stunDuration)
    {
        if (dead) return;
        bool heavy = false;
        if (pelletHitsForHeavy > 0)
        {
            if (pelletBuildupResetTime > 0f && Time.time - lastPelletHitTime > pelletBuildupResetTime)
                pelletHits = 0;
            pelletHits++;
            heavy = pelletHits >= pelletHitsForHeavy;
            if (heavy) pelletHits = 0;
        }
        else pelletHits = 0;

        lastPelletHitTime = Time.time;
        // The threshold pellet still deals its damage exactly once.
        ReceiveHit(damage, attackerPosition, contact, knockbackDistance, stunDuration, heavy);
    }

    void ApplyDamage(float damage)
    {
        if (dead) return;
        currentHealth = Mathf.Max(0f, currentHealth - Mathf.Max(0f, damage));
        UpdateBar();
        if (currentHealth <= 0f)
        {
            dead = true;
            if (controller != null) controller.InterruptAttack();
            Destroy(gameObject);
        }
    }

    void UpdateBar()
    {
        // Preserve compatibility with the existing integer HealthBar API.
        // Only the display rounds; damage and stored health stay fractional.
        if (healthBar != null) healthBar.SetHealth(Mathf.CeilToInt(currentHealth), maxHealth);
    }
}