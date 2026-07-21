// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;

namespace UOA.Audio
{
    /// <summary>
    /// Plays movement sounds (footsteps now; hoofbeats for a mounted player, or
    /// any other locomotion, later) on a gait-timed cadence. Modeled on
    /// ClassicUO's Mobile.ProcessFootstepsSound: a small set of step sounds is
    /// alternated for a left/right footfall feel, spaced by a walk/run interval.
    ///
    /// The locomotion "mode" is an opaque int key: the game registers one
    /// <see cref="Profile"/> per mode (on-foot, mounted, swimming, ...) up front
    /// and, each frame, tells <see cref="Update"/> which mode is active. Adding
    /// mounts later is therefore just registering a mount profile and passing
    /// its mode when the player is riding - no change to this controller or the
    /// per-frame call site. Engine-level so any prototype gets it; the UO sound
    /// ids stay in the game (this class hardcodes nothing UO-specific).
    /// </summary>
    public sealed class LocomotionSoundController
    {
        /// <summary>Sounds + timing for one locomotion mode.</summary>
        public readonly struct Profile
        {
            /// <summary>Sound ids played in round-robin order (typically two, for a left/right cadence).</summary>
            public readonly ushort[] StepSounds;

            /// <summary>Seconds between steps at walking speed.</summary>
            public readonly float WalkInterval;

            /// <summary>Seconds between steps at running/sprinting speed.</summary>
            public readonly float RunInterval;

            public Profile(ushort[] stepSounds, float walkInterval, float runInterval)
            {
                StepSounds = stepSounds;
                WalkInterval = walkInterval;
                RunInterval = runInterval;
            }
        }

        private readonly AudioManager _audio;
        private readonly Dictionary<int, Profile> _profiles = new();

        // Counts down to when the next step is allowed. Decremented every frame
        // (even while idle) so a step after a long stand plays promptly, but two
        // steps never land closer than the gait interval - even across brief
        // stop/start taps. Mirrors ClassicUO's absolute LastStepSoundTime gate.
        private float _cooldown;
        private int _stepIndex;

        /// <summary>Master on/off (the game wires this to a config toggle).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Volume multiplier (0..1) layered on top of the global sound volume,
        /// so movement sounds can sit quieter in the mix without dragging every
        /// other effect down with them. 1 = same as any other sound.
        /// </summary>
        public float Volume { get; set; } = 1f;

        public LocomotionSoundController(AudioManager audio)
        {
            _audio = audio;
        }

        /// <summary>Registers (or replaces) the sound profile for a locomotion mode.</summary>
        public void Register(int mode, Profile profile) => _profiles[mode] = profile;

        /// <summary>
        /// Advances the cadence and plays a step sound when due. Call once per
        /// frame with the player's current movement state.
        /// </summary>
        /// <param name="deltaSeconds">Frame time.</param>
        /// <param name="moving">Whether the player is currently moving.</param>
        /// <param name="running">Whether moving at run/sprint speed (uses the shorter interval).</param>
        /// <param name="mode">Active locomotion mode key (must have been registered).</param>
        public void Update(float deltaSeconds, bool moving, bool running, int mode)
        {
            if (_cooldown > 0f)
            {
                _cooldown -= deltaSeconds;
            }

            if (!Enabled || !moving)
            {
                return;
            }

            if (!_profiles.TryGetValue(mode, out var profile) || profile.StepSounds.Length == 0)
            {
                return;
            }

            if (_cooldown > 0f)
            {
                return;
            }

            _audio.PlaySound(profile.StepSounds[_stepIndex], Volume);
            _stepIndex = (_stepIndex + 1) % profile.StepSounds.Length;
            _cooldown = running ? profile.RunInterval : profile.WalkInterval;
        }
    }
}
