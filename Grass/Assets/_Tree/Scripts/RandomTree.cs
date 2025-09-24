using UnityEngine;

[System.Serializable]
public class BranchRuntime
{
    public float radius;
    public int segments;
    public float percent;
    public float branchLength;
    public NodeEndType endType;
    public BranchRuntime childBranch;

    public static BranchRuntime CreateRandom(int depth, int maxDepth)
    {
        BranchRuntime b = new BranchRuntime();
        b.radius = Random.Range(1f, 3f);
        b.segments = Random.Range(3, 8);
        b.percent = Random.Range(40, 90);
        b.branchLength = Random.Range(2f, 6f);

        // uç tipini seç
        if (depth < maxDepth && Random.value > 0.3f)
        {
            b.endType = NodeEndType.Branch;
            b.childBranch = CreateRandom(depth + 1, maxDepth);
        }
        else
        {
            b.endType = NodeEndType.Dot;
        }

        return b;
    }
}
