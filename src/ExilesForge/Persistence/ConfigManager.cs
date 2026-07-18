// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using System.Text.Json;
using UOA.Core;

namespace TEF.Persistence
{
    /// <summary>
    /// Owns the launch config file's location and raw read/write, mirroring
    /// SaveManager's pattern. Replaces the hardcoded --uopath/--clientversion
    /// developer defaults in Program.cs with a discoverable, editable file -
    /// a real player shouldn't need to pass launch args or recompile just to
    /// point TEF at their UO install.
    /// </summary>
    public static class ConfigManager
    {
        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ExilesForge"
        );

        private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "config.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        /// <summary>Returns null if there's no config file, or it can't be read/parsed - caller falls back to defaults.</summary>
        public static GameSettings Load()
        {
            if (!File.Exists(ConfigPath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(ConfigPath);

                return JsonSerializer.Deserialize<GameSettings>(json, JsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Save(GameSettings settings)
        {
            Directory.CreateDirectory(ConfigDirectory);

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
    }
}
