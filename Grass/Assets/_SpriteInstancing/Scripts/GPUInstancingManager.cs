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

    // Camera frustum planes
    private Plane[] frustumPlanes = new Plane[6];
    private ComputeBuffer frustumPlanesBuffer;

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
        frustumPlanesBuffer = new ComputeBuffer(6, sizeof(float) * 4); // 6 planes, each has 4 floats (normal.xyz + distance)

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
        args[1] = 0; // Will be updated per frame
        args[2] = 0;
        args[3] = 0;
        args[4] = 0;
        argsBuffer.SetData(args);

        // Find kernel indices
        spawnKernel = GetSpawnKernel();
        cullKernel = computeShader.FindKernel("CSFrustumCull");
        nearPlayerKernel = computeShader.FindKernel("CSNearPlayer");

        // Set camera if not assigned
        if (cullingCamera == null)
            cullingCamera = Camera.main;

        // Initial spawn
        SpawnInstances(currentInstanceCount, 0);
        totalSpawned = currentInstanceCount;
        activeCount = currentInstanceCount;
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
            // No culling - copy all active instances to filtered buffer
            CopyAllToFiltered();
        }

        // Check for deletions
        ProcessDeletions();

        // Render instances
        RenderInstances();
    }

    void CopyAllToFiltered()
    {
        // Simple pass-through without culling for testing
        filteredBuffer.SetCounterValue(0);
        
        int copyKernel = computeShader.FindKernel("CSCopyAll");
        computeShader.SetInt("PointCount", currentInstanceCount);
        computeShader.SetBuffer(copyKernel, "inputBuffer", instanceDataBuffer);
        computeShader.SetBuffer(copyKernel, "filteredBuffer", filteredBuffer);
        
        int threadGroups = Mathf.CeilToInt(currentInstanceCount / 64f);
        computeShader.Dispatch(copyKernel, threadGroups, 1, 1);
    }

    void PerformCulling()
    {
        // Reset filtered buffer
        filteredBuffer.SetCounterValue(0);

        // Extract frustum planes from camera
        GeometryUtility.CalculateFrustumPlanes(cullingCamera, frustumPlanes);
        
        // Convert planes to float4 array for GPU (normal.xyz, distance)
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
            Debug.Log($"Kamera Pozisyon: {cullingCamera.transform.position}, Rotasyon: {cullingCamera.transform.eulerAngles}");
            Debug.Log($"Plane 0 (Near): normal={frustumPlanes[0].normal}, dist={frustumPlanes[0].distance}");
        }

        // Setup frustum culling compute
        computeShader.SetInt("PointCount", currentInstanceCount);
        computeShader.SetBuffer(cullKernel, "inputBuffer", instanceDataBuffer);
        computeShader.SetBuffer(cullKernel, "filteredBuffer", filteredBuffer);
        computeShader.SetBuffer(cullKernel, "frustumPlanes", frustumPlanesBuffer);

        int threadGroups = Mathf.CeilToInt(currentInstanceCount / 64f);
        computeShader.Dispatch(cullKernel, threadGroups, 1, 1);

        // Optional: Near player filtering (additional pass)
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

            Debug.Log($"Silindi: {newDeletions} obje | Toplam Silinen: {deletedCount}");

            // Reset deletion buffers
            deletionIndicesBuffer.SetCounterValue(0);
            deletionCountBuffer.SetData(new int[] { 0 });
        }
    }

    void RenderInstances()
    {
        // Update args buffer with filtered count
        ComputeBuffer renderBuffer = (usePlayerRadius && player != null) ? nearPlayerBuffer : filteredBuffer;
        ComputeBuffer.CopyCount(renderBuffer, argsBuffer, sizeof(uint));

        // Read visible count for statistics
        uint[] argsData = new uint[5];
        argsBuffer.GetData(argsData);
        visibleCount = (int)argsData[1];

        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[Frame {Time.frameCount}] Aktif: {activeCount}, Görünen: {visibleCount}, Silinen: {deletedCount}");
        }

        // Set buffer to material
        instanceMaterial.SetBuffer("_InstanceData", renderBuffer);

        // Draw
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
        
        // Get sprite data
        Vector2[] spriteVertices = sprite.vertices;
        ushort[] spriteTriangles = sprite.triangles;
        Vector2[] spriteUVs = sprite.uv;
        
        // Convert to mesh format
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
        #if UNITY_EDITOR
    void OnDrawGizmos()
    {
        areaData?.DrawGizmos(transform.position);

        // Draw spawned positions
        if (showSpawnedPositions && instanceDataBuffer != null && Application.isPlaying)
        {
            DrawSpawnedPositions();
        }

        // Draw camera frustum
        if (cullingCamera != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.green;
            
            // Draw frustum wireframe
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
            
            // Draw near plane
            for (int i = 0; i < 4; i++)
                Gizmos.DrawLine(nearCorners[i], nearCorners[(i + 1) % 4]);
            
            // Draw far plane
            for (int i = 0; i < 4; i++)
                Gizmos.DrawLine(farCorners[i], farCorners[(i + 1) % 4]);
            
            // Draw connecting lines
            for (int i = 0; i < 4; i++)
                Gizmos.DrawLine(nearCorners[i], farCorners[i]);
        }

        // Draw player radius if enabled
        if (usePlayerRadius && player != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(player.position, cullingRadius);
        }

        // Draw statistics text
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
        // Read a sample of positions from GPU (expensive, only for debugging)
        int sampleCount = Mathf.Min(maxGizmoPoints, currentInstanceCount);
    
        // Simple struct matching GPU data

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

        // Only draw in Game View, not Scene View
        if (!Application.isPlaying) return;
        
        // Check if we're in Game View
        if (UnityEditor.SceneView.lastActiveSceneView != null && 
            UnityEditor.SceneView.lastActiveSceneView.hasFocus)
            return;

        UnityEditor.Handles.BeginGUI();
        
        // Background box
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.alignment = TextAnchor.UpperLeft;
        boxStyle.normal.background = MakeTex(2, 2, new Color(0, 0, 0, 0.7f));
        boxStyle.padding = new RectOffset(10, 10, 10, 10);
        
        // Text style
        GUIStyle textStyle = new GUIStyle();
        textStyle.normal.textColor = Color.white;
        textStyle.fontSize = 14;
        textStyle.fontStyle = FontStyle.Bold;
        
        // Calculate positions
        float startX = 10;
        float startY = 10;
        float width = 280;
        float height = 140;
        
        // Draw box
        GUI.Box(new Rect(startX, startY, width, height), "", boxStyle);
        
        // Draw text
        float yOffset = startY + 10;
        float lineHeight = 20;
        
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"GPU INSTANCING İSTATİSTİKLERİ", textStyle);
        
        textStyle.fontSize = 12;
        textStyle.fontStyle = FontStyle.Normal;
        
        yOffset += lineHeight + 5;
        
        // Visible count - green
        textStyle.normal.textColor = Color.green;
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"Ekranda Görünen: {visibleCount:N0}", textStyle);
        yOffset += lineHeight;
        
        // Active count - cyan
        textStyle.normal.textColor = Color.cyan;
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"Aktif Obje: {activeCount:N0}", textStyle);
        yOffset += lineHeight;
        
        // Deleted count - red
        textStyle.normal.textColor = Color.red;
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"Silinen Obje: {deletedCount:N0}", textStyle);
        yOffset += lineHeight;
        
        // Total spawned - yellow
        textStyle.normal.textColor = Color.yellow;
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"Toplam Spawn: {totalSpawned:N0}", textStyle);
        yOffset += lineHeight;
        
        // Culling percentage - white
        float cullingPercentage = activeCount > 0 ? (visibleCount / (float)activeCount * 100f) : 0;
        textStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight), 
            $"Görünürlük: %{cullingPercentage:F1}", textStyle);
        
        UnityEditor.Handles.EndGUI();
        #endif
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




    // Public accessors for external systems
    public ComputeBuffer GetInstanceDataBuffer() => instanceDataBuffer;
    public ComputeBuffer GetDeletionIndicesBuffer() => deletionIndicesBuffer;
    public ComputeBuffer GetDeletionCountBuffer() => deletionCountBuffer;
