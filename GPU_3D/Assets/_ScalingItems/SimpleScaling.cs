using System.Runtime.InteropServices;
using UnityEngine;

public class SimpleScaling : MonoBehaviour
{
    [SerializeField] ComputeShader simpleScalingCompute;
    [SerializeField] GameObject prefab;
    [SerializeField] Material material;
    [SerializeField] Mesh mesh;
    [SerializeField][Range(1, 100)] int x_Count;
    [SerializeField][Range(1, 100)] int y_Count;
    [SerializeField][Range(1, 100)] int z_Count;
    [SerializeField][Range(0.1f, 10f)] float itemSize;
    [SerializeField] Color itemColor;
    struct ItemData
    {
        Vector3 position;
        float size;
        Color color;
    };
    int spawnCount;
    ComputeBuffer itemBuffer;
    ComputeBuffer argsBuffer;
    int kernel;
    uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    void Start()
    {
        spawnCount = x_Count * y_Count * z_Count;
        InitializeArg();
        InitializeBuffers();
    }


    void InitializeBuffers()
    {
        int stride=Marshal.SizeOf(typeof(ItemData));
        itemBuffer= new ComputeBuffer(spawnCount,stride);
        ItemData[] initialData=new ItemData[spawnCount];
        itemBuffer.SetData(initialData);

        kernel = simpleScalingCompute.FindKernel("SimpleScaling");

        simpleScalingCompute.SetBuffer(kernel,"itemBuffer", itemBuffer);
        
        simpleScalingCompute.SetVector("color", itemColor);
        simpleScalingCompute.SetFloat("size", itemSize);
        simpleScalingCompute.SetInt("x_Count",x_Count);
        simpleScalingCompute.SetInt("y_Count",y_Count);
        simpleScalingCompute.SetInt("z_Count",z_Count);
        int threadGroups=Mathf.CeilToInt(spawnCount/64f);
        simpleScalingCompute.Dispatch(kernel,threadGroups,1,1);

        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);
        material.SetBuffer("itemBuffer", itemBuffer);

    }
    void InitializeArg()
    {
        args[0]=mesh.GetIndexCount(0);
        args[1]=(uint)spawnCount;
        args[2]=mesh.GetIndexStart(0);
        args[3]=mesh.GetBaseVertex(0);
        args[4]=0;

    }
    void Update()
    {
        if (mesh == null) Debug.LogError("spawnMesh null");
        if (material == null) Debug.LogError("spawnMaterial null");
        if (argsBuffer == null) Debug.LogError("argsBuffer null");

        simpleScalingCompute.SetBuffer(kernel,"itemBuffer", itemBuffer);
        simpleScalingCompute.SetInt("x_Count",x_Count);
        simpleScalingCompute.SetInt("y_Count",y_Count);
        simpleScalingCompute.SetInt("z_Count",z_Count);
        simpleScalingCompute.SetFloat("size", itemSize);
        int threadGroups=Mathf.CeilToInt(spawnCount/64f);
        simpleScalingCompute.Dispatch(kernel,threadGroups,1,1);

        material.SetFloat("_Size", itemSize);
        if(argsBuffer==null)
        {
            argsBuffer.SetData(args);
        }
        Graphics.DrawMeshInstancedIndirect(mesh, 0, material,new Bounds(Vector3.zero, Vector3.one * spawnCount * itemSize), argsBuffer);

    }
    void OnDisable(){
        itemBuffer?.Release();
        argsBuffer?.Release();
    }

}