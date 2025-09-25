using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class TimeLineSpriteComputeInstancer : MonoBehaviour
{
    [Header("Source SpriteRenderer")]
    public SpriteRenderer sourceSprite;

    [Header("Instances Settings")]
    public int startCount = 0;
    public int maxInstances = 10000;
    public int incrementPerFrame = 10;
    public float scaleMultiplier = 1f;

    [Header("Position Limits")]
    public Vector3 minPosition = new Vector3(-100f, 0, -100f);
    public Vector3 maxPosition = new Vector3(100f, 0, 100f);

    [Header("Debug / Inspector")]
    public List<Vector3> positions = new List<Vector3>();

    Mesh quadMesh;
    public Material instancingMaterial;
    ComputeBuffer instanceBuffer;

    struct InstanceData
    {
        public Vector3 position;
        public int active; // 1: etkileşimli / 0: yok edilecek
    }


    bool bufferInitialized = false;
    public ComputeShader filterShader;

    void FilterInstances(Vector3 playerPos, float radius)
    {
        int kernel = filterShader.FindKernel("CSMain");
        filterShader.SetBuffer(kernel, "instanceBuffer", instanceBuffer);
        filterShader.SetVector("playerPos", playerPos);
        filterShader.SetFloat("radius", radius);

        int threadGroups = Mathf.CeilToInt(positions.Count / 256f);
        filterShader.Dispatch(kernel, threadGroups, 1, 1);

        // CPU'ya oku
        AsyncGPUReadback.Request(instanceBuffer, req =>
        {
            if(req.hasError) return;
            InstanceData[] data = req.GetData<InstanceData>().ToArray();

            // active==0 olanları yok et
            for(int i = 0; i < data.Length; i++)
            {
                if(data[i].active == 0)
                    positions[i] = Vector3.negativeInfinity; // örnek yok etme
            }
            UpdateBuffer(); // buffer tekrar GPU'ya gönder
        });
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

        // Başlangıç pozisyonları
        for (int i = 0; i < startCount; i++)
            AddRandomPosition();

        // Buffer oluştur
        instanceBuffer = new ComputeBuffer(positions.Count, sizeof(float) * 4);
        bufferInitialized = true;
        UpdateBuffer();
    }

    void Update()
    {
        if (!bufferInitialized) return;

        // Increment ekle
        for (int i = 0; i < incrementPerFrame && positions.Count < maxInstances; i++)
            AddRandomPosition();

        UpdateBuffer();

        // Çizim
        instancingMaterial.SetBuffer("_InstanceDataBuffer", instanceBuffer);
        Graphics.DrawMeshInstancedProcedural(
            quadMesh, 0, instancingMaterial,
            new Bounds(Vector3.zero, Vector3.one * 100f),
            positions.Count
        );
    }

    void AddRandomPosition()
    {
        Vector3 pos = new Vector3(
            Random.Range(minPosition.x, maxPosition.x),
            Random.Range(minPosition.y, maxPosition.y),
            Random.Range(minPosition.z, maxPosition.z)
        );
        positions.Add(pos);
    }

    void UpdateBuffer()
    {
        if (instanceBuffer != null)
            instanceBuffer.Release();

        instanceBuffer = new ComputeBuffer(positions.Count, sizeof(float) * 4);

        InstanceData[] dataArray = new InstanceData[positions.Count];
        for (int i = 0; i < positions.Count; i++)
            dataArray[i] = new InstanceData { position = positions[i], active=0 };

        instanceBuffer.SetData(dataArray);
    }
void OnDrawGizmosSelected()
{
    if (positions == null) return;

    // Alanın sınırlarını çiz
    Vector3 areaCenter = (minPosition + maxPosition) / 2f;
    Vector3 areaSize = maxPosition - minPosition;
    Gizmos.color = Color.blue;
    Gizmos.DrawWireCube(areaCenter, areaSize);

    // Spawn count ve alan bilgisi Scene View'da yazsın
    #if UNITY_EDITOR
    GUIStyle style = new GUIStyle();
    style.normal.textColor = Color.white;
    style.fontSize = 12;
    UnityEditor.Handles.Label(areaCenter + Vector3.up * (areaSize.y / 2f + 1f), 
                              $"Instances: {positions.Count}\nArea: {areaSize}", style);
    #endif
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
            Gizmos.DrawSphere(pos, 0.1f);
    }
}
