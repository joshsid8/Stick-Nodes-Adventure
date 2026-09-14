using System;

namespace SasquatchInspector.Editor.Export
{
    /// <summary>
    /// The marker contract shared by the emitter (writes it) and the designer (detects it): every exported
    /// standalone editor class carries a public const int named <see cref="FieldName"/>. Detection lets the
    /// designer's conflict notice talk about "your exported editor" — offering the ordinary Force Takeover /
    /// Release toggle with tailored wording — instead of warning about a competing third-party editor.
    /// An exported editor is otherwise a completely normal <c>[CustomEditor]</c>: takeover shows the live
    /// layout, release shows the file's baked copy.
    /// </summary>
    internal static class ExportMarker
    {
        /// <summary>Name of the public const int the emitter writes into every exported editor class.</summary>
        public const string FieldName = "SasquatchExportedEditorVersion";

        /// <summary>Value written for exports produced by this package version. Version 1 files carried a
        /// (since-removed) reflection bridge back into the package; its probe fails silently, so they render
        /// their baked copy exactly like a version 2 file — re-export to refresh them.</summary>
        public const int Version = 2;

        /// <summary>True when <paramref name="editorType"/> is a Sasquatch-exported standalone editor (it
        /// carries the emitted marker const).</summary>
        public static bool IsExportedEditor(Type editorType)
        {
            if (editorType == null) return false;
            try
            {
                var f = editorType.GetField(FieldName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return f != null && f.IsLiteral && f.FieldType == typeof(int);
            }
            catch { return false; }
        }
    }
}
