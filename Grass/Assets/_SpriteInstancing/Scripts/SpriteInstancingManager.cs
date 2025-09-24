
using System.Collections.Generic;
using UnityEngine;

public class SpriteComputeInstancer : MonoBehaviour
{
    [Header("Source SpriteRenderer")]
    public SpriteRenderer sourceSprite;

    [Header("Instances Settings")]
    public float scaleMultiplier = 1f;
    [Range(100,100000)]
    public int maxInstances = 1000; // buffer boyutu

    [Header("Inspector Positions")]
    [SerializeField] private List<Vector3> positions = new List<Vector3>();

    Mesh quadMesh;
    Material instancingMaterial;

    ComputeBuffer instanceBuffer;

    struct InstanceData
    {
        public Vector3 position;
        public float scale;
    }

    bool bufferInitialized = false;

    [ContextMenu("Add Test Position")]
    void AddTestPosition()
    {
        if (positions.Count >= maxInstances) return;

        Vector3 pos = new Vector3(Random.Range(-10f,10f), 0f, 0f);
        positions.Add(pos);
        UpdateBuffer();
    }

    void UpdateBuffer()
    {
        if (!bufferInitialized) return;

        InstanceData[] dataArray = new InstanceData[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            dataArray[i] = new InstanceData { position = positions[i], scale = 1f };
        }

        instanceBuffer.SetData(dataArray, 0, 0, positions.Count);
    }

    void Start()
    {
        if (sourceSprite == null || sourceSprite.sprite == null) return;

        if (quadMesh == null)
            quadMesh = CreateQuad();

        if (instancingMaterial == null)
        {
            Shader shader = Shader.Find("Custom/UnlitInstancedCompute");
            instancingMaterial = new Material(shader);
            instancingMaterial.mainTexture = sourceSprite.sprite.texture;
            instancingMaterial.enableInstancing = true;
        }

        sourceSprite.enabled = false;

        // 1️⃣ Başta buffer oluştur
        instanceBuffer = new ComputeBuffer(maxInstances, sizeof(float) * 4);
        bufferInitialized = true;

        // 2️⃣ İlk veri seti (boş olabilir)
        UpdateBuffer();
    }

    void Update()
    {
        if (!bufferInitialized || positions.Count == 0) return;

        instancingMaterial.SetBuffer("_InstanceDataBuffer", instanceBuffer);

        Graphics.DrawMeshInstancedProcedural(
            quadMesh, 0, instancingMaterial,
            new Bounds(Vector3.zero, Vector3.one * 100f), positions.Count
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
        Gizmos.color = Color.red;
        foreach (var pos in positions)
            Gizmos.DrawSphere(pos,0.1f);
    }
}
