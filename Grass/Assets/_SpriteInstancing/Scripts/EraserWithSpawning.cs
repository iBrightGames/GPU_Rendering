
// using UnityEngine;
// using System.Collections.Generic;

// public class EraserWithSpawning : MonoBehaviour
// {
//     [Header("References")]
//     public GameObject Player;
//     public SpriteRenderer sourceSprite;

//     [Header("Instance Settings")]
//     [Range(1000, 100000)] public int maxInstances = 10000;
//     public int startInstanceCount = 1000;
//     public int spawnPerFrame = 10; // Her frame'de eklenen yeni instance sayısı
//     public float eraseRadius = 10;
//     public float scaleMultiplier = 1f;
//     public Vector3 minPosition = new Vector3(-100f, 0, -100f);
//     public Vector3 maxPosition = new Vector3(100f, 0, 100f);

//     [Header("Shaders")]
//     public ComputeShader filterComputeShader;
//     public Shader shader;
//     public Material originalMaterial;

//     [Header("Performance")]
//     public int cpuUpdateInterval = 5;

//     // CPU tracking
//     private List<Vector3> erasedPos = new List<Vector3>();

//     // GPU buffers
//     private Mesh quadMesh;
//     private ComputeBuffer currentActiveBuffer; // Ping-pong A
//     private ComputeBuffer nextActiveBuffer;    // Ping-pong B
//     private ComputeBuffer filteredBuffer;
//     private ComputeBuffer argsBuffer;
//     private ComputeBuffer spawnDataBuffer;     // Yeni pozisyonlar için

//     // Performance tracking
//     private int frameCount = 0;
//     private bool bufferInitialized = false;
//     private int stride;
//     private uint[] argsData = new uint[5] { 0, 0, 0, 0, 0 };

//     struct InstanceData
//     {
//         public Vector3 position;
//         public int active; // Artık kullanılmayacak, AppendBuffer mantığı bunu GPU'da yapıyor
//     }

//     void Start()
//     {
//         if (!ValidateComponents()) return;

//         InitializeBuffers();
//         GenerateInitialPositions();
//         UpdateArgsBuffer(); // Arguman buffer'ını quad mesh verileriyle doldur
//     }

//     bool ValidateComponents()
//     {
//         // ... (ValidateComponents metodu aynı kalır)
//         if (sourceSprite == null || sourceSprite.sprite == null)
//         {
//             Debug.LogError("Source sprite is missing!");
//             return false;
//         }

//         if (Player == null)
//         {
//             Debug.LogError("Player reference is missing!");
//             return false;
//         }

//         if (filterComputeShader == null)
//         {
//             Debug.LogError("Filter compute shader is missing!");
//             return false;
//         }

//         sourceSprite.enabled = false;
//         quadMesh = CreateQuad();

//         if (shader == null)
//             shader = Shader.Find("CustomUnlit/SingleSpriteCompute_GPUActive");

//         if (shader == null)
//         {
//             Debug.LogError("Shader not found!");
//             return false;
//         }

//         if (originalMaterial == null)
//         {
//             originalMaterial = new Material(shader);
//             originalMaterial.mainTexture = sourceSprite.sprite.texture;
//             originalMaterial.enableInstancing = true;
//         }
//         return true;
//     }

//     void InitializeBuffers()
//     {
//         stride = sizeof(float) * 3 + sizeof(int);

//         // Ping-pong Buffer'lar (Append yerine Structured, çünkü spawn ekleyeceğiz)
//         currentActiveBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Structured);
//         nextActiveBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);

//         filteredBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
//         argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
//         spawnDataBuffer = new ComputeBuffer(spawnPerFrame, stride, ComputeBufferType.Structured);

//         bufferInitialized = true;
//     }

//     void GenerateInitialPositions()
//     {
//         InstanceData[] initialData = new InstanceData[maxInstances];
//         for (int i = 0; i < maxInstances; i++)
//         {
//             if (i < startInstanceCount)
//             {
//                 initialData[i] = new InstanceData
//                 {
//                     position = GetRandomPosition(),
//                     active = 1
//                 };
//             }
//             else
//             {
//                 initialData[i] = new InstanceData
//                 {
//                     position = Vector3.zero,
//                     active = 0
//                 };
//             }
//         }
//         currentActiveBuffer.SetData(initialData);

//         // argsBuffer'daki instance sayısını başlangıçta startInstanceCount olarak ayarla
//         ComputeBuffer.CopyCount(currentActiveBuffer, argsBuffer, sizeof(uint));
//         // Alternatif olarak: argsData[1] = (uint)startInstanceCount; argsBuffer.SetData(argsData);
//     }

