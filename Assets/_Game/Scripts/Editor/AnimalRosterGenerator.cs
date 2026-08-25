using System.Collections.Generic;
using System.IO;
using System.Text;
using RainbowZoo.Core;
using UnityEditor;
using UnityEngine;

namespace RainbowZoo.Editor
{
    /// <summary>
    /// Batch-creates one AnimalDefinition asset per Suriyun "Agent-*" prefab across the Cute Zoo
    /// 1-4 packs (Cute Pet excluded, per the design doc's Phase 10 scope) and registers each new
    /// one into the shared AnimalRoster asset. Idempotent: an existing AnimalDefinition for a
    /// given id is left completely untouched -- so hand-authored SFX/VFX/toy appearance/
    /// isIntroductory on already-created entries (e.g. the curated Cat) survive a re-run, and it's
    /// only newly-created assets that get default id/displayName/animalPrefab/isMythical=false/
    /// rarityTag="standard" wired up. Everything else (attachmentPoint, VFX, SFX, isIntroductory)
    /// is left unset for a new entry, same as every hand-authored placeholder so far -- the game
    /// already degrades gracefully without them (AnimalController falls back to the root transform
    /// for toy-carrying when AttachmentPoint is unset).
    /// </summary>
    public static class AnimalRosterGenerator
    {
        private const string DataFolder = "Assets/_Game/Data";

        private static readonly string[] PackFolders =
        {
            "Assets/Suriyun/Addon-PetZoo/Prefab/Zoo",
            "Assets/Suriyun/Addon-PetZoo/Prefab/Zoo2",
            "Assets/Suriyun/Addon-PetZoo/Prefab/Zoo3",
            "Assets/Suriyun/Addon-PetZoo/Prefab/Zoo4",
        };

        [MenuItem("Rainbow Zoo/Content/Generate Full Animal Roster (Cute Zoo 1-4)")]
        private static void GenerateRoster()
        {
            var roster = FindRoster();
            if (roster == null)
            {
                Debug.LogError($"[AnimalRosterGenerator] No AnimalRoster asset found under {DataFolder} -- aborting.");
                return;
            }

            var rosterSerialized = new SerializedObject(roster);
            var animalsProp = rosterSerialized.FindProperty("animals");

            var existingIds = new HashSet<string>();
            for (int i = 0; i < animalsProp.arraySize; i++)
            {
                var existing = animalsProp.GetArrayElementAtIndex(i).objectReferenceValue as AnimalDefinition;
                if (existing != null) existingIds.Add(existing.Id);
            }

            // Also catch AnimalDefinition assets that exist on disk but aren't (yet) in the
            // roster list, so a re-run never creates a second asset for the same species.
            foreach (var guid in AssetDatabase.FindAssets("t:AnimalDefinition", new[] { DataFolder }))
            {
                var def = AssetDatabase.LoadAssetAtPath<AnimalDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && !string.IsNullOrEmpty(def.Id)) existingIds.Add(def.Id);
            }

            int created = 0, skipped = 0;

            foreach (var folder in PackFolders)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    if (!fileName.StartsWith("Agent-") || fileName == "Agent-") continue;

                    string speciesId = fileName.Substring("Agent-".Length);
                    if (existingIds.Contains(speciesId))
                    {
                        skipped++;
                        continue;
                    }

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;

                    var definition = ScriptableObject.CreateInstance<AnimalDefinition>();
                    var so = new SerializedObject(definition);
                    so.FindProperty("id").stringValue = speciesId;
                    so.FindProperty("displayName").stringValue = SplitPascalCase(speciesId);
                    so.FindProperty("animalPrefab").objectReferenceValue = prefab;
                    so.FindProperty("isMythical").boolValue = false;
                    so.FindProperty("rarityTag").stringValue = "standard";
                    so.ApplyModifiedPropertiesWithoutUndo();

                    string assetPath = $"{DataFolder}/AnimalDefinition_{speciesId}.asset";
                    AssetDatabase.CreateAsset(definition, assetPath);

                    animalsProp.InsertArrayElementAtIndex(animalsProp.arraySize);
                    animalsProp.GetArrayElementAtIndex(animalsProp.arraySize - 1).objectReferenceValue = definition;

                    existingIds.Add(speciesId);
                    created++;
                }
            }

            rosterSerialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[AnimalRosterGenerator] Created {created} new AnimalDefinition asset(s) and added them to AnimalRoster; skipped {skipped} species that already had one.");
        }

        private static AnimalRoster FindRoster()
        {
            var guids = AssetDatabase.FindAssets("t:AnimalRoster", new[] { DataFolder });
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<AnimalRoster>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>"BearA" -> "Bear A", "MalayanTapir" -> "Malayan Tapir" -- a readable display name guessed from the prefab's PascalCase species name. Free to hand-edit afterward in the Inspector.</summary>
        private static string SplitPascalCase(string value)
        {
            var result = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
                {
                    result.Append(' ');
                }
                result.Append(value[i]);
            }
            return result.ToString();
        }
    }
}