public int CurrentInstanceCount => currentInstanceCount;
}

// using UnityEngine;

// public class GPUInstancingManager : MonoBehaviour
// {
//     public static GPUInstancingManager Instance { get; private set; }

//     [Header("Compute Shader")]
//     [SerializeField] private ComputeShader computeShader;

//     [Header("Configuration")]
//     [SerializeField] private SpawnVisualData visualData;
//     [SerializeField] private SpawnAreaData areaData;
//     [SerializeField] private SpawnCountData countData;

//     [Header("Runtime Settings")]
//     [SerializeField] private Camera cullingCamera;
//     [SerializeField] private bool useFrustumCulling = true;
//     [SerializeField] private bool usePlayerRadius = false;
//     [SerializeField] private Transform player;
//     [SerializeField] private float cullingRadius = 50f;
//     [SerializeField] private bool showDebugLogs = false;
//     [SerializeField] private bool showSpawnedPositions = false;
//     [SerializeField] private int maxGizmoPoints = 100;

//     // Buffers
//     private ComputeBuffer instanceDataBuffer;
//     private ComputeBuffer filteredBuffer;
//     private ComputeBuffer nearPlayerBuffer;
//     private ComputeBuffer argsBuffer;
//     private ComputeBuffer deletionIndicesBuffer;
//     private ComputeBuffer deletionCountBuffer;

//     // Cached data
//     private Mesh instanceMesh;
//     private Material instanceMaterial;
//     private int currentInstanceCount;
//     private uint[] args = new uint[5];

//     // Statistics
//     private int visibleCount = 0;
//     private int activeCount = 0;
//     private int deletedCount = 0;
//     private int totalSpawned = 0;

//     // Kernel indices
//     private int spawnKernel;
//     private int cullKernel;
//     private int nearPlayerKernel;

//     // Camera frustum planes
//     private Plane[] frustumPlanes = new Plane[6];
//     private ComputeBuffer frustumPlanesBuffer;

//     void Awake()
//     {
//         if (Instance != null && Instance != this)
//         {
//             Destroy(gameObject);
//             return;
//         }
//         Instance = this;
        
//         visualData?.Initialize();
//         InitializeSystem();
//     }

//     void InitializeSystem()
//     {
//         int maxInstances = countData.maxInstances;
//         currentInstanceCount = countData.minInstances;

