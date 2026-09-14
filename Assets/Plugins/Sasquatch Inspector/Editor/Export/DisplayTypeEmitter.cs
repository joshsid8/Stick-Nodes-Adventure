using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor.Export
{
    /// <summary>
    /// Maps a <see cref="BindingNode"/>'s <see cref="DisplayType"/> to emitted UI-Toolkit code. Faithful widgets
    /// come from the embedded standalone kit (<see cref="WidgetKitEmitter"/>) with the node's configuration and
    /// theme colors baked into the call site; types Unity's stock <c>PropertyField</c> already renders well
    /// (enums, colors, curves, gradients, vectors, custom drawers) stay on <c>PropertyField</c>. Only the
    /// genuinely package-bound widgets (Spring graph, live audio/particle previews, custom widgets) degrade,
    /// each with an honest fidelity note.
    /// </summary>
    internal static class DisplayTypeEmitter
    {
        /// <summary>Emits the control for <paramref name="node"/> into <paramref name="w"/> and returns its local variable name.</summary>
        public static string Emit(CodeWriter w, EmitContext ctx, BindingNode node, string path, string defaultLabel)
        {
            FieldInfo fi = ctx.ResolveField(path);
            // ResolveFieldType, not fi?.FieldType: an ELEMENT-TEMPLATE binding has an empty path and so no
            // FieldInfo of its own, but it does have a type — the collection's element type — and every
            // widget case below is gated on that type. Reading it off `fi` alone made a styled
            // List<Vector2> / List<SomeEnum> export as a bare field.
            Type ft = ctx.ResolveFieldType(path);
            // A TABLE CELL never shows a label — the column header already names the value, which is why live
            // passes forceHideLabel for every cell. Without this a Slider / Rating / Toggle column exported its
            // header text a SECOND time inside every cell of every row (the kit's own stock cell already uses
            // ""), so the authored-widget columns read doubled while the plain ones did not.
            //
            // An ELEMENT-TEMPLATE binding in a LIST ROW takes the same rule for the same reason: the row
            // already carries its [index], and the stock display name for an array element is the word
            // "Element" plus that same index. An explicit Label override still wins, exactly as live's
            // element-row label rule does — keep the two in step.
            //
            // A DICTIONARY CELL takes the same rule as a table cell and for the same reason — the column
            // header names it, and live builds it with forceHideLabel — but it cannot be detected the same
            // way, because its path arrives through RowBaseExpr rather than CellPathExpr. It also has to
            // beat the element-self arm below it: a cell template carries a "Key" / "Value" Label override
            // so the DESIGNER'S HIERARCHY has a name for the row, and printing that inside every cell of
            // every row is exactly the doubling this whole block exists to stop.
            bool elementSelfRow = ctx.RowBaseExpr != null && string.IsNullOrEmpty(path);
            string label = ctx.CellPathExpr != null || ctx.DictionaryCell ? ""
                : elementSelfRow ? (node.EffectiveLabelOverride ?? "")
                : StockLabel(node, defaultLabel);

            // A SpringConfig field is ALWAYS the Spring widget, whatever the node's DisplayType says. Live
            // auto-routes it ahead of everything else (BindingFactory's IsSpringConfigProperty branch, which
            // deliberately wins over the custom-drawer guard) because the type has no useful Unity default
            // and its own SpringConfigDrawer is superseded — so "even a Default or legacy node renders
            // Spring". Without this arm a DisplayType.Default SpringConfig node — which the designer cannot
            // author today but an older layout can carry — would render the widget live and a stock struct
            // foldout in the export: a render-path parity gap opened by porting the widget, not closed by it.
            if (IsSpringConfigField(ft)) return EmitSpringWell(w, ctx, node, path, label);

            // A SerializedDictionary field is ALWAYS the Sasquatch Key/Value table, on exactly the SpringConfig
            // precedent above. Live auto-routes it ahead of the custom-drawer arm — which is what used to catch
            // it, as a PropertyField into its own drawer, and is precisely why the node's Dictionary Style and
            // Row Frame could not reach it — so the export has to route it the same way or a Default-typed
            // dictionary renders the styled table live and the drawer's unstyled one in a generated editor.
            // UNGATED: this shape is ours, so it needs none of the 6.6 dictionary APIs and works on the floor.
            if (ft != null && typeof(SerializedDictionaryBase).IsAssignableFrom(ft))
                return EmitDictionaryTable(w, ctx, node, path, label, fi, ft, native: false);

            switch (node.DisplayType)
            {
                case DisplayType.Slider:
                    if (DisplayTypeCatalog.IsSliderNumeric(ft))
                        return EmitSlider(w, ctx, node, path, fi, label, isInt: IsIntegralSlider(ft));
                    break;

                case DisplayType.MinMaxSlider:
                    if (ft == typeof(Vector2) || ft == typeof(Vector2Int))
                        return EmitMinMax(w, ctx, node, path, label);
                    break;

                case DisplayType.ProgressBar:
                    // SasqKit.Progress reads and writes through ReadNumeric/WriteNumeric, so every numeric
                    // width it accepts is edited at native precision.
                    if (DisplayTypeCatalog.IsSliderNumeric(ft))
                        return EmitProgress(w, ctx, node, path, fi, label);
                    break;

                case DisplayType.Rating:
                    if (ft == typeof(int)) return EmitRating(w, ctx, node, path, label);
                    break;

                case DisplayType.Toggle:
                    if (ft == typeof(bool))
                    {
                        var tds = node.DisplayStyleData;
                        // A stock Checkbox stays on PropertyField unless it carries styling the kit must
                        // apply in place (outline / tick tint / size) — mirrors the live checkbox branch.
                        bool checkboxStyled = tds.toggleOutlineColor.a > 0f || tds.toggleKnobColor.a > 0f
                            || tds.toggleSize != SliderSize.Normal;
                        if (tds.toggleStyle != ToggleStyle.Checkbox || checkboxStyled)
                            return EmitToggle(w, ctx, node, path, label);
                    }
                    break;

                case DisplayType.TagChips:
                    if (IsStringCollection(ft)) return EmitTagChips(w, ctx, node, path, label);
                    break;

                case DisplayType.EnumBadgeGrid:
                    if (ft != null && ft.IsEnum) return EmitEnumBadges(w, ctx, node, path, label, ft);
                    break;

                case DisplayType.ColorPalette:
                    // Color32 too: DisplayTypeCatalog offers the palette on both, and Unity surfaces a Color32
                    // as SerializedPropertyType.Color, so the kit's colorValue read/write round-trips it exactly
                    // as the live widget's TryReadPaletteColor does. Guarding on Color alone silently demoted a
                    // Color32 palette to a plain swatch with no fidelity note.
                    if (ft == typeof(Color) || ft == typeof(Color32)) return EmitPalette(w, ctx, node, path, label);
                    break;

                case DisplayType.AnimationCurveEditor:
                    // Guarded on the FIELD type here and on the PROPERTY type again inside the kit, for the
                    // same two reasons the gradient case gives: an element template has no FieldInfo at all,
                    // and the kit's own check is what protects an export whose target changed shape later.
                    if (ft == typeof(AnimationCurve)) return EmitCurve(w, ctx, node, path, label);
                    break;

                case DisplayType.Gradient:
                    // Guarded on the FIELD type here and on the PROPERTY type again inside the kit. Both are
                    // needed: this one keeps a Gradient-less field (an element template has no FieldInfo at
                    // all) out of the kit call, and the kit's own check is what protects an exported editor
                    // whose target type changed shape after the export was generated.
                    if (ft == typeof(Gradient)) return EmitGradient(w, ctx, node, path, label);
                    break;

                case DisplayType.XYPad:
                    if (ft == typeof(Vector2) || ft == typeof(Vector2Int))
                        return EmitXYPad(w, ctx, node, path, fi, label);
                    break;

                case DisplayType.VectorSliders:
                    if (ft == typeof(Vector3) || ft == typeof(Vector4))
                        return EmitVectorSliders(w, ctx, node, path, label, isVec4: ft == typeof(Vector4));
                    break;

                case DisplayType.EulerAngles:
                    if (ft == typeof(Quaternion)) return EmitEuler(w, ctx, node, path, label);
                    break;

                case DisplayType.Spring:
                    // Structural, NOT `ft == typeof(SpringConfig)`. The kit reaches every value through
                    // FindPropertyRelative by name and never references the type, so what it can actually
                    // drive is "a serialized struct with float Duration + float Bounce" — and that is
                    // exactly live's own opening guard, expressed at export time instead of at draw time.
                    // Anything else (the kitchen sink deliberately parks Spring on an int) falls through to
                    // the degrade note below, so the guard has a pinned failure case as well as a pass one.
                    if (IsSpringShaped(ft)) return EmitSpringWell(w, ctx, node, path, label);
                    break;

                case DisplayType.DropDown:
                    return EmitDropdown(w, ctx, node, path, ft, label);

                case DisplayType.SerializeReference:
                    if (ft != null && !ft.IsGenericType) return EmitSRef(w, ctx, node, path, label, ft);
                    break;

                // The NATIVE table, over Unity 6.6's own Dictionary<K,V>.
                //
                // DELIBERATELY NOT VERSION-GATED, unlike the decline note this replaced. The gate was right
                // for a NOTE — a note naming Unity's dictionary view on an editor that has no such view is a
                // lie — but it would be wrong here, twice over. Reflection can see a Dictionary<,> field type
                // on every editor version; only the SERIALIZER is version-dependent. So on 6.0 the emitted
                // call finds no property and degrades to the same stock field live degrades to, which is exact
                // parity; and an editor exported on the 6.0 floor and later opened on 6.6 renders the real
                // table instead of being frozen at the version it was generated on. The kit's own use of the
                // 6.6-only ignored-entry query is what carries the gate, and it ships verbatim so the buyer's
                // compiler picks the branch.
                //
                // Twin of the live guard in CreateDictionaryTable: the display type parked on a field whose
                // code type is no longer a dictionary falls through to the PropertyField below, exactly as
                // live falls through to its own Degrade.
                case DisplayType.Dictionary:
                    if (ft != null && ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        return EmitDictionaryTable(w, ctx, node, path, label, fi, ft, native: true);
                    break;

                case DisplayType.InlineEditor:
                    if (ft != null && typeof(UnityEngine.Object).IsAssignableFrom(ft))
                        return EmitInlineEditor(w, ctx, node, path, label, ft);
                    break;

                case DisplayType.AssetPreview:
                    if (ft != null && typeof(UnityEngine.Object).IsAssignableFrom(ft))
                        return EmitAssetPreview(w, ctx, node, path, label, ft);
                    break;

                case DisplayType.Text:
                    if (ft == typeof(string))
                        return node.RichTextPreview
                            ? EmitRichText(w, ctx, node, path, label)
                            : EmitTextArea(w, ctx, node, path, label);
                    // A SasquatchString binds its INNER template, exactly as the live widget does
                    // (BindingFactory.CreateTextField). `path` stays the outer field so [TextArea]/[Multiline]
                    // still resolve from the member the user actually declared.
                    if (ft == typeof(SasquatchString))
                    {
                        // The binding is a PROPERTY PATH STRING, so the generated editor never names the type
                        // and needs no package reference. The field's own declaration does, of course — the
                        // buyer's script cannot compile without Runtime/Text/SasquatchString.cs.
                        ctx.Note($"{path} (SasquatchString) -> bound by property path, so this editor needs no "
                               + "Sasquatch Inspector reference; the FIELD's type still needs the runtime script.");
                        // PathArgWithin, not path + ".template": inside a table cell the member path is a runtime
                        // value, and PathArg would discard the suffix entirely (a cell's path IS the expression).
                        string bindExpr = ctx.PathArgWithin(path, "template");
                        string sv = node.RichTextPreview
                            ? EmitRichText(w, ctx, node, path, label, bindExpr)
                            : EmitTextArea(w, ctx, node, path, label, bindExpr);
                        // The template is only half the struct. Runtime token resolution reads its `source`
                        // reference, which nothing but the inspector ever writes — live bakes it in
                        // BindingFactory.CreateTextField. Skip this and the exported editor edits the text
                        // perfectly and the buyer's GAME then resolves nothing: a silent, runtime-only failure
                        // no compile check or visual diff would catch.
                        //
                        // The MEMBER path travels, not the inner `.template` the box binds — the kit walks down
                        // to `template` and `source` itself.
                        ctx.UseKit(null);   // Core (BindTokenSource) — claimed explicitly, never assumed
                        w.Line($"SasqKit.BindTokenSource({sv}, so, {ctx.PathArg(path)});");
                        return sv;
                    }
                    break;

#if SASQ_AUDIO
                case DisplayType.AudioPreview:
                    // Live falls back to a plain PropertyField for a non-object property too
                    // (BindingFactory.CreateAudioPreview), so a `break` here is parity, not a degradation.
                    if (ft != null && typeof(UnityEngine.Object).IsAssignableFrom(ft))
                        return EmitAudioPreview(w, ctx, node, path, label, ft);
                    break;
#endif

#if SASQ_PARTICLES
                case DisplayType.ParticleSystemPreview:
                    if (ft != null && typeof(UnityEngine.Object).IsAssignableFrom(ft))
                        return EmitParticlePreview(w, ctx, node, path, label, ft);
                    break;
#endif

                case DisplayType.UnityEvent:
                    if (ft != null && typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(ft))
                        return EmitUnityEvent(w, ctx, node, path, label);
                    break;
            }

            // Axis color pips on Default-drawn vector fields (and the Quaternion euler special case).
            if (node.VectorAxisPips && ft != null && IsPipEligible(ft))
            {
                ctx.UseKit("AxisPips");
                if (ft == typeof(Quaternion))
                {
                    ctx.UseKit("Euler");
                    string ev = ctx.NewVar(VarBase(path) + "Euler", "euler");
                    w.Line($"var {ev} = SasqKit.EulerField(so, {ctx.PathArg(path)}, {Lit.Str(label)});");
                    w.Line($"SasqKit.AxisPips({ev});");
                    return ev;
                }
                string pv = EmitPropertyField(w, ctx, node, path);
                w.Line($"SasqKit.AxisPips({pv});");
                return pv;
            }

            // A plain Quaternion on Default renders as Unity's IMGUI euler fallback — soft text a UITK
            // inspector can't align — so emit the crisp euler Vector3Field the live inspector now uses.
            // (Pips + Quaternion already returned from the axis-pips block above.)
            if (ft == typeof(Quaternion)) return EmitEuler(w, ctx, node, path, label);

            // HDR color: PropertyField honors a [ColorUsage(hdr:true)] attribute already, but the Sasquatch HDR
            // mode can be set with no attribute — emit the kit HDR field (badge included) to preserve it.
            if ((ft == typeof(Color) || ft == typeof(Color32)) && node.ColorFieldMode == ColorFieldMode.HDR)
            {
                ctx.UseKit("HdrBadge");
                string hv = ctx.NewVar(VarBase(path) + "Color", "color");
                w.Line($"var {hv} = SasqKit.HdrColor(so, {ctx.PathArg(path)}, {Lit.Str(label)}, showAlpha: {Bool(node.ColorShowAlpha)});");
                return hv;
            }

            // Plain colour with the alpha channel switched off. A stock ColorField already does this, so it is
            // emitted directly rather than through the kit — no section, no weight. Without it the export
            // silently handed the buyer an editable alpha the designer had turned off (live: ColorField.showAlpha
            // in BindingFactory.CreateColorField).
            if ((ft == typeof(Color) || ft == typeof(Color32)) && !node.ColorShowAlpha)
            {
                ctx.UseKit(null);   // Core only (alignment class + label column)
                string cv = ctx.NewVar(VarBase(path) + "Color", "color");
                w.Line($"var {cv} = new ColorField({Lit.Str(label)}) {{ bindingPath = {ctx.PathArg(path)}, showAlpha = false }};");
                w.Line($"{cv}.AddToClassList(\"unity-base-field__aligned\");");
                return cv;
            }

            // Numeric stepper option on a Default field.
            if (node.DisplayType == DisplayType.Default && node.ShowStepper
                && (ft == typeof(int) || ft == typeof(float) || ft == typeof(long) || ft == typeof(double)))
            {
                ctx.UseKit("Stepper");
                string sv = ctx.NewVar(VarBase(path) + "Stepper", "stepper");
                int placement = node.StepperPlacement == StepperPlacement.GroupedLeft ? 1
                    : node.StepperPlacement == StepperPlacement.Split ? 2 : 0;
                // Mirror the live stepper's guard (CreateStepperField: `StepSize <= 0f ? 1.0 : StepSize`).
                // Emitting the raw value shipped dead buttons for a StepSize of 0 — the kit's zero-delta no-op
                // swallows both — and an INVERTED pair for a negative one, where − raised the value.
                double stepSize = node.StepSize <= 0f ? 1.0 : node.StepSize;
                var sArgs = new List<string> { "so", ctx.PathArg(path), $"step: {Lit.D(stepSize)}" };
                if (placement != 0) sArgs.Add($"placement: {placement}");
                if (node.StepperButtonStyle != ButtonStyle.Classic) sArgs.Add($"buttonStyle: {(int)node.StepperButtonStyle}");
                if (node.StepperButtonAccent.isOverridden) sArgs.Add($"accent: {Lit.Col(node.StepperButtonAccent.value)}");
                if (node.StepperPlusColor.isOverridden) sArgs.Add($"plusColor: {Lit.Col(node.StepperPlusColor.value)}");
                if (node.StepperMinusColor.isOverridden) sArgs.Add($"minusColor: {Lit.Col(node.StepperMinusColor.value)}");
                if (node.StepperButtonCornerRadius.isOverridden) sArgs.Add($"buttonRadius: {Lit.F(node.StepperButtonCornerRadius.value)}");
                // The stepper's own default is Small (0), not Medium — hence the != 0 guard, not != 1.
                int stepperSize = StyleEmitter.ButtonKitSize(node.StepperButtonSize);
                if (stepperSize != 0) sArgs.Add($"size: {stepperSize}");
                if (node.StepperButtonHoverColor.isOverridden) sArgs.Add($"hoverColor: {Lit.Col(node.StepperButtonHoverColor.value)}");
                if (node.StepperButtonPressedColor.isOverridden) sArgs.Add($"pressedColor: {Lit.Col(node.StepperButtonPressedColor.value)}");
                if (node.StepperButtonHoverAnimation != ButtonHoverAnimation.Lift) sArgs.Add($"animation: {(int)node.StepperButtonHoverAnimation}");
                // Only a label the node actually changed travels — a stock label is left to PropertyField's own
                // nicifier, which produces the same text. Without this a Label override or Hide Label was
                // silently dropped and the exported stepper reverted to the nicified field name.
                if (label != defaultLabel) sArgs.Add($"label: {Lit.Str(label)}");
                // The bounds a press may not cross. Emitted because the buttons write the property behind the
                // drawer that paints it: without them the exported stepper walked a [Range] field's value past its
                // ceiling exactly as the live one used to, storing data the drawn field clamps out of sight.
                ResolveStepperBounds(node, fi, out float? clampLo, out float? clampHi);
                if (clampLo.HasValue) sArgs.Add($"clampMin: {Lit.F(clampLo.Value)}");
                if (clampHi.HasValue) sArgs.Add($"clampMax: {Lit.F(clampHi.Value)}");
                w.Line($"var {sv} = SasqKit.Stepper({string.Join(", ", sArgs)});");
                return sv;
            }

            // A [SerializeReference] field left on Default still deserves the polymorphic picker.
            // `fi` is NULL for an ELEMENT TEMPLATE (an empty path has no FieldInfo to reflect), and
            // GetCustomAttribute is an extension method, so a null receiver is an ArgumentNullException thrown
            // straight out of GenerateSource rather than a quiet false — i.e. the Export Editor button dies on
            // a layout the designer will happily author (a List<string>/List<int> template left on Default).
            // No FieldInfo means no attribute to find, so falling through to the PropertyField is the answer.
            if (node.DisplayType == DisplayType.Default && ft != null && !ft.IsGenericType
                && fi != null && fi.GetCustomAttribute<SerializeReference>() != null)
                return EmitSRef(w, ctx, node, path, label, ft);

            // NOTHING RENDERS HERE, AND THE REPORT HAS TO SAY SO. Live intercepts a binding whose resolved
            // path has no SerializedProperty and, for a plain unserializable BCL collection, draws the
            // conversion guidance card (LayoutTreeBuilder's two dead branches). The export walk has no such
            // branch: it falls through to a PropertyField whose bindingPath names a property that will never
            // exist, so the exported editor shows an empty gap where live shows a card explaining the fix.
            // A note is the honest minimum -- baking the card itself would need a kit section for a field
            // that, by definition, has no data to edit.
            if (ft != null && SasquatchReflectionUtility.IsPlainUnserializableCollection(ft))
                ctx.Note($"{path} (plain {SasquatchReflectionUtility.GetFriendlyTypeName(ft)}) -> Unity cannot "
                       + "serialize this type, so this field draws NOTHING in the exported editor. The live "
                       + "inspector shows a conversion card with the one-line fix.");

            string note = DegradeNote(node, ft);
            if (note != null)
                // The note names Force Takeover, not "the bridge": Force Takeover is what actually restores the
                // live widget, and the v1 reflection bridge is gone (ExportMarker.Version is 2), so a buyer sent
                // looking for the bridge would find nothing.
                ctx.Note($"{path} ({note}) -> default field; the live widget needs the Sasquatch Inspector package (Force Takeover draws it over this file).");

            return EmitPropertyField(w, ctx, node, path);
        }

        // ---- PropertyField (the universal, drawer-preserving fallback) --------------------------------------

        private static string EmitPropertyField(CodeWriter w, EmitContext ctx, BindingNode node, string path)
        {
            string v = ctx.NewVar(VarBase(path) + "Field", "field");
            w.Line($"var {v} = new PropertyField();");
            w.Line($"{v}.bindingPath = {ctx.PathArg(path)};");
            // Same two rules as Emit's label above: a table cell's header names the value, and a list row's
            // element template sits beside its own [index] — so both are blank-labelled unless the node
            // carries an explicit override.
            bool elementSelfRow = ctx.RowBaseExpr != null && string.IsNullOrEmpty(path);
            if (node.IsLabelHidden || ctx.CellPathExpr != null
                || (elementSelfRow && node.EffectiveLabelOverride == null)) w.Line($"{v}.label = \"\";");
            else if (node.EffectiveLabelOverride != null) w.Line($"{v}.label = {Lit.Str(node.EffectiveLabelOverride)};");
            return v;
        }

        // ---- Kit widgets -----------------------------------------------------------------------------------

        /// <summary>The whole-number half of the Slider/ProgressBar type set. Live decides the same thing from
        /// the SerializedProperty (Integer vs Float), which is exactly what these C# types map to.</summary>
        private static bool IsIntegralSlider(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(uint) || t == typeof(ulong)
            || t == typeof(short) || t == typeof(ushort) || t == typeof(byte) || t == typeof(sbyte);

        /// <summary>Types whose value a 32-bit companion field would truncate, so the slider needs an exact
        /// LongField/DoubleField beside it instead of its own inline input.
        /// <para>
        /// Bakes live's runtime answer: FitsInt32 returns false for UInt32/Int64/UInt64 and IsDoubleProperty
        /// is true for Double — and both read the property's numericType, which is fixed by the C# field type,
        /// so the answer is fully determined at export time. Note <c>uint</c> counts as wide: its upper half
        /// does not fit an int32.
        /// </para></summary>
        private static bool NeedsExactValueField(Type t) =>
            t == typeof(long) || t == typeof(ulong) || t == typeof(uint) || t == typeof(double);

        /// <summary>
        /// The ProgressBar folds its affix into ONE interpolated read-out STRING, so an icon, a pill or per-side
        /// text styling genuinely has nowhere to live there — the buyer gets told rather than silently losing it.
        /// </summary>
        /// <remarks>
        /// Slider and Min-Max used to be on this list too, and should never have been. Their read-outs build a
        /// real affix ELEMENT — the live <c>FieldAffixes.Build</c> — so they carry everything the shared
        /// decorator does; it was only the EXPORT that dropped it, because the kit's read-out mount built a
        /// plain dim Label. That is fixed at the source (<c>SasqKit.AffixReadoutMount</c> now goes through
        /// <c>AffixBuild</c>), so the note would now be telling the buyer about a loss that no longer happens.
        /// </remarks>
        private static void NoteRichAffixDegradation(EmitContext ctx, BindingNode node)
        {
            if (HasRichAffix(node.PrefixAffix) || HasRichAffix(node.SuffixAffix))
                ctx.Note("Prefix/suffix icons, pills and text styling are not carried on the Progress Bar's "
                       + "read-out — it is a single interpolated string, so only the text travels.");
        }

        // "Rich" = anything the ProgressBar's folded read-out string cannot reproduce.
        private static bool HasRichAffix(FieldAffix a) =>
            !a.IsEmpty &&
            (!string.IsNullOrEmpty(a.iconId) || a.pillColor.isOverridden
             || a.style.color.isOverridden || a.style.fontSize.isOverridden || a.style.fontStyle.isOverridden);

        /// <summary>
        /// Emits one read-out affix's STYLE as named kit arguments (the text and mount are emitted by the
        /// caller, which knows whether to read <c>UnitSuffix</c> or <c>PrefixAffix.text</c>).
        /// </summary>
        /// <remarks>
        /// Slider and Min-Max read-outs build a real affix element, so everything the shared decorator carries
        /// has somewhere to live on them: icon, colour, size, font style, pill and gap. Without these the
        /// exported widget rendered bare text where the live one rendered a pilled, tinted, icon-bearing unit
        /// — and an ICON-ONLY affix vanished from the export entirely, because the caller's text guard was the
        /// only thing that emitted anything at all.
        /// <para>Same argument names and same order as the <c>SasqKit.Affixes</c> block in
        /// <c>LayoutSourceEmitter.EmitAffixes</c>, and the icon is baked WHITE and tinted at runtime by the
        /// same one-bake-two-skins trick — keep the two in lockstep.</para>
        /// </remarks>
        private static void EmitReadoutAffixStyle(EmitContext ctx, List<string> args, FieldAffix a, string p)
        {
            if (a.IsEmpty) return;
            if (!string.IsNullOrEmpty(a.iconId))
            {
                var tex = SasquatchIcons.RenderToTexture(a.iconId, 32, Color.white);
                if (tex != null) args.Add($"{p}Icon: \"{Convert.ToBase64String(tex.EncodeToPNG())}\"");
            }
            if (a.style.color.isOverridden) args.Add($"{p}Color: {Lit.Col(a.style.color.value)}");
            if (a.style.fontSize.isOverridden && a.style.fontSize.value > 0f)
                args.Add($"{p}Size: {Lit.F(a.style.fontSize.value)}");
            if (a.style.fontStyle.isOverridden) args.Add($"{p}Font: {(int)a.style.fontStyle.value}");
            if (a.pillColor.isOverridden) args.Add($"{p}Pill: {Lit.Col(a.pillColor.value)}");
            if (a.gap.isOverridden) args.Add($"{p}Gap: {Lit.F(a.gap.value)}");
            // A custom font asset cannot travel into a package-free file — note it and drop it.
            if (a.style.font != null) ctx.Note("A prefix/suffix custom font is not carried into the export.");
        }

        private static string EmitSlider(CodeWriter w, EmitContext ctx, BindingNode node, string path, FieldInfo fi, string label, bool isInt)
        {
            ctx.UseKit("Slider");
            // Per-type fallback, mirroring BindingFactory.Slider: ints ride 0..100, floats 0..1.
            ResolveLimits(node, fi, 0f, isInt ? 100f : 1f, out float low, out float high);
            var s = node.DisplayStyleData;
            var args = new List<string>
            {
                "so", ctx.PathArg(path), Lit.Str(label), Lit.F(low), Lit.F(high), Bool(isInt)
            };
            if (s.sliderSize != SliderSize.Normal) args.Add($"size: {(int)s.sliderSize}");
            int shape = s.sliderKnobShape == SliderKnobShape.Square ? 1
                : s.sliderKnobShape == SliderKnobShape.Capsule ? 2
                : s.sliderKnobShape == SliderKnobShape.Rectangle ? 3
                : s.sliderKnobShape == SliderKnobShape.Grip ? 4
                : 0;
            if (shape != 0) args.Add($"knobShape: {shape}");
            // Square/Rectangle knobs take the roundness VERBATIM (live: Mathf.Max(0f, roundness)), so a
            // deliberate 0 — sharp corners — has to travel. Guarding on "> 0f" swallowed it and the kit
            // fell back to its 2px default, quietly rounding off the one shape the user picked for edges.
            // Every other shape derives its own radius, so there the value is genuinely optional.
            if (shape == 1 || shape == 3) args.Add($"knobRoundness: {Lit.F(Mathf.Max(0f, s.sliderKnobRoundness))}");
            else if (s.sliderKnobRoundness > 0f) args.Add($"knobRoundness: {Lit.F(s.sliderKnobRoundness)}");
            if (s.sliderBarColor.a > 0f) args.Add($"barColor: {Lit.Col(s.sliderBarColor)}");
            if (s.sliderKnobColor.a > 0f) args.Add($"knobColor: {Lit.Col(s.sliderKnobColor)}");
            if (s.sliderBarHoverColor.a > 0f) args.Add($"barHover: {Lit.Col(s.sliderBarHoverColor)}");
            if (s.sliderKnobHoverColor.a > 0f) args.Add($"knobHover: {Lit.Col(s.sliderKnobHoverColor)}");
            if (s.sliderHideField) args.Add("hideField: true");
            else
            {
                // Field placement + unit suffix only matter while a value field shows at all.
                if (s.sliderFieldPlacement != SliderFieldPlacement.Right)
                    args.Add($"fieldPosition: {(int)s.sliderFieldPlacement}");
                bool vertical = s.sliderFieldPlacement == SliderFieldPlacement.Above
                    || s.sliderFieldPlacement == SliderFieldPlacement.Below;
                if (vertical && s.sliderFieldAlignment != SliderFieldAlignment.Center)
                    args.Add($"fieldAlign: {(int)s.sliderFieldAlignment}");
                if (!string.IsNullOrEmpty(node.PrefixAffix.text)) args.Add($"prefix: {Lit.Str(node.PrefixAffix.text)}");
                if (!string.IsNullOrEmpty(node.UnitSuffix)) args.Add($"unit: {Lit.Str(node.UnitSuffix)}");
                // The PLACEMENT travels too. Without these the exported read-out always rendered outside, so
                // "Inside field" worked live and silently did nothing in an exported editor.
                if (node.PrefixAffix.mount != AffixMount.Auto) args.Add($"prefixMount: {(int)node.PrefixAffix.mount}");
                if (node.SuffixAffix.mount != AffixMount.Auto) args.Add($"unitMount: {(int)node.SuffixAffix.mount}");
                // …and so does the STYLE. A slider read-out builds a real affix element, so the icon, pill,
                // colour, size and gap all render there live — the export used to drop every one of them.
                EmitReadoutAffixStyle(ctx, args, node.PrefixAffix, "prefix");
                EmitReadoutAffixStyle(ctx, args, node.SuffixAffix, "unit");
            }
            if (s.cornerRadius.isOverridden)
                args.Add($"barRadius: {Lit.F(Mathf.Max(0f, s.cornerRadius.value))}");
            // A slider the layout never styled stays 100% stock — no knob/ring surgery on defaults.
            bool styledLook = s.sliderSize != SliderSize.Normal || shape != 0
                || s.sliderBarColor.a > 0f || s.sliderKnobColor.a > 0f;
            if (styledLook) args.Add("styled: true");
            // A 64-bit or unsigned-32 integer, or a double, would be truncated by the slider's own 32-bit
            // inline input — live gives those an exact LongField/DoubleField companion instead. The slider
            // control itself still binds normally; only its read-out needs the wider type.
            // ResolveFieldType, so an element-template slider on a List<long>/List<double> gets the wide
            // read-out too — it has no FieldInfo of its own to ask.
            if (NeedsExactValueField(ctx.ResolveFieldType(path))) args.Add("wideValue: true");
            string v = ctx.NewVar(VarBase(path) + "Slider", "slider");
            w.Line($"var {v} = SasqKit.Slider({string.Join(", ", args)});");
            return v;
        }

        private static string EmitMinMax(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label)
        {
            ctx.UseKit("MinMax");
            var s = node.DisplayStyleData;
            var args = new List<string>
            {
                "so", ctx.PathArg(path), Lit.Str(label), Lit.F(node.MinMaxLowLimit), Lit.F(node.MinMaxHighLimit)
            };
            if (s.minMaxHideValues) args.Add("showLabels: false");
            if (s.minMaxReadOnlyValues) args.Add("editableLabels: false");
            if (!string.IsNullOrEmpty(s.minMaxLabelFormat)) args.Add($"format: {Lit.Str(s.minMaxLabelFormat.Trim('"'))}");
            if (s.accent.isOverridden) args.Add($"accent: {Lit.Col(s.accent.value)}");
            // Only while the read-outs render: with showLabels off there is no number to qualify, and the
            // live widget skips the affixes entirely (the old code emitted `unit:` unconditionally).
            if (!s.minMaxHideValues)
            {
                if (!string.IsNullOrEmpty(node.PrefixAffix.text)) args.Add($"prefix: {Lit.Str(node.PrefixAffix.text)}");
                if (!string.IsNullOrEmpty(node.UnitSuffix)) args.Add($"unit: {Lit.Str(node.UnitSuffix)}");
                // The PLACEMENT travels too. Without these the exported read-out always rendered outside, so
                // "Inside field" worked live and silently did nothing in an exported editor.
                if (node.PrefixAffix.mount != AffixMount.Auto) args.Add($"prefixMount: {(int)node.PrefixAffix.mount}");
                if (node.SuffixAffix.mount != AffixMount.Auto) args.Add($"unitMount: {(int)node.SuffixAffix.mount}");
                // …and so does the STYLE, on BOTH read-outs. Same reason as the slider: these build real affix
                // elements, so everything the shared decorator carries renders here live too.
                EmitReadoutAffixStyle(ctx, args, node.PrefixAffix, "prefix");
                EmitReadoutAffixStyle(ctx, args, node.SuffixAffix, "unit");
            }
            if (s.sliderSize != SliderSize.Normal) args.Add($"size: {(int)s.sliderSize}");
            // Capsule reads ridiculous as a range handle — the live widget demotes it to Default; match.
            int shape = s.sliderKnobShape == SliderKnobShape.Square ? 1
                : s.sliderKnobShape == SliderKnobShape.Rectangle ? 3
                : s.sliderKnobShape == SliderKnobShape.Grip ? 4
                : 0;
            if (shape != 0) args.Add($"knobShape: {shape}");
            // Square/Rectangle knobs take the roundness VERBATIM (live: Mathf.Max(0f, roundness)), so a
            // deliberate 0 — sharp corners — has to travel. Guarding on "> 0f" swallowed it and the kit
            // fell back to its 2px default, quietly rounding off the one shape the user picked for edges.
            // Every other shape derives its own radius, so there the value is genuinely optional.
            if (shape == 1 || shape == 3) args.Add($"knobRoundness: {Lit.F(Mathf.Max(0f, s.sliderKnobRoundness))}");
            else if (s.sliderKnobRoundness > 0f) args.Add($"knobRoundness: {Lit.F(s.sliderKnobRoundness)}");
            if (s.sliderKnobColor.a > 0f) args.Add($"knobColor: {Lit.Col(s.sliderKnobColor)}");
            if (s.sliderBarColor.a > 0f) args.Add($"barColor: {Lit.Col(s.sliderBarColor)}");
            if (s.sliderCenterBarColor.a > 0f) args.Add($"centerColor: {Lit.Col(s.sliderCenterBarColor)}");
            if (s.cornerRadius.isOverridden)
                args.Add($"barRadius: {Lit.F(Mathf.Max(0f, s.cornerRadius.value))}");
            if (s.sliderBarHoverColor.a > 0f) args.Add($"barHover: {Lit.Col(s.sliderBarHoverColor)}");
            if (s.sliderCenterBarHoverColor.a > 0f) args.Add($"centerHover: {Lit.Col(s.sliderCenterBarHoverColor)}");
            if (s.sliderKnobHoverColor.a > 0f) args.Add($"knobHover: {Lit.Col(s.sliderKnobHoverColor)}");
            // The min/max read-outs live OUTSIDE any field input box, so they carry their own text style
            // (the live MinMaxLabelTextStyle) instead of riding the generic StyleField path.
            var ts = node.MinMaxLabelTextStyle;
            if (ts.color.isOverridden) args.Add($"valueColor: {Lit.Col(ts.color.value)}");
            if (TextStyleApplier.IsUsableFontSize(ts.fontSize)) args.Add($"valueSize: {Lit.F(ts.fontSize.value)}");
            if (ts.fontStyle.isOverridden) args.Add($"valueFontStyle: {(int)ts.fontStyle.value}");
            if (ts.align.isOverridden) args.Add($"valueAlign: {(int)ts.align.value}");
            // Above/Below stack the read-outs on their own line beside a full-width track. The live widget
            // branches on "vertical" alone, so Left collapses to the inline form exactly as it does there —
            // only a genuinely vertical placement needs to travel.
            if (s.sliderFieldPlacement == SliderFieldPlacement.Above
                || s.sliderFieldPlacement == SliderFieldPlacement.Below)
                args.Add($"fieldPosition: {(int)s.sliderFieldPlacement}");
            string v = ctx.NewVar(VarBase(path) + "Range", "range");
            w.Line($"var {v} = SasqKit.MinMax({string.Join(", ", args)});");
            return v;
        }

        private static string EmitProgress(CodeWriter w, EmitContext ctx, BindingNode node, string path, FieldInfo fi, string label)
        {
            ctx.UseKit("Progress");
            // 0..100 for BOTH int and float, mirroring BindingFactory.ProgressBar — a progress bar reads as a
            // percentage, so a float `health = 50f` must show "50 / 100", not "50 / 1" pinned full.
            ResolveLimits(node, fi, 0f, 100f, out float low, out float high);
            if (high <= low) high = low + 1f;   // same degenerate-range guard the live bar applies
            var s = node.DisplayStyleData;
            var args = new List<string>
            {
                "so", ctx.PathArg(path), Lit.Str(label), Lit.F(low), Lit.F(high)
            };
            if (!node.ProgressBarInteractive) args.Add("interactive: false");
            if (node.ProgressBarTextMode != ProgressBarTextMode.ValueAndMax) args.Add($"textMode: {(int)node.ProgressBarTextMode}");
            if (node.ProgressBarColor.a > 0f) args.Add($"fill: {Lit.Col(node.ProgressBarColor)}");
            else if (s.accent.isOverridden) args.Add($"fill: {Lit.Col(s.accent.value)}");
            if (node.ProgressBarBgColor.a > 0f) args.Add($"trackBg: {Lit.Col(node.ProgressBarBgColor)}");
            if (node.ProgressBarThickness > 0f) args.Add($"thickness: {Lit.F(node.ProgressBarThickness)}");
            if (s.cornerRadius.isOverridden) args.Add($"radius: {Lit.F(s.cornerRadius.value)}");
            if (!string.IsNullOrEmpty(node.PrefixAffix.text)) args.Add($"prefix: {Lit.Str(node.PrefixAffix.text)}");
            if (!string.IsNullOrEmpty(node.UnitSuffix)) args.Add($"unit: {Lit.Str(node.UnitSuffix)}");
            // NO mount argument here, and that is deliberate twice over. SasqKit.Progress has no such
            // parameter — a bar with a non-Auto mount emitted `prefixMount: 1` and the buyer's project failed
            // to compile with CS1739, which nothing here catches because no corpus case sets one. And it would
            // be meaningless anyway: this read-out is ONE interpolated string with no box to sit inside, so
            // there is no inside for a mount to select. Affix TEXT is all that can travel.
            NoteRichAffixDegradation(ctx, node);
            if (node.ProgressBarValueFill && node.ProgressBarFillGradient != null)
            {
                // Bake the value-fill gradient as evenly spaced samples the kit lerps between.
                var samples = new List<string>();
                for (int i = 0; i < 16; i++)
                    samples.Add(Lit.Col(node.ProgressBarFillGradient.Evaluate(i / 15f)));
                args.Add("valueFill: new[] { " + string.Join(", ", samples) + " }");
            }
            if (node.ProgressBarTextBackdrop) args.Add("textBackdrop: true");
            string v = ctx.NewVar(VarBase(path) + "Progress", "progress");
            w.Line($"var {v} = SasqKit.Progress({string.Join(", ", args)});");
            return v;
        }

        /// <summary>
        /// True for a serialized struct the exported Spring widget can actually drive: a non-primitive value
        /// type carrying <c>float Duration</c> and <c>float Bounce</c>. The compile-time half of the guard —
        /// the kit re-checks the same thing at draw time off the SerializedProperty, because the file can land
        /// in a project where the SpringConfig type is absent entirely.
        /// </summary>
        private static bool IsSpringShaped(Type ft)
        {
            if (ft == null || !ft.IsValueType || ft.IsPrimitive || ft.IsEnum) return false;
            var d = ft.GetField("Duration", BindingFlags.Public | BindingFlags.Instance);
            var b = ft.GetField("Bounce", BindingFlags.Public | BindingFlags.Instance);
            return d != null && d.FieldType == typeof(float)
                && b != null && b.FieldType == typeof(float);
        }

        /// <summary>
        /// The export-time twin of <c>BindingFactory.IsSpringConfigProperty</c> — the test that makes live
        /// auto-route the widget regardless of DisplayType. Matched by NAME plus shape, exactly as live does
        /// (it reads <c>prop.type == "SpringConfig"</c>), and deliberately NOT <c>typeof(SpringConfig)</c>:
        /// <c>Editor/Export/</c> stays a leaf, and a hard type reference here would be the first line in it
        /// that a package-free reader of this emitter could not follow.
        /// </summary>
        private static bool IsSpringConfigField(Type ft)
            => ft != null && ft.Name == "SpringConfig" && IsSpringShaped(ft);

        /// <summary>
        /// The Spring config editor: preset dropdown, Duration/Bounce sliders, the live response graph, the
        /// drag-and-release tester and the Advanced group. Carries no node styling — live's widget reads
        /// nothing off the BindingNode but its label, so neither does this.
        /// </summary>
        private static string EmitSpringWell(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label)
        {
            ctx.UseKit("SpringWell");
            var args = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };
            // Live keys the Advanced fold on the PROPERTY PATH (BindingFactory's FoldoutStateStore.Key call
            // passes prop.propertyPath + "SpringAdvanced"), not on structure — mirror that, so moving the
            // field in the layout does not forget how the buyer left the group.
            string advancedStateKey = ctx.FoldoutKeyArg(path, "SpringAdvanced");
            if (advancedStateKey != null) args.Add($"stateKey: {advancedStateKey}");
            string v = ctx.NewVar(VarBase(path) + "Spring", "spring");
            w.Line($"var {v} = SasqKit.SpringWell({string.Join(", ", args)});");
            return v;
        }

        /// <summary>
        /// The Key / Value dictionary table, for BOTH shapes: this package's own <c>SerializedDictionary</c>
        /// (<paramref name="native"/> false) and Unity 6.6's <c>Dictionary&lt;K,V&gt;</c> (true). One kit entry
        /// point renders both, exactly as one live renderer does, so the two cannot drift apart.
        /// </summary>
        private static string EmitDictionaryTable(CodeWriter w, EmitContext ctx, BindingNode node, string path,
                                                  string label, FieldInfo fi, Type ft, bool native)
        {
            ctx.UseKit("Dictionary");

            // The RUNTIME-SCRIPT note, and it belongs here rather than beside the degrade notes at the bottom
            // of Emit: this method returns early, so a note left down there stopped firing the moment the
            // dictionary auto-route landed. It is NOT a fidelity note — nothing degrades — it is the same
            // thing the SasquatchString note says. The emitted editor binds by property path and names no
            // package type, so it needs no assembly reference; the FIELD's own declaration still does, so a
            // teammate handed this .cs without the package cannot compile their script.
            if (!native)
                ctx.Note($"{path} (SerializedDictionary) -> bound by property path, so this editor needs no "
                       + "Sasquatch Inspector reference; the FIELD's type still needs the runtime script.");

            var args = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };
            if (native) args.Add("native: true");

            // Unity's own [DictionaryDisplay] on the field, and the assembly-scoped registration behind it.
            // Read at CODEGEN time and baked, because the generated file may not name a 6.6-only attribute
            // type at all. Only a deviation from the kit's defaults travels — see the omitted-defaults rule.
            ResolveDictionaryDisplay(fi, ft, out string keyLabel, out string valueLabel, out float keyFraction);
            if (keyLabel != null) args.Add($"keyLabel: {Lit.Str(keyLabel)}");
            if (valueLabel != null) args.Add($"valueLabel: {Lit.Str(valueLabel)}");
            if (keyFraction > 0f) args.Add($"keyColumnFraction: {Lit.F(keyFraction)}");

            // Style and Row Frame. Table / None are BOTH the node default and the kit parameter default, so an
            // unstyled dictionary keeps the shortest possible emitted call.
            if (node.DictionaryStyle != DictionaryTableStyle.Table)
                args.Add($"style: {(int)node.DictionaryStyle}");
            if (node.DictionaryRowSeparator != CollectionRowSeparator.None)
                args.Add($"rowSeparator: {(int)node.DictionaryRowSeparator}");
            if (node.DictionarySeparatorColor.isOverridden)
                args.Add($"separatorColor: {Lit.Col(node.DictionarySeparatorColor.value)}");

            // The expand fold. Live keys it on the target type plus the property path; the exported editor has
            // its own preference namespace, so this is the kit's ordinary foldout key rather than that string.
            string stateKey = ctx.FoldoutKeyArg(path, native ? "NativeDictionary" : "Dictionary");
            if (stateKey != null) args.Add($"stateKey: {stateKey}");

            // AUTHORED CELL TEMPLATES FIRST, the auto-route second. The node's Key / Value templates are what
            // the designer offers and what the live table reads, so they have to win here or a styled value
            // column renders as a Color Palette live and a bare colour field in a generated editor. The
            // SpringConfig auto-route below is the fallback for an UNTEMPLATED dictionary, and it stays
            // because live keeps it too: BindingFactory routes a SpringConfig ahead of the Display Type
            // whether or not a template exists (see springconfig-auto-routes-past-displaytype).
            string keyFactory = DictionaryCellFactory(w, ctx, node.DictionaryKeyTemplate, ft, 0, "__dictKey", "keyFactory");
            if (keyFactory != null) args.Add(keyFactory);

            string valueFactory = DictionaryCellFactory(w, ctx, node.DictionaryValueTemplate, ft, 1, "__dictValue", "valueFactory")
                                  ?? DictionaryValueFactory(ctx, ft);
            if (valueFactory != null) args.Add(valueFactory);

            string v = ctx.NewVar(VarBase(path) + "Dict", "dict");
            w.Line($"var {v} = SasqKit.DictionaryTable({string.Join(", ", args)});");
            return v;
        }

        /// <summary>
        /// Unity's <c>[DictionaryDisplay]</c> settings for the bound field, or nulls / 0 when there is nothing
        /// to carry. The FIELD attribute wins over the assembly-scoped <c>[DictionaryDisplayForType]</c>, which
        /// is the order Unity's own view resolves them in. Everything here is 6.6-only API, hence the gate —
        /// and the whole method is a no-op on older editors, where no dictionary field serializes anyway.
        /// </summary>
        private static void ResolveDictionaryDisplay(FieldInfo fi, Type ft,
                                                     out string keyLabel, out string valueLabel, out float keyFraction)
        {
            keyLabel = null;
            valueLabel = null;
            keyFraction = 0f;
#if UNITY_6000_6_OR_NEWER
            if (fi == null) return;
            var attr = Attribute.GetCustomAttribute(fi, typeof(UnityEngine.DictionaryDisplayAttribute))
                       as UnityEngine.DictionaryDisplayAttribute
                       ?? FindAssemblyDictionaryDisplay(ft);
            if (attr == null) return;
            if (!string.IsNullOrEmpty(attr.keyLabel)) keyLabel = attr.keyLabel;
            if (!string.IsNullOrEmpty(attr.valueLabel)) valueLabel = attr.valueLabel;
            // UNITY'S OWN RANGE, 0.01–0.99 — the same clamp the live reader applies, so the two views of one
            // field cannot disagree about width for any legal value.
            if (attr.keyColumnFraction > 0f)
                keyFraction = Mathf.Clamp(attr.keyColumnFraction, 0.01f, 0.99f);
#endif
        }

#if UNITY_6000_6_OR_NEWER
        /// <summary>The assembly-scoped registration for <paramref name="dictionaryType"/>, or null. Keyed on
        /// the EXACT closed dictionary type, which is what <c>targetType</c> means. Built once per domain
        /// reload: an assembly-level attribute cannot appear without a recompile, and a reload clears the
        /// static, so there is nothing to invalidate.</summary>
        private static Dictionary<Type, UnityEngine.DictionaryDisplayForTypeAttribute> s_assemblyDictionaryDisplay;

        private static UnityEngine.DictionaryDisplayForTypeAttribute FindAssemblyDictionaryDisplay(Type dictionaryType)
        {
            if (dictionaryType == null) return null;
            if (s_assemblyDictionaryDisplay == null)
            {
                s_assemblyDictionaryDisplay = new Dictionary<Type, UnityEngine.DictionaryDisplayForTypeAttribute>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    object[] found;
                    // A dynamic or unloadable assembly throws rather than answering; "no registrations" is the
                    // right reading for one that cannot be read at all.
                    try { found = asm.GetCustomAttributes(typeof(UnityEngine.DictionaryDisplayForTypeAttribute), false); }
                    catch { continue; }
                    foreach (var o in found)
                    {
                        var a = o as UnityEngine.DictionaryDisplayForTypeAttribute;
                        if (a == null || a.targetType == null) continue;
                        // FIRST wins, matching the live reader and Unity's own registry, so the exported editor
                        // and the live one cannot quietly pick different registrations.
                        if (!s_assemblyDictionaryDisplay.ContainsKey(a.targetType))
                            s_assemblyDictionaryDisplay[a.targetType] = a;
                    }
                }
            }
            return s_assemblyDictionaryDisplay.TryGetValue(dictionaryType, out var hit) ? hit : null;
        }
#endif

        /// <summary>
        /// The <c>valueFactory:</c> argument when a dictionary's VALUE type has a Sasquatch editor of its own,
        /// or null when a plain bound field is already what live draws.
        /// <para>
        /// Live builds every dictionary value through the same field factory an ordinary binding uses, so a
        /// value type that auto-routes past the display type gets its widget inside the cell. Today that is
        /// SpringConfig, which has no useful Unity default. Emitted as a delegate rather than wired inside the
        /// Dictionary kit section for the same reason the collection's element factory is: it keeps that
        /// section free of any compile-time reference to SpringWell, so an ordinary dictionary export does not
        /// drag the spring solver and its graph along.
        /// </para>
        /// </summary>
        /// <summary>
        /// The <c>keyFactory:</c> / <c>valueFactory:</c> lambda for one authored dictionary CELL TEMPLATE, or
        /// null when there is no template (or nothing emitted for it), in which case the kit draws the stock
        /// cell — exactly what live draws with no template.
        /// <para>
        /// The lambda is the dictionary's answer to a list row's <c>elementFactory:</c>, and it is built the
        /// same way: point <see cref="EmitContext.RowBaseExpr"/> at the lambda's own parameter so every path
        /// inside resolves at RUNTIME, point <see cref="EmitContext.ResolveRoot"/> at the half's C# type so
        /// widget selection is driven by the real type, and give the scope its own
        /// <see cref="EmitContext.NodePath"/> so a foldout inside a cell does not share fold state with the
        /// dictionary above it.
        /// </para>
        /// <para>
        /// <c>CellPathExpr</c> is CLEARED for the body, not merely left alone. A dictionary can be a table
        /// COLUMN, and inside a cell factory that expression is in force — <c>PathArg</c> returns it ahead of
        /// <c>RowBaseExpr</c>, so every widget in the dictionary's own cells would have bound to the table
        /// cell's path instead of the row's. Every one of the four is restored in the <c>finally</c>, for the
        /// reason those fields' own docs give: emitting a cell re-enters the ordinary field path.
        /// </para>
        /// </summary>
        private static string DictionaryCellFactory(CodeWriter w, EmitContext ctx, BindingNode template,
                                                    Type ft, int half, string param, string argName)
        {
            if (template == null) return null;
            Type halfType = BindingFactory.DictionaryHalfType(ft, half);
            if (halfType == null) return null;

            var body = new CodeWriter();
            string prevRow = ctx.RowBaseExpr;
            string prevCell = ctx.CellPathExpr;
            Type prevRoot = ctx.ResolveRoot;
            string prevNodePath = ctx.NodePath;
            bool prevDictCell = ctx.DictionaryCell;
            string expr;
            try
            {
                ctx.RowBaseExpr = param + ".propertyPath";
                ctx.CellPathExpr = null;
                ctx.ResolveRoot = halfType;
                ctx.NodePath = prevNodePath + (half == 0 ? "/key" : "/value");
                ctx.DictionaryCell = true;
                expr = Emit(body, ctx, template, "", "");
            }
            finally
            {
                ctx.RowBaseExpr = prevRow;
                ctx.CellPathExpr = prevCell;
                ctx.ResolveRoot = prevRoot;
                ctx.NodePath = prevNodePath;
                ctx.DictionaryCell = prevDictCell;
            }

            if (expr == null) return null;

            string fv = ctx.NewVar(VarBase(argName) + "Cell", "cell");
            w.Line($"System.Func<SerializedProperty, VisualElement> {fv} = {param} =>");
            w.OpenBrace();
            w.AppendBlock(body.ToString());
            w.Line($"return {expr};");
            w.CloseBrace(";");
            return $"{argName}: {fv}";
        }

        private static string DictionaryValueFactory(EmitContext ctx, Type ft)
        {
            Type valueType = DictionaryValueType(ft);
            if (!IsSpringConfigField(valueType)) return null;

            ctx.UseKit("SpringWell");
            // A prefixed parameter name, never a short one: this lambda is dropped into a method alongside
            // other emitted locals, and C# rejects a lambda parameter that shadows a name already in scope.
            // Distinct from the collection's own row-lambda names so a dictionary inside a list row is safe.
            // The label is blank because the column header already names the value.
            return "valueFactory: __dictValue => SasqKit.SpringWell(so, __dictValue.propertyPath, \"\")";
        }

        /// <summary>The VALUE type of either dictionary shape, or null when <paramref name="ft"/> is neither.
        /// The package's own dictionary is a generic CLASS, so its arguments are read off the type itself; a
        /// native one is the closed <c>Dictionary&lt;K,V&gt;</c>.</summary>
        private static Type DictionaryValueType(Type ft) => BindingFactory.DictionaryHalfType(ft, 1);

        private static string EmitRating(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label)
        {
            ctx.UseKit("Rating");
            var args = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };
            if (node.RatingCount != 5) args.Add($"count: {node.RatingCount}");
            if (node.RatingStyle != RatingIconStyle.Stars) args.Add($"style: {(int)node.RatingStyle}");
            if (Math.Abs(node.RatingPipSize - 15f) > 0.01f) args.Add($"pipSize: {Lit.F(node.RatingPipSize)}");
            if (node.RatingSelectedColor.isOverridden) args.Add($"selected: {Lit.Col(node.RatingSelectedColor.value)}");
            else if (node.DisplayStyleData.accent.isOverridden) args.Add($"selected: {Lit.Col(node.DisplayStyleData.accent.value)}");
            if (node.RatingUnselectedColor.isOverridden) args.Add($"unselected: {Lit.Col(node.RatingUnselectedColor.value)}");
            if (node.RatingHoverColor.isOverridden) args.Add($"hover: {Lit.Col(node.RatingHoverColor.value)}");
            if (node.RatingAnimate) args.Add("animate: true");
            string v = ctx.NewVar(VarBase(path) + "Rating", "rating");
            w.Line($"var {v} = SasqKit.Rating({string.Join(", ", args)});");
            return v;
        }

        private static string EmitToggle(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label)
        {
            ctx.UseKit("Toggle");
            var s = node.DisplayStyleData;
            int style = s.toggleStyle == ToggleStyle.ModernCheckbox ? 2
                : s.toggleStyle == ToggleStyle.Square ? 1
                : s.toggleStyle == ToggleStyle.Slim ? 3
                : s.toggleStyle == ToggleStyle.Checkbox ? 4 : 0;
            var args = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };
            if (style != 0) args.Add($"style: {style}");
            if (s.toggleFillColor.a > 0f) args.Add($"fillOn: {Lit.Col(s.toggleFillColor)}");
            else if (s.accent.isOverridden) args.Add($"fillOn: {Lit.Col(s.accent.value)}");
            if (s.toggleFillColorOff.a > 0f) args.Add($"fillOff: {Lit.Col(s.toggleFillColorOff)}");
            if (s.toggleOutlineColor.a > 0f) args.Add($"outline: {Lit.Col(s.toggleOutlineColor)}");
            if (s.cornerRadius.isOverridden) args.Add($"cornerRadius: {Lit.F(s.cornerRadius.value)}");
            if (s.toggleKnobColor.a > 0f) args.Add($"knobColor: {Lit.Col(s.toggleKnobColor)}");
            if (s.toggleSize != SliderSize.Normal) args.Add($"size: {(int)s.toggleSize}");
            if (s.toggleShowOnOff)
            {
                args.Add("showOnOff: true");
                var ots = node.ToggleOnOffTextStyle;
                if (ots.color.isOverridden) args.Add($"onOffColor: {Lit.Col(ots.color.value)}");
                if (TextStyleApplier.IsUsableFontSize(ots.fontSize)) args.Add($"onOffSize: {Lit.F(ots.fontSize.value)}");
                if (ots.fontStyle.isOverridden) args.Add($"onOffFontStyle: {(int)ots.fontStyle.value}");
                if (ots.align.isOverridden) args.Add($"onOffAlign: {(int)ots.align.value}");
            }
            string v = ctx.NewVar(VarBase(path) + "Toggle", "toggle");
            w.Line($"var {v} = SasqKit.ToggleSwitch({string.Join(", ", args)});");
            return v;
        }

        private static string EmitTagChips(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label)
        {
            ctx.UseKit("TagChips");
            string v = ctx.NewVar(VarBase(path) + "Chips", "chips");
            w.Line($"var {v} = SasqKit.TagChips(so, {ctx.PathArg(path)}, {Lit.Str(label)});");
            return v;
        }

        private static string EmitEnumBadges(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label, Type enumType)
        {
            ctx.UseKit("EnumBadges");
            bool isFlags = enumType.GetCustomAttribute<FlagsAttribute>() != null;
            var names = new List<string>();
            var values = new List<long>();
            var obsolete = new List<long>();
            foreach (var mf in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                long value = EnumRawToLong(mf.GetRawConstantValue());
                // Obsolete members are baked in and FLAGGED rather than dropped: the live grid keeps one visible
                // while it is the current value so the selection can still be seen and cleared, and only the
                // running editor knows what the current value is. Dropping them here made that field look unset
                // and impossible to change.
                if (mf.GetCustomAttribute<ObsoleteAttribute>() != null) obsolete.Add(value);
                // Bake the RESOLVED label, not the raw member name: the exported editor has no reflection pass
                // of its own, so [InspectorName] has to be applied here or it is lost for good. (The kit takes
                // these verbatim — see SasqKit.EnumBadges — precisely so this stays the only place it happens.)
                names.Add(EnumMemberLabel.ResolveFromMember(mf, mf.Name));
                values.Add(value);
            }
            string namesLit = "new[] { " + string.Join(", ", names.ConvertAll(Lit.Str)) + " }";
            string valuesLit = "new long[] { " + string.Join(", ", values.ConvertAll(x => x.ToString())) + " }";
            var args = new List<string> { "so", ctx.PathArg(path), Lit.Str(label), namesLit, valuesLit, Bool(isFlags) };
            if (obsolete.Count > 0)
                args.Add("obsoleteValues: new long[] { " + string.Join(", ", obsolete.ConvertAll(x => x.ToString())) + " }");
            if (node.EnumBadgeIncludeObsolete) args.Add("includeObsolete: true");
            if (!node.EnumBadgeShowAllNone) args.Add("showAllNone: false");
            if (Math.Abs(node.EnumBadgeHeight - 20f) > 0.01f) args.Add($"height: {Lit.F(node.EnumBadgeHeight)}");
            if (node.EnumBadgeColumns > 0) args.Add($"columns: {node.EnumBadgeColumns}");
            // Only meaningful under auto-wrap; the kit enforces the same precedence, but not emitting a value the
            // exported editor would ignore keeps the generated call honest about what it actually does.
            if (node.EnumBadgeWidth > 0f && node.EnumBadgeColumns <= 0) args.Add($"width: {Lit.F(node.EnumBadgeWidth)}");
            if (node.EnumBadgeSpacing > 0f) args.Add($"spacing: {Lit.F(node.EnumBadgeSpacing)}");
            if (node.EnumBadgeTextBackdrop) args.Add("textBackdrop: true");
            if (node.DisplayStyleData.accent.isOverridden) args.Add($"accent: {Lit.Col(node.DisplayStyleData.accent.value)}");

            // Per-value tint + icon overrides. Colors bake trivially; a built-in icon has no pipeline in the target
            // project, so bake it as base64 PNGs (one per editor skin), the same as the script header's glyph.
            if (node.EnumBadgeOverrides != null && node.EnumBadgeOverrides.Length > 0)
            {
                var tintVals = new List<long>();
                var tintCols = new List<string>();
                var iconVals = new List<long>();
                var iconDark = new List<string>();
                var iconLight = new List<string>();
                foreach (var ov in node.EnumBadgeOverrides)
                {
                    if (ov.color.isOverridden) { tintVals.Add(ov.value); tintCols.Add(Lit.Col(ov.color.value)); }
                    if (!string.IsNullOrEmpty(ov.iconId))
                    {
                        var dark = SasquatchIcons.RenderToTexture(ov.iconId, 32, new Color(0.85f, 0.85f, 0.85f, 1f));
                        var light = SasquatchIcons.RenderToTexture(ov.iconId, 32, new Color(0.13f, 0.13f, 0.13f, 1f));
                        if (dark != null && light != null)
                        {
                            iconVals.Add(ov.value);
                            iconDark.Add(Lit.Str(Convert.ToBase64String(dark.EncodeToPNG())));
                            iconLight.Add(Lit.Str(Convert.ToBase64String(light.EncodeToPNG())));
                        }
                    }
                }
                if (tintVals.Count > 0)
                {
                    args.Add($"tintValues: new long[] {{ {string.Join(", ", tintVals)} }}");
                    args.Add($"tintColors: new Color[] {{ {string.Join(", ", tintCols)} }}");
                }
                if (iconVals.Count > 0)
                {
                    args.Add($"iconValues: new long[] {{ {string.Join(", ", iconVals)} }}");
                    args.Add($"iconPngDark: new string[] {{ {string.Join(", ", iconDark)} }}");
                    args.Add($"iconPngLight: new string[] {{ {string.Join(", ", iconLight)} }}");
                }
            }

            string v = ctx.NewVar(VarBase(path) + "Badges", "badges");
            w.Line($"var {v} = SasqKit.EnumBadges({string.Join(", ", args)});");
            return v;
        }

        private static string EmitPalette(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label)
        {
            // Resolve the palette NOW — the swatches travel with the file (the recipient has no library).
            var library = SasquatchPaletteLibraryManager.Current;
            var palette = library != null ? (library.GetPalette(node.ColorPaletteId) ?? library.DefaultPalette) : null;
            if (palette == null || palette.Swatches == null || palette.Swatches.Count == 0)
            {
                ctx.Note($"{path} (color palette) -> color field (no palette to bake).");
                return EmitPropertyField(w, ctx, node, path);
            }

            ctx.UseKit("Palette");
            var names = new List<string>();
            var colors = new List<string>();
            foreach (var swatch in palette.Swatches)
            {
                names.Add(Lit.Str(swatch.name ?? ""));
                colors.Add(Lit.Col(swatch.color));
            }
            string namesLit = "new[] { " + string.Join(", ", names) + " }";
            string colorsLit = "new[] { " + string.Join(", ", colors) + " }";
            var args = new List<string> { "so", ctx.PathArg(path), Lit.Str(label), namesLit, colorsLit };
            if (node.ColorPaletteLayout == ColorPaletteLayout.Grid) args.Add("layout: 0");
            if (node.ColorPaletteColumns > 0) args.Add($"columns: {node.ColorPaletteColumns}");
            if (Math.Abs(node.ColorSwatchSize - 22f) > 0.01f) args.Add($"swatchSize: {Lit.F(node.ColorSwatchSize)}");
            if (Math.Abs(node.ColorSwatchSpacing - 1f) > 0.01f) args.Add($"spacing: {Lit.F(node.ColorSwatchSpacing)}");
            if (!node.ColorShowBar) args.Add("showBar: false");
            if (node.ColorBarPlacement != ColorBarPlacement.Right) args.Add($"barPlacement: {(int)node.ColorBarPlacement}");
            if (!node.ColorShowAlpha) args.Add("showAlpha: false");
            if (node.ColorFieldMode == ColorFieldMode.HDR) { args.Add("hdr: true"); ctx.UseKit("HdrBadge"); }
            if (node.ColorCopyOnClick) args.Add("copyOnClick: true");
            AppendPaletteSetArgs(args, ctx, node.ColorPaletteId);
            string v = ctx.NewVar(VarBase(path) + "Palette", "palette");
            w.Line($"var {v} = SasqKit.Palette({string.Join(", ", args)});");
            return v;
        }

        /// <summary>
        /// Adds the baked palette LIBRARY plus the palette the designer chose, so the widget can carry the gear.
        /// A no-op when the project has fewer than two palettes — nothing to switch between, so the widget keeps
        /// the swatch arrays it always had and no gear appears.
        /// <para>
        /// The authored palette therefore travels TWICE: inline as the widget's own swatch arrays, and again as
        /// one entry of the set. Deliberate, and cheap (a palette is 6–10 colours). The inline copy is what the
        /// widget draws when the set cannot resolve — a pref naming a palette a later re-export dropped, say —
        /// so the failure mode is "the layout's own swatches", never an empty row.
        /// </para></summary>
        private static void AppendPaletteSetArgs(List<string> args, EmitContext ctx, string authoredId)
        {
            string setRef = ctx.PaletteSetRef();
            if (setRef == null) return;
            args.Add($"palettes: {setRef}");
            // The id is resolved through the library FIRST: a node left on "use the default" stores an empty
            // id, and baking that empty string would make the exported file follow whatever palette happens to
            // sit first in the set rather than the one the designer was actually looking at.
            var library = SasquatchPaletteLibraryManager.Current;
            var resolved = library != null ? (library.GetPalette(authoredId) ?? library.DefaultPalette) : null;
            if (resolved != null) args.Add($"paletteId: {Lit.Str(resolved.Id)}");
        }

        // The inline Gradient editor. Three project-asset dependencies are resolved NOW and travel as literals,
        // because the recipient has neither library: the pin picker's palette SWATCHES (same bake the color
        // palette widget does), the whole palette LIBRARY behind the picker's gear (AppendPaletteSetArgs), and
        // the preset ribbon's GRADIENTS.
        //
        // The gear itself used to be the decline here, on the reasoning that "switch which palette feeds the
        // picker" writes GradientColorPaletteId onto the LayoutNode and a standalone file has no layout asset
        // to edit. True about the STORAGE, wrong about the capability: the buyer's pick goes to EditorPrefs
        // instead, exactly as the resize grip's height and the preset ribbon's own edits already did. What
        // still does not travel is EDITING the library — "Set as Default" and "Create New Palette…" — which is
        // absent by construction rather than degraded, so it earns no fidelity note (see the kit's Palettes
        // section for the full argument).
        private static string EmitCurve(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label)
        {
            ctx.UseKit("CurveWell");

            var args = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };

            // Live clamps the two ranges differently — 24..160 fixed, 24..600 resizable — so the emitted
            // value is clamped the same way rather than passed raw. A node authored resizable and then
            // switched back can hold a height the fixed branch would never allow.
            float height = node.CurveResizable
                ? Mathf.Clamp(node.CurveWellHeight, 24f, 600f)
                : Mathf.Clamp(node.CurveWellHeight, 24f, 160f);
            // 56f, matching BindingNode's own default and the kit signature's — not 60f. The three have to
            // agree or the "omit what the designer never changed" rule silently stops applying.
            if (Math.Abs(height - 56f) > 0.01f) args.Add($"height: {Lit.F(height)}");
            // Inverted: BindingNode defaults resizable and editableMaxY ON, so the argument is emitted only
            // to turn them OFF. (Not the obvious guess for either — pinned by the untouched-node half of
            // TestExportEmitsTheAnimationCurveEditor, which is what caught them drifting in the first place.)
            if (!node.CurveResizable) args.Add("resizable: false");
            // Live reads `!CurveReadOnlyInline && !IsReadOnly`; both reasons collapse to one flag here.
            if (node.CurveReadOnlyInline || node.IsReadOnly) args.Add("readOnly: true");

            // The four authored colours travel as the STORED value, never pre-resolved: the kit re-runs
            // ResolveLegacy at draw time, because whether the light twin applies depends on the skin of the
            // machine OPENING the inspector, not the one that produced the file. Emitted only when the node
            // deviates from the dark-authored default, so an untouched curve carries no colour argument.
            if (!ColorApprox(node.CurveLineColor, WidgetPalette.CurveLineDarkDefault))
                args.Add($"lineColor: {Lit.Col(node.CurveLineColor)}");
            if (!ColorApprox(node.CurveFillColor, WidgetPalette.CurveFillDarkDefault))
                args.Add($"fillColor: {Lit.Col(node.CurveFillColor)}");
            if (!ColorApprox(node.CurveBackgroundColor, WidgetPalette.CurveWellDarkDefault))
                args.Add($"wellColor: {Lit.Col(node.CurveBackgroundColor)}");
            if (!ColorApprox(node.CurveGridColor, WidgetPalette.CurveGridDarkDefault))
                args.Add($"gridColor: {Lit.Col(node.CurveGridColor)}");

            if (!node.CurveShowGrid) args.Add("showGrid: false");
            if (Math.Abs(node.CurveGridSize - 0.25f) > 1e-4f) args.Add($"gridSize: {Lit.F(node.CurveGridSize)}");
            if (node.CurveSnapToGrid) args.Add("snapToGrid: true");
            if (node.CurveEditableMaxX) args.Add("editableMaxX: true");
            if (!node.CurveEditableMaxY) args.Add("editableMaxY: false");
            // The designer's persistent value-axis pins. Nullable in the kit, because "not overridden" and
            // "overridden to 0" are different framings and a float default cannot tell them apart.
            if (node.CurveRangeMin.isOverridden) args.Add($"rangeMin: {Lit.F(node.CurveRangeMin.value)}");
            if (node.CurveRangeMax.isOverridden) args.Add($"rangeMax: {Lit.F(node.CurveRangeMax.value)}");

            // Live's well border: width defaults to 1 and the colour resolves to the theme border when unset,
            // which is exactly the kit's own default — so only a deviation travels.
            float borderWidth = Mathf.Clamp(node.CurveBorderWidth, 0f, 6f);
            if (Math.Abs(borderWidth - 1f) > 0.01f) args.Add($"borderWidth: {Lit.F(borderWidth)}");
            if (node.CurveBorderColor.isOverridden) args.Add($"borderColor: {Lit.Col(node.CurveBorderColor.value)}");

            if (node.CurveEditorMode == CurveEditorMode.Expandable) args.Add("editorMode: 1");
            else if (node.CurveEditorMode == CurveEditorMode.ExpandableWithPreview) args.Add("editorMode: 2");
            if (node.WidgetDefaultToOpen) args.Add("defaultOpen: true");

            // Preset ribbon. The buyer's own add/delete/reset layer over these in EditorPrefs (see the kit's
            // CurvePresetStore), so the ribbon stays fully editable without a library asset behind it — the
            // library is a ScriptableObject under User Data, which is an export exclusion by design.
            var presetLib = SasquatchCurvePresetLibrary.Instance;
            if (presetLib?.Presets != null && presetLib.Presets.Count > 0)
            {
                var curves = new List<string>();
                var names = new List<string>();
                foreach (var entry in presetLib.Presets)
                {
                    if (entry?.curve == null) continue;
                    curves.Add(CurveLiteral(entry.curve));
                    names.Add(Lit.Str(entry.name ?? ""));
                }
                if (curves.Count > 0)
                {
                    args.Add("presets: new[] { " + string.Join(", ", curves) + " }");
                    args.Add("presetNames: new[] { " + string.Join(", ", names) + " }");
                }
            }

            // FoldoutKeyArg, not FoldoutKey: inside a list row this folds in the row's own element path, so
            // each row remembers its own open state instead of every row sharing one.
            if (node.CurveEditorMode != CurveEditorMode.Inline)
            {
                string curveStateKey = ctx.FoldoutKeyArg(path, label ?? "");
                if (curveStateKey != null) args.Add($"stateKey: {curveStateKey}");
            }

            string cv = ctx.NewVar(VarBase(path) + "Curve", "curve");
            w.Line($"var {cv} = SasqKit.CurveWell({string.Join(", ", args)});");
            StyleEmitter.EmitLayoutStyle(w, cv, node, ctx);
            return cv;
        }

        /// <summary>WidgetPalette.ResolveLegacy's own tolerance, so "did the designer change this colour?"
        /// is decided here exactly as "is this still the legacy default?" is decided at draw time.</summary>
        private static bool ColorApprox(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f
            && Mathf.Abs(a.b - b.b) < 0.004f && Mathf.Abs(a.a - b.a) < 0.004f;

        /// <summary>
        /// An AnimationCurve as a C# expression. An immediately-invoked lambda rather than an object
        /// initializer for the same reason the gradient's is: the tangent MODES are not part of the Keyframe
        /// struct — <c>AnimationUtility</c> owns them, and they have to be applied AFTER construction, once
        /// the constructor has sorted the keys and an index means the key it is supposed to mean.
        /// <para>Dropping them is not cosmetic: a Constant (stepped) or Linear preset baked without its modes
        /// comes back as a smooth Auto curve, i.e. a different shape wearing the same name.</para>
        /// </summary>
        private static string CurveLiteral(AnimationCurve c)
        {
            var keys = c.keys;
            var parts = new List<string>();
            foreach (var k in keys)
                parts.Add($"new Keyframe({Lit.F(k.time)}, {Lit.F(k.value)}, {Lit.F(k.inTangent)}, {Lit.F(k.outTangent)})");

            var modes = new List<string>();
            for (int i = 0; i < keys.Length; i++)
            {
                modes.Add($"AnimationUtility.SetKeyLeftTangentMode(c, {i}, (AnimationUtility.TangentMode){(int)UnityEditor.AnimationUtility.GetKeyLeftTangentMode(c, i)});");
                modes.Add($"AnimationUtility.SetKeyRightTangentMode(c, {i}, (AnimationUtility.TangentMode){(int)UnityEditor.AnimationUtility.GetKeyRightTangentMode(c, i)});");
                if (UnityEditor.AnimationUtility.GetKeyBroken(c, i)) modes.Add($"AnimationUtility.SetKeyBroken(c, {i}, true);");
            }

            return "((Func<AnimationCurve>)(() => { var c = new AnimationCurve(new[] { "
                 + string.Join(", ", parts) + " }); "
                 + $"c.preWrapMode = (WrapMode){(int)c.preWrapMode}; c.postWrapMode = (WrapMode){(int)c.postWrapMode}; "
                 + string.Join(" ", modes)
                 + " return c; }))()";
        }

        private static string EmitGradient(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label)
        {
            ctx.UseKit("GradientWell");

            var args = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };

            // Live clamps the two ranges differently — 18..80 fixed, 18..160 resizable — so the emitted value
            // is clamped the same way rather than passed raw. A node authored resizable and then switched back
            // can hold a height the fixed branch would never allow.
            float height = node.GradientResizable
                ? Mathf.Clamp(node.GradientWellHeight, 18f, 160f)
                : Mathf.Clamp(node.GradientWellHeight, 18f, 80f);
            // 28f, matching BindingNode's own default and the kit signature's — not 32f. The three have to
            // agree or the "omit what the designer never changed" rule silently stops applying.
            if (Math.Abs(height - 28f) > 0.01f) args.Add($"height: {Lit.F(height)}");
            if (node.GradientResizable) args.Add("resizable: true");
            // Live reads `!GradientReadOnlyInline && !IsReadOnly`; both reasons collapse to one flag here.
            if (node.GradientReadOnlyInline || node.IsReadOnly) args.Add("readOnly: true");
            if (node.GradientBorderWidth > 0.01f)
            {
                args.Add($"borderColor: {Lit.Col(node.GradientBorderColor)}");
                args.Add($"borderWidth: {Lit.F(Mathf.Clamp(node.GradientBorderWidth, 0f, 6f))}");
            }
            if (node.GradientPinLayout == GradientPinLayout.Merged) args.Add("merged: true");
            if (node.GradientEditorMode == GradientEditorMode.Expandable) args.Add("editorMode: 1");
            else if (node.GradientEditorMode == GradientEditorMode.ExpandableWithPreview) args.Add("editorMode: 2");
            if (node.GradientHdr) args.Add("hdr: true");
            // Inverted, because BindingNode defaults this ON: the argument is emitted only to turn it OFF.
            if (!node.GradientShowPaletteDot) args.Add("paletteDot: false");
            // NOT gated on GradientShowPaletteRow. That property is VESTIGIAL — nothing in Editor/Binding or
            // Editor/Building reads it, and the only writer is StyleClipboard copying one node to another
            // (the same shape as the UseStandardAlignment / InlinePreview false alarms in the gap doc). Live
            // shows the ribbon whenever the strip is editable, full stop, so gating on that flag here would
            // invent a divergence out of a field stuck at its default.

            // Palette swatches — the pin picker's 1-click grid. Absent library or empty palette simply means no
            // grid; the color field beside it still works, so there is nothing to note.
            var library = SasquatchPaletteLibraryManager.Current;
            var palette = library != null
                ? (library.GetPalette(node.GradientColorPaletteId) ?? library.DefaultPalette)
                : null;
            if (palette?.Swatches != null && palette.Swatches.Count > 0)
            {
                var colors = new List<string>();
                foreach (var s in palette.Swatches) colors.Add(Lit.Col(s.color));
                args.Add("swatches: new[] { " + string.Join(", ", colors) + " }");
            }
            AppendPaletteSetArgs(args, ctx, node.GradientColorPaletteId);

            // Preset ribbon. The buyer's own add/delete/reset layer over these in EditorPrefs (see the kit's
            // GradientPresetStore), so the ribbon stays fully editable without a library asset behind it.
            var presetLib = SasquatchGradientPresetLibrary.Instance;
            if (node.GradientShowPaletteRow && presetLib?.Presets != null && presetLib.Presets.Count > 0)
            {
                var grads = new List<string>();
                var names = new List<string>();
                foreach (var entry in presetLib.Presets)
                {
                    if (entry?.gradient == null) continue;
                    grads.Add(GradientLiteral(entry.gradient));
                    names.Add(Lit.Str(entry.name ?? ""));
                }
                if (grads.Count > 0)
                {
                    args.Add("presets: new[] { " + string.Join(", ", grads) + " }");
                    args.Add("presetNames: new[] { " + string.Join(", ", names) + " }");
                }
            }

            // FoldoutKeyArg, not FoldoutKey: inside a list row this folds in the row's own element path, so
            // each row remembers its own open state instead of every row sharing one. Null means "no identity
            // to key on", which the kit reads as don't-persist.
            if (node.GradientEditorMode != GradientEditorMode.Inline)
            {
                string gradientStateKey = ctx.FoldoutKeyArg(path, label ?? "");
                if (gradientStateKey != null) args.Add($"stateKey: {gradientStateKey}");
            }

            string v = ctx.NewVar(VarBase(path) + "Gradient", "gradient");
            w.Line($"var {v} = SasqKit.GradientWell({string.Join(", ", args)});");
            StyleEmitter.EmitLayoutStyle(w, v, node, ctx);
            return v;
        }

        /// <summary>A Gradient as a C# expression. An immediately-invoked lambda rather than an object
        /// initializer because SetKeys is a METHOD, not a settable property — and the keys must go in before
        /// `mode`, since SetKeys is what clamps each channel to eight.</summary>
        private static string GradientLiteral(Gradient g)
        {
            var ck = new List<string>();
            foreach (var k in g.colorKeys) ck.Add($"new GradientColorKey({Lit.Col(k.color)}, {Lit.F(k.time)})");
            var ak = new List<string>();
            foreach (var k in g.alphaKeys) ak.Add($"new GradientAlphaKey({Lit.F(k.alpha)}, {Lit.F(k.time)})");
            return "((Func<Gradient>)(() => { var g = new Gradient(); g.SetKeys("
                 + "new[] { " + string.Join(", ", ck) + " }, "
                 + "new[] { " + string.Join(", ", ak) + " }); "
                 + $"g.mode = (GradientMode){(int)g.mode}; return g; }}))()";
        }

        private static string EmitXYPad(CodeWriter w, EmitContext ctx, BindingNode node, string path, FieldInfo fi, string label)
        {
            ctx.UseKit("XYPad");
            float low = node.RangeMin.isOverridden ? node.RangeMin.value : 0f;
            float high = node.RangeMax.isOverridden ? node.RangeMax.value : 1f;
            var args = new List<string>
            {
                "so", ctx.PathArg(path), Lit.Str(label),
                $"minX: {Lit.F(low)}", $"minY: {Lit.F(low)}", $"maxX: {Lit.F(high)}", $"maxY: {Lit.F(high)}"
            };
            if (node.XYPadAlignment != ImageAlignment.Left) args.Add($"alignment: {(int)node.XYPadAlignment}");
            if (!node.XYPadShowGrid) args.Add("showGrid: false");
            if (Math.Abs(node.XYPadGridSize - 0.25f) > 0.001f) args.Add($"gridSize: {Lit.F(node.XYPadGridSize)}");
            if (node.XYPadSnapToGrid) args.Add("snapToGrid: true");
            if (node.XYPadBackgroundColor.isOverridden) args.Add($"background: {Lit.Col(node.XYPadBackgroundColor.value)}");
            if (node.XYPadGridColor.isOverridden) args.Add($"gridColor: {Lit.Col(node.XYPadGridColor.value)}");
            if (node.XYPadHandleColor.isOverridden) args.Add($"handle: {Lit.Col(node.XYPadHandleColor.value)}");
            else if (node.DisplayStyleData.accent.isOverridden) args.Add($"handle: {Lit.Col(node.DisplayStyleData.accent.value)}");
            if (node.XYPadEditableFields) args.Add("editableFields: true");
            if (node.XYPadHandleCrosshair)
            {
                args.Add("handleCrosshair: true");
                if (Math.Abs(node.XYPadCrosshairThickness - 1f) > 0.001f) args.Add($"crosshairThickness: {Lit.F(node.XYPadCrosshairThickness)}");
                if (node.XYPadCrosshairColor.isOverridden) args.Add($"crosshairColor: {Lit.Col(node.XYPadCrosshairColor.value)}");
            }
            string v = ctx.NewVar(VarBase(path) + "Pad", "pad");
            w.Line($"var {v} = SasqKit.XYPad({string.Join(", ", args)});");
            return v;
        }

        private static string EmitVectorSliders(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label, bool isVec4)
        {
            ctx.UseKit("VectorSliders");
            float low = node.RangeMin.isOverridden ? node.RangeMin.value : -1f;
            float high = node.RangeMax.isOverridden ? node.RangeMax.value : 1f;
            var args = new List<string>
            {
                "so", ctx.PathArg(path), Lit.Str(label), Bool(isVec4), $"min: {Lit.F(low)}", $"max: {Lit.F(high)}"
            };
            if (node.VectorShowNormalize) args.Add("showNormalize: true");

            // Per-axis slider look — mirrors EmitSlider so a styled vector slider carries the same knob/bar
            // styling as the standalone Slider widget.
            var s = node.DisplayStyleData;
            if (s.sliderSize != SliderSize.Normal) args.Add($"size: {(int)s.sliderSize}");
            int shape = s.sliderKnobShape == SliderKnobShape.Square ? 1
                : s.sliderKnobShape == SliderKnobShape.Capsule ? 2
                : s.sliderKnobShape == SliderKnobShape.Rectangle ? 3
                : s.sliderKnobShape == SliderKnobShape.Grip ? 4
                : 0;
            if (shape != 0) args.Add($"knobShape: {shape}");
            // Square/Rectangle knobs take the roundness VERBATIM (live: Mathf.Max(0f, roundness)), so a
            // deliberate 0 — sharp corners — has to travel. Guarding on "> 0f" swallowed it and the kit
            // fell back to its 2px default, quietly rounding off the one shape the user picked for edges.
            // Every other shape derives its own radius, so there the value is genuinely optional.
            if (shape == 1 || shape == 3) args.Add($"knobRoundness: {Lit.F(Mathf.Max(0f, s.sliderKnobRoundness))}");
            else if (s.sliderKnobRoundness > 0f) args.Add($"knobRoundness: {Lit.F(s.sliderKnobRoundness)}");
            if (s.sliderBarColor.a > 0f) args.Add($"barColor: {Lit.Col(s.sliderBarColor)}");
            if (s.sliderKnobColor.a > 0f) args.Add($"knobColor: {Lit.Col(s.sliderKnobColor)}");
            if (s.sliderBarHoverColor.a > 0f) args.Add($"barHover: {Lit.Col(s.sliderBarHoverColor)}");
            if (s.sliderKnobHoverColor.a > 0f) args.Add($"knobHover: {Lit.Col(s.sliderKnobHoverColor)}");
            if (s.cornerRadius.isOverridden) args.Add($"barRadius: {Lit.F(Mathf.Max(0f, s.cornerRadius.value))}");
            if (s.sliderSize != SliderSize.Normal || shape != 0 || s.sliderBarColor.a > 0f || s.sliderKnobColor.a > 0f)
                args.Add("styled: true");

            // Normalize-button chrome + placement — mirrors EmitButton (only meaningful when the button shows).
            if (node.VectorShowNormalize)
            {
                if (node.NormalizeButtonStyle != ButtonStyle.Modern) args.Add($"normStyle: {(int)node.NormalizeButtonStyle}");
                if (node.NormalizeButtonAccent.isOverridden) args.Add($"normAccent: {Lit.Col(node.NormalizeButtonAccent.value)}");
                if (node.NormalizeButtonCornerRadius.isOverridden) args.Add($"normRadius: {Lit.F(node.NormalizeButtonCornerRadius.value)}");
                int normSize = StyleEmitter.ButtonKitSize(node.NormalizeButtonSize);
                if (normSize != 1) args.Add($"normSize: {normSize}");
                if (node.NormalizeButtonHoverColor.isOverridden) args.Add($"normHover: {Lit.Col(node.NormalizeButtonHoverColor.value)}");
                if (node.NormalizeButtonPressedColor.isOverridden) args.Add($"normPressed: {Lit.Col(node.NormalizeButtonPressedColor.value)}");
                if (node.NormalizeButtonHoverAnimation != ButtonHoverAnimation.Lift) args.Add($"normAnimation: {(int)node.NormalizeButtonHoverAnimation}");
                if (node.VectorNormalizeAlignment != ImageAlignment.Left) args.Add($"normAlign: {(int)node.VectorNormalizeAlignment}");
            }

            // Axis color pips on each axis' value box. The kit section always ships with VectorSliders (see
            // WidgetKitEmitter.SectionDependencies), so this is a plain flag, not a UseKit decision.
            if (node.VectorAxisPips) args.Add("axisPips: true");

            string v = ctx.NewVar(VarBase(path) + "Vector", "vector");
            w.Line($"var {v} = SasqKit.VectorSliders({string.Join(", ", args)});");
            return v;
        }

        private static string EmitEuler(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label)
        {
            ctx.UseKit("Euler");
            string v = ctx.NewVar(VarBase(path) + "Euler", "euler");
            w.Line($"var {v} = SasqKit.EulerField(so, {ctx.PathArg(path)}, {Lit.Str(label)});");
            if (node.VectorAxisPips)
            {
                ctx.UseKit("AxisPips");
                w.Line($"SasqKit.AxisPips({v});");
            }
            return v;
        }

        private static bool IsPipEligible(Type ft) =>
            ft == typeof(Vector2) || ft == typeof(Vector3) || ft == typeof(Vector4)
            || ft == typeof(Vector2Int) || ft == typeof(Vector3Int) || ft == typeof(Quaternion);

        // bindPathExpr: the emitted path EXPRESSION the field binds to when that differs from the declared
        // member — a SasquatchString binds its inner `.template`. An expression rather than a path string
        // because inside a table cell the path is only a runtime value, so the caller composes it through
        // EmitContext.PathArgWithin and hands the result down. `path` remains the member, and stays the source
        // for attribute lookup and variable naming.
        private static string EmitTextArea(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label,
                                           string bindPathExpr = null)
        {
            ctx.UseKit("TextArea");
            var args = new List<string> { "so", bindPathExpr ?? ctx.PathArg(path), Lit.Str(label) };
            AppendTextBaseArgs(args, node, ctx, path, richText: false);
            if (node.TextPathMode == TextPathMode.Path)
            {
                args.Add("pathMode: true");
                if (node.PathIsFolder) args.Add("pathIsFolder: true");
                if (!string.IsNullOrEmpty(node.PathFilter)) args.Add($"pathFilter: {Lit.Str(node.PathFilter)}");
            }
            string v = ctx.NewVar(VarBase(path) + "Text", "text");
            w.Line($"var {v} = SasqKit.TextArea({string.Join(", ", args)});");
            return v;
        }

        // The base multiline-box args shared by TextArea and RichTextArea (box height, resize handle, grip,
        // placeholder, the input's corner radius, and the [TextArea]/[Multiline] shape).
        //
        // richText callers force multiline (live ORs RichTextPreview into the same decision), so they only
        // need the attribute heights.
        private static void AppendTextBaseArgs(List<string> args, BindingNode node, EmitContext ctx, string path,
                                               bool richText)
        {
            // An authored Box Height is honoured in BOTH resize modes, exactly as live honours it: on a Fixed
            // box it is the only height control there is (no grip to drag), so it beats the [TextArea]/
            // [Multiline] box rather than being capped by it. 0 = unauthored ⇒ nothing emitted.
            if (node.TextBoxHeight > 0f)
                args.Add($"boxHeight: {Lit.F(node.TextBoxHeight)}");
            if (node.TextResize != TextResizeMode.Resizable) args.Add("resizable: false");
            if (node.TextResizeHandleSize != TextResizeHandleSize.Compact) args.Add($"handleSize: {(int)node.TextResizeHandleSize}");
            if (node.GripAlignment != GripAlignment.Center) args.Add($"gripAlign: {(int)node.GripAlignment}");
            if (!string.IsNullOrEmpty(node.TextPlaceholder)) args.Add($"placeholder: {Lit.Str(node.TextPlaceholder)}");
            if (node.DisplayStyleData.cornerRadius.isOverridden)
                args.Add($"cornerRadius: {Lit.F(Mathf.Max(0f, node.DisplayStyleData.cornerRadius.value))}");

            // [TextArea]/[Multiline] is only visible by reflection, and the generated file has no reflection
            // budget — so the ANSWER is baked here, at export time, from the same attributes live reads.
            ResolveTextAreaShape(ctx, path, out bool attrMultiline, out float attrMin, out float attrMax);
            if (!richText && !attrMultiline && node.TextResize != TextResizeMode.Resizable)
                args.Add("multiline: false");   // live: no attribute + not resizable ⇒ a normal one-line field
            if (attrMin > 0f)
            {
                args.Add($"attrMinHeight: {Lit.F(attrMin)}");
                args.Add($"attrMaxHeight: {Lit.F(attrMax)}");
            }
        }

        // The live rule, verbatim (BindingFactory.Text): [TextArea(min,max)] sizes the box at 18px per line
        // plus 6px of padding/border; [Multiline(lines)] pins min and max to the same height. Either one also
        // makes the field multiline on its own, independently of the Resize mode.
        private static void ResolveTextAreaShape(EmitContext ctx, string path,
                                                 out bool attrMultiline, out float minHeight, out float maxHeight)
        {
            attrMultiline = false;
            minHeight = 0f;
            maxHeight = 0f;
            var field = ctx?.ResolveField(path);
            if (field == null) return;

            var textArea = Attribute.GetCustomAttribute(field, typeof(TextAreaAttribute)) as TextAreaAttribute;
            if (textArea != null)
            {
                attrMultiline = true;
                minHeight = textArea.minLines * 18f + 6f;
                maxHeight = textArea.maxLines * 18f + 6f;
                return;
            }
            var multiline = Attribute.GetCustomAttribute(field, typeof(MultilineAttribute)) as MultilineAttribute;
            if (multiline != null)
            {
                attrMultiline = true;
                minHeight = multiline.lines * 18f + 6f;
                maxHeight = minHeight;
            }
        }

        // bindPathExpr: see EmitTextArea — a SasquatchString binds its inner `.template`.
        private static string EmitRichText(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label,
                                           string bindPathExpr = null)
        {
            ctx.UseKit("RichText");
            var args = new List<string> { "so", bindPathExpr ?? ctx.PathArg(path), Lit.Str(label) };
            AppendTextBaseArgs(args, node, ctx, path, richText: true);
            if (!node.RichTextToolbar) args.Add("toolbar: false");
            if (node.RichTextPreviewBackground.isOverridden) args.Add($"bubbleBg: {Lit.Col(node.RichTextPreviewBackground.value)}");
            if (node.RichTextPreviewCornerRadius.isOverridden) args.Add($"bubbleRadius: {Lit.F(node.RichTextPreviewCornerRadius.value)}");

            // Bake the palette swatches (the Color row has no library to read at runtime) when a toolbar ships.
            if (node.RichTextToolbar)
            {
                var library = SasquatchPaletteLibraryManager.Current;
                var palette = library != null ? (library.GetPalette(node.ColorPaletteId) ?? library.DefaultPalette) : null;
                if (palette?.Swatches != null && palette.Swatches.Count > 0)
                {
                    var colors = new List<string>();
                    var names = new List<string>();
                    foreach (var sw in palette.Swatches) { colors.Add(Lit.Col(sw.color)); names.Add(Lit.Str(sw.name ?? "")); }
                    args.Add($"swatches: new[] {{ {string.Join(", ", colors)} }}");
                    args.Add($"swatchNames: new[] {{ {string.Join(", ", names)} }}");
                }
                AppendPaletteSetArgs(args, ctx, node.ColorPaletteId);
            }

            // No fidelity note. This widget carried one until the palette library started travelling — the
            // toolbar, tag toggling, {field} token autocomplete, the live token-resolving preview and the
            // styling always ported, and the Color row's gear now does too. All that is left behind is
            // WRITING the library (Set as Default / Create New Palette…), which is the same absent-by-
            // construction case the gradient's gear is, and is not a degradation of anything on screen.

            string v = ctx.NewVar(VarBase(path) + "Rich", "rich");
            w.Line($"var {v} = SasqKit.RichTextArea({string.Join(", ", args)});");
            return v;
        }