//     Vector3 GetRandomPosition()
//     {
//         return new Vector3(
//             Random.Range(minPosition.x, maxPosition.x),
//             Random.Range(minPosition.y, maxPosition.y),
//             Random.Range(minPosition.z, maxPosition.z)
//         );
//     }

//     void UpdateArgsBuffer()
//     {
//         if (quadMesh != null)
//         {
//             argsData[0] = quadMesh.GetIndexCount(0);
//             argsData[1] = (uint)startInstanceCount;
//             argsData[2] = quadMesh.GetIndexStart(0);
//             argsData[3] = quadMesh.GetBaseVertex(0);
//             argsData[4] = 0;
//         }
//         argsBuffer.SetData(argsData);
//     }

//     void Update()
//     {
//         if (!bufferInitialized || Player == null) return;

//         // Adım 1: Yeni spawn pozisyonlarını CPU'da hazırla ve GPU'ya gönder
//         PrepareSpawnData();

//         // Adım 2: GPU'da instance'ları işle (Filtreleme ve Spawn)
//         ProcessInstances();

//         // Adım 3: GPU'da render et
//         RenderInstances();

//         frameCount++;
//         if (cpuUpdateInterval == 0 || frameCount % cpuUpdateInterval == 0)
//         {
//             // Adım 4: Silinen pozisyonları CPU'ya geri oku
//             UpdateErasedPositions();
//         }
//     }

//     void PrepareSpawnData()
//     {
//         if (spawnPerFrame == 0) return;

//         InstanceData[] newSpawns = new InstanceData[spawnPerFrame];

//         for (int i = 0; i < spawnPerFrame; i++)
//         {
//             newSpawns[i] = new InstanceData
//             {
//                 position = GetRandomPosition(),
//                 active = 1
//             };
//         }

//         // Spawn datayı GPU buffer'ına yolla
//         spawnDataBuffer.SetData(newSpawns);
//     }

//     void ProcessInstances()
//     {
//         Vector3 playerPos = Player.transform.position;

//         // 1. Reset Append Buffers
//         nextActiveBuffer.SetCounterValue(0);
//         filteredBuffer.SetCounterValue(0);

//         // 2. Dispatch Filter Kernel
//         int filterKernel = filterComputeShader.FindKernel("CSMain");
//         filterComputeShader.SetBuffer(filterKernel, "inputBuffer", currentActiveBuffer);
//         filterComputeShader.SetBuffer(filterKernel, "activeBuffer", nextActiveBuffer);
//         filterComputeShader.SetBuffer(filterKernel, "filteredBuffer", filteredBuffer);
//         filterComputeShader.SetVector("playerPos", new Vector4(playerPos.x, playerPos.y, playerPos.z, 0f));
//         filterComputeShader.SetFloat("radius", eraseRadius);


//         filterComputeShader.SetInt("maxInstances", maxInstances);

//         int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
//         filterComputeShader.Dispatch(filterKernel, threadGroups, 1, 1);

//         // 3. Dispatch Spawn Kernel
//         int spawnKernel = filterComputeShader.FindKernel("CSSpawnNew");
//         filterComputeShader.SetBuffer(spawnKernel, "spawnDataBuffer", spawnDataBuffer);
//         filterComputeShader.SetBuffer(spawnKernel, "activeBuffer", nextActiveBuffer);
//         filterComputeShader.SetInt("spawnCount", spawnPerFrame);

//         int spawnThreadGroups = Mathf.CeilToInt(spawnPerFrame / 64.0f);
//         filterComputeShader.Dispatch(spawnKernel, spawnThreadGroups, 1, 1);

//         // 4. Update Args Buffer
//         ComputeBuffer.CopyCount(nextActiveBuffer, argsBuffer, sizeof(uint));

//         // 5. Swap Buffers (Ping-Pong)
//         SwapBuffers(ref currentActiveBuffer, ref nextActiveBuffer);
//     }

//     void SwapBuffers(ref ComputeBuffer a, ref ComputeBuffer b)
//     {
//         ComputeBuffer temp = a;
//         a = b;
//         b = temp;
//     }

//     void RenderInstances()
//     {
//         originalMaterial.SetBuffer("_InstanceDataBuffer", currentActiveBuffer);
//         originalMaterial.SetFloat("_Scale", scaleMultiplier);

//         Graphics.DrawMeshInstancedIndirect(
//             quadMesh,
//             0,
//             originalMaterial,
//             new Bounds(Vector3.zero, Vector3.one * 500f),
//             argsBuffer
//         );
//     }

//     void UpdateErasedPositions()
//     {
//         erasedPos.Clear();
//         int erasedCount = filteredBuffer.count;

