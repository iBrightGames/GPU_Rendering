using UnityEngine;

public class SimpleGPUInstancer : MonoBehaviour
{
    [Header("Spawn Settings")]
    [Tooltip("The prefab that will be instantiated via GPU instancing. Should be standard 3d GameObject")]
    public GameObject objectToSpawn;

    [Tooltip("Number of instances to spawn.")]
    public int spawnCount = 100;

    [Header("Area Limits")]
    [Tooltip("Size of the spawning area in X, Y, Z.")]
    public Vector3 areaSize = new Vector3(100f, 5f, 100f);


    Vector3 areaCenter;

    private Matrix4x4[] matrices;
    private Mesh instanceMesh;
    private Material instanceMaterial;

    void Start()
    {
        areaCenter = transform.position;
        InitializeInstancing();
        
    }

    void InitializeRandomInstancing()
    {
        if (objectToSpawn == null) return;

        // Get mesh and material from the object
        MeshFilter meshFilter = objectToSpawn.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = objectToSpawn.GetComponent<MeshRenderer>();

        if (meshFilter != null && meshRenderer != null)
        {
            instanceMesh = meshFilter.sharedMesh;
            instanceMaterial = meshRenderer.sharedMaterial;
            instanceMaterial.enableInstancing = true;

            // Create random positions and rotations
            matrices = new Matrix4x4[spawnCount];

            for (int i = 0; i < spawnCount; i++)
            {
                Vector3 randomPosition = new Vector3(
                    Random.Range(-areaSize.x / 2, areaSize.x / 2) + areaCenter.x,
                    Random.Range(-areaSize.y / 2, areaSize.y / 2) + areaCenter.y,
                    Random.Range(-areaSize.z / 2, areaSize.z / 2) + areaCenter.z
                );

                Quaternion randomRotation = Quaternion.Euler(
                    Random.Range(0, 360),
                    Random.Range(0, 360),
                    Random.Range(0, 360)
                );

                matrices[i] = Matrix4x4.TRS(randomPosition, randomRotation, Vector3.one);
            }
        }
    }
    void InitializeInstancing()
    {
        if (objectToSpawn == null) return;

        MeshFilter meshFilter = objectToSpawn.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = objectToSpawn.GetComponent<MeshRenderer>();

        if (meshFilter != null && meshRenderer != null)
        {
            instanceMesh = meshFilter.sharedMesh;
            instanceMaterial = meshRenderer.sharedMaterial;
            instanceMaterial.enableInstancing = true;

            matrices = new Matrix4x4[spawnCount];

            int gridSize = Mathf.CeilToInt(Mathf.Pow(spawnCount, 1f / 3f)); // cube root
            Vector3 step = new Vector3(areaSize.x / gridSize, areaSize.y / gridSize, areaSize.z / gridSize);

            int index = 0;
            for (int x = 0; x < gridSize && index < spawnCount; x++)
            {
                for (int y = 0; y < gridSize && index < spawnCount; y++)
                {
                    for (int z = 0; z < gridSize && index < spawnCount; z++)
                    {
                        Vector3 pos = new Vector3(
                            -areaSize.x/2 + step.x/2 + x*step.x + areaCenter.x,
                            -areaSize.y/2 + step.y/2 + y*step.y + areaCenter.y,
                            -areaSize.z/2 + step.z/2 + z*step.z + areaCenter.z
                        );

                        matrices[index] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
                        index++;
                    }
                }
            }
        }
    }

    void Update()
    {
        if (matrices != null && instanceMesh != null && instanceMaterial != null)
        {
            // Draw all instances in one call
            Graphics.DrawMeshInstanced(instanceMesh, 0, instanceMaterial, matrices);
        }
    }

    // Visualize the spawn area in the editor
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(areaCenter, areaSize);
        Gizmos.color = Color.red;
        Gizmos.DrawCube(areaCenter, areaSize);
    }

    // Method to regenerate instances (call this if you change values at runtime)
    public void RegenerateInstances()
    {
        InitializeInstancing();
    }
}
