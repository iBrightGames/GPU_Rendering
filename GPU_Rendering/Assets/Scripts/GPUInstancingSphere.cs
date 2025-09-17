using UnityEngine;

public class GPUInstancingSphere : MonoBehaviour
{
    [Header("Prefab ve Material")]
    public GameObject prefab;           // Çoğaltmak istediğin prefab
    public Material material;
    [Header("Renk Ayarları")]
    public Gradient colorGradient; // Inspector'dan Gradient seçebilirsin

    // GPU instancing destekleyen material

    [Header("Instance Ayarları")]
    public int instanceCount = 10000;   // Kaç kopya
    public Vector3 center = Vector3.zero;   // Küre merkezi
    public float maxRadius = 50f;          // Kürenin maksimum yarıçapı

    private Mesh mesh;
    private Matrix4x4[] matrices;
    private Vector4[] colors;            // MaterialPropertyBlock için renk

    private MaterialPropertyBlock mpb;

    void Start()
    {
        if (prefab == null || material == null)
        {
            Debug.LogError("Prefab veya Material atanmamış!");
            return;
        }

        MeshFilter mf = prefab.GetComponent<MeshFilter>();
        if (mf == null)
        {
            Debug.LogError("Prefab’da MeshFilter bulunamadı!");
            return;
        }
        mesh = mf.sharedMesh;

        matrices = new Matrix4x4[instanceCount];
        colors = new Vector4[instanceCount];
        mpb = new MaterialPropertyBlock();

        GenerateInstances();
    }

    void GenerateInstances()
    {
        for (int i = 0; i < instanceCount; i++)
        {
            // Küre yüzeyinde rastgele yön
            Vector3 dir = Random.onUnitSphere;

            // Giderek artan mesafe
            float radius = (i / (float)instanceCount) * maxRadius;
            Vector3 pos = center + dir * radius;

            matrices[i] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);

            // Gradient kullanımı
            Color color = colorGradient.Evaluate(radius / maxRadius);
            colors[i] = color;
    
        }

        mpb.SetVectorArray("_Color", colors);
    }

    void Update()
    {
        if (mesh == null || material == null) return;

        int batchSize = 1023; // Max GPU instancing batch
        for (int i = 0; i < instanceCount; i += batchSize)
        {
            int count = Mathf.Min(batchSize, instanceCount - i);

            Graphics.DrawMeshInstanced(mesh, 0, material, matrices, count, mpb,
                UnityEngine.Rendering.ShadowCastingMode.On, true);
        }
    }
}
