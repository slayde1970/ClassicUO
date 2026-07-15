// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using TEF.Input;

namespace TEF.UI
{
    /// <summary>
    /// Base of the lightweight control/gump system (Tier 2 #5). Position is
    /// LOCAL to the parent (X, Y are an offset, not a screen coordinate);
    /// <see cref="ScreenX"/>/<see cref="ScreenY"/> are resolved each frame in
    /// <see cref="Update"/> by walking down from whatever root
    /// <see cref="UIManager"/> owns this control. No dragging/resizing/
    /// layout engine - deliberately just enough to compose panels, labels,
    /// and buttons; add those capabilities if/when a concrete feature
    /// actually needs them rather than up front.
    /// </summary>
    public class Control
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public bool Visible = true;

        // False for purely decorative controls (e.g. Label) so hovering over
        // them doesn't steal hover/click from an interactive parent (e.g. a
        // Button with a text label drawn on top of it) - see UIManager.HitTest.
        public bool HitTestVisible = true;

        public int ScreenX { get; private set; }
        public int ScreenY { get; private set; }

        public readonly List<Control> Children = new();

        /// <summary>
        /// Resolves ScreenX/ScreenY from the parent's screen position and
        /// recurses into children. Override to also update per-frame state
        /// (e.g. auto-sizing a label from its current text), but call the
        /// base implementation so children still get positioned.
        /// </summary>
        public virtual void Update(int parentScreenX, int parentScreenY)
        {
            ScreenX = parentScreenX + X;
            ScreenY = parentScreenY + Y;

            foreach (var child in Children)
            {
                child.Update(ScreenX, ScreenY);
            }
        }

        public virtual void Draw(UltimaBatcher2D batcher)
        {
            if (!Visible)
            {
                return;
            }

            foreach (var child in Children)
            {
                child.Draw(batcher);
            }
        }

        /// <summary>True if the given screen-space point falls within this control's own bounds (not its children - see UIManager.HitTest for the recursive version).</summary>
        public bool Contains(Point screenPoint)
        {
            return Visible
                && screenPoint.X >= ScreenX && screenPoint.X < ScreenX + Width
                && screenPoint.Y >= ScreenY && screenPoint.Y < ScreenY + Height;
        }

        public virtual void OnMouseEnter()
        {
        }

        public virtual void OnMouseLeave()
        {
        }

        public virtual void OnClick(MouseButton button)
        {
        }
    }
}
