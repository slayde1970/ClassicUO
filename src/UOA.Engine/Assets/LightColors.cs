// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;

namespace UOA.Assets
{
    /// <summary>
    /// Trimmed port of ClassicUO's Game/Data/LightColors.cs. That version reads
    /// user-editable lightshaders.txt/lights.txt overrides from disk; TEF only
    /// needs the baked-in default palette to populate the light lookup texture
    /// (texture slot 2, see GameAssets.Load), so the file-override machinery
    /// was dropped. Reintroduce it here if per-item light recoloring is needed.
    /// </summary>
    internal static class LightColors
    {
        private enum Curve
        {
            Standard,
            A, // small
            B, // very small and dim
            C, // full, flat
            D, // medium dim
            E  // halo
        }

        private readonly struct ShaderData
        {
            public readonly uint RGB;
            public readonly Curve Red;
            public readonly Curve Green;
            public readonly Curve Blue;

            public ShaderData(uint rgb, Curve red = Curve.Standard, Curve green = Curve.Standard, Curve blue = Curve.Standard)
            {
                RGB = rgb;
                Red = red;
                Green = green;
                Blue = blue;
            }
        }

        private static readonly byte[][] _curveTables =
        {
            new byte[32] { 0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31 },
            new byte[32] { 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,2,3,4,6,8,10,12,14,16,18,20,22,24,26,28 },
            new byte[32] { 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,2,3,4,5,6,7,8 },
            new byte[32] { 0,1,2,4,6,8,11,14,17,20,23,26,29,30,31,31,31,31,31,31,31,31,31,31,31,31,31,31,31,31,31,31 },
            new byte[32] { 0,0,0,0,0,0,0,0,1,1,2,2,3,3,4,4,5,6,7,8,9,10,11,12,13,15,17,19,21,23,25,27 },
            new byte[32] { 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,5,10,15,20,25,30,30,18,18,18,18,18,18,18 },
        };

        public static void CreateLightTextures(uint[] buffer, int count)
        {
            var shaders = new Dictionary<ushort, ShaderData>();

            for (ushort i = 1; i <= count; i++)
            {
                shaders[i] = new ShaderData(0xFF_FF_FF);
            }

            shaders[1] = new ShaderData(0x00_FF_00, green: Curve.A);
            shaders[2] = new ShaderData(0x7F_7F_FF);
            shaders[6] = new ShaderData(0xFF_00_FF, blue: Curve.A, red: Curve.B);
            shaders[10] = new ShaderData(0x3F_3F_FF);
            shaders[20] = new ShaderData(0x00_FF_00);
            shaders[30] = new ShaderData(0xFF_7F_00, green: Curve.C, red: Curve.C);
            shaders[31] = new ShaderData(0xFF_7F_00, green: Curve.A, red: Curve.A);
            shaders[32] = new ShaderData(0xFF_00_FF);
            shaders[40] = new ShaderData(0xFF_00_00);
            shaders[50] = new ShaderData(0xFF_FF_00);
            shaders[60] = new ShaderData(0xFF_FF_00, red: Curve.A, green: Curve.A);
            shaders[61] = new ShaderData(0xFF_FF_00, red: Curve.D, green: Curve.D);
            shaders[62] = new ShaderData(0xFF_FF_FF, Curve.D, Curve.D, Curve.D);
            shaders[63] = new ShaderData(0xFF_FF_FF, Curve.E, Curve.E, Curve.E);

            foreach (var entry in shaders)
            {
                for (uint i = 0; i < 32; i++)
                {
                    uint r = (entry.Value.RGB & 0xFF_00_00) >> 16;
                    uint g = (entry.Value.RGB & 0x00_FF_00) >> 8;
                    uint b = entry.Value.RGB & 0x00_00_FF;

                    buffer[32 * (entry.Key - 1) + i] = 0xFF_00_00_00 |
                        ((_curveTables[(int)entry.Value.Blue][i] * b) / 31) << 16 |
                        ((_curveTables[(int)entry.Value.Green][i] * g) / 31) << 8 |
                        ((_curveTables[(int)entry.Value.Red][i] * r) / 31);
                }
            }
        }
    }
}
