// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace ClassicUO.Utility.Logging
{
    public class Logger
    {
        private static readonly Dictionary<LogTypes, Tuple<ConsoleColor, string>> _logTypesInfo = new Dictionary<LogTypes, Tuple<ConsoleColor, string>>
        {
            {
                LogTypes.None, Tuple.Create(ConsoleColor.White, "")
            },
            {
                LogTypes.Info, Tuple.Create(ConsoleColor.Green, "  Info    ")
            },
            {
                LogTypes.Debug, Tuple.Create(ConsoleColor.DarkGreen, "  Debug   ")
            },
            {
                LogTypes.Trace, Tuple.Create(ConsoleColor.Green, "  Trace   ")
            },
            {
                LogTypes.Warning, Tuple.Create(ConsoleColor.Yellow, "  Warning ")
            },
            {
                LogTypes.Error, Tuple.Create(ConsoleColor.Red, "  Error   ")
            },
            {
                LogTypes.Panic, Tuple.Create(ConsoleColor.Red, "  Panic   ")
            }
        };

        private int _indent;

        private bool _isLogging;
        private LogFile _logFile;
        private readonly object _syncObject = new object();

        // No volatile support for properties, let's use a private backing field.
        public LogTypes LogTypes { get; set; }

        public void Start(LogFile logFile = null)
        {
            // Optional persistent sink. Callers that pass null (e.g. the main
            // ClassicUO client) stay console-only, as before.
            _logFile = logFile;
            _isLogging = true;
        }

        public void Stop()
        {
            _isLogging = false;
            _logFile?.Dispose();
            _logFile = null;
        }

        public void Message(LogTypes logType, string text)
        {
            lock (_syncObject)
            {
                SetLogger(logType, text);
            }
        }

        public void NewLine()
        {
            lock (_syncObject)
            {
                SetLogger(LogTypes.None, string.Empty);
            }
        }

        public void Clear()
        {
            Console.Clear();
        }

        public void PushIndent()
        {
            _indent++;
        }

        public void PopIndent()
        {
            _indent--;

            if (_indent < 0)
            {
                _indent = 0;
            }
        }

        private void SetLogger(LogTypes type, string text)
        {
            if (!_isLogging)
            {
                return;
            }

            if ((LogTypes & type) == type)
            {
                string indent = _indent > 0 ? new string('\t', _indent * 2) : string.Empty;

                if (type == LogTypes.None)
                {
                    Console.Write(indent);
                    Console.WriteLine(text);

                    _logFile?.Write($"{indent}{text}");
                }
                else
                {
                    ConsoleColor temp = Console.ForegroundColor;

                    Console.Write(DateTime.UtcNow);
                    Console.Write(" | ");
                    Console.ForegroundColor = _logTypesInfo[type].Item1;
                    Console.Write(_logTypesInfo[type].Item2);
                    Console.ForegroundColor = temp;
                    Console.Write(" | ");
                    Console.Write(indent);
                    Console.WriteLine(text);

                    _logFile?.Write($"{DateTime.UtcNow} | {_logTypesInfo[type].Item2} | {indent}{text}");
                }
            }
        }
    }
}