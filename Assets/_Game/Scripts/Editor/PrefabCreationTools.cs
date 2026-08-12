using UnityEditor;
using UnityEngine;

namespace RainbowZoo.Editor
{
    /// <summary>
    /// Creates a prefab from the selected Hierarchy GameObject via PrefabUtility directly,
    /// bypassing the Project window's drag-and-drop-to-create-prefab path (broken in some
    /// Unity 6 Editor builds -- throws inside UnityEditor.DragAndDrop.Drop's reflection call
    /// with an EntityId/UInt64 vs Int32 ArgumentException).
    /// </summary>
    public static class PrefabCreationTools
    {
        [MenuItem("GameObject/Rainbow Zoo/Save As Prefab...", false, 49)]
        private static void SaveSelectedAsPrefab()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("Save As Prefab", "Select a GameObject in the Hierarchy first.", "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Save As Prefab",
                go.name + ".prefab",
                "prefab",
                "Choose where to save the prefab.",
                "Assets/_Game");

            if (string.IsNullOrEmpty(path)) return;

            PrefabUtility.SaveAsPrefabAsset(go, path, out bool success);
            if (success)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                EditorGUIUtility.PingObject(asset);
                Debug.Log($"Saved prefab: {path}");
            }
            else
            {
                Debug.LogError($"Failed to save prefab at {path}");
            }
        }

        [MenuItem("GameObject/Rainbow Zoo/Save As Prefab...", true)]
        private static bool ValidateSaveSelectedAsPrefab()
        {
            return Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject);
        }
    }
}
