using UnityEngine;

public class GPUInstancingManager : MonoBehaviour
{
    public static GPUInstancingManager Instance { get; private set; }

    [Header("Compute Shader")]
    [SerializeField] private ComputeShader computeShader;

    [Header("Configuration")]
    [SerializeField] private SpawnVisualData visualData;
    [SerializeField] private SpawnAreaData areaData;
    [SerializeField] private SpawnCountData countData;

    [Header("Runtime Settings")]
    [SerializeField] private Camera cullingCamera;
    [SerializeField] private bool useFrustumCulling = true;
    [SerializeField] private bool usePlayerRadius = false;
    [SerializeField] private Transform player;
    [SerializeField] private float cullingRadius = 50f;
    [SerializeField] private bool showDebugLogs = false;
    [SerializeField] private bool showSpawnedPositions = false;
    [SerializeField] private int maxGizmoPoints = 100;

    // Buffers
    private ComputeBuffer instanceDataBuffer;
    private ComputeBuffer filteredBuffer;
    private ComputeBuffer nearPlayerBuffer;
    private ComputeBuffer argsBuffer;
    private ComputeBuffer deletionIndicesBuffer;
    private ComputeBuffer deletionCountBuffer;
    private ComputeBuffer frustumPlanesBuffer;

    // Cached data
    private Mesh instanceMesh;
    private Material instanceMaterial;
    private int currentInstanceCount;
    private uint[] args = new uint[5];

    // Statistics
    private int visibleCount = 0;
    private int activeCount = 0;
    private int deletedCount = 0;
    private int totalSpawned = 0;

    // Kernel indices
    private int spawnKernel;
    private int cullKernel;
    private int nearPlayerKernel;
    private int copyAllKernel;

