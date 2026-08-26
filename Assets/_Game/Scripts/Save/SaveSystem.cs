using System;
using System.IO;
using RainbowZoo.Core;
using UnityEngine;

namespace RainbowZoo.Save
{
    /// <summary>
    /// Static service that serializes the zoo's persisted state (ZooLayoutState's placed
    /// animals, plus ZooCareMeterState's heart count/threshold) to a local JSON file via a
    /// temp-file-then-atomic-rename write, keeping a 1-slot rolling backup of the last known-good
    /// save (section 12). ZooManager is the only caller -- it already owns both state objects.
    /// </summary>
    public static class SaveSystem
    {
        private const string SaveFileName = "zoo_save.json";
        private const string BackupFileName = "zoo_save.backup.json";
        private const string TempFileName = "zoo_save.tmp.json";

        /// <summary>Settable so Edit Mode tests can redirect writes to a throwaway directory instead of the real Application.persistentDataPath.</summary>
        public static string SaveDirectory { get; set; } = Application.persistentDataPath;

        private static string SavePath => Path.Combine(SaveDirectory, SaveFileName);
        private static string BackupPath => Path.Combine(SaveDirectory, BackupFileName);
        private static string TempPath => Path.Combine(SaveDirectory, TempFileName);

        [Serializable]
        public sealed class SaveData
        {
            public ZooLayoutState layout = new ZooLayoutState();
            public int currentHearts;
            public int currentThreshold;
            public bool hasFullUnlock;
        }

        /// <summary>
        /// Writes the current zoo state. Order matters for crash-safety: the new data lands in a
        /// temp file first (a failed/interrupted write never touches the real save), any existing
        /// save is mirrored to the backup slot next (so backup always holds the last state that
        /// was fully committed before this write), and only then does the temp file atomically
        /// replace the main save.
        /// </summary>
        public static void Save(ZooLayoutState layout, ZooCareMeterState careMeter, bool hasFullUnlock)
        {
            var data = new SaveData
            {
                layout = layout,
                currentHearts = careMeter.currentHearts,
                currentThreshold = careMeter.currentThreshold,
                hasFullUnlock = hasFullUnlock
            };
            string json = JsonUtility.ToJson(data, prettyPrint: true);

            Directory.CreateDirectory(SaveDirectory);
            File.WriteAllText(TempPath, json);

            if (File.Exists(SavePath))
            {
                File.Copy(SavePath, BackupPath, overwrite: true);
                File.Replace(TempPath, SavePath, null);
            }
            else
            {
                File.Move(TempPath, SavePath);
            }
        }

        /// <summary>
        /// Loads the primary save; falls back to the backup slot if the primary is missing or
        /// fails to parse; returns null (fresh empty zoo) if neither is usable.
        /// </summary>
        public static SaveData Load()
        {
            var data = TryLoad(SavePath);
            if (data != null) return data;

            if (File.Exists(SavePath))
            {
                Debug.LogWarning($"[SaveSystem] Primary save at {SavePath} failed to load -- falling back to backup.");
            }

            data = TryLoad(BackupPath);
            if (data != null) return data;

            return null;
        }

        /// <summary>Deletes both the primary save and its backup -- Reset Zoo (design doc section 12). Irreversible; the caller is responsible for confirming with the player first.</summary>
        public static void DeleteSave()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            if (File.Exists(BackupPath)) File.Delete(BackupPath);
        }

        private static SaveData TryLoad(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<SaveData>(json);
                return data?.layout?.placedAnimals != null ? data : null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Failed to parse save at {path}: {e.Message}");
                return null;
            }
        }
    }
}
