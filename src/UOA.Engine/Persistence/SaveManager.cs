// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace UOA.Persistence
{
    /// <summary>
    /// Owns a game's on-disk saves as JSON documents keyed by participant
    /// section (Tier 4.5). Systems register an <see cref="ISaveParticipant"/>;
    /// <see cref="Save"/> asks each to snapshot its section and <see cref="Load"/>
    /// dispatches each stored section back to its participant. The engine never
    /// knows the shape of any section - each game/system owns its own.
    ///
    /// Tier 4.7 #29: one manager now serves multiple named slots (the same
    /// registered participants read/write whichever slot is passed), and every
    /// document carries a format version in a reserved <see cref="MetaSection"/>
    /// so a future schema change has a migration hook (<see cref="LoadedVersion"/>).
    /// Never throws on a bad/absent read - callers treat that as "no save".
    /// </summary>
    public sealed class SaveManager
    {
        /// <summary>Slot used by the parameterless-style calls (maps to save.json).</summary>
        public const string DefaultSlot = "default";

        /// <summary>Reserved document key holding save-format metadata (not a participant).</summary>
        public const string MetaSection = "_meta";

        /// <summary>Bumped when the on-disk layout changes in a way participants must migrate.</summary>
        public const int SaveFormatVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _directory;
        private readonly List<ISaveParticipant> _participants = new();

        /// <param name="appId">App folder under %AppData% (e.g. "ExilesForge").</param>
        public SaveManager(string appId)
        {
            _directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                appId
            );
        }

        /// <summary>Format version of the document most recently <see cref="Load"/>ed (0 if none/absent).</summary>
        public int LoadedVersion { get; private set; }

        /// <summary>Registers a system's save section. Order only affects on-disk key order, not correctness (each section is independent).</summary>
        public void Register(ISaveParticipant participant) => _participants.Add(participant);

        public bool Exists(string slot = DefaultSlot) => File.Exists(SlotPath(slot));

        public void Save(string slot = DefaultSlot)
        {
            var document = new Dictionary<string, JsonElement>(_participants.Count + 1)
            {
                [MetaSection] = JsonSerializer.SerializeToElement(new SaveMeta { Version = SaveFormatVersion }),
            };

            foreach (var participant in _participants)
            {
                object payload = participant.Capture();
                // Serialize by the payload's RUNTIME type so a section declared
                // as `object` still emits its real properties (avoids the
                // System.Text.Json "object serializes as empty" gotcha).
                document[participant.Section] = JsonSerializer.SerializeToElement(payload, payload.GetType());
            }

            Directory.CreateDirectory(_directory);
            File.WriteAllText(SlotPath(slot), JsonSerializer.Serialize(document, JsonOptions));
        }

        /// <summary>
        /// Loads a slot's save file (if any) and hands each registered
        /// participant its section. Returns false if there was no file or it
        /// couldn't be parsed (participants are left untouched). A participant
        /// whose section is missing from the file is simply skipped, and
        /// <see cref="LoadedVersion"/> is set from the document metadata.
        /// </summary>
        public bool Load(string slot = DefaultSlot)
        {
            LoadedVersion = 0;

            var path = SlotPath(slot);
            if (!File.Exists(path))
            {
                return false;
            }

            Dictionary<string, JsonElement> document;
            try
            {
                document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path), JsonOptions);
            }
            catch (Exception)
            {
                return false;
            }

            if (document == null)
            {
                return false;
            }

            if (document.TryGetValue(MetaSection, out var meta) &&
                meta.ValueKind == JsonValueKind.Object &&
                meta.TryGetProperty(nameof(SaveMeta.Version), out var version) &&
                version.TryGetInt32(out var v))
            {
                LoadedVersion = v;
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

        public void Delete(string slot = DefaultSlot)
        {
            var path = SlotPath(slot);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>Names of every slot that currently has a save on disk (unordered).</summary>
        public IEnumerable<string> ListSlots()
        {
            if (!Directory.Exists(_directory))
            {
                yield break;
            }

            foreach (var file in Directory.EnumerateFiles(_directory, "save*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name == "save")
                {
                    yield return DefaultSlot;
                }
                else if (name.StartsWith("save-", StringComparison.Ordinal))
                {
                    yield return name.Substring("save-".Length);
                }
            }
        }

        // Default slot keeps the historical save.json name; named slots get a
        // sanitized suffix so a slot label can't escape the app folder.
        private string SlotPath(string slot)
        {
            string file = slot == DefaultSlot ? "save.json" : $"save-{Sanitize(slot)}.json";
            return Path.Combine(_directory, file);
        }

        private static string Sanitize(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot))
            {
                return "unnamed";
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                slot = slot.Replace(c, '_');
            }

            return slot;
        }

        private sealed class SaveMeta
        {
            public int Version { get; set; }
        }
    }
}
