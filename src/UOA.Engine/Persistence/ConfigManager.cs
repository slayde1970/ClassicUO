// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using System.Text.Json;

namespace UOA.Persistence
{
    /// <summary>
    /// Reads/writes a single app config file, generically over the game's own
    /// config type (Tier 4.5). The engine never names the game's settings
    /// shape - a game passes its own <typeparamref name="TConfig"/> (which, by
    /// convention, holds an <c>EngineSettings Engine</c> section plus whatever
    /// game-specific fields it wants) and the app folder name. Same
    /// null-on-missing/parse-fail contract as before: callers treat null as
    /// "no config, fall back to defaults".
    /// </summary>
    public sealed class ConfigManager<TConfig> where TConfig : class
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _directory;
        private readonly string _path;

        /// <param name="appId">App folder under %AppData% (e.g. "ExilesForge") - keeps each game's config isolated.</param>
        /// <param name="fileName">Config file name; defaults to config.json.</param>
        public ConfigManager(string appId, string fileName = "config.json")
        {
            _directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                appId
            );
            _path = Path.Combine(_directory, fileName);
        }

        /// <summary>Returns null if there's no config file, or it can't be read/parsed.</summary>
        public TConfig Load()
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<TConfig>(File.ReadAllText(_path), JsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Save(TConfig config)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(config, JsonOptions));
        }
    }
}
