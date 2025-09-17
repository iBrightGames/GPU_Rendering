// using UnityEngine;
// using System.Collections.Generic;

// public enum NodeEndType { Dot, Branch }

// [CreateAssetMenu(menuName = "Tree/TreeData")]
// public class TreeDataSO : ScriptableObject
// {
//     public List<BranchData> childBranches = new List<BranchData>();
// }

// [System.Serializable]
// public class BranchData
// {
//     [Header("Çember Özellikleri")]
//     [Range(1f, 10f)] public float radius = 1f;
//     [Range(3, 10)] public int segments = 8;
//     [Range(10, 90)] public int percent = 70;

//     [Header("Dal Özellikleri")]
//     [Range(1, 10)] public float branchLength = 5f;

//     [Header("Uç Noktalar")]
//     public NodeEndType endType = NodeEndType.Dot;

//     // Alt dallar serilize edilmiş olarak, inspector’da açılır

//     public BranchData childBranch;
// }
