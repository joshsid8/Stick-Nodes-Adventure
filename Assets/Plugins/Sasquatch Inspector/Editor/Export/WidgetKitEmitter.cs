using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;

namespace SasquatchInspector.Editor.Export
{
    /// <summary>
    /// Turns <c>ExportedWidgetKit.cs</c> (the compiled, compile-checked template) into the source block a
    /// generated standalone editor embeds: reads the template text, keeps the prelude (SasqTheme + the SasqKit
    /// class opening) plus only the SECTIONs the export actually uses, and rewrites the two class declarations
    /// from <c>internal</c> to <c>private</c> so they nest inside the generated editor class without colliding
    /// with a second exported editor in the same project.
    /// <para>
    /// It also STRIPS the template's module-gate directives (<c>#if SASQ_PARTICLES</c> …) and always emits the
    /// guarded body. Those gates exist only so this package's assembly compiles when a removable built-in module
    /// is absent; no SASQ_* symbol is ever defined in a buyer's Assembly-CSharp, so a directive that travelled
    /// would delete the widget it guards. A <c>#if UNITY_&lt;NAME&gt;</c> VERSION gate is the opposite case and
    /// ships VERBATIM (with its <c>#elif UNITY_&lt;NAME&gt;</c> and <c>#else</c>): UNITY_* symbols ARE defined
    /// in the buyer's assembly, and the branches are real per-editor-version code, so the buyer's own compiler
    /// picks one. See <see cref="ClassifyDirective"/>; anything else makes
    /// <see cref="Emit"/> return "".
    /// </para>
    /// </summary>
    internal static class WidgetKitEmitter
    {
        private const string BeginMarker = "// >>> SASQ-KIT BEGIN";
        private const string EndMarker = "// >>> SASQ-KIT END";
        private const string SectionOpenPrefix = "// >>> SASQ-KIT SECTION ";
        private const string SectionClose = "// <<< SASQ-KIT SECTION";

        /// <summary>Every widget section pulls Core in implicitly.</summary>
        public const string CoreSection = "Core";

