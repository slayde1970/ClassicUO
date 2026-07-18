// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using ClassicUO.Renderer.Animations;
using ClassicUO.Renderer.Arts;
using ClassicUO.Renderer.Gumps;
using ClassicUO.Renderer.Lights;
using ClassicUO.Renderer.Sounds;
using ClassicUO.Renderer.Texmaps;
using ClassicUO.Utility;
using Microsoft.Xna.Framework.Graphics;
using UOA.Core;

namespace UOA.Assets
{
    /// <summary>
    /// Owns every loaded UO data file and the GPU-side wrappers around it
    /// (art, animations, gumps, hues, sounds, ...). Modeled directly on
    /// ClassicUO's internal `UltimaOnline` class (see ClassicUO.Client/Client.cs)
    /// but trimmed of anything tied to network play: no World, no GameCursor,
    /// no protocol negotiation.
    /// </summary>
    public sealed class GameAssets : IDisposable
    {
        private const int HUE_TEXTURE_WIDTH = 512;
        private const int HUE_TEXTURE_HEIGHT = 1024;
        private const int LIGHT_TEXTURE_WIDTH = 32;
        private const int LIGHT_TEXTURE_HEIGHT = 63;

        public UOFileManager Files { get; private set; }
        public ClientVersion Version { get; private set; }

        public Animations Animations { get; private set; }
        public Art Art { get; private set; }
        public Gump Gumps { get; private set; }
        public Texmap Texmaps { get; private set; }
        public Light Lights { get; private set; }
        public Sound Sounds { get; private set; }
        public FontGlyphAtlas GlyphAtlas { get; private set; }

        private readonly HashSet<int> _loadedMaps = new();

        public unsafe void Load(GraphicsDevice device, EngineSettings settings)
        {
            if (!Directory.Exists(settings.UltimaOnlineDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"UO directory not found: '{settings.UltimaOnlineDirectory}'"
                );
            }

            if (!ClientVersionHelper.IsClientVersionValid(settings.ClientVersion, out var version))
            {
                if (!ClientVersionHelper.TryParseFromFile(
                        Path.Combine(settings.UltimaOnlineDirectory, "client.exe"),
                        out var parsedText)
                    || !ClientVersionHelper.IsClientVersionValid(parsedText, out version))
                {
                    throw new InvalidOperationException(
                        $"Could not determine a valid client version from '{settings.ClientVersion}' or client.exe"
                    );
                }
            }

            Version = version;
            Files = new UOFileManager(version, settings.UltimaOnlineDirectory);
            Files.Load(settings.UseVerdata, settings.Language);

            // Hue/light lookup textures live in texture slots 1 and 2 - every
            // UO shader (art, gumps, land, statics...) samples them there.
            var hueSamplers = new Texture2D[2];
            hueSamplers[0] = new Texture2D(device, HUE_TEXTURE_WIDTH, HUE_TEXTURE_HEIGHT);
            hueSamplers[1] = new Texture2D(device, LIGHT_TEXTURE_WIDTH, LIGHT_TEXTURE_HEIGHT);

            var buffer = new uint[Math.Max(
                LIGHT_TEXTURE_WIDTH * LIGHT_TEXTURE_HEIGHT,
                HUE_TEXTURE_WIDTH * HUE_TEXTURE_HEIGHT
            )];

            fixed (uint* ptr = buffer)
            {
                Files.Hues.CreateShaderColors(buffer);
                hueSamplers[0].SetDataPointerEXT(0, null, (IntPtr)ptr, HUE_TEXTURE_WIDTH * HUE_TEXTURE_HEIGHT * sizeof(uint));

                LightColors.CreateLightTextures(buffer, LIGHT_TEXTURE_HEIGHT);
                hueSamplers[1].SetDataPointerEXT(0, null, (IntPtr)ptr, LIGHT_TEXTURE_WIDTH * LIGHT_TEXTURE_HEIGHT * sizeof(uint));
            }

            device.Textures[1] = hueSamplers[0];
            device.Textures[2] = hueSamplers[1];

            Animations = new Animations(Files.Animations, device);
            Art = new Art(Files.Arts, Files.Hues, device);
            Gumps = new Gump(Files.Gumps, device);
            Texmaps = new Texmap(Files.Texmaps, device);
            Lights = new Light(Files.Lights, device);
            Sounds = new Sound(Files.Sounds);
            GlyphAtlas = new FontGlyphAtlas(Files.Fonts, device);
        }

        /// <summary>
        /// UOFileManager.Load only opens the raw map/statics files - the
        /// per-block index (UOFileManager.Maps.BlockData, used by GetIndex)
        /// isn't populated until MapLoader.LoadMap(index) runs, which
        /// ClassicUO.Client only calls once World.Map is assigned after
        /// login (see Game/World.cs). TileRenderer calls this the first time
        /// it touches a given map index.
        /// </summary>
        public void EnsureMapLoaded(int mapIndex)
        {
            if (_loadedMaps.Add(mapIndex))
            {
                Files.Maps.LoadMap(mapIndex);
            }
        }

        public void Dispose()
        {
            Files?.Dispose();
        }
    }
}
