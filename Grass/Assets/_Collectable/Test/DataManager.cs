using UnityEngine;
using System.Collections.Generic;

public class DataManager : MonoBehaviour
{
    public static DataManager Instance { get; private set; }
    public List<Vector3> positions = new List<Vector3>();

    [Header("Spawn Settings")]
    public GameObject prefab;
    public int spawnCount = 1000;
    public Vector3 areaSize = new Vector3(50, 0, 50);

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;

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
                Random.Range(-areaSize.y / 2, areaSize.y / 2) + center.y,
                Random.Range(-areaSize.z / 2, areaSize.z / 2) + center.z
            );
            positions.Add(pos);
        }
    }
}
