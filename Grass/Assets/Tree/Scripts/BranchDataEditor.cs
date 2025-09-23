using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(BranchData))]
public class BranchDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        RandomBranchData data = (RandomBranchData)target;

        if (GUILayout.Button("Randomize Values"))
        {
            data.Randomize();
            EditorUtility.SetDirty(data);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime Preview", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Radius", data.runtimeRadius.ToString("F2"));
        EditorGUILayout.LabelField("Segments", data.runtimeSegments.ToString());
        EditorGUILayout.LabelField("Percent", data.runtimePercent.ToString("F0") + "%");
        EditorGUILayout.LabelField("Length", data.runtimeLength.ToString("F2"));
    }
}