//         // Create buffers - InstanceData struct: position(3) + velocity(3) + rotation(3) + active(1) = 10 floats
//         int instanceDataStride = sizeof(float) * 10;
//         instanceDataBuffer = new ComputeBuffer(maxInstances, instanceDataStride);
//         filteredBuffer = new ComputeBuffer(maxInstances, instanceDataStride, ComputeBufferType.Append);
//         nearPlayerBuffer = new ComputeBuffer(maxInstances, instanceDataStride, ComputeBufferType.Append);
//         deletionIndicesBuffer = new ComputeBuffer(maxInstances, sizeof(int), ComputeBufferType.Append);
//         deletionCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
//         argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
//         frustumPlanesBuffer = new ComputeBuffer(6, sizeof(float) * 4); // 6 planes, each has 4 floats (normal.xyz + distance)

//         // Setup mesh and material
//         if (visualData.use2D)
//         {
//             instanceMesh = visualData.CachedSprite != null ? 
//                            CreateMeshFromSprite(visualData.CachedSprite) : 
//                            CreateQuadMesh();
//         }
//         else
//         {
//             instanceMesh = visualData.CachedMesh;
//         }
        
//         instanceMaterial = new Material(visualData.CachedMaterial);
//         instanceMaterial.enableInstancing = true;

//         // Initialize args buffer
//         args[0] = instanceMesh.GetIndexCount(0);
//         args[1] = 0; // Will be updated per frame
//         args[2] = 0;
//         args[3] = 0;
//         args[4] = 0;
//         argsBuffer.SetData(args);

//         // Find kernel indices
//         spawnKernel = GetSpawnKernel();
//         cullKernel = computeShader.FindKernel("CSFrustumCull");
//         nearPlayerKernel = computeShader.FindKernel("CSNearPlayer");

//         // Set camera if not assigned
//         if (cullingCamera == null)
//             cullingCamera = Camera.main;

//         // Initial spawn
//         SpawnInstances(currentInstanceCount, 0);
//         totalSpawned = currentInstanceCount;
//         activeCount = currentInstanceCount;
//     }

//     int GetSpawnKernel()
//     {
//         switch (areaData)
//         {
//             case BoxSpawnData box:
//                 return computeShader.FindKernel(box.fillCube ? "BoxGrid" : "BoxLayered");
//             case SphereSpawnData sphere:
//                 return computeShader.FindKernel(sphere.expandFromCenter ? "SphereLayered" : "SphereUniform");
//             default:
//                 return computeShader.FindKernel("BoxRandom");
//         }
//     }

//     void SpawnInstances(int count, int startIndex)
//     {
//         computeShader.SetBuffer(spawnKernel, "Positions", instanceDataBuffer);
//         computeShader.SetInt("PointCount", count);
//         computeShader.SetInt("RandomSeed", startIndex + (int)(Time.time * 1000));

//         SetSpawnAreaParameters();

//         int threadGroups = Mathf.CeilToInt(count / 256f);
//         computeShader.Dispatch(spawnKernel, threadGroups, 1, 1);
//     }

//     void SetSpawnAreaParameters()
//     {
//         switch (areaData)
//         {
//             case BoxSpawnData box:
//                 computeShader.SetVector("Data1", box.minPosition + transform.position);
//                 computeShader.SetVector("Data2", box.maxPosition + transform.position);
//                 computeShader.SetVector("Data3", new Vector3(box.layerCount, 0, 0));
//                 break;

//             case SphereSpawnData sphere:
//                 computeShader.SetVector("Data1", sphere.center + transform.position);
//                 computeShader.SetVector("Data2", new Vector3(sphere.maxRadius, 0, 0));
//                 computeShader.SetVector("Data3", new Vector3(sphere.layerCount, sphere.startRadius, 0));
//                 break;
//         }
//     }

//     void Update()
//     {
//         if (instanceDataBuffer == null) return;

//         // Increment spawn count
//         if (currentInstanceCount < countData.maxInstances)
//         {
//             int newCount = Mathf.Min(currentInstanceCount + countData.incrementPerFrame, countData.maxInstances);
//             if (newCount > currentInstanceCount)
//             {
//                 SpawnInstances(newCount - currentInstanceCount, currentInstanceCount);
//                 int spawned = newCount - currentInstanceCount;
//                 currentInstanceCount = newCount;
//                 totalSpawned += spawned;
//                 activeCount += spawned;
//             }
//         }

//         // Perform culling
//         if (useFrustumCulling)
//         {
//             PerformCulling();
//         }
//         else
//         {
//             // No culling - copy all active instances to filtered buffer
//             CopyAllToFiltered();
//         }

//         // Check for deletions
//         ProcessDeletions();

//         // Render instances
//         RenderInstances();
//     }

//     void CopyAllToFiltered()
//     {
//         // Simple pass-through without culling for testing
//         filteredBuffer.SetCounterValue(0);
        
//         int copyKernel = computeShader.FindKernel("CSCopyAll");
//         computeShader.SetInt("PointCount", currentInstanceCount);
//         computeShader.SetBuffer(copyKernel, "inputBuffer", instanceDataBuffer);
//         computeShader.SetBuffer(copyKernel, "filteredBuffer", filteredBuffer);
        
