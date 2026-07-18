// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UOA.Core;

namespace TEF.World
{
    /// <summary>
    /// Full-screen ambient day/night darkening driven by WorldClock.TimeOfDay.
    /// Architected as a separate render target composited over the finished
    /// world scene via a multiply blend - not just a flat screen-space tint -
    /// so point light sources (torches/braziers, not implemented yet, see
    /// Design/next-steps.md #10) can later be additively drawn into the SAME
    /// target before compositing and "cut through" the darkness, matching
    /// ClassicUO.Client's approach (GameScene.PrepareLightsRendering's
    /// LightRenderTarget). This pass only clears the target to an ambient
    /// brightness - no light sources are drawn into it yet.
    /// </summary>
    public sealed class DayNightOverlay
    {
        // Darkest-midnight ambient brightness floor - 0 would be pitch
        // black; a floor keeps night navigable without any light sources,
        // similar to how UO's own nights are dim but never fully black.
        private const float MinBrightness = 0.30f;

        // Multiply blend (destination-color * source-color) - XNA/FNA has
        // no built-in BlendState.Multiply, so it's constructed explicitly.
        private static readonly BlendState MultiplyBlend = new()
        {
            ColorSourceBlend = Blend.DestinationColor,
            ColorDestinationBlend = Blend.Zero,
            AlphaSourceBlend = Blend.DestinationAlpha,
            AlphaDestinationBlend = Blend.Zero,
        };

        private RenderTarget2D _target;

        /// <summary>
        /// Prepares the darkness target for this frame (currently just a
        /// clear - future point lights would additively draw into it here).
        /// Must be called BEFORE the world is drawn to the backbuffer, not
        /// after: switching the active render target away and back (which
        /// this does) can silently discard the backbuffer's existing
        /// contents under RenderTargetUsage.DiscardContents (the default) -
        /// harmless here only because the backbuffer is still empty/just-
        /// cleared at this point in the frame. See Composite for the actual
        /// compositing draw, which must run after the world instead.
        /// </summary>
        public void Prepare(GraphicsDevice device, WorldClock clock, int viewportWidth, int viewportHeight)
        {
            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                return;
            }

            EnsureTarget(device, viewportWidth, viewportHeight);

            float brightness = ComputeBrightness(clock.TimeOfDay);

            device.SetRenderTarget(_target);
            device.Clear(new Color(brightness, brightness, brightness, 1f));
            device.SetRenderTarget(null);
        }

        /// <summary>
        /// Composites the already-Prepare()'d darkness target over whatever
        /// is currently on the backbuffer within <paramref name="viewport"/>,
        /// via a multiply blend. No render-target switching happens here
        /// (see Prepare) - call this after the world is drawn but before
        /// screen-space UI/HUD, so the darkening never affects UI.
        /// </summary>
        public void Composite(UltimaBatcher2D batcher, Rectangle viewport)
        {
            if (_target == null)
            {
                return;
            }

            batcher.Begin();
            batcher.SetBlendState(MultiplyBlend);
            batcher.Draw(_target, viewport, ShaderHueTranslator.GetHueVector(0), 0f);
            batcher.SetBlendState(null);
            batcher.End();
        }

        /// <summary>1 (full daylight) at noon, MinBrightness at midnight, smooth in between.</summary>
        private static float ComputeBrightness(float timeOfDay)
        {
            float wave = MathF.Cos((timeOfDay - 0.5f) * MathF.PI * 2f);
            float normalized = wave * 0.5f + 0.5f;

            return MinBrightness + (1f - MinBrightness) * normalized;
        }

        private void EnsureTarget(GraphicsDevice device, int width, int height)
        {
            if (_target != null && _target.Width == width && _target.Height == height)
            {
                return;
            }

            _target?.Dispose();
            _target = new RenderTarget2D(device, width, height);
        }
    }
}
