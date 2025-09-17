using UnityEngine;

// public class GPUInstancing : MonoBehaviour
// {
//     public Mesh mesh;                  // Çoğaltmak istediğin obje mesh’i
//     public Material material;          // GPU instancing destekleyen material
//     public int instanceCount = 10000;   // Kaç kopya
//     private Matrix4x4[] matrices;

//     void Start()
//     {
//         // Transform matrislerini oluştur
//         matrices = new Matrix4x4[instanceCount];
//         for (int i = 0; i < instanceCount; i++)
//         {
//             Vector3 pos = new Vector3(
//                 Random.Range(-50f, 50f),
//                 Random.Range(0f, 10f),
//                 Random.Range(-50f, 50f)
//             );
//             matrices[i] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
//         }
//     }

//     void Update()
//     {
//         // Max 1023 instance sınırı var her çağrıda
//         int batchSize = 1023;
//         for (int i = 0; i < instanceCount; i += batchSize)
//         {
//             int count = Mathf.Min(batchSize, instanceCount - i);
//             Graphics.DrawMeshInstanced(mesh, 0, material, matrices, count, null, UnityEngine.Rendering.ShadowCastingMode.On, true);
//         }
//     }
// }


public class GPUInstancing : MonoBehaviour
{
    [Header("Prefab ve Material")]
    public GameObject prefab;           // Çoğaltmak istediğin prefab
    public Material material;           // GPU instancing destekleyen material

    [Header("Instance Ayarları")]
    public int instanceCount = 10000;   // Kaç kopya
    public Vector3 areaMin = new Vector3(-50f, -50f, -50f); // Dışardan ayarlanabilir alan min
    public Vector3 areaMax = new Vector3(50f, 50f, 50f);  // Dışardan ayarlanabilir alan max

    private Mesh mesh;
    private Matrix4x4[] matrices;

    void Start()
    {
        if (prefab == null)
        {
            Debug.LogError("Prefab atanmamış!");
            return;
        }

        // Prefab’den mesh’i al
        MeshFilter mf = prefab.GetComponent<MeshFilter>();
        if (mf == null)
        {
            Debug.LogError("Prefab’da MeshFilter bulunamadı!");
            return;
        }
        mesh = mf.sharedMesh;

        // Transform matrislerini oluştur
        matrices = new Matrix4x4[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            Vector3 pos = new Vector3(
                Random.Range(areaMin.x, areaMax.x),
                Random.Range(areaMin.y, areaMax.y),
                Random.Range(areaMin.z, areaMax.z)
            );
            matrices[i] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
        }
    }

    void Update()
    {
        if (mesh == null || material == null) return;

        int batchSize = 1023; // Max GPU instancing batch
        for (int i = 0; i < instanceCount; i += batchSize)
        {
            int count = Mathf.Min(batchSize, instanceCount - i);
            Graphics.DrawMeshInstanced(mesh, 0, material, matrices, count, null,
                UnityEngine.Rendering.ShadowCastingMode.On, true);
        }
    }
}