//         if (erasedCount > 0)
//         {
//             InstanceData[] erasedData = new InstanceData[erasedCount];
//             filteredBuffer.GetData(erasedData);

//             // CPU sadece pozisyonları kaydeder, index takibi artık yapılmaz.
//             foreach (var data in erasedData)
//                 erasedPos.Add(data.position);
//         }
//     }

//     void OnDisable() => ReleaseBuffers();
//     void OnDestroy() => ReleaseBuffers();

//     void ReleaseBuffers()
//     {
//         currentActiveBuffer?.Release();
//         nextActiveBuffer?.Release();
//         filteredBuffer?.Release();
//         argsBuffer?.Release();
//         spawnDataBuffer?.Release();

//         currentActiveBuffer = null;
//         nextActiveBuffer = null;
//         filteredBuffer = null;
//         argsBuffer = null;
//         spawnDataBuffer = null;

//         bufferInitialized = false;
//     }

//     Mesh CreateQuad()
//     {
//         // ... (CreateQuad metodu aynı kalır)
//         Mesh mesh = new Mesh();
//         mesh.name = "EraserQuad";
//         mesh.vertices = new Vector3[]
//         {
//             new Vector3(-0.5f,-0.5f,0), new Vector3(0.5f,-0.5f,0),
//             new Vector3(0.5f,0.5f,0), new Vector3(-0.5f,0.5f,0)
//         };
//         mesh.uv = new Vector2[]
//         {
//             new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1)
//         };
//         mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
//         mesh.RecalculateNormals();
//         mesh.RecalculateBounds();
//         return mesh;
//     }

//     public int GetActiveInstanceCount()
//     {
//         if (!bufferInitialized) return 0;

//         // Aktif sayıyı okumak için argsBuffer'ı kullan
//         argsBuffer.GetData(argsData);
//         // argsData[1] DrawMeshInstancedIndirect'in Instance Count'udur
//         return (int)argsData[1];
//     }

//     public int GetErasedInstanceCount() => erasedPos.Count;
//     public List<Vector3> GetErasedPositions() => new List<Vector3>(erasedPos);

//     public void ResetInstances()
//     {
//         if (bufferInitialized)
//         {
//             GenerateInitialPositions();
//             erasedPos.Clear();
//         }
//     }
// }

using UnityEngine;
using System.Collections.Generic;

public class SimpleEraserWithSpawning : MonoBehaviour
{
    [Header("References")]
    public GameObject Player;
    public SpriteRenderer sourceSprite;

    [Header("Instance Settings")]
    public int maxInstances = 10000;
    public int startInstanceCount = 1000;
    public int spawnPerFrame = 10;
    public float eraseRadius = 5f;
    public float scaleMultiplier = 1f;
    public Vector3 minPosition = new Vector3(-50f, 0, -50f);
    public Vector3 maxPosition = new Vector3(50f, 0, 50f);

    [Header("Shaders")]
    public ComputeShader filterComputeShader;
    public Material instancingMaterial;

    // Buffers
    private ComputeBuffer instanceBuffer;
    private ComputeBuffer argsBuffer;
    private Mesh quadMesh;

    private List<Vector3> positions = new List<Vector3>();
    private bool bufferInitialized = false;
    private uint[] argsData = new uint[5] { 0, 0, 0, 0, 0 };

    struct InstanceData
    {
        public Vector3 position;
        public int active;
    }

    void Start()
    {
        if (!ValidateComponents()) return;

        InitializeBuffers();
        GenerateInitialPositions();
        UpdateBuffer();
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

        quadMesh = CreateQuad();

        if (instancingMaterial == null)
        {
            Shader shader = Shader.Find("CustomUnlit/SingleSpriteCompute");
            if (shader != null)
            {
                instancingMaterial = new Material(shader);
                instancingMaterial.mainTexture = sourceSprite.sprite.texture;
                instancingMaterial.enableInstancing = true;
            }
        }

        sourceSprite.enabled = false;
        return true;
    }

    void InitializeBuffers()
    {
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        UpdateArgsBuffer();
        bufferInitialized = true;
    }

    void GenerateInitialPositions()
    {
        positions.Clear();
        for (int i = 0; i < startInstanceCount && i < maxInstances; i++)
        {
            positions.Add(GetRandomPosition());
        }
    }

    Vector3 GetRandomPosition()
    {
        return new Vector3(
            Random.Range(minPosition.x, maxPosition.x),
            Random.Range(minPosition.y, maxPosition.y),
            Random.Range(minPosition.z, maxPosition.z)
        );
    }