//         int threadGroups = Mathf.CeilToInt(currentInstanceCount / 64f);
//         computeShader.Dispatch(copyKernel, threadGroups, 1, 1);
//     }

//     void PerformCulling()
//     {
//         // Reset filtered buffer
//         filteredBuffer.SetCounterValue(0);

//         // Extract frustum planes from camera
//         GeometryUtility.CalculateFrustumPlanes(cullingCamera, frustumPlanes);
        
//         // Convert planes to float4 array for GPU (normal.xyz, distance)
//         Vector4[] planesData = new Vector4[6];
//         for (int i = 0; i < 6; i++)
//         {
//             planesData[i] = new Vector4(
//                 frustumPlanes[i].normal.x,
//                 frustumPlanes[i].normal.y,
//                 frustumPlanes[i].normal.z,
//                 frustumPlanes[i].distance
//             );
//         }
//         frustumPlanesBuffer.SetData(planesData);

//         if (showDebugLogs && Time.frameCount % 120 == 0)
//         {
//             Debug.Log($"Kamera Pozisyon: {cullingCamera.transform.position}, Rotasyon: {cullingCamera.transform.eulerAngles}");
//             Debug.Log($"Plane 0 (Near): normal={frustumPlanes[0].normal}, dist={frustumPlanes[0].distance}");
//         }

//         // Setup frustum culling compute
//         computeShader.SetInt("PointCount", currentInstanceCount);
//         computeShader.SetBuffer(cullKernel, "inputBuffer", instanceDataBuffer);
//         computeShader.SetBuffer(cullKernel, "filteredBuffer", filteredBuffer);
//         computeShader.SetBuffer(cullKernel, "frustumPlanes", frustumPlanesBuffer);

//         int threadGroups = Mathf.CeilToInt(currentInstanceCount / 64f);
//         computeShader.Dispatch(cullKernel, threadGroups, 1, 1);

//         // Optional: Near player filtering (additional pass)
//         if (usePlayerRadius && player != null)
//         {
//             nearPlayerBuffer.SetCounterValue(0);
//             computeShader.SetInt("PointCount", currentInstanceCount);
//             computeShader.SetBuffer(nearPlayerKernel, "inputBufferNear", filteredBuffer);
//             computeShader.SetBuffer(nearPlayerKernel, "nearPlayerBuffer", nearPlayerBuffer);
//             computeShader.SetVector("playerPos", player.position);
//             computeShader.SetFloat("radius", cullingRadius);
//             computeShader.Dispatch(nearPlayerKernel, threadGroups, 1, 1);
//         }
//     }

//     void ProcessDeletions()
//     {
//         int[] countArray = new int[1];
//         ComputeBuffer.CopyCount(deletionIndicesBuffer, deletionCountBuffer, 0);
//         deletionCountBuffer.GetData(countArray);

//         int newDeletions = countArray[0];
//         if (newDeletions > 0)
//         {
//             int[] deletedIndices = new int[newDeletions];
//             deletionIndicesBuffer.GetData(deletedIndices, 0, 0, newDeletions);

//             deletedCount += newDeletions;
//             activeCount -= newDeletions;

//             Debug.Log($"Silindi: {newDeletions} obje | Toplam Silinen: {deletedCount}");

//             // Reset deletion buffers
//             deletionIndicesBuffer.SetCounterValue(0);
//             deletionCountBuffer.SetData(new int[] { 0 });
//         }
//     }

//     void RenderInstances()
//     {
//         // Update args buffer with filtered count
//         ComputeBuffer renderBuffer = (usePlayerRadius && player != null) ? nearPlayerBuffer : filteredBuffer;
//         ComputeBuffer.CopyCount(renderBuffer, argsBuffer, sizeof(uint));

//         // Read visible count for statistics
//         uint[] argsData = new uint[5];
//         argsBuffer.GetData(argsData);
//         visibleCount = (int)argsData[1];

//         if (showDebugLogs && Time.frameCount % 60 == 0)
//         {
//             Debug.Log($"[Frame {Time.frameCount}] Aktif: {activeCount}, Görünen: {visibleCount}, Silinen: {deletedCount}");
//         }

//         // Set buffer to material
//         instanceMaterial.SetBuffer("_InstanceData", renderBuffer);

//         // Draw
//         Bounds bounds = new Bounds(transform.position, Vector3.one * 1000f);
//         Graphics.DrawMeshInstancedIndirect(instanceMesh, 0, instanceMaterial, bounds, argsBuffer);
//     }

//     Mesh CreateQuadMesh()
//     {
//         Mesh mesh = new Mesh();
//         mesh.vertices = new Vector3[]
//         {
//             new Vector3(-0.5f, -0.5f, 0),
//             new Vector3(0.5f, -0.5f, 0),
//             new Vector3(-0.5f, 0.5f, 0),
//             new Vector3(0.5f, 0.5f, 0)
//         };
//         mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
//         mesh.uv = new Vector2[]
//         {
//             new Vector2(0, 0),
//             new Vector2(1, 0),
//             new Vector2(0, 1),
//             new Vector2(1, 1)
//         };
//         mesh.RecalculateNormals();
//         return mesh;
//     }

//     Mesh CreateMeshFromSprite(Sprite sprite)
//     {
//         Mesh mesh = new Mesh();
        
