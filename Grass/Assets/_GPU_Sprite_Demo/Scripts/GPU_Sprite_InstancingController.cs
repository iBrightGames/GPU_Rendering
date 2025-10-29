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
    public float gizmoDotSize = 0.3f;
    public float gizmoDisplayDuration = 1f;
    public bool enableFrustumCulling = true;
    public float statsUpdateInterval = 0.1f; // Update stats more frequently

    // Buffers - Even simpler now!
    private ComputeBuffer mainBuffer;
    private ComputeBuffer newItemsBuffer;
    private ComputeBuffer deletedItemsBuffer;
    private ComputeBuffer renderBuffer;
    private ComputeBuffer argsBuffer;
    private ComputeBuffer frustumPlanesBuffer;
    private ComputeBuffer counterBuffer; // GPU-side atomic counter

    private Mesh quadMesh;
    private uint[] argsData = new uint[5] { 0, 0, 0, 0, 0 };
    private Camera mainCamera;

    // Debug tracking
    private struct DeletedItemDebug
    {
        public Vector3 position;
        public float timeDeleted;
    }

    private List<DeletedItemDebug> deletedPositions = new List<DeletedItemDebug>();
    private int activeCount = 0;
    private int emptySlotCount = 0;
    private int totalInstances = 0;
    private int deletedItemsCount = 0;
    private float lastStatsUpdate = 0f;

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
        InitializeGUIStyle();
        mainCamera = Camera.main;
    }

    void InitializeGUIStyle()
    {
        guiStyle = new GUIStyle();
        guiStyle.fontSize = 24;
        guiStyle.fontStyle = FontStyle.Bold;
        guiStyle.normal.textColor = Color.white;
        guiStyle.alignment = TextAnchor.UpperLeft;
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
        newItemsBuffer = new ComputeBuffer(maxInstances, stride);
        deletedItemsBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
        renderBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
        argsBuffer = new ComputeBuffer(1, argsData.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        frustumPlanesBuffer = new ComputeBuffer(6, sizeof(float) * 4);
        counterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw); // Atomic counter

        // Initialize all slots as inactive
        ItemData[] initialData = new ItemData[maxInstances];
        for (int i = 0; i < maxInstances; i++)
        {
            initialData[i].active = 0;
        }
        mainBuffer.SetData(initialData);

        // Initialize counter to 0
        counterBuffer.SetData(new uint[] { 0 });

        argsData[0] = quadMesh.GetIndexCount(0);
        argsBuffer.SetData(argsData);
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
        renderBuffer.SetCounterValue(0);
        deletedItemsBuffer.SetCounterValue(0);
        

        // 1. Erase instances
        EraseInstancesGPU();

        // 2. Read deleted items
        ReadDeletedItems();
        

        // 3. Add new instances (simple linear allocation)
        AddNewInstances();

        // 4. Prepare render buffer
        PrepareRenderBuffer();

        // 5. Update statistics
        bool shouldUpdateStats = Time.time - lastStatsUpdate >= statsUpdateInterval;
        if (shouldUpdateStats)
        {
            UpdateStatistics();
            lastStatsUpdate = Time.time;
        }

        // 6. Clean up old gizmos
        CleanupOldGizmos();

        // 7. Render
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

        // GPU finds free slots automatically using atomic counter!
        int kernel = instanceCompute.FindKernel("AddNewItems");
        instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
        instanceCompute.SetBuffer(kernel, "newItemsBuffer", newItemsBuffer);
        instanceCompute.SetBuffer(kernel, "counterBuffer", counterBuffer);
        instanceCompute.SetInt("newItemsCount", toAdd);
        instanceCompute.SetInt("maxInstances", maxInstances);

        int threadGroups = Mathf.CeilToInt(toAdd / 64.0f);
        instanceCompute.Dispatch(kernel, threadGroups, 1, 1);
    }

    void EraseInstancesGPU()
    {
        int kernel = instanceCompute.FindKernel("SphericalCulling");
        instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
        instanceCompute.SetBuffer(kernel, "deletedItemsBuffer", deletedItemsBuffer);
        instanceCompute.SetVector("playerPos", Player.transform.position);
        instanceCompute.SetFloat("radius", eraseRadius);
        instanceCompute.SetInt("instanceCount", maxInstances);

        int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
        instanceCompute.Dispatch(kernel, threadGroups, 1, 1);
    }

    void ReadDeletedItems()
    {
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
        ComputeBuffer.CopyCount(deletedItemsBuffer, countBuffer, 0);

        uint[] countArray = new uint[1];
        countBuffer.GetData(countArray);
        int deletedCount = (int)countArray[0];
        countBuffer.Release();

        if (deletedCount > 0)
        {
            int safeCount = Mathf.Min(deletedCount, 500);

            ItemData[] deletedData = new ItemData[safeCount];
            deletedItemsBuffer.GetData(deletedData, 0, 0, safeCount);

            float currentTime = Time.time;
            for (int i = 0; i < safeCount; i++)
            {
                deletedPositions.Add(new DeletedItemDebug
                {
                    position = deletedData[i].position,
                    timeDeleted = currentTime
                });
            }
        }
    }

    void CleanupOldGizmos()
    {
        float currentTime = Time.time;
        deletedPositions.RemoveAll(item => currentTime - item.timeDeleted > gizmoDisplayDuration);
    }

    void PrepareRenderBuffer()
    {
        int kernel = instanceCompute.FindKernel("PrepareRenderBuffer");
        instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
        instanceCompute.SetBuffer(kernel, "renderBuffer", renderBuffer);
        instanceCompute.SetInt("instanceCount", maxInstances);

        if (enableFrustumCulling && mainCamera != null)
        {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
            Vector4[] planeData = new Vector4[6];
            for (int i = 0; i < 6; i++)
            {
                planeData[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            }
            frustumPlanesBuffer.SetData(planeData);
            instanceCompute.SetBuffer(kernel, "frustumPlanes", frustumPlanesBuffer);
            instanceCompute.SetInt("enableFrustum", 1);
        }
        else
        {
            instanceCompute.SetInt("enableFrustum", 0);
        }

        int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
        instanceCompute.Dispatch(kernel, threadGroups, 1, 1);

        ComputeBuffer.CopyCount(renderBuffer, argsBuffer, sizeof(uint));
    }

    void UpdateStatistics()
    {
        // Get active count
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
        ComputeBuffer.CopyCount(renderBuffer, countBuffer, 0);

        uint[] countArray = new uint[1];
        countBuffer.GetData(countArray);
        activeCount = (int)countArray[0];
        countBuffer.Release();

        // Get deleted items count from gizmo list (currently visible)
        deletedItemsCount = deletedPositions.Count;

        // Calculate empty slots
        totalInstances = maxInstances;
        emptySlotCount = totalInstances - activeCount;
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
        GUI.Box(new Rect(100, 100, 500, 160), "");

        int yPos = 100;
        int lineHeight = 100;

        GUI.Label(new Rect(50, yPos, 600, 100), $"Total Capacity: {totalInstances}", guiStyle);
        guiStyle.fontSize = 50;
        yPos += lineHeight;

        GUIStyle activeStyle = new GUIStyle(guiStyle);
        activeStyle.normal.textColor = Color.green;
        activeStyle.fontSize = 50;
        GUI.Label(new Rect(50, yPos, 600, 100), $"Active Items: {activeCount}", activeStyle);
        yPos += lineHeight;

        GUIStyle emptyStyle = new GUIStyle(guiStyle);
        emptyStyle.normal.textColor = Color.cyan;
        emptyStyle.fontSize = 50;
        GUI.Label(new Rect(50, yPos, 600, 100), $"Empty Slots: {emptySlotCount}", emptyStyle);
        yPos += lineHeight;

        GUIStyle deletedStyle = new GUIStyle(guiStyle);
        deletedStyle.normal.textColor = Color.red;
        deletedStyle.fontSize = 50;
        GUI.Label(new Rect(50, yPos, 600, 100), $"Deleted Items: {deletedItemsCount}", deletedStyle);
    }

    void OnDrawGizmos()
    {
        if (!showDeletedGizmos || deletedPositions == null) return;

        float currentTime = Time.time;

        // Draw deleted items as solid dots with fade
        foreach (var item in deletedPositions)
        {
            float age = currentTime - item.timeDeleted;
            float fadeAlpha = 1f - (age / gizmoDisplayDuration);

            Color gizmoColor = deletedGizmoColor;
            gizmoColor.a = fadeAlpha;
            Gizmos.color = gizmoColor;

            // Draw as solid sphere (dot)
            Gizmos.DrawSphere(item.position, gizmoDotSize);
        }

        // Draw erase radius around player
        if (Player != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 1f); // Semi-transparent yellow
            Gizmos.DrawWireSphere(Player.transform.position, eraseRadius);
        }
    }

    void OnDisable()
    {
        mainBuffer?.Release();
        newItemsBuffer?.Release();
        deletedItemsBuffer?.Release();
        renderBuffer?.Release();
        argsBuffer?.Release();
        frustumPlanesBuffer?.Release();
        counterBuffer?.Release();
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
