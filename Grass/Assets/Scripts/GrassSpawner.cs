

// // [System.Serializable]
// // public struct GrassDataSerializable
// // {
// //     public Vector3 offset;
// //     public float scale;
// //     public Color color;
// // }

// // // [ExecuteAlways]
// // public class GrassSpawner : MonoBehaviour
// // {
// //     [Header("Çimen Ayarları")]
// //     public GameObject grassPrefab;
// //     public int grassCount = 1000;
// //     public Vector2 areaSize = new Vector2(10f, 10f);
// //     public float minScale = 0.8f;
// //     public float maxScale = 1.2f;

// //     private Mesh grassMesh;
// //     private Material grassMaterial;
// //     private ComputeBuffer dataBuffer;
// //     private ComputeBuffer colorBuffer;

// //     struct GrassData { public Vector3 offset; public float scale; }

// //     void OnEnable() => SetupBuffers();
// //     void OnDisable() { dataBuffer?.Release(); colorBuffer?.Release(); }

// //     void SetupBuffers()
// //     {
// //         if (!grassPrefab) return;

// //         ProBuilderMesh pb = grassPrefab.GetComponent<ProBuilderMesh>();
// //         if (pb == null)
// //         {
// //             Debug.LogError("ProBuilderMesh yok!");
// //             return;
// //         }

// //         // MeshFilter üzerinden al
// //         MeshFilter mf = pb.GetComponent<MeshFilter>();
// //         if (mf == null || mf.sharedMesh == null)
// //         {
// //             Debug.LogError("MeshFilter veya Mesh yok!");
// //             return;
// //         }

// //         Mesh mesh = mf.sharedMesh;
// //         grassMesh = mesh;


// //         if (grassMaterial == null)
// //         {
// //             Debug.LogError("Material atanmalı!");
// //             return;
// //         }

// //         GrassData[] instanceData = new GrassData[grassCount];
// //         Vector4[] instanceColors = new Vector4[grassCount];

// //         int gridSize = Mathf.CeilToInt(Mathf.Sqrt(grassCount));
// //         float cellX = areaSize.x / gridSize;
// //         float cellZ = areaSize.y / gridSize;

// //         int index = 0;
// //         for (int x = 0; x < gridSize; x++)
// //         {
// //             for (int z = 0; z < gridSize; z++)
// //             {
// //                 if (index >= grassCount) break;

// //                 float posX = (x + 0.5f) * cellX - areaSize.x / 2f;
// //                 float posZ = (z + 0.5f) * cellZ - areaSize.y / 2f;

// //                 instanceData[index].offset = transform.position + new Vector3(posX, 0f, posZ);
// //                 instanceData[index].scale = Random.Range(minScale, maxScale);

// //                 instanceColors[index] = Color.Lerp(new Color(0.2f,0.6f,0.2f,1), new Color(0.8f,1f,0.8f,1), Random.value);

// //                 index++;
// //             }
// //         }

// //         dataBuffer?.Release();
// //         dataBuffer = new ComputeBuffer(grassCount, sizeof(float) * 4);
// //         dataBuffer.SetData(instanceData);
// //         grassMaterial.SetBuffer("_InstanceData", dataBuffer);

// //         colorBuffer?.Release();
// //         colorBuffer = new ComputeBuffer(grassCount, sizeof(float) * 4);
// //         colorBuffer.SetData(instanceColors);
// //         grassMaterial.SetBuffer("_InstanceColor", colorBuffer);
// //     }

// //     void Update()
// //     {
// //         int batchSize = 1023;
// //         int remaining = grassCount;
// //         int offset = 0;

// //         while (remaining > 0)
// //         {
// //             int count = Mathf.Min(batchSize, remaining);
// //             Matrix4x4[] dummyMatrices = new Matrix4x4[count];
// //             for (int i = 0; i < count; i++) dummyMatrices[i] = Matrix4x4.identity;

// //             Graphics.DrawMeshInstanced(grassMesh, 0, grassMaterial, dummyMatrices, count);

// //             remaining -= count;
// //             offset += count;
// //         }
// //     }
// // }

// public class GrassSpawner : MonoBehaviour
// {
//     [Header("Çimen Ayarları")]
//     public GameObject grassPrefab;
//     public int grassCount = 1000;
//     public Vector2 areaSize = new Vector2(10f, 10f);
//     public float minScale = 0.8f;
//     public float maxScale = 1.2f;

//     [Header("Çimen Renk Ayarı")]
//     public Gradient grassGradient;

//     private Mesh grassMesh;
//     private Material grassMaterial;

//     struct GrassData { public Vector3 offset; public float scale; public Color color; }
//     private GrassData[] instanceData;

//     void OnEnable() => SetupBuffers();
//     void OnDisable() { }

//     void SetupBuffers()
//     {
//         if (!grassPrefab) return;

//         // Mesh al
//         ProBuilderMesh pb = grassPrefab.GetComponent<ProBuilderMesh>();
//         if (pb == null)
//         {
//             Debug.LogError("ProBuilderMesh yok!");
//             return;
//         }
//         MeshFilter mf = pb.GetComponent<MeshFilter>();
//         if (mf == null || mf.sharedMesh == null)
//         {
//             Debug.LogError("MeshFilter veya Mesh yok!");
//             return;
//         }

