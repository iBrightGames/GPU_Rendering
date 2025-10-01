using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class GPU_Sprite_InstancingController : MonoBehaviour
{
    [Header("References")]
    public GameObject Player;
    public SpriteRenderer sourceSprite;

    [Header("Instance Settings")]
    public int maxInstances = 10000;
    public int spawnPerFrame = 10;
    public float eraseRadius = 5f;
    public Vector3 minPosition = new Vector3(0, 0, 0);
    public Vector3 maxPosition = new Vector3(100, 100, 0);

    [Header("Shaders")]
    public ComputeShader instanceCompute;
    public Material instancingMaterial;

    [Header("Debug Settings")]
    public bool showDeletedGizmos = true;
    public Color deletedGizmoColor = Color.red;
    public float gizmoRadius = 0.5f;
    public float statsUpdateInterval = 0.5f; // Update stats twice per second
    public bool trackDeletedItems = true; // Toggle GPU readback
    public float gizmoDisplayDuration = 1f; // How long to show deleted items

    // Buffers
    private ComputeBuffer mainBuffer;
    private ComputeBuffer freeSlotsBuffer;
    private ComputeBuffer freeSlotsAppendBuffer;
    private ComputeBuffer newItemsBuffer;
    private ComputeBuffer deletedItemsBuffer;
    private ComputeBuffer renderBuffer;
    private ComputeBuffer argsBuffer;

    private Mesh quadMesh;
    private uint[] argsData = new uint[5] { 0, 0, 0, 0, 0 };

    // Debug tracking
    private struct DeletedItemDebug
    {
        public Vector3 position;
        public float timeDeleted;
    }

    private List<DeletedItemDebug> deletedPositions = new List<DeletedItemDebug>();
    private int activeCount = 0;
    private int inactiveCount = 0;
    private int totalInstances = 0;
    private int deletedThisFrame = 0;
    private float lastStatsUpdate = 0f;

    // GUI Style
    private GUIStyle guiStyle;

    struct ItemData
    {
        public Vector3 position;
        public int active;
    }

    void Start()
    {
        if (!ValidateComponents()) return;
        InitializeBuffers();
        InitializeFreeSlots();
        InitializeGUIStyle();
    }

    void InitializeGUIStyle()
    {
        guiStyle = new GUIStyle();
        guiStyle.fontSize = 24;
        guiStyle.fontStyle = FontStyle.Bold;
        guiStyle.normal.textColor = Color.white;
        guiStyle.alignment = TextAnchor.UpperLeft;

        // Add shadow for better readability
        guiStyle.normal.background = MakeTex(2, 2, new Color(0, 0, 0, 0.7f));
        guiStyle.padding = new RectOffset(10, 10, 5, 5);
    }

    Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
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

        if (sourceSprite != null)
            sourceSprite.enabled = false;

        return true;
    }

    void InitializeBuffers()
    {
        int stride = Marshal.SizeOf(typeof(ItemData));

        mainBuffer = new ComputeBuffer(maxInstances, stride);
        freeSlotsBuffer = new ComputeBuffer(maxInstances, sizeof(uint), ComputeBufferType.Append | ComputeBufferType.Counter);
        freeSlotsAppendBuffer = new ComputeBuffer(maxInstances, sizeof(uint), ComputeBufferType.Append);
        newItemsBuffer = new ComputeBuffer(maxInstances, stride);
        deletedItemsBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
        renderBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
        argsBuffer = new ComputeBuffer(1, argsData.Length * sizeof(uint), ComputeBufferType.IndirectArguments);

        // Initialize main buffer with inactive items
        ItemData[] initialData = new ItemData[maxInstances];
        for (int i = 0; i < maxInstances; i++)
        {
            initialData[i].active = 0;
        }
        mainBuffer.SetData(initialData);

        // Set args buffer
        argsData[0] = quadMesh.GetIndexCount(0);
        argsBuffer.SetData(argsData);
    }

    void InitializeFreeSlots()
    {
        uint[] freeSlots = new uint[maxInstances];
        for (uint i = 0; i < maxInstances; i++)
        {
            freeSlots[i] = i;
        }
        freeSlotsBuffer.SetData(freeSlots);
        freeSlotsBuffer.SetCounterValue((uint)maxInstances);
        freeSlotsAppendBuffer.SetCounterValue(0);
    }

    Vector3 GetRandomPosition()
    {
        return new Vector3(
            Random.Range(minPosition.x, maxPosition.x),
            Random.Range(minPosition.y, maxPosition.y),
            Random.Range(minPosition.z, maxPosition.z)
        );
    }

    void Update()
    {
        // Reset buffers
        renderBuffer.SetCounterValue(0);
        deletedItemsBuffer.SetCounterValue(0);
        deletedThisFrame = 0; // Reset frame counter

        // 1. Erase instances
        EraseInstancesGPU();

        // 2. Read deleted items THIS FRAME (always read to track deletions)
        if (trackDeletedItems)
        {
            ReadDeletedItems();
        }

        // 3. Copy freed slots back to consume buffer
        CopyFreedSlots();

        // 4. Add new instances
        AddNewInstances();

        // 5. Prepare render buffer
        PrepareRenderBuffer();

        // 6. Update statistics (only at interval)
        bool shouldUpdateStats = Time.time - lastStatsUpdate >= statsUpdateInterval;
        if (shouldUpdateStats)
        {
            UpdateStatistics();
            lastStatsUpdate = Time.time;
        }

        // 7. Clean up old gizmo positions (remove items older than display duration)
        CleanupOldGizmos();

        // 8. Render
        RenderInstances();
    }

    void AddNewInstances()
    {
        int toAdd = Mathf.Min(spawnPerFrame, maxInstances);
        if (toAdd <= 0) return;

        ItemData[] newData = new ItemData[toAdd];
        for (int i = 0; i < toAdd; i++)
        {
            newData[i] = new ItemData
            {
                position = GetRandomPosition(),
                active = 1
            };
        }

        newItemsBuffer.SetData(newData);

        int kernel = instanceCompute.FindKernel("NewItemAdding");
        instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
        instanceCompute.SetBuffer(kernel, "newItemsBuffer", newItemsBuffer);
        instanceCompute.SetBuffer(kernel, "freeSlotsBuffer", freeSlotsBuffer);
        instanceCompute.SetInt("newItemsCount", toAdd);

        int threadGroups = Mathf.CeilToInt(toAdd / 64.0f);
        instanceCompute.Dispatch(kernel, threadGroups, 1, 1);
    }

    void EraseInstancesGPU()
    {
        int kernel = instanceCompute.FindKernel("SphericalCulling");
        instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
        instanceCompute.SetBuffer(kernel, "freeSlotsAppendBuffer", freeSlotsAppendBuffer);
        instanceCompute.SetBuffer(kernel, "deletedItemsBuffer", deletedItemsBuffer);
        instanceCompute.SetVector("playerPos", Player.transform.position);
        instanceCompute.SetFloat("radius", eraseRadius);
        instanceCompute.SetInt("instanceCount", maxInstances);

        int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
        instanceCompute.Dispatch(kernel, threadGroups, 1, 1);
    }

    void ReadDeletedItems()
    {
        // Get count buffer
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
        ComputeBuffer.CopyCount(deletedItemsBuffer, countBuffer, 0);

        uint[] countArray = new uint[1];
        countBuffer.GetData(countArray);
        int deletedCount = (int)countArray[0];
        countBuffer.Release();

        deletedThisFrame = deletedCount; // Store for display

        if (deletedCount > 0)
        {
            // Clamp to reasonable size for safety
            int safeCount = Mathf.Min(deletedCount, 500);

            // Read deleted items
            ItemData[] deletedData = new ItemData[safeCount];
            deletedItemsBuffer.GetData(deletedData, 0, 0, safeCount);

            // Store positions with timestamp
            float currentTime = Time.time;
            for (int i = 0; i < safeCount; i++)
            {
                deletedPositions.Add(new DeletedItemDebug
                {
                    position = deletedData[i].position,
                    timeDeleted = currentTime
                });
            }

            // Debug log to verify data
            if (deletedCount > 0)
            {
                Debug.Log($"[Frame {Time.frameCount}] Deleted {deletedCount} items. First pos: {deletedData[0].position}");
            }
        }
    }

    void CleanupOldGizmos()
    {
        float currentTime = Time.time;
        // Remove items older than display duration
        deletedPositions.RemoveAll(item => currentTime - item.timeDeleted > gizmoDisplayDuration);
    }

    void CopyFreedSlots()
    {
        // Copy count from append buffer to consume buffer
        ComputeBuffer.CopyCount(freeSlotsAppendBuffer, freeSlotsBuffer, 0);

        // Reset append buffer for next frame
        freeSlotsAppendBuffer.SetCounterValue(0);
    }

    void PrepareRenderBuffer()
    {
        int kernel = instanceCompute.FindKernel("PrepareRenderBuffer");
        instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
        instanceCompute.SetBuffer(kernel, "renderBuffer", renderBuffer);
        instanceCompute.SetInt("instanceCount", maxInstances);

        int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
        instanceCompute.Dispatch(kernel, threadGroups, 1, 1);

        // Copy active count to args buffer
        ComputeBuffer.CopyCount(renderBuffer, argsBuffer, sizeof(uint));
    }

    void UpdateStatistics()
    {
        // Create a temporary buffer to get the count
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
        ComputeBuffer.CopyCount(renderBuffer, countBuffer, 0);

        uint[] countArray = new uint[1];
        countBuffer.GetData(countArray);
        activeCount = (int)countArray[0];
        countBuffer.Release();

        // Calculate totals
        totalInstances = maxInstances;
        inactiveCount = totalInstances - activeCount;
    }

    void RenderInstances()
    {
        if (instancingMaterial != null)
        {
            instancingMaterial.SetBuffer("_InstanceDataBuffer", renderBuffer);
            instancingMaterial.SetFloat("_ScaleMultiplier", 1.0f);

            Graphics.DrawMeshInstancedIndirect(
                quadMesh,
                0,
                instancingMaterial,
                new Bounds(Vector3.zero, Vector3.one * 100f),
                argsBuffer
            );
        }
    }

    void OnGUI()
    {
        // Background box
        GUI.Box(new Rect(10, 10, 350, 160), "");

        int yPos = 20;
        int lineHeight = 35;

        GUI.Label(new Rect(20, yPos, 400, 30), $"Total Instances: {totalInstances}", guiStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 400, 30), $"Active: {activeCount}", guiStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 400, 30), $"Inactive: {inactiveCount}", guiStyle);
        yPos += lineHeight;

        // Show deleted THIS FRAME (not total)
        GUIStyle deletedStyle = new GUIStyle(guiStyle);
        deletedStyle.normal.textColor = Color.yellow;
        GUI.Label(new Rect(20, yPos, 400, 30), $"Deleted This Frame: {deletedThisFrame}", deletedStyle);
        yPos += lineHeight;

        // Show currently visible gizmos
        GUIStyle gizmoStyle = new GUIStyle(guiStyle);
        gizmoStyle.normal.textColor = Color.red;
        gizmoStyle.fontSize = 20;
        GUI.Label(new Rect(20, yPos, 400, 30), $"Gizmos Visible: {deletedPositions.Count}", gizmoStyle);
    }

    void OnDrawGizmos()
    {
        if (!showDeletedGizmos || deletedPositions == null) return;

        float currentTime = Time.time;

        // Draw deleted items with fade effect
        foreach (var item in deletedPositions)
        {
            float age = currentTime - item.timeDeleted;
            float fadeAlpha = 1f - (age / gizmoDisplayDuration);

            Color gizmoColor = deletedGizmoColor;
            gizmoColor.a = fadeAlpha;
            Gizmos.color = gizmoColor;

            Gizmos.DrawWireSphere(item.position, gizmoRadius);

            // Draw a solid sphere in the center for better visibility
            Gizmos.DrawSphere(item.position, gizmoRadius * 0.3f);
        }

        // Draw erase radius around player
        if (Player != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(Player.transform.position, eraseRadius);
        }
    }

    void OnDisable()
    {
        mainBuffer?.Release();
        freeSlotsBuffer?.Release();
        freeSlotsAppendBuffer?.Release();
        newItemsBuffer?.Release();
        deletedItemsBuffer?.Release();
        renderBuffer?.Release();
        argsBuffer?.Release();
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
            new Vector2(0,0),
            new Vector2(1,0),
            new Vector2(1,1),
            new Vector2(0,1)
        };
        mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        return mesh;
    }
}
// using UnityEngine;
// using System.Collections.Generic;
// using System.Runtime.InteropServices;

