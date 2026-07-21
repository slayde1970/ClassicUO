// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace UOA.World
{
    /// <summary>
    /// A colored ambient-tint curve over the day, sampled by
    /// <see cref="DayNightOverlay"/>. Each stop pairs a time of day (0..1
    /// fraction of a full day, matching WorldClock.TimeOfDay) with the multiply
    /// tint applied to the whole scene at that time: white (255,255,255) = full
    /// neutral daylight, a dim blue = night moonlight, warm hues =
    /// sunrise/sunset. <see cref="Sample"/> linearly interpolates between the
    /// two surrounding stops (clamping past the ends). The game builds one from
    /// daynight.json and assigns it to the overlay; <see cref="Default"/> is the
    /// built-in fallback so the engine renders sensibly with no config.
    /// </summary>
    public sealed class DayNightGradient
    {
        public readonly struct Stop
        {
            /// <summary>Time of day as a 0..1 fraction (0 = midnight, 0.5 = noon).</summary>
            public readonly float Time;

            /// <summary>Multiply tint applied to the whole scene at this time.</summary>
            public readonly Color Color;

            public Stop(float time, Color color)
            {
                Time = time;
                Color = color;
            }
        }

        private readonly Stop[] _stops;

        /// <param name="stops">Time-keyed tint stops. Any order (sorted on construction). Null/empty falls back to <see cref="Default"/>'s stops.</param>
        public DayNightGradient(IReadOnlyList<Stop> stops)
        {
            if (stops == null || stops.Count == 0)
            {
                _stops = Default._stops;
                return;
            }

            var copy = new Stop[stops.Count];
            for (int i = 0; i < stops.Count; i++)
            {
                copy[i] = stops[i];
            }

            Array.Sort(copy, static (a, b) => a.Time.CompareTo(b.Time));
            _stops = copy;
        }

        /// <summary>Multiply tint at a given time of day (0..1): clamps past the first/last stop, linearly interpolates between stops.</summary>
        public Color Sample(float timeOfDay)
        {
            var stops = _stops;

            if (timeOfDay <= stops[0].Time)
            {
                return stops[0].Color;
            }

            if (timeOfDay >= stops[stops.Length - 1].Time)
            {
                return stops[stops.Length - 1].Color;
            }

            for (int i = 1; i < stops.Length; i++)
            {
                if (timeOfDay <= stops[i].Time)
                {
                    Stop a = stops[i - 1];
                    Stop b = stops[i];
                    float span = b.Time - a.Time;
                    float f = span <= 0f ? 0f : (timeOfDay - a.Time) / span;
                    return Color.Lerp(a.Color, b.Color, f);
                }
            }

            return stops[stops.Length - 1].Color;
        }

        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        // Tasteful defaults: neutral white midday, warm sunrise/sunset, cool
        // blue moonlight at night. Kept in sync with the game's daynight.json
        // seed (TEF.Persistence.DayNightProfile.CreateDefault) so behaviour is
        // identical whether the tint comes from the file or this fallback.
        public static readonly DayNightGradient Default = new(new[]
        {
            new Stop(0.000f, Rgb(70, 82, 125)),   // 00:00 night - blue moonlight
            new Stop(0.229f, Rgb(80, 88, 138)),   // 05:30 pre-dawn
            new Stop(0.271f, Rgb(232, 160, 110)), // 06:30 sunrise - warm
            new Stop(0.354f, Rgb(255, 255, 255)), // 08:30 full day
            new Stop(0.688f, Rgb(255, 255, 255)), // 16:30 full day
            new Stop(0.750f, Rgb(232, 138, 90)),  // 18:00 sunset - warm
            new Stop(0.812f, Rgb(94, 87, 136)),   // 19:30 dusk - blue/purple
            new Stop(0.875f, Rgb(70, 82, 125)),   // 21:00 night
            new Stop(1.000f, Rgb(70, 82, 125)),   // 24:00 = 00:00 wrap
        });
    }
}
