// SPDX-License-Identifier: BSD-2-Clause

using Microsoft.Xna.Framework.Input;
using UOA.Input;

namespace TEF.Input
{
    /// <summary>
    /// Game-side bridge between TEF's <see cref="GameAction"/> enum and the
    /// engine's action-agnostic <see cref="InputManager"/> (Tier 4.5). The
    /// engine keys bindings by plain int so it never depends on a specific
    /// game's action set; these extensions cast <see cref="GameAction"/> to
    /// int, restoring the typed <c>input.IsActionDown(GameAction.X)</c>
    /// call-site convenience the whole codebase already uses. Because
    /// <see cref="GameAction"/> does not implicitly convert to int, these
    /// overloads bind ahead of the engine's <c>int</c> ones with no ambiguity.
    /// </summary>
    public static class InputActions
    {
        /// <summary>
        /// Installs TEF's default key bindings onto a freshly-constructed
        /// InputManager (previously the InputManager's own field initializer,
        /// now game-owned). Call once at startup.
        /// </summary>
        public static void InstallDefaults(this InputManager input)
        {
            input.Rebind(GameAction.MoveForward, Keys.W);
            input.Rebind(GameAction.MoveBack, Keys.S);
            input.Rebind(GameAction.StrafeLeft, Keys.A);
            input.Rebind(GameAction.StrafeRight, Keys.D);
            input.Rebind(GameAction.Sprint, Keys.LeftShift);
            input.Rebind(GameAction.Jump, Keys.Space);
            input.Rebind(GameAction.Interact, Keys.E);
            input.Rebind(GameAction.OpenInventory, Keys.I);
            input.Rebind(GameAction.OpenCraftingMenu, Keys.C);
            input.Rebind(GameAction.OpenCharacterMenu, Keys.Tab);
            input.Rebind(GameAction.QuickSave, Keys.F5);
            input.Rebind(GameAction.ToggleStatics, Keys.F6);
            input.Rebind(GameAction.ToggleMapStatics, Keys.F7);
            input.Rebind(GameAction.ToggleDebugInfo, Keys.F8);
            input.Rebind(GameAction.ToggleFpsCounter, Keys.F9);
            input.Rebind(GameAction.ToggleMenu, Keys.Escape);
            input.Rebind(GameAction.ToggleTerrainLighting, new KeyBinding(Keys.L, Keys.LeftControl));
            input.Rebind(GameAction.DebugAdvanceTime, Keys.F10);
            input.Rebind(GameAction.DebugToggleDepthTest, Keys.F11);
        }

        public static void Rebind(this InputManager input, GameAction action, KeyBinding binding) =>
            input.Rebind((int)action, binding);

        public static bool IsActionDown(this InputManager input, GameAction action) =>
            input.IsActionDown((int)action);

        public static bool IsActionPressed(this InputManager input, GameAction action) =>
            input.IsActionPressed((int)action);

        public static bool IsActionReleased(this InputManager input, GameAction action) =>
            input.IsActionReleased((int)action);
    }
}