// public class GPU_Sprite_InstancingController : MonoBehaviour
// {
//     [Header("References")]
//     public GameObject Player;
//     public SpriteRenderer sourceSprite;

//     [Header("Instance Settings")]
//     public int maxInstances = 10000;
//     public int spawnPerFrame = 10;
//     public float eraseRadius = 5f;
//     public Vector3 minPosition = new Vector3(0, 0, 0);
//     public Vector3 maxPosition = new Vector3(100, 100, 0);

//     [Header("Shaders")]
//     public ComputeShader instanceCompute;
//     public Material instancingMaterial;

//     [Header("Debug Settings")]
//     public bool showDeletedGizmos = true;
//     public Color deletedGizmoColor = Color.red;
//     public float gizmoRadius = 0.5f;

//     // Buffers
//     private ComputeBuffer mainBuffer;
//     private ComputeBuffer freeSlotsBuffer;
//     private ComputeBuffer freeSlotsAppendBuffer;
//     private ComputeBuffer newItemsBuffer;
//     private ComputeBuffer deletedItemsBuffer;
//     private ComputeBuffer renderBuffer;
//     private ComputeBuffer argsBuffer;

//     private Mesh quadMesh;
//     private uint[] argsData = new uint[5] { 0, 0, 0, 0, 0 };

