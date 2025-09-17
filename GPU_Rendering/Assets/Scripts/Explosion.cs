using UnityEngine;

public class GPUInstancingExplosion : MonoBehaviour
{
    [Header("Prefab ve Material")]
    public GameObject prefab;           // Çoğaltmak istediğin prefab
    public Material material;           // GPU instancing destekleyen material

    [Header("Instance Ayarları")]
    public int instanceCount = 10000;   // Kaç kopya
    public Vector3 center = Vector3.zero;   // Patlama merkezi
    public float maxRadius = 50f;          // Başlangıçta küre yayılımı
    public float explosionSpeed = 10f;     // Patlama hızı

    [Header("Renk Ayarları")]
    public Gradient colorGradient;     // Inspector'dan Gradient seçebilirsin

    private Mesh mesh;
    private Matrix4x4[] matrices;
    private Vector4[] colors;            // MaterialPropertyBlock için renk
    private MaterialPropertyBlock mpb;

    private Vector3[] directions;        // Patlama yönleri
    private float[] speeds;              // Her instance hızı (isteğe göre random)

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
        directions = new Vector3[instanceCount];
        speeds = new float[instanceCount];
        mpb = new MaterialPropertyBlock();

        GenerateInstances();
    }

    void GenerateInstances()
    {
        for (int i = 0; i < instanceCount; i++)
        {
            // Küre yüzeyinde rastgele yön
            Vector3 dir = Random.onUnitSphere;
            directions[i] = dir.normalized;

            // Başlangıç radius
            float radius = (i / (float)instanceCount) * maxRadius;
            Vector3 pos = center + dir * radius;
            matrices[i] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);

            // Renk: Gradient ile
            colors[i] = colorGradient.Evaluate(radius / maxRadius);

            // Hız: biraz rastgele olabilir
            speeds[i] = explosionSpeed * Random.Range(0.8f, 1.2f);
        }

        mpb.SetVectorArray("_Color", colors);
    }

    void Update()
    {
        if (mesh == null || material == null) return;

        // Pozisyonları güncelle (patlama)
        for (int i = 0; i < instanceCount; i++)
        {
            Vector3 pos = matrices[i].GetColumn(3); // mevcut pozisyon
            pos += directions[i] * speeds[i] * Time.deltaTime;
            matrices[i].SetColumn(3, new Vector4(pos.x, pos.y, pos.z, 1f));
        }

        // Batch çizim
        int batchSize = 1023;
        for (int i = 0; i < instanceCount; i += batchSize)
        {
            int count = Mathf.Min(batchSize, instanceCount - i);
            Graphics.DrawMeshInstanced(mesh, 0, material, matrices, count, mpb,
                UnityEngine.Rendering.ShadowCastingMode.On, true);
        }
    }
}

