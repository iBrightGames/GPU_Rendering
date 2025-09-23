// using UnityEngine;

// public class GPUInstancedMatrixGenerator : MonoBehaviour
// {
//     public ComputeShader computeShader;
//     public int instanceCount = 1000;

//     public Vector3 areaSize = new Vector3(50, 0, 50);
//     public Vector3 rotationMin = Vector3.zero;
//     public Vector3 rotationMax = new Vector3(0, 360, 0);
//     public float scaleMin = 1f;
//     public float scaleMax = 1f;

//     public GameObject prefab;
//      Mesh mesh;
//     public Material material;

//     private ComputeBuffer matrixBuffer;

//     void Start()
//     {
//         var mf = prefab.GetComponent<MeshFilter>();
//         mesh = mf.sharedMesh;
//         matrixBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 16); // 4x4 matrix
//         UpdateMatrices();
//     }

//     void UpdateMatrices()
//     {
//         int kernel = computeShader.FindKernel("CSMain");

//         computeShader.SetInt("instanceCount", instanceCount);
//         computeShader.SetVector("areaSize", areaSize);
//         computeShader.SetVector("rotationMin", rotationMin);
//         computeShader.SetVector("rotationMax", rotationMax);
//         computeShader.SetFloat("scaleMin", scaleMin);
//         computeShader.SetFloat("scaleMax", scaleMax);
//         computeShader.SetBuffer(kernel, "matrices", matrixBuffer);

//         int threadGroups = Mathf.CeilToInt(instanceCount / 64f);
//         computeShader.Dispatch(kernel, threadGroups, 1, 1);

//         material.SetBuffer("_Matrices", matrixBuffer);
//     }

//     void Update()
//     {
//         Graphics.DrawMeshInstancedProcedural(mesh, 0, material, new Bounds(Vector3.zero, Vector3.one * 1000), instanceCount);
//     }

//     void OnDisable()
//     {
//         if (matrixBuffer != null)
//         {
//             matrixBuffer.Release();
//         }
//     }
// }
using UnityEngine;

public class GPUInstancedMatrixGenerator : MonoBehaviour
{
    [Header("GPU Setup")]
    public ComputeShader computeShader;
    public int instanceCount = 1000;

    [Header("Instance Settings")]
    public Vector3 areaSize = new Vector3(50,0,50);
    public Vector3 rotationMin = Vector3.zero;
    public Vector3 rotationMax = new Vector3(0,360,0);
    public float scaleMin = 1f;
    public float scaleMax = 1f;

    [Header("Rendering")]
    public Material material;

    private ComputeBuffer matrixBuffer;
public GameObject prefab; // Inspector’dan prefab ata
private Mesh mesh;
// private Material material;

void Start()
{
    if(prefab == null)
    {
        Debug.LogError("Prefab atanmadı!");
        return;
    }

    MeshFilter mf = prefab.GetComponent<MeshFilter>();
    MeshRenderer mr = prefab.GetComponent<MeshRenderer>();
    if(mf == null || mr == null)
    {
        Debug.LogError("Prefab MeshFilter veya MeshRenderer içermiyor!");
        return;
    }

    mesh = mf.sharedMesh;
    material = mr.sharedMaterial;

    matrixBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 16);
    UpdateMatrices();

        if(mesh == null || material == null || computeShader == null)
        {
            Debug.LogError("Mesh, Material veya ComputeShader atanmadı!");
            return;
        }

        // Buffer oluştur
        matrixBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 16);

        UpdateMatrices();
    }

    void UpdateMatrices()
    {
        int kernel = computeShader.FindKernel("CSMain");
        computeShader.SetInt("instanceCount", instanceCount);
        computeShader.SetVector("areaSize", areaSize);
        computeShader.SetVector("rotationMin", rotationMin);
        computeShader.SetVector("rotationMax", rotationMax);
        computeShader.SetFloat("scaleMin", scaleMin);
        computeShader.SetFloat("scaleMax", scaleMax);
        computeShader.SetBuffer(kernel, "matrices", matrixBuffer);

        int threadGroups = Mathf.CeilToInt(instanceCount / 64f);
        computeShader.Dispatch(kernel, threadGroups, 1, 1);

        material.SetBuffer("_Matrices", matrixBuffer);
    }

    void Update()
    {
        if(matrixBuffer != null)
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            Graphics.DrawMeshInstancedProcedural(mesh, 0, material, bounds, instanceCount);
        }
    }

    void OnDisable()
    {
        if(matrixBuffer != null)
        {
            matrixBuffer.Release();
            matrixBuffer = null;
        }
    }
}