//     // Debug tracking
//     private List<Vector3> deletedPositions = new List<Vector3>();
//     private int activeCount = 0;
//     private int inactiveCount = 0;
//     private int totalInstances = 0;

//     // GUI Style
//     private GUIStyle guiStyle;

//     struct ItemData
//     {
//         public Vector3 position;
//         public int active;
//     }

//     void Start()
//     {
//         if (!ValidateComponents()) return;
//         InitializeBuffers();
//         InitializeFreeSlots();
//         InitializeGUIStyle();
//     }

//     void InitializeGUIStyle()
//     {
//         guiStyle = new GUIStyle();
//         guiStyle.fontSize = 20;
//         guiStyle.normal.textColor = Color.white;
//         guiStyle.alignment = TextAnchor.UpperLeft;
//     }

//     bool ValidateComponents()
//     {
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

//         quadMesh = CreateQuad();

//         if (instancingMaterial == null)
//         {
//             Shader shader = Shader.Find("CustomUnlit/SingleSpriteCompute");
//             if (shader != null)
//             {
//                 instancingMaterial = new Material(shader);
//                 instancingMaterial.mainTexture = sourceSprite.sprite.texture;
//                 instancingMaterial.enableInstancing = true;
//             }
//         }

//         if (sourceSprite != null)
//             sourceSprite.enabled = false;

