// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Utility.Logging;

namespace UOA.Core
{
    /// <summary>
    /// App-level logging + crash handling (Tier 4.7 #29). <see cref="Initialize"/>
    /// points the shared ClassicUO.Utility.Log at a timestamped file under the
    /// app's data folder and installs an unhandled-exception hook; <see cref="Guard"/>
    /// runs the app inside a try/catch that writes a standalone crash log and
    /// rethrows so the process still surfaces failure. Engine-level, so every
    /// prototype gets the same diagnostics from one call.
    /// </summary>
    public static class EngineDiagnostics
    {
        private static string _logDir;

        /// <summary>Starts file logging under %AppData%/&lt;appId&gt;/logs and hooks unhandled exceptions.</summary>
        public static void Initialize(string appId, LogTypes logTypes = LogTypes.All)
        {
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                appId,
                "logs");
            Directory.CreateDirectory(_logDir);

            Log.Start(logTypes, new LogFile(_logDir, "app.log"));
            Log.Info($"--- session start {DateTime.Now:u} ---");

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }

        /// <summary>
        /// Runs the app, catching + logging any fatal exception to a timestamped
        /// crash file, then rethrows so a non-zero exit still reflects failure.
        /// Stops logging on the way out.
        /// </summary>
        public static void Guard(Action run)
        {
            try
            {
                run();
            }
            catch (Exception ex)
            {
                WriteCrash(ex);
                throw;
            }
            finally
            {
                Log.Info($"--- session end {DateTime.Now:u} ---");
                Log.Stop();
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                WriteCrash(ex);
            }
            else
            {
                Log.Panic($"Unhandled non-exception: {e.ExceptionObject}");
            }
        }

        private static void WriteCrash(Exception ex)
        {
            Log.Panic(ex.ToString());

            // Best-effort standalone crash file (never let crash logging itself
            // throw and mask the original failure).
            try
            {
                if (_logDir != null)
                {
                    File.WriteAllText(
                        Path.Combine(_logDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log"),
                        $"{DateTime.Now:u}{Environment.NewLine}{ex}");
                }
            }
            catch
            {
                // ignored
            }
        }
    }
}
