// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using UOA.UI.Controls;

namespace UOA.UI
{
    /// <summary>
    /// Maps a string type-name to a control constructor (Tier 4.5). This is
    /// the seam that lets the UI be built data-drivenly - from markup, a save,
    /// or a future Lua/TS script - rather than only in hand-written C#: a
    /// loader reads `Panel { ... }` and calls <see cref="Create"/>("Panel"),
    /// then sets properties via <see cref="ControlProperties"/>. No interpreter
    /// is involved yet; this just makes construction addressable by name so one
    /// can be added additively later. Games register their own custom control
    /// types here alongside the built-ins.
    /// </summary>
    public sealed class ControlFactory
    {
        private readonly Dictionary<string, Func<Control>> _ctors =
            new(StringComparer.OrdinalIgnoreCase);

        public void Register(string typeName, Func<Control> constructor)
        {
            _ctors[typeName] = constructor;
        }

        public bool IsRegistered(string typeName) => _ctors.ContainsKey(typeName);

        /// <summary>Creates a control by registered type-name. Throws <see cref="KeyNotFoundException"/> for an unknown name (a data/markup error worth surfacing loudly).</summary>
        public Control Create(string typeName)
        {
            if (!_ctors.TryGetValue(typeName, out var ctor))
            {
                throw new KeyNotFoundException($"No control type registered as '{typeName}'.");
            }

            return ctor();
        }

        /// <summary>A factory pre-registered with the built-in control types.</summary>
        public static ControlFactory CreateDefault()
        {
            var factory = new ControlFactory();
            factory.Register("Control", () => new Control());
            factory.Register("Panel", () => new Panel());
            factory.Register("Label", () => new Label());
            factory.Register("Button", () => new Button());
            return factory;
        }
    }
}
