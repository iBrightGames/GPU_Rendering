using UnityEngine;

public class AdvancedGPUInstancer : MonoBehaviour
{
    [Header("Spawn Object")]
    public GameObject objectToSpawn;
    
    [Header("Spawn Settings")]
    [Range(1, 1000)]
    public int spawnCount = 100;
    
    [Header("Area Limits")]
    public Vector3 areaSize = new Vector3(10f, 5f, 10f);
    Vector3 areaCenter;
    
    [Header("Rotation Settings")]
    public bool randomRotation = true;
    public Vector3 rotationMin = Vector3.zero;
    public Vector3 rotationMax = new Vector3(360, 360, 360);
    
    [Header("Scale Settings")]
    public bool randomScale = false;
    public float minScale = 0.5f;
    public float maxScale = 2f;
    
    private Matrix4x4[] matrices;
    private Mesh instanceMesh;
    private Material instanceMaterial;
    
    void Start()
    {
        areaCenter = transform.position;
        GenerateInstances();
    }
    
    void Update()
    {
        if (matrices != null && instanceMesh != null && instanceMaterial != null)
        {
            Graphics.DrawMeshInstanced(instanceMesh, 0, instanceMaterial, matrices);
        }
    }
    
    [ContextMenu("Generate Instances")]
    public void GenerateInstances()
    {
        if (objectToSpawn == null) return;
        
        MeshFilter meshFilter = objectToSpawn.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = objectToSpawn.GetComponent<MeshRenderer>();
        
        if (meshFilter != null && meshRenderer != null)
        {
            instanceMesh = meshFilter.sharedMesh;
            instanceMaterial = meshRenderer.sharedMaterial;
            
            matrices = new Matrix4x4[spawnCount];
            
            for (int i = 0; i < spawnCount; i++)
            {
                // Random position within area limits
                Vector3 randomPosition = new Vector3(
                    Random.Range(-areaSize.x / 2, areaSize.x / 2) + areaCenter.x,
                    Random.Range(-areaSize.y / 2, areaSize.y / 2) + areaCenter.y,
                    Random.Range(-areaSize.z / 2, areaSize.z / 2) + areaCenter.z
                );
                
                // Random rotation
                Quaternion rotation = randomRotation ? 
                    Quaternion.Euler(
                        Random.Range(rotationMin.x, rotationMax.x),
                        Random.Range(rotationMin.y, rotationMax.y),
                        Random.Range(rotationMin.z, rotationMax.z)
                    ) : Quaternion.identity;
                
                // Random scale
                Vector3 scale = randomScale ? 
                    Vector3.one * Random.Range(minScale, maxScale) : 
                    Vector3.one;
                
                matrices[i] = Matrix4x4.TRS(randomPosition, rotation, scale);
            }
        }
    }
    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(areaCenter, areaSize);
        
        // Show spawn count in scene view
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.white;
        style.fontSize = 12;
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(areaCenter + Vector3.up * areaSize.y / 2, 
                                $"Instances: {spawnCount}\nArea: {areaSize}", style);
        #endif
    }
}