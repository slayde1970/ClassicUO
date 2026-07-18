// SPDX-License-Identifier: BSD-2-Clause

namespace UOA.Core
{
    public static class Time
    {
        public static uint Ticks;
        public static float Delta;

        /// <summary>Frames rendered in the last completed 1-second window - updated by GameController.Draw, matching ClassicUO's CUOEnviroment.CurrentRefreshRate pattern.</summary>
        public static int Fps;

        /// <summary>Total fixed simulation ticks elapsed (see SimulationClock) - mirrored here each frame for anything that just wants a read, not a subscription.</summary>
        public static uint SimTicks;
    }
}