    // Camera frustum planes
    private Plane[] frustumPlanes = new Plane[6];

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        visualData?.Initialize();
        InitializeSystem();
    }

    void InitializeSystem()
    {
        int maxInstances = countData.maxInstances;
        currentInstanceCount = countData.minInstances;

        // Create buffers - InstanceData struct: position(3) + velocity(3) + rotation(3) + active(1) = 10 floats
        int instanceDataStride = sizeof(float) * 10;
        instanceDataBuffer = new ComputeBuffer(maxInstances, instanceDataStride);
        filteredBuffer = new ComputeBuffer(maxInstances, instanceDataStride, ComputeBufferType.Append);
        nearPlayerBuffer = new ComputeBuffer(maxInstances, instanceDataStride, ComputeBufferType.Append);
        deletionIndicesBuffer = new ComputeBuffer(maxInstances, sizeof(int), ComputeBufferType.Append);
        deletionCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        frustumPlanesBuffer = new ComputeBuffer(6, sizeof(float) * 4);

        // Setup mesh and material
        if (visualData.use2D)
        {
            instanceMesh = visualData.CachedSprite != null ? 
                           CreateMeshFromSprite(visualData.CachedSprite) : 
                           CreateQuadMesh();
        }
        else
        {
            instanceMesh = visualData.CachedMesh;
        }
        
        instanceMaterial = new Material(visualData.CachedMaterial);
        instanceMaterial.enableInstancing = true;

        // Initialize args buffer
        args[0] = instanceMesh.GetIndexCount(0);
        args[1] = 0;
        args[2] = 0;
        args[3] = 0;
        args[4] = 0;
        argsBuffer.SetData(args);

        // Find kernel indices
        spawnKernel = GetSpawnKernel();
        cullKernel = computeShader.FindKernel("CSFrustumCull");
        nearPlayerKernel = computeShader.FindKernel("CSNearPlayer");
        copyAllKernel = computeShader.FindKernel("CSCopyAll");

        // Set camera if not assigned
        if (cullingCamera == null)
            cullingCamera = Camera.main;

        // Initial spawn
        SpawnInstances(currentInstanceCount, 0);
        totalSpawned = currentInstanceCount;
        activeCount = currentInstanceCount;

        if (showDebugLogs)
        {
            Debug.Log($"System Initialized: {currentInstanceCount} instances spawned");
            Debug.Log($"Mesh: {instanceMesh != null}, Material: {instanceMaterial != null}");
            Debug.Log($"Shader: {instanceMaterial?.shader?.name}");
        }
    }

    int GetSpawnKernel()
    {
        switch (areaData)
        {
            case BoxSpawnData box:
                return computeShader.FindKernel(box.fillCube ? "BoxGrid" : "BoxLayered");
            case SphereSpawnData sphere:
                return computeShader.FindKernel(sphere.expandFromCenter ? "SphereLayered" : "SphereUniform");
            default:
                return computeShader.FindKernel("BoxRandom");
        }
    }

    void SpawnInstances(int count, int startIndex)
    {
        computeShader.SetBuffer(spawnKernel, "Positions", instanceDataBuffer);
        computeShader.SetInt("PointCount", count);
        computeShader.SetInt("RandomSeed", startIndex + (int)(Time.time * 1000));

        SetSpawnAreaParameters();

        int threadGroups = Mathf.CeilToInt(count / 256f);
        computeShader.Dispatch(spawnKernel, threadGroups, 1, 1);
    }

    void SetSpawnAreaParameters()
    {
        switch (areaData)
        {
            case BoxSpawnData box:
                computeShader.SetVector("Data1", box.minPosition + transform.position);
                computeShader.SetVector("Data2", box.maxPosition + transform.position);
                computeShader.SetVector("Data3", new Vector3(box.layerCount, 0, 0));
                break;

            case SphereSpawnData sphere:
                computeShader.SetVector("Data1", sphere.center + transform.position);
                computeShader.SetVector("Data2", new Vector3(sphere.maxRadius, 0, 0));
                computeShader.SetVector("Data3", new Vector3(sphere.layerCount, sphere.startRadius, 0));
                break;
        }
    }

    void Update()
    {
        if (instanceDataBuffer == null) return;

        // Increment spawn count
        if (currentInstanceCount < countData.maxInstances)
        {
            int newCount = Mathf.Min(currentInstanceCount + countData.incrementPerFrame, countData.maxInstances);
            if (newCount > currentInstanceCount)
            {
                SpawnInstances(newCount - currentInstanceCount, currentInstanceCount);
                int spawned = newCount - currentInstanceCount;
                currentInstanceCount = newCount;
                totalSpawned += spawned;
                activeCount += spawned;
            }
        }

        // Perform culling
        if (useFrustumCulling)
        {
            PerformCulling();
        }
        else
        {
            CopyAllToFiltered();
        }

        // Check for deletions
        ProcessDeletions();

        // Render instances
        RenderInstances();
    }

    void CopyAllToFiltered()
    {
        filteredBuffer.SetCounterValue(0);
        
        computeShader.SetInt("PointCount", currentInstanceCount);
        computeShader.SetBuffer(copyAllKernel, "inputBuffer", instanceDataBuffer);
        computeShader.SetBuffer(copyAllKernel, "filteredBuffer", filteredBuffer);
        
        int threadGroups = Mathf.CeilToInt(currentInstanceCount / 64f);
        computeShader.Dispatch(copyAllKernel, threadGroups, 1, 1);
    }

    void PerformCulling()
    {
        filteredBuffer.SetCounterValue(0);

        GeometryUtility.CalculateFrustumPlanes(cullingCamera, frustumPlanes);
        
        Vector4[] planesData = new Vector4[6];
        for (int i = 0; i < 6; i++)
        {
            planesData[i] = new Vector4(
                frustumPlanes[i].normal.x,
                frustumPlanes[i].normal.y,
                frustumPlanes[i].normal.z,
                frustumPlanes[i].distance
            );
        }
        frustumPlanesBuffer.SetData(planesData);

        if (showDebugLogs && Time.frameCount % 120 == 0)
        {
            Debug.Log($"Camera: {cullingCamera.transform.position}, Rot: {cullingCamera.transform.eulerAngles}");
        }

        computeShader.SetInt("PointCount", currentInstanceCount);
        computeShader.SetBuffer(cullKernel, "inputBuffer", instanceDataBuffer);
        computeShader.SetBuffer(cullKernel, "filteredBuffer", filteredBuffer);
        computeShader.SetBuffer(cullKernel, "frustumPlanes", frustumPlanesBuffer);

        int threadGroups = Mathf.CeilToInt(currentInstanceCount / 64f);
        computeShader.Dispatch(cullKernel, threadGroups, 1, 1);

        if (usePlayerRadius && player != null)
        {
            nearPlayerBuffer.SetCounterValue(0);
            computeShader.SetInt("PointCount", currentInstanceCount);
            computeShader.SetBuffer(nearPlayerKernel, "inputBufferNear", filteredBuffer);
            computeShader.SetBuffer(nearPlayerKernel, "nearPlayerBuffer", nearPlayerBuffer);
            computeShader.SetVector("playerPos", player.position);
            computeShader.SetFloat("radius", cullingRadius);
            computeShader.Dispatch(nearPlayerKernel, threadGroups, 1, 1);
        }
    }

    void ProcessDeletions()
    {
        int[] countArray = new int[1];
        ComputeBuffer.CopyCount(deletionIndicesBuffer, deletionCountBuffer, 0);
        deletionCountBuffer.GetData(countArray);

        int newDeletions = countArray[0];
        if (newDeletions > 0)
        {
            int[] deletedIndices = new int[newDeletions];
            deletionIndicesBuffer.GetData(deletedIndices, 0, 0, newDeletions);

            deletedCount += newDeletions;
            activeCount -= newDeletions;

            Debug.Log($"Silindi: {newDeletions} obje | Toplam: {deletedCount}");

            deletionIndicesBuffer.SetCounterValue(0);
            deletionCountBuffer.SetData(new int[] { 0 });
        }
    }

    void RenderInstances()
    {
        ComputeBuffer renderBuffer = (usePlayerRadius && player != null) ? nearPlayerBuffer : filteredBuffer;
        ComputeBuffer.CopyCount(renderBuffer, argsBuffer, sizeof(uint));

        uint[] argsData = new uint[5];
        argsBuffer.GetData(argsData);
        visibleCount = (int)argsData[1];

        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[Frame {Time.frameCount}] Aktif: {activeCount}, Görünen: {visibleCount}, Silinen: {deletedCount}");
        }

        instanceMaterial.SetBuffer("_InstanceData", renderBuffer);

        Bounds bounds = new Bounds(transform.position, Vector3.one * 1000f);
        Graphics.DrawMeshInstancedIndirect(instanceMesh, 0, instanceMaterial, bounds, argsBuffer);
    }

    Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(0.5f, -0.5f, 0),
            new Vector3(-0.5f, 0.5f, 0),
            new Vector3(0.5f, 0.5f, 0)
        };
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };
        mesh.RecalculateNormals();
        return mesh;
    }

    Mesh CreateMeshFromSprite(Sprite sprite)
    {
        Mesh mesh = new Mesh();
        
        Vector2[] spriteVertices = sprite.vertices;
        ushort[] spriteTriangles = sprite.triangles;
        Vector2[] spriteUVs = sprite.uv;
        
        Vector3[] vertices = new Vector3[spriteVertices.Length];
        for (int i = 0; i < spriteVertices.Length; i++)
        {
            vertices[i] = new Vector3(spriteVertices[i].x, spriteVertices[i].y, 0);
        }
        
        int[] triangles = new int[spriteTriangles.Length];
        for (int i = 0; i < spriteTriangles.Length; i++)
        {
            triangles[i] = spriteTriangles[i];
        }
        
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = spriteUVs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }

    void OnDestroy()
    {
        instanceDataBuffer?.Release();
        filteredBuffer?.Release();
        nearPlayerBuffer?.Release();
        argsBuffer?.Release();
        deletionIndicesBuffer?.Release();
        deletionCountBuffer?.Release();
        frustumPlanesBuffer?.Release();
    }

    void OnDrawGizmos()
    {
        areaData?.DrawGizmos(transform.position);

        if (showSpawnedPositions && instanceDataBuffer != null && Application.isPlaying)
        {
            DrawSpawnedPositions();
        }

        if (cullingCamera != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.green;
            
            Vector3[] nearCorners = new Vector3[4];
            Vector3[] farCorners = new Vector3[4];
            
            cullingCamera.CalculateFrustumCorners(
                new Rect(0, 0, 1, 1), 
                cullingCamera.nearClipPlane, 
                Camera.MonoOrStereoscopicEye.Mono, 
                nearCorners
            );
            
            cullingCamera.CalculateFrustumCorners(
                new Rect(0, 0, 1, 1), 
                cullingCamera.farClipPlane, 
                Camera.MonoOrStereoscopicEye.Mono, 
                farCorners
            );
            
            for (int i = 0; i < 4; i++)
            {
                nearCorners[i] = cullingCamera.transform.TransformPoint(nearCorners[i]);
                farCorners[i] = cullingCamera.transform.TransformPoint(farCorners[i]);
            }
            
            for (int i = 0; i < 4; i++)
                Gizmos.DrawLine(nearCorners[i], nearCorners[(i + 1) % 4]);
            
            for (int i = 0; i < 4; i++)
                Gizmos.DrawLine(farCorners[i], farCorners[(i + 1) % 4]);
            
            for (int i = 0; i < 4; i++)
                Gizmos.DrawLine(nearCorners[i], farCorners[i]);
        }

        if (usePlayerRadius && player != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(player.position, cullingRadius);
        }
    }

    void OnGUI()
    {
        if (!Application.isPlaying) return;
        DrawStatisticsGUI();
    }
    struct InstanceDataCPU
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 rotation;
        public int active;
    }
    void DrawSpawnedPositions()
    {
        int sampleCount = Mathf.Min(maxGizmoPoints, currentInstanceCount);
        

        
        InstanceDataCPU[] samples = new InstanceDataCPU[sampleCount];
        instanceDataBuffer.GetData(samples, 0, 0, sampleCount);
        
        for (int i = 0; i < sampleCount; i++)
        {
            if (samples[i].active == 1)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(samples[i].position, 0.5f);
            }
        }
    }

    void DrawStatisticsGUI()
    {
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.alignment = TextAnchor.UpperLeft;
        boxStyle.normal.background = MakeTex(2, 2, new Color(0, 0, 0, 0.7f));
        boxStyle.padding = new RectOffset(10, 10, 10, 10);
        
        GUIStyle textStyle = new GUIStyle();
        textStyle.normal.textColor = Color.white;
        textStyle.fontSize = 14;
        textStyle.fontStyle = FontStyle.Bold;
        
        float startX = 10;
        float startY = 10;
        float width = 280;
        float height = 140;
        
        GUI.Box(new Rect(startX, startY, width, height), "", boxStyle);
        
        float yOffset = startY + 10;
        float lineHeight = 20;
        
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"GPU INSTANCING İSTATİSTİKLERİ", textStyle);
        
        textStyle.fontSize = 12;
        textStyle.fontStyle = FontStyle.Normal;
        yOffset += lineHeight + 5;
        
        textStyle.normal.textColor = Color.green;
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"Ekranda Görünen: {visibleCount:N0}", textStyle);
        yOffset += lineHeight;
        
        textStyle.normal.textColor = Color.cyan;
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"Aktif Obje: {activeCount:N0}", textStyle);
        yOffset += lineHeight;
        
        textStyle.normal.textColor = Color.red;
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"Silinen Obje: {deletedCount:N0}", textStyle);
        yOffset += lineHeight;
        
        textStyle.normal.textColor = Color.yellow;
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"Toplam Spawn: {totalSpawned:N0}", textStyle);
        yOffset += lineHeight;
        
        float cullingPercentage = activeCount > 0 ? (visibleCount / (float)activeCount * 100f) : 0;
        textStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"Görünürlük: %{cullingPercentage:F1}", textStyle);
    }

    private Texture2D backgroundTexture;
    
    private Texture2D MakeTex(int width, int height, Color col)
    {
        if (backgroundTexture != null) return backgroundTexture;
        
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;
        
        backgroundTexture = new Texture2D(width, height);
        backgroundTexture.SetPixels(pix);
        backgroundTexture.Apply();
        return backgroundTexture;
    }

    public ComputeBuffer GetInstanceDataBuffer() => instanceDataBuffer;
    public ComputeBuffer GetDeletionIndicesBuffer() => deletionIndicesBuffer;
    public ComputeBuffer GetDeletionCountBuffer() => deletionCountBuffer;
    public int CurrentInstanceCount => currentInstanceCount;
}
