using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor.Export
{
    /// <summary>
    /// Exports a type's Sasquatch layout as a single, self-contained <c>[CustomEditor]</c> C# file (UI Toolkit) with
    /// no dependency on the Sasquatch Inspector package — so it can be handed to teammates who don't own it. The
    /// exported file is a plain custom editor: with the package installed, the ordinary per-type Force Takeover
    /// decides whether the live layout displaces it (on by default after a first export) or the baked copy renders
    /// (Release — the in-project preview of exactly what package-less teammates get). The pure
    /// <see cref="GenerateSource"/> is the testable core (no Unity side effects); <see cref="ExportWithDialog"/>
    /// wraps it with layout resolution, save/warning dialogs, asset import, and the first-export takeover opt-in.
    /// </summary>
    public static class StandaloneEditorExporter
    {
        /// <summary>True when <paramref name="targetType"/> has a Sasquatch layout that can be exported.</summary>
        public static bool CanExport(Type targetType)
        {
            return targetType != null
                && LayoutRegistry.TryGet(targetType, out var asset)
                && asset != null
                && asset.Root != null;
        }

        /// <summary>
        /// Generates the complete standalone editor source for <paramref name="targetType"/> from its layout
        /// <paramref name="root"/>. Pure — no file or asset access — so it can be unit-tested directly.
        /// The file is a self-contained baked copy of the layout with zero package references; its only Sasquatch
        /// tie is the <see cref="ExportMarker"/> const, which lets the designer recognize it and offer the normal
        /// Force Takeover / Release toggle instead of a third-party-editor conflict warning.
        /// </summary>
        /// <param name="dateStamp">Optional date string for the header; pass null for deterministic output (tests).</param>
        /// <param name="applyToSubclasses">
        /// Mirrors <see cref="InspectorLayoutAsset.ApplyToSubclasses"/>. When set, the emitted
        /// <c>[CustomEditor]</c> also claims derived types, matching what the live registry does for the layout's
        /// subclasses — otherwise a teammate sees the baked inspector on the base type only and Unity's stock
        /// inspector on every subclass, which is usually the whole set the layout was designed for.
        /// </param>
        public static string GenerateSource(Type targetType, LayoutNode root, string dateStamp = null,
                                            bool applyToSubclasses = false)
        {
            // CYCLE GUARD — must stay the FIRST thing that touches the graph, for the same reason the live
            // path's copy does (LayoutTreeBuilder.Build). This file is a SECOND, independent renderer of the
            // same model: it never goes through Build, so the guard there does not cover it. Everything below
            // walks the node tree recursively — EnsureMigratedTree on the very next line, then
            // HasSearchEnabledHeader, then the whole LayoutSourceEmitter walk — and a cyclic graph sends all
            // of them into unbounded recursion. That surfaces as StackOverflowException, which no catch can
            // intercept: exporting a corrupted layout would take the editor down with it.
            //
            // Throwing (rather than emitting a degraded file) matches how this method already refuses the
            // other thing it cannot reproduce, the unreadable widget-kit template below: an export writes a
            // real .cs file, so a half-baked one is worse than none. ExportWithDialog turns this into a
            // dialog; a direct caller gets the exception.
            if (LayoutTreeBuilder.HasCycle(root, out var cyclicNode))
                throw new InvalidOperationException(
                    $"The layout for {targetType?.Name ?? "<null>"} could not be exported: its node graph contains a " +
                    $"CYCLE (a {(cyclicNode != null ? cyclicNode.GetType().Name : "node")} that is nested inside itself). " +
                    "Nothing in Sasquatch Inspector can author this, so the layout file has been hand-edited or badly " +
                    "merged. Restore a snapshot from Project Settings ▸ Sasquatch Inspector, or delete the layout " +
                    "asset, then export again.");

            var ctx = new EmitContext(targetType);
            // Populate ConditionRules from any legacy flat conditions (idempotent, side-effect-free on migrated trees).
            ConditionModel.EnsureMigratedTree(root);

            // Decided BEFORE the walk, because it gates work spread across all of it: with the field search on,
            // every node publishes its baked haystack and every collapsible site its transient expander. The
            // Script Header's magnifier is the sole entry point, live and exported alike, so no header (or its
            // toggle off) means no search — and an export that emits none of the above.
            ctx.SearchEnabled = HasSearchEnabledHeader(root);

            // Same shape, same reason, for Live Debug — and asked through the PACKAGE'S OWN predicate rather
            // than a second walk written here. That is the parity guarantee: "does this layout record?" has
            // exactly one implementation, so the exported file can never disagree with the live one about
            // which layouts get graphs.
            ctx.DebugEnabled = LiveDebug.DebugRowBinder.LayoutWantsDebug(root);

            // Walk first so the header can list the degradation notes the walk collected.
            var body = new CodeWriter();
            body.Line("var so = serializedObject;");
            body.Line("var root = new VisualElement();");
            body.Line("root.AddToClassList(\"sasq-export-root\");");
            new LayoutSourceEmitter(ctx).EmitRoot(body, root);
            EmitLabelColumn(body, ctx);
            body.Line("root.Bind(so);");
            EmitAttachSearch(body, ctx);
            // AFTER the search, which is what puts the header's button cluster in live's order: the search
            // inserts its magnifier at the header's anchor, and the bug glyph appends after it — [🔍] [🐞].
            EmitAttachLiveDebug(body, ctx);
            EmitReturnRoot(body, ctx);

            // The emitted body references SasqKit/SasqTheme, so a missing template must fail loudly (a silent
            // skip would generate a file that doesn't compile). Emit also returns "" when the template contains a
            // preprocessor directive it refuses to strip — a package-authoring mistake, not a user one, but it
            // surfaces here so it can never ship silently.
            // The type's name goes in as the kit's per-export id: it is what makes this file's Live Debug
            // shortcut a DISTINCT entry in Edit ▸ Shortcuts rather than a duplicate registration of the one
            // the export next door already made. Inert for every other section.
            string kitBlock = ctx.UsedKitSections.Count > 0
                ? WidgetKitEmitter.Emit(ctx.UsedKitSections, targetType != null ? targetType.Name : null)
                : "";
            if (ctx.UsedKitSections.Count > 0 && string.IsNullOrEmpty(kitBlock))
                throw new InvalidOperationException(
                    "The widget-kit template (ExportedWidgetKit.cs) could not be read or parsed — reimport Sasquatch Inspector and export again. " +
                    "(If you have edited the template, note that only `#if SASQ_<NAME>` and " +
                    "`#if UNITY_<NAME>` / `#elif UNITY_<NAME>` / `#else`, each with its `#endif`, are " +
                    "supported inside the SASQ-KIT markers.)");

            var file = new CodeWriter();
            EmitHeader(file, ctx, dateStamp);
            // The whole file is editor-only code (Editor / [CustomEditor] / UnityEditor.UIElements). Users save it
            // wherever the save panel lands them — very often right next to their MonoBehaviour, which is NOT an
            // Editor folder and NOT an editor asmdef, so it compiles into Assembly-CSharp for the PLAYER too. There
            // UnityEditor doesn't exist and the build dies on CS0246 "CustomEditor could not be found" — and Unity
            // then misreads those unresolved types as renamed APIs and pops the "API Update Required" consent
            // dialog, which cannot fix it. The guard makes the file correct in every placement (UNITY_EDITOR is
            // defined in Editor folders, editor asmdefs, and the editor pass of Assembly-CSharp alike).
            file.Line("#if UNITY_EDITOR");
            EmitUsings(file, targetType);
            file.Line();
            // One flat namespace for every generated editor, with the class name carrying the target's namespace.
            // Two decisions, both load-bearing:
            //  * NAMESPACED at all, so exporting Weapons.Config and Audio.Config can't declare `ConfigEditor`
            //    twice in the global namespace (CS0101 — which breaks the export that was already working).
            //  * FLAT, rather than nested under the target's own namespace. Nesting puts the user's sibling
            //    namespaces in scope, and C# resolves an enclosing namespace before a using-directive: inside
            //    `MyGame.SasquatchGenerated`, a sibling `MyGame.Editor` namespace shadows UnityEditor.Editor and
            //    every `Editor`/`Button`/`Label` in the baked kit stops compiling (CS0118). A flat namespace has
            //    exactly one enclosing scope, and it contains nothing but our own `*Editor` classes.
            file.Line($"namespace {GeneratedNamespace}");
            file.OpenBrace();
            string editorFor = applyToSubclasses
                ? $"[global::UnityEditor.CustomEditor(typeof({ctx.TargetRef}), editorForChildClasses: true)]"
                : $"[global::UnityEditor.CustomEditor(typeof({ctx.TargetRef}))]";
            file.Line(editorFor);
            file.Line("[global::UnityEditor.CanEditMultipleObjects]");
            file.Line($"public class {ClassName(targetType)} : global::UnityEditor.Editor");
            file.OpenBrace();

            file.Comment("Marker: identifies this file as a Sasquatch-exported editor. With the package installed,");
            file.Comment("the Layout Designer reads it to offer the normal Force Takeover / Release toggle for this");
            file.Comment("type instead of warning about a competing third-party editor. Harmless without the package.");
            file.Line($"public const int {ExportMarker.FieldName} = {ExportMarker.Version};");
            file.Line();

            // Baked data several widgets share (today: the palette library). Class MEMBERS, above
            // CreateInspectorGUI, because a big layout splits into Build_* helper methods and a local declared
            // in one of them is invisible to the rest.
            foreach (var decl in ctx.Declarations)
            {
                file.AppendBlock(decl);
                file.Line();
            }

            file.Line("public override VisualElement CreateInspectorGUI()");
            file.OpenBrace();
            file.AppendBlock(body.ToString());
            file.CloseBrace();

            foreach (var method in ctx.Methods)
            {
                file.Line();
                file.AppendBlock(method);
            }

            file.Line();
            file.AppendBlock(ConditionEmitter.BakedHelpers(ctx));

            if (!string.IsNullOrEmpty(kitBlock))
            {
                file.Line();
                file.Comment("--- Sasquatch standalone widget kit (baked copy of the package's default look) ---");
                file.AppendBlock(kitBlock);
            }

            file.CloseBrace();  // class
            file.CloseBrace();  // namespace
            file.Line("#endif // UNITY_EDITOR");
            return file.ToString();
        }

        /// <summary>
        /// First export only: turn on the per-type Force Takeover so the inspector keeps showing the live
        /// Sasquatch layout once the new <c>[CustomEditor]</c> compiles — without it, Unity's editor resolution
        /// would silently swap the type over to the baked copy the moment the file imports. Re-exports (the type
        /// already has a genuine per-type editor) preserve the user's existing takeover/release choice: someone
        /// who Released to review the baked copy should stay released when they refresh it.
        /// </summary>
        internal static void EnableTakeoverForFirstExport(Type targetType)
        {
            if (targetType == null) return;
            if (SasquatchGeneratedEditors.HasGenuinePerTypeEditor(targetType)) return;
            if (!LayoutRegistry.IsForceTakeoverOwn(targetType))
                LayoutRegistry.SetForceTakeover(targetType, true);
        }

        /// <summary>
        /// Resolves the type's layout, prompts for a save location, warns about in-project deference, then writes +
        /// imports the file. Pass <paramref name="assetOverride"/> (the designer's live working clone) to export the
        /// on-screen layout rather than the last-saved one; otherwise the saved layout is resolved from the registry.
        /// </summary>
        public static void ExportWithDialog(Type targetType, InspectorLayoutAsset assetOverride = null)
        {
            if (targetType == null) return;

            InspectorLayoutAsset asset = (assetOverride != null && assetOverride.Root != null) ? assetOverride : null;
            if (asset == null && (!LayoutRegistry.TryGet(targetType, out asset) || asset == null || asset.Root == null))
            {
                EditorUtility.DisplayDialog("Export Standalone Editor",
                    $"No Sasquatch Inspector layout was found for {targetType.Name}.", "OK");
                return;
            }

            // A corrupted (cyclic) layout is refused by GenerateSource before it can overflow the stack.
            // Caught here so the user gets the reason in a dialog rather than a console exception after
            // they have already clicked Export — and BEFORE the save-file panel, so nothing is written.
            string source;
            try
            {
                source = GenerateSource(targetType, asset.Root, DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                                        asset.ApplyToSubclasses);
            }
            catch (InvalidOperationException ex)
            {
                EditorUtility.DisplayDialog("Export Standalone Editor", ex.Message, "OK");
                return;
            }

            string defaultDir = ResolveDefaultDir(asset);
            string path = EditorUtility.SaveFilePanelInProject(
                "Export Standalone Editor Script",
                targetType.Name + "Editor",
                "cs",
                "Choose where to save the generated [CustomEditor] script — a self-contained baked copy of this layout that needs no Sasquatch Inspector package to compile or run.",
                defaultDir);
            if (string.IsNullOrEmpty(path)) return;

            bool proceed = EditorUtility.DisplayDialog(
                "Standalone Editor for " + targetType.Name,
                $"This creates a real [CustomEditor] for {targetType.Name} — a self-contained baked copy of this " +
                "layout with no package dependency.\n\n" +
                "In THIS project, Sasquatch Inspector keeps drawing your live layout over it via Force Takeover " +
                "(turned on for you on a first export). Use Release in the designer's notice bar any time to " +
                "preview exactly what the exported file renders on its own.\n\n" +
                "Teammates without Sasquatch Inspector just add this file (plus your script) and get the same " +
                "baked inspector.\n\nContinue?",
                "Export", "Cancel");
            if (!proceed) return;

            File.WriteAllText(path, source);
            AssetDatabase.ImportAsset(path);

            // Keep the live layout on screen after the new [CustomEditor] compiles (first export only — a
            // re-export never overrides an explicit Release; see EnableTakeoverForFirstExport).
            EnableTakeoverForFirstExport(targetType);

            string asmName = targetType.Assembly.GetName().Name;
            if (asmName != "Assembly-CSharp")
            {
                Debug.LogWarning($"[Sasquatch Inspector] {targetType.Name} lives in assembly '{asmName}'. " +
                    $"Place '{Path.GetFileName(path)}' under an Editor assembly definition that references '{asmName}' so it compiles.");
            }

            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj != null) EditorGUIUtility.PingObject(obj);
            Debug.Log($"[Sasquatch Inspector] Exported standalone editor for {targetType.Name} → {path}");
        }

        // ---- Assembly helpers ------------------------------------------------------------------------------

        /// <summary>The one namespace every generated editor lives in — see the comment at the emit site.</summary>
        private const string GeneratedNamespace = "SasquatchGenerated";

        /// <summary>
        /// The generated class name. The target's namespace is flattened into the name (<c>Weapons.Config</c> →
        /// <c>Weapons_ConfigEditor</c>) so two same-short-named targets stay distinct inside the one flat
        /// namespace. The saved FILE is still named after the short type name, so this is invisible in practice —
        /// Unity resolves a custom editor by attribute, never by class name.
        /// </summary>
        private static string ClassName(Type t)
        {
            string ns = t.Namespace;
            string prefix = string.IsNullOrEmpty(ns) ? "" : ns.Replace('.', '_') + "_";
            return prefix + t.Name.Replace('+', '_') + "Editor";
        }

        /// <summary>
        /// Hands the generated root to the kit's label-column engine and bakes the project's Label Column
        /// setting into it. Live drives EVERY field row from one capped formula (FieldLabelColumn); without
        /// this the export keeps Unity's fully proportional stock column, so at an 800px-wide Inspector the
        /// buyer's label gutter grows to ~323px where the layout the designer approved stops at 180.
        ///
        /// The setting travels as a value, not a dependency: the kit's own default already IS the package
        /// default (Capped at 180), so only a project that changed it emits an override line. Claiming Core
        /// is free in practice — every non-empty layout already pulls it in — but it is claimed explicitly
        /// rather than assumed, because the call would be a CS0103 in a file with no SasqKit class.
        /// </summary>
        private static void EmitLabelColumn(CodeWriter w, EmitContext ctx)
        {
            ctx.UseKit(null);   // Core

            var settings = SasquatchSettingsManager.Current;
            if (settings != null)
            {
                if (settings.LabelColumnMode == LabelColumnMode.UnityStandard)
                    w.Line("SasqKit.LabelColumnCap = 0f;   // Label Column: Unity Standard");
                else if (settings.MaxLabelColumnWidth != 180)
                    w.Line($"SasqKit.LabelColumnCap = {Lit.F(settings.MaxLabelColumnWidth)};");
            }
            w.Line("SasqKit.AdoptLabelColumn(root);");
        }

        /// <summary>
        /// Returns the body inside the kit's horizontal-overflow rescue — the twin of what BOTH live render
        /// paths do (VisualInspectorEditor / SasquatchForceTakeover both wrap in HorizontalOverflowRescue).
        /// Unity's Inspector scrolls vertically only, so a row that paints past the right edge is unreachable
        /// rather than scrollable; live shows a slim pan rail while that lasts. Without this the buyer — who
        /// has no package to diagnose with — silently loses the content instead.
        ///
        /// Unconditional, like the live wiring and for the same reason: painted overflow has too many sources
        /// for the emitter to predict, and a net that guesses wrong is worse than none. Claims Core explicitly
        /// rather than assuming EmitLabelColumn already did, since the call would be a CS0103 in a file with
        /// no SasqKit class. Layout-neutral while everything fits, so it never moves a layout that already fits.
        /// </summary>
        private static void EmitReturnRoot(CodeWriter w, EmitContext ctx)
        {
            ctx.UseKit(null);   // Core
            w.Line("return SasqKit.WrapOverflowRescue(root);");
        }

        /// <summary>
        /// True when <paramref name="root"/> carries a Script Header with its search magnifier on. The header is
        /// the field search's SOLE entry point — no Ctrl+F, no separate bar — so this one question decides
        /// whether the export ships the search at all.
        /// </summary>
        private static bool HasSearchEnabledHeader(LayoutNode node)
        {
            if (node == null) return false;
            if (node is ScriptHeaderNode header) return header.ShowSearch;
            if (node is ContainerNode container && container.Children != null)
                foreach (var child in container.Children)
                    if (HasSearchEnabledHeader(child)) return true;
            return false;
        }

        /// <summary>
        /// Wires the field search onto the finished body. Emitted AFTER <c>root.Bind(so)</c>, mirroring live:
        /// the engine walks the built tree, so everything it indexes has to exist first.
        /// </summary>
        private static void EmitAttachSearch(CodeWriter w, EmitContext ctx)
        {
            if (!ctx.SearchEnabled) return;
            ctx.UseKit("Search");
            w.Line($"SasqKit.AttachSearch(root, {Lit.Str(ctx.SearchGlyphPng)});");
        }

        /// <summary>
        /// Wires Live Debug onto the finished body: the baked glyph set, then the call that slots the bug into
        /// the Script Header and installs the transport + redraw tick. Emitted AFTER <c>root.Bind(so)</c> for
        /// the same reason the search is — the engine walks the built tree, so everything has to exist first.
        /// </summary>
        private static void EmitAttachLiveDebug(CodeWriter w, EmitContext ctx)
        {
            if (!ctx.DebugEnabled) return;
            ctx.UseKit("LiveDebug");

            // Fourteen glyphs, baked WHITE and tinted at runtime, so one payload covers both editor skins AND
            // the accent "recording" state. The buyer's project has no icon pipeline; this IS the icon set.
            w.Line("SasqKit.DebugIcons(");
            w.Line($"    bug: \"{DebugGlyph(SasquatchIcons.Ids.Bug)}\",");
            w.Line($"    record: \"{DebugGlyph(SasquatchIcons.Ids.CircleDot)}\",");
            w.Line($"    stop: \"{DebugGlyph(SasquatchIcons.Ids.Square)}\",");
            w.Line($"    first: \"{DebugGlyph(SasquatchIcons.Ids.SkipBack)}\",");
            w.Line($"    prev: \"{DebugGlyph(SasquatchIcons.Ids.ChevronLeft)}\",");
            w.Line($"    next: \"{DebugGlyph(SasquatchIcons.Ids.ChevronRight)}\",");
            w.Line($"    last: \"{DebugGlyph(SasquatchIcons.Ids.SkipForward)}\",");
            w.Line($"    settings: \"{DebugGlyph(SasquatchIcons.Ids.Settings)}\",");
            w.Line($"    copy: \"{DebugGlyph(SasquatchIcons.Ids.Copy)}\",");
            w.Line($"    save: \"{DebugGlyph(SasquatchIcons.Ids.Download)}\",");
            w.Line($"    erase: \"{DebugGlyph(SasquatchIcons.Ids.Eraser)}\",");
            w.Line($"    close: \"{DebugGlyph(SasquatchIcons.Ids.X)}\",");
            w.Line($"    info: \"{DebugGlyph(SasquatchIcons.Ids.Info)}\",");
            w.Line($"    keys: \"{DebugGlyph(SasquatchIcons.Ids.Terminal)}\");");
            w.Line("SasqKit.AttachLiveDebug(root, so);");
        }

        /// <summary>One Live Debug glyph as a base64 PNG. White, because the kit tints it — see the caller.</summary>
        private static string DebugGlyph(string iconId)
        {
            var tex = SasquatchIcons.RenderToTexture(iconId, 32, Color.white);
            return tex != null ? System.Convert.ToBase64String(tex.EncodeToPNG()) : "";
        }

        private static void EmitHeader(CodeWriter f, EmitContext ctx, string dateStamp)
        {
            f.Comment("<auto-generated>");
            f.Comment($"Sasquatch Inspector — exported standalone custom editor for {ctx.TargetType.Name}.");
            if (!string.IsNullOrEmpty(dateStamp)) f.Comment($"Generated: {dateStamp}");
            f.Comment("Self-contained: draws a faithful baked copy of the layout, depending only on");
            f.Comment("UnityEditor / UnityEngine / UI Toolkit — no Sasquatch Inspector package required.");
            f.Comment("With the package installed, the type's Force Takeover setting decides what the inspector");
            f.Comment("shows: taking over = the live designer layout draws in place of this editor's body;");
            f.Comment("released = this file's baked copy renders (exactly what package-less teammates see).");
            f.Comment("Re-run Export Editor after layout changes to refresh this baked copy.");
            if (ctx.Notes.Count > 0)
            {
                f.Comment("");
                f.Comment("Fidelity notes (this baked copy vs. the in-package rendering):");
                foreach (var note in ctx.Notes) f.Comment("  * " + note);
            }
            f.Comment("</auto-generated>");
        }

        // Only the stock Unity namespaces are imported. The target type's own namespace is deliberately NOT
        // pulled in: it would put every user type in scope beside UnityEngine's, so a script named Grid, Light,
        // Camera, Animation or Tree would make its own name ambiguous (CS0104) and the generated file would never
        // compile. The target is named through EmitContext.TargetRef (global::-qualified) instead.
        private static void EmitUsings(CodeWriter f, Type t)
        {
            f.Line("using System;");
            f.Line("using System.Collections.Generic;");
            f.Line("using UnityEngine;");
            f.Line("using UnityEngine.UIElements;");
            f.Line("using UnityEditor;");
            f.Line("using UnityEditor.UIElements;");
        }

        private static string ResolveDefaultDir(InspectorLayoutAsset asset)
        {
            if (asset != null && !string.IsNullOrEmpty(asset.ScriptGuid))
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(asset.ScriptGuid);
                if (!string.IsNullOrEmpty(scriptPath))
                {
                    string dir = Path.GetDirectoryName(scriptPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        dir = dir.Replace('\\', '/');
                        string editorSub = dir + "/Editor";
                        if (AssetDatabase.IsValidFolder(editorSub)) return editorSub;
                        return dir;
                    }
                }
            }
            if (AssetDatabase.IsValidFolder("Assets/Editor")) return "Assets/Editor";
            return "Assets";
        }
    }
}
