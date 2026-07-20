// SPDX-License-Identifier: BSD-2-Clause

using System;
using UOA.Core;
using UOA.Persistence;
using TEF.Input;
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
            // Tier 4.7 #29: file logging + crash handling wrap the whole run,
            // so any fatal exception lands in %AppData%/<AppId>/logs rather than
            // vanishing with the window.
            EngineDiagnostics.Initialize(TefApp.AppId);
            EngineDiagnostics.Guard(() => RunGame(args));
        }

        private static void RunGame(string[] args)
        {
            var configManager = new ConfigManager<TefConfig>(TefApp.AppId);
            var config = configManager.Load();

            // Reseed when the file is missing/unparseable (config == null) OR
            // present but lacking a usable engine section - e.g. a config from
            // before the Tier 4.5 nesting, which deserializes to a TefConfig
            // with an empty Engine. Self-heal to defaults instead of crashing
            // later with "UO directory not found: ''".
            if (config == null || string.IsNullOrEmpty(config.Engine?.UltimaOnlineDirectory))
            {
                config = new TefConfig
                {
                    Engine = new EngineSettings
                    {
                        UltimaOnlineDirectory = DefaultUltimaOnlineDirectory,
                        ClientVersion = DefaultClientVersion,
                    },
                };

                // First run (or a repaired legacy config) - write out a
                // discoverable, editable file rather than silently falling
                // back to these defaults every launch.
                configManager.Save(config);
            }

            var settings = config.Engine;

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

            // The engine's InputManager ships with no bindings (it's action-
            // agnostic, Tier 4.5); the game installs its own default key map.
            game.Input.InstallDefaults();

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