//         return true;
//     }

//     void InitializeBuffers()
//     {
//         int stride = Marshal.SizeOf(typeof(ItemData));

//         mainBuffer = new ComputeBuffer(maxInstances, stride);
//         freeSlotsBuffer = new ComputeBuffer(maxInstances, sizeof(uint), ComputeBufferType.Append | ComputeBufferType.Counter);
//         freeSlotsAppendBuffer = new ComputeBuffer(maxInstances, sizeof(uint), ComputeBufferType.Append);
//         newItemsBuffer = new ComputeBuffer(maxInstances, stride);
//         deletedItemsBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
//         renderBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
//         argsBuffer = new ComputeBuffer(1, argsData.Length * sizeof(uint), ComputeBufferType.IndirectArguments);

//         // Initialize main buffer with inactive items
//         ItemData[] initialData = new ItemData[maxInstances];
//         for (int i = 0; i < maxInstances; i++)
//         {
//             initialData[i].active = 0;
//         }
//         mainBuffer.SetData(initialData);

//         // Set args buffer
//         argsData[0] = quadMesh.GetIndexCount(0);
//         argsBuffer.SetData(argsData);
//     }

//     void InitializeFreeSlots()
//     {
//         uint[] freeSlots = new uint[maxInstances];
//         for (uint i = 0; i < maxInstances; i++)
//         {
//             freeSlots[i] = i;
//         }
//         freeSlotsBuffer.SetData(freeSlots);
//         freeSlotsBuffer.SetCounterValue((uint)maxInstances);
//         freeSlotsAppendBuffer.SetCounterValue(0);
//     }

