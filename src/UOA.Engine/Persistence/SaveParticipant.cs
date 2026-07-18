// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Text.Json;

namespace UOA.Persistence
{
    /// <summary>
    /// One system's slice of the save file (Tier 4.5). Each participant owns a
    /// uniquely-named <see cref="Section"/> of a single save document and
    /// knows how to snapshot its own state (<see cref="Capture"/>) and apply a
    /// previously-saved snapshot (<see cref="Restore"/>). A game adds save data
    /// by registering a participant with <see cref="SaveManager"/> - no engine
    /// change, and unrelated systems never touch each other's data. Sections
    /// absent from an older save are simply skipped on load (forward/backward
    /// compatible as a game gains systems).
    /// </summary>
    public interface ISaveParticipant
    {
        /// <summary>Stable, unique key for this participant's section (e.g. "player", "world.entities").</summary>
        string Section { get; }

        /// <summary>Returns a serializable snapshot of this system's current state.</summary>
        object Capture();

        /// <summary>Applies this section's saved payload back onto the live system.</summary>
        void Restore(JsonElement section);
    }

    /// <summary>
    /// Delegate-backed <see cref="ISaveParticipant"/> so a caller can register
    /// a section with two lambdas instead of a whole class - the common case
    /// when the state lives on an object the caller already holds (e.g. a
    /// scene registering its player/clock/entities sections).
    /// </summary>
    public sealed class SaveSection : ISaveParticipant
    {
        private readonly Func<object> _capture;
        private readonly Action<JsonElement> _restore;

        public SaveSection(string section, Func<object> capture, Action<JsonElement> restore)
        {
            Section = section;
            _capture = capture;
            _restore = restore;
        }

        public string Section { get; }
        public object Capture() => _capture();
        public void Restore(JsonElement section) => _restore(section);
    }
}
