using System.Collections;
using UnityEngine;

public class Flicker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PlayerMovement playerMovement;
    [SerializeField] GameObject visualModel;
    [SerializeField] ParticleSystem speedLines;

    [Header("Afterimages")]
    [SerializeField] int copiesPerSide = 3;
    [SerializeField] float spacing = 0.08f;
    [SerializeField] float lifetime = 0.08f;
    [SerializeField, Range(0f, 1f)] float opacity = 0.25f;
    [SerializeField] int flickerFrames = 5;

    [Header("Sound")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip flickerSound;
    [SerializeField] float soundStartTime = 0.15f;
    [SerializeField] float minPitch = 0.95f;
    [SerializeField] float maxPitch = 1.05f;

    [Header("Invisible Time")]
    [SerializeField] int invisibleFrames = 2;

    Coroutine effectRoutine;
    bool modelHidden;

    void OnEnable()
    {
        if (playerMovement == null)
        {
            playerMovement = GetComponentInParent<PlayerMovement>();
        }

        if (playerMovement == null)
        {
            Debug.LogError("Flicker: assign Player2's PlayerMovement reference.", this);
            return;
        }

        playerMovement.DashStarted += OnDashStarted;
    }

    void OnDisable()
    {
        if (playerMovement != null)
        {
            playerMovement.DashStarted -= OnDashStarted;
        }

        StopEffect();
    }

    void OnDashStarted()
    {
        // A short cooldown can allow another dash before the old effect ends.
        StopEffect();
        effectRoutine = StartCoroutine(TeleportEffect());
    }

    void StopEffect()
    {
        if (effectRoutine != null)
        {
            StopCoroutine(effectRoutine);
            effectRoutine = null;
        }

        if (modelHidden)
        {
            SetModelVisible(true);
        }

    }

    IEnumerator TeleportEffect()
    {

        // =========================
        // DEPARTURE
        // =========================

        if (speedLines != null) speedLines.Play();
        PlayFlickerSound();

        for (int frame = 0; frame < flickerFrames; frame++)
        {
            CreateAfterimages();
            yield return null;
        }

        // =========================
        // DISAPPEAR
        // =========================

        SetModelVisible(false);

        for (int frame = 0; frame < invisibleFrames; frame++)
        {
            yield return null;
        }

        // =========================
        // REAPPEAR
        // =========================

        SetModelVisible(true);

        // =========================
        // ARRIVAL
        // =========================

        if (speedLines != null) speedLines.Play();


        for (int frame = 0; frame < flickerFrames; frame++)
        {
            CreateAfterimages();
            yield return null;
        }

    }

    void SetModelVisible(bool visible)
    {
        modelHidden = !visible;
        if (visualModel == null) return;

        Renderer[] renderers =
            visualModel.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = visible;
        }
    }

    void PlayFlickerSound()
    {
        if (audioSource == null || flickerSound == null) return;

        audioSource.Stop();

        audioSource.clip = flickerSound;
        audioSource.pitch = Random.Range(minPitch, maxPitch);

        // Start this many seconds into the audio file
        audioSource.time = soundStartTime;

        audioSource.Play();
    }

    void CreateAfterimages()
    {
        if (visualModel == null) return;

        SkinnedMeshRenderer[] renderers =
            visualModel.GetComponentsInChildren<SkinnedMeshRenderer>();

        foreach (SkinnedMeshRenderer smr in renderers)
        {
            // Bake exactly what the SkinnedMeshRenderer
            // is currently displaying.
            Mesh bakedMesh = new Mesh();

            smr.BakeMesh(bakedMesh);

            for (int i = 1; i <= copiesPerSide; i++)
            {
                float distance = spacing * i;

                CreateGhost(
                    smr,
                    bakedMesh,
                    -distance,
                    i
                );

                CreateGhost(
                    smr,
                    bakedMesh,
                    distance,
                    i
                );
            }

            // All ghosts share this mesh.
            Destroy(bakedMesh, lifetime);
        }
    }

    void CreateGhost(
        SkinnedMeshRenderer source,
        Mesh mesh,
        float offset,
        int copyIndex)
    {
        GameObject ghost =
            new GameObject("Teleport Afterimage");

        // BakeMesh already accounts for the renderer's scale.
        // Therefore DO NOT apply lossyScale again.
        ghost.transform.position =
            source.transform.position;

        ghost.transform.rotation =
            source.transform.rotation;

        ghost.transform.localScale =
            Vector3.one;

        // Your cow's setup uses forward as its
        // visual left/right blur direction.
        ghost.transform.position +=
            transform.forward * offset;

        // =========================
        // MESH
        // =========================

        MeshFilter filter =
            ghost.AddComponent<MeshFilter>();

        filter.sharedMesh = mesh;

        MeshRenderer renderer =
            ghost.AddComponent<MeshRenderer>();

        // =========================
        // MATERIALS
        // =========================

        Material[] ghostMaterials =
            new Material[source.materials.Length];

        for (int i = 0; i < ghostMaterials.Length; i++)
        {
            Material mat =
                new Material(source.materials[i]);

            // URP transparency
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);

            mat.SetOverrideTag(
                "RenderType",
                "Transparent"
            );

            mat.renderQueue = 3000;

            mat.SetInt(
                "_SrcBlend",
                (int)UnityEngine.Rendering.BlendMode.SrcAlpha
            );

            mat.SetInt(
                "_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
            );

            mat.SetInt("_ZWrite", 0);

            float alpha =
                opacity / copyIndex;

            if (mat.HasProperty("_BaseColor"))
            {
                Color color =
                    mat.GetColor("_BaseColor");

                color.a = alpha;

                mat.SetColor(
                    "_BaseColor",
                    color
                );
            }

            ghostMaterials[i] = mat;
        }

        renderer.materials =
            ghostMaterials;

        Destroy(ghost, lifetime);
    }
}