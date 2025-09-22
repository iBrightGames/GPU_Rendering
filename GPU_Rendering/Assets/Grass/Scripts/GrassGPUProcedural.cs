// // using UnityEngine;

// // public class GrassGPUProcedural : MonoBehaviour
// // {
// //     public ComputeShader computeShader;
// //     public Material grassMaterial;

// //     [Header("Grass Parameters")]
// //     public float width = 0.1f;
// //     public float height = 1f;
// //     public float positionNoise = 0.5f;
// //     public float scaleNoise = 0.3f;
// //     public Gradient colorGradient;

// //     [Header("Spawn Area")]
// //     public Vector2 areaSize = new Vector2(100, 100);
// //     public int grassCount = 1000;

// //     ComputeBuffer posScaleBuffer;
// //     ComputeBuffer colorBuffer;
// //     RenderTexture gradientRT;

// //     void Start()
// //     {
// //         posScaleBuffer = new ComputeBuffer(grassCount, sizeof(float) * 4);
// //         colorBuffer    = new ComputeBuffer(grassCount, sizeof(float) * 4);

// //         // Gradient -> RenderTexture
// //         gradientRT = new RenderTexture(256, 1, 0, RenderTextureFormat.ARGB32);
// //         gradientRT.enableRandomWrite = true; 
// //         gradientRT.Create();

// //         // CPU Gradient’i kopyala
// //         Texture2D gradTexCPU = new Texture2D(256, 1, TextureFormat.RGBA32, false);
// //         for (int i = 0; i < 256; i++)
// //             gradTexCPU.SetPixel(i, 0, colorGradient.Evaluate(i / 255f));
// //         gradTexCPU.Apply();

// //         Graphics.Blit(gradTexCPU, gradientRT);

// //         // Compute shader setup
// //         int kernel = computeShader.FindKernel("CSMain");
// //         computeShader.SetInt("grassCount", grassCount);
// //         computeShader.SetVector("areaSize", areaSize);
// //         computeShader.SetFloat("width", width);
// //         computeShader.SetFloat("height", height);
// //         computeShader.SetFloat("positionNoise", positionNoise);
// //         computeShader.SetFloat("scaleNoise", scaleNoise);
// //         computeShader.SetBuffer(kernel, "PosScaleBuffer", posScaleBuffer);
// //         computeShader.SetBuffer(kernel, "ColorBuffer", colorBuffer);
// //         computeShader.SetTexture(kernel, "gradientTex", gradientRT);

// //         int threadGroups = Mathf.CeilToInt(grassCount / 1024f);
// //         computeShader.Dispatch(kernel, threadGroups, 1, 1);

// //         // Material setup
// //         grassMaterial.SetBuffer("_InstanceData_PosScale", posScaleBuffer);
// //         grassMaterial.SetBuffer("_InstanceData_Color", colorBuffer);
// //     }

// //     void Update()
// //     {
// //         Graphics.DrawProcedural(
// //             grassMaterial,
// //             new Bounds(Vector3.zero, new Vector3(areaSize.x, height*2, areaSize.y)),
// //             MeshTopology.Triangles, 3, grassCount

// //         );
// //     }

// //     void OnDestroy()
// //     {
// //         posScaleBuffer?.Release();
// //         colorBuffer?.Release();
// //         if (gradientRT != null) gradientRT.Release();
// //     }
// // }

// using UnityEngine;
// using UnityEngine.Rendering;

// public class GrassGPUProcedural : MonoBehaviour
// {
//     [Header("References")]
//     public ComputeShader computeShader;
//     public Material grassMaterial;

//     [Header("Grass Parameters")]
//     public float width = 0.1f;
//     public float height = 1f;
//     [Range(0f, 1f)] public float positionNoise = 0.5f;
//     [Range(0f, 1f)] public float scaleNoise = 0.3f;
//     public Gradient colorGradient;

//     [Header("Spawn Area")]
//     public Vector2 areaSize = new Vector2(100, 100);
//     [Range(1, 1000)] public int grassCount = 1000;

//     [Header("Rendering")]
//     public ShadowCastingMode shadowMode = ShadowCastingMode.On;
//     public bool receiveShadows = true;

//     private ComputeBuffer posScaleBuffer;
//     private ComputeBuffer colorBuffer;
//     private RenderTexture gradientRT;
//     private bool isInitialized = false;
//     private int kernelHandle;
//     private uint threadGroupSize;

//     void Start()
//     {
//         InitializeGrass();
//     }

