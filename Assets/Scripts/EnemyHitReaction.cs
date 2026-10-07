using UnityEngine;

// Physics reaction runs before the AI's FixedUpdate.
[DefaultExecutionOrder(-10)]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class EnemyHitReaction : MonoBehaviour
{
    [Header("Light knockback")]
    [Min(0.02f)] public float knockbackDuration = 0.18f;

    [Header("Heavy: whole-body topple")]
    [Min(0f)] public float heavyHorizontalSpeed = 8f;
    [Min(0f)] public float heavyUpwardSpeed = 3f;
    [Min(0f)] public float heavySpinSpeed = 8f;
    [Min(0.1f)] public float heavyDownTime = 1.2f;
    public LayerMask groundLayers = ~0;

    [Header("Hit flash")]
    public Color flashColor = Color.red;
    [Min(0f)] public float flashDuration = 0.1f;
    [Tooltip("Leave empty to find renderers automatically. Exclude health bar/UI renderers.")]
    public Renderer[] flashRenderers;

    public bool IsReacting => toppled || Time.time < stunnedUntil || knockbackRemaining > 0f;

    Rigidbody body;
    Collider bodyCollider;
    RigidbodyConstraints uprightConstraints;
    float uprightBottomOffset;
    bool toppled;
    float standUpAfter;
    float stunnedUntil;
    Vector3 knockbackDirection;
    float knockbackDistance;
    float knockbackRemaining;
    float knockbackTotalTime;
    bool stopKnockbackNextStep;
    bool flashing;
    float flashEndsAt;
    MaterialPropertyBlock[] originalBlocks;
    MaterialPropertyBlock flashBlock;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        uprightBottomOffset = body.position.y - bodyCollider.bounds.min.y;
        if (flashRenderers == null || flashRenderers.Length == 0)
            flashRenderers = GetComponentsInChildren<Renderer>();
        originalBlocks = new MaterialPropertyBlock[flashRenderers.Length];
        for (int i = 0; i < originalBlocks.Length; i++) originalBlocks[i] = new MaterialPropertyBlock();
        flashBlock = new MaterialPropertyBlock();
    }

    public void React(Vector3 attackerPosition, Vector3 contact,
        float distance, float stunDuration, bool heavy)
    {
        stunnedUntil = Mathf.Max(stunnedUntil, Time.time + Mathf.Max(0f, stunDuration));
        Flash();
        if (body == null || body.isKinematic) return;
        Vector3 away = body.worldCenterOfMass - attackerPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            away = body.worldCenterOfMass - contact;
            away.y = 0f;
        }
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        away.Normalize();

        if (heavy)
        {
            stopKnockbackNextStep = false;
            if (!toppled) uprightConstraints = body.constraints;
            toppled = true;
            standUpAfter = Time.time + Mathf.Max(0.1f, heavyDownTime);
            knockbackRemaining = 0f;
            body.constraints &= ~RigidbodyConstraints.FreezeRotation;
            body.linearVelocity = away * heavyHorizontalSpeed + Vector3.up * heavyUpwardSpeed;
            body.angularVelocity = Vector3.Cross(Vector3.up, away).normalized * heavySpinSpeed;
            body.WakeUp();
        }
        else if (toppled)
        {
            // Light hits can keep a fallen enemy stunned without snapping it upright.
            standUpAfter = Mathf.Max(standUpAfter, stunnedUntil);
            body.AddForce(away * (Mathf.Max(0f, distance) / Mathf.Max(0.02f, knockbackDuration)),
                ForceMode.VelocityChange);
        }
        else
        {
            stopKnockbackNextStep = false;
            knockbackDirection = away;
            knockbackDistance = Mathf.Max(0f, distance);
            knockbackTotalTime = Mathf.Max(0.02f, knockbackDuration);
            knockbackRemaining = knockbackTotalTime;
        }
    }

    void FixedUpdate()
    {
        if (body.isKinematic) return;
        if (stopKnockbackNextStep)
        {
            body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);
            stopKnockbackNextStep = false;
        }
        if (toppled)
        {
            if (Time.time >= Mathf.Max(standUpAfter, stunnedUntil) && IsGrounded()) StandUp();
            return;
        }

        if (knockbackRemaining > 0f)
        {
            // Integrate decelerating knockback so its unobstructed distance is
            // close to the player's configured attack push, independent of FPS.
            float before = knockbackRemaining / knockbackTotalTime;
            knockbackRemaining = Mathf.Max(0f, knockbackRemaining - Time.fixedDeltaTime);
            float after = knockbackRemaining / knockbackTotalTime;
            float distanceThisStep = knockbackDistance * (before * before - after * after);
            Vector3 velocity = knockbackDirection * (distanceThisStep / Time.fixedDeltaTime);
            velocity.y = body.linearVelocity.y;
            body.linearVelocity = velocity;
            stopKnockbackNextStep = knockbackRemaining <= 0f;
            // Keep AI suppressed for the physics step that consumes the last portion.
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + Time.fixedDeltaTime);
        }
        else if (Time.time < stunnedUntil)
        {
            body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);
        }
    }

    bool IsGrounded()
    {
        foreach (RaycastHit hit in Physics.RaycastAll(bodyCollider.bounds.center, Vector3.down,
            bodyCollider.bounds.extents.y + 0.2f, groundLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.normal.y > 0.4f) return true;
        }
        return false;
    }

    void StandUp()
    {
        float yaw = transform.eulerAngles.y;
        Vector3 position = body.position;
        // Raise the center to keep the upright capsule above the floor.
        position.y = Mathf.Max(position.y, bodyCollider.bounds.min.y + uprightBottomOffset + 0.02f);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = position;
        body.rotation = Quaternion.Euler(0f, yaw, 0f);
        body.constraints = uprightConstraints;
        toppled = false;
        stunnedUntil = Mathf.Max(stunnedUntil, Time.time + 0.1f);
    }

    void Flash()
    {
        for (int i = 0; i < flashRenderers.Length; i++)
        {
            Renderer renderer = flashRenderers[i];
            if (renderer == null) continue;
            if (!flashing) renderer.GetPropertyBlock(originalBlocks[i]);
            renderer.GetPropertyBlock(flashBlock);
            flashBlock.SetColor("_BaseColor", flashColor);
            flashBlock.SetColor("_Color", flashColor);
            renderer.SetPropertyBlock(flashBlock);
        }
        flashing = true;
        flashEndsAt = Time.time + Mathf.Max(0f, flashDuration);
    }

    void Update()
    {
        if (flashing && Time.time >= flashEndsAt) RestoreFlash();
    }

    void RestoreFlash()
    {
        if (!flashing) return;
        for (int i = 0; i < flashRenderers.Length; i++)
            if (flashRenderers[i] != null) flashRenderers[i].SetPropertyBlock(originalBlocks[i]);
        flashing = false;
    }

    void OnDisable()
    {
        RestoreFlash();
        if (toppled && body != null)
        {
            StandUp();
        }
        toppled = false;
        knockbackRemaining = 0f;
        stopKnockbackNextStep = false;
        stunnedUntil = 0f;
    }
}