//     Vector3 GetRandomPosition()
//     {
//         return new Vector3(
//             Random.Range(minPosition.x, maxPosition.x),
//             Random.Range(minPosition.y, maxPosition.y),
//             Random.Range(minPosition.z, maxPosition.z)
//         );
//     }

//     void Update()
//     {
//         // Reset buffers
//         renderBuffer.SetCounterValue(0);
//         deletedItemsBuffer.SetCounterValue(0);

//         // 1. Erase instances
//         EraseInstancesGPU();

//         // 2. Read deleted items from GPU
//         ReadDeletedItems();

//         // 3. Copy freed slots back to consume buffer
//         CopyFreedSlots();

//         // 4. Add new instances
//         AddNewInstances();

//         // 5. Prepare render buffer
//         PrepareRenderBuffer();

//         // 6. Update statistics
//         UpdateStatistics();

//         // 7. Render
//         RenderInstances();
//     }

//     void AddNewInstances()
//     {
//         int toAdd = Mathf.Min(spawnPerFrame, maxInstances);
//         if (toAdd <= 0) return;

//         ItemData[] newData = new ItemData[toAdd];
//         for (int i = 0; i < toAdd; i++)
//         {
//             newData[i] = new ItemData
//             {
//                 position = GetRandomPosition(),
//                 active = 1
//             };
//         }

//         newItemsBuffer.SetData(newData);

//         int kernel = instanceCompute.FindKernel("NewItemAdding");
//         instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
//         instanceCompute.SetBuffer(kernel, "newItemsBuffer", newItemsBuffer);
//         instanceCompute.SetBuffer(kernel, "freeSlotsBuffer", freeSlotsBuffer);
//         instanceCompute.SetInt("newItemsCount", toAdd);

//         int threadGroups = Mathf.CeilToInt(toAdd / 64.0f);
//         instanceCompute.Dispatch(kernel, threadGroups, 1, 1);
//     }

//     void EraseInstancesGPU()
//     {
//         int kernel = instanceCompute.FindKernel("SphericalCulling");
//         instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
//         instanceCompute.SetBuffer(kernel, "freeSlotsAppendBuffer", freeSlotsAppendBuffer);
//         instanceCompute.SetBuffer(kernel, "deletedItemsBuffer", deletedItemsBuffer);
//         instanceCompute.SetVector("playerPos", Player.transform.position);
//         instanceCompute.SetFloat("radius", eraseRadius);
//         instanceCompute.SetInt("instanceCount", maxInstances);

//         int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
//         instanceCompute.Dispatch(kernel, threadGroups, 1, 1);
//     }

//     void ReadDeletedItems()
//     {
//         // Create a temporary buffer to get the count
//         ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
//         ComputeBuffer.CopyCount(deletedItemsBuffer, countBuffer, 0);

//         uint[] countArray = new uint[1];
//         countBuffer.GetData(countArray);
//         int deletedCount = (int)countArray[0];
//         countBuffer.Release();

//         if (deletedCount > 0)
//         {
//             // Read deleted items
//             ItemData[] deletedData = new ItemData[deletedCount];
//             deletedItemsBuffer.GetData(deletedData, 0, 0, deletedCount);

