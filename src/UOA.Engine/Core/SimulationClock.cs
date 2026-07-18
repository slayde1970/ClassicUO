// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace UOA.Core
{
    /// <summary>
    /// Fixed-step accumulator, decoupled from the variable render frame rate
    /// (GameController runs with IsFixedTimeStep = false). Survival timers,
    /// resource respawn, and the world clock all advance on this tick instead
    /// of render Delta, so their behavior doesn't depend on framerate.
    ///
    /// Uses the classic accumulate-and-drain pattern: each render frame adds
    /// its elapsed time to an accumulator and drains whole ticks out of it,
    /// so ticks stay a fixed size regardless of how choppy or fast rendering
    /// is. MaxTicksPerFrame guards against the "spiral of death" - if a stall
    /// (e.g. asset load, debugger break) leaves a huge backlog, excess time is
    /// dropped rather than the game trying to simulate hundreds of ticks in
    /// one frame (which would just cause another, worse stall).
    /// </summary>
    public sealed class SimulationClock
    {
        public const float TickRate = 20f;
        public const float FixedDelta = 1f / TickRate;

        private const int MaxTicksPerFrame = 5;

        private float _accumulator;

        public uint TickCount { get; private set; }

        public void Advance(float frameDelta, Action<float> onTick)
        {
            _accumulator += frameDelta;

            int ticksThisFrame = 0;

            while (_accumulator >= FixedDelta && ticksThisFrame < MaxTicksPerFrame)
            {
                onTick(FixedDelta);
                TickCount++;
                _accumulator -= FixedDelta;
                ticksThisFrame++;
            }

            if (ticksThisFrame == MaxTicksPerFrame)
            {
                _accumulator = 0f;
            }
        }
    }
}
