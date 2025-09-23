using UnityEngine;

public class SimpleInstancing : MonoBehaviour
{
    public GameObject prefab;
    public int instanceCount = 100;
    public Vector3 areaSize = new Vector3(10, 0, 10);

    private Mesh mesh;
    private Material material;
    private Matrix4x4[] matrices;

    void Start()
    {
        if (prefab == null) return;

        var mf = prefab.GetComponent<MeshFilter>();
        var mr = prefab.GetComponent<MeshRenderer>();
        if (mf == null || mr == null) return;

        mesh = mf.sharedMesh;
        material = mr.sharedMaterial;

        matrices = new Matrix4x4[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            Vector3 pos = new Vector3(
                Random.Range(-areaSize.x / 2, areaSize.x / 2),
                0f,
                Random.Range(-areaSize.z / 2, areaSize.z / 2)
            );
            matrices[i] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
        }
    }

    void Update()
    {
        if (mesh == null || material == null || matrices == null) return;

        Graphics.DrawMeshInstanced(mesh, 0, material, matrices);
    }
}
