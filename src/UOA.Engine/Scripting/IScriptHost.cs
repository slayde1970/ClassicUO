// SPDX-License-Identifier: BSD-2-Clause

namespace UOA.Scripting
{
    /// <summary>
    /// The seam between the engine's UI (and later, other systems) and a
    /// scripting layer (Tier 4.5). Data-driven UI binds an action by NAME -
    /// e.g. a button's <c>onClick="harvest"</c> - and dispatches it through
    /// this interface, so the engine never depends on a concrete VM. Today the
    /// only implementation is <see cref="DelegateScriptHost"/> (named C#
    /// handlers, no interpreter). Dropping in a Lua (e.g. MoonSharp) or JS
    /// host later is purely additive: implement this interface so
    /// <see cref="Invoke"/> resolves the name against the script runtime.
    /// </summary>
    public interface IScriptHost
    {
        /// <summary>Invokes the named handler, if one is registered (unknown names are ignored, not errors - a UI shouldn't crash over a stale binding).</summary>
        void Invoke(string handler, params object[] args);
    }
}
