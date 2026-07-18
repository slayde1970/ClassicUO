// SPDX-License-Identifier: BSD-2-Clause

namespace UOA.Core
{
    /// <summary>
    /// Minimal bootstrap settings needed to locate and parse the UO client
    /// data files. Unlike ClassicUO's Settings, this has no network/account
    /// fields - TEF never talks to a UO server.
    /// </summary>
    public sealed class GameSettings
    {
        public string UltimaOnlineDirectory { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public string Language { get; set; } = "ENU";
        public bool UseVerdata { get; set; }
        public int FPS { get; set; } = 60;
    }
}
