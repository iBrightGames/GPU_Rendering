using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem;

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

    [Header("Mouse Culling Settings")]
    public bool useMouseCulling = true;
    public float mouseCullingRadius = 2f;

    [Header("Shaders")]
    public ComputeShader instanceCompute;
    public Material instancingMaterial;

    [Header("Debug Settings")]
    public bool showDeletedGizmos = true;
    public Color deletedGizmoColor = Color.red;
    public float gizmoDotSize = 0.3f;
    public float gizmoDisplayDuration = 1f;
    public bool enableFrustumCulling = true;
    public float statsUpdateInterval = 0.1f;

    // Buffers
    private ComputeBuffer mainBuffer;
    private ComputeBuffer newItemsBuffer;
    private ComputeBuffer deletedItemsBuffer;
    private ComputeBuffer renderBuffer;
    private ComputeBuffer argsBuffer;
    private ComputeBuffer frustumPlanesBuffer;
    private ComputeBuffer counterBuffer;

    private Mesh quadMesh;
    private uint[] argsData = new uint[5] { 0, 0, 0, 0, 0 };
    private Camera mainCamera;

    // Mouse culling tracking
    private Vector3 previousMouseWorldPos;
    private bool hasValidPreviousMousePos = false;

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

    private int addItemsKernel;
    private int sphericalCullingKernel;
    private int capsuleCullingKernel;
    private int prepareRenderKernel;

