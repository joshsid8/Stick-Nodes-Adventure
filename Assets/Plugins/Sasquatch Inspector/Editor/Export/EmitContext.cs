using System;
using System.Collections.Generic;
using System.Reflection;

namespace SasquatchInspector.Editor.Export
{
    /// <summary>
    /// Per-export state threaded through the emitter: the target type, a shared <see cref="NameAllocator"/>, the
    /// collected helper-method blocks, the running list of fidelity/degradation notes, and flags recording which
    /// shared baked helpers (EvalCond / EvalBoolMember) the generated file actually needs.
    /// </summary>
    internal sealed class EmitContext
    {
        /// <summary>The type the generated <c>[CustomEditor]</c> targets.</summary>
        public readonly Type TargetType;

        /// <summary>
        /// How the target type is written in the generated source: a fully <c>global::</c>-qualified reference.
        /// The file lands in an arbitrary project, where a user type named <c>Grid</c>, <c>Light</c>, <c>Camera</c>
        /// or <c>Animation</c> would collide with the UnityEngine type of the same name the moment both namespaces
        /// are in scope (CS0104). Naming the target in full removes the ambiguity at every cast and <c>typeof</c>.
        /// </summary>
        public readonly string TargetRef;

        public readonly NameAllocator Names = new NameAllocator();

        /// <summary>Rendered helper-method source blocks (each a full method), emitted after CreateInspectorGUI.</summary>
        public readonly List<string> Methods = new List<string>();

        /// <summary>
        /// Rendered FIELD declarations, emitted as class members ahead of <c>CreateInspectorGUI</c>. For baked
        /// data several widgets share: emitting it per call site would duplicate the payload once per field,
        /// and threading it through a local would not survive the helper-method split (each section of a big
        /// layout becomes its own <c>Build_*</c> method, which cannot see another method's locals).
        /// </summary>
        public readonly List<string> Declarations = new List<string>();

        private readonly List<string> _notes = new List<string>();

        /// <summary>True once any emitted condition needs the baked <c>EvalCond(SerializedProperty,int,string)</c> helper.</summary>
        public bool NeedsEvalCond;

        /// <summary>True once any emitted condition needs the baked <c>EvalBoolMember(TargetType,string)</c> helper.</summary>
        public bool NeedsBoolMember;

        /// <summary>Widget-kit sections the emitted code references; drives which parts of the kit template the
        /// generated file embeds (Core rides along automatically — see <see cref="WidgetKitEmitter"/>).</summary>
        public readonly HashSet<string> UsedKitSections = new HashSet<string>();

        /// <summary>
        /// True when this layout carries a Script Header with its search magnifier on — i.e. the export ships
        /// the field search. Decided ONCE up front (<see cref="StandaloneEditorExporter.GenerateSource"/>)
        /// because it gates work spread across the whole walk: every node has to publish its baked haystack,
        /// and every collapsible site its transient expander. The magnifier is the sole entry point, so a
        /// layout with no header (or with the toggle off) emits none of it and is byte-for-byte what it was.
        /// </summary>
        public bool SearchEnabled;

        /// <summary>
        /// The magnifier glyph, baked to a base64 PNG at export time (there is no icon pipeline in the target
        /// project). Rendered WHITE and tinted at runtime, so one payload covers both editor skins AND the
        /// accent-blue "search is open" state — the same trick the Action Button icons use.
        /// </summary>
        public string SearchGlyphPng;

        /// <summary>
        /// True when this layout carries a Script Header with its Live Debug toggle on — i.e. the export ships
        /// the recording graphs. Decided ONCE up front for the same reason <see cref="SearchEnabled"/> is: it
        /// gates work spread across the whole walk (every bound field has to publish a channel), and the
        /// header's bug glyph is the sole entry point, so a layout without it emits none of it and is
        /// byte-for-byte what it always was. The live builder derives the identical answer per build, in
        /// <c>DebugRowBinder.LayoutWantsDebug</c>.
        /// </summary>
        public bool DebugEnabled;

