// SPDX-License-Identifier: BSD-2-Clause

namespace UOA.Core
{
    /// <summary>
    /// Minimal bootstrap settings needed to locate and parse the UO client
    /// data files. Unlike ClassicUO's Settings, this has no network/account
    /// fields - TEF never talks to a UO server.
    /// </summary>
    public sealed class EngineSettings
    {
        public string UltimaOnlineDirectory { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public string Language { get; set; } = "ENU";
        public bool UseVerdata { get; set; }
        public int FPS { get; set; } = 60;

        /// <summary>Draw ground shadows for the player and shadow-casting statics (trees, foliage, rocks). On by default; set false in config.json to disable.</summary>
        public bool ShadowsEnabled { get; set; } = true;

        /// <summary>Play movement sounds (footsteps; hoofbeats when mounted later). On by default; set false in config.json to disable.</summary>
        public bool FootstepsEnabled { get; set; } = true;
    }
}
