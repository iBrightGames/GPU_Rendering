
// using System.Collections.Generic;
// using UnityEngine;
// using UnityEngine.Rendering;

// public class Eraser : MonoBehaviour
// {
//     public GameObject Player;
//     public SpriteRenderer sourceSprite;
//     [Range(1000, 100000)] public int maxInstances = 10000;
//     public float eraseRadius = 10;
//     public float scaleMultiplier = 1f;
//     public Vector3 minPosition = new Vector3(-100f, 0, -100f);
//     public Vector3 maxPosition = new Vector3(100f, 0, 100f);

//     public ComputeShader filterComputeShader;
//     public Shader shader;
//     public Material orijinalMaterial;

//     // Silinen noktaları CPU'da takip etmek için
//     private List<Vector3> erasedPos = new List<Vector3>();
//     private InstanceData[] cpuInstanceData;

//     Mesh quadMesh;
//     ComputeBuffer originalBuffer;
//     ComputeBuffer activeBuffer; // Yeni: Aktif instance'ları tutar
//     ComputeBuffer argsBuffer;
//     ComputeBuffer filteredBuffer; // Silinen instance'ları tutar

//     struct InstanceData
//     {
//         public Vector3 position;
//         public int active;
//     }

//     bool bufferInitialized = false;
//     int stride;
//     uint[] argsData = new uint[5] { 0, 0, 0, 0, 0 };

//     void Start()
//     {
//         ValidateEntry();
//         if (sourceSprite != null) sourceSprite.enabled = false;

//         stride = sizeof(float) * 3 + sizeof(int);

//         originalBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Structured);
//         activeBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
//         filteredBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
//         argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);

//         cpuInstanceData = new InstanceData[maxInstances];

//         bufferInitialized = true;
//         GeneratePositionsDirectlyToBuffer();
//         UpdateArgsBuffer();
//     }

//     void ValidateEntry()
//     {
//         if (sourceSprite == null || sourceSprite.sprite == null)
//         {
//             Debug.LogError("Source sprite is missing!");
//             return;
//         }

//         if (Player == null) Debug.LogWarning("Player is null in Eraser.");

//         quadMesh = CreateQuad();
//         if (shader == null)
//             shader = Shader.Find("CustomUnlit/SingleSpriteCompute_GPUActive");

//         if (orijinalMaterial == null)
//         {
//             orijinalMaterial = new Material(shader);
//             orijinalMaterial.mainTexture = sourceSprite.sprite.texture;
//             orijinalMaterial.enableInstancing = true;
//         }
//     }

//     void GeneratePositionsDirectlyToBuffer()
//     {
//         if (originalBuffer == null) return;

//         for (int i = 0; i < maxInstances; i++)
//         {
//             cpuInstanceData[i] = new InstanceData
//             {
//                 position = new Vector3(
//                     Random.Range(minPosition.x, maxPosition.x),
//                     Random.Range(minPosition.y, maxPosition.y),
//                     Random.Range(minPosition.z, maxPosition.z)),
//                 active = 1
//             };
//         }
//         originalBuffer.SetData(cpuInstanceData);
//     }

//     // Bu metod Start'ta çağrılır ve argümanların temelini oluşturur.
//     void UpdateArgsBuffer()
//     {
//         if (quadMesh != null)
//         {
//             argsData[0] = quadMesh.GetIndexCount(0);
//             argsData[1] = (uint)maxInstances; // Bu değer Update içinde dinamikleşecek
//             argsData[2] = quadMesh.GetIndexStart(0);
//             argsData[3] = quadMesh.GetBaseVertex(0);
//         }
//         argsBuffer.SetData(argsData);
//     }

//     void Update()
//     {
//         if (!bufferInitialized || Player == null) return;

//         Vector3 playerPos = Player.transform.position;

//         // Her iki append buffer'ı da sıfırla
//         activeBuffer.SetCounterValue(0);
//         filteredBuffer.SetCounterValue(0);

