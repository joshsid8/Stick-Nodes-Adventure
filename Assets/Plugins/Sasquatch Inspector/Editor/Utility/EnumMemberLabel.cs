using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The ONE definition of "what an enum member is called in the UI" for the paths that have no
    /// <see cref="SerializedProperty"/> to ask.
    ///
    /// Live widgets read <c>prop.enumDisplayNames</c>, which Unity already builds with
    /// <c>[InspectorName]</c> applied — that stays the source of truth wherever a property exists. But two
    /// paths have only a <see cref="Type"/>: the Layout Designer's per-value authoring rows (colour + icon
    /// per enum member, edited against the layout, not against an instance) and the standalone-editor
    /// EXPORT, which bakes the labels into generated source at emit time. Both must honour
    /// <c>[InspectorName]</c> here, or the designer and the exported editor disagree with the live inspector
    /// about what the very same member is called.
    ///
    /// Keep this the only reflection-based answer, so the three surfaces can't drift.
    /// </summary>
    public static class EnumMemberLabel
    {
        /// <summary>
        /// The UI label for <paramref name="memberName"/> on <paramref name="enumType"/>: its
        /// <c>[InspectorName]</c> when declared, otherwise the nicified member name — byte-for-byte what
        /// <c>SerializedProperty.enumDisplayNames</c> would report for the same member.
        /// </summary>
        public static string Resolve(Type enumType, string memberName)
        {
            if (string.IsNullOrEmpty(memberName)) return string.Empty;
            if (enumType == null) return ObjectNames.NicifyVariableName(memberName);

            var member = enumType.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
            return ResolveFromMember(member, memberName);
        }

        /// <summary>
        /// Same rule, for callers that already hold the member's <see cref="FieldInfo"/> (the export emitter
        /// enumerates the enum's static fields directly, so re-looking-it-up by name would be wasteful).
        /// Deliberately NOT an overload of <see cref="Resolve"/> — <c>Resolve(null, name)</c> would be an
        /// ambiguous call, and a null type is a real case on the authoring paths.
        /// </summary>
        public static string ResolveFromMember(FieldInfo member, string memberName)
        {
            if (member != null &&
                Attribute.GetCustomAttribute(member, typeof(InspectorNameAttribute)) is InspectorNameAttribute inspectorName &&
                !string.IsNullOrEmpty(inspectorName.displayName))
            {
                return inspectorName.displayName;
            }
            return ObjectNames.NicifyVariableName(memberName ?? (member != null ? member.Name : string.Empty));
        }
    }
}