//         // Get sprite data
//         Vector2[] spriteVertices = sprite.vertices;
//         ushort[] spriteTriangles = sprite.triangles;
//         Vector2[] spriteUVs = sprite.uv;
        
//         // Convert to mesh format
//         Vector3[] vertices = new Vector3[spriteVertices.Length];
//         for (int i = 0; i < spriteVertices.Length; i++)
//         {
//             vertices[i] = new Vector3(spriteVertices[i].x, spriteVertices[i].y, 0);
//         }
        
//         int[] triangles = new int[spriteTriangles.Length];
//         for (int i = 0; i < spriteTriangles.Length; i++)
//         {
//             triangles[i] = spriteTriangles[i];
//         }
        
//         mesh.vertices = vertices;
//         mesh.triangles = triangles;
//         mesh.uv = spriteUVs;
//         mesh.RecalculateNormals();
//         mesh.RecalculateBounds();
        
//         return mesh;
//     }

//     void OnDestroy()
//     {
//         instanceDataBuffer?.Release();
//         filteredBuffer?.Release();
//         nearPlayerBuffer?.Release();
//         argsBuffer?.Release();
//         deletionIndicesBuffer?.Release();
//         deletionCountBuffer?.Release();
//         frustumPlanesBuffer?.Release();
//     }

//     void OnDrawGizmos()
//     {
//         areaData?.DrawGizmos(transform.position);

//         // Draw spawned positions
//         if (showSpawnedPositions && instanceDataBuffer != null && Application.isPlaying)
//         {
//             DrawSpawnedPositions();
//         }

//         // Draw camera frustum
//         if (cullingCamera != null)
//         {
//             Gizmos.matrix = Matrix4x4.identity;
//             Gizmos.color = Color.green;
            
//             // Draw frustum wireframe
//             Vector3[] nearCorners = new Vector3[4];
//             Vector3[] farCorners = new Vector3[4];
            
//             cullingCamera.CalculateFrustumCorners(
//                 new Rect(0, 0, 1, 1), 
//                 cullingCamera.nearClipPlane, 
//                 Camera.MonoOrStereoscopicEye.Mono, 
//                 nearCorners
//             );
            
//             cullingCamera.CalculateFrustumCorners(
//                 new Rect(0, 0, 1, 1), 
//                 cullingCamera.farClipPlane, 
//                 Camera.MonoOrStereoscopicEye.Mono, 
//                 farCorners
//             );
            
//             for (int i = 0; i < 4; i++)
//             {
//                 nearCorners[i] = cullingCamera.transform.TransformPoint(nearCorners[i]);
//                 farCorners[i] = cullingCamera.transform.TransformPoint(farCorners[i]);
//             }
            
//             // Draw near plane
//             for (int i = 0; i < 4; i++)
//                 Gizmos.DrawLine(nearCorners[i], nearCorners[(i + 1) % 4]);
            
//             // Draw far plane
//             for (int i = 0; i < 4; i++)
//                 Gizmos.DrawLine(farCorners[i], farCorners[(i + 1) % 4]);
            
//             // Draw connecting lines
//             for (int i = 0; i < 4; i++)
//                 Gizmos.DrawLine(nearCorners[i], farCorners[i]);
//         }

//         // Draw player radius if enabled
//         if (usePlayerRadius && player != null)
//         {
//             Gizmos.color = Color.yellow;
//             Gizmos.DrawWireSphere(player.position, cullingRadius);
//         }

//         // Draw statistics text
//         DrawStatisticsGUI();
//     }



 
    

//     // Public accessors for external systems
//     public ComputeBuffer GetInstanceDataBuffer() => instanceDataBuffer;
//     public ComputeBuffer GetDeletionIndicesBuffer() => deletionIndicesBuffer;
//     public ComputeBuffer GetDeletionCountBuffer() => deletionCountBuffer;
//     public int CurrentInstanceCount => currentInstanceCount;
// }

// using UnityEngine;

// public class GPUInstancingManager : MonoBehaviour
// {
//     public static GPUInstancingManager Instance { get; private set; }

//     [Header("Compute Shader")]
//     [SerializeField] private ComputeShader computeShader;

//     [Header("Configuration")]
//     [SerializeField] private SpawnVisualData visualData;
//     [SerializeField] private SpawnAreaData areaData;
//     [SerializeField] private SpawnCountData countData;

//     [Header("Runtime Settings")]
//     [SerializeField] private Camera cullingCamera;
//     [SerializeField] private bool usePlayerRadius = false;
//     [SerializeField] private Transform player;
//     [SerializeField] private float cullingRadius = 50f;

//     // Buffers
//     private ComputeBuffer instanceDataBuffer;
//     private ComputeBuffer filteredBuffer;
//     private ComputeBuffer nearPlayerBuffer;
//     private ComputeBuffer argsBuffer;
//     private ComputeBuffer deletionIndicesBuffer;
//     private ComputeBuffer deletionCountBuffer;

//     // Cached data
//     private Mesh instanceMesh;
//     private Material instanceMaterial;
//     private int currentInstanceCount;
//     private uint[] args = new uint[5];

//     // Statistics
//     private int visibleCount = 0;
//     private int activeCount = 0;
//     private int deletedCount = 0;
//     private int totalSpawned = 0;

//     // Kernel indices
//     private int spawnKernel;
//     private int cullKernel;
//     private int nearPlayerKernel;

