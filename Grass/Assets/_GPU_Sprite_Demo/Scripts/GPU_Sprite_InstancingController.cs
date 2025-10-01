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

    // Buffers
    private ComputeBuffer mainBuffer;
    private ComputeBuffer freeSlotsBuffer;
    private ComputeBuffer newItemsBuffer;
    private ComputeBuffer deletedItemsBuffer;
    private ComputeBuffer renderBuffer;
    private ComputeBuffer argsBuffer;
// Add this field
private ComputeBuffer freeSlotsAppendBuffer;


    private Mesh quadMesh;
    private uint[] argsData = new uint[5] { 0, 0, 0, 0, 0 };

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
        freeSlotsBuffer = new ComputeBuffer(maxInstances, sizeof(uint), ComputeBufferType.Append);
        newItemsBuffer = new ComputeBuffer(maxInstances, stride);
        deletedItemsBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
        renderBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
        argsBuffer = new ComputeBuffer(1, argsData.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        // In InitializeBuffers():
        // freeSlotsBuffer = new ComputeBuffer(maxInstances, sizeof(uint), ComputeBufferType.Consume);
        freeSlotsAppendBuffer = new ComputeBuffer(maxInstances, sizeof(uint), ComputeBufferType.Append);


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
        // Başlangıçta tüm slotlar boş
        uint[] freeSlots = new uint[maxInstances];
        for (uint i = 0; i < maxInstances; i++)
        {
            freeSlots[i] = i;
        }
        freeSlotsBuffer.SetData(freeSlots);
        freeSlotsBuffer.SetCounterValue((uint)maxInstances);
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
        // Buffer'ları resetle
        renderBuffer.SetCounterValue(0);
        deletedItemsBuffer.SetCounterValue(0);

        // 1. Silme işlemi
        EraseInstancesGPU();

        // 2. Yeni öğe ekle
        AddNewInstances();

        // 3. Aktifleri render buffer'a kopyala
        PrepareRenderBuffer();

        // 4. Render
        RenderInstances();

        // After each frame, copy append buffer back to consume buffer
        // Add this at the end of Update() or beginning of next frame:
        ComputeBuffer.CopyCount(freeSlotsAppendBuffer, freeSlotsBuffer, 0);

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
        instanceCompute.SetBuffer(kernel, "freeSlotsBuffer", freeSlotsBuffer);
        instanceCompute.SetBuffer(kernel, "deletedItemsBuffer", deletedItemsBuffer);
        instanceCompute.SetVector("playerPos", Player.transform.position);
        instanceCompute.SetFloat("radius", eraseRadius);
        instanceCompute.SetInt("instanceCount", maxInstances);
// In EraseInstancesGPU():
instanceCompute.SetBuffer(kernel, "freeSlotsAppendBuffer", freeSlotsAppendBuffer);


        int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
        instanceCompute.Dispatch(kernel, threadGroups, 1, 1);
    }

    void PrepareRenderBuffer()
    {
        int kernel = instanceCompute.FindKernel("PrepareRenderBuffer");
        instanceCompute.SetBuffer(kernel, "mainBuffer", mainBuffer);
        instanceCompute.SetBuffer(kernel, "renderBuffer", renderBuffer);
        instanceCompute.SetInt("instanceCount", maxInstances);

        int threadGroups = Mathf.CeilToInt(maxInstances / 64.0f);
        instanceCompute.Dispatch(kernel, threadGroups, 1, 1);

        // RenderBuffer'daki count'ı argsBuffer'a yaz
        ComputeBuffer.CopyCount(renderBuffer, argsBuffer, sizeof(uint));
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

    void OnDisable()
    {
        mainBuffer?.Release();
        freeSlotsBuffer?.Release();
        newItemsBuffer?.Release();
        deletedItemsBuffer?.Release();
        renderBuffer?.Release();
        argsBuffer?.Release();
        // In OnDisable():
freeSlotsAppendBuffer?.Release();

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

