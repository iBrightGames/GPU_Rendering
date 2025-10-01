using UnityEngine;

public abstract class SpawnAreaData : ScriptableObject
{
    public virtual void DrawGizmos(Vector3 worldPosition)
    {

    }

}

[CreateAssetMenu(fileName = "NewSphereSpawnData", menuName = "Spawn/Sphere Spawn Data")]
public class SphereSpawnData : SpawnAreaData
{
    public Vector3 center = new Vector3(0, 0, 0);
    [Range(1, 100)] public float startRadius = 5f;
    [Range(100, 1000)] public float maxRadius = 50f;
    [Range(1, 100)] public int layerCount = 5;
    public bool expandFromCenter = true;

    [Header("Gizmo Settings")]
    public Color gizmoColor = Color.blue;
    public bool showGizmos = true;

    public override void DrawGizmos(Vector3 worldPosition)
    {
        if (!showGizmos) return;

        Vector3 worldCenter = worldPosition + center;

        // Maksimum küre
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(worldCenter, maxRadius);

        // Başlangıç küresi
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.5f);
        Gizmos.DrawWireSphere(worldCenter, startRadius);

        // Layer'ları çiz
        if (layerCount > 1 && expandFromCenter)
        {
            DrawSphereLayersGizmos(worldPosition);
        }
    }

    private void DrawSphereLayersGizmos(Vector3 worldPosition)
    {
        Vector3 worldCenter = worldPosition + center;

        for (int i = 0; i < layerCount; i++)
        {
            float t = i / (float)(layerCount - 1);
            float layerRadius = Mathf.Lerp(startRadius, maxRadius, t);

            Color layerColor = Color.Lerp(Color.red, gizmoColor, t);
            layerColor.a = 0.2f;
            Gizmos.color = layerColor;

            Gizmos.DrawWireSphere(worldCenter, layerRadius);
        }
    }
}

[CreateAssetMenu(fileName = "NewBoxSpawnData", menuName = "Spawn/Box Spawn Data")]
public class BoxSpawnData : SpawnAreaData
{
    public Vector3 minPosition = new Vector3(0, 0, 0);
    public Vector3 maxPosition = new Vector3(100, 100, 100);
    [Range(1, 100)] public int layerCount = 5;
    public Vector3 startLayer = new Vector3(10, 10, 10);
    public bool fillCube;

    [Header("Gizmo Settings")]
    public Color gizmoColor = Color.green;
    public bool showGizmos = true;

    // Gizmo çizim methodu
    public override void DrawGizmos(Vector3 worldPosition)
    {
        if (!showGizmos) return;

        Gizmos.color = gizmoColor;

        // Ana kutu
        Vector3 center = worldPosition + (minPosition + maxPosition) * 0.5f;
        Vector3 size = maxPosition - minPosition;
        Gizmos.DrawWireCube(center, size);

        // Layer'ları çiz
        if (layerCount > 1)
        {
            DrawLayersGizmos(worldPosition);
        }
    }

    private void DrawLayersGizmos(Vector3 worldPosition)
    {
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.3f);

        Vector3 fullSize = maxPosition - minPosition;
        Vector3 layerSize = fullSize / layerCount;

        for (int i = 0; i < layerCount; i++)
        {
            Vector3 layerMin = minPosition + layerSize * i;
            Vector3 layerCenter = worldPosition + layerMin + layerSize * 0.5f;
            Gizmos.DrawWireCube(layerCenter, layerSize);
        }
    }
}

[CreateAssetMenu(fileName = "SpawnCountData", menuName = "Spawn/Count Data")]

public class SpawnCountData : ScriptableObject
{
    [Range(0, 10000)]
    public int minInstances = 10000;
    [Range(100000, 1000000)]
    public int maxInstances = 1000000;

    [Range(0, 1000)] public int incrementPerFrame=100;
}

[CreateAssetMenu(fileName = "SpawnVisualData", menuName = "Spawn/Visual Data")]
public class SpawnVisualData : ScriptableObject
{
    [Header("3D Objects")]
    public GameObject referenceGameObject;
    public Mesh mesh;
    public Material material;

    [Header("2D Sprites")]
    public GameObject spriteReferenceObject;
    public Sprite sprite;
    public Material spriteMaterial;

    [Header("Settings")]
    public bool use2D = false;

    // Cache'lenmiş değerler
    public Mesh CachedMesh { get; private set; }
    public Material CachedMaterial { get; private set; }
    public Sprite CachedSprite { get; private set; }

    public void Initialize()
    {
        // 3D Mesh/Material cache'le
        if (referenceGameObject != null)
        {
            MeshFilter mf = referenceGameObject.GetComponent<MeshFilter>();
            MeshRenderer mr = referenceGameObject.GetComponent<MeshRenderer>();

            if (mf != null) CachedMesh = mf.sharedMesh;
            if (mr != null && mr.sharedMaterials.Length > 0)
                CachedMaterial = mr.sharedMaterials[0];
        }
        else
        {
            CachedMesh = mesh;
            CachedMaterial = material;
        }

        // 2D Sprite cache'le
        if (spriteReferenceObject != null)
        {
            SpriteRenderer sr = spriteReferenceObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                CachedSprite = sr.sprite;
                if (sr.sharedMaterial != null)
                    CachedMaterial = sr.sharedMaterial;
            }
        }
        else
        {
            CachedSprite = sprite;
        }
    }
}


