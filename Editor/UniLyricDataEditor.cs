// Editor-only. Will NOT be included in player builds.
using UnityEditor;
using UnityEngine;

namespace UniLyricSync.Editor
{
    [CustomEditor(typeof(UniLyricData))]
    public class UniLyricDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // ── Open button ───────────────────────────────────────────────────
            GUI.color = new Color(0.55f, 0.85f, 1f);
            if (GUILayout.Button("Open in UniLyricSync", GUILayout.Height(28)))
            {
                UniLyricSyncWindow.OpenWithData((UniLyricData)target);
            }
            GUI.color = Color.white;

            EditorGUILayout.Space(4);

            // ── Draw default inspector below the button ────────────────────────
            DrawDefaultInspector();
        }
    }
}
