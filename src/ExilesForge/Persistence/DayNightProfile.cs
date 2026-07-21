// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using UOA.World;

namespace TEF.Persistence
{
    /// <summary>
    /// Designer-friendly JSON DTO for the day/night color curve (daynight.json,
    /// Tier 4.8 #32). Each stop is a wall-clock <see cref="DayNightStop.Hour"/>
    /// (0..24) plus a hex multiply tint (<c>#RRGGBB</c>): white = full daylight,
    /// a dim blue = night moonlight, warm hues = sunrise/sunset. Edit the file
    /// under %AppData%/ExilesForge to retune the look without recompiling;
    /// <see cref="ToGradient"/> converts it to the engine's
    /// <see cref="DayNightGradient"/> (Hour/24 -> 0..1 time). Missing/unparseable
    /// stops fall back to the engine default.
    /// </summary>
    public sealed class DayNightProfile
    {
        public List<DayNightStop> Stops { get; set; } = new();

        /// <summary>
        /// Builds the engine gradient from these stops. Kept in sync with
        /// <see cref="DayNightGradient.Default"/> so the seeded file and the
        /// built-in fallback look identical.
        /// </summary>
        public DayNightGradient ToGradient()
        {
            if (Stops == null || Stops.Count == 0)
            {
                return DayNightGradient.Default;
            }

            var stops = new List<DayNightGradient.Stop>(Stops.Count);
            foreach (var s in Stops)
            {
                stops.Add(new DayNightGradient.Stop(s.Hour / 24f, ParseHex(s.Color)));
            }

            return new DayNightGradient(stops);
        }

        // "#RRGGBB" (or "RRGGBB") -> Color; falls back to white (a no-op
        // daylight tint) on anything malformed rather than throwing.
        private static Color ParseHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return Color.White;
            }

            string h = hex.TrimStart('#');
            if (h.Length != 6
                || !int.TryParse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r)
                || !int.TryParse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g)
                || !int.TryParse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
            {
                return Color.White;
            }

            return new Color(r / 255f, g / 255f, b / 255f);
        }

        /// <summary>The seed written on first run; matches DayNightGradient.Default.</summary>
        public static DayNightProfile CreateDefault() => new()
        {
            Stops = new List<DayNightStop>
            {
                new() { Hour = 0.0f,  Color = "#46527D" }, // 00:00 night - blue moonlight
                new() { Hour = 5.5f,  Color = "#50588A" }, // 05:30 pre-dawn
                new() { Hour = 6.5f,  Color = "#E8A06E" }, // 06:30 sunrise - warm
                new() { Hour = 8.5f,  Color = "#FFFFFF" }, // 08:30 full day
                new() { Hour = 16.5f, Color = "#FFFFFF" }, // 16:30 full day
                new() { Hour = 18.0f, Color = "#E88A5A" }, // 18:00 sunset - warm
                new() { Hour = 19.5f, Color = "#5E5788" }, // 19:30 dusk - blue/purple
                new() { Hour = 21.0f, Color = "#46527D" }, // 21:00 night
                new() { Hour = 24.0f, Color = "#46527D" }, // 24:00 = 00:00 wrap
            },
        };
    }

    public sealed class DayNightStop
    {
        /// <summary>Wall-clock hour, 0..24.</summary>
        public float Hour { get; set; }

        /// <summary>Hex multiply tint, "#RRGGBB".</summary>
        public string Color { get; set; } = "#FFFFFF";
    }
}