#if SASQ_AUDIO
        /// <summary>
        /// The AudioPreview widget, IN FULL — both modes, transport included. Nothing about it is declined.
        /// <para>
        /// This was declined for a long time on a premise that turned out to be about the wrong mechanism.
        /// Editor audio has no public *Unity* API — an AudioSource does not sound in edit mode
        /// (<c>AudioSettings.dspTime</c> does not advance outside play mode) and <c>UnityEditor.AudioUtil</c> is
        /// internal, which §2.5.g bars — but the package never used either. It drives its own P/Invoke device
        /// straight to the OS (winmm / AudioToolbox / ALSA / PulseAudio), which does not care about play mode,
        /// and P/Invoke into a system audio library is not reflection into editor internals.
        /// </para><para>
        /// The single real obstacle was that <c>NativeAudioBackend</c> selects its backend with
        /// <c>#if UNITY_EDITOR_WIN</c> / <c>#elif</c> / <c>#else</c>, and <c>WidgetKitEmitter</c> refuses every
        /// directive that is not a bare <c>#if SASQ_&lt;NAME&gt;</c>. The kit fixes that by selecting at RUNTIME
        /// (<c>Application.platform</c>) with all four backends compiled in — safe because a DllImport binds its
        /// library on first call, not at type load. See the Audio section's header comment.
        /// </para></summary>
        private static string EmitAudioPreview(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label, Type ft)
        {
            bool wave = node.AudioPreviewMode == AudioPreviewMode.ShowSoundwaves;

            // The project-wide "Disable Native Audio Preview" setting is baked, because with it on the LIVE
            // widget draws no transport either — emitting one would make the export differ from the inspector
            // it was exported from. The buyer keeps their own runtime switch (SasqKit.AudioDisabledPrefKey).
            bool transport = !SasquatchSettingsManager.Current.DisableNativeAudioPreview;

            ctx.UseKit("Audio");
            var args = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };

            if (!wave)
            {
                // Standard mode: field + play button + a play-head sliding across the field.
                // align: ImageAlignment — 0 Left (the default, so it emits nothing), 1 Centre, 2 Right.
                if (node.AudioPlayAlignment != ImageAlignment.Left)
                    args.Add($"align: {(int)node.AudioPlayAlignment}");
                args.Add($"objectType: {TypeOf(typeof(AudioClip).IsAssignableFrom(ft) ? ft : typeof(AudioClip))}");
                if (!transport) args.Add("transport: false");
                string sv = ctx.NewVar(VarBase(path) + "Audio", "audio");
                w.Line($"var {sv} = SasqKit.AudioField({string.Join(", ", args)});");
                return sv;
            }

            if (Math.Abs(node.AudioWaveformHeight - 50f) > 0.01f)
                args.Add($"waveHeight: {Lit.F(node.AudioWaveformHeight)}");
            // placement: ElementPlacement — 0 Right (the default, so it emits nothing), 1 Left, 2 Above, 3 Below.
            if (node.AudioWaveformPlacement != ElementPlacement.Right)
                args.Add($"placement: {(int)node.AudioWaveformPlacement}");
            // Live's waveform ink is the node's accent override when set, else the theme default (which the kit
            // carries as SasqTheme.Waveform) — so an un-overridden node emits no colour at all.
            if (node.DisplayStyleData.accent.isOverridden)
                args.Add($"waveColor: {Lit.Col(node.DisplayStyleData.accent.value)}");
            // Live constrains the picker to AudioClip, or to the array/list ELEMENT type when that is itself a
            // clip subclass. `ft` is already the resolved element type here, so the same rule is one test.
            args.Add($"objectType: {TypeOf(typeof(AudioClip).IsAssignableFrom(ft) ? ft : typeof(AudioClip))}");
            if (!transport) args.Add("transport: false");
            if (node.AudioPlayAlignment != ImageAlignment.Left)
                args.Add($"playAlign: {(int)node.AudioPlayAlignment}");
            // AudioInteractive defaults to TRUE on the node, so the kit default matches and only an author who
            // turned scrubbing OFF emits the argument.
            if (!node.AudioInteractive) args.Add("interactive: false");
            string v = ctx.NewVar(VarBase(path) + "Audio", "audio");
            w.Line($"var {v} = SasqKit.AudioWave({string.Join(", ", args)});");
            return v;
        }