//             // Store positions for gizmo drawing
//             for (int i = 0; i < deletedCount; i++)
//             {
//                 deletedPositions.Add(deletedData[i].position);
//             }

//             // Limit stored positions to prevent memory issues
//             if (deletedPositions.Count > 1000)
//             {
//                 deletedPositions.RemoveRange(0, deletedPositions.Count - 1000);
//             }
//         }
//     }

//     void CopyFreedSlots()
//     {
//         // Copy count from append buffer to consume buffer
//         ComputeBuffer.CopyCount(freeSlotsAppendBuffer, freeSlotsBuffer, 0);

//         // Reset append buffer for next frame
//         freeSlotsAppendBuffer.SetCounterValue(0);
//     }

//     void PrepareRenderBuffer()
//     {
//         int kernel = instanceCompute.FindKernel("PrepareRenderBuffer");
//         instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
//         instanceCompute.SetBuffer(kernel, "renderBuffer", renderBuffer);
//         instanceCompute.SetInt("instanceCount", maxInstances);

//         int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
//         instanceCompute.Dispatch(kernel, threadGroups, 1, 1);

//         // Copy active count to args buffer
//         ComputeBuffer.CopyCount(renderBuffer, argsBuffer, sizeof(uint));
//     }

//     void UpdateStatistics()
//     {
//         // Create a temporary buffer to get the count
//         ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
//         ComputeBuffer.CopyCount(renderBuffer, countBuffer, 0);

//         uint[] countArray = new uint[1];
//         countBuffer.GetData(countArray);
//         activeCount = (int)countArray[0];
//         countBuffer.Release();

//         // Calculate totals
//         totalInstances = maxInstances;
//         inactiveCount = totalInstances - activeCount;
//     }

//     void RenderInstances()
//     {
//         if (instancingMaterial != null)
//         {
//             instancingMaterial.SetBuffer("_InstanceDataBuffer", renderBuffer);
//             instancingMaterial.SetFloat("_ScaleMultiplier", 1.0f);

//             Graphics.DrawMeshInstancedIndirect(
//                 quadMesh,
//                 0,
//                 instancingMaterial,
//                 new Bounds(Vector3.zero, Vector3.one * 100f),
//                 argsBuffer
//             );
//         }
//     }

//     void OnGUI()
//     {
//         guiStyle.fontSize = 60;

//         GUI.Box(new Rect(10, 10, 250, 120), "");

//         GUI.Label(new Rect(20, 20, 300, 50), $"Total Instances: {totalInstances}", guiStyle);
//         GUI.Label(new Rect(20, 70, 300, 50), $"Active: {activeCount}", guiStyle);
//         GUI.Label(new Rect(20, 120, 300, 50), $"Inactive: {inactiveCount}", guiStyle);
//         GUI.Label(new Rect(20, 170, 300, 50), $"Deleted (Tracked): {deletedPositions.Count}", guiStyle);
//     }

//     void OnDrawGizmos()
//     {
//         if (!showDeletedGizmos || deletedPositions == null) return;

//         Gizmos.color = deletedGizmoColor;
//         foreach (Vector3 pos in deletedPositions)
//         {
//             Gizmos.DrawWireSphere(pos, gizmoRadius);
//         }

//         // Draw erase radius around player
//         if (Player != null)
//         {
//             Gizmos.color = Color.yellow;
//             Gizmos.DrawWireSphere(Player.transform.position, eraseRadius);
//         }
//     }

//     void OnDisable()
//     {
//         mainBuffer?.Release();
//         freeSlotsBuffer?.Release();
//         freeSlotsAppendBuffer?.Release();
//         newItemsBuffer?.Release();
//         deletedItemsBuffer?.Release();
//         renderBuffer?.Release();
//         argsBuffer?.Release();
//     }

//     Mesh CreateQuad()
//     {
//         Mesh mesh = new Mesh();
//         mesh.vertices = new Vector3[]
//         {
//             new Vector3(-0.5f, -0.5f, 0),
//             new Vector3(0.5f, -0.5f, 0),
//             new Vector3(0.5f, 0.5f, 0),
//             new Vector3(-0.5f, 0.5f, 0)
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
