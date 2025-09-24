
using UnityEngine;

public class CircleGizmo : MonoBehaviour
{
    [Header("Circle Settings")]
    public float radius = 2f;
    public int segments = 100;
    [Range(0f, 100f)]
    public float percent = 100f; // Çizilecek çember yüzdesi (0-100)

    public Color lineColor = Color.green; // Çember çizgisi
    public Color dotColor = Color.red;    // Segment noktaları
    public Color centerLineColor = new Color(0.55f, 0.27f, 0.07f); // Kahverengi
    public Color extraLineColor = Color.blue; // Extra line color
    public float dotSize = 0.05f;

    [Header("Extra Line Settings")]
    public float extraLineLength = 1f;  // Uzunluk
    public float extraLineWidth = 0.05f; // Kalınlık

    private void OnDrawGizmos()
    {
        if (segments <= 0) return;

        int drawSegments = Mathf.CeilToInt(segments * (percent / 100f));
        float totalAngle = (percent / 100f) * 360f;
        float startAngle = -totalAngle / 2f; // Ortala
        float angleStep = totalAngle / drawSegments;
        float rotationOffset = 90f; // Dikey başlangıç

        Vector3 prevPoint = transform.position + new Vector3(Mathf.Cos((startAngle + rotationOffset) * Mathf.Deg2Rad) * radius,
                                                              Mathf.Sin((startAngle + rotationOffset) * Mathf.Deg2Rad) * radius, 0);

        for (int i = 1; i <= drawSegments; i++)
        {
            float angle = startAngle + i * angleStep + rotationOffset;
            Vector3 newPoint = transform.position + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                                                                Mathf.Sin(angle * Mathf.Deg2Rad) * radius, 0);

            // Çember çizgisi
            Gizmos.color = lineColor;
            Gizmos.DrawLine(prevPoint, newPoint);

            // Dot
            Gizmos.color = dotColor;
            Gizmos.DrawSphere(newPoint, dotSize);

            // Merkeze kahverengi çizgi
            Gizmos.color = centerLineColor;
            Gizmos.DrawLine(transform.position, newPoint);

            prevPoint = newPoint;
        }

        // Başlangıç noktasına dot ve merkeze çizgi
        Vector3 firstPoint = transform.position + new Vector3(Mathf.Cos((startAngle + rotationOffset) * Mathf.Deg2Rad) * radius,
                                                              Mathf.Sin((startAngle + rotationOffset) * Mathf.Deg2Rad) * radius, 0);
        Gizmos.color = dotColor;
        Gizmos.DrawSphere(firstPoint, dotSize);

        Gizmos.color = centerLineColor;
        Gizmos.DrawLine(transform.position, firstPoint);

        // --- Extra line dikdörtgen ---
        float midAngle = rotationOffset + 180; // Çemberin boş kısmının ortası
        Vector3 dir = new Vector3(Mathf.Cos(midAngle * Mathf.Deg2Rad), Mathf.Sin(midAngle * Mathf.Deg2Rad), 0);
        Vector3 center = transform.position;

        // Dört köşe
        Vector3 perp = new Vector3(-dir.y, dir.x, 0) * (extraLineWidth / 2f);
        Vector3 start = center;                // merkezden başla
        Vector3 end = center + dir * extraLineLength; // hedef yöne doğru

        Vector3 p1 = start + perp;
        Vector3 p2 = start - perp;
        Vector3 p3 = end - perp;
        Vector3 p4 = end + perp;

        Gizmos.color = extraLineColor;
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);

    }
}
