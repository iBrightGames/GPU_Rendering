
using UnityEngine;

public class SimpleSpriteInstancer : MonoBehaviour
{
    [Header("Source SpriteRenderer (Scene or Prefab)")]
    public SpriteRenderer sourceSprite;

    [Header("Instances Settings")]
    public int instanceCount = 10000;
    public float scaleMultiplier = 1f;

    [Header("Position Limits")]
    public Vector3 minPosition = new Vector3(-10f, -10f, 0f);
    public Vector3 maxPosition = new Vector3(10f, 10f, 0f);

    Mesh quadMesh;
    public Material instancingMaterial;
    public Matrix4x4[] matrices;

    void Start()
    {
        InitializeMatrices();
    }

    void Update()
    {
        if (instancingMaterial != null && matrices != null)
            Graphics.DrawMeshInstanced(quadMesh, 0, instancingMaterial, matrices, matrices.Length);
    }

    Mesh CreateQuad()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3( 0.5f, -0.5f, 0),
            new Vector3( 0.5f,  0.5f, 0),
            new Vector3(-0.5f,  0.5f, 0)
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };
        mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        return mesh;
    }

    Material CreateMaterial(Texture2D tex)
    {
        Shader shader = Shader.Find("Custom/UnlitInstancedCompute");
        Material mat = new Material(shader);
        mat.mainTexture = tex;
        mat.enableInstancing = true;
        return mat;
    }

    [ContextMenu("Regenerate Matrices")]
    public void InitializeMatrices()
    {
        if (sourceSprite == null || sourceSprite.sprite == null)
        {
            Debug.LogWarning("Source SpriteRenderer veya sprite atanmadı!");
            return;
        }

        // Quad mesh
        if (quadMesh == null)
            quadMesh = CreateQuad();

        // Material
        if (instancingMaterial == null)
            instancingMaterial = CreateMaterial(sourceSprite.sprite.texture);

        // Matrisler
        matrices = new Matrix4x4[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            Vector3 pos = new Vector3(
                Random.Range(minPosition.x, maxPosition.x),
                Random.Range(minPosition.y, maxPosition.y),
                Random.Range(minPosition.z, maxPosition.z)
            );

            matrices[i] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * scaleMultiplier);
        }

        // Orijinal SpriteRenderer’ı gizle
        sourceSprite.enabled = false;
    }

    void OnDrawGizmos()
    {
        if (matrices == null) return;

        Gizmos.color = Color.red;
        for (int i = 0; i < matrices.Length; i++)
        {
            Vector3 pos = matrices[i].GetColumn(3);
            Gizmos.DrawSphere(pos, 0.1f);
        }
    }
}
