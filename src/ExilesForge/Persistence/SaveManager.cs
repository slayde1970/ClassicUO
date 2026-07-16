// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using System.Text.Json;

namespace TEF.Persistence
{
    /// <summary>
    /// Owns the save file's location and raw read/write - the only place
    /// that knows the on-disk path, so nothing else hardcodes it. One save
    /// slot for now (see Design/prd-persistence.md 4.1/4.2/6 - multiple
    /// slots are an explicit non-goal this phase).
    /// </summary>
    public static class SaveManager
    {
        private static readonly string SaveDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ExilesForge"
        );

        private static readonly string SavePath = Path.Combine(SaveDirectory, "save.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        public static bool Exists() => File.Exists(SavePath);

        public static void Save(SaveData data)
        {
            Directory.CreateDirectory(SaveDirectory);

            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(SavePath, json);
        }

        /// <summary>Returns null if there's no save file, or it can't be read/parsed - callers treat that the same as "no save".</summary>
        public static SaveData Load()
        {
            if (!File.Exists(SavePath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(SavePath);

                return JsonSerializer.Deserialize<SaveData>(json, JsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Delete()
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
            }
        }
    }
}
