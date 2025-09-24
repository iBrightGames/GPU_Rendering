using UnityEngine;

public class InteractionChecker : MonoBehaviour
{
    [Header("Interaction Settings")]
    public float radius = 5f;
    public float forwardDotThreshold = 0.9f; // 1 = tam ortada

    void Update()
    {
        var cam = Camera.main;
        if (cam == null) return;

        Vector3 camPos = cam.transform.position;
        Vector3 camForward = cam.transform.forward;

        foreach (var pos in DataManager.Instance.positions)
        {
            Vector3 dir = pos - camPos;
            if (dir.magnitude < radius && Vector3.Dot(camForward, dir.normalized) > forwardDotThreshold)
            {
                Debug.Log("Orta alanda obje var!");
            }
        }
    }
}