        /// <summary>
        /// The field name of the baked palette LIBRARY, declared once on first use — or null when the project
        /// has fewer than two palettes, because a gear that can only offer the palette already showing is not
        /// worth the payload. Callers pass the returned name as the widget's <c>palettes:</c> argument and
        /// simply omit it when this is null.
        /// </summary>
        public string PaletteSetRef()
        {
            if (_paletteSetResolved) return _paletteSetField;
            _paletteSetResolved = true;

            var library = SasquatchPaletteLibraryManager.Current;
            var palettes = library?.Palettes;
            if (palettes == null) return null;

            var entries = new List<string>();
            foreach (var p in palettes)
            {
                if (p?.Swatches == null || p.Swatches.Count == 0) continue;   // an empty palette offers nothing
                var names = new List<string>();
                var colors = new List<string>();
                foreach (var sw in p.Swatches) { names.Add(Lit.Str(sw.name ?? "")); colors.Add(Lit.Col(sw.color)); }
                entries.Add($"    SasqKit.Pal({Lit.Str(p.Id)}, {Lit.Str(p.DisplayName)},");
                entries.Add($"        new[] {{ {string.Join(", ", names)} }},");
                entries.Add($"        new[] {{ {string.Join(", ", colors)} }}),");
            }
            // Fewer than two PALETTES, counted in entries of three lines each — one palette leaves the gear
            // with nothing to offer but the swatches already on screen, so the whole payload is skipped and
            // the widgets emit exactly what they always did.
            if (entries.Count < 6) return null;

            // The last entry closes the argument list instead of separating from a next one.
            entries[entries.Count - 1] = entries[entries.Count - 1].TrimEnd(',') + ");";

            _paletteSetField = "s_SasqPalettes";
            var decl = new List<string>
            {
                "// Every palette in the designer's project library, baked so the gear beside the swatches can",
                "// offer them. The recipient has no palette asset; this IS the library, and their pick is",
                "// remembered in EditorPrefs per field (SasqKit.PalettePrefKey).",
                $"private static readonly SasqKit.SasqPaletteSet {_paletteSetField} = SasqKit.PaletteSet(",
            };
            decl.AddRange(entries);
            // AppendBlock splits on '\n', so the block is joined with it rather than Environment.NewLine —
            // a CRLF join would leave a stray '\r' at the end of every emitted line on Windows.
            Declarations.Add(string.Join("\n", decl));
            return _paletteSetField;
        }

        private bool _paletteSetResolved;
        private string _paletteSetField;

        /// <summary>Marks a kit section (and the Core helpers) as required by the generated file.</summary>
        public void UseKit(string section)
        {
            UsedKitSections.Add(WidgetKitEmitter.CoreSection);
            if (!string.IsNullOrEmpty(section)) UsedKitSections.Add(section);
        }

        /// <summary>
        /// The structural path of the node being emitted — <c>"0"</c> at the root, then parent + <c>"/"</c> +
        /// child index. Same shape <c>LayoutTreeBuilder</c> threads for the live <c>FoldoutStateStore</c>, and
        /// pushed/popped around each child recursion (the same put-it-on-the-context trick the table-cell path
        /// override uses) so no <c>Emit*</c> signature has to grow a parameter for it.
        /// </summary>
        public string NodePath = "0";

        /// <summary>
        /// A stable, bounded key for one collapsible site's remembered open-state, baked into the generated file
        /// so the buyer's project never hashes or reflects anything at runtime. Null when there is no identity to
        /// key on, which the kit treats as "don't persist".
        /// <para>
        /// DELIBERATELY a separate key space from the live <c>FoldoutStateStore</c>'s. Live identifies the target
        /// by its MonoScript GUID; doing that here would mean an AssetDatabase scan from what is otherwise a
        /// deterministic, side-effect-free <see cref="StandaloneEditorExporter.GenerateSource"/>, and would bake a
        /// project-specific <c>.meta</c> GUID into the generated source. The type's full name is live's OWN
        /// documented fallback identity. The cost: an exported editor starts with fresh fold memory rather than
        /// inheriting the live one. The pref prefix is shared with live, so clearing one clears both.
        /// </para>
        /// </summary>
        public string FoldoutKey(string discriminator, string title)
        {
            string id = TargetType != null ? TargetType.FullName : null;
            if (string.IsNullOrEmpty(id)) return null;
            return Fnv1a(id + "|" + discriminator + "|" + title);
        }

