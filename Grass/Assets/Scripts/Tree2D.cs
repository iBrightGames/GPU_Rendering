// // using UnityEngine;
// // using UnityEngine.Rendering;

// // public class Tree2DGenerator : MonoBehaviour
// // {
// //     public Mesh leafMesh;
// //     public Material leafMaterial;
// //     public int leafCount = 200;

// //     public int branchCount = 20;
// //     public float branchLength = 1f;
// //     public float treeRadius = 2f;

// //     ComputeBuffer leafBuffer;

// //     struct LeafData
// //     {
// //         public Vector3 position;
// //         public float scale;
// //         public float isBranch; // 0 = yaprak, 1 = dal
// //     }

// //     void Start()
// //     {
// //         LeafData[] leaves = new LeafData[leafCount + branchCount];

// //         // Branch generation
// //         for (int i = 0; i < branchCount; i++)
// //         {
// //             float angle = Random.Range(0f, Mathf.PI * 2f);
// //             float radius = Random.Range(0f, treeRadius * 0.7f);
// //             Vector3 pos = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
// //             leaves[i] = new LeafData { position = pos, scale = branchLength * Random.Range(0.8f,1.2f), isBranch = 1f };
// //         }

// //         // Leaf generation
// //         for (int i = 0; i < leafCount; i++)
// //         {
// //             float angle = Random.Range(0f, Mathf.PI * 2f);
// //             float radius = Random.Range(treeRadius * 0.5f, treeRadius);
// //             Vector3 pos = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
// //             leaves[i + branchCount] = new LeafData { position = pos, scale = Random.Range(0.05f, 0.15f), isBranch = 0f };
// //         }

// //         leafBuffer = new ComputeBuffer(leaves.Length, sizeof(float) * 5);
// //         leafBuffer.SetData(leaves);
// //         leafMaterial.SetBuffer("_LeafBuffer", leafBuffer);
// //     }

// //     void OnRenderObject()
// //     {
// //         leafMaterial.SetPass(0);
// //         Graphics.DrawMeshInstancedProcedural(leafMesh, 0, leafMaterial, new Bounds(Vector3.zero, Vector3.one * 10f), leafCount + branchCount);
// //     }

// //     void OnDestroy()
// //     {
// //         if (leafBuffer != null)
// //             leafBuffer.Release();
// //     }
// // }
// using UnityEngine;
// using UnityEngine.Rendering;

// public class Tree2DGPU : MonoBehaviour
// {
//     public Mesh leafMesh;        // Küçük quad
//     public Mesh branchMesh;      // İnce dikdörtgen
//     public Material treeMaterial;

//     public int branchCount = 20;
//     public int leafCount = 200;
//     public float treeRadius = 2f;

//     ComputeBuffer treeBuffer;

//     struct TreeData
//     {
//         public Vector3 position;
//         public float scale;
//         public float rotation;
//         public float isBranch; // 1 = dal, 0 = yaprak
//     }

//     void Start()
//     {
//         TreeData[] data = new TreeData[branchCount + leafCount];

//         // Branch generation (ince dikdörtgen)
//         for (int i = 0; i < branchCount; i++)
//         {
//             float angle = Random.Range(0f, Mathf.PI * 2f);
//             float radius = Random.Range(0f, treeRadius * 0.7f);
//             Vector3 pos = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
//             float scale = Random.Range(0.1f, 0.2f);
//             data[i] = new TreeData { position = pos, scale = scale, rotation = angle, isBranch = 1f };
//         }

//         // Leaf generation
//         for (int i = 0; i < leafCount; i++)
//         {
//             float angle = Random.Range(0f, Mathf.PI * 2f);
//             float radius = Random.Range(treeRadius * 0.5f, treeRadius);
//             Vector3 pos = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
//             float scale = Random.Range(0.05f, 0.15f);
//             data[i + branchCount] = new TreeData { position = pos, scale = scale, rotation = 0f, isBranch = 0f };
//         }

//         treeBuffer = new ComputeBuffer(data.Length, sizeof(float) * 6);
//         treeBuffer.SetData(data);
//         treeMaterial.SetBuffer("_TreeBuffer", treeBuffer);
//     }

//     void OnRenderObject()
//     {
//         treeMaterial.SetPass(0);
//         Graphics.DrawMeshInstancedProcedural(branchMesh, 0, treeMaterial, new Bounds(Vector3.zero, Vector3.one * 10f), branchCount);
//         Graphics.DrawMeshInstancedProcedural(leafMesh, 0, treeMaterial, new Bounds(Vector3.zero, Vector3.one * 10f), leafCount);
//     }

//     void OnDestroy()
//     {
//         if (treeBuffer != null)
//             treeBuffer.Release();
//     }
// }
using UnityEngine;
using UnityEngine.Rendering;

public class CircleGPU : MonoBehaviour
{
    public Material circleMaterial;
    public int pointCount = 100; // Daire üzerindeki nokta sayısı
    public float radius = 2f;

    ComputeBuffer circleBuffer;

    struct CirclePoint
    {
        public Vector3 position;
    }

    void Start()
    {
        CirclePoint[] points = new CirclePoint[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            float angle = (i / (float)pointCount) * Mathf.PI * 2f;
            points[i].position = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
        }

        circleBuffer = new ComputeBuffer(points.Length, sizeof(float) * 3);
        circleBuffer.SetData(points);
        circleMaterial.SetBuffer("_CircleBuffer", circleBuffer);
    }

    void OnRenderObject()
    {
        circleMaterial.SetPass(0);
        Graphics.DrawProceduralNow(MeshTopology.LineStrip, pointCount);
    }

    void OnDestroy()
    {
        if (circleBuffer != null)
            circleBuffer.Release();
    }
}
