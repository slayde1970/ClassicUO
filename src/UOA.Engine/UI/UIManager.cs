// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UOA.Input;

namespace UOA.UI
{
    /// <summary>
    /// Owns the root-level controls ("gumps") in Z order - last in the list
    /// draws on top and is hit-tested first. Resolves per-frame screen
    /// positions and routes hover/click to whichever control is topmost under
    /// the cursor. Draws in plain screen space (its own batcher.Begin()/End(),
    /// no camera matrix), same convention as DebugHud.
    ///
    /// Tier 4.5 window management, modeled on ClassicUO's UIManager: named/typed
    /// gump lookup (<see cref="GetByName"/>/<see cref="GetGump{T}"/>), Z-order
    /// control (<see cref="BringToFront"/>, automatic on drag-grab), a modal
    /// stack (a modal root blocks input to everything below it), open/close
    /// lifecycle callbacks (<see cref="Control.OnOpened"/>/<see cref="Control.OnClosed"/>),
    /// and dragging of controls flagged <see cref="Control.Draggable"/>.
    ///
    /// Also exposes <see cref="IsMouseOverUI"/> so world-space input handling
    /// (e.g. WorldScene's click-to-harvest) can skip a click that actually
    /// landed on a UI panel instead of the world behind it.
    /// </summary>
    public sealed class UIManager
    {
        // Editing keys forwarded to the focused control each frame (typed
        // printable chars come separately via InputManager.TypedChars).
        private static readonly Keys[] EditingKeys =
        {
            Keys.Back, Keys.Delete, Keys.Left, Keys.Right, Keys.Home, Keys.End, Keys.Enter, Keys.Tab,
        };

        private readonly List<Control> _roots = new();
        private Control _hovered;
        private Control _focused;

        private Control _dragging;
        private Point _dragLast;

        public bool IsMouseOverUI => _hovered != null;

        /// <summary>The control currently holding keyboard focus, or null.</summary>
        public Control Focused => _focused;

        /// <summary>Gives keyboard focus to a control (fires OnBlur/OnFocus). Pass null to clear focus.</summary>
        public void SetFocus(Control control)
        {
            if (_focused == control)
            {
                return;
            }

            if (_focused != null)
            {
                _focused.IsFocused = false;
                _focused.OnBlur();
            }

            _focused = control;

            if (_focused != null)
            {
                _focused.IsFocused = true;
                _focused.OnFocus();
            }
        }

        /// <summary>True while a control is being dragged - callers can suppress world interaction until the drag ends.</summary>
        public bool IsDragging => _dragging != null;

        /// <summary>Adds a root gump (drawn/hit-tested on top of existing ones) and fires its OnOpened.</summary>
        public void Add(Control control)
        {
            _roots.Add(control);
            control.OnOpened();
        }

        /// <summary>Removes a root gump and fires its OnClosed. No-op if it isn't currently a root.</summary>
        public void Remove(Control control)
        {
            if (_roots.Remove(control))
            {
                if (_dragging == control)
                {
                    _dragging = null;
                }

                if (_hovered == control)
                {
                    _hovered = null;
                }

                // Clear focus if the focused control lived in this gump.
                if (_focused != null && ContainsInTree(control, _focused))
                {
                    SetFocus(null);
                }

                control.OnClosed();
            }
        }

        private static bool ContainsInTree(Control root, Control target)
        {
            if (root == target)
            {
                return true;
            }

            foreach (var child in root.Children)
            {
                if (ContainsInTree(child, target))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Alias for <see cref="Remove"/> reading naturally as "close this gump".</summary>
        public void Close(Control control) => Remove(control);

        /// <summary>Moves a root to the top of the Z order (drawn last, hit-tested first). No-op if it isn't a root.</summary>
        public void BringToFront(Control root)
        {
            if (_roots.Remove(root))
            {
                _roots.Add(root);
            }
        }

        /// <summary>First control anywhere in the tree with the given Name, or null.</summary>
        public Control GetByName(string name)
        {
            foreach (var root in _roots)
            {
                var found = root.FindByName(name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>Topmost root gump assignable to <typeparamref name="T"/>, or null.</summary>
        public T GetGump<T>() where T : Control
        {
            for (int i = _roots.Count - 1; i >= 0; i--)
            {
                if (_roots[i] is T gump)
                {
                    return gump;
                }
            }

            return null;
        }

        public void Update(InputManager input)
        {
            foreach (var root in _roots)
            {
                root.Update(0, 0);
            }

            var mouse = input.MousePosition;

            // A drag in progress owns the mouse until the button releases -
            // no hover/click routing while dragging.
            if (_dragging != null)
            {
                if (input.IsMouseDown(MouseButton.Left))
                {
                    _dragging.X += mouse.X - _dragLast.X;
                    _dragging.Y += mouse.Y - _dragLast.Y;
                    _dragLast = mouse;
                    return;
                }

                _dragging = null;
            }

            // Modal stack: if any visible root is modal, only the topmost such
            // root (and anything above it) is interactive; everything below is
            // blocked - including clicks that miss the modal entirely.
            int floor = 0;
            for (int i = _roots.Count - 1; i >= 0; i--)
            {
                if (_roots[i].Visible && _roots[i].IsModal)
                {
                    floor = i;
                    break;
                }
            }

            Control hit = null;
            for (int i = _roots.Count - 1; i >= floor && hit == null; i--)
            {
                hit = HitTest(_roots[i], mouse);
            }

            if (hit != _hovered)
            {
                _hovered?.OnMouseLeave();
                _hovered = hit;
                _hovered?.OnMouseEnter();
            }

            // Focus follows a left-click: a focusable control gains focus; any
            // other left-click clears it - including one that misses the UI
            // entirely (_hovered == null), so clicking the world defocuses a
            // text box.
            if (input.IsMousePressed(MouseButton.Left))
            {
                SetFocus(_hovered != null && _hovered.Focusable ? _hovered : null);
            }

            if (_hovered != null)
            {
                if (input.IsMousePressed(MouseButton.Left))
                {
                    // Grabbing a draggable control starts a drag and raises it;
                    // otherwise it's a normal click. Because only root gumps are
                    // typically flagged Draggable (buttons/labels aren't),
                    // clicking a button still clicks while grabbing the panel
                    // body drags.
                    if (_hovered.Draggable)
                    {
                        _dragging = _hovered;
                        _dragLast = mouse;
                        BringToFront(_hovered);
                    }
                    else
                    {
                        _hovered.OnClick(MouseButton.Left);
                    }
                }
                else if (input.IsMousePressed(MouseButton.Right))
                {
                    _hovered.OnClick(MouseButton.Right);
                }
            }

            // Route keyboard to the focused control: this frame's typed
            // characters, then the editing keys that are pressed.
            if (_focused != null)
            {
                var typed = input.TypedChars;
                for (int i = 0; i < typed.Count; i++)
                {
                    _focused.OnTextInput(typed[i]);
                }

                foreach (var key in EditingKeys)
                {
                    if (input.IsKeyPressed(key))
                    {
                        _focused.OnKeyDown(key);
                    }
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