        /// <summary>Cross-section calls inside the template (checked by compilation there); the emitter must
        /// keep the callee when the caller ships.</summary>
        private static readonly Dictionary<string, string[]> SectionDependencies = new Dictionary<string, string[]>
        {
            { "Stepper", new[] { "Button" } },
            // The field search lives INSIDE the Script Header: the magnifier is its sole entry point, so a
            // search slice without the header would compile and then find nothing to attach to. Declared
            // rather than relied on: the emitter only ever requests Search from a layout that has a header,
            // and this keeps that true even if a later caller asks for the section on its own.
            { "Search", new[] { "ScriptHeader" } },
            // Live Debug hangs its bug glyph in the Script Header's button cluster (the same host the search
            // magnifier uses), and its log pins swell on the baked SPRING solver in Animation. Both edges are
            // unconditional: the glyph is the feature's only entry point, and a pin swells on hover rather
            // than behind an authored option, so an "only when it animates" edge would compile here (the
            // template is one class) and land as CS0103 in the buyer's project the first time a log arrives.
            { "LiveDebug", new[] { "ScriptHeader", "Animation" } },
            // AxisPips unconditionally: VectorSliders' body calls AxisPip() whether or not the exported node
            // turned the pips on. A "only when node.VectorAxisPips" dependency would compile here (the
            // template is one class) and land as CS0103 in the buyer's project for every un-pipped export.
            { "VectorSliders", new[] { "Slider", "Button", "AxisPips" } },
            // …and Palettes, for the toolbar's Color row. UNCONDITIONAL even though only a node with the
            // toolbar on reaches the swatches: RichTextArea's SIGNATURE names SasqPaletteSet, so a toolbar-less
            // export that skipped the section would not compile at all.
            { "RichText", new[] { "TextArea", "Palettes" } },
            { "SRef", new[] { "Dropdowns" } },
            // Animation, for the SPRING SOLVER the switch knob is thrown on. Unconditional: every switch-style
            // toggle springs its knob on click, it is not behind an authored option, and a "only when the node
            // animates" edge would compile here (the template is one class) and land as CS0103 in the buyer's
            // project the first time someone flips a switch.
            { "Toggle", new[] { "Animation" } },
            { "InlineEditor", new[] { "Group", "AssetCreate" } },
            // AssetPreview draws its own thumbnail well beside (or instead of) Unity's ObjectFieldDisplay, so it
            // needs ObjectSlot to make that well a real slot (drop + Delete). ObjectSlot is its own small section for the
            // same reason as PopupPeek: Collection needs it too, and neither should drag the other in.
            // …and PreviewView, the per-field memory that keeps an interactive viewport's camera pose and 2D
            // toggle across an inspector rebuild. Its own section for the same reason as ObjectSlot: either
            // preview can be exported without the other, and neither should drag the other in.
            // …and PreviewBlank, the empty-slot / "off screen" overlays that tell the three different reasons a
            // preview box can render as a blank square apart. Its own section for the same reason as the two
            // above: either preview can ship without the other, and neither should drag the other in.
            { "AssetPreview", new[] { "Group", "AssetCreate", "ObjectSlot", "PreviewView", "PreviewBlank" } },
            { "ParticlePreview", new[] { "Group", "ObjectSlot", "PreviewView", "PreviewBlank" } },
            // UnityEventCard carries its own sectioned SasqEventMethodPicker, so a UnityEvent export does NOT
            // drag the whole Dropdowns section in behind it. Both DO share the popup chrome — the code-peek
            // card's placement and the scroll lock — which is why PopupPeek is its own small section rather
            // than living in either of them or being paid for by every export via Core.
            { "Dropdowns", new[] { "PopupPeek" } },
            { "UnityEventCard", new[] { "PopupPeek" } },
            // Group for the Expandable / ExpandableWithPreview shells, and Animation for the SPRING SOLVER the
            // pins pop on. Animation is declared unconditionally even though only a pin hover/select reaches it:
            // SpringPin is on the interaction path of every gradient, not behind an authored option, so a
            // "only when the node animates" edge would compile here (the template is one class) and land as
            // CS0103 in the buyer's project the first time someone hovers a key.
            // …and Palettes, for the pin picker's swatch grid and its gear. Same unconditional reasoning as
            // RichText: GradientWell's signature names SasqPaletteSet whether or not a library was baked.
            { "GradientWell", new[] { "Group", "Animation", "Palettes" } },
            // …and Animation, for the drag-reorder glide. A row being dragged pushes its neighbours out of
            // the way, and live springs them (CollectionRowReorder's SlideConfig); the kit used an 80 ms CSS
            // transition because it had no spring solver until the Animation section gained one, and nobody
            // revisited it. The edge is declared rather than the solver moved to Core: only a collection
            // export pays for it, whereas Core is paid for by every export including ones with no motion.
            { "Collection", new[] { "Group", "ObjectSlot", "Animation" } },
            // The dictionary table reuses the COLLECTION's row machinery verbatim — the drag grip, the
            // right-click row menu, the row-separator painter and the Card header inset — which is the whole
            // point of the edge: a dictionary row frame and a list row frame are ONE decision point live, so
            // re-declaring them here would be two copies free to drift. Unconditional even for a table whose
            // Row Frame is None: the grip and the menu are on the interaction path of every dictionary, not
            // behind an authored option, so an "only when it is framed" edge would compile here (the template
            // is one class) and land as CS0103 in the buyer's project on the very first drag.
            { "Dictionary", new[] { "Collection" } },
            // Palettes is the BAKED LIBRARY + the gear; Palette is the widget that draws one of them. Its own
            // section rather than folded into Palette because three unrelated widgets read it (the palette
            // widget, the rich-text Color row, the gradient pin picker) and an export carrying only one of the
            // three should not pay for the other two.
            { "Palette", new[] { "HdrBadge", "Palettes" } },
            // The Spring CONFIG editor. Animation carries the baked SpringMath twin (SpringEvaluate /
            // SpringTo / SpringCancel / SpringSettleCoefficient and the editor tick that advances them), so
            // the response graph and the drag-and-release tester run on the SAME solver the Spring Pop /
            // Slide / Shake node animations do — one copy, which is the point of the edge. Declared rather
            // than moved to Core: only an export that actually carries a SpringConfig field pays for it.
            // …and Group, for the collapsible Advanced shell (Settle Epsilon / Frequency Scale / Unscaled).
            { "SpringWell", new[] { "Animation", "Group" } },
            // The Animation Curve editor. Animation for the SPRING SOLVER — the key-diamond pops, the preset
            // morph and the view reframe all run on it, and every one of those is on the interaction path of
            // any curve rather than behind an authored option, so the edge is unconditional. Group for the
            // Expandable / ExpandableWithPreview shells.
            { "CurveWell", new[] { "Animation", "Group" } },
        };

        private static string _cachedTemplate;

