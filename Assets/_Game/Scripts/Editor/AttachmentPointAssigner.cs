using System;
using System.Collections.Generic;
using System.Linq;
using RainbowZoo.Core;
using UnityEditor;
using UnityEngine;

namespace RainbowZoo.Editor
{
    /// <summary>
    /// Phase 11 UX refinement: auto-detects a sensible "carry bone" per species (mouth/jaw first,
    /// falling back to the head) across every AnimalDefinition's AnimalPrefab, and sets it as that
    /// definition's AttachmentPoint -- the shared Toy currently parents to the root transform for
    /// every animal (AnimalController.ResolveRuntimeAttachmentPoint's fallback), since none of the
    /// 77 definitions have ever had this set. Idempotent: skips any definition that already has an
    /// AttachmentPoint, so a hand-tuned choice (e.g. a hand bone for a primate instead of the head)
    /// survives a re-run. Never guesses past its name-match heuristic -- a rig with no bone
    /// matching the priority list is left unset and reported, not assigned something arbitrary
    /// like the root bone, which would just read as "toy floating at the animal's center" instead
    /// of "toy floating near its face," not meaningfully better than the existing fallback.
    /// </summary>
    public static class AttachmentPointAssigner
    {
        private const string DataFolder = "Assets/_Game/Data";

        // Ordered most-specific first. Every entry gets a full exact-name pass across all
        // definitions before the looser contains-match pass runs at all, so a rig with a real
        // "Mouth" bone never loses it to a same-run coincidental substring match elsewhere.
        private static readonly string[] PriorityNames =
        {
            "Mouth", "Jaw", "Muzzle", "Snout", "Beak",
            "HeadEnd", "Head_End", "Head",
        };

        [MenuItem("Rainbow Zoo/Content/Assign Toy Attachment Points")]
        private static void AssignAll()
        {
            var guids = AssetDatabase.FindAssets("t:AnimalDefinition", new[] { DataFolder });

            int assigned = 0, alreadySet = 0, noPrefab = 0;
            var unmatchedIds = new List<string>();
            var matchLog = new List<string>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<AnimalDefinition>(path);
                if (definition == null) continue;

                if (definition.AttachmentPoint != null)
                {
                    alreadySet++;
                    continue;
                }

                if (definition.AnimalPrefab == null)
                {
                    noPrefab++;
                    Debug.LogWarning($"[AttachmentPointAssigner] '{definition.Id}' has no AnimalPrefab -- skipping.");
                    continue;
                }

                var bone = FindCarryBone(definition.AnimalPrefab.transform);
                if (bone == null)
                {
                    unmatchedIds.Add(definition.Id);
                    continue;
                }

                var so = new SerializedObject(definition);
                so.FindProperty("attachmentPoint").objectReferenceValue = bone;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(definition);

                assigned++;
                matchLog.Add($"  {definition.Id} -> {RelativePath(definition.AnimalPrefab.transform, bone)}");
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[AttachmentPointAssigner] Assigned {assigned}, already set {alreadySet}, no prefab {noPrefab}, unmatched {unmatchedIds.Count}.\n" +
                string.Join("\n", matchLog));

            if (unmatchedIds.Count > 0)
            {
                Debug.LogWarning($"[AttachmentPointAssigner] No carry-bone match (still falling back to the root transform): {string.Join(", ", unmatchedIds)}");
            }
        }

        /// <summary>Exact-name match across the whole priority list first, then a looser contains-match pass -- see the class summary for why the passes don't interleave.</summary>
        private static Transform FindCarryBone(Transform root)
        {
            var all = root.GetComponentsInChildren<Transform>(true);

            foreach (var name in PriorityNames)
            {
                var exact = all.FirstOrDefault(t => t != root && string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }

            foreach (var name in PriorityNames)
            {
                var contains = all.FirstOrDefault(t => t != root && t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (contains != null) return contains;
            }

            return null;
        }

        private static string RelativePath(Transform root, Transform target)
        {
            var segments = new List<string>();
            for (var current = target; current != null && current != root; current = current.parent)
            {
                segments.Insert(0, current.name);
            }
            return string.Join("/", segments);
        }
    }
}
