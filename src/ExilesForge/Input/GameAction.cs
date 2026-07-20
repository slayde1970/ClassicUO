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
        ToggleStatics, // debug: show/hide static tiles
        ToggleMapStatics, // debug: show/hide MAP statics only - entities always draw regardless
        ToggleDebugInfo, // debug: show/hide the tile/entity-under-cursor HUD text
        ToggleFpsCounter, // debug: show/hide the FPS counter
        ToggleTerrainLighting, // debug: toggle stretched-land directional shading on/off (Brightlight) for comparison
        DebugAdvanceTime, // debug: jump the world clock forward 1 in-game hour, to check day/night without waiting
        DebugToggleDepthTest, // debug: A/B toggle for the Tier 4 #13 GPU depth-buffer work
        ToggleRoofHide, // toggle auto-hiding of roofs/upper floors when the player walks under them (Tier 4.5)
        DebugRemoveStatic, // debug: suppress the map static under the cursor - exercises WorldMap's dynamic mutation + BlockMesh dirty-rebuild (Tier 4.7)
    }
}