        /// <summary>
        /// Every section name declared in the template, in file order. Test-only hook: the slicer's real failure mode is a
        /// cross-section call that compiles here (the template is one class) but lands as CS0103 in a buyer's
        /// project once the callee's section is stripped; this list is what lets each section be compiled in
        /// isolation to catch that.
        /// </summary>
        internal static IEnumerable<string> SectionNames()
        {
            string template = LoadTemplate();
            if (string.IsNullOrEmpty(template)) yield break;
            foreach (var rawLine in template.Split('\n'))
            {
                string trimmed = rawLine.TrimEnd('\r').TrimStart();
                if (trimmed.StartsWith(SectionOpenPrefix, StringComparison.Ordinal))
                    yield return trimmed.Substring(SectionOpenPrefix.Length).Trim();
            }
        }

        /// <summary>
        /// Renders the kit block for <paramref name="usedSections"/> (Core is always included). Returns "" when
        /// the template cannot be found, or contains a preprocessor directive this emitter refuses to strip.
        /// The caller (<c>StandaloneEditorExporter</c>) THROWS on "" rather than degrading — a kit-less editor
        /// would reference SasqKit/SasqTheme and not compile, so failing the export outright is the kinder
        /// outcome.
        /// </summary>
        public static string Emit(IEnumerable<string> usedSections, string exportId = null)
        {
            string template = LoadTemplate();
            if (string.IsNullOrEmpty(template)) return "";

            var wanted = new HashSet<string>(usedSections, StringComparer.Ordinal) { CoreSection };
            var queue = new Queue<string>(wanted);
            while (queue.Count > 0)
            {
                if (!SectionDependencies.TryGetValue(queue.Dequeue(), out var deps)) continue;
                foreach (var dep in deps)
                    if (wanted.Add(dep)) queue.Enqueue(dep);
            }

            int begin = template.IndexOf(BeginMarker, StringComparison.Ordinal);
            int end = template.IndexOf(EndMarker, StringComparison.Ordinal);
            if (begin < 0 || end < 0 || end <= begin) return "";
            string body = template.Substring(begin + BeginMarker.Length, end - begin - BeginMarker.Length);

            var output = new StringBuilder(body.Length);
            bool inSection = false;
            bool keepSection = false;
            // One entry per OPEN #if, innermost last: true = a UNITY_* version gate (ships verbatim),
            // false = a SASQ_* module gate (stripped). See the directive block below for why they differ.
            var gateStack = new List<bool>();
            foreach (var rawLine in body.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith(SectionOpenPrefix, StringComparison.Ordinal))
                {
                    inSection = true;
                    keepSection = wanted.Contains(trimmed.Substring(SectionOpenPrefix.Length).Trim());
                    continue; // the marker itself never ships
                }
                if (trimmed.StartsWith(SectionClose, StringComparison.Ordinal))
                {
                    inSection = false;
                    keepSection = false;
                    continue;
                }
                if (inSection && !keepSection) continue;

                // MODULE GATES NEVER SHIP. A `#if SASQ_*` inside the template exists ONLY so THIS package's
                // assembly still compiles when a removable built-in module (Particle System, Physics, Audio) is
                // absent. The generated file lands in the buyer's Assembly-CSharp, where no SASQ_* symbol is ever
                // defined — a directive that travelled would silently DELETE the widget it guards and hand the
                // buyer CS0103 on a call to a method that is visibly right there in the file. So the directive
                // line is dropped and the guarded body ALWAYS ships. That is safe because the emitter only ever
                // REQUESTS a gated section from a project where the module is present (DisplayTypeEmitter gates
                // the matching case), so the body is only sliced when it is real.
                //
                // VERSION GATES DO SHIP, AND MUST. A `#if UNITY_6000_5_OR_NEWER` is the opposite case: UNITY_*
                // symbols ARE defined in the buyer's assembly, by their editor, and the two branches are
                // genuinely different code for different editor versions. Stripping one would ship the wrong
                // branch; refusing the slice would drop the widget. So a version gate — and its `#else` and
                // `#endif` — is emitted VERBATIM and the buyer's own compiler picks the branch.
                //   Why this exists: Unity is migrating int instance ids to EntityId one wave at a time, and
                //   each wave lands as an ERROR-level obsolete (CS0619), not a warning. 6000.5 took
                //   GetInstanceID(); 6000.6 took EditorApplication's per-item GUI events and
                //   EditorUtility.InstanceIDToObject(int). The kit's eyedropper needs both spellings, and
                //   there is no runtime-selection trick available (unlike the audio backend) because the
                //   callback's PARAMETER TYPE is what changed — EntityId does not exist on 6000.0 at all.
                //
                // Anything that is neither a strippable module gate nor a shippable version gate aborts the
                // whole slice: a negated `#if !SASQ_X` would ship the WRONG branch, an `#elif` on a MODULE gate
                // (whose `#if` was stripped, leaving nothing to attach to) is not
                // something to guess at, and a `#region`/`#pragma` is noise in a file the buyer cannot fix.
                // Emitting a subtly wrong widget is far worse than failing the export outright.
                if (trimmed.Length > 0 && trimmed[0] == '#')
                {
                    string kind = ClassifyDirective(trimmed, gateStack);
                    if (kind == "refuse") return "";
                    if (kind == "strip") continue;
                    // "ship" falls through to the emit below, verbatim.
                }

                // HOLD-TO-CLEAR IS A PROJECT SETTING, AND THE KIT CANNOT READ ONE. The generated file is
                // package-free — no SasquatchSettingsManager, no settings asset — so the duration is BAKED here,
                // at export time, from whatever the project is set to. The template keeps a real default literal
                // (so it still compiles package-side) plus a marker comment; only the value after '=' is
                // rewritten, and the line's own indentation survives for the shift below.
                if (line.IndexOf(ClearHoldMarker, StringComparison.Ordinal) >= 0)
                {
                    int eq = line.IndexOf('=');
                    if (eq >= 0) line = line.Substring(0, eq + 1) + " " + ClearHoldLiteral() + ";";
                }

                // The template body sits one namespace level deep — shift it back to column 0 so the caller's
                // AppendBlock indents it exactly one class level.
                if (line.StartsWith("    ", StringComparison.Ordinal)) line = line.Substring(4);
                output.Append(line).Append('\n');
            }

