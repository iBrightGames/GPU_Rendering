// using UnityEngine;
// using System.Collections.Generic;

// public class DataManager : MonoBehaviour
// {
//     public static DataManager Instance { get; private set; }

//     public List<Vector3> positions = new List<Vector3>();

//     [Header("Spawn Settings")]
//     public int spawnCount = 1000;
//     public Vector2 areaSize = new Vector2(50f, 50f);

//     void Awake()
//     {
//         if (Instance != null && Instance != this) Destroy(gameObject);
//         Instance = this;
//     }

//     void Start()
//     {
//         // Rastgele pozisyonlar üret
//         for (int i = 0; i < spawnCount; i++)
//         {
//             Vector3 pos = new Vector3(
//                 Random.Range(-areaSize.x, areaSize.x),
//                 0f,
//                 Random.Range(-areaSize.y, areaSize.y)
//             );
//             positions.Add(pos);
//         }
//     }
// }
using UnityEngine;
using System.Collections.Generic;

public class XPDataManager : MonoBehaviour
{
    public static XPDataManager Instance { get; private set; }

    public List<Vector3> positions = new List<Vector3>();
    [Header("Spawn Settings")]
    public GameObject prefab;
    public int spawnCount = 1000;
    public Vector3 areaSize = new Vector3(50, 0, 50);

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;
    }

    void Start()
    {
        if (prefab == null)
        {
            Debug.LogError("Prefab atanmadı!");
            return;
        }

        GeneratePositions();
    }

    void GeneratePositions()
    {
        positions.Clear();
        Vector3 center = transform.position;
        for (int i = 0; i < spawnCount; i++)
        {
            Vector3 pos = new Vector3(
                Random.Range(-areaSize.x / 2, areaSize.x / 2) + center.x,
                0f,
                Random.Range(-areaSize.z / 2, areaSize.z / 2) + center.z
            );
            positions.Add(pos);
        }
    }
}
