
using UnityEngine;
using UnityEngine.ProBuilder;

public class LeafInstancer : MonoBehaviour
{
    [Header("References")]
    public GameObject source;   // ProBuilder Sphere
    public GameObject leaf;     // ProBuilder Leaf
    public Material leafMaterial; // Instancing destekli shader

    Mesh sourceMesh;
    Mesh leafMesh;

    ComputeBuffer positionsBuffer;
    ComputeBuffer argsBuffer;

    int instanceCount;

    void Start()
    {
        // Source mesh al
        var pb = source.GetComponent<ProBuilderMesh>();
        if (pb != null)
            sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;

        // Leaf mesh al
        leafMesh = leaf.GetComponent<MeshFilter>().sharedMesh;

        if (sourceMesh == null || leafMesh == null || leafMaterial == null)
        {
            Debug.LogError("Eksik referans!");
            return;
        }

        // Sphere mesh vertexlerini al
        Vector3[] vertices = sourceMesh.vertices;
        instanceCount = vertices.Length;

        // Vertex pozisyonlarını GPU buffer’a koy
        positionsBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 3);
        positionsBuffer.SetData(vertices);

        // Shader’a pozisyonları ver
        leafMaterial.SetBuffer("_Positions", positionsBuffer);

        // Indirect args buffer (DrawMeshInstancedIndirect için)
        uint[] args = new uint[5];
        args[0] = (uint)leafMesh.GetIndexCount(0);
        args[1] = (uint)instanceCount;
        args[2] = (uint)leafMesh.GetIndexStart(0);
        args[3] = (uint)leafMesh.GetBaseVertex(0);
        args[4] = 0;

        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);
    }

    void Update()
    {
        if (positionsBuffer == null || argsBuffer == null) return;

        Graphics.DrawMeshInstancedIndirect(
            leafMesh,
            0,
            leafMaterial,
            new Bounds(Vector3.zero, Vector3.one * 100f),
            argsBuffer
        );
    }

    void OnDestroy()
    {
        positionsBuffer?.Release();
        argsBuffer?.Release();
    }
}