//     void InitializeGrass()
//     {
//         Cleanup();

//         // Validate inputs
//         grassCount = Mathf.Max(1, grassCount);

//         // Create buffers
//         posScaleBuffer = new ComputeBuffer(grassCount, sizeof(float) * 4);
//         colorBuffer = new ComputeBuffer(grassCount, sizeof(float) * 4);

//         // Create gradient texture
//         CreateGradientTexture();

//         // Setup compute shader
//         kernelHandle = computeShader.FindKernel("CSMain");

//         // Get thread group size
//         computeShader.GetKernelThreadGroupSizes(kernelHandle, out threadGroupSize, out _, out _);

//         // Set parameters
//         computeShader.SetInt("grassCount", grassCount);
//         computeShader.SetVector("areaSize", areaSize);
//         computeShader.SetFloat("width", width);
//         computeShader.SetFloat("height", height);
//         computeShader.SetFloat("positionNoise", positionNoise);
//         computeShader.SetFloat("scaleNoise", scaleNoise);
//         computeShader.SetBuffer(kernelHandle, "PosScaleBuffer", posScaleBuffer);
//         computeShader.SetBuffer(kernelHandle, "ColorBuffer", colorBuffer);
//         computeShader.SetTexture(kernelHandle, "gradientTex", gradientRT);

//         // Dispatch
//         int threadGroups = Mathf.CeilToInt((float)grassCount / threadGroupSize);
//         computeShader.Dispatch(kernelHandle, threadGroups, 1, 1);

//         // Setup material
//         grassMaterial.SetBuffer("_InstanceData_PosScale", posScaleBuffer);
//         grassMaterial.SetBuffer("_InstanceData_Color", colorBuffer);
//         grassMaterial.SetFloat("_Width", width);
//         grassMaterial.SetFloat("_Height", height);

//         isInitialized = true;
//     }

//     void CreateGradientTexture()
//     {
//         if (gradientRT != null) gradientRT.Release();

//         gradientRT = new RenderTexture(256, 1, 0, RenderTextureFormat.ARGB32)
//         {
//             enableRandomWrite = true,
//             filterMode = FilterMode.Bilinear,
//             wrapMode = TextureWrapMode.Clamp
//         };
//         gradientRT.Create();

//         Texture2D gradTexCPU = new Texture2D(256, 1, TextureFormat.RGBA32, false);
//         for (int i = 0; i < 256; i++)
//             gradTexCPU.SetPixel(i, 0, colorGradient.Evaluate(i / 255f));
//         gradTexCPU.Apply();

//         Graphics.Blit(gradTexCPU, gradientRT);
//         DestroyImmediate(gradTexCPU);
//     }

//     void Update()
//     {
//         if (!isInitialized) return;

//         Graphics.DrawProcedural(
//             grassMaterial,
//             new Bounds(transform.position, new Vector3(areaSize.x, height * 2, areaSize.y)),
//             MeshTopology.Triangles,
//             6, // Typically 2 triangles = 6 vertices for a quad
//             grassCount,
//             null,
//             null,
//             shadowMode,
//             receiveShadows,
//             gameObject.layer
//         );
//     }

//     public void RegenerateGrass()
//     {
//         InitializeGrass();
//     }

//     void Cleanup()
//     {
//         posScaleBuffer?.Release();
//         colorBuffer?.Release();
//         if (gradientRT != null) gradientRT.Release();
//         isInitialized = false;
//     }

//     void OnDestroy()
//     {
//         Cleanup();
//     }

//     void OnValidate()
//     {
//         // Only regenerate if already initialized and parameters change
//         if (isInitialized && Application.isPlaying)
//         {
//             RegenerateGrass();
//         }
//     }

//     void OnDrawGizmosSelected()
//     {
//         // Visualize spawn area in scene view
//         Gizmos.color = Color.green;
//         Gizmos.DrawWireCube(transform.position + Vector3.up * height * 0.5f,
//                            new Vector3(areaSize.x, height, areaSize.y));
//     }
// }
using UnityEngine;
using UnityEngine.Rendering;

public class GrassGPUProcedural : MonoBehaviour
{
    [Header("References")]
    public ComputeShader computeShader;
    public Material grassMaterial;

    [Header("Grass Parameters")]
    public float width = 0.1f;
    public float height = 1f;
    [Range(0f, 1f)] public float positionNoise = 0.5f;
    [Range(0f, 1f)] public float scaleNoise = 0.3f;
    public Gradient colorGradient;

