// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using UOA.Assets;
using UOA.Core;

namespace TEF.World
{
    /// <summary>
    /// Advances the shared per-graphic-id animation frame for animated
    /// statics (fountains, torches, lava, ...), matching ClassicUO.Client's
    /// AnimatedStaticsManager (Game/Managers/AnimatedStaticsManager.cs).
    /// This is deliberately a GLOBAL table keyed by graphic id, not
    /// per-instance/per-entity state - every map tile sharing a graphic id
    /// (e.g. every fountain on the map) animates in lockstep off the same
    /// offset, exactly like the real client, which stores the current
    /// offset on the shared art-file entry rather than per placed object.
    /// TileRenderer reads the current offset via <see cref="CurrentOffset"/>
    /// and adds it to the base graphic before calling Art.GetArt, mirroring
    /// View.DrawStaticAnimated's `graphic + index.AnimOffset` (hue/tiledata/
    /// name lookups stay keyed to the base graphic - only the art lookup
    /// uses the offset one).
    /// </summary>
    public sealed class AnimatedStatics
    {
        // Matches ClassicUO.Client's Constants.ITEM_EFFECT_ANIMATION_DELAY * 2.
        private const uint FrameDelayMs = 100;

        private struct AnimState
        {
            public ushort Graphic;
            public byte FrameIndex;
            public uint NextUpdateTicks;
        }

        private AnimState[] _states = Array.Empty<AnimState>();
        private sbyte[] _currentOffset = Array.Empty<sbyte>();

        // Gates Update() the same way the real client's Process() does -
        // skip entirely until the soonest-due animated graphic's next frame
        // time, rather than re-scanning every animated graphic every frame.
        private uint _nextProcessTicks;

        public void Initialize(GameAssets assets)
        {
            var staticData = assets.Files.TileData.StaticData;
            var states = new List<AnimState>();

            for (int i = 0; i < staticData.Length; i++)
            {
                if (staticData[i].IsAnimated)
                {
                    states.Add(new AnimState { Graphic = (ushort)i });
                }
            }

            _states = states.ToArray();
            _currentOffset = new sbyte[staticData.Length];
        }

        /// <summary>Current animation-frame offset for a static graphic id - 0 if that graphic isn't animated. Add to the base graphic before Art.GetArt.</summary>
        public sbyte CurrentOffset(ushort graphic)
        {
            return graphic < _currentOffset.Length ? _currentOffset[graphic] : (sbyte)0;
        }

        public unsafe void Update(GameAssets assets)
        {
            if (_states.Length == 0 || Time.Ticks < _nextProcessTicks)
            {
                return;
            }

            uint nextTime = Time.Ticks + 250;

            for (int i = 0; i < _states.Length; i++)
            {
                ref var state = ref _states[i];

                if (state.NextUpdateTicks < Time.Ticks)
                {
                    var frame = assets.Files.AnimData.CalculateCurrentGraphic(state.Graphic);

                    byte offset = state.FrameIndex;

                    state.NextUpdateTicks = frame.FrameInterval > 0
                        ? Time.Ticks + (uint)frame.FrameInterval * FrameDelayMs + 1
                        : Time.Ticks + FrameDelayMs;

                    if (offset < frame.FrameCount)
                    {
                        _currentOffset[state.Graphic] = frame.FrameData[offset++];
                    }

                    if (offset >= frame.FrameCount)
                    {
                        offset = 0;
                    }

                    state.FrameIndex = offset;
                }

                if (state.NextUpdateTicks < nextTime)
                {
                    nextTime = state.NextUpdateTicks;
                }
            }

            _nextProcessTicks = nextTime;
        }
    }
}
