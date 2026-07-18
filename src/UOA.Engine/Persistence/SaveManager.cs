// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace UOA.Persistence
{
    /// <summary>
    /// Owns one save slot's on-disk location and read/write, as a single JSON
    /// document keyed by participant section (Tier 4.5). Systems register an
    /// <see cref="ISaveParticipant"/>; <see cref="Save"/> asks each to snapshot
    /// its section and <see cref="Load"/> dispatches each stored section back
    /// to its participant. The engine never knows the shape of any section -
    /// each game/system owns its own. One slot for now (multiple slots stay an
    /// explicit non-goal); never throws on a bad/absent read - callers treat
    /// that as "no save".
    /// </summary>
    public sealed class SaveManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _directory;
        private readonly string _path;
        private readonly List<ISaveParticipant> _participants = new();

        /// <param name="appId">App folder under %AppData% (e.g. "ExilesForge").</param>
        /// <param name="fileName">Save file name; defaults to save.json.</param>
        public SaveManager(string appId, string fileName = "save.json")
        {
            _directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                appId
            );
            _path = Path.Combine(_directory, fileName);
        }

        /// <summary>Registers a system's save section. Order only affects on-disk key order, not correctness (each section is independent).</summary>
        public void Register(ISaveParticipant participant) => _participants.Add(participant);

        public bool Exists() => File.Exists(_path);

        public void Save()
        {
            var document = new Dictionary<string, JsonElement>(_participants.Count);

            foreach (var participant in _participants)
            {
                object payload = participant.Capture();
                // Serialize by the payload's RUNTIME type so a section declared
                // as `object` still emits its real properties (avoids the
                // System.Text.Json "object serializes as empty" gotcha).
                document[participant.Section] = JsonSerializer.SerializeToElement(payload, payload.GetType());
            }

            Directory.CreateDirectory(_directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(document, JsonOptions));
        }

        /// <summary>
        /// Loads the save file (if any) and hands each registered participant
        /// its section. Returns false if there was no file or it couldn't be
        /// parsed (participants are left untouched). A participant whose
        /// section is missing from the file is simply skipped.
        /// </summary>
        public bool Load()
        {
            if (!File.Exists(_path))
            {
                return false;
            }

            Dictionary<string, JsonElement> document;
            try
            {
                document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(_path), JsonOptions);
            }
            catch (Exception)
            {
                return false;
            }

            if (document == null)
            {
                return false;
            }

            foreach (var participant in _participants)
            {
                if (document.TryGetValue(participant.Section, out var section))
                {
                    participant.Restore(section);
                }
            }

            return true;
        }

        public void Delete()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
