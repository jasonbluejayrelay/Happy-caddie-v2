using System;
using System.IO;
using Harvestline.Core;
using Harvestline.Core.Content;
using Harvestline.Core.Save;
using UnityEngine;

namespace Harvestline.Unity.Bootstrap
{
    /// <summary>
    /// Platform file I/O for the save (spec §8). The Core owns serialization
    /// (SaveSerializer); this layer just does an atomic write to
    /// <see cref="Application.persistentDataPath"/> — temp file → File.Replace — so a
    /// crash mid-write never corrupts an existing save. Keeps all platform code out of Core.
    /// </summary>
    public static class SaveIO
    {
        private static string Path => System.IO.Path.Combine(Application.persistentDataPath, "harvestline.save.json");
        private static string TempPath => Path + ".tmp";
        private static string BackupPath => Path + ".bak";

        public static bool Exists() => File.Exists(Path);

        public static void Save(GameState game)
        {
            string json = SaveSerializer.Serialize(game);
            File.WriteAllText(TempPath, json);
            if (File.Exists(Path))
                File.Replace(TempPath, Path, BackupPath); // atomic swap, keeps a backup
            else
                File.Move(TempPath, Path);
        }

        /// <summary>Load the save, or start a new game if none exists / it's unreadable.</summary>
        public static GameState LoadOrNew(ContentDatabase content, long nowUtc, ulong newGameSeed)
        {
            if (!Exists()) return GameState.NewGame(content, nowUtc, newGameSeed);
            try
            {
                return SaveSerializer.Deserialize(File.ReadAllText(Path), content);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Harvestline] Save unreadable, starting fresh: {e.Message}");
                // Try the backup before giving up.
                try { if (File.Exists(BackupPath)) return SaveSerializer.Deserialize(File.ReadAllText(BackupPath), content); }
                catch { /* fall through */ }
                return GameState.NewGame(content, nowUtc, newGameSeed);
            }
        }
    }
}
