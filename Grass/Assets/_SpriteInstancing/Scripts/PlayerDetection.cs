using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class PlayerDetection : MonoBehaviour
{
    public GameObject Player;
    public SpriteRenderer sourceSprite;
    public int startCount = 0;
    public int maxInstances = 10000;
    public int incrementPerFrame = 10;
    public float scaleMultiplier = 1f;
    public Vector3 minPosition = new Vector3(-100f, 0, -100f);
    public Vector3 maxPosition = new Vector3(100f, 0, 100f);

    public ComputeShader filterShader;

    public List<Vector3> positions;

    Mesh quadMesh;
    Material instancingMaterial;
    ComputeBuffer originalBuffer;   // structured buffer for instance data
    ComputeBuffer filteredBuffer;   // append buffer for filtered results
    ComputeBuffer counterBuffer;    // small buffer to hold the append count (1 uint)

    struct InstanceData
    {
        public Vector3 position;
        public int active;
    }

    bool bufferInitialized = false;
    int stride;

    void Start()
    {
        if (sourceSprite == null || sourceSprite.sprite == null) return;
        if (Player == null) Debug.LogWarning("Player is null in PlayerDetection.");

        quadMesh = CreateQuad();

        Shader shader = Shader.Find("CustomUnlit/SingleSpriteCompute_GPUActive");
        instancingMaterial = new Material(shader);
        instancingMaterial.mainTexture = sourceSprite.sprite.texture;
        instancingMaterial.enableInstancing = true;

        sourceSprite.enabled = false;

        // initialise positions list with maxInstances entries
        positions = new List<Vector3>(new Vector3[maxInstances]);
        for (int i = 0; i < startCount; i++)
            AddRandomPosition(i);

        // stride: float3 (12 bytes) + int (4 bytes) = 16
        stride = sizeof(float) * 3 + sizeof(int);

        // original: regular structured buffer
        originalBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Default);

        // filteredBuffer will be an AppendStructuredBuffer on GPU; create as Append type
        filteredBuffer = new ComputeBuffer(maxInstances, stride, ComputeBufferType.Append);
        filteredBuffer.SetCounterValue(0);

        // counterBuffer to receive the number of appended elements (1 uint)
        counterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);

        bufferInitialized = true;
        UpdateBuffer();
    }

    void Update()
    {
        if (!bufferInitialized) return;

        // reset append buffer counter BEFORE dispatch
        filteredBuffer.SetCounterValue(0);

        Vector3 playerPos = Player != null ? Player.transform.position : Vector3.zero;
        FilterInstances(playerPos, 1f);

        // fill some empty slots each frame
        int countAdded = 0;
        for (int i = 0; i < positions.Count && countAdded < incrementPerFrame; i++)
        {
            if (positions[i] == Vector3.zero || positions[i] == Vector3.negativeInfinity)
            {
                AddRandomPosition(i);
                countAdded++;
            }
        }

        // If you modified positions on CPU, update original buffer so GPU has up-to-date positions.
        UpdateBuffer();

        instancingMaterial.SetBuffer("_InstanceDataBuffer", originalBuffer);

        Graphics.DrawMeshInstancedProcedural(
            quadMesh, 0, instancingMaterial,
            new Bounds(Vector3.zero, Vector3.one * 500f),
            positions.Count
        );
    }

    void FilterInstances(Vector3 playerPos, float radius)
    {
        int kernel = filterShader.FindKernel("CSMain");

        // set buffers and parameters
        filterShader.SetBuffer(kernel, "originalBuffer", originalBuffer);
        filterShader.SetBuffer(kernel, "filteredBuffer", filteredBuffer);
        filterShader.SetVector("playerPos", new Vector4(playerPos.x, playerPos.y, playerPos.z, 0f));
        filterShader.SetFloat("radius", radius);

        int threadGroups = Mathf.CeilToInt(positions.Count / 256f);
        filterShader.Dispatch(kernel, threadGroups, 1, 1);

        // copy the append count into counterBuffer (GPU -> small structured/raw buffer)
        ComputeBuffer.CopyCount(filteredBuffer, counterBuffer, 0);

        // read the count synchronously (small buffer, cheap)
        uint[] countData = new uint[1];
        counterBuffer.GetData(countData);
        int filteredCount = (int)countData[0];
        Debug.Log($"Filtered count (GPU reported): {filteredCount}");

        if (filteredCount > 0)
        {
            // Async readback only the valid portion of the buffer:
            // AsyncGPUReadback.Request(ComputeBuffer, sizeInBytes, callback)
            int byteSize = filteredCount * stride;
            AsyncGPUReadback.Request(filteredBuffer, byteSize, 0, req =>
            {
                if (req.hasError)
                {
                    Debug.LogError("GPU Readback error!");
                    return;
                }

                var data = req.GetData<InstanceData>();
                // data.Length will be filteredCount (because we requested only that many bytes)
                Debug.Log($"Async readback returned {data.Length} items");

            });
        }
    }

    void AddRandomPosition(int index)
    {
        positions[index] = new Vector3(
            Random.Range(minPosition.x, maxPosition.x),
            Random.Range(minPosition.y, maxPosition.y),
            Random.Range(minPosition.z, maxPosition.z)
        );
    }

    void UpdateBuffer()
    {
        if (originalBuffer == null) return;
        InstanceData[] dataArray = new InstanceData[positions.Count];
        for (int i = 0; i < positions.Count; i++)
            dataArray[i] = new InstanceData
            {
                position = positions[i],
                active = (positions[i] == Vector3.negativeInfinity) ? 0 : 1
            };
        originalBuffer.SetData(dataArray);
    }

    void OnDisable()
    {
        originalBuffer?.Release();
        filteredBuffer?.Release();
        counterBuffer?.Release();
        originalBuffer = filteredBuffer = null;
        bufferInitialized = false;
    }

    Mesh CreateQuad()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f,-0.5f,0),
            new Vector3(0.5f,-0.5f,0),
            new Vector3(0.5f,0.5f,0),
            new Vector3(-0.5f,0.5f,0)
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0,0),
            new Vector2(1,0),
            new Vector2(1,1),
            new Vector2(0,1)
        };
        mesh.triangles = new int[]{0,1,2,0,2,3};
        mesh.RecalculateNormals();
        return mesh;
    }
}
