using System.Collections.Generic;
using UnityEngine;

public class SimpleSpriteComputeInstancer : MonoBehaviour
{
    [Header("Source SpriteRenderer")]
    public SpriteRenderer sourceSprite;

    [Header("Instances Settings")]
    public int instanceCount = 10000;
    public float scaleMultiplier = 1f;

    [Header("Position Limits")]
    public Vector3 minPosition = new Vector3(-10f, -10f, 0f);
    public Vector3 maxPosition = new Vector3(10f, 10f, 0f);

    [Header("Debug / Inspector")]
    public List<Vector3> positions = new List<Vector3>();

    Mesh quadMesh;
    public Material instancingMaterial;
    ComputeBuffer instanceBuffer;

    struct InstanceData
    {
        public Vector3 position;
        public float scale;
    }

    bool bufferInitialized = false;

    [ContextMenu("Regenerate Positions")]
    public void GeneratePositions()
    {
        positions.Clear();
        for (int i = 0; i < instanceCount; i++)
        {
            Vector3 pos = new Vector3(
                Random.Range(minPosition.x, maxPosition.x),
                Random.Range(minPosition.y, maxPosition.y),
                Random.Range(minPosition.z, maxPosition.z)
            );
            positions.Add(pos);
        }
        UpdateBuffer();
    }

    void UpdateBuffer()
    {
        if (!bufferInitialized || positions.Count == 0) return;

        InstanceData[] dataArray = new InstanceData[positions.Count];
        for (int i = 0; i < positions.Count; i++)
            dataArray[i] = new InstanceData { position = positions[i], scale = scaleMultiplier };

        instanceBuffer.SetData(dataArray);
    }

    void Start()
    {
        if (sourceSprite == null || sourceSprite.sprite == null) return;

        if (quadMesh == null)
            quadMesh = CreateQuad();

        if (instancingMaterial == null)
        {
            Shader shader = Shader.Find("CustomUnlit/SingleSpriteCompute");
            instancingMaterial = new Material(shader);
            instancingMaterial.mainTexture = sourceSprite.sprite.texture;
            instancingMaterial.enableInstancing = true;
        }

        sourceSprite.enabled = false;

        // Buffer oluştur
        instanceBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 4);
        bufferInitialized = true;

        // Başlangıç verisi
        GeneratePositions();
    }

    void Update()
    {
        if (!bufferInitialized || positions.Count == 0) return;

        instancingMaterial.SetBuffer("_InstanceDataBuffer", instanceBuffer);

        Graphics.DrawMeshInstancedProcedural(
            quadMesh, 0, instancingMaterial,
            new Bounds(Vector3.zero, Vector3.one * 100f),
            positions.Count
        );
    }

    void OnDisable()
    {
        if (instanceBuffer != null)
        {
            instanceBuffer.Release();
            instanceBuffer = null;
            bufferInitialized = false;
        }
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
        return mesh;
    }

    void OnDrawGizmos()
    {
        if (positions == null) return;
        Gizmos.color = Color.red;
        foreach (var pos in positions)
            Gizmos.DrawSphere(pos,0.1f);
    }
}
