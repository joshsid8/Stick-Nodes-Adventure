using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Per-value visual override for one <see cref="DisplayType.EnumBadgeGrid"/> badge, keyed by the enum member's
    /// underlying numeric value (matching <c>SerializedProperty.enumValueFlag</c> semantics). Lets a specific enum
    /// member show a custom active-state tint and/or a leading icon. Stored as an array on the binding; a member
    /// with no entry renders with the shared accent and no icon, so this is purely additive.
    /// </summary>
    [Serializable]
    public struct EnumBadgeOverride
    {
        /// <summary>The enum member's underlying numeric value.</summary>
        public int value;

        /// <summary>Per-badge active-state tint; unset (isOverridden=false) ⇒ the shared accent.</summary>
        public OverridableColor color;

        /// <summary>Sasquatch icon id shown before the badge label; empty ⇒ no icon.</summary>
        public string iconId;
    }
}
