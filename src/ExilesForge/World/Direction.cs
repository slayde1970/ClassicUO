// SPDX-License-Identifier: BSD-2-Clause

using System;
using Microsoft.Xna.Framework;

namespace TEF.World
{
    /// <summary>
    /// UO's 8-way facing, numbered the same as ClassicUO's internal
    /// (Client-only) Game/Data/Direction.cs so it lines up with the raw
    /// direction byte every animation/movement API in ClassicUO.Assets and
    /// ClassicUO.Renderer expects.
    /// </summary>
    public enum Direction : byte
    {
        North = 0,
        Right = 1,
        East = 2,
        Down = 3,
        South = 4,
        Left = 5,
        West = 6,
        Up = 7,
    }

    public static class DirectionHelper
    {
        /// <summary>
        /// Maps a movement vector (screen space: +X right, +Y down) to the
        /// nearest of the 8 facings, so W = North (visually straight up on
        /// screen), D = East, S = South, A = West, and the diagonals
        /// (W+A = NW, W+D = NE, A+S = SW, D+S = SE) fall exactly between
        /// them - plain cardinal/ordinal facing, not UO's raw direction byte.
        ///
        /// UO's animation art itself is rotated 45 degrees from the screen
        /// compass: byte value Direction.North (0) renders as a character
        /// walking toward screen NE, not straight up (confirmed empirically -
        /// feeding the "obvious" byte for each key produced a sprite one step
        /// clockwise from the intended facing for all 4 cardinals). So the
        /// desired on-screen compass direction is computed first, then
        /// rotated back by one step (45 degrees counter-clockwise) to find
        /// the byte value that actually renders that way.
        /// </summary>
        public static Direction FromVector(Vector2 v)
        {
            if (v == Vector2.Zero)
            {
                return Direction.South;
            }

            double angle = Math.Atan2(-v.Y, v.X);
            if (angle < 0)
            {
                angle += Math.PI * 2.0;
            }

            int step = (int)Math.Round(angle / (Math.PI / 4.0)) % 8;
            int index = ((1 - step) % 8 + 8) % 8;

            return (Direction)index;
        }
    }
}
