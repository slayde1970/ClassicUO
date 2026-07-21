// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UOA.Core;

namespace UOA.World
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
        /// <summary>
        /// The colored ambient-tint curve sampled each frame (Tier 4.8 #32):
        /// warm sunrise/sunset, cool blue moonlight, neutral white midday. The
        /// game assigns one built from daynight.json; the built-in default is
        /// used until then. Its darkest (night) stops are the ambient floor -
        /// never pitch black, so night stays navigable without light sources.
        /// </summary>
        public DayNightGradient Gradient { get; set; } = DayNightGradient.Default;

        // The displayed tint eases toward the freshly-sampled target each frame
        // rather than snapping, so a sudden time change (the F10 debug jump, or
        // any large clock step) cross-fades smoothly instead of popping;
        // continuous play was already smooth via the gradient's own lerp. Higher
        // rate = snappier, lower = a longer, gentler cross-fade (~1s at 3).
        private const float TransitionRate = 3f;
        private Vector3 _currentTint;
        private bool _tintInitialized;

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

            Vector3 target = Gradient.Sample(clock.TimeOfDay).ToVector3();

            if (!_tintInitialized)
            {
                // First frame: adopt the target directly so we don't fade in
                // from black on load.
                _currentTint = target;
                _tintInitialized = true;
            }
            else
            {
                // Frame-rate-independent exponential ease toward the target.
                float t = 1f - MathF.Exp(-TransitionRate * Time.Delta);
                _currentTint = Vector3.Lerp(_currentTint, target, t);
            }

            device.SetRenderTarget(_target);
            device.Clear(new Color(_currentTint));
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
