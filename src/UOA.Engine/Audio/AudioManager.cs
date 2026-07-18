// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.IO.Audio;
using Microsoft.Xna.Framework.Audio;
using UOA.Assets;
using UOA.Core;

namespace UOA.Audio
{
    /// <summary>
    /// Trimmed port of ClassicUO's Game/Managers/AudioManager.cs. The original
    /// reads volume/mute state from a network Profile and factors in warmode
    /// music layering; TEF has neither, so this exposes plain volume knobs and
    /// a single music channel instead of the two-channel (peace/war) setup.
    /// </summary>
    public sealed class AudioManager
    {
        private const float SOUND_DELTA = 250f;

        private readonly LinkedList<UOSound> _currentSounds = new();
        private UOMusic _currentMusic;
        private bool _canPlayAudio = true;
        private GameAssets _assets;

        public bool EnableSound { get; set; } = true;
        public bool EnableMusic { get; set; } = true;
        public int SoundVolume { get; set; } = 100;
        public int MusicVolume { get; set; } = 100;
        public bool IsActive { get; set; } = true;

        public void Initialize(GameAssets assets)
        {
            _assets = assets;

            try
            {
                new DynamicSoundEffectInstance(0, AudioChannels.Stereo).Dispose();
            }
            catch (NoAudioHardwareException)
            {
                _canPlayAudio = false;
            }
        }

        public void PlaySound(int index)
        {
            if (!_canPlayAudio || !EnableSound)
            {
                return;
            }

            float volume = ResolveVolume(SoundVolume);
            if (volume <= 0f)
            {
                return;
            }

            var sound = (UOSound)_assets.Sounds.GetSound(index);
            if (sound != null && sound.Play(Time.Ticks, volume))
            {
                sound.CalculateByDistance = false;
                _currentSounds.AddLast(sound);
            }
        }

        /// <summary>
        /// Plays a positional sound attenuated by distance from a listener
        /// (typically the player). `viewRange` is the max tile distance at
        /// which the sound is audible - mirrors GameActions' world view range.
        /// </summary>
        public void PlaySoundWithDistance(int index, int listenerX, int listenerY, int emitterX, int emitterY, int viewRange)
        {
            if (!_canPlayAudio || !EnableSound)
            {
                return;
            }

            int distX = Math.Abs(emitterX - listenerX);
            int distY = Math.Abs(emitterY - listenerY);
            int distance = Math.Max(distX, distY);

            if (distance > viewRange)
            {
                return;
            }

            float volume = ResolveVolume(SoundVolume);
            float distanceFactor = distance >= 1 ? (volume / (viewRange + 1)) * distance : 0f;

            var sound = (UOSound)_assets.Sounds.GetSound(index);
            if (sound != null && sound.Play(Time.Ticks, volume, distanceFactor))
            {
                sound.CalculateByDistance = true;
                _currentSounds.AddLast(sound);
            }
        }

        public void PlayMusic(int musicIndex)
        {
            if (!_canPlayAudio)
            {
                return;
            }

            float volume = ResolveVolume(MusicVolume, isMusic: true);

            var music = (UOMusic)_assets.Sounds.GetMusic(musicIndex);
            if (music == null)
            {
                StopMusic();
                return;
            }

            if (music == _currentMusic)
            {
                return;
            }

            StopMusic();
            _currentMusic = music;
            _currentMusic.Play(Time.Ticks, volume);
        }

        public void StopMusic()
        {
            _currentMusic?.Stop();
            _currentMusic?.Dispose();
            _currentMusic = null;
        }

        public void StopSounds()
        {
            var node = _currentSounds.First;
            while (node != null)
            {
                var next = node.Next;
                node.Value.Stop();
                _currentSounds.Remove(node);
                node = next;
            }
        }

        public void Update()
        {
            if (!_canPlayAudio)
            {
                return;
            }

            if (_currentMusic != null)
            {
                _currentMusic.Volume = ResolveVolume(MusicVolume, isMusic: true);
                _currentMusic.Update();
            }

            var node = _currentSounds.First;
            while (node != null)
            {
                var next = node.Next;
                if (!node.Value.IsPlaying(Time.Ticks))
                {
                    node.Value.Stop();
                    _currentSounds.Remove(node);
                }
                node = next;
            }
        }

        private float ResolveVolume(int volumeSetting, bool isMusic = false)
        {
            if (!IsActive || (isMusic ? !EnableMusic : !EnableSound))
            {
                return 0f;
            }

            float volume = volumeSetting / SOUND_DELTA;
            return volume < -1f || volume > 1f ? 0f : volume;
        }
    }
}
