// SPDX-License-Identifier: BSD-2-Clause

using UOA.Core;

namespace TEF.Persistence
{
    /// <summary>
    /// The Exile's Forge's own config shape (Tier 4.5). Holds the engine's
    /// bootstrap settings under <see cref="Engine"/> plus room for
    /// game-specific config fields as they appear (none yet). Loaded/saved via
    /// the engine's generic <c>ConfigManager&lt;TefConfig&gt;</c>, keyed by
    /// <see cref="TefApp.AppId"/>.
    /// </summary>
    public sealed class TefConfig
    {
        public EngineSettings Engine { get; set; } = new();
    }

    /// <summary>App identity - the %AppData% folder both config and save live under.</summary>
    public static class TefApp
    {
        public const string AppId = "ExilesForge";
    }
}
