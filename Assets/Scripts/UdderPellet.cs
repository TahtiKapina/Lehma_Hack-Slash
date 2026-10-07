using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class UdderPellet : MonoBehaviour
{
    [Tooltip("Include enemies AND the environment. Non-enemy trigger volumes are ignored.")]
    public LayerMask impactLayers = ~0;
    [Min(0f)] public float gravityMultiplier = 1f;

    Rigidbody body;
    SphereCollider sphere;
    Transform owner;
    Vector3 velocity;
    Vector3 launchPosition;
    float damage;
    float stun;
    float push;
    bool launched;
    bool consumed;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        sphere = GetComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.center = Vector3.zero;
        // Explicit ballistic movement plus a sweep catches fast projectiles;
        // engine gravity is off because it is integrated below exactly once.
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public void Launch(Transform shooter, Vector3 initialVelocity,
        float hitDamage, float hitStun, float knockback)
    {
        owner = shooter;
        velocity = initialVelocity;
        launchPosition = transform.position;
        damage = Mathf.Max(0f, hitDamage);
        stun = Mathf.Max(0f, hitStun);
        push = Mathf.Max(0f, knockback);
        launched = true;
    }

    void FixedUpdate()
    {
        if (!launched || consumed) return;
        float dt = Time.fixedDeltaTime;
        Vector3 scale = transform.lossyScale;
        float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        radius = Mathf.Max(0.001f, radius);
        Vector3 start = body.position;

        foreach (Collider collider in Physics.OverlapSphere(start, radius, impactLayers, QueryTriggerInteraction.Collide))
        {
            if (!CanHit(collider)) continue;
            Impact(collider, collider.ClosestPoint(start));
            return;
        }

        Vector3 acceleration = Physics.gravity * Mathf.Max(0f, gravityMultiplier);
        Vector3 displacement = velocity * dt + acceleration * (0.5f * dt * dt);
        velocity += acceleration * dt;
        float distance = displacement.magnitude;
        Collider nearest = null;
        Vector3 contact = Vector3.zero;
        float nearestDistance = float.PositiveInfinity;
        if (distance > 0.00001f)
        {
            foreach (RaycastHit hit in Physics.SphereCastAll(start, radius, displacement / distance,
                distance, impactLayers, QueryTriggerInteraction.Collide))
            {
                if (!CanHit(hit.collider) || hit.distance >= nearestDistance) continue;
                nearest = hit.collider;
                nearestDistance = hit.distance;
                contact = hit.point;
            }
        }
        if (nearest != null)
        {
            Impact(nearest, contact);
            return;
        }
        body.MovePosition(start + displacement);
    }

    bool CanHit(Collider collider)
    {
        if (collider == sphere || collider.GetComponentInParent<UdderPellet>() != null) return false;
        if (owner != null && (collider.transform == owner || collider.transform.IsChildOf(owner))) return false;
        if ((impactLayers.value & (1 << collider.gameObject.layer)) == 0) return false;
        // Pass through trigger-only sensors, but hit enemy trigger hurtboxes.
        return !collider.isTrigger || collider.GetComponentInParent<EnemyHealth>() != null;
    }

    void OnTriggerEnter(Collider other)
    {
        if (launched && !consumed && CanHit(other)) Impact(other, other.ClosestPoint(body.position));
    }

    void Impact(Collider collider, Vector3 contact)
    {
        if (consumed) return;
        consumed = true;
        EnemyHealth enemy = collider.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
        {
            // Knock away from the incoming pellet, not the player's new position.
            Vector3 from = velocity.sqrMagnitude > 0.0001f
                ? contact - velocity.normalized : launchPosition;
            enemy.ReceivePelletHit(damage, from, contact, push, stun);
        }
        Destroy(gameObject);
    }
}