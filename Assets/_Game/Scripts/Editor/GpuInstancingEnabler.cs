using UnityEditor;
using UnityEngine;

namespace RainbowZoo.Editor
{
    /// <summary>
    /// Batch-enables GPU Instancing on every vendor Suriyun material (design doc section 13,
    /// Phase 9 performance pass) -- a single flag per material that lets URP batch repeated
    /// habitat/animal meshes sharing a material into fewer draw calls. Pure metadata: it doesn't
    /// touch shaders, textures, or visuals, so it's safe to run and re-run at any time.
    /// </summary>
    public static class GpuInstancingEnabler
    {
        [MenuItem("Rainbow Zoo/Performance/Enable GPU Instancing on Suriyun Materials")]
        private static void EnableInstancing()
        {
            var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Suriyun" });
            int changed = 0;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.enableInstancing) continue;

                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                changed++;
            }

            if (changed > 0)
            {
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[GpuInstancingEnabler] Enabled GPU Instancing on {changed} material(s) under Assets/Suriyun ({guids.Length} scanned).");
        }
    }
}
