using UnityEngine;

public class MultiCircleGizmo : MonoBehaviour
{
    public BranchData rootBranch;

    private void OnDrawGizmos()
    {
        if (rootBranch == null) return;
        DrawMainBranch(transform.position, rootBranch, 0, 90); // 90 derece yukarı
    }
    void DrawMainBranch(Vector3 start, BranchData branch, int depth, float rotationAngle)
    {
        // Ana dalı çiz
        Vector3 direction = new Vector3(Mathf.Cos(rotationAngle * Mathf.Deg2Rad),
                                        Mathf.Sin(rotationAngle * Mathf.Deg2Rad), 0f);
        Vector3 end = start + direction * branch.branchLength;

        Gizmos.color = new Color(0.55f, 0.27f, 0.07f);
        Gizmos.DrawLine(start, end);

        // Uçta çember çiz, segmentleri tip kontrolü ile
        DrawCircleSegments(end, branch, depth, rotationAngle);
    }

    void DrawCircleSegments(Vector3 center, BranchData branch, int depth, float angle)
    {
        int segments = branch.segments;
        float percent = branch.percent;
        int drawSegments = Mathf.CeilToInt(segments * (percent / 100f));

        float totalAngle = (percent / 100f) * 360f;
        float startAngle = -totalAngle / 2f + angle;
        float angleStep = totalAngle / Mathf.Max(1, drawSegments);

        Vector3 prevPoint = center + new Vector3(Mathf.Cos(startAngle * Mathf.Deg2Rad) * branch.radius,
                                                 Mathf.Sin(startAngle * Mathf.Deg2Rad) * branch.radius, 0f);

        for (int i = 0; i <= drawSegments; i++)
        {
            float _angle = startAngle + i * angleStep;
            Vector3 newPoint = center + new Vector3(Mathf.Cos(_angle * Mathf.Deg2Rad) * branch.radius,
                                                    Mathf.Sin(_angle * Mathf.Deg2Rad) * branch.radius, 0f);

            // Segmentten merkeze çizgi
            Gizmos.color = new Color(0.55f, 0.27f, 0.07f);
            Gizmos.DrawLine(center, newPoint);

            // Segment tipi kontrolü
            if (branch.childBranch != null)
            {
                if (branch.childBranch.endType == NodeEndType.Dot)
                {
                    DrawDot(newPoint);
                }
                else if (branch.childBranch.endType == NodeEndType.Branch)
                {
                    // Segmentin açısını main branch yönü olarak ver
                    DrawMainBranch(newPoint, branch.childBranch, depth + 1, _angle);
                }
            }

            prevPoint = newPoint;
        }
    }

    void DrawDot(Vector3 position)
    {
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(position, 0.1f);
    }
}
