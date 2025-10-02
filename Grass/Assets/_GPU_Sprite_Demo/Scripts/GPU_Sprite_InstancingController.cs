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
    public bool enableKernelTesting = false; // Gate testing behind flag

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

        // Validate compute shader
        if (instanceCompute == null)
        {
            Debug.LogError("Compute Shader is not assigned!");
            enabled = false;
            return;
        }

        // Cache kernels with validation
        try
        {
            addItemsKernel = instanceCompute.FindKernel("AddNewItems");
            sphericalCullingKernel = instanceCompute.FindKernel("SphericalCulling");
            capsuleCullingKernel = instanceCompute.FindKernel("CapsuleCulling");
            prepareRenderKernel = instanceCompute.FindKernel("PrepareRenderBuffer");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to find kernels: {e.Message}");
            enabled = false;
            return;
        }

        // Validate kernel indices
        if (addItemsKernel == -1 || sphericalCullingKernel == -1 ||
            capsuleCullingKernel == -1 || prepareRenderKernel == -1)
        {
            Debug.LogError($"Invalid kernel indices - Add: {addItemsKernel}, Sphere: {sphericalCullingKernel}, Capsule: {capsuleCullingKernel}, Prepare: {prepareRenderKernel}");
            Debug.LogError("Make sure your compute shader has all required kernels and is assigned correctly!");
            enabled = false;
            return;
        }

        Debug.Log($"✓ Kernel Indices - Add: {addItemsKernel}, Sphere: {sphericalCullingKernel}, Capsule: {capsuleCullingKernel}, Prepare: {prepareRenderKernel}");

        InitializeBuffers();

        // Test kernels only if enabled
        if (enableKernelTesting)
        {
            TestKernel("AddNewItems", addItemsKernel);
            TestKernel("SphericalCulling", sphericalCullingKernel);
            TestKernel("CapsuleCulling", capsuleCullingKernel);
            TestKernel("PrepareRenderBuffer", prepareRenderKernel);
        }

        InitializeGUIStyle();
        mainCamera = Camera.main;

        // Initialize mouse position safely
        if (Mouse.current != null)
        {
            previousMouseWorldPos = GetMouseWorldPosition();
            hasValidPreviousMousePos = true;
        }
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

            if (kernelName == "PrepareRenderBuffer")
            {
                instanceCompute.SetBuffer(kernelIndex, "mainBuffer", mainBuffer);
                instanceCompute.SetBuffer(kernelIndex, "renderBuffer", renderBuffer);
                instanceCompute.SetBuffer(kernelIndex, "frustumPlanes", frustumPlanesBuffer);
                instanceCompute.SetInt("instanceCount", 1);
                instanceCompute.SetInt("enableFrustum", 0);

                instanceCompute.Dispatch(kernelIndex, 1, 1, 1);
                Debug.Log($"✅ Kernel '{kernelName}' TEST PASSED");
            }
            else
            {
                instanceCompute.SetInt("instanceCount", 1);
                instanceCompute.Dispatch(kernelIndex, 1, 1, 1);
                Debug.Log($"✅ Kernel '{kernelName}' TEST PASSED");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Kernel '{kernelName}' TEST FAILED: {e.Message}");
        }
    }

    void PrepareRenderBuffer()
    {
        // Always set all buffers
        instanceCompute.SetBuffer(prepareRenderKernel, "mainBuffer", mainBuffer);
        instanceCompute.SetBuffer(prepareRenderKernel, "renderBuffer", renderBuffer);
        instanceCompute.SetBuffer(prepareRenderKernel, "frustumPlanes", frustumPlanesBuffer);
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
            instanceCompute.SetInt("enableFrustum", 0);
        }

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

        ItemData[] initialData = new ItemData[maxInstances];
        for (int i = 0; i < maxInstances; i++)
        {
            initialData[i].active = 0;
        }
        mainBuffer.SetData(initialData);

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
        if (Mouse.current == null)
        {
            return hasValidPreviousMousePos ? previousMouseWorldPos : Vector3.zero;
        }

        Vector3 screenPosition = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(screenPosition);
        Plane plane = new Plane(Vector3.forward, Vector3.zero);

        if (plane.Raycast(ray, out float distance))
        {
            return ray.GetPoint(distance);
        }

        return hasValidPreviousMousePos ? previousMouseWorldPos : Vector3.zero;
    }

    void Update()
    {
        renderBuffer.SetCounterValue(0);
        deletedItemsBuffer.SetCounterValue(0);

        // Mouse culling
        if (useMouseCulling && Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            EraseWithMouse();
        }

        // Player-based erase
        EraseInstancesGPU();

        // Read deleted items
        ReadDeletedItems();

        // Add new instances
        AddNewInstances();

        // Prepare render buffer
        PrepareRenderBuffer();

        // Update statistics
        if (Time.time - lastStatsUpdate >= statsUpdateInterval)
        {
            UpdateStatistics();
            lastStatsUpdate = Time.time;
        }

        // Clean up old gizmos
        CleanupOldGizmos();

        // Render
        RenderInstances();
    }

    void EraseWithMouse()
    {
        Vector3 currentMouseWorldPos = GetMouseWorldPosition();

        if (hasValidPreviousMousePos && Vector3.Distance(previousMouseWorldPos, currentMouseWorldPos) > 0.01f)
        {
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

        // Mouse culling status
        if (useMouseCulling)
        {
            GUIStyle mouseStyle = new GUIStyle(guiStyle);
            bool isMouseHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
            mouseStyle.normal.textColor = isMouseHeld ? Color.yellow : Color.gray;
            GUI.Label(new Rect(20, yPos, 400, 30), $"Mouse Erase: {(isMouseHeld ? "ACTIVE" : "Ready")}", mouseStyle);
        }
    }

    void OnDrawGizmos()
    {
        // Draw deleted items
        if (showDeletedGizmos && deletedPositions != null)
        {
            float currentTime = Time.time;

            foreach (var item in deletedPositions)
            {
                float age = currentTime - item.timeDeleted;
                float fadeAlpha = 1f - (age / gizmoDisplayDuration);

                Color gizmoColor = deletedGizmoColor;
                gizmoColor.a = fadeAlpha;
                Gizmos.color = gizmoColor;

                Gizmos.DrawSphere(item.position, gizmoDotSize);
            }
        }

        // Draw camera frustum
        if (mainCamera != null && enableFrustumCulling)
        {
            Gizmos.color = Color.yellow;
            Gizmos.matrix = mainCamera.transform.localToWorldMatrix;
            Gizmos.DrawFrustum(
                Vector3.zero,
                mainCamera.fieldOfView,
                mainCamera.farClipPlane,
                mainCamera.nearClipPlane,
                mainCamera.aspect
            );
            Gizmos.matrix = Matrix4x4.identity;
        }

        // Draw erase radius around player
        if (Player != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(Player.transform.position, eraseRadius);
        }

        // Draw mouse cursor and trail
        if (useMouseCulling && Mouse.current != null && Mouse.current.leftButton.isPressed && mainCamera != null)
        {
            Vector3 mousePos = GetMouseWorldPosition();
            Gizmos.color = new Color(1f, 0f, 1f, 0.5f);
            Gizmos.DrawWireSphere(mousePos, mouseCullingRadius);

            if (hasValidPreviousMousePos)
            {
                Gizmos.color = new Color(1f, 0f, 1f, 0.8f);
                Gizmos.DrawLine(previousMouseWorldPos, mousePos);
            }
        }

        // Draw spawn boundary
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