void Start()
{
    if (!ValidateComponents()) return;

    // Kernel'leri cache'le
    addItemsKernel = instanceCompute.FindKernel("AddNewItems");
    sphericalCullingKernel = instanceCompute.FindKernel("SphericalCulling");
    capsuleCullingKernel = instanceCompute.FindKernel("CapsuleCulling");
    prepareRenderKernel = instanceCompute.FindKernel("PrepareRenderBuffer");

    Debug.Log($"Kernel Indices - Add: {addItemsKernel}, Sphere: {sphericalCullingKernel}, Capsule: {capsuleCullingKernel}, Prepare: {prepareRenderKernel}");

    // ✅ Tüm kernel'leri test et
    TestKernel("AddNewItems", addItemsKernel);
    TestKernel("SphericalCulling", sphericalCullingKernel);
    TestKernel("CapsuleCulling", capsuleCullingKernel);
    TestKernel("PrepareRenderBuffer", prepareRenderKernel);

    InitializeBuffers();
    InitializeGUIStyle();
    mainCamera = Camera.main;
    previousMouseWorldPos = GetMouseWorldPosition();
}


    void TestKernel(string kernelName, int kernelIndex)
    {
        if (kernelIndex == -1)
        {
            Debug.LogError($"❌ Kernel '{kernelName}' NOT FOUND!");
            return;
        }

        try
        {
            Debug.Log($"Testing kernel: {kernelName}");

            // MINIMAL setup - sadece zorunlu buffer'lar
            if (kernelName == "PrepareRenderBuffer")
            {
                Debug.Log("Setting up PrepareRenderBuffer buffers...");

                // SADECE mainBuffer ve renderBuffer set et, frustumPlanes'i geçici olarak atla
                instanceCompute.SetBuffer(kernelIndex, "mainBuffer", mainBuffer);
                instanceCompute.SetBuffer(kernelIndex, "renderBuffer", renderBuffer);

                // Basit değerler
                instanceCompute.SetInt("instanceCount", 1);
                instanceCompute.SetInt("enableFrustum", 0); // Frustum'u kapalı tut

                Debug.Log("Dispatching PrepareRenderBuffer...");
                instanceCompute.Dispatch(kernelIndex, 1, 1, 1);
                Debug.Log($"✅ Kernel '{kernelName}' TEST PASSED (without frustum)");

                // Şimdi frustum ile test et
                try
                {
                    instanceCompute.SetBuffer(kernelIndex, "frustumPlanes", frustumPlanesBuffer);
                    instanceCompute.SetInt("enableFrustum", 1);
                    instanceCompute.Dispatch(kernelIndex, 1, 1, 1);
                    Debug.Log($"✅ Kernel '{kernelName}' TEST PASSED (with frustum)");
                }
                catch (System.Exception e2)
                {
                    Debug.LogError($"❌ Kernel '{kernelName}' FAILED with frustum: {e2.Message}");
                }
            }
            else
            {
                // Diğer kernel'ler için basit test
                instanceCompute.SetInt("instanceCount", 1);
                instanceCompute.Dispatch(kernelIndex, 1, 1, 1);
                Debug.Log($"✅ Kernel '{kernelName}' TEST PASSED");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Kernel '{kernelName}' TEST FAILED: {e.Message}");

            // Daha detaylı hata bilgisi
            if (e.Message.Contains("frustumPlanes"))
            {
                Debug.LogError("FRUSTUM PLANES BUFFER BINDING ERROR!");
            }
        }
    }

    void PrepareRenderBuffer()
    {
        // ✅ HER ZAMAN tüm buffer'ları set et
        instanceCompute.SetBuffer(prepareRenderKernel, "mainBuffer", mainBuffer);
        instanceCompute.SetBuffer(prepareRenderKernel, "renderBuffer", renderBuffer);
        instanceCompute.SetBuffer(prepareRenderKernel, "frustumPlanes", frustumPlanesBuffer); // ✅ HER ZAMAN SET ET!
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
            instanceCompute.SetInt("enableFrustum", 1);
        }
        else
        {
            // ✅ Buffer zaten set edildi, sadece flag'i kapat
            instanceCompute.SetInt("enableFrustum", 0);
        }

        // ✅ Safe dispatch
        if (prepareRenderKernel != -1)
        {
            int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
            instanceCompute.Dispatch(prepareRenderKernel, threadGroups, 1, 1);
        }
        else
        {
            Debug.LogError("PrepareRenderBuffer kernel is invalid!");
        }

        ComputeBuffer.CopyCount(renderBuffer, argsBuffer, sizeof(uint));
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
        counterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);

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

    Vector3 GetMouseWorldPosition()
    {
        // Check if the new Input System's Mouse is available
        if (Mouse.current == null)
        {
            // Fallback if the mouse device isn't initialized or available
            return previousMouseWorldPos;
        }

        // Use the New Input System to get the screen position
        Vector3 screenPosition = Mouse.current.position.ReadValue();

        // Create the ray from the camera
        Ray ray = mainCamera.ScreenPointToRay(screenPosition);

        // Z=0 plane for 2D
        Plane plane = new Plane(Vector3.forward, Vector3.zero);

        if (plane.Raycast(ray, out float distance))
        {
            return ray.GetPoint(distance);
        }

        // Fallback to previous position if raycast fails (e.g., mouse is off-screen)
        return previousMouseWorldPos;
    }

    void Update()
    {
        renderBuffer.SetCounterValue(0);
        deletedItemsBuffer.SetCounterValue(0);

        // 1. Mouse culling (if enabled)
        if (useMouseCulling && Mouse.current.leftButton.isPressed) // Left mouse button held
        {
            EraseWithMouse();
        }

        // 2. Player-based erase instances
        EraseInstancesGPU();

        // 3. Read deleted items
        ReadDeletedItems();

        // 4. Add new instances
        AddNewInstances();

        // 5. Prepare render buffer
        PrepareRenderBuffer();

        // 6. Update statistics
        bool shouldUpdateStats = Time.time - lastStatsUpdate >= statsUpdateInterval;
        if (shouldUpdateStats)
        {
            UpdateStatistics();
            lastStatsUpdate = Time.time;
        }

        // 7. Clean up old gizmos
        CleanupOldGizmos();

        // 8. Render
        RenderInstances();
    }

    void EraseWithMouse()
    {
        Vector3 currentMouseWorldPos = GetMouseWorldPosition();

        if (hasValidPreviousMousePos && Vector3.Distance(previousMouseWorldPos, currentMouseWorldPos) > 0.01f)
        {
            // Use capsule culling for smooth mouse trail
            instanceCompute.SetBuffer(capsuleCullingKernel, "mainBuffer", mainBuffer);
            instanceCompute.SetBuffer(capsuleCullingKernel, "deletedItemsBuffer", deletedItemsBuffer);
            instanceCompute.SetVector("mouseStartPos", previousMouseWorldPos);
            instanceCompute.SetVector("mouseEndPos", currentMouseWorldPos);
            instanceCompute.SetFloat("lineRadius", mouseCullingRadius);
            instanceCompute.SetInt("instanceCount", maxInstances);

            int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
            instanceCompute.Dispatch(capsuleCullingKernel, threadGroups, 1, 1);
        }

        previousMouseWorldPos = currentMouseWorldPos;
        hasValidPreviousMousePos = true;
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

        instanceCompute.SetBuffer(addItemsKernel, "mainBuffer", mainBuffer);
        instanceCompute.SetBuffer(addItemsKernel, "newItemsBuffer", newItemsBuffer);
        instanceCompute.SetBuffer(addItemsKernel, "counterBuffer", counterBuffer);
        instanceCompute.SetInt("newItemsCount", toAdd);
        instanceCompute.SetInt("maxInstances", maxInstances);

        int threadGroups = Mathf.CeilToInt(toAdd / 64.0f);
        instanceCompute.Dispatch(addItemsKernel, threadGroups, 1, 1);
    }

    void EraseInstancesGPU()
    {
        instanceCompute.SetBuffer(sphericalCullingKernel, "mainBuffer", mainBuffer);
        instanceCompute.SetBuffer(sphericalCullingKernel, "deletedItemsBuffer", deletedItemsBuffer);
        instanceCompute.SetVector("playerPos", Player.transform.position);
        instanceCompute.SetFloat("radius", eraseRadius);
        instanceCompute.SetInt("instanceCount", maxInstances);

        int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
        instanceCompute.Dispatch(sphericalCullingKernel, threadGroups, 1, 1);
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

    // void PrepareRenderBuffer()
    // {
    //     int kernel = instanceCompute.FindKernel("PrepareRenderBuffer");
    //     instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
    //     instanceCompute.SetBuffer(kernel, "renderBuffer", renderBuffer);
    //     instanceCompute.SetInt("instanceCount", maxInstances);

    //     if (enableFrustumCulling && mainCamera != null)
    //     {
    //         Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
    //         Vector4[] planeData = new Vector4[6];
    //         for (int i = 0; i < 6; i++)
    //         {
    //             planeData[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
    //         }
    //         frustumPlanesBuffer.SetData(planeData);
    //         instanceCompute.SetBuffer(kernel, "frustumPlanes", frustumPlanesBuffer);
    //         instanceCompute.SetInt("enableFrustum", 1);
    //     }
    //     else
    //     {
    //         instanceCompute.SetInt("enableFrustum", 0);
    //     }

    //     int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
    //     instanceCompute.Dispatch(kernel, threadGroups, 1, 1);

    //     ComputeBuffer.CopyCount(renderBuffer, argsBuffer, sizeof(uint));
    // }

    void UpdateStatistics()
    {
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
        ComputeBuffer.CopyCount(renderBuffer, countBuffer, 0);

        uint[] countArray = new uint[1];
        countBuffer.GetData(countArray);
        activeCount = (int)countArray[0];
        countBuffer.Release();

        deletedItemsCount = deletedPositions.Count;
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
        GUI.Box(new Rect(10, 10, 350, 200), "");

        int yPos = 20;
        int lineHeight = 35;

        GUI.Label(new Rect(20, yPos, 400, 30), $"Total Capacity: {totalInstances}", guiStyle);
        yPos += lineHeight;

        GUIStyle activeStyle = new GUIStyle(guiStyle);
        activeStyle.normal.textColor = Color.green;
        GUI.Label(new Rect(20, yPos, 400, 30), $"Active Items: {activeCount}", activeStyle);
        yPos += lineHeight;

        GUIStyle emptyStyle = new GUIStyle(guiStyle);
        emptyStyle.normal.textColor = Color.cyan;
        GUI.Label(new Rect(20, yPos, 400, 30), $"Empty Slots: {emptySlotCount}", emptyStyle);
        yPos += lineHeight;

        GUIStyle deletedStyle = new GUIStyle(guiStyle);
        deletedStyle.normal.textColor = Color.red;
        GUI.Label(new Rect(20, yPos, 400, 30), $"Deleted Items: {deletedItemsCount}", deletedStyle);
        yPos += lineHeight;


        GUI.Label(new Rect(20, yPos, 400, 30), $"Deleted Items: {deletedItemsCount}", deletedStyle);
        yPos += lineHeight;

        // Mouse culling status - FIXED
        if (useMouseCulling)
        {
            GUIStyle mouseStyle = new GUIStyle(guiStyle);
            // Check left mouse button state using the New Input System
            bool isMouseHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;

            mouseStyle.normal.textColor = isMouseHeld ? Color.yellow : Color.gray;
            GUI.Label(new Rect(20, yPos, 400, 30), $"Mouse Erase: {(isMouseHeld ? "ACTIVE" : "Ready")}", mouseStyle);
        }



    }

    void OnDrawGizmos()
    {

        // Draw erase radius around player
        if (Player != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(Player.transform.position, eraseRadius);
        }

        // Draw mouse cursor position and radius - FIXED/UNCOMMENTED
        // Check left mouse button state using the New Input System
        bool isMouseHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;

        if (useMouseCulling && isMouseHeld && mainCamera != null)
        {
            Vector3 mousePos = GetMouseWorldPosition();
            Gizmos.color = new Color(1f, 0f, 1f, 0.5f); // Magenta
            Gizmos.DrawWireSphere(mousePos, mouseCullingRadius);

            // Draw line between previous and current position
            if (hasValidPreviousMousePos)
            {
                Gizmos.color = new Color(1f, 0f, 1f, 0.8f);
                Gizmos.DrawLine(previousMouseWorldPos, mousePos);
            }
        }


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

            Gizmos.DrawSphere(item.position, gizmoDotSize);

            if (mainCamera == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.matrix = mainCamera.transform.localToWorldMatrix;
            Gizmos.DrawFrustum(
                Vector3.zero,
                mainCamera.fieldOfView,
                mainCamera.farClipPlane,
                mainCamera.nearClipPlane,
                mainCamera.aspect
            );
        }

        // Draw erase radius around player
        if (Player != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(Player.transform.position, eraseRadius);
        }

        Gizmos.color = Color.green;

        Vector3 p1 = new Vector3(minPosition.x, minPosition.y, 0);
        Vector3 p2 = new Vector3(maxPosition.x, minPosition.y, 0);
        Vector3 p3 = new Vector3(maxPosition.x, maxPosition.y, 0);
        Vector3 p4 = new Vector3(minPosition.x, maxPosition.y, 0);

        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);
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
//     public float gizmoDotSize = 0.3f;
//     public float gizmoDisplayDuration = 1f;
//     public bool enableFrustumCulling = true;
//     public float statsUpdateInterval = 0.1f; // Update stats more frequently

//     // Buffers - Even simpler now!
//     private ComputeBuffer mainBuffer;
//     private ComputeBuffer newItemsBuffer;
//     private ComputeBuffer deletedItemsBuffer;
//     private ComputeBuffer renderBuffer;
//     private ComputeBuffer argsBuffer;
//     private ComputeBuffer frustumPlanesBuffer;
//     private ComputeBuffer counterBuffer; // GPU-side atomic counter

//     private Mesh quadMesh;
//     private uint[] argsData = new uint[5] { 0, 0, 0, 0, 0 };
//     private Camera mainCamera;

//     // Debug tracking
//     private struct DeletedItemDebug
//     {
//         public Vector3 position;
//         public float timeDeleted;
//     }

//     private List<DeletedItemDebug> deletedPositions = new List<DeletedItemDebug>();
//     private int activeCount = 0;
//     private int emptySlotCount = 0;
//     private int totalInstances = 0;
//     private int deletedItemsCount = 0;
//     private float lastStatsUpdate = 0f;

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
//         InitializeGUIStyle();
//         mainCamera = Camera.main;
//     }

//     void InitializeGUIStyle()
//     {
//         guiStyle = new GUIStyle();
//         guiStyle.fontSize = 24;
//         guiStyle.fontStyle = FontStyle.Bold;
//         guiStyle.normal.textColor = Color.white;
//         guiStyle.alignment = TextAnchor.UpperLeft;
//         guiStyle.normal.background = MakeTex(2, 2, new Color(0, 0, 0, 0.7f));
//         guiStyle.padding = new RectOffset(10, 10, 5, 5);
//     }

//     Texture2D MakeTex(int width, int height, Color col)
//     {
//         Color[] pix = new Color[width * height];
//         for (int i = 0; i < pix.Length; i++)
//             pix[i] = col;
//         Texture2D result = new Texture2D(width, height);
//         result.SetPixels(pix);
//         result.Apply();
//         return result;
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
//         newItemsBuffer = new ComputeBuffer(maxInstances, stride);
//         deletedItemsBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
//         renderBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
//         argsBuffer = new ComputeBuffer(1, argsData.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
//         frustumPlanesBuffer = new ComputeBuffer(6, sizeof(float) * 4);
//         counterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw); // Atomic counter

//         // Initialize all slots as inactive
//         ItemData[] initialData = new ItemData[maxInstances];
//         for (int i = 0; i < maxInstances; i++)
//         {
//             initialData[i].active = 0;
//         }
//         mainBuffer.SetData(initialData);

//         // Initialize counter to 0
//         counterBuffer.SetData(new uint[] { 0 });

//         argsData[0] = quadMesh.GetIndexCount(0);
//         argsBuffer.SetData(argsData);
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
//         renderBuffer.SetCounterValue(0);
//         deletedItemsBuffer.SetCounterValue(0);
        

//         // 1. Erase instances
//         EraseInstancesGPU();

//         // 2. Read deleted items
//         ReadDeletedItems();
        

//         // 3. Add new instances (simple linear allocation)
//         AddNewInstances();

//         // 4. Prepare render buffer
//         PrepareRenderBuffer();

//         // 5. Update statistics
//         bool shouldUpdateStats = Time.time - lastStatsUpdate >= statsUpdateInterval;
//         if (shouldUpdateStats)
//         {
//             UpdateStatistics();
//             lastStatsUpdate = Time.time;
//         }

//         // 6. Clean up old gizmos
//         CleanupOldGizmos();

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

//         // GPU finds free slots automatically using atomic counter!
//         int kernel = instanceCompute.FindKernel("AddNewItems");
//         instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
//         instanceCompute.SetBuffer(kernel, "newItemsBuffer", newItemsBuffer);
//         instanceCompute.SetBuffer(kernel, "counterBuffer", counterBuffer);
//         instanceCompute.SetInt("newItemsCount", toAdd);
//         instanceCompute.SetInt("maxInstances", maxInstances);

//         int threadGroups = Mathf.CeilToInt(toAdd / 64.0f);
//         instanceCompute.Dispatch(kernel, threadGroups, 1, 1);
//     }

//     void EraseInstancesGPU()
//     {
//         int kernel = instanceCompute.FindKernel("SphericalCulling");
//         instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
//         instanceCompute.SetBuffer(kernel, "deletedItemsBuffer", deletedItemsBuffer);
//         instanceCompute.SetVector("playerPos", Player.transform.position);
//         instanceCompute.SetFloat("radius", eraseRadius);
//         instanceCompute.SetInt("instanceCount", maxInstances);

//         int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
//         instanceCompute.Dispatch(kernel, threadGroups, 1, 1);
//     }

//     void ReadDeletedItems()
//     {
//         ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
//         ComputeBuffer.CopyCount(deletedItemsBuffer, countBuffer, 0);

//         uint[] countArray = new uint[1];
//         countBuffer.GetData(countArray);
//         int deletedCount = (int)countArray[0];
//         countBuffer.Release();

//         if (deletedCount > 0)
//         {
//             int safeCount = Mathf.Min(deletedCount, 500);

//             ItemData[] deletedData = new ItemData[safeCount];
//             deletedItemsBuffer.GetData(deletedData, 0, 0, safeCount);

//             float currentTime = Time.time;
//             for (int i = 0; i < safeCount; i++)
//             {
//                 deletedPositions.Add(new DeletedItemDebug
//                 {
//                     position = deletedData[i].position,
//                     timeDeleted = currentTime
//                 });
//             }
//         }
//     }

//     void CleanupOldGizmos()
//     {
//         float currentTime = Time.time;
//         deletedPositions.RemoveAll(item => currentTime - item.timeDeleted > gizmoDisplayDuration);
//     }

//     void PrepareRenderBuffer()
//     {
//         int kernel = instanceCompute.FindKernel("PrepareRenderBuffer");
//         instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
//         instanceCompute.SetBuffer(kernel, "renderBuffer", renderBuffer);
//         instanceCompute.SetInt("instanceCount", maxInstances);

//         if (enableFrustumCulling && mainCamera != null)
//         {
//             Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
//             Vector4[] planeData = new Vector4[6];
//             for (int i = 0; i < 6; i++)
//             {
//                 planeData[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
//             }
//             frustumPlanesBuffer.SetData(planeData);
//             instanceCompute.SetBuffer(kernel, "frustumPlanes", frustumPlanesBuffer);
//             instanceCompute.SetInt("enableFrustum", 1);
//         }
//         else
//         {
//             instanceCompute.SetInt("enableFrustum", 0);
//         }

//         int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
//         instanceCompute.Dispatch(kernel, threadGroups, 1, 1);

//         ComputeBuffer.CopyCount(renderBuffer, argsBuffer, sizeof(uint));
//     }

//     void UpdateStatistics()
//     {
//         // Get active count
//         ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
//         ComputeBuffer.CopyCount(renderBuffer, countBuffer, 0);

//         uint[] countArray = new uint[1];
//         countBuffer.GetData(countArray);
//         activeCount = (int)countArray[0];
//         countBuffer.Release();

//         // Get deleted items count from gizmo list (currently visible)
//         deletedItemsCount = deletedPositions.Count;

//         // Calculate empty slots
//         totalInstances = maxInstances;
//         emptySlotCount = totalInstances - activeCount;
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
//         GUI.Box(new Rect(10, 10, 350, 160), "");

//         int yPos = 20;
//         int lineHeight = 35;

//         GUI.Label(new Rect(20, yPos, 400, 30), $"Total Capacity: {totalInstances}", guiStyle);
//         yPos += lineHeight;

//         GUIStyle activeStyle = new GUIStyle(guiStyle);
//         activeStyle.normal.textColor = Color.green;
//         GUI.Label(new Rect(20, yPos, 400, 30), $"Active Items: {activeCount}", activeStyle);
//         yPos += lineHeight;

//         GUIStyle emptyStyle = new GUIStyle(guiStyle);
//         emptyStyle.normal.textColor = Color.cyan;
//         GUI.Label(new Rect(20, yPos, 400, 30), $"Empty Slots: {emptySlotCount}", emptyStyle);
//         yPos += lineHeight;

//         GUIStyle deletedStyle = new GUIStyle(guiStyle);
//         deletedStyle.normal.textColor = Color.red;
//         GUI.Label(new Rect(20, yPos, 400, 30), $"Deleted Items: {deletedItemsCount}", deletedStyle);
//     }

//     void OnDrawGizmos()
//     {
//         if (!showDeletedGizmos || deletedPositions == null) return;

//         float currentTime = Time.time;

//         // Draw deleted items as solid dots with fade
//         foreach (var item in deletedPositions)
//         {
//             float age = currentTime - item.timeDeleted;
//             float fadeAlpha = 1f - (age / gizmoDisplayDuration);

//             Color gizmoColor = deletedGizmoColor;
//             gizmoColor.a = fadeAlpha;
//             Gizmos.color = gizmoColor;

//             // Draw as solid sphere (dot)
//             Gizmos.DrawSphere(item.position, gizmoDotSize);
       
//             if (mainCamera == null) return;

//             Gizmos.color = Color.yellow;
//             Gizmos.matrix = mainCamera.transform.localToWorldMatrix;
//             Gizmos.DrawFrustum(
//                 Vector3.zero,
//                 mainCamera.fieldOfView,
//                 mainCamera.farClipPlane,
//                 mainCamera.nearClipPlane,
//                 mainCamera.aspect
//             );
//         }


//         // Draw erase radius around player
//         if (Player != null)
//         {
//             Gizmos.color = new Color(1f, 1f, 0f, 0.3f); // Semi-transparent yellow
//             Gizmos.DrawWireSphere(Player.transform.position, eraseRadius);
//         }
  
//         Gizmos.color = Color.green;

//         Vector3 p1 = new Vector3(minPosition.x, minPosition.y, 0);
//         Vector3 p2 = new Vector3(maxPosition.x, minPosition.y, 0);
//         Vector3 p3 = new Vector3(maxPosition.x, maxPosition.y, 0);
//         Vector3 p4 = new Vector3(minPosition.x, maxPosition.y, 0);

//         Gizmos.DrawLine(p1, p2);
//         Gizmos.DrawLine(p2, p3);
//         Gizmos.DrawLine(p3, p4);
//         Gizmos.DrawLine(p4, p1);
//     }

//     void OnDisable()
//     {
//         mainBuffer?.Release();
//         newItemsBuffer?.Release();
//         deletedItemsBuffer?.Release();
//         renderBuffer?.Release();
//         argsBuffer?.Release();
//         frustumPlanesBuffer?.Release();
//         counterBuffer?.Release();
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