//         // Compute Shader'a buffer'ları bağla
//         int filterKernel = filterComputeShader.FindKernel("CSMain");
//         filterComputeShader.SetBuffer(filterKernel, "_ComputeInstanceDataBuffer", originalBuffer);
//         filterComputeShader.SetBuffer(filterKernel, "activeBuffer", activeBuffer);
//         filterComputeShader.SetBuffer(filterKernel, "filteredBuffer", filteredBuffer);
//         filterComputeShader.SetVector("playerPos", new Vector4(playerPos.x, playerPos.y, playerPos.z, 0f));
//         filterComputeShader.SetFloat("radius", eraseRadius);
//         filterComputeShader.SetInt("maxInstances", maxInstances);

//         // Compute Shader'ı çalıştır
//         int threadGroups = Mathf.CeilToInt(maxInstances / 256.0f);
//         filterComputeShader.Dispatch(filterKernel, threadGroups, 1, 1);

//         // Aktif instance sayısını argsBuffer'a kopyala
//         ComputeBuffer.CopyCount(activeBuffer, argsBuffer, sizeof(uint));

//         // Render için materyale artık aktifleri içeren buffer'ı bağla
//         orijinalMaterial.SetBuffer("_InstanceDataBuffer", activeBuffer);

//         // Render komutunu ver
//         Graphics.DrawMeshInstancedIndirect(
//             quadMesh,
//             0,
//             orijinalMaterial,
//             new Bounds(Vector3.zero, Vector3.one * 500f),
//             argsBuffer
//         );

//         // Opsiyonel: CPU verisini güncelle
//         UpdateErasedPositions();
//     }


//     void UpdateErasedPositions()
//     {
//         erasedPos.Clear();

//         // Get the exact number of erased instances from the filtered buffer's counter
//         int erasedCount = filteredBuffer.count;

//         // Create an array to hold the erased data
//         InstanceData[] erasedData = new InstanceData[erasedCount];

//         // Get the data from the GPU buffer
//         filteredBuffer.GetData(erasedData);

//         // Populate the erasedPositions list
//         foreach (var data in erasedData)
//         {
//             erasedPos.Add(data.position);
//         }

//         // Debug.Log($"Silinen nokta sayısı: {erasedPos.Count}");
//     }


//     // Gizmo ile silinen noktaları göster
//     void OnDrawGizmos()
//     {
//         if (erasedPos == null || erasedPos.Count == 0) return;

//         Gizmos.color = Color.red;
//         foreach (Vector3 pos in erasedPos)
//         {
//             Gizmos.DrawSphere(pos, 0.5f);
//         }

//         if (Player != null)
//         {
//             Gizmos.color = Color.yellow;
//             Gizmos.DrawWireSphere(Player.transform.position, eraseRadius);
//         }
//     }

//     void OnDisable()
//     {
//         originalBuffer?.Release();
//         activeBuffer?.Release();
//         filteredBuffer?.Release();
//         argsBuffer?.Release();
//         originalBuffer = activeBuffer = filteredBuffer = argsBuffer = null;
//         bufferInitialized = false;
//     }

