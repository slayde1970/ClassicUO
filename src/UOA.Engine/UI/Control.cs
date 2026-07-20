// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UOA.Input;

namespace UOA.UI
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

        // Optional lookup key (UIManager.GetByName). Null/empty = anonymous.
        public string Name;

        // When a root control is modal, UIManager blocks hover/click to every
        // root below the topmost modal one (Tier 4.5) - e.g. a confirm dialog
        // that must be answered before the UI underneath is usable again.
        public bool IsModal;

        // When true and this is the control directly grabbed, UIManager drags
        // it by the mouse (moving its X/Y). Typically set on a root gump so
        // it can be repositioned; buttons/labels leave it false so clicking
        // them still clicks rather than drags.
        public bool Draggable;

        // False for purely decorative controls (e.g. Label) so hovering over
        // them doesn't steal hover/click from an interactive parent (e.g. a
        // Button with a text label drawn on top of it) - see UIManager.HitTest.
        public bool HitTestVisible = true;

        // Keyboard focus (Tier 4.7). Focusable controls (e.g. TextBox) receive
        // typed characters + editing keys while focused. UIManager focuses a
        // focusable control when it's left-clicked and clears focus otherwise.
        public bool Focusable;

        /// <summary>Whether this control currently holds keyboard focus (set by UIManager).</summary>
        public bool IsFocused { get; internal set; }

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

        /// <summary>Called when this control gains keyboard focus (Tier 4.7).</summary>
        public virtual void OnFocus()
        {
        }

        /// <summary>Called when this control loses keyboard focus.</summary>
        public virtual void OnBlur()
        {
        }

        /// <summary>A printable character was typed while this control is focused.</summary>
        public virtual void OnTextInput(char c)
        {
        }

        /// <summary>An editing key (backspace/enter/arrows/...) was pressed while this control is focused.</summary>
        public virtual void OnKeyDown(Keys key)
        {
        }

        /// <summary>Called by UIManager when this control is added as a root (gump opened). Override to run open-time logic.</summary>
        public virtual void OnOpened()
        {
        }

        /// <summary>Called by UIManager when this control is removed as a root (gump closed). Override to run cleanup.</summary>
        public virtual void OnClosed()
        {
        }

        /// <summary>Depth-first search of this subtree for a control with the given <see cref="Name"/> (this control included). Null if none.</summary>
        public Control FindByName(string name)
        {
            if (Name == name)
            {
                return this;
            }

            foreach (var child in Children)
            {
                var found = child.FindByName(name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