//     // Camera frustum planes
//     private Plane[] frustumPlanes = new Plane[6];
//     private ComputeBuffer frustumPlanesBuffer;

//     void Awake()
//     {
//         if (Instance != null && Instance != this)
//         {
//             Destroy(gameObject);
//             return;
//         }
//         Instance = this;

//         visualData?.Initialize();
//         InitializeSystem();
//     }

//     void InitializeSystem()
//     {
//         int maxInstances = countData.maxInstances;
//         currentInstanceCount = countData.minInstances;

//         // Create buffers - InstanceData struct: position(3) + velocity(3) + rotation(3) + active(1) = 10 floats
//         int instanceDataStride = sizeof(float) * 10;
//         instanceDataBuffer = new ComputeBuffer(maxInstances, instanceDataStride);
//         filteredBuffer = new ComputeBuffer(maxInstances, instanceDataStride, ComputeBufferType.Append);
//         nearPlayerBuffer = new ComputeBuffer(maxInstances, instanceDataStride, ComputeBufferType.Append);
//         deletionIndicesBuffer = new ComputeBuffer(maxInstances, sizeof(int), ComputeBufferType.Append);
//         deletionCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
//         argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
//         frustumPlanesBuffer = new ComputeBuffer(6, sizeof(float) * 4); // 6 planes, each has 4 floats (normal.xyz + distance)

//         // Setup mesh and material
//         if (visualData.use2D)
//         {
//             instanceMesh = visualData.CachedSprite != null ?
//                            CreateMeshFromSprite(visualData.CachedSprite) :
//                            CreateQuadMesh();
//         }
//         else
//         {
//             instanceMesh = visualData.CachedMesh;
//         }

//         instanceMaterial = new Material(visualData.CachedMaterial);
//         instanceMaterial.enableInstancing = true;

//         // Initialize args buffer
//         args[0] = instanceMesh.GetIndexCount(0);
//         args[1] = 0; // Will be updated per frame
//         args[2] = 0;
//         args[3] = 0;
//         args[4] = 0;
//         argsBuffer.SetData(args);

//         // Find kernel indices
//         spawnKernel = GetSpawnKernel();
//         cullKernel = computeShader.FindKernel("CSFrustumCull");
//         nearPlayerKernel = computeShader.FindKernel("CSNearPlayer");

//         // Set camera if not assigned
//         if (cullingCamera == null)
//             cullingCamera = Camera.main;

//         // Initial spawn
//         SpawnInstances(currentInstanceCount, 0);
//         totalSpawned = currentInstanceCount;
//         activeCount = currentInstanceCount;
//     }

//     int GetSpawnKernel()
//     {
//         switch (areaData)
//         {
//             case BoxSpawnData box:
//                 return computeShader.FindKernel(box.fillCube ? "BoxGrid" : "BoxLayered");
//             case SphereSpawnData sphere:
//                 return computeShader.FindKernel(sphere.expandFromCenter ? "SphereLayered" : "SphereUniform");
//             default:
//                 return computeShader.FindKernel("BoxRandom");
//         }
//     }

//     void SpawnInstances(int count, int startIndex)
//     {
//         computeShader.SetBuffer(spawnKernel, "Positions", instanceDataBuffer);
//         computeShader.SetInt("PointCount", count);
//         computeShader.SetInt("RandomSeed", startIndex + (int)(Time.time * 1000));

//         SetSpawnAreaParameters();

//         int threadGroups = Mathf.CeilToInt(count / 256f);
//         computeShader.Dispatch(spawnKernel, threadGroups, 1, 1);
//     }

//     void SetSpawnAreaParameters()
//     {
//         switch (areaData)
//         {
//             case BoxSpawnData box:
//                 computeShader.SetVector("Data1", box.minPosition + transform.position);
//                 computeShader.SetVector("Data2", box.maxPosition + transform.position);
//                 computeShader.SetVector("Data3", new Vector3(box.layerCount, 0, 0));
//                 break;

//             case SphereSpawnData sphere:
//                 computeShader.SetVector("Data1", sphere.center + transform.position);
//                 computeShader.SetVector("Data2", new Vector3(sphere.maxRadius, 0, 0));
//                 computeShader.SetVector("Data3", new Vector3(sphere.layerCount, sphere.startRadius, 0));
//                 break;
//         }
//     }

//     void Update()
//     {
//         if (instanceDataBuffer == null) return;

//         // Increment spawn count
//         if (currentInstanceCount < countData.maxInstances)
//         {
//             int newCount = Mathf.Min(currentInstanceCount + countData.incrementPerFrame, countData.maxInstances);
//             if (newCount > currentInstanceCount)
//             {
//                 SpawnInstances(newCount - currentInstanceCount, currentInstanceCount);
//                 int spawned = newCount - currentInstanceCount;
//                 currentInstanceCount = newCount;
//                 totalSpawned += spawned;
//                 activeCount += spawned;
//             }
//         }

//         // Perform culling
//         PerformCulling();

//         // Check for deletions
//         ProcessDeletions();

//         // Render instances
//         RenderInstances();
//     }

//     void PerformCulling()
//     {
//         // Reset filtered buffer
//         filteredBuffer.SetCounterValue(0);

//         // Extract frustum planes from camera
//         GeometryUtility.CalculateFrustumPlanes(cullingCamera, frustumPlanes);

