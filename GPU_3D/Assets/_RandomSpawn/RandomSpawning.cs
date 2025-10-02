using UnityEngine;
using System.Runtime.InteropServices;


public class RandomSpawning : MonoBehaviour
{
    [SerializeField] GameObject spawnPrefab;
    [SerializeField] ComputeShader randomSpawningCompute;

    [SerializeField] [Range(0,100000)]
    int spawnCount=100000;

    [SerializeField] [Range(0,1000)]
    int x_Length=100;
    [SerializeField] [Range(0,1000)]
    int y_Length=100;
    [SerializeField] [Range(0,1000)]
    int z_Length=100;

    Vector3 center;
    Mesh spawnMesh;
    Material spawnMaterial;

    ComputeBuffer randomItemBuffer;
    ComputeBuffer argsBuffer;
    uint[] args=new uint[5]{0,0,0,0,0};

    
    struct ItemData 
    {
        public Vector4 position;

    }

    void Start()
    {
        InitializeComponents();
        InitializeArg();
        InitializeBuffers();
    }

    void InitializeComponents()
    {
        center=transform.position;

        if(spawnPrefab==null)
        {throw new System.Exception("No prefab");}

        var renderer=spawnPrefab.GetComponent<Renderer>();
        if (renderer==null)
        {throw new System.Exception("No renderer");}
        spawnMaterial=renderer.sharedMaterial;

        var mf=spawnPrefab.GetComponent<MeshFilter>();
        if(mf==null)
        {throw new System.Exception("No mf");}
        spawnMesh=mf.mesh;
        

    }
    void InitializeBuffers()
    {
        int stride=Marshal.SizeOf(typeof(ItemData));
        randomItemBuffer= new ComputeBuffer(spawnCount,stride);
        ItemData[] initialData=new ItemData[spawnCount];
        randomItemBuffer.SetData(initialData);

        int kernel = randomSpawningCompute.FindKernel("RandomSpawning");
        randomSpawningCompute.SetBuffer(kernel,"itemBuffer", randomItemBuffer);
        randomSpawningCompute.SetInt("x_Length", x_Length);
        randomSpawningCompute.SetInt("y_Length",y_Length);
        randomSpawningCompute.SetInt("z_Length", z_Length);
        randomSpawningCompute.SetInt("spawnCount",spawnCount);
        randomSpawningCompute.SetVector("center",center);
        int threadGroups=Mathf.CeilToInt((float)spawnCount/64.0f);
        randomSpawningCompute.Dispatch(kernel,threadGroups,1,1);

        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);

        spawnMaterial.SetBuffer("itemBuffer", randomItemBuffer);

    }
    void InitializeArg()
    {
        args[0]=(uint)spawnMesh.GetIndexCount(0);
        args[1]=(uint)spawnCount;
        args[2]=(uint)spawnMesh.GetIndexStart(0);
        args[3]=(uint)spawnMesh.GetBaseVertex(0);
        args[4]=0;

    }
    void Update()
    {
        if (spawnMesh == null) Debug.LogError("spawnMesh null");
        if (spawnMaterial == null) Debug.LogError("spawnMaterial null");
        if (argsBuffer == null) Debug.LogError("argsBuffer null");


        Vector3 boundsCenter = center + new Vector3(x_Length, y_Length, z_Length) / 2.0f; 
        Bounds bounds = new Bounds(boundsCenter, new Vector3(x_Length, y_Length, z_Length));
        if(argsBuffer==null)
        {
            argsBuffer.SetData(args);
        }
        Graphics.DrawMeshInstancedIndirect(spawnMesh, 0, spawnMaterial,bounds, argsBuffer);

    }
    void OnDisable(){
        randomItemBuffer?.Release();
        argsBuffer?.Release();
    }

    
    void OnDrawGizmos()
    {
        Gizmos.color=Color.green;
        Gizmos.DrawWireCube(transform.position,new Vector3(x_Length,y_Length,z_Length));
        
    }
}
