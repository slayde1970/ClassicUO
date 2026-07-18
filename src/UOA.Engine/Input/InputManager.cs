// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace UOA.Input
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
    /// UOA has no UI/text layer needing that yet, so plain per-frame polling
    /// via FNA's Keyboard/Mouse state - simpler, and enough to drive an
    /// action map.
    ///
    /// Actions are keyed by a plain <c>int</c>, not a game-specific enum, so
    /// this stays engine-agnostic (Tier 4.5): each game defines its own action
    /// enum and registers bindings via <see cref="Rebind"/>, casting the enum
    /// to int. A thin game-side extension (e.g. TEF.Input.InputActions) gives
    /// call sites the typed <c>IsActionDown(MyAction)</c> convenience back.
    /// </summary>
    /// <summary>A key, plus an optional required modifier (e.g. Ctrl+L) - implicitly convertible from a bare Keys so single-key bindings need no change at the call site.</summary>
    public readonly struct KeyBinding
    {
        public readonly Keys Key;
        public readonly Keys? Modifier;

        public KeyBinding(Keys key, Keys? modifier = null)
        {
            Key = key;
            Modifier = modifier;
        }

        public static implicit operator KeyBinding(Keys key) => new(key);
    }

    public sealed class InputManager
    {
        // Keyed by (int)someGameActionEnum - the engine never names the actions
        // itself. Starts empty; the game installs its bindings after
        // construction (see the game's InputActions.InstallDefaults).
        private readonly Dictionary<int, KeyBinding> _bindings = new();

        private KeyboardState _keyboard, _prevKeyboard;
        private MouseState _mouse, _prevMouse;

        public Point MousePosition => new(_mouse.X, _mouse.Y);
        public Point MouseDelta => new(_mouse.X - _prevMouse.X, _mouse.Y - _prevMouse.Y);
        public int ScrollDelta => _mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

        public void Rebind(int action, KeyBinding binding) => _bindings[action] = binding;

        public void Update()
        {
            _prevKeyboard = _keyboard;
            _prevMouse = _mouse;

            _keyboard = Keyboard.GetState();
            _mouse = Mouse.GetState();
        }

        private static bool IsBindingDown(in KeyBinding binding, in KeyboardState state) =>
            state.IsKeyDown(binding.Key) && (binding.Modifier is not Keys modifier || state.IsKeyDown(modifier));

        public bool IsActionDown(int action) =>
            _bindings.TryGetValue(action, out var binding) && IsBindingDown(binding, _keyboard);

        public bool IsActionPressed(int action) =>
            _bindings.TryGetValue(action, out var binding)
            && IsBindingDown(binding, _keyboard)
            && !IsBindingDown(binding, _prevKeyboard);

        public bool IsActionReleased(int action) =>
            _bindings.TryGetValue(action, out var binding)
            && !IsBindingDown(binding, _keyboard)
            && IsBindingDown(binding, _prevKeyboard);

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
