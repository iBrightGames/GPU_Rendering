
using System.Runtime.InteropServices;
using UnityEngine;

public class SimpleScaling : MonoBehaviour
{
    [SerializeField] ComputeShader simpleScalingCompute;
    [SerializeField] Material material;
    [SerializeField] Mesh mesh;
    [SerializeField][Range(1, 100)] int x_Count;
    [SerializeField][Range(1, 100)] int y_Count;
    [SerializeField][Range(1, 100)] int z_Count;
    [SerializeField][Range(0.1f, 10f)] float itemSize;
    [SerializeField] Color itemColor;

    struct ItemData
    {
        public Vector3 position;
        public float size;
        public Color color;
    }

    int maxItemSize = 1000000;
    int spawnCount;
    ComputeBuffer itemBuffer;
    ComputeBuffer argsBuffer;
    int kernel;
    uint[] args = new uint[5];

    void Start()
    {
        spawnCount = x_Count * y_Count * z_Count;

        // Item buffer oluştur
        int stride = Marshal.SizeOf(typeof(ItemData));
        itemBuffer = new ComputeBuffer(maxItemSize, stride);

        ItemData[] initialData = new ItemData[maxItemSize];
        for (int i = 0; i < spawnCount; i++)
        {
            initialData[i].size = itemSize;
            initialData[i].color = itemColor;
        }
        itemBuffer.SetData(initialData);

        // Compute shader kernel
        kernel = simpleScalingCompute.FindKernel("SimpleScaling");
        simpleScalingCompute.SetBuffer(kernel, "itemBuffer", itemBuffer);

        simpleScalingCompute.SetVector("color", itemColor);
        simpleScalingCompute.SetFloat("size", itemSize);
        simpleScalingCompute.SetInt("x_Count", x_Count);
        simpleScalingCompute.SetInt("y_Count", y_Count);
        simpleScalingCompute.SetInt("z_Count", z_Count);

        int threadGroups = Mathf.CeilToInt(spawnCount / 64f);
        threadGroups = Mathf.Max(1, threadGroups);
        simpleScalingCompute.Dispatch(kernel, threadGroups, 1, 1);

        // Indirect draw args buffer
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        args[0] = mesh.GetIndexCount(0);
        args[1] = (uint)spawnCount;
        args[2] = mesh.GetIndexStart(0);
        args[3] = mesh.GetBaseVertex(0);
        args[4] = 0;
        argsBuffer.SetData(args);

        material.SetBuffer("itemBuffer", itemBuffer);
    }

    void Update()
    {
        spawnCount = x_Count * y_Count * z_Count;
        if (spawnCount > maxItemSize) spawnCount = maxItemSize;

        // Compute shader parametreleri
        simpleScalingCompute.SetInt("x_Count", x_Count);
        simpleScalingCompute.SetInt("y_Count", y_Count);
        simpleScalingCompute.SetInt("z_Count", z_Count);
        simpleScalingCompute.SetFloat("size", itemSize);

        int threadGroups = Mathf.CeilToInt(spawnCount / 64f);
        threadGroups = Mathf.Max(1, threadGroups);
        simpleScalingCompute.Dispatch(kernel, threadGroups, 1, 1);

        // argsBuffer sadece spawnCount değişirse set et
        args[1] = (uint)spawnCount;
        argsBuffer.SetData(args);

        // Draw
        Graphics.DrawMeshInstancedIndirect(mesh, 0, material,
            new Bounds(Vector3.zero, new Vector3(x_Count, y_Count, z_Count) * itemSize),
            argsBuffer);
    }

    void OnDisable()
    {
        itemBuffer?.Release();
        itemBuffer = null;
        argsBuffer?.Release();
        argsBuffer = null;
    }
}
