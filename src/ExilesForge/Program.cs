// SPDX-License-Identifier: BSD-2-Clause

using System;
using TEF.Core;
using TEF.Persistence;
using TEF.Scenes;

namespace TEF
{
    internal static class Program
    {
        // Only used to seed config.json the very first time it's created -
        // see ConfigManager. After that, the file is the source of truth;
        // edit it directly to point at a different UO install rather than
        // touching these.
        private const string DefaultUltimaOnlineDirectory = @"D:\Games\UO Outlands Online";
        private const string DefaultClientVersion = "7.0.15.1";

        [STAThread]
        private static void Main(string[] args)
        {
            var settings = ConfigManager.Load();

            if (settings == null)
            {
                settings = new GameSettings
                {
                    UltimaOnlineDirectory = DefaultUltimaOnlineDirectory,
                    ClientVersion = DefaultClientVersion,
                };

                // First run - write out a discoverable, editable file rather
                // than silently falling back to these defaults every launch.
                ConfigManager.Save(settings);
            }

            // Command-line args are a transient per-launch override, not
            // something that gets written back into the saved config.
            var uoPath = GetArg(args, "--uopath");
            var clientVersion = GetArg(args, "--clientversion");

            if (!string.IsNullOrEmpty(uoPath))
            {
                settings.UltimaOnlineDirectory = uoPath;
            }

            if (!string.IsNullOrEmpty(clientVersion))
            {
                settings.ClientVersion = clientVersion;
            }

            using var game = new GameController(settings);
            game.Scenes.ChangeScene(new TitleScene(game));
            game.Run();
        }

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