//         MeshRenderer mr = grassPrefab.GetComponent<MeshRenderer>();
//         if (mr == null || mr.sharedMaterial == null)
//         {
//             Debug.LogError("Material yok!");
//             return;
//         }

//         grassMesh = mf.sharedMesh;
//         grassMaterial = mr.sharedMaterial;

//         // Instance data oluştur
//         instanceData = new GrassData[grassCount];
//         int gridSize = Mathf.CeilToInt(Mathf.Sqrt(grassCount));
//         float cellX = areaSize.x / gridSize;
//         float cellZ = areaSize.y / gridSize;

//         int index = 0;
//         for (int x = 0; x < gridSize; x++)
//         {
//             for (int z = 0; z < gridSize; z++)
//             {
//                 if (index >= grassCount) break;

//                 float posX = (x + 0.5f) * cellX - areaSize.x / 2f;
//                 float posZ = (z + 0.5f) * cellZ - areaSize.y / 2f;

//                 instanceData[index].offset = transform.position + new Vector3(posX, 0f, posZ);
//                 instanceData[index].scale = Random.Range(minScale, maxScale);
//                 instanceData[index].color = grassGradient.Evaluate(Random.value);

//                 index++;
//             }
//         }

//         // Shader’a tek renk gönderebilirsin ya da array için ComputeBuffer gerek
//         // Örnek tek renk için:
//         grassMaterial.SetColor("_BaseColor", Color.white);
//     }

//     void Update()
//     {
//         if (!grassMesh || !grassMaterial) return;

//         int batchSize = 1023;
//         int remaining = grassCount;
//         int offset = 0;

//         while (remaining > 0)
//         {
//             int count = Mathf.Min(batchSize, remaining);
//             Matrix4x4[] dummyMatrices = new Matrix4x4[count];
//             for (int i = 0; i < count; i++) dummyMatrices[i] = Matrix4x4.identity;

//             Graphics.DrawMeshInstanced(grassMesh, 0, grassMaterial, dummyMatrices, count);

//             remaining -= count;
//             offset += count;
//         }
//     }
// }
using UnityEngine;
using UnityEngine.ProBuilder;

public class GrassSpawner : MonoBehaviour
{
    [Header("Çimen Ayarları")]
    public GameObject grassPrefab;
    public int grassCount = 1000;
    public Vector2 areaSize = new Vector2(10f, 10f);
    public float minScale = 0.8f;
    public float maxScale = 1.2f;
    public Gradient colorGradient; // Inspectörde ayarlanacak

    private Mesh grassMesh;
    private Material grassMaterial;
    private ComputeBuffer dataBuffer;
    private ComputeBuffer colorBuffer;

    struct GrassData { public Vector3 offset; public float scale; }

    void OnEnable() => SetupBuffers();
    void OnDisable() { dataBuffer?.Release(); colorBuffer?.Release(); }

    void SetupBuffers()
    {
        if (!grassPrefab) return;

        MeshFilter mf = grassPrefab.GetComponent<MeshFilter>();
        MeshRenderer mr = grassPrefab.GetComponent<MeshRenderer>();
        if (!mf || !mr) return;

        grassMesh = mf.sharedMesh;
        grassMaterial = mr.sharedMaterial;

        GrassData[] instanceData = new GrassData[grassCount];
        Vector4[] instanceColors = new Vector4[grassCount];

        int gridSize = Mathf.CeilToInt(Mathf.Sqrt(grassCount));
        float cellX = areaSize.x / gridSize;
        float cellZ = areaSize.y / gridSize;

        int index = 0;
        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                if (index >= grassCount) break;

                float posX = (x + 0.5f) * cellX - areaSize.x / 2f;
                float posZ = (z + 0.5f) * cellZ - areaSize.y / 2f;

                instanceData[index].offset = transform.position + new Vector3(posX, 0f, posZ);
                instanceData[index].scale = Random.Range(minScale, maxScale);

                float t = Random.value;
                instanceColors[index] = colorGradient.Evaluate(t);

                index++;
            }
        }

        dataBuffer?.Release();
        dataBuffer = new ComputeBuffer(grassCount, sizeof(float) * 4);
        dataBuffer.SetData(instanceData);
        grassMaterial.SetBuffer("_InstanceData", dataBuffer);

        colorBuffer?.Release();
        colorBuffer = new ComputeBuffer(grassCount, sizeof(float) * 4);
        colorBuffer.SetData(instanceColors);
        grassMaterial.SetBuffer("_InstanceColor", colorBuffer);
    }

    void Update()
    {
        int batchSize = 1023;
        int remaining = grassCount;
        int offset = 0;

        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        mpb.SetBuffer("_InstanceData", dataBuffer);
        mpb.SetBuffer("_InstanceColor", colorBuffer);

        while (remaining > 0)
        {
            int count = Mathf.Min(batchSize, remaining);
            Matrix4x4[] dummyMatrices = new Matrix4x4[count];
            for (int i = 0; i < count; i++) dummyMatrices[i] = Matrix4x4.identity;

            Graphics.DrawMeshInstanced(grassMesh, 0, grassMaterial, dummyMatrices, count, mpb);

            remaining -= count;
            offset += count;
        }
    }

}
