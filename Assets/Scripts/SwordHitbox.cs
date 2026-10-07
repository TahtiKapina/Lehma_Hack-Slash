using System.Collections.Generic;
using UnityEngine;

// Sample the sword after animation evaluation, not in physics trigger callbacks.
[DefaultExecutionOrder(50)]
[RequireComponent(typeof(BoxCollider))]
public class SwordHitbox : MonoBehaviour
{
    public PlayerCombat combat;
    public LayerMask enemyLayers = ~0;
    [Tooltip("Smaller steps improve detection during fast sword rotation.")]
    [Min(0.01f)] public float sweepStep = 0.1f;
    [Range(1, 128)] public int maxSweepSteps = 64;

    BoxCollider box;
    readonly HashSet<EnemyHealth> hitEnemies = new HashSet<EnemyHealth>();
    int lastSwing = -1;
    bool havePrevious;
    Vector3 previousCenter;
    Quaternion previousRotation;
    float previousTime;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        // This collider is only an editable shape for overlap/sweep queries.
        // It must never push bodies or participate in the player's physics.
        box.isTrigger = true;
        box.enabled = false;
        if (combat == null) combat = GetComponentInParent<PlayerCombat>();
    }

    void LateUpdate()
    {
        if (box == null || combat == null ||
            !combat.TryGetSwordAttack(out PlayerCombat.Attack attack, out int swing, out float time))
        {
            havePrevious = false;
            return;
        }

        Vector3 center = transform.TransformPoint(box.center);
        Quaternion rotation = transform.rotation;
        Vector3 scale = transform.lossyScale;
        Vector3 half = Vector3.Scale(box.size * 0.5f,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        half = Vector3.Max(half, Vector3.one * 0.001f);

        if (swing != lastSwing)
        {
            hitEnemies.Clear();
            lastSwing = swing;
            havePrevious = false;
        }

        float start = Mathf.Clamp01(attack.hitStartNormalized);
        float end = Mathf.Max(start, Mathf.Clamp01(attack.hitEndNormalized));
        if (havePrevious && time >= previousTime && time > previousTime)
        {
            // Clip the sweep to the actual active window, even if a long frame
            // crosses both its start and end. Never sweep between two attacks.
            float from = Mathf.Max(previousTime, start);
            float to = Mathf.Min(time, end);
            if (to >= from)
            {
                float a = Mathf.InverseLerp(previousTime, time, from);
                float b = Mathf.InverseLerp(previousTime, time, to);
                Sweep(Vector3.Lerp(previousCenter, center, a), Quaternion.Slerp(previousRotation, rotation, a),
                    Vector3.Lerp(previousCenter, center, b), Quaternion.Slerp(previousRotation, rotation, b), half, attack);
            }
        }
        else if (time >= start && time <= end)
        {
            CheckBox(center, half, rotation, attack);
        }

        previousCenter = center;
        previousRotation = rotation;
        previousTime = time;
        havePrevious = true;
    }

    void Sweep(Vector3 from, Quaternion fromRotation, Vector3 to, Quaternion toRotation,
        Vector3 half, PlayerCombat.Attack attack)
    {
        float motion = Vector3.Distance(from, to)
            + Quaternion.Angle(fromRotation, toRotation) * Mathf.Deg2Rad * half.magnitude;
        int steps = Mathf.Clamp(Mathf.CeilToInt(motion / Mathf.Max(0.01f, sweepStep)), 1, Mathf.Max(1, maxSweepSteps));
        Vector3 last = from;
        CheckBox(from, half, fromRotation, attack);
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector3 point = Vector3.Lerp(from, to, t);
            Quaternion rotation = Quaternion.Slerp(fromRotation, toRotation, t);
            Vector3 delta = point - last;
            if (delta.sqrMagnitude > 0.000001f)
            {
                foreach (RaycastHit hit in Physics.BoxCastAll(last, half, delta.normalized,
                    rotation, delta.magnitude, enemyLayers, QueryTriggerInteraction.Collide))
                    TryHit(hit.collider, hit.point, attack);
            }
            CheckBox(point, half, rotation, attack);
            last = point;
        }
    }

    void CheckBox(Vector3 center, Vector3 half, Quaternion rotation, PlayerCombat.Attack attack)
    {
        foreach (Collider collider in Physics.OverlapBox(center, half, rotation,
            enemyLayers, QueryTriggerInteraction.Collide))
            TryHit(collider, collider.ClosestPoint(center), attack);
    }

    void TryHit(Collider collider, Vector3 contact, PlayerCombat.Attack attack)
    {
        EnemyHealth enemy = collider.GetComponentInParent<EnemyHealth>();
        if (enemy == null || enemy.IsDead || hitEnemies.Contains(enemy)) return;
        Transform source = combat.playerMovement != null ? combat.playerMovement.transform : combat.transform;
        if (enemy.transform == source || enemy.transform.IsChildOf(source)) return;
        hitEnemies.Add(enemy);
        float distance = Mathf.Max(0f, attack.forwardPushSpeed)
            * Mathf.Max(0.01f, attack.forwardPushDuration) * 0.5f
            * Mathf.Max(0f, attack.knockbackMultiplier);
        enemy.ReceiveHit(attack.damage, source.position, contact, distance, attack.hitStun, attack.isHeavy);
    }

    void OnDisable()
    {
        havePrevious = false;
    }

    void OnDrawGizmosSelected()
    {
        BoxCollider shape = GetComponent<BoxCollider>();
        if (shape == null) return;
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(shape.center, shape.size);
        Gizmos.matrix = old;
    }
}