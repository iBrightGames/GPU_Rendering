using UnityEngine;
using UnityEngine.ProBuilder;

public class GrassGPUManager : MonoBehaviour
{
    public ComputeShader computeShader;
    public Material grassMaterial;
    public GameObject grassObject;
    public int grassCount = 1000;
    public Vector2 areaSize = new Vector2(100f,100f);

    [Header("Scale Range")]
    public float scaleMin = 0.8f;
    public float scaleMax = 1.2f;

    [Header("Gradient")]
    public Gradient grassGradient;

    Mesh grassMesh;
    ComputeBuffer bufferPosScale;
    ComputeBuffer bufferColor;
    ComputeBuffer argsBuffer;
    Texture2D gradientTex;

    void Start()
    {
        ProBuilderMesh pb = grassObject.GetComponent<ProBuilderMesh>();
        if(pb==null) { Debug.LogError("ProBuilderMesh yok!"); return; }

        MeshFilter mf = pb.GetComponent<MeshFilter>();
        if(mf==null || mf.sharedMesh==null) { Debug.LogError("Mesh yok!"); return; }
        grassMesh = mf.sharedMesh;

        bufferPosScale = new ComputeBuffer(grassCount, sizeof(float)*4);
        bufferColor    = new ComputeBuffer(grassCount, sizeof(float)*4);
        argsBuffer     = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);

        uint[] args = new uint[5] { grassMesh.GetIndexCount(0), (uint)grassCount, 0, 0, 0 };
        argsBuffer.SetData(args);

        // Gradient’i Texture2D olarak oluştur
        gradientTex = new Texture2D(256,1,TextureFormat.RGBA32,false);
        for(int i=0;i<256;i++)
            gradientTex.SetPixel(i,0, grassGradient.Evaluate(i/255f));
        gradientTex.Apply();

        int kernel = computeShader.FindKernel("CSMain");
        computeShader.SetInt("grassCount", grassCount);
        computeShader.SetVector("areaSize", areaSize);
        computeShader.SetFloat("scaleMin", scaleMin);
        computeShader.SetFloat("scaleMax", scaleMax);
        computeShader.SetBuffer(kernel, "PosScaleBuffer", bufferPosScale);
        computeShader.SetBuffer(kernel, "ColorBuffer", bufferColor);
        RenderTexture gradientRT = new RenderTexture(256, 1, 0, RenderTextureFormat.ARGB32);
gradientRT.enableRandomWrite = true;
gradientRT.Create();

Texture2D gradTexCPU = new Texture2D(256, 1, TextureFormat.RGBA32, false);
for (int i = 0; i < 256; i++)
    gradTexCPU.SetPixel(i, 0, grassGradient.Evaluate(i / 255f));
gradTexCPU.Apply();

Graphics.Blit(gradTexCPU, gradientRT);

computeShader.SetTexture(kernel, "gradientTex", gradientRT);


        int threadGroups = Mathf.CeilToInt(grassCount / 256f);
        computeShader.Dispatch(kernel, threadGroups, 1, 1);
    }

    void Update()
    {
        grassMaterial.SetBuffer("_InstanceData_PosScale", bufferPosScale);
        grassMaterial.SetBuffer("_InstanceData_Color", bufferColor);

        Graphics.DrawMeshInstancedIndirect(
            grassMesh, 0, grassMaterial,
            new Bounds(Vector3.zero, new Vector3(50,10,50)),
            argsBuffer
        );
    }

    void OnDestroy()
    {
        bufferPosScale?.Release();
        bufferColor?.Release();
        argsBuffer?.Release();
    }
}