        // 64-bit FNV-1a over the UTF-16 code units → 16-char hex. Bounds the pref-key length and keeps it free of
        // registry-unfriendly characters; collisions across one type's foldouts are vanishingly unlikely.
        private static string Fnv1a(string s)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                hash = (hash ^ (byte)(c & 0xFF)) * prime;
                hash = (hash ^ (byte)(c >> 8)) * prime;
            }
            return hash.ToString("x16");
        }

        /// <summary>
        /// Non-null only while emitting a TABLE CELL: the C# <em>expression</em> a widget must bind to instead of
        /// its design-time path literal.
        /// <para>
        /// Every other emit site knows its property path at export time and bakes it as a literal. A table cell
        /// cannot: its path (<c>inventory.Array.data[3].quantity</c>) is only known per row at runtime. The kit
        /// widgets all take <c>(SerializedObject so, string path)</c>, so a cell factory can still build any of
        /// them from <c>so</c> plus the runtime <c>cellProp.propertyPath</c> — this carries that expression down
        /// to the widget emitters without adding a parameter to all 22 of them.
        /// </para>
        /// Always restore the previous value in a <c>finally</c>: emitting a cell is re-entrant with the ordinary
        /// field path, and a leaked expression would silently rebind unrelated widgets.
        /// </summary>
        public string CellPathExpr;

        /// <summary>
        /// Non-null only while emitting a TABLE CELL: the type a column's path resolves against.
        /// <para>
        /// A column path is relative to the collection's ELEMENT type (<c>quantity</c> on <c>InventoryRow</c>),
        /// not to the target. Resolving it against the target would find nothing — or, worse, an unrelated field
        /// of the same name — and every type-driven widget decision (is it an int? which enum?) would be wrong.
        /// </para>
        /// </summary>
        public Type ResolveRoot;

        /// <summary>
        /// Non-null only while emitting a LIST ROW's item layout: the C# <em>expression</em> for the ELEMENT's
        /// own property path, which every path inside the row is relative to.
        /// <para>
        /// The sibling of <see cref="CellPathExpr"/>, and different from it in exactly one way: a table cell
        /// knows its whole path at runtime (<c>__cell.propertyPath</c> IS the value's path), whereas a row is
        /// a whole sub-layout of several fields, so what travels is the BASE and each field appends its own
        /// segment to it. An element-template binding — whose relative path is empty — resolves to the base
        /// itself, exactly as it does live.
        /// </para>
        /// Always restore the previous value in a <c>finally</c>, for the same reason
        /// <see cref="CellPathExpr"/> must: emitting a row re-enters the ordinary field path, and a leaked
        /// expression would rebind unrelated widgets to a variable that isn't even in their scope.
        /// </summary>
        public string RowBaseExpr;

        /// <summary>
        /// True only while emitting a DICTIONARY CELL — the Key or Value template of a dictionary table.
        /// <para>
        /// It exists for one decision: the label. A dictionary cell is built live with
        /// <c>forceHideLabel</c>, because the table's column header already names it — but a cell template
        /// carries a Label override anyway ("Key" / "Value"), because that is what the designer's hierarchy
        /// reads to name the row. Without this flag the emitter would take that hierarchy name for a real
        /// label and print it inside every cell of every row, so the same layout would read clean live and
        /// doubled in a generated editor.
        /// </para>
        /// Deliberately NOT expressed by borrowing <see cref="CellPathExpr"/>, which already blanks the label:
        /// that field is the runtime PATH expression, and a dictionary cell's path comes from
        /// <see cref="RowBaseExpr"/> instead. Restore it in a <c>finally</c>, like its two neighbours.
        /// </summary>
        public bool DictionaryCell;

        /// <summary>
        /// The path argument a widget should emit: a runtime expression when one is in force, otherwise the
        /// baked design-time literal. With both overrides null this is exactly <c>Lit.Str(path)</c>, so the
        /// substitution is inert outside tables and list rows.
        /// </summary>
        public string PathArg(string path)
        {
            if (CellPathExpr != null) return CellPathExpr;      // a cell's path IS the expression
            if (RowBaseExpr == null) return Lit.Str(path);      // ordinary emit: bake the literal
            // Inside a row: the element base plus this field's own relative segment. An EMPTY relative path
            // is the element-template binding — the element itself — so the base travels alone.
            return string.IsNullOrEmpty(path) ? RowBaseExpr : $"{RowBaseExpr} + {Lit.Str("." + path)}";
        }

        /// <summary>
        /// <see cref="PathArg"/> with a relative segment appended INSIDE the member — the one case where a widget
        /// binds something nested in the member it was handed, rather than the member itself. Today that is only a
        /// SasquatchString, which binds its inner <c>template</c> string.
        /// <para>
        /// The suffix cannot simply be concatenated onto the path before calling <see cref="PathArg"/>, because a
        /// table CELL's override ignores its argument outright (a cell's path IS the expression). Composing here
        /// is what lets a cell reach an inner field at all — without it a SasquatchString column bound the STRUCT
        /// and the text box had nothing to edit.
        /// </para>
        /// With both overrides null this is exactly <c>Lit.Str(path + "." + relative)</c>, so it is inert outside
        /// tables and list rows.
        /// </summary>
        public string PathArgWithin(string path, string relative)
        {
            if (string.IsNullOrEmpty(relative)) return PathArg(path);
            if (CellPathExpr != null) return $"{CellPathExpr} + {Lit.Str("." + relative)}";
            if (RowBaseExpr == null) return Lit.Str(path + "." + relative);
            // Inside a row: an EMPTY relative path is the element-template binding — the element itself — so the
            // suffix rides the base alone, mirroring PathArg's own empty-path rule.
            return string.IsNullOrEmpty(path)
                ? $"{RowBaseExpr} + {Lit.Str("." + relative)}"
                : $"{RowBaseExpr} + {Lit.Str("." + path + "." + relative)}";
        }

        /// <summary>
        /// A per-ROW variant of <see cref="FoldoutKey"/>: the emitted expression for a collapsible site's
        /// state key. Outside a row this is the baked literal; inside one the row's own element path is
        /// appended, so each row remembers its own fold instead of every row sharing one. Returns null when
        /// there is no identity to key on, which the kit reads as "don't persist".
        /// </summary>
        public string FoldoutKeyArg(string discriminator, string title)
        {
            string key = FoldoutKey(discriminator, title);
            if (key == null) return null;
            return RowBaseExpr == null ? Lit.Str(key) : $"{Lit.Str(key + "/")} + {RowBaseExpr}";
        }

        public EmitContext(Type targetType)
        {
            TargetType = targetType;
            TargetRef = GlobalRef(targetType);
        }

        /// <summary>
        /// Writes <paramref name="t"/> as an unambiguous <c>global::</c>-qualified reference. Reflection reports a
        /// nested type as <c>Outer+Inner</c>, which is not valid C# source, so the '+' is mapped back to '.'.
        /// </summary>
        internal static string GlobalRef(Type t)
        {
            if (t == null) return "object";
            string full = t.FullName;                 // null for open generic parameters — nothing better to say.
            return string.IsNullOrEmpty(full) ? t.Name : "global::" + full.Replace('+', '.');
        }

        /// <summary>Records a one-line degradation/fidelity note for the file header (de-duplicated).</summary>
        public void Note(string note)
        {
            if (!string.IsNullOrEmpty(note) && !_notes.Contains(note)) _notes.Add(note);
        }

        public IReadOnlyList<string> Notes => _notes;

        /// <summary>Reserves a unique helper-method name (e.g. <c>Build_Vitals</c>) from a desired base.</summary>
        public string NewMethod(string desired) => Names.Reserve(desired, "Build_Section");

        /// <summary>Reserves a unique local-variable name from a desired base.</summary>
        public string NewVar(string desired, string fallback = "el") => Names.Reserve(desired, fallback);

        // ---- Reflection helpers (resolve members on the TARGET type at export time) -------------------------

        private const BindingFlags InstanceAny =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// The TYPE a path resolves to. Identical to <c>ResolveField(path)?.FieldType</c> except for the one
        /// path reflection cannot express: an EMPTY relative path, which binds the SCOPE itself — a
        /// collection element inside a row or a Value cell — and whose type is therefore
        /// <see cref="ResolveRoot"/>. Widget selection is driven entirely by this type, so without it every
        /// element-template binding silently degraded to a plain field in the exported editor.
        /// <para>
        /// At ordinary scope <see cref="ResolveRoot"/> is null, so an empty path still answers null exactly
        /// as it always did.
        /// </para>
        /// </summary>
        public Type ResolveFieldType(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return ResolveRoot;
            return ResolveField(propertyPath)?.FieldType;
        }

        /// <summary>
        /// Resolves the <see cref="FieldInfo"/> a (possibly dotted) serialized property path points at, walking
        /// declared instance fields down the type chain. Returns null for array/list element paths or any segment
        /// that can't be resolved — the caller then falls back to a plain <c>PropertyField</c>, which is always safe.
        /// </summary>
        public FieldInfo ResolveField(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return null;
            Type cur = ResolveRoot ?? TargetType;
            FieldInfo field = null;
            foreach (var rawSeg in propertyPath.Split('.'))
            {
                if (cur == null) return null;
                // Array/list element paths ("Array", "data[0]") aren't reflected here — bail to PropertyField.
                if (rawSeg == "Array" || rawSeg.StartsWith("data[")) return null;
                field = FindInstanceField(cur, rawSeg);
                if (field == null) return null;
                cur = field.FieldType;
            }
            return field;
        }

        private static FieldInfo FindInstanceField(Type type, string name)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var f = t.GetField(name, InstanceAny | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>
        /// Finds a parameterless instance method by name on the target type (public or private, walking the base
        /// chain). Returns null when there's no parameterless overload — the caller then emits a reflection call
        /// guarded with <c>?.</c> so the generated file still compiles.
        /// </summary>
        public MethodInfo ResolveParameterlessMethod(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (Type t = TargetType; t != null && t != typeof(object); t = t.BaseType)
            {
                var m = t.GetMethod(name, InstanceAny | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m != null) return m;
            }
            return null;
        }

        /// <summary>
        /// Finds a readable instance property by name on the target type (for <c>ShowInInspector</c> PropertyNodes).
        /// Returns null when unreadable/absent — the caller degrades to a reflection read guarded against nulls.
        /// </summary>
        public PropertyInfo ResolveReadableProperty(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (Type t = TargetType; t != null && t != typeof(object); t = t.BaseType)
            {
                var p = t.GetProperty(name, InstanceAny | BindingFlags.DeclaredOnly);
                if (p != null && p.CanRead) return p;
            }
            return null;
        }
    }
}