//         // Convert planes to float4 array for GPU (normal.xyz, distance)
//         Vector4[] planesData = new Vector4[6];
//         for (int i = 0; i < 6; i++)
//         {
//             planesData[i] = new Vector4(
//                 frustumPlanes[i].normal.x,
//                 frustumPlanes[i].normal.y,
//                 frustumPlanes[i].normal.z,
//                 frustumPlanes[i].distance
//             );
//         }
//         frustumPlanesBuffer.SetData(planesData);

//         // Setup frustum culling compute
//         computeShader.SetBuffer(cullKernel, "inputBuffer", instanceDataBuffer);
//         computeShader.SetBuffer(cullKernel, "filteredBuffer", filteredBuffer);
//         computeShader.SetBuffer(cullKernel, "frustumPlanes", frustumPlanesBuffer);

//         int threadGroups = Mathf.CeilToInt(currentInstanceCount / 64f);
//         computeShader.Dispatch(cullKernel, threadGroups, 1, 1);

//         // Optional: Near player filtering (additional pass)
//         if (usePlayerRadius && player != null)
//         {
//             nearPlayerBuffer.SetCounterValue(0);
//             computeShader.SetBuffer(nearPlayerKernel, "inputBufferNear", filteredBuffer);
//             computeShader.SetBuffer(nearPlayerKernel, "nearPlayerBuffer", nearPlayerBuffer);
//             computeShader.SetVector("playerPos", player.position);
//             computeShader.SetFloat("radius", cullingRadius);
//             computeShader.Dispatch(nearPlayerKernel, threadGroups, 1, 1);
//         }
//     }

//     void ProcessDeletions()
//     {
//         int[] countArray = new int[1];
//         ComputeBuffer.CopyCount(deletionIndicesBuffer, deletionCountBuffer, 0);
//         deletionCountBuffer.GetData(countArray);

//         int newDeletions = countArray[0];
//         if (newDeletions > 0)
//         {
//             int[] deletedIndices = new int[newDeletions];
//             deletionIndicesBuffer.GetData(deletedIndices, 0, 0, newDeletions);

//             deletedCount += newDeletions;
//             activeCount -= newDeletions;

//             Debug.Log($"Silindi: {newDeletions} obje | Toplam Silinen: {deletedCount}");

//             // Reset deletion buffers
//             deletionIndicesBuffer.SetCounterValue(0);
//             deletionCountBuffer.SetData(new int[] { 0 });
//         }
//     }

//     void RenderInstances()
//     {
//         // Update args buffer with filtered count
//         ComputeBuffer renderBuffer = (usePlayerRadius && player != null) ? nearPlayerBuffer : filteredBuffer;
//         ComputeBuffer.CopyCount(renderBuffer, argsBuffer, sizeof(uint));

//         // Read visible count for statistics
//         uint[] argsData = new uint[5];
//         argsBuffer.GetData(argsData);
//         visibleCount = (int)argsData[1];

//         // Set buffer to material
//         instanceMaterial.SetBuffer("_InstanceData", renderBuffer);

//         // Draw
//         Bounds bounds = new Bounds(transform.position, Vector3.one * 1000f);
//         Graphics.DrawMeshInstancedIndirect(instanceMesh, 0, instanceMaterial, bounds, argsBuffer);
//     }

//     Mesh CreateQuadMesh()
//     {
//         Mesh mesh = new Mesh();
//         mesh.vertices = new Vector3[]
//         {
//             new Vector3(-0.5f, -0.5f, 0),
//             new Vector3(0.5f, -0.5f, 0),
//             new Vector3(-0.5f, 0.5f, 0),
//             new Vector3(0.5f, 0.5f, 0)
//         };
//         mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
//         mesh.uv = new Vector2[]
//         {
//             new Vector2(0, 0),
//             new Vector2(1, 0),
//             new Vector2(0, 1),
//             new Vector2(1, 1)
//         };
//         mesh.RecalculateNormals();
//         return mesh;
//     }

//     Mesh CreateMeshFromSprite(Sprite sprite)
//     {
//         Mesh mesh = new Mesh();

//         // Get sprite data
//         Vector2[] spriteVertices = sprite.vertices;
//         ushort[] spriteTriangles = sprite.triangles;
//         Vector2[] spriteUVs = sprite.uv;

//         // Convert to mesh format
//         Vector3[] vertices = new Vector3[spriteVertices.Length];
//         for (int i = 0; i < spriteVertices.Length; i++)
//         {
//             vertices[i] = new Vector3(spriteVertices[i].x, spriteVertices[i].y, 0);
//         }

//         int[] triangles = new int[spriteTriangles.Length];
//         for (int i = 0; i < spriteTriangles.Length; i++)
//         {
//             triangles[i] = spriteTriangles[i];
//         }

//         mesh.vertices = vertices;
//         mesh.triangles = triangles;
//         mesh.uv = spriteUVs;
//         mesh.RecalculateNormals();
//         mesh.RecalculateBounds();

//         return mesh;
//     }

//     void OnDestroy()
//     {
//         instanceDataBuffer?.Release();
//         filteredBuffer?.Release();
//         nearPlayerBuffer?.Release();
//         argsBuffer?.Release();
//         deletionIndicesBuffer?.Release();
//         deletionCountBuffer?.Release();
//         frustumPlanesBuffer?.Release();
//     }