    void UpdateArgsBuffer()
    {
        if (quadMesh != null)
        {
            argsData[0] = quadMesh.GetIndexCount(0);
            argsData[1] = (uint)positions.Count;
            argsData[2] = quadMesh.GetIndexStart(0);
            argsData[3] = quadMesh.GetBaseVertex(0);
            argsData[4] = 0;
        }
        argsBuffer.SetData(argsData);
    }

    void Update()
    {
        if (!bufferInitialized || Player == null) return;

        // Yeni instance'lar ekle
        AddNewInstances();

        // Silme işlemi
        EraseInstances();

        // Buffer'ı güncelle ve render et
        UpdateBuffer();
        RenderInstances();
    }

    void AddNewInstances()
    {
        if (positions.Count >= maxInstances) return;

        int toAdd = Mathf.Min(spawnPerFrame, maxInstances - positions.Count);
        for (int i = 0; i < toAdd; i++)
        {
            positions.Add(GetRandomPosition());
        }
    }

    void EraseInstances()
    {
        Vector3 playerPos = Player.transform.position;
        float radiusSq = eraseRadius * eraseRadius;

        // Basit CPU tabanlı silme (debug için)
        for (int i = positions.Count - 1; i >= 0; i--)
        {
            float distSq = (positions[i] - playerPos).sqrMagnitude;
            if (distSq < radiusSq)
            {
                positions.RemoveAt(i);
            }
        }

        // Eğer compute shader kullanmak isterseniz:
        // EraseWithComputeShader(playerPos);
    }

    void EraseWithComputeShader(Vector3 playerPos)
    {
        if (filterComputeShader == null) return;

        // Mevcut pozisyonları GPU'ya gönder
        InstanceData[] instanceData = new InstanceData[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            instanceData[i] = new InstanceData
            {
                position = positions[i],
                active = 1
            };
        }

        ComputeBuffer tempBuffer = new ComputeBuffer(positions.Count, sizeof(float) * 3 + sizeof(int));
        tempBuffer.SetData(instanceData);

        // Filtreleme kernel'i
        int kernel = filterComputeShader.FindKernel("CSMain");
        filterComputeShader.SetBuffer(kernel, "instanceBuffer", tempBuffer);
        filterComputeShader.SetVector("playerPos", new Vector4(playerPos.x, playerPos.y, playerPos.z, 0f));
        filterComputeShader.SetFloat("radius", eraseRadius);
        filterComputeShader.SetInt("instanceCount", positions.Count);

        ComputeBuffer resultBuffer = new ComputeBuffer(positions.Count, sizeof(float) * 3 + sizeof(int));
        filterComputeShader.SetBuffer(kernel, "resultBuffer", resultBuffer);

        int threadGroups = Mathf.CeilToInt(positions.Count / 64.0f);
        filterComputeShader.Dispatch(kernel, threadGroups, 1, 1);

        // Sonuçları oku
        InstanceData[] results = new InstanceData[positions.Count];
        resultBuffer.GetData(results);

        // Aktif olanları positions listesine geri yaz
        List<Vector3> newPositions = new List<Vector3>();
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].active == 1)
            {
                newPositions.Add(results[i].position);
            }
        }

        positions = newPositions;

        tempBuffer.Release();
        resultBuffer.Release();
    }

    void UpdateBuffer()
    {
        if (instanceBuffer != null)
            instanceBuffer.Release();

        if (positions.Count == 0) return;

        instanceBuffer = new ComputeBuffer(positions.Count, sizeof(float) * 3);
        instanceBuffer.SetData(positions);

        UpdateArgsBuffer();
    }

    void RenderInstances()
    {
        if (positions.Count == 0 || instanceBuffer == null) return;

        instancingMaterial.SetBuffer("_InstanceDataBuffer", instanceBuffer);
        instancingMaterial.SetFloat("_Scale", scaleMultiplier);

        Graphics.DrawMeshInstancedProcedural(
            quadMesh,
            0,
            instancingMaterial,
            new Bounds(Vector3.zero, Vector3.one * 100f),
            positions.Count
        );
    }

    void OnDisable()
    {
        if (instanceBuffer != null)
            instanceBuffer.Release();
        if (argsBuffer != null)
            argsBuffer.Release();
    }

    Mesh CreateQuad()
    {
        Mesh mesh = new Mesh();
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
        return mesh;
    }

    void OnDrawGizmos()
    {
        if (Player != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(Player.transform.position, eraseRadius);
        }

        Gizmos.color = Color.green;
        foreach (var pos in positions)
        {
            Gizmos.DrawCube(pos, Vector3.one * 0.3f);
        }
    }

    public int GetActiveInstanceCount() => positions.Count;
}