            return output.ToString()
                .Replace("internal static class SasqTheme", "private static class SasqTheme")
                .Replace("internal static class SasqKit", "private static class SasqKit")
                // The one PER-EXPORT token in the whole template. A [Shortcut] id has to be a compile-time
                // constant AND globally unique: two exported editors registering the same id is a duplicate
                // registration, and Unity resolves it by dropping both. Substituting the target type's name
                // gives each generated file its own entry in Edit ▸ Shortcuts. Left as the literal token when
                // no id is supplied (the section-slice tests), where it is only ever a string.
                .Replace(ExportIdToken, string.IsNullOrEmpty(exportId) ? ExportIdToken : exportId);
        }

        /// <summary>The placeholder the emitter rewrites to the exported type's name. Package-side it is only
        /// ever a literal inside a string, so the template still compiles here.</summary>
        internal const string ExportIdToken = "__SASQ_EXPORT_ID__";

        /// <summary>Marks the template's hold-to-clear constant so its value can be baked at export time. Lives
        /// in a trailing <c>//</c> comment, so the template line is a perfectly ordinary compiling declaration.
        /// Its twin is <c>KitClearHoldSeconds</c> in ExportedWidgetKit.</summary>
        internal const string ClearHoldMarker = "__SASQ_CLEAR_HOLD__";

        /// <summary>The project's hold-to-clear duration as a C# float literal. INVARIANT CULTURE is not optional:
        /// on a comma-decimal locale "0,65f" is not a number the generated file could ever compile.</summary>
        private static string ClearHoldLiteral()
        {
            var settings = SasquatchSettingsManager.Current;
            float seconds = settings != null ? settings.ClearHoldSeconds : CollectionClearAll.DefaultHoldSeconds;
            return seconds.ToString("0.###", CultureInfo.InvariantCulture) + "f";
        }

        /// <summary>The only two preprocessor forms the template may contain, both of which are STRIPPED from the
        /// emitted text: <c>#if SASQ_&lt;NAME&gt;</c> — one non-negated module-gate symbol, no operators — and
        /// <c>#endif</c>, each optionally followed by a <c>//</c> comment. Anything else makes <see cref="Emit"/>
        /// refuse the slice rather than guess which branch the buyer should receive.</summary>
        /// <summary>
        /// Decides what happens to one preprocessor line, maintaining <paramref name="gateStack"/> (one entry
        /// per open <c>#if</c>, innermost last; true = version gate).
        /// <list type="bullet">
        /// <item><c>"strip"</c> — a <c>#if SASQ_&lt;NAME&gt;</c> module gate, or the <c>#endif</c> closing one.
        /// The directive goes, the body always ships (SASQ_* is never defined for a buyer).</item>
        /// <item><c>"ship"</c> — a <c>#if UNITY_&lt;NAME&gt;</c> version gate, or the <c>#else</c>/<c>#endif</c>
        /// belonging to one. Emitted verbatim; the buyer's compiler resolves it.</item>
        /// <item><c>"refuse"</c> — anything else, including a <c>#else</c> under a MODULE gate (its two
        /// branches would collapse into one) and any unbalanced <c>#endif</c>.</item>
        /// </list>
        /// </summary>
        private static string ClassifyDirective(string trimmed, List<bool> gateStack)
        {
            string code = trimmed;
            int comment = code.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) code = code.Substring(0, comment);
            code = code.Trim();

