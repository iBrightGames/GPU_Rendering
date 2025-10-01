
using UnityEngine;

public class GPUInstancingSystem : MonoBehaviour
{
    public static GPUInstancingSystem Instance { get; private set; }

    [SerializeField] private ComputeShader spawnCompute;
    [SerializeField] private SpawnVisualData spawnVisualData;
    [SerializeField] private SpawnAreaData spawnAreaData;
    [SerializeField] private SpawnCountData spawnCountData;

    // Ortak bufferlar
    public ComputeBuffer ArgsBuffer { get; private set; }
    public ComputeBuffer PositionsBuffer { get; private set; }
    public ComputeBuffer VisibilityBuffer { get; private set; }
    public ComputeBuffer AliveBuffer { get; private set; }
    public ComputeBuffer DeletionIndicesBuffer { get; private set; }
    public ComputeBuffer DeletionCountBuffer { get; private set; }
    public ComputeBuffer SpawnParamsBuffer { get; private set; }
    public ComputeShader SpawnCompute => spawnCompute;
    public SpawnVisualData SpawnVisualData => spawnVisualData;
    public SpawnAreaData SpawnAreaData => spawnAreaData;
    public SpawnCountData SpawnCountData => spawnCountData;
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitBuffers();
    }

    void InitBuffers()
    {
        int maxInstances = spawnCountData.maxInstances;

        PositionsBuffer        = new ComputeBuffer(maxInstances, sizeof(float) * 3);
        VisibilityBuffer       = new ComputeBuffer(maxInstances, sizeof(int));
        AliveBuffer            = new ComputeBuffer(maxInstances, sizeof(int));
        DeletionIndicesBuffer  = new ComputeBuffer(maxInstances, sizeof(int), ComputeBufferType.Append);
        DeletionCountBuffer    = new ComputeBuffer(1, sizeof(int));
        SpawnParamsBuffer      = new ComputeBuffer(1, sizeof(float) * 4);
        ArgsBuffer             = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    void OnDestroy()
    {
        PositionsBuffer?.Release();
        VisibilityBuffer?.Release();
        AliveBuffer?.Release();
        DeletionIndicesBuffer?.Release();
        DeletionCountBuffer?.Release();
        SpawnParamsBuffer?.Release();
        ArgsBuffer?.Release();
    }


    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    Mesh instanceMesh;
    Material instanceMaterial;
    int maxInstances;

    void Start()
    {
        instanceMesh = spawnVisualData.mesh;
        instanceMaterial = spawnVisualData.material;

        ArgsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        args[0] = (uint)instanceMesh.GetIndexCount(0);
        args[1] = (uint)maxInstances;
        ArgsBuffer.SetData(args);
    }

    void Update()
    {
        // ComputeShader dispatch
        int kernel = 0; // kernel adı ComputeShader’a göre
        int threadGroups = Mathf.CeilToInt(maxInstances / 64f);
        spawnCompute.Dispatch(kernel, threadGroups, 1, 1);

        // GPU instancing ile çizim
        // instanceMaterial.SetBuffer("positions", positionBuffer);
        Graphics.DrawMeshInstancedIndirect(instanceMesh, 0, instanceMaterial, new Bounds(Vector3.zero, Vector3.one * 1000), ArgsBuffer);
    }
    private void SetComputeParameters(int kernel, int batchSize, int startIndex)
    {
        spawnCompute.SetInt("PointCount", batchSize);
        spawnCompute.SetInt("RandomSeed", startIndex);

        switch (spawnAreaData)
        {
            case BoxSpawnData box:
                spawnCompute.SetVector("Data1", box.minPosition);
                spawnCompute.SetVector("Data2", box.maxPosition);
                spawnCompute.SetVector("Data3", new Vector3(box.layerCount, 0, 0));
                break;

            case SphereSpawnData sphere:
                spawnCompute.SetVector("Data1", sphere.center);
                spawnCompute.SetVector("Data2", new Vector3(sphere.maxRadius, 0, 0));
                spawnCompute.SetVector("Data3", new Vector3(sphere.layerCount, sphere.startRadius, 0));
                break;
        }
    }
    void OnDrawGizmos()
    {
        spawnAreaData.DrawGizmos(transform.position);
    }

}

