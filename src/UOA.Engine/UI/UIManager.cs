// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using UOA.Input;

namespace UOA.UI
{
    /// <summary>
    /// Owns the root-level controls (in Z order - last added draws on top),
    /// resolves per-frame screen positions, and routes hover/click to
    /// whichever control is topmost under the cursor. Draws in plain screen
    /// space (its own batcher.Begin()/End(), no camera matrix), same
    /// convention as DebugHud.
    ///
    /// Also exposes <see cref="IsMouseOverUI"/> so world-space input handling
    /// (e.g. WorldScene's click-to-harvest) can skip acting on a click that
    /// actually landed on a UI panel instead of the world behind it.
    /// </summary>
    public sealed class UIManager
    {
        private readonly List<Control> _roots = new();
        private Control _hovered;

        public bool IsMouseOverUI => _hovered != null;

        public void Add(Control control) => _roots.Add(control);
        public void Remove(Control control) => _roots.Remove(control);

        public void Update(InputManager input)
        {
            foreach (var root in _roots)
            {
                root.Update(0, 0);
            }

            var mouse = input.MousePosition;
            Control hit = null;

            for (int i = _roots.Count - 1; i >= 0 && hit == null; i--)
            {
                hit = HitTest(_roots[i], mouse);
            }

            if (hit != _hovered)
            {
                _hovered?.OnMouseLeave();
                _hovered = hit;
                _hovered?.OnMouseEnter();
            }

            if (_hovered != null)
            {
                if (input.IsMousePressed(MouseButton.Left))
                {
                    _hovered.OnClick(MouseButton.Left);
                }
                else if (input.IsMousePressed(MouseButton.Right))
                {
                    _hovered.OnClick(MouseButton.Right);
                }
            }
        }

        public void Draw(UltimaBatcher2D batcher)
        {
            if (_roots.Count == 0)
            {
                return;
            }

            batcher.Begin();

            foreach (var root in _roots)
            {
                root.Draw(batcher);
            }

            batcher.End();
        }

        /// <summary>Deepest (frontmost) descendant of <paramref name="control"/> containing <paramref name="point"/>, or the control itself if only its own bounds match, or null.</summary>
        private static Control HitTest(Control control, Point point)
        {
            if (!control.Visible)
            {
                return null;
            }

            for (int i = control.Children.Count - 1; i >= 0; i--)
            {
                var child = control.Children[i];
                if (!child.HitTestVisible)
                {
                    continue;
                }

                var hit = HitTest(child, point);
                if (hit != null)
                {
                    return hit;
                }
            }

            return control.Contains(point) ? control : null;
        }
    }
}
