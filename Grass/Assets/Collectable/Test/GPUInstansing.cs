using UnityEngine;

public class GPUInstancer : MonoBehaviour
{
    public GameObject prefab;
    private Mesh mesh;
    public Material material;
    private ComputeBuffer positionBuffer;

    void Start()
    {
        if (prefab == null) return;

        var mf = prefab.GetComponent<MeshFilter>();
        var mr = prefab.GetComponent<MeshRenderer>();
        if (mf == null || mr == null) return;

        mesh = mf.sharedMesh;
        material = mr.sharedMaterial;

        var data = DataManager.Instance.positions;
        if (data.Count == 0) return;

        positionBuffer = new ComputeBuffer(data.Count, sizeof(float) * 3);
        positionBuffer.SetData(data);
        material.SetBuffer("_Positions", positionBuffer);
    }

    void Update()
    {
        if (mesh == null || material == null || positionBuffer == null) return;

        Graphics.DrawMeshInstancedProcedural(
            mesh,
            0,
            material,
            new Bounds(Vector3.zero, Vector3.one * 1000f),
            DataManager.Instance.positions.Count
        );
    }

    void OnDisable()
    {
        if (positionBuffer != null)
        {
            positionBuffer.Release();
            positionBuffer = null;
        }
    }
}
