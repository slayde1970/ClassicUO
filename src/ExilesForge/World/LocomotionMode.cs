// SPDX-License-Identifier: BSD-2-Clause

namespace TEF.World
{
    /// <summary>
    /// How the player is currently getting around - the key into the engine's
    /// <see cref="UOA.Audio.LocomotionSoundController"/> profile registry (and a
    /// natural hook for future mount visuals/speed too). Only <see cref="OnFoot"/>
    /// is reachable today; <see cref="Mounted"/> is registered and ready so that
    /// adding a mount system later is just flipping the active mode.
    /// </summary>
    public enum LocomotionMode
    {
        OnFoot = 0,
        Mounted = 1,
    }
}
