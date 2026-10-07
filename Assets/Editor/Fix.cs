using UnityEngine;
using UnityEditor;

public class AddMapColliders
{
    [MenuItem("Tools/Add Colliders To Selected Map")]
    static void AddColliders()
    {
        GameObject root = Selection.activeGameObject;

        if (root == null)
        {
            Debug.LogWarning("Select the map root first.");
            return;
        }

        MeshFilter[] meshes = root.GetComponentsInChildren<MeshFilter>(true);

        int added = 0;

        foreach (MeshFilter meshFilter in meshes)
        {
            GameObject obj = meshFilter.gameObject;

            if (obj.GetComponent<Collider>() != null)
                continue;

            MeshCollider collider = obj.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.sharedMesh;
            collider.convex = false;

            added++;
        }

        Debug.Log("Added " + added + " Mesh Colliders.");
    }
}