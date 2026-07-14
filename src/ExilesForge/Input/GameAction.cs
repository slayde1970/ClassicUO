// SPDX-License-Identifier: BSD-2-Clause

namespace TEF.Input
{
    /// <summary>
    /// Logical actions the player can perform, decoupled from any specific key
    /// or button. Scenes/gameplay code should ask InputManager about actions,
    /// never raw Keys/Buttons, so rebinding never touches gameplay code.
    /// </summary>
    public enum GameAction
    {
        MoveForward,
        MoveBack,
        StrafeLeft,
        StrafeRight,
        Sprint,
        Jump,
        Interact,
        PrimaryAction,   // attack / use tool
        SecondaryAction, // block / aim
        OpenInventory,
        OpenCraftingMenu,
        OpenCharacterMenu,
        QuickSave,
        ToggleMenu,
    }
}
