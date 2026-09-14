using System;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor.Export
{
    /// <summary>
    /// Emits a node's <see cref="LayoutStyle"/> / <see cref="TextStyle"/> as inline <c>element.style.*</c> C#.
    /// Only OVERRIDDEN properties are written (unset ones fall through to the element's UITK defaults, exactly as
    /// the live <see cref="StyleApplier"/> / <see cref="TextStyleApplier"/> do). Theme-token base styles are NOT
    /// resolved — the exported file has no theme, so only a node's own explicit overrides travel with it.
    /// </summary>
    internal static class StyleEmitter
    {
        /// <summary>
        /// Translates a <see cref="ButtonSize"/> to the int the exported kit's <c>SasqKit.StyleButton</c>
        /// takes: 0 small, 1 medium, 2 large, 4 huge (3 is unused — it briefly held a "Tiny" class that
        /// was cut before release). Deliberately NOT a cast — the model's values are frozen by
        /// serialization (Medium = 0) while the kit's are frozen by every file already exported
        /// (medium = 1), so the two orderings can never be unified. Shared by the Button widget and the
        /// stepper / normalize buttons.
        /// </summary>
        public static int ButtonKitSize(ButtonSize size)
        {
            switch (size)
            {
                case ButtonSize.Small: return 0;
                case ButtonSize.Large: return 2;
                case ButtonSize.Huge: return 4;
                default: return 1; // Medium
            }
        }

        /// <summary>Writes the overridden layout-style setters for <paramref name="node"/> onto local variable
        /// <paramref name="v"/>. Pass <paramref name="ctx"/> and <paramref name="resolvedPath"/> for a bound
        /// field so a C# <c>[Tooltip]</c> can be resolved and baked; without them only an explicit designer
        /// tooltip travels, which is the correct behaviour for containers and other fieldless nodes.</summary>
        public static void EmitLayoutStyle(CodeWriter w, string v, LayoutNode node,
            EmitContext ctx = null, string resolvedPath = null)
        {
            var s = node.Style;

            if (s.flexDirection.isOverridden) w.Line($"{v}.style.flexDirection = FlexDirection.{s.flexDirection.value};");
            if (s.alignSelf.isOverridden)
            {
                w.Line($"{v}.style.alignSelf = Align.{s.alignSelf.value};");
                // Live reads the SAME override to hand the row back to itself (FieldLabelColumn's
                // `drive = !node.Style.alignSelf.isOverridden`): a row told to hug an edge has to size its
                // label to the text, or the column's forced width pins it back where it started.
                w.Line($"SasqKit.ReleaseLabelColumn({v});");
            }
            if (s.alignItems.isOverridden) w.Line($"{v}.style.alignItems = Align.{s.alignItems.value};");
            if (s.justify.isOverridden) w.Line($"{v}.style.justifyContent = Justify.{s.justify.value};");

            if (s.borderRadius.isOverridden)
            {
                string r = Lit.F(s.borderRadius.value);
                w.Line($"{v}.style.borderTopLeftRadius = {r};");
                w.Line($"{v}.style.borderTopRightRadius = {r};");
                w.Line($"{v}.style.borderBottomLeftRadius = {r};");
                w.Line($"{v}.style.borderBottomRightRadius = {r};");
            }
            if (s.borderWidth.isOverridden)
            {
                string bw = Lit.F(s.borderWidth.value);
                w.Line($"{v}.style.borderTopWidth = {bw};");
                w.Line($"{v}.style.borderBottomWidth = {bw};");
                w.Line($"{v}.style.borderLeftWidth = {bw};");
                w.Line($"{v}.style.borderRightWidth = {bw};");
            }

            if (s.backgroundColor.isOverridden) w.Line($"{v}.style.backgroundColor = {Lit.Col(s.backgroundColor.value)};");
            if (s.borderColor.isOverridden)
            {
                string bc = Lit.Col(s.borderColor.value);
                w.Line($"{v}.style.borderTopColor = {bc};");
                w.Line($"{v}.style.borderBottomColor = {bc};");
                w.Line($"{v}.style.borderLeftColor = {bc};");
                w.Line($"{v}.style.borderRightColor = {bc};");
            }

            if (s.marginTop.isOverridden) w.Line($"{v}.style.marginTop = {Lit.F(s.marginTop.value)};");
            if (s.marginBottom.isOverridden) w.Line($"{v}.style.marginBottom = {Lit.F(s.marginBottom.value)};");
            if (s.marginLeft.isOverridden) w.Line($"{v}.style.marginLeft = {Lit.F(s.marginLeft.value)};");
            if (s.marginRight.isOverridden) w.Line($"{v}.style.marginRight = {Lit.F(s.marginRight.value)};");

            if (s.paddingTop.isOverridden) w.Line($"{v}.style.paddingTop = {Lit.F(s.paddingTop.value)};");
            if (s.paddingBottom.isOverridden) w.Line($"{v}.style.paddingBottom = {Lit.F(s.paddingBottom.value)};");
            if (s.paddingLeft.isOverridden) w.Line($"{v}.style.paddingLeft = {Lit.F(s.paddingLeft.value)};");
            if (s.paddingRight.isOverridden) w.Line($"{v}.style.paddingRight = {Lit.F(s.paddingRight.value)};");

            // Mirrors StyleApplier: a stored Width is a designed width, capped at the parent's content box
            // so a node designed wider than the inspector shrinks instead of painting over its neighbours.
            if (s.width.isOverridden)
            {
                w.Line($"{v}.style.width = {Lit.F(s.width.value)};");
                w.Line($"{v}.style.maxWidth = Length.Percent(100f);");
            }

            // Containers (and height-resize nodes) treat a stored height as a grow-only floor (min-height), never a
            // fixed height — mirrors StyleApplier so flex-shrink children can never overlap.
            if (s.height.isOverridden)
            {
                bool heightIsFloor = node is ContainerNode || node.MakeHeightResizable;
                string prop = heightIsFloor ? "minHeight" : "height";
                w.Line($"{v}.style.{prop} = {Lit.F(s.height.value)};");
            }

            if (s.flexGrow.isOverridden) w.Line($"{v}.style.flexGrow = {Lit.F(s.flexGrow.value)};");
            if (s.flexShrink.isOverridden) w.Line($"{v}.style.flexShrink = {Lit.F(s.flexShrink.value)};");

            // AFTER the padding writes above, exactly as live runs AttachNodeResizeHandles after StyleApplier:
            // the grip reserves 8px on its own edge only when the node did not already author more.
            EmitNodeResizeGrips(w, v, node, ctx);

            EmitTooltip(w, v, node, ctx, resolvedPath);
        }

        /// <summary>
        /// Emits the node's opt-in drag grips (<see cref="LayoutNode.MakeWidthResizable"/> /
        /// <see cref="LayoutNode.MakeHeightResizable"/>) — the live builder's <c>AttachNodeResizeHandles</c>.
        /// These are an INSPECTOR feature, not a designer-only one, so an export that carried the stored size
        /// but no grip left the buyer unable to drag a field the layout's author expected them to drag, and
        /// said nothing about it.
        /// <para>
        /// The persistence half deliberately does not travel: live writes the dragged size back into the
        /// layout asset, and a standalone editor has no layout asset. The exported grip resizes for the
        /// session, which is what the exported table columns and Columns/Grid tracks have always done. That is
        /// not worth a fidelity note — a note is only honest when it describes something the buyer will
        /// actually notice was missing, and a size that does not survive a rebuild reads as ordinary inspector
        /// behaviour rather than as a lost feature.
        /// </para>
        /// </summary>
        private static void EmitNodeResizeGrips(CodeWriter w, string v, LayoutNode node, EmitContext ctx)
        {
            bool wResize = node.MakeWidthResizable;
            // Same gate as the live AttachNodeResizeHandles: an aspect-locked frame's height is derived from
            // its width, so an emitted bottom grip would be just as inert in a standalone editor.
            bool hResize = node.MakeHeightResizable && !FrameBuilder.AspectLocked(node);
            if (!wResize && !hResize) return;   // opt-in, and both default false: defaults emit nothing

            // The width grip sits on the node's FREE edge — the left one when the node hugs the right, so it
            // is never trapped against the container wall. Same read live makes.
            bool leftEdge = wResize && node.Style.alignSelf.isOverridden
                && node.Style.alignSelf.value == UnityEngine.UIElements.Align.FlexEnd;

            string args = wResize ? "width: true" : string.Empty;
            if (hResize) args += (args.Length > 0 ? ", " : string.Empty) + "height: true";
            if (leftEdge) args += ", leftEdge: true";

            ctx?.UseKit(null); // Core (NodeResizeGrips)
            w.Line($"SasqKit.NodeResizeGrips({v}, {args});");
        }

        /// <summary>
        /// Resolves the tooltip the way live's BindingFactory does — blanked wins, then a designer override,
        /// then the field's C# <c>[Tooltip]</c> — and bakes the ANSWER, since the exported file cannot reflect
        /// in the buyer's project.
        /// <para>
        /// Three things this has to get right, all of which a plain <c>v.tooltip = "…"</c> gets wrong.
        /// A code <c>[Tooltip]</c> only surfaces by itself on a Default widget's PropertyField, so on
        /// any other widget it is simply lost. A BLANKED tooltip has to overwrite with the empty string
        /// rather than merely skip the assignment, because a PropertyField surfaces the C# attribute on its
        /// own and "don't set ours" does not suppress it. And it has to out-rank the control's own stamped
        /// tooltip at HOVER time, from one stable anchor — which is what the kit's claim does (SasqKit's
        /// SetTooltip / SetNodeTooltip, twins of live's <see cref="TooltipAnchor"/>).
        /// </para>
        /// <para>
        /// The two kit calls split the way live's do: a bound widget's claim outranks the stock chrome inside
        /// it, a container's yields to whatever the pointer is really over. Without a <paramref name="ctx"/>
        /// there is no kit to claim, so a fieldless node falls back to the plain assignment — still stable,
        /// just anchored to its own box rather than to its title.
        /// </para>
        /// </summary>
        private static void EmitTooltip(CodeWriter w, string v, LayoutNode node, EmitContext ctx, string resolvedPath)
        {
            if (node.IsTooltipHidden)
            {
                // Only a bound field can carry a C# [Tooltip] to suppress; a fieldless node has nothing to
                // overwrite, so the house "defaults emit nothing" idiom applies and we stay silent.
                if (ctx == null || string.IsNullOrEmpty(resolvedPath)) return;
                if (ResolveCodeTooltip(ctx, resolvedPath) == null) return;
                ctx.UseKit(null); // Core (SetTooltip)
                w.Line($"SasqKit.SetTooltip({v}, \"\");");
                return;
            }

            string effective = node.Tooltip;
            if (string.IsNullOrEmpty(effective) && ctx != null && !string.IsNullOrEmpty(resolvedPath))
                effective = ResolveCodeTooltip(ctx, resolvedPath);
            if (string.IsNullOrEmpty(effective)) return;

            if (ctx == null)
            {
                // No context to claim Core with (fieldless call sites) — the plain assignment is what live
                // does for those anyway.
                w.Line($"{v}.tooltip = {Lit.Str(effective)};");
                return;
            }
            ctx.UseKit(null); // Core (SetTooltip)
            w.Line(node is BindingNode
                ? $"SasqKit.SetTooltip({v}, {Lit.Str(effective)});"
                : $"SasqKit.SetNodeTooltip({v}, {Lit.Str(effective)});");
        }

        /// <summary>The field's declared <c>[Tooltip]</c> text, or null. Read at EXPORT time — the same
        /// bake-the-answer rule every other attribute the export carries follows.</summary>
        private static string ResolveCodeTooltip(EmitContext ctx, string resolvedPath)
        {
            var field = ctx.ResolveField(resolvedPath);
            if (field == null) return null;
            var attr = Attribute.GetCustomAttribute(field, typeof(TooltipAttribute)) as TooltipAttribute;
            return string.IsNullOrEmpty(attr?.tooltip) ? null : attr.tooltip;
        }

        /// <summary>Writes the overridden inherited text-style setters in <paramref name="t"/> onto local variable <paramref name="v"/>.</summary>
        public static void EmitTextStyle(CodeWriter w, string v, TextStyle t, EmitContext ctx)
        {
            if (t.fontStyle.isOverridden) w.Line($"{v}.style.unityFontStyleAndWeight = FontStyle.{t.fontStyle.value};");
            if (TextStyleApplier.IsUsableFontSize(t.fontSize)) w.Line($"{v}.style.fontSize = {Lit.F(t.fontSize.value)};");
            if (t.color.isOverridden) w.Line($"{v}.style.color = {Lit.Col(t.color.value)};");
            if (t.align.isOverridden) w.Line($"{v}.style.unityTextAlign = TextAnchor.{t.align.value};");
            if (t.font != null) EmitFont(w, v, t.font, ctx);
        }

        /// <summary>
        /// Bakes a custom Font as a GUID lookup — the same mechanism the image banner uses
        /// (<c>LayoutSourceEmitter</c>'s <c>TextureGuid</c> branch), and for the same reason: a GUID survives the
        /// asset being moved or renamed in the receiving project, where a baked path would not.
        /// <para>
        /// <c>unityFontDefinition</c>, never <c>unityFont</c>. Live's <see cref="TextStyleApplier"/> documents
        /// why: an inherited font DEFINITION out-ranks a plain <c>unityFont</c>, so setting only the latter
        /// leaves the custom font invisible wherever anything up the tree defines one — the export would look
        /// correct in isolation and wrong in a real inspector.
        /// </para>
        /// <para>
        /// The GUID read is a direct lookup on an already-loaded object, not an AssetDatabase scan, so it keeps
        /// <see cref="StandaloneEditorExporter.GenerateSource"/> deterministic — the same latitude
        /// <c>EmitPalette</c> already takes when it resolves the swatch library.
        /// </para>
        /// </summary>
        private static void EmitFont(CodeWriter w, string v, Font font, EmitContext ctx)
        {
            string assetPath = AssetDatabase.GetAssetPath(font);
            string guid = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath);

            // A built-in editor font lives inside the Unity install rather than the asset database, so it has no
            // GUID the buyer's project could resolve and there is nothing to hand them. That case stays a note —
            // and it is an honest one, because it names something they genuinely cannot be given.
            if (string.IsNullOrEmpty(guid))
            {
                ctx.Note("A custom Font that is not a project asset (a built-in editor font) was not exported — "
                       + "assign a font from the project to bake it.");
                return;
            }

            // An asset-dependency ADVISORY, not a degradation — the same shape as the image banner's, which the
            // fidelity-note allow-list already recognises as one. The font renders at full fidelity once the
            // asset travels with the file.
            ctx.Note($"A custom Font references '{System.IO.Path.GetFileName(assetPath)}' by GUID — include that "
                   + "font asset in the shared project.");

            string p = ctx.NewVar("fontPath", "fontPath");
            string f = ctx.NewVar("fontAsset", "fontAsset");
            w.Line($"var {p} = AssetDatabase.GUIDToAssetPath({Lit.Str(guid)});");
            w.Line($"var {f} = AssetDatabase.LoadAssetAtPath<Font>({p});");
            // Missing from the receiving project ⇒ stay on the inherited font rather than blanking the text.
            // Same degrade-don't-throw rule the image banner's missing texture follows.
            w.Line($"if ({f} != null) {v}.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont({f}));");
        }

        /// <summary>True when the text style has at least one override worth emitting.</summary>
        /// <remarks>
        /// The font is part of this test, exactly as it is in live's <c>TextStyleApplier.HasOverride</c>. Leaving
        /// it out meant a style whose ONLY override was a custom font read as "no overrides at all", so every
        /// caller below skipped <see cref="EmitTextStyle"/> outright — the font never emitted AND the old
        /// fidelity note never fired, so the export was silent about it too.
        /// </remarks>
        public static bool HasTextOverride(TextStyle t) =>
            t.fontStyle.isOverridden || TextStyleApplier.IsUsableFontSize(t.fontSize) || t.color.isOverridden
            || t.align.isOverridden || t.font != null;
    }
}
