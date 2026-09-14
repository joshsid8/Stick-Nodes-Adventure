using System;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Built-in condition drivers: reserved names that resolve to EDITOR state rather than to a member of the
    /// inspected object — currently just <see cref="PlayMode"/> ("IsInPlayMode"), so a layout can show, hide,
    /// enable or disable a field purely on whether the game is running.
    ///
    /// A built-in is a driver NAME, not a model field, which is why the feature needs no schema bump and no
    /// migration. It is the LAST thing tried when a driver is resolved: a real serialized field wins, then a real
    /// bool property/method wins, and only a name that matches nothing on the target falls through to here. So a
    /// class that already has its own <c>IsInPlayMode</c> keeps it, and the built-in is what everyone else gets.
    ///
    /// A built-in reads as a bool driver, so it pairs with the <c>is true</c> / <c>is false</c> operators
    /// (<c>= true</c> / <c>= false</c> work too).
    /// </summary>
    public static class ConditionDrivers
    {
        /// <summary>True while the editor is in Play Mode. Authored as "IsInPlayMode is true" (or "is false" for edit mode).</summary>
        public const string PlayMode = "IsInPlayMode";

        /// <summary>Every reserved name, in the order the designer offers them.</summary>
        public static readonly string[] All = { PlayMode };

        /// <summary>
        /// True when <paramref name="driver"/> names a reserved built-in. Matched against the known list rather
        /// than by any prefix or sigil, so an ordinary driver name can never be mistaken for one.
        /// </summary>
        public static bool IsBuiltIn(string driver)
        {
            if (string.IsNullOrEmpty(driver)) return false;
            foreach (var name in All)
                if (string.Equals(driver, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Short human description of a built-in, shown as the detail text in the designer's driver list.</summary>
        public static string Describe(string driver) =>
            IsPlayMode(driver) ? "editor is in Play Mode" : "";

        /// <summary>True when <paramref name="driver"/> is the play-mode built-in (case-insensitive — it is hand-typeable).</summary>
        public static bool IsPlayMode(string driver) =>
            string.Equals(driver, PlayMode, StringComparison.OrdinalIgnoreCase);
    }
}
