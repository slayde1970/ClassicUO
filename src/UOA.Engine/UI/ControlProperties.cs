// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace UOA.UI
{
    /// <summary>
    /// Sets a control's property by string name with light value coercion
    /// (Tier 4.5) - the other half of the data-driven UI seam alongside
    /// <see cref="ControlFactory"/>. A markup/save/script layer produces
    /// (name, value) pairs (e.g. "Width" -> 200, "Draggable" -> true) and this
    /// reflects them onto public fields or settable properties, converting
    /// strings/numbers to the target type (including enums). Reflection is fine
    /// here: UI is built once, not per frame, and a scripting bridge needs
    /// exactly this name-addressable indirection.
    /// </summary>
    public static class ControlProperties
    {
        private const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy;

        public static void Set(Control control, string name, object value)
        {
            var type = control.GetType();

            var field = type.GetField(name, Flags);
            if (field != null)
            {
                field.SetValue(control, Coerce(value, field.FieldType));
                return;
            }

            var property = type.GetProperty(name, Flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(control, Coerce(value, property.PropertyType));
                return;
            }

            throw new ArgumentException($"Control type '{type.Name}' has no settable property or field named '{name}'.");
        }

        /// <summary>Convenience: apply a whole bag of properties (e.g. parsed from markup) in one call.</summary>
        public static void Apply(Control control, IEnumerable<KeyValuePair<string, object>> properties)
        {
            foreach (var (name, value) in properties)
            {
                Set(control, name, value);
            }
        }

        private static object Coerce(object value, Type target)
        {
            if (value == null || target.IsInstanceOfType(value))
            {
                return value;
            }

            var underlying = Nullable.GetUnderlyingType(target) ?? target;

            if (underlying.IsEnum)
            {
                return Enum.Parse(underlying, value.ToString(), ignoreCase: true);
            }

            if (underlying == typeof(string))
            {
                return value.ToString();
            }

            return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        }
    }
}
