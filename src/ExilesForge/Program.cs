// SPDX-License-Identifier: BSD-2-Clause

using System;
using TEF.Core;
using TEF.Scenes;

namespace TEF
{
    internal static class Program
    {
        private const string DefaultUltimaOnlineDirectory = @"D:\Games\UO Outlands Online";
        private const string DefaultClientVersion = "7.0.15.1";

        [STAThread]
        private static void Main(string[] args)
        {
            var uoPath = GetArg(args, "--uopath");
            var clientVersion = GetArg(args, "--clientversion");

            var settings = new GameSettings
            {
                UltimaOnlineDirectory = string.IsNullOrEmpty(uoPath) ? DefaultUltimaOnlineDirectory : uoPath,
                ClientVersion = string.IsNullOrEmpty(clientVersion) ? DefaultClientVersion : clientVersion,
            };

            using var game = new GameController(settings);
            game.Scenes.ChangeScene(new WorldScene(game));
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
