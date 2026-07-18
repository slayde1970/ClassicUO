// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace UOA.Scripting
{
    /// <summary>
    /// An <see cref="IScriptHost"/> backed by plain C# delegates (Tier 4.5):
    /// the game registers named handlers and data-driven UI dispatches to them
    /// by name - proving the scripting seam end-to-end with no interpreter. A
    /// real Lua/JS host later implements <see cref="IScriptHost"/> the same
    /// way; call sites that dispatch by name don't change.
    /// </summary>
    public sealed class DelegateScriptHost : IScriptHost
    {
        private readonly Dictionary<string, Action<object[]>> _handlers =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registers a no-arg named handler.</summary>
        public void Register(string handler, Action action) =>
            _handlers[handler] = _ => action();

        /// <summary>Registers a named handler that receives the dispatch args.</summary>
        public void Register(string handler, Action<object[]> action) =>
            _handlers[handler] = action;

        public void Invoke(string handler, params object[] args)
        {
            if (_handlers.TryGetValue(handler, out var action))
            {
                action(args);
            }
        }
    }
}
