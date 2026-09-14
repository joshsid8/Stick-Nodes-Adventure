using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// How often Sasquatch Inspector is allowed to take an automatic rolling backup of the inspector-layouts folder
    /// (a snapshot is taken before a save overwrites anything, but no more than once per this interval of edits).
    /// Only consulted when automatic backups are enabled (Project Settings ▸ Sasquatch Inspector). The numeric values are
    /// the interval in MINUTES, so the serialized setting is self-describing and stable across renames — read the
    /// interval via <c>((int)frequency)</c> minutes rather than switching on the name. The <see cref="InspectorName"/>
    /// labels are what the Project Settings dropdown shows.
    /// </summary>
    public enum LayoutBackupFrequency
    {
        /// <summary>At most one backup every 5 minutes of edits.</summary>
        [InspectorName("5 minutes")] FiveMinutes = 5,
        /// <summary>At most one backup every 15 minutes of edits.</summary>
        [InspectorName("15 minutes")] FifteenMinutes = 15,
        /// <summary>At most one backup every 30 minutes of edits.</summary>
        [InspectorName("30 minutes")] ThirtyMinutes = 30,
        /// <summary>At most one backup per hour of edits (the default).</summary>
        [InspectorName("Hourly")] Hourly = 60,
        /// <summary>At most one backup every 2 hours of edits.</summary>
        [InspectorName("Every 2 hours")] TwoHours = 120,
        /// <summary>At most one backup every 6 hours of edits.</summary>
        [InspectorName("Every 6 hours")] SixHours = 360,
        /// <summary>At most one backup per day of edits.</summary>
        [InspectorName("Daily")] Daily = 1440
    }
}
