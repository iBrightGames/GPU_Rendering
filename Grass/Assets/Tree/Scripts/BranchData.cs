
using UnityEngine;

public enum NodeEndType { Dot, Branch }

[CreateAssetMenu(menuName = "Tree/BranchData")]
public class BranchData : ScriptableObject
{
    [Header("Çember Özellikleri")]
    [Range(1f, 10f)]
    public float radius = 1f;

    [Range(3, 10)]
    public int segments = 8;

    [Range(10, 90)]
    public int percent = 70;


    [Header("Dal Özellikleri")]

    [Range(1, 10)]
    public float branchLength = 5f;

    // [Range(0, 180)]
    // public float branchAngle = 90f;

    [Header("Uç Noktalar")]
    public NodeEndType endType = NodeEndType.Dot;
    public BranchData childBranch; // endType Branch ise kullanılır
}