//     void OnDrawGizmos()
//     {
//         areaData?.DrawGizmos(transform.position);

//         // Draw camera frustum
//         if (cullingCamera != null)
//         {
//             Gizmos.matrix = Matrix4x4.identity;
//             Gizmos.color = Color.green;

//             // Draw frustum wireframe
//             Vector3[] nearCorners = new Vector3[4];
//             Vector3[] farCorners = new Vector3[4];

//             cullingCamera.CalculateFrustumCorners(
//                 new Rect(0, 0, 1, 1),
//                 cullingCamera.nearClipPlane,
//                 Camera.MonoOrStereoscopicEye.Mono,
//                 nearCorners
//             );

//             cullingCamera.CalculateFrustumCorners(
//                 new Rect(0, 0, 1, 1),
//                 cullingCamera.farClipPlane,
//                 Camera.MonoOrStereoscopicEye.Mono,
//                 farCorners
//             );

//             for (int i = 0; i < 4; i++)
//             {
//                 nearCorners[i] = cullingCamera.transform.TransformPoint(nearCorners[i]);
//                 farCorners[i] = cullingCamera.transform.TransformPoint(farCorners[i]);
//             }

//             // Draw near plane
//             for (int i = 0; i < 4; i++)
//                 Gizmos.DrawLine(nearCorners[i], nearCorners[(i + 1) % 4]);

//             // Draw far plane
//             for (int i = 0; i < 4; i++)
//                 Gizmos.DrawLine(farCorners[i], farCorners[(i + 1) % 4]);

//             // Draw connecting lines
//             for (int i = 0; i < 4; i++)
//                 Gizmos.DrawLine(nearCorners[i], farCorners[i]);
//         }

//         // Draw player radius if enabled
//         if (usePlayerRadius && player != null)
//         {
//             Gizmos.color = Color.yellow;
//             Gizmos.DrawWireSphere(player.position, cullingRadius);
//         }

//         // Draw statistics text
//         DrawStatisticsGUI();
//     }

//     void DrawStatisticsGUI()
//     {
// #if UNITY_EDITOR
//         UnityEditor.Handles.BeginGUI();

//         // Background box
//         GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
//         boxStyle.alignment = TextAnchor.UpperLeft;
//         boxStyle.normal.background = MakeTex(2, 2, new Color(0, 0, 0, 0.7f));
//         boxStyle.padding = new RectOffset(10, 10, 10, 10);

//         // Text style
//         GUIStyle textStyle = new GUIStyle();
//         textStyle.normal.textColor = Color.white;
//         textStyle.fontSize = 14;
//         textStyle.fontStyle = FontStyle.Bold;

//         // Calculate positions
//         float startX = 10;
//         float startY = 10;
//         float width = 280;
//         float height = 140;

//         // Draw box
//         GUI.Box(new Rect(startX, startY, width, height), "", boxStyle);

//         // Draw text
//         float yOffset = startY + 10;
//         float lineHeight = 20;

//         GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight),
//             $"GPU INSTANCING İSTATİSTİKLERİ", textStyle);

//         textStyle.fontSize = 12;
//         textStyle.fontStyle = FontStyle.Normal;

//         yOffset += lineHeight + 5;

//         // Visible count - green
//         textStyle.normal.textColor = Color.green;
//         GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight),
//             $"Ekranda Görünen: {visibleCount:N0}", textStyle);
//         yOffset += lineHeight;

//         // Active count - cyan
//         textStyle.normal.textColor = Color.cyan;
//         GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight),
//             $"Aktif Obje: {activeCount:N0}", textStyle);
//         yOffset += lineHeight;

//         // Deleted count - red
//         textStyle.normal.textColor = Color.red;
//         GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight),
//             $"Silinen Obje: {deletedCount:N0}", textStyle);
//         yOffset += lineHeight;

//         // Total spawned - yellow
//         textStyle.normal.textColor = Color.yellow;
//         GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight),
//             $"Toplam Spawn: {totalSpawned:N0}", textStyle);
//         yOffset += lineHeight;

//         // Culling percentage - white
//         float cullingPercentage = activeCount > 0 ? (visibleCount / (float)activeCount * 100f) : 0;
//         textStyle.normal.textColor = Color.white;
//         GUI.Label(new Rect(startX + 10, yOffset, width - 20, lineHeight),
//             $"Görünürlük: %{cullingPercentage:F1}", textStyle);

//         UnityEditor.Handles.EndGUI();
// #endif
//     }

// #if UNITY_EDITOR
//     private Texture2D MakeTex(int width, int height, Color col)
//     {
//         Color[] pix = new Color[width * height];
//         for (int i = 0; i < pix.Length; i++)
//             pix[i] = col;

//         Texture2D result = new Texture2D(width, height);
//         result.SetPixels(pix);
//         result.Apply();
//         return result;
//     }
// #endif

//     // Public accessors for external systems
//     public ComputeBuffer GetInstanceDataBuffer() => instanceDataBuffer;
//     public ComputeBuffer GetDeletionIndicesBuffer() => deletionIndicesBuffer;
//     public ComputeBuffer GetDeletionCountBuffer() => deletionCountBuffer;
//     public int CurrentInstanceCount => currentInstanceCount;
// }
