// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace TEF.Input
{
    public enum MouseButton
    {
        Left,
        Right,
        Middle
    }

    /// <summary>
    /// Polling-based input layer. ClassicUO drives input off an SDL event
    /// filter (see ClassicUO.Client/GameController.cs HandleSdlEvent) because
    /// it needs precise double-click timing and raw text input for gumps.
    /// TEF doesn't have a UI/text layer yet, so plain per-frame polling via
    /// FNA's Keyboard/Mouse state - simpler, and enough to drive an action map.
    /// </summary>
    public sealed class InputManager
    {
        private readonly Dictionary<GameAction, Keys> _bindings = new()
        {
            [GameAction.MoveForward] = Keys.W,
            [GameAction.MoveBack] = Keys.S,
            [GameAction.StrafeLeft] = Keys.A,
            [GameAction.StrafeRight] = Keys.D,
            [GameAction.Sprint] = Keys.LeftShift,
            [GameAction.Jump] = Keys.Space,
            [GameAction.Interact] = Keys.E,
            [GameAction.OpenInventory] = Keys.I,
            [GameAction.OpenCraftingMenu] = Keys.C,
            [GameAction.OpenCharacterMenu] = Keys.Tab,
            [GameAction.QuickSave] = Keys.F5,
            [GameAction.ToggleMenu] = Keys.Escape,
        };

        private KeyboardState _keyboard, _prevKeyboard;
        private MouseState _mouse, _prevMouse;

        public Point MousePosition => new(_mouse.X, _mouse.Y);
        public Point MouseDelta => new(_mouse.X - _prevMouse.X, _mouse.Y - _prevMouse.Y);
        public int ScrollDelta => _mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

        public void Rebind(GameAction action, Keys key) => _bindings[action] = key;

        public void Update()
        {
            _prevKeyboard = _keyboard;
            _prevMouse = _mouse;

            _keyboard = Keyboard.GetState();
            _mouse = Mouse.GetState();
        }

        public bool IsActionDown(GameAction action) =>
            _bindings.TryGetValue(action, out var key) && _keyboard.IsKeyDown(key);

        public bool IsActionPressed(GameAction action) =>
            _bindings.TryGetValue(action, out var key)
            && _keyboard.IsKeyDown(key)
            && !_prevKeyboard.IsKeyDown(key);

        public bool IsActionReleased(GameAction action) =>
            _bindings.TryGetValue(action, out var key)
            && !_keyboard.IsKeyDown(key)
            && _prevKeyboard.IsKeyDown(key);

        public bool IsMouseDown(MouseButton button) => Resolve(_mouse, button) == ButtonState.Pressed;

        public bool IsMousePressed(MouseButton button) =>
            Resolve(_mouse, button) == ButtonState.Pressed && Resolve(_prevMouse, button) == ButtonState.Released;

        private static ButtonState Resolve(in MouseState state, MouseButton button) => button switch
        {
            MouseButton.Left => state.LeftButton,
            MouseButton.Right => state.RightButton,
            MouseButton.Middle => state.MiddleButton,
            _ => ButtonState.Released
        };
    }
}
