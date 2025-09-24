
using UnityEngine;

[System.Serializable]
public struct RangeF {
    public float min, max;
    public float RandomValue() => Random.Range(min, max);
}

[CreateAssetMenu(menuName = "Tree/RandomBranchData")]
public class RandomBranchData : ScriptableObject
{
    [Header("Çember Özellikleri")]
    public RangeF radius = new RangeF { min = 1f, max = 3f };
    public RangeF segments = new RangeF { min = 3, max = 8 };
    public RangeF percent = new RangeF { min = 40, max = 90 };

    [Header("Dal Özellikleri")]
    public RangeF branchLength = new RangeF { min = 2f, max = 6f };

    [Header("Uç Noktalar")]
    public NodeEndType endType = NodeEndType.Dot;
    public BranchData childBranch;
    
    // Randomize edilmiş cache değerleri
    [HideInInspector] public float runtimeRadius;
    [HideInInspector] public int runtimeSegments;
    [HideInInspector] public float runtimePercent;
    [HideInInspector] public float runtimeLength;

    public void Randomize()
    {
        runtimeRadius = radius.RandomValue();
        runtimeSegments = Mathf.RoundToInt(segments.RandomValue());
        runtimePercent = percent.RandomValue();
        runtimeLength = branchLength.RandomValue();
    }
}
class RuntimeBranch
{
    public float radius;
    public int segments;
    public float percent;
    public float branchLength;
    public NodeEndType endType;
    public BranchData childRef;
}

