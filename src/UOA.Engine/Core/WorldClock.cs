// SPDX-License-Identifier: BSD-2-Clause

namespace UOA.Core
{
    /// <summary>
    /// In-game day/night clock. Advances on the fixed simulation tick (see
    /// SimulationClock), not render Delta, so it stays consistent regardless
    /// of framerate. Day/night lighting itself is Tier 4 - this just tracks
    /// the underlying time so that and future systems (day-gated events,
    /// shop hours, etc.) have something to read.
    /// </summary>
    public sealed class WorldClock
    {
        // Placeholder - not yet tuned to a final pace. Change freely; nothing
        // else depends on this specific value yet.
        public float DayLengthSeconds = 24f * 60f;

        /// <summary>0 (midnight) to just under 1 (end of day), wraps into Day incrementing.</summary>
        public float TimeOfDay { get; private set; }

        public uint Day { get; private set; }

        public int Hour => (int)(TimeOfDay * 24f);
        public int Minute => (int)(TimeOfDay * 24f * 60f) % 60;

        /// <summary>Hour on a 12-hour clock (1-12, never 0).</summary>
        public int Hour12 => Hour % 12 == 0 ? 12 : Hour % 12;

        public string MeridiemTag => Hour < 12 ? "AM" : "PM";

        /// <summary>Restores exact saved state (see Persistence/SaveManager) - no fast-forwarding for time spent closed, per the persistence PRD's explicit non-goal.</summary>
        public void Restore(uint day, float timeOfDay)
        {
            Day = day;
            TimeOfDay = timeOfDay;
        }

        public void Advance(float fixedDelta)
        {
            TimeOfDay += fixedDelta / DayLengthSeconds;

            while (TimeOfDay >= 1f)
            {
                TimeOfDay -= 1f;
                Day++;
            }
        }
    }
}