    [Header("Spawn Area")]
    public Vector2 areaSize = new Vector2(100, 100);
    [Range(1, 1000)] public int grassCount = 1000;

    [Header("Debug")]
    public bool enableDebug = true;

    private ComputeBuffer posScaleBuffer;
    private ComputeBuffer colorBuffer;
    private RenderTexture gradientRT;
    private bool isInitialized = false;

    void Start()
    {
        InitializeGrass();
    }

    void InitializeGrass()
    {
        Cleanup();

        grassCount = Mathf.Max(1, grassCount);

        // Create buffers
        posScaleBuffer = new ComputeBuffer(grassCount, sizeof(float) * 4);
        colorBuffer = new ComputeBuffer(grassCount, sizeof(float) * 4);

        // Create gradient texture
        CreateGradientTexture();

        // Setup compute shader
        int kernelHandle = computeShader.FindKernel("CSMain");

        // Set parameters
        computeShader.SetInt("grassCount", grassCount);
        computeShader.SetVector("areaSize", areaSize);
        computeShader.SetFloat("width", width);
        computeShader.SetFloat("height", height);
        computeShader.SetFloat("positionNoise", positionNoise);
        computeShader.SetFloat("scaleNoise", scaleNoise);
        computeShader.SetBuffer(kernelHandle, "PosScaleBuffer", posScaleBuffer);
        computeShader.SetBuffer(kernelHandle, "ColorBuffer", colorBuffer);
        computeShader.SetTexture(kernelHandle, "gradientTex", gradientRT);

        // Dispatch
        uint threadGroupSize;
        computeShader.GetKernelThreadGroupSizes(kernelHandle, out threadGroupSize, out _, out _);
        int threadGroups = Mathf.CeilToInt((float)grassCount / threadGroupSize);
        computeShader.Dispatch(kernelHandle, threadGroups, 1, 1);

        // DEBUG: Read back data to verify it's working
        if (enableDebug)
        {
            Vector4[] debugData = new Vector4[Mathf.Min(grassCount, 10)];
            posScaleBuffer.GetData(debugData);
            Debug.Log("First 10 grass positions:");
            for (int i = 0; i < debugData.Length; i++)
            {
                Debug.Log($"Grass {i}: Pos({debugData[i].x}, {debugData[i].y}, {debugData[i].z}) Scale:{debugData[i].w}");
            }
        }

        // Setup material - CRITICAL: Enable instancing
        grassMaterial.enableInstancing = true;
        grassMaterial.SetBuffer("_PosScaleBuffer", posScaleBuffer);
        grassMaterial.SetBuffer("_ColorBuffer", colorBuffer);
        grassMaterial.SetFloat("_Width", width);
        grassMaterial.SetFloat("_Height", height);

        // Set material properties for the shader
        grassMaterial.SetVector("_AreaSize", areaSize);
        grassMaterial.SetInt("_GrassCount", grassCount);

        isInitialized = true;
        Debug.Log($"Grass system initialized with {grassCount} blades");
    }

    void CreateGradientTexture()
    {
        if (gradientRT != null) gradientRT.Release();

        gradientRT = new RenderTexture(256, 1, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        gradientRT.Create();

        Texture2D gradTexCPU = new Texture2D(256, 1, TextureFormat.RGBA32, false);
        for (int i = 0; i < 256; i++)
            gradTexCPU.SetPixel(i, 0, colorGradient.Evaluate(i / 255f));
        gradTexCPU.Apply();

        Graphics.Blit(gradTexCPU, gradientRT);
        DestroyImmediate(gradTexCPU);
    }

    void Update()
    {
        if (!isInitialized) return;

        // Use CommandBuffer for more control
        Graphics.DrawProcedural(
            grassMaterial,
            new Bounds(transform.position, new Vector3(areaSize.x, height * 2, areaSize.y)),
            MeshTopology.Triangles,
            6, // 6 vertices per quad (2 triangles)
            grassCount,
            camera: null,
            properties: null,
            ShadowCastingMode.On,
            true,
            gameObject.layer
        );
    }

    void OnDestroy()
    {
        Cleanup();
    }

    void Cleanup()
    {
        posScaleBuffer?.Release();
        colorBuffer?.Release();
        if (gradientRT != null) gradientRT.Release();
        isInitialized = false;
    }

    void OnValidate()
    {
        if (isInitialized && Application.isPlaying)
        {
            InitializeGrass();
        }
    }
} 