#endif

        private static string EmitParticlePreview(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label, Type ft)
        {
            ctx.UseKit("ParticlePreview");
            // Pass the resolved label (not null): the ObjectField needs it explicitly (unlike a PropertyField).
            var args = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };
            if (Math.Abs(node.ParticleThumbnailSize - 128f) > 0.01f) args.Add($"size: {Lit.F(node.ParticleThumbnailSize)}");
            // mode: 0 Field+Thumbnail / 1 Standard / 2 ThumbnailOnly / 3 Expandable (ParticlePreviewMode int, no gaps).
            int mode = (int)node.ParticlePreviewMode;
            if (mode != 0) args.Add($"mode: {mode}");
            // Alignment of the thumbnail (shared PreviewAlignment; Left default).
            if (node.PreviewAlignment != ImageAlignment.Left) args.Add($"alignment: {(int)node.PreviewAlignment}");
            if (node.ParticleBackgroundColor.a > 0f) args.Add($"background: {Lit.Col(node.ParticleBackgroundColor)}");
            if (Math.Abs(node.ParticlePosterTime - 0.35f) > 0.001f) args.Add($"posterTime: {Lit.F(node.ParticlePosterTime)}");
            if (!node.ParticlePreviewInteractive) args.Add("interactive: false");
            if (node.ParticlePlayButtonAnchor != PreviewAnchor.Center) args.Add($"playAnchor: {(int)node.ParticlePlayButtonAnchor}");
            if (Math.Abs(node.ParticlePlayButtonWidth - 32f) > 0.01f) args.Add($"playWidth: {Lit.F(node.ParticlePlayButtonWidth)}");
            if (node.ParticlePlayButtonOffset != Vector2.zero)
            {
                args.Add($"playOffsetX: {Lit.F(node.ParticlePlayButtonOffset.x)}");
                args.Add($"playOffsetY: {Lit.F(node.ParticlePlayButtonOffset.y)}");
            }
            if (Math.Abs(node.ParticlePreviewZoom - 1f) > 0.001f) args.Add($"vfxZoom: {Lit.F(node.ParticlePreviewZoom)}");
            // Expandable-only foldout options.
            if (node.ParticlePreviewMode == ParticlePreviewMode.Expandable)
            {
                if (node.ParticleDefaultToOpen) args.Add("defaultOpen: true");
                if (!node.ParticlePlayOnOpen) args.Add("playOnOpen: false");
            }
            args.Add($"objectType: {TypeOf(ft)}"); // filters the picker + resolves ParticleSystem/GameObject/VFX
            string v = ctx.NewVar(VarBase(path) + "Particles", "particles");
            w.Line($"var {v} = SasqKit.ParticlePreview({string.Join(", ", args)});");
            return v;
        }

        private static string EmitUnityEvent(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label)
        {
            ctx.UseKit("UnityEventCard");
            var a = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };
            if (node.UnityEventCompact) a.Add("compact: true");
            if (!node.UnityEventDefaultToOpen) a.Add("defaultOpen: false");
            if (node.UnityEventAccentColor.isOverridden) a.Add($"accentColor: {Lit.Col(node.UnityEventAccentColor.value)}");
            if (node.UnityEventCardColor.isOverridden) a.Add($"cardColor: {Lit.Col(node.UnityEventCardColor.value)}");
            if (!node.UnityEventCodePeek) a.Add("codePeek: false");
            string v = ctx.NewVar(VarBase(path) + "Event", "uevent");
            w.Line($"var {v} = SasqKit.UnityEventCard({string.Join(", ", a)});");
            return v;
        }

        private static string EmitDropdown(CodeWriter w, EmitContext ctx, BindingNode node, string path, Type ft, string label)
        {
            // Enum fields are their own value list — always the styled + searchable enum dropdown (matches the live
            // CreateDropDown, which routes enums before consulting the source selector).
            if (ft != null && ft.IsEnum) return EmitEnumDropdown(w, ctx, node, path, ft, label);

            // Resolve Auto the same way the live widget does.
            DropdownSource source = node.DropdownSource;
            if (source == DropdownSource.Auto)
            {
                if (ft == typeof(string)) source = DropdownSource.Tags;
                else if (ft == typeof(int)) source = DropdownSource.Layer;
                else if (ft == typeof(LayerMask)) source = DropdownSource.LayerMask;
                else source = DropdownSource.Method;
            }

            switch (source)
            {
                case DropdownSource.Tags when ft == typeof(string):
                {
                    ctx.UseKit("Dropdowns");
                    string v = ctx.NewVar(VarBase(path) + "Tag", "tag");
                    var a = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };
                    AddDropdownStyleArgs(a, node);
                    w.Line($"var {v} = SasqKit.TagDropdown({string.Join(", ", a)});");
                    return v;
                }
                case DropdownSource.Layer when ft == typeof(int):
                {
                    ctx.UseKit("Dropdowns");
                    string v = ctx.NewVar(VarBase(path) + "Layer", "layer");
                    var a = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };
                    AddDropdownStyleArgs(a, node);
                    w.Line($"var {v} = SasqKit.LayerDropdown({string.Join(", ", a)});");
                    return v;
                }
                // The `when` is the emitter half of the live CreateLayerMaskField guard, and it is here for
                // the same reason its Tags/Layer/Method siblings have one: without it a mis-sourced node
                // emits SasqKit.LayerMaskDropdown against a string field, and the BUYER ships the inert
                // picker. Falls through to EmitPropertyField, which is what the live path degrades to too.
                case DropdownSource.LayerMask when ft == typeof(LayerMask) || ft == typeof(int):
                {
                    ctx.UseKit("Dropdowns");
                    string v = ctx.NewVar(VarBase(path) + "Mask", "mask");
                    var a = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };
                    AddDropdownStyleArgs(a, node, includeSearchable: false); // the mask popup isn't searchable
                    w.Line($"var {v} = SasqKit.LayerMaskDropdown({string.Join(", ", a)});");
                    return v;
                }
                case DropdownSource.Method when !string.IsNullOrEmpty(node.ValueSourceMethod):
                {
                    ctx.UseKit("Dropdowns");
                    string v = ctx.NewVar(VarBase(path) + "Dropdown", "dropdown");
                    var a = new List<string> { "so", ctx.PathArg(path), Lit.Str(label), Lit.Str(node.ValueSourceMethod) };
                    AddDropdownStyleArgs(a, node);
                    w.Line($"var {v} = SasqKit.MethodDropdown({string.Join(", ", a)});");
                    return v;
                }
            }
            return EmitPropertyField(w, ctx, node, path);
        }

        private static string EmitEnumDropdown(CodeWriter w, EmitContext ctx, BindingNode node, string path, Type ft, string label)
        {
            ctx.UseKit("Dropdowns");
            // Display names in enumValueIndex order (= Enum.GetNames order), honoring [InspectorName].
            var names = new List<string>();
            foreach (var raw in Enum.GetNames(ft))
            {
                names.Add(EnumMemberLabel.Resolve(ft, raw));
            }
            string namesLit = "new[] { " + string.Join(", ", names.ConvertAll(Lit.Str)) + " }";
            var a = new List<string> { "so", ctx.PathArg(path), Lit.Str(label), namesLit };
            AddDropdownStyleArgs(a, node);
            string v = ctx.NewVar(VarBase(path) + "Dropdown", "dropdown");
            w.Line($"var {v} = SasqKit.EnumDropdown({string.Join(", ", a)});");
            return v;
        }

        // The per-node dropdown popup styling shared by every dropdown kind (searchable + popup bg + item text/hover).
        // The LayerMask multi-select popup has no search box, so its caller passes includeSearchable: false.
        private static void AddDropdownStyleArgs(List<string> args, BindingNode node, bool includeSearchable = true)
        {
            if (includeSearchable && node.SearchableDropdown) args.Add("searchable: true");
            if (node.DropdownPopupBackground.isOverridden) args.Add($"popupBg: {Lit.Col(node.DropdownPopupBackground.value)}");
            var its = node.DropdownItemTextStyle;
            if (its.color.isOverridden) args.Add($"itemTextColor: {Lit.Col(its.color.value)}");
            if (TextStyleApplier.IsUsableFontSize(its.fontSize)) args.Add($"itemTextSize: {Lit.F(its.fontSize.value)}");
            if (its.fontStyle.isOverridden) args.Add($"itemFontStyle: {(int)its.fontStyle.value}");
            if (node.DropdownItemHoverBackground.isOverridden) args.Add($"itemHoverBg: {Lit.Col(node.DropdownItemHoverBackground.value)}");
            if (node.DropdownItemHoverColor.isOverridden) args.Add($"itemHoverColor: {Lit.Col(node.DropdownItemHoverColor.value)}");
        }

        private static string EmitSRef(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label, Type baseType)
        {
            ctx.UseKit("SRef");
            ctx.UseKit("Dropdowns"); // StyleDropdownButton + SasqSearchableDropdown live there
            string typeRef = TypeOf(baseType);
            var a = new List<string> { "so", ctx.PathArg(path), Lit.Str(label), typeRef };
            // Polymorphic search is its own flag (defaults ON, like the live picker) — only emit when off.
            if (!node.PolymorphicSearchable) a.Add("searchable: false");
            AddDropdownStyleArgs(a, node, includeSearchable: false); // popup bg + item text/hover styling
            // Same story for the hover code peek: on by default in the kit, so only the OFF state travels.
            if (!node.PolymorphicCodePeek) a.Add("codePeek: false");
            string v = ctx.NewVar(VarBase(path) + "Picker", "picker");
            w.Line($"var {v} = SasqKit.SRefPicker({string.Join(", ", a)});");
            return v;
        }

        private static string EmitInlineEditor(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label, Type ft)
        {
            ctx.UseKit("InlineEditor");
            // Pass the resolved label (not null): it labels the field AND titles the foldout, mirroring live.
            var a = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };
            // expandable + defaultOpen both default TRUE in the kit (a collapsible, open editor) — emit only when off.
            if (!node.Expandable) a.Add("expandable: false");
            if (!node.DefaultToOpen) a.Add("defaultOpen: false");
            if (node.CreateAssetEnabled) { a.Add("createAsset: true"); a.Add($"objectType: {TypeOf(ft)}"); }
            // Only the EXPANDABLE look has a fold to remember. Live keys this one on the property path
            // (BindingFactory.InlineEditor's FoldoutStateStore.Key call), not on structure — mirror that.
            if (node.Expandable)
            {
                // FoldoutKeyArg: inside a list row this folds in the row's own element path, so one row's
                // open editor doesn't open every row's.
                string inlineStateKey = ctx.FoldoutKeyArg(path, label ?? "");
                if (inlineStateKey != null) a.Add($"stateKey: {inlineStateKey}");
            }
            string v = ctx.NewVar(VarBase(path) + "Inline", "inline");
            w.Line($"var {v} = SasqKit.InlineEditor({string.Join(", ", a)});");
            return v;
        }

        private static string EmitAssetPreview(CodeWriter w, EmitContext ctx, BindingNode node, string path, string label, Type ft)
        {
            ctx.UseKit("AssetPreview");
            // Pass the resolved label (not null): the ObjectField needs it explicitly (unlike a PropertyField).
            var args = new List<string> { "so", ctx.PathArg(path), Lit.Str(label) };
            if (Math.Abs(node.PreviewSize - 64f) > 0.01f) args.Add($"previewSize: {Lit.F(node.PreviewSize)}");
            // mode: 0 Standard / 1 ThumbnailOnly / 2 Expandable (kit enum, collapsing the AssetPreviewMode int gap).
            int mode = node.AssetPreviewMode == AssetPreviewMode.ThumbnailOnly ? 1
                     : node.AssetPreviewMode == AssetPreviewMode.Expandable ? 2 : 0;
            if (mode != 0) args.Add($"mode: {mode}");
            // Standard mode honors the 4-way placement (Right default); Thumbnail/Expandable honor the alignment.
            if (mode == 0 && node.PreviewPlacement != ElementPlacement.Right) args.Add($"placement: {(int)node.PreviewPlacement}");
            if (mode != 0 && node.PreviewAlignment != ImageAlignment.Left) args.Add($"alignment: {(int)node.PreviewAlignment}");
            if (node.CreateAssetEnabled) args.Add("createAsset: true");
            // Interactive orbit / pan / zoom viewport (kit default true, matching the model). Emit only when OFF.
            if (!node.AssetPreviewInteractive) args.Add("interactive: false");
            args.Add($"objectType: {TypeOf(ft)}"); // filters the picker + drives the Create button
            // Only the EXPANDABLE mode has anything to remember. Live keys this one on the property path
            // (BindingFactory.Preview's FoldoutStateStore.Key call), not on structure — mirror that.
            if (node.AssetPreviewMode == AssetPreviewMode.Expandable)
            {
                string previewStateKey = ctx.FoldoutKeyArg(path, label ?? "");
                if (previewStateKey != null) args.Add($"stateKey: {previewStateKey}");
            }
            string v = ctx.NewVar(VarBase(path) + "Preview", "preview");
            w.Line($"var {v} = SasqKit.AssetPreviewBox({string.Join(", ", args)});");
            return v;
        }

        /// <summary>A <c>typeof(global::Full.Name)</c> expression (nested <c>+</c> flattened to <c>.</c>).</summary>
        private static string TypeOf(Type t) => "typeof(global::" + t.FullName.Replace('+', '.') + ")";

        // ---- Helpers ---------------------------------------------------------------------------------------

        /// <summary>Emits the node's per-field styling (label/value text styles + field background) onto the
        /// built element.</summary>
        public static void EmitFieldStyling(CodeWriter w, EmitContext ctx, BindingNode node, string v)
        {
            var l = node.LabelTextStyle;
            var f = node.FieldTextStyle;
            var args = new List<string>();
            if (l.color.isOverridden) args.Add($"labelColor: {Lit.Col(l.color.value)}");
            if (TextStyleApplier.IsUsableFontSize(l.fontSize)) args.Add($"labelSize: {Lit.F(l.fontSize.value)}");
            if (l.fontStyle.isOverridden) args.Add($"labelFontStyle: {(int)l.fontStyle.value}");
            if (l.align.isOverridden) args.Add($"labelAlign: {(int)l.align.value}");
            if (f.color.isOverridden) args.Add($"textColor: {Lit.Col(f.color.value)}");
            if (TextStyleApplier.IsUsableFontSize(f.fontSize)) args.Add($"textSize: {Lit.F(f.fontSize.value)}");
            if (f.fontStyle.isOverridden) args.Add($"textFontStyle: {(int)f.fontStyle.value}");
            if (node.FieldBackground.isOverridden) args.Add($"fieldBg: {Lit.Col(node.FieldBackground.value)}");
            if (args.Count > 0)
            {
                ctx.UseKit(null); // Core (StyleField)
                w.Line($"SasqKit.StyleField({v}, {string.Join(", ", args)});");
            }

            // Size preset (Compact / Large): reproduces the live sq-size-- USS class the export has no sheet for.
            // UseKit MUST be claimed here as well as in the StyleField branch above. A layout whose ONLY kit
            // usage is a size preset — legacy layouts still carry DisplayStyleData.size, and Copy Style passes
            // the whole struct along — emitted SasqKit.FieldSize into a file with no kit block at all, and the
            // buyer got CS0103 on a generated file they cannot fix.
            var size = node.DisplayStyleData.size;
            if (size == DisplaySize.Compact || size == DisplaySize.Large)
            {
                ctx.UseKit(null); // Core (FieldSize)
                w.Line($"SasqKit.FieldSize({v}, {(size == DisplaySize.Compact ? 1 : 2)});");
            }
        }

        /// <summary>
        /// An enum member's constant as a long. Twin of <c>SasquatchEnumBadgeControl.ToLong</c>: a ulong-backed
        /// enum whose member sets the high bit (<c>All = ulong.MaxValue</c> is the common one) overflows
        /// <see cref="Convert.ToInt64(object)"/>, which used to throw straight out of the exporter. Reinterpret
        /// the bits instead — the kit compares the same reinterpreted values, so the round trip is exact.
        /// </summary>
        private static long EnumRawToLong(object raw)
        {
            try { return Convert.ToInt64(raw); }
            catch
            {
                try { return unchecked((long)Convert.ToUInt64(raw)); }
                catch { return 0; }
            }
        }

        /// <summary>
        /// Resolves a widget's numeric bounds exactly as the live <c>BindingFactory.ResolveLimits</c> does: a
        /// designer override wins, then a <c>[Range]</c> attribute, then the fallback the CALLER supplies.
        /// <para>
        /// The fallback belongs to the caller because it is per-WIDGET, not per-type. The Slider splits by type
        /// (0..1 float, 0..100 int — <c>BindingFactory.Slider</c>); the Progress Bar uses 0..100 for both
        /// (<c>BindingFactory.ProgressBar</c>). Deriving it here from <c>isInt</c> alone silently gave every float
        /// progress bar a 0..1 range, pinning the fill full and letting a scrub overwrite the value with a 0..1
        /// fraction.
        /// </para>
        /// </summary>
        private static void ResolveLimits(BindingNode node, FieldInfo fi, float defaultLow, float defaultHigh,
                                          out float low, out float high)
        {
            RangeAttribute ra = fi != null ? fi.GetCustomAttribute<RangeAttribute>() : null;
            low = node.RangeMin.isOverridden ? node.RangeMin.value : (ra != null ? ra.min : defaultLow);
            high = node.RangeMax.isOverridden ? node.RangeMax.value : (ra != null ? ra.max : defaultHigh);
        }

        /// <summary>
        /// Baked twin of <c>BindingFactory.ResolveStepperBounds</c>: the tightest floor / ceiling the field
        /// declares (<c>[Range]</c>, <c>[Min]</c>) plus the binding's own validation range when Clamp to Range is
        /// on. Null means "that side is unbounded" — the kit then leaves it alone. Warn-only validation
        /// deliberately contributes nothing: going out of range is how the warning is meant to appear.
        /// </summary>
        private static void ResolveStepperBounds(BindingNode node, FieldInfo fi, out float? lo, out float? hi)
        {
            float? l = null, h = null;
            void TightenLo(float v) { if (!l.HasValue || v > l.Value) l = v; }
            void TightenHi(float v) { if (!h.HasValue || v < h.Value) h = v; }

            RangeAttribute ra = fi != null ? fi.GetCustomAttribute<RangeAttribute>() : null;
            if (ra != null) { TightenLo(ra.min); TightenHi(ra.max); }

            MinAttribute ma = fi != null ? fi.GetCustomAttribute<MinAttribute>() : null;
            if (ma != null) TightenLo(ma.min);

            if (node.ValidateRange && node.ClampToRange)
            {
                if (node.RangeHasMin) TightenLo(node.ValidationMin);
                if (node.RangeHasMax) TightenHi(node.ValidationMax);
            }

            // Contradictory claims: the tighter FLOOR wins and the ceiling follows it (live does the same), so the
            // value lands on one reachable point instead of an empty window.
            if (l.HasValue && h.HasValue && l.Value > h.Value) h = l;
            lo = l; hi = h;
        }

        private static bool IsStringCollection(Type ft)
        {
            if (ft == null) return false;
            if (ft == typeof(string[])) return true;
            return ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>)
                && ft.GetGenericArguments()[0] == typeof(string);
        }

        /// <summary>Label for a kit/stock control: "" when hidden, the override when set, else the nicified field name.</summary>
        private static string StockLabel(BindingNode node, string defaultLabel)
        {
            if (node.IsLabelHidden) return "";
            return node.EffectiveLabelOverride ?? defaultLabel ?? "";
        }

        private static string Bool(bool b) => b ? "true" : "false";

        private static string VarBase(string path)
        {
            if (string.IsNullOrEmpty(path)) return "field";
            int dot = path.LastIndexOf('.');
            return dot >= 0 ? path.Substring(dot + 1) : path;
        }

        /// <summary>A short widget label for the degradation note, or null for display types that PropertyField renders faithfully.</summary>
        private static string DegradeNote(BindingNode node, Type ft)
        {
            // NO DICTIONARY ARM HERE ANY MORE, and its absence is deliberate — the same decision the Spring,
            // Gradient and Curve ports made when each of them closed. Both dictionary shapes now render
            // through the kit's own Dictionary section with all four styles, all three row frames, the
            // separator colour, drag-reorder, the per-row menu and the row search, so a note would invent a
            // gap rather than report one. What is left over is EXACT PARITY rather than a silent hole: a
            // SerializedDictionary and a native Dictionary both reach EmitDictionaryTable ahead of this, and
            // the one case that still falls through — the Dictionary display type parked on a field whose code
            // type is no longer a dictionary — is what live degrades to a stock PropertyField as well.
            switch (node.DisplayType)
            {
                // DisplayType.Spring is NOT here, and its absence is deliberate. The widget now ports in
                // full, and the one case that still falls through — Spring parked on a field that is not a
                // Duration/Bounce struct — is at EXACT PARITY: live's CreateSpringEditor opens with the same
                // guard and returns the same stock PropertyField. A note there would invent a gap rather than
                // report one, which is the mistake the shaped-section tint note was removed for.
                case DisplayType.CustomWidget: return "custom widget";
                // DisplayType.Dictionary is NOT here, and its absence is deliberate — see the paragraph at the
                // top of this method. The table ports in full through the kit's Dictionary section, and the
                // one case that still falls through (the display type parked on a field that is no longer a
                // dictionary) is exact parity with what live draws for the same input.
                // DisplayType.AnimationCurveEditor is NOT here, and its absence is deliberate — the same
                // decision the Spring and Gradient ports made. The widget ports in full, and the one case
                // that still falls through (the display type parked on a field that is not an
                // AnimationCurve) is exact PARITY: live's CreateAnimationCurveEditor opens with the same
                // propertyType guard and returns the same stock PropertyField. A note there would invent a
                // gap rather than report one.
                // DisplayType.Gradient is NOT here: the inline gradient editor ports in full (kit section
                // GradientWell). A Gradient DisplayType on a non-Gradient field still lands on the generic
                // PropertyField below, which is what live does too — so it is parity, not a degradation.
#if !SASQ_AUDIO
                // With the module present, EmitAudioPreview writes its own per-mode note and never reaches here.
                case DisplayType.AudioPreview: return "audio preview (the Audio built-in module is not installed)";
#endif
#if !SASQ_PARTICLES
                case DisplayType.ParticleSystemPreview: return "particle preview (the Particle System built-in module is not installed)";
#endif
                default:
                    return null; // Default, DropDown(enum), Checkbox toggle, and anything else PropertyField
                                 // renders well — plus every widget the kit now reproduces.
            }
        }
    }
}
