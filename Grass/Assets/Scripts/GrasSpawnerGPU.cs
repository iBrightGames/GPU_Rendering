
using UnityEngine;
using System.Collections.Generic;

public class GrassSpawnerGPU : MonoBehaviour
{
    [Header("Çimen Ayarları")]
    public GameObject grassPrefab; // Mesh GameObject
    public int grassCount = 1000;
    public Vector2 areaSize = new Vector2(10f, 10f);

    [Header("Jitter Ayarı")]
    public float jitter = 0.3f; // hücre içi rastgele kaydırma

    private Mesh grassMesh;
    private Material grassMaterial;
    private Matrix4x4[] matrices;

    void Start()
    {
        if (grassPrefab == null)
        {
            Debug.LogError("Grass prefab atanmadı!");
            return;
        }

        MeshFilter mf = grassPrefab.GetComponent<MeshFilter>();
        MeshRenderer mr = grassPrefab.GetComponent<MeshRenderer>();

        if (mf == null || mr == null)
        {
            Debug.LogError("Prefab MeshFilter ve MeshRenderer içermeli.");
            return;
        }

        grassMesh = mf.sharedMesh;
        grassMaterial = mr.sharedMaterial;

        matrices = new Matrix4x4[grassCount];

        GenerateMatrices();
    }

void GenerateMatrices()
{
    int gridSize = Mathf.CeilToInt(Mathf.Sqrt(grassCount));
    float cellSizeX = areaSize.x / gridSize;
    float cellSizeZ = areaSize.y / gridSize;

    int index = 0;
    for (int x = 0; x < gridSize; x++)
    {
        for (int z = 0; z < gridSize; z++)
        {
            if (index >= grassCount) break;

            // Merkez pivot'u baz al
            float posX = (x + 0.5f) * cellSizeX - areaSize.x / 2f + Random.Range(-jitter, jitter);
            float posZ = (z + 0.5f) * cellSizeZ - areaSize.y / 2f + Random.Range(-jitter, jitter);

            Vector3 pos = transform.position + new Vector3(posX, 0f, posZ);
            Quaternion rot = Quaternion.Euler(0f, 0f, 0f);
            Vector3 scale = grassPrefab.transform.localScale;

            matrices[index] = Matrix4x4.TRS(pos, rot, scale);
            index++;
        }
    }
}



    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

        // Alanın merkezini spawner objesi pivot’u kabul ediyoruz
        Vector3 center = transform.position;
        Vector3 size = new Vector3(areaSize.x, 0f, areaSize.y);

        // Dört köşe için
        Vector3 half = size * 0.5f;
        Vector3 p1 = center + new Vector3(-half.x, 0, -half.z);
        Vector3 p2 = center + new Vector3(half.x, 0, -half.z);
        Vector3 p3 = center + new Vector3(half.x, 0, half.z);
        Vector3 p4 = center + new Vector3(-half.x, 0, half.z);

        // Kenarları çiz
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);
    }


    void Update()
    {
        // GPU Instancing ile çizim
        const int batchSize = 1023; // Unity limit
        for (int i = 0; i < grassCount; i += batchSize)
        {
            int count = Mathf.Min(batchSize, grassCount - i);
            Graphics.DrawMeshInstanced(grassMesh, 0, grassMaterial, 
                                       new System.ArraySegment<Matrix4x4>(matrices, i, count).ToArray());
        }
    }
}
