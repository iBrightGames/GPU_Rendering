using UnityEngine;
using System.Collections.Generic;

public class CPUInstancer : MonoBehaviour
{
    [Header("Prefab & Material")]
    public GameObject objectToSpawn;

    [Header("Spawn Settings")]
    public int spawnCount = 100;
    public Vector3 areaSize = new Vector3(50, 0, 50);

    private Mesh mesh;
    private Material material;
    private Matrix4x4[] matrices;

    public List<Vector3> positions = new List<Vector3>();

    void Start()
    {
        if (objectToSpawn == null)
        {
            Debug.LogError("Prefab atanmadı!");
            return;
        }

        var mf = objectToSpawn.GetComponent<MeshFilter>();
        var mr = objectToSpawn.GetComponent<MeshRenderer>();
        if (mf == null || mr == null)
        {
            Debug.LogError("Prefab MeshFilter veya MeshRenderer içermiyor!");
            return;
        }

        mesh = mf.sharedMesh;
        material = mr.sharedMaterial;

        GeneratePositions();
        PrepareMatrices();
    }

    // void Update()
    // {
    //     if (matrices == null || mesh == null || material == null)
    //     {
    //         Debug.Log("something is null");
    //         return;
    //     }

    //     Graphics.DrawMeshInstanced(mesh, 0, material, matrices);
    // }
    void Update()
{
    if (matrices == null || mesh == null || material == null)
    {
        Debug.Log("something is null");
        return;
    }

    const int batchSize = 1023; // Unity tek çağrıda max
    int total = matrices.Length;
    for (int i = 0; i < total; i += batchSize)
    {
        int count = Mathf.Min(batchSize, total - i);
        Graphics.DrawMeshInstanced(mesh, 0, material, matrices, count, null,
            UnityEngine.Rendering.ShadowCastingMode.On, true, gameObject.layer);
    }
}


    [ContextMenu("Generate Positions")]
    public void GeneratePositions()
    {
        positions.Clear();
        Vector3 center = transform.position;
        for (int i = 0; i < spawnCount; i++)
        {
            Vector3 pos = new Vector3(
                Random.Range(-areaSize.x / 2, areaSize.x / 2) + center.x,
                Random.Range(-areaSize.y / 2, areaSize.y / 2) + center.y,
                Random.Range(-areaSize.z / 2, areaSize.z / 2) + center.z
            );
            positions.Add(pos);
        }
        Debug.Log($"Positions generated: {positions.Count}");
    }

    [ContextMenu("Prepare Matrices")]
    public void PrepareMatrices()
    {
        GeneratePositions();
        if (positions == null || positions.Count == 0)
        {
            Debug.LogWarning("Positions listesi boş, önce Generate Positions çalıştırın.");
            return;
        }

        matrices = new Matrix4x4[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            matrices[i] = Matrix4x4.TRS(positions[i], Quaternion.identity, Vector3.one);
        }
        Debug.Log("Matrices hazırlandı.");
    }
    
}
