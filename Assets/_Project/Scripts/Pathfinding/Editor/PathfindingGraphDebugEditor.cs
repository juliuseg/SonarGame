using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PathfindingGraphDebug))]
public class PathfindingGraphDebugEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Generate Graph", GUILayout.Height(28)))
                ((PathfindingGraphDebug)target).GenerateGraph();

            if (GUILayout.Button("Spawn Spider", GUILayout.Height(28)))
                ((PathfindingGraphDebug)target).SpawnSpider();
        }

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter play mode to generate the graph.", MessageType.Info);
    }
}