//     Mesh CreateQuad()
//     {
//         Mesh mesh = new Mesh();
//         mesh.vertices = new Vector3[]
//         {
//             new Vector3(-0.5f,-0.5f,0),
//             new Vector3(0.5f,-0.5f,0),
//             new Vector3(0.5f,0.5f,0),
//             new Vector3(-0.5f,0.5f,0)
//         };
//         mesh.uv = new Vector2[]
//         {
//             new Vector2(0,0),
//             new Vector2(1,0),
//             new Vector2(1,1),
//             new Vector2(0,1)
//         };
//         mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
//         mesh.RecalculateNormals();
//         return mesh;
//     }
// }

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class Eraser : MonoBehaviour
{
    [Header("References")]
    public GameObject Player;
    public SpriteRenderer sourceSprite;

    [Header("Instance Settings")]
    [Range(1000, 100000)] public int maxInstances = 10000;
    public float eraseRadius = 10;
    public float scaleMultiplier = 1f;
    public Vector3 minPosition = new Vector3(-100f, 0, -100f);
    public Vector3 maxPosition = new Vector3(100f, 0, 100f);

    [Header("Shaders")]
    public ComputeShader filterComputeShader;
    public Shader shader;
    public Material originalMaterial;

    [Header("Performance")]
    [Tooltip("Update erased positions every N frames (0 = every frame)")]
    public int cpuUpdateInterval = 5;

    // CPU tracking of erased positions
    private List<Vector3> erasedPos = new List<Vector3>();
    private InstanceData[] cpuInstanceData;

    // GPU buffers
    private Mesh quadMesh;
    private ComputeBuffer originalBuffer;
    private ComputeBuffer activeBuffer;
    private ComputeBuffer argsBuffer;
    private ComputeBuffer filteredBuffer;

    // Performance tracking
    private int frameCount = 0;
    private bool bufferInitialized = false;
    private int stride;
    private uint[] argsData = new uint[5] { 0, 0, 0, 0, 0 };

    struct InstanceData
    {
        public Vector3 position;
        public int active;
    }

    void Start()
    {
        if (!ValidateComponents()) return;

        _InitializeBuffers();
        InitializeBuffers();
        GenerateInitialPositions();
        UpdateArgsBuffer();
    }
    void _InitializeBuffers()
    {
        stride = sizeof(float) * 3 + sizeof(int); // Senin hesabın: 16
        
        originalBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Structured);
        
        // GERÇEK STRİDE'I KONTROL ET:
        Debug.Log($"Senin hesabın: {stride} bytes");
        Debug.Log($"GPU'nun kullandığı: {originalBuffer.stride} bytes");
        
        if (stride != originalBuffer.stride)
        {
            Debug.LogError($"PADDING PROBLEM! Fark: {originalBuffer.stride - stride} bytes");
        }
    }
    
    bool ValidateComponents()
    {
        if (sourceSprite == null || sourceSprite.sprite == null)
        {
            Debug.LogError("Source sprite is missing!");
            return false;
        }

        if (Player == null)
        {
            Debug.LogError("Player reference is missing!");
            return false;
        }

        if (filterComputeShader == null)
        {
            Debug.LogError("Filter compute shader is missing!");
            return false;
        }

        // Hide source sprite
        sourceSprite.enabled = false;

        // Create quad mesh
        quadMesh = CreateQuad();

        // Setup shader
        if (shader == null)
            shader = Shader.Find("CustomUnlit/SingleSpriteCompute_GPUActive");

        if (shader == null)
        {
            Debug.LogError("Shader not found!");
            return false;
        }

        // Setup material
        if (originalMaterial == null)
        {
            originalMaterial = new Material(shader);
            originalMaterial.mainTexture = sourceSprite.sprite.texture;
            originalMaterial.enableInstancing = true;
        }

        return true;
    }

    void InitializeBuffers()
    {
        stride = sizeof(float) * 3 + sizeof(int); // Vector3 + int

        originalBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Structured);
        activeBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
        filteredBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);

        cpuInstanceData = new InstanceData[maxInstances];
        bufferInitialized = true;
    }

    void GenerateInitialPositions()
    {
        if (originalBuffer == null) return;

        // Generate random positions
        for (int i = 0; i < maxInstances; i++)
        {
            cpuInstanceData[i] = new InstanceData
            {
                position = new Vector3(
                    Random.Range(minPosition.x, maxPosition.x),
                    Random.Range(minPosition.y, maxPosition.y),
                    Random.Range(minPosition.z, maxPosition.z)),
                active = 1
            };
        }

        originalBuffer.SetData(cpuInstanceData);
    }

    void UpdateArgsBuffer()
    {
        if (quadMesh != null)
        {
            argsData[0] = quadMesh.GetIndexCount(0);
            argsData[1] = (uint)maxInstances; // Will be updated dynamically
            argsData[2] = quadMesh.GetIndexStart(0);
            argsData[3] = quadMesh.GetBaseVertex(0);
            argsData[4] = 0;
        }
        argsBuffer.SetData(argsData);
    }

    void Update()
    {
        if (!bufferInitialized || Player == null) return;

        ProcessInstances();
        RenderInstances();

        // Update CPU data periodically for performance
        frameCount++;
        if (cpuUpdateInterval == 0 || frameCount % cpuUpdateInterval == 0)
        {
            UpdateErasedPositions();
        }
    }

    void ProcessInstances()
    {
        Vector3 playerPos = Player.transform.position;

        // Reset append buffers
        activeBuffer.SetCounterValue(0);
        filteredBuffer.SetCounterValue(0);

        // Setup compute shader
        int filterKernel = filterComputeShader.FindKernel("CSMain");
        filterComputeShader.SetBuffer(filterKernel, "_ComputeInstanceDataBuffer", originalBuffer);
        filterComputeShader.SetBuffer(filterKernel, "activeBuffer", activeBuffer);
        filterComputeShader.SetBuffer(filterKernel, "filteredBuffer", filteredBuffer);

        // Set shader parameters
        filterComputeShader.SetVector("playerPos", new Vector4(playerPos.x, playerPos.y, playerPos.z, 0f));
        filterComputeShader.SetFloat("radius", eraseRadius);
        filterComputeShader.SetInt("maxInstances", maxInstances);

        // Dispatch compute shader
        int threadGroups = Mathf.CeilToInt(maxInstances / 256.0f);
        filterComputeShader.Dispatch(filterKernel, threadGroups, 1, 1);

        // Copy active count to args buffer for indirect rendering
        ComputeBuffer.CopyCount(activeBuffer, argsBuffer, sizeof(uint));
    }

    void RenderInstances()
    {
        // Set the active buffer to the material
        originalMaterial.SetBuffer("_InstanceDataBuffer", activeBuffer);
        originalMaterial.SetFloat("_Scale", scaleMultiplier);

        // Render using indirect GPU instancing
        Graphics.DrawMeshInstancedIndirect(
            quadMesh,
            0,
            originalMaterial,
            new Bounds(Vector3.zero, Vector3.one * 500f),
            argsBuffer
        );
    }

    void UpdateErasedPositions()
    {
        erasedPos.Clear();

        // Get count safely
        int erasedCount = 0;

        try
        {
            erasedCount = filteredBuffer.count;
            if (erasedCount > 0)
            {
                InstanceData[] erasedData = new InstanceData[erasedCount];
                filteredBuffer.GetData(erasedData);

                foreach (var data in erasedData)
                {
                    erasedPos.Add(data.position);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to update erased positions: {e.Message}");
        }
    }

    void OnDrawGizmos()
    {
        // Draw erased positions
        if (erasedPos != null && erasedPos.Count > 0)
        {
            Gizmos.color = Color.red;
            foreach (Vector3 pos in erasedPos)
            {
                Gizmos.DrawSphere(pos, 0.5f);
            }
        }

        // Draw player erase radius
        if (Player != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(Player.transform.position, eraseRadius);
        }

        // Draw bounds
        Gizmos.color = Color.blue;
        Vector3 center = (minPosition + maxPosition) * 0.5f;
        Vector3 size = maxPosition - minPosition;
        Gizmos.DrawWireCube(center, size);
    }

    void OnDisable()
    {
        ReleaseBuffers();
    }

    void OnDestroy()
    {
        ReleaseBuffers();
    }

    void ReleaseBuffers()
    {
        originalBuffer?.Release();
        activeBuffer?.Release();
        filteredBuffer?.Release();
        argsBuffer?.Release();

        originalBuffer = null;
        activeBuffer = null;
        filteredBuffer = null;
        argsBuffer = null;

        bufferInitialized = false;
    }

    Mesh CreateQuad()
    {
        Mesh mesh = new Mesh();
        mesh.name = "EraserQuad";

        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(0.5f, -0.5f, 0),
            new Vector3(0.5f, 0.5f, 0),
            new Vector3(-0.5f, 0.5f, 0)
        };

        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };

        mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    // Public methods for external access
    public int GetActiveInstanceCount()
    {
        return bufferInitialized ? activeBuffer.count : 0;
    }

    public int GetErasedInstanceCount()
    {
        return erasedPos.Count;
    }

    public List<Vector3> GetErasedPositions()
    {
        return new List<Vector3>(erasedPos);
    }

    public void ResetInstances()
    {
        if (bufferInitialized)
        {
            GenerateInitialPositions();
            erasedPos.Clear();
        }
    }
}

