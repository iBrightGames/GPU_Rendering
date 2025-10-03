using UnityEngine;
using UnityEngine.Rendering;

public class BallManager : MonoBehaviour
{
    [SerializeField] ComputeShader compute;
    [SerializeField] Mesh mesh;
    [SerializeField] Material material;

    [SerializeField] int ballCount = 10000;
    [SerializeField] Vector3Int gridSize = new Vector3Int(50, 50, 50);
    [SerializeField] float cellSize = 1f;

    ComputeBuffer itemBuffer, gridHeads, gridNext, renderBuffer, argsBuffer;
    int[] clearHeads;

    struct ItemData {
        public Vector3 position;
        public float radius;
    }

    int kernelBuild, kernelResolve, kernelGrow, kernelPrepare;

    void Start()
    {
        if (compute == null || mesh == null || material == null) {
            Debug.LogError("ComputeShader, Mesh or Material is null. Disable script.");
            enabled = false;
            return;
        }

        // Kernels
        kernelBuild   = compute.FindKernel("BuildGrid");
        kernelResolve = compute.FindKernel("ResolveCollisions");
        kernelGrow    = compute.FindKernel("GrowBalls");
        kernelPrepare = compute.FindKernel("PrepareRender");

        // Items init
        ItemData[] items = new ItemData[ballCount];
        for (int i = 0; i < ballCount; i++) {
            items[i].position = new Vector3(
                Random.Range(0f, gridSize.x * cellSize),
                Random.Range(0f, gridSize.y * cellSize),
                Random.Range(0f, gridSize.z * cellSize)
            );
            items[i].radius = 0.5f;
        }

        itemBuffer = new ComputeBuffer(ballCount, sizeof(float)*4);
        itemBuffer.SetData(items);

        int gridCount = gridSize.x * gridSize.y * gridSize.z;
        gridHeads = new ComputeBuffer(gridCount, sizeof(int));
        gridNext  = new ComputeBuffer(ballCount, sizeof(int));

        // reusable clear array
        clearHeads = new int[gridCount];
        for (int i = 0; i < gridCount; i++) clearHeads[i] = -1;

        renderBuffer = new ComputeBuffer(ballCount, sizeof(float) * 16);

        // Args buffer
        uint[] args = new uint[5] {
            mesh.GetIndexCount(0),
            (uint)ballCount,
            mesh.GetIndexStart(0),
            mesh.GetBaseVertex(0),
            0
        };
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);

        material.SetBuffer("renderBuffer", renderBuffer);
    }

    void Update()
    {
        int gridCount = gridSize.x * gridSize.y * gridSize.z;

        // Clear gridHeads (reuse array)
        gridHeads.SetData(clearHeads);

        // Params - ensure types match shader
        compute.SetInt("itemCount", ballCount);
        compute.SetInts("gridDims", new int[]{gridSize.x, gridSize.y, gridSize.z});
        compute.SetFloat("cellSize", cellSize);
        compute.SetFloat("deltaTime", Time.deltaTime);
        compute.SetFloat("growthRate", 0.1f);

        // Buffers set
        compute.SetBuffer(kernelBuild, "itemBuffer", itemBuffer);
        compute.SetBuffer(kernelBuild, "gridHeads", gridHeads);
        compute.SetBuffer(kernelBuild, "gridNext", gridNext);

        compute.SetBuffer(kernelResolve, "itemBuffer", itemBuffer);
        compute.SetBuffer(kernelResolve, "gridHeads", gridHeads);
        compute.SetBuffer(kernelResolve, "gridNext", gridNext);

        compute.SetBuffer(kernelGrow, "itemBuffer", itemBuffer);

        compute.SetBuffer(kernelPrepare, "itemBuffer", itemBuffer);
        compute.SetBuffer(kernelPrepare, "renderBuffer", renderBuffer);

        // Dispatch (ensure >=1)
        int groups = Mathf.Max(1, Mathf.CeilToInt(ballCount / 256f));
        compute.Dispatch(kernelBuild, groups, 1, 1);
        compute.Dispatch(kernelResolve, groups, 1, 1);
        compute.Dispatch(kernelGrow, groups, 1, 1);
        compute.Dispatch(kernelPrepare, groups, 1, 1);
        
        // Render
        Bounds b = new Bounds(new Vector3(gridSize.x * cellSize, gridSize.y * cellSize, gridSize.z * cellSize) * 0.5f,
                              new Vector3(gridSize.x * cellSize, gridSize.y * cellSize, gridSize.z * cellSize));
        Graphics.DrawMeshInstancedIndirect(mesh, 0, material, b, argsBuffer);
    }

    void OnDestroy()
    {
        itemBuffer?.Release();
        gridHeads?.Release();
        gridNext?.Release();
        renderBuffer?.Release();
        argsBuffer?.Release();
    }
}