            if (code == "#endif")
            {
                if (gateStack.Count == 0) return "refuse";          // unbalanced — do not guess
                bool wasVersionGate = gateStack[gateStack.Count - 1];
                gateStack.RemoveAt(gateStack.Count - 1);
                return wasVersionGate ? "ship" : "strip";
            }

            if (code == "#else")
            {
                // Only meaningful under a version gate. Under a stripped module gate BOTH branches would end
                // up in the output, which is exactly the "ship the wrong widget" failure this guard prevents.
                if (gateStack.Count == 0 || !gateStack[gateStack.Count - 1]) return "refuse";
                return "ship";
            }

            // `#elif UNITY_<NAME>` — allowed 2026-09-04, under exactly the same rule as `#else` above.
            //
            // The blanket refusal read "an #elif chain is not something to guess at", and for a MODULE gate
            // that is still true: a stripped `#if SASQ_X` has no branch structure left to attach an #elif to.
            // For a VERSION gate there is nothing to guess. The whole `#if`/`#elif`/`#else`/`#endif` group
            // ships verbatim and the BUYER'S OWN COMPILER picks the branch, so an #elif is no more of a
            // judgement call than the #else beside it.
            //
            // It became necessary because the EntityId waves do not land together: EditorUtility
            // .EntityIdToObject exists from 6000.3 but the ...ByEntityIdOnGUI events only from 6000.4, so the
            // eyedropper adapters need a middle branch for 6000.3 alone. Without this the kit's twin could not
            // carry the same three tiers as the live path, and an exported editor would warn on 6000.3 where
            // the live one does not - the exact divergence the lockstep rule exists to prevent.
            //
            // Deliberately NOT pushing or popping the gate stack: an #elif continues the current gate rather
            // than opening a new one, and the matching #endif still sees the `true` its #if pushed.
            if (code.StartsWith("#elif", StringComparison.Ordinal) && code.Length > 5 && char.IsWhiteSpace(code[5]))
            {
                if (gateStack.Count == 0 || !gateStack[gateStack.Count - 1]) return "refuse";
                string elifSymbol = code.Substring(5).Trim();
                if (elifSymbol.Length <= 6) return "refuse";
                foreach (char c in elifSymbol)
                    if (!char.IsLetterOrDigit(c) && c != '_') return "refuse";   // rejects !, &&, ||, (, )
                if (!elifSymbol.StartsWith("UNITY_", StringComparison.Ordinal)) return "refuse";
                return "ship";
            }

            if (code.Length < 4 || !code.StartsWith("#if", StringComparison.Ordinal) || !char.IsWhiteSpace(code[3]))
                return "refuse";

            string symbol = code.Substring(3).Trim();
            foreach (char c in symbol)
                if (!char.IsLetterOrDigit(c) && c != '_') return "refuse";   // rejects !, &&, ||, (, )

            if (symbol.StartsWith("SASQ_", StringComparison.Ordinal) && symbol.Length > 5)
            {
                gateStack.Add(false);
                return "strip";
            }
            if (symbol.StartsWith("UNITY_", StringComparison.Ordinal) && symbol.Length > 6)
            {
                gateStack.Add(true);
                return "ship";
            }
            return "refuse";
        }

        private static string LoadTemplate()
        {
            if (_cachedTemplate != null) return _cachedTemplate;
            try
            {
                string path = ExportedWidgetKitLocator.SourcePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return _cachedTemplate = File.ReadAllText(path);

                // The compile-time path can go stale if the package folder moved after the last compile —
                // fall back to finding the script asset by name.
                foreach (var guid in AssetDatabase.FindAssets("ExportedWidgetKit t:MonoScript"))
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (assetPath != null && assetPath.EndsWith("ExportedWidgetKit.cs", StringComparison.Ordinal))
                        return _cachedTemplate = File.ReadAllText(Path.GetFullPath(assetPath));
                }
            }
            catch { /* fall through to "" — the exporter notes the degradation */ }
            return _cachedTemplate = "";
        }
    }
}
