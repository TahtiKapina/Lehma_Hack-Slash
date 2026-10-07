using System;
using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    // Fired once per accepted damaging combat hit, including the killing hit.
    public static event Action<float, bool> CombatDamageDealt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetEvents() { CombatDamageDealt = null; }

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

    void Start() { UpdateBar(); }

    // Generic damage retains its old behavior; it does not award player style.
    public void TakeDamage(int damage) { ApplyDamage(damage, false); }

    public void ReceiveHit(float damage, Vector3 attackerPosition, Vector3 contact,
        float knockbackDistance, float stunDuration, bool heavy)
    {
        if (dead) return;
        if (controller != null) controller.InterruptAttack();
        if (reaction != null)
            reaction.React(attackerPosition, contact, knockbackDistance, stunDuration, heavy);
        ApplyDamage(damage, true);
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
        ReceiveHit(damage, attackerPosition, contact, knockbackDistance, stunDuration, heavy);
    }

    void ApplyDamage(float damage, bool awardStyle)
    {
        if (dead) return;
        float before = currentHealth;
        currentHealth = Mathf.Max(0f, currentHealth - Mathf.Max(0f, damage));
        float dealt = Mathf.Max(0f, before - currentHealth);
        // Mark death before notifying listeners, preventing repeat kill rewards.
        dead = currentHealth <= 0f;
        UpdateBar();
        if (awardStyle && dealt > 0f) CombatDamageDealt?.Invoke(dealt, dead);
        if (dead)
        {
            if (controller != null) controller.InterruptAttack();
            Destroy(gameObject);
        }
    }

    void UpdateBar()
    {
        if (healthBar != null) healthBar.SetHealth(Mathf.CeilToInt(currentHealth), maxHealth);
    }
}
