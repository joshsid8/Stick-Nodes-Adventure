using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;
using SasquatchSpring;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Builds the UI Toolkit field for a bound property — the central widget factory. The dispatcher and shared
    /// helpers live here; each widget family lives in a <c>BindingFactory.*.cs</c> partial under <c>Widgets/</c>.
    /// </summary>
    public static partial class BindingFactory
    {
        /// <summary>
        /// Registers a value-changed handler that runs only for a REAL change. Use this instead of
        /// <c>RegisterValueChangedCallback</c> on a BOUND field whenever the handler WRITES anything.
        /// </summary>
        /// <remarks>
        /// Unity's two-way binding SENDS a <c>ChangeEvent</c> the moment a bound field attaches to a panel
        /// (<c>SerializedObjectBindingToBaseField.OnFieldAttached</c> → <c>SendEvent</c>), so a plain
        /// <c>RegisterValueChangedCallback</c> on a bound field fires once per BUILD with nobody having touched
        /// anything. That notification is the initial value announcing itself, and carries
        /// <c>previousValue == newValue</c> — which is how it is told apart from a real edit.
        ///
        /// Harmless for a handler that only repaints. NOT harmless for one that writes: the SasquatchString
        /// owner bake rode such a callback and stamped <c>source</c> onto every object the user merely SELECTED
        /// — a fake prefab override on an instance, a dirtied scene on a plain object. Building an inspector
        /// must perform zero SerializedProperty writes, and "it only runs on change" is not true here.
        /// </remarks>
        internal static void RegisterUserValueChanged<T>(VisualElement field, Action<ChangeEvent<T>> onChanged)
        {
            if (field == null || onChanged == null) return;
            field.RegisterCallback<ChangeEvent<T>>(evt =>
            {
                if (EqualityComparer<T>.Default.Equals(evt.previousValue, evt.newValue)) return;
                onChanged(evt);
            });
        }

        /// <summary>
        /// Builds the UI Toolkit field for a single bound property: dispatches on the node's <see cref="DisplayType"/>
        /// (falling back to a default <c>PropertyField</c>), then applies read-only state, alignment, styling,
        /// tooltip and any header wrapping. The central entry point used by both the inspector and the designer.
        /// </summary>
        /// <param name="isTableCell">
        /// True ONLY for a real Table-mode cell. Distinct from <paramref name="forceHideLabel"/>, which merely
        /// says "the caller supplies the label" and is also true for Columns / Grid / Frame cells, element
        /// template rows, SerializedDictionary rows and ColorPalette swatches — none of which has a column
        /// header. Affix suppression keys off THIS flag: reading forceHideLabel instead deleted the affix in
        /// five non-table contexts with no header anywhere to carry it.
        /// </param>
        public static VisualElement CreateBindingField(BindingNode node, string resolvedPropertyPath, SerializedObject serializedObject, bool? forceHideLabel = null, bool designMode = false, bool designInteract = false, bool isTableCell = false)
        {
            if (string.IsNullOrEmpty(resolvedPropertyPath))
            {
                return new Label("BindingNode has no property path.");
            }

            var prop = serializedObject.FindProperty(resolvedPropertyPath);
            if (prop == null)
            {
                // UnityEngine.Object '!= null' safely covers a destroyed target, so .GetType() can't NRE here.
                var t = serializedObject.targetObject;
                string targetName = t != null ? t.GetType().Name : "<none>";
                return new Label($"Property path '{node.PropertyPath}' not found on target {targetName}.");
            }

            VisualElement fieldElement = null;
            // When a widget wraps its real field in a plain container (the Default + Create-button row),
            // the label-column alignment class must land on the INNER field, not the wrapper.
            VisualElement alignmentTarget = null;
            // forceHideLabel, when set, overrides the node's own label state — used by table cells, where the
            // column header carries the label instead of the field. Otherwise the node's setting wins (a blanked
            // Label, i.e. HiddenSentinel, or the legacy HideLabel flag — both surfaced via IsLabelHidden).
            bool hideLabel = forceHideLabel ?? node.IsLabelHidden;
            string resolvedLabel = hideLabel ? "" : (node.EffectiveLabelOverride ?? prop.displayName);

            // A SpringConfig field is ALWAYS the Spring widget — it has no useful Unity default, and its own
            // SpringConfigDrawer is superseded here. Auto-routed (wins over the custom-drawer guard below) so even
            // a Default or legacy node renders Spring; the offered list also forces Spring (DisplayTypeCatalog).
            if (IsSpringConfigProperty(prop))
            {
                fieldElement = CreateSpringEditor(node, prop, resolvedLabel);
            }
            // A SerializedDictionary is ALWAYS the Sasquatch Key/Value table, on exactly the SpringConfig
            // precedent above: there is no other sensible widget for it, and its own SerializedDictionaryDrawer
            // draws the same table with none of the node's styling. Auto-routed ahead of the custom-drawer arm
            // below, which is what used to catch it (as a PropertyField into that drawer) and is precisely why
            // the Dictionary Style and Row Frame could not reach it. Works on the 6000.0 floor - this shape is
            // ours, so it needs none of the 6.6 dictionary APIs.
            else if (IsSerializedDictionaryProperty(prop))
            {
                fieldElement = CreateDictionaryTable(node, prop, hideLabel ? "" : resolvedLabel, designMode);
            }
            // Only route through PropertyField for custom drawers when the user hasn't explicitly
            // picked a widget. An explicit widget choice wins (drawers still apply via Auto).
            // Exception: Quaternion registers a BUILT-IN drawer whose PropertyField is an IMGUI fallback
            // (the label column can't align it, its text renders soft, and it overflows narrow hosts) —
            // a Quaternion ALWAYS renders as the euler Vector3Field in the Default case below instead.
            else if (node.DisplayType == DisplayType.Default && !node.InlinePreview &&
                serializedObject.targetObject != null &&
                prop.propertyType != SerializedPropertyType.Quaternion &&
                SasquatchReflectionUtility.HasCustomPropertyDrawer(serializedObject.targetObject.GetType(), resolvedPropertyPath))
            {
                var propField = new PropertyField(prop);
                if (hideLabel)
                {
                    propField.label = "";
                }
                else if (node.EffectiveLabelOverride != null)
                {
                    propField.label = node.EffectiveLabelOverride;
                }
                fieldElement = propField;
            }
            else if (prop.propertyType == SerializedPropertyType.ManagedReference &&
                     (node.DisplayType == DisplayType.Default || node.DisplayType == DisplayType.SerializeReference))
            {
                // Polymorphic [SerializeReference] field: auto-detected (Default) or explicitly chosen.
                // Renders a type picker + inline fields for the chosen concrete type. A custom drawer on
                // the type still wins via the guard above (DisplayType.Default + HasCustomPropertyDrawer).
                fieldElement = CreateSerializeReferenceWrapper(node, prop, resolvedLabel);
            }
            else if (IsUnityEventProperty(node, prop, resolvedPropertyPath, serializedObject))
            {
                // Custom UnityEvent card drawer — ONLY when the UnityEvent widget is explicitly chosen. A field
                // left on Default renders Unity's standard UnityEvent drawer instead (falls through to the switch
                // below → PropertyField). The global Enable Custom UnityEvent Drawer setting can still opt out.
                fieldElement = UnityEventWidget.Create(node, prop, resolvedLabel);
            }
            else if (node.InlinePreview && prop.propertyType == SerializedPropertyType.ObjectReference
                     && node.DisplayType != DisplayType.AssetPreview && node.DisplayType != DisplayType.InlineEditor)
            {
                // Legacy "Inline Preview" toggle path (pre-dates the dedicated widgets). Guarded so the
                // AssetPreview and InlineEditor widgets always route through their own creators in the switch.
                fieldElement = CreateInlineEditorWrapper(node, prop, resolvedLabel);
            }
            // NOTE: deliberately NO enum/LayerMask auto-route into the custom dropdown here. Dropdown options
            // that linger on a node (Searchable on, popup styling from a prior DropDown selection) must not
            // re-skin a Default field: Default always renders Unity's stock PropertyField, so a dev who prefers
            // Unity's own dropdowns can keep them by simply choosing Default.
            else
            {
                switch (node.DisplayType)
                {
                    case DisplayType.MinMaxSlider:
                        fieldElement = CreateMinMaxSlider(node, prop, resolvedLabel, isTableCell);
                        break;
                    case DisplayType.Slider:
                        fieldElement = CreateSlider(node, prop, resolvedLabel, isTableCell);
                        break;
                    case DisplayType.Toggle:
                        fieldElement = CreateToggle(node, prop, resolvedLabel);
                        break;
                    case DisplayType.Text:
                        fieldElement = CreateTextField(node, prop, resolvedLabel, out alignmentTarget);
                        break;
                    case DisplayType.ProgressBar:
                        fieldElement = CreateProgressBar(node, prop, resolvedLabel, isTableCell);
                        break;
                    case DisplayType.ColorPalette:
                        fieldElement = CreateColorPalette(node, prop, resolvedLabel);
                        break;
                    case DisplayType.UnityEvent:
                        fieldElement = UnityEventWidget.Create(node, prop, resolvedLabel);
                        break;
                    case DisplayType.DropDown:
                        fieldElement = CreateDropDown(node, prop, resolvedLabel);
                        break;
                    case DisplayType.AssetPreview:
                        fieldElement = CreateAssetPreview(node, prop, resolvedLabel);
                        break;
                    case DisplayType.InlineEditor:
                        fieldElement = CreateInlineEditorWrapper(node, prop, resolvedLabel);
                        break;
                    case DisplayType.SerializeReference:
                        // Normally caught by the managed-reference branch above; reached only if the field
                        // isn't actually a managed reference (e.g. the field type changed under the node).
                        // CreateSerializeReferenceWrapper falls back to a plain field in that case.
                        fieldElement = CreateSerializeReferenceWrapper(node, prop, resolvedLabel);
                        break;
#if SASQ_AUDIO
                    case DisplayType.AudioPreview:
                        fieldElement = CreateAudioPreview(node, prop, resolvedLabel);
                        break;
#endif
#if SASQ_PARTICLES
                    case DisplayType.ParticleSystemPreview:
                        fieldElement = CreateParticleSystemPreview(node, prop, resolvedLabel);
                        break;
#endif
                    case DisplayType.AnimationCurveEditor:
                        fieldElement = CreateAnimationCurveEditor(node, prop, resolvedLabel);
                        break;
                    case DisplayType.Gradient:
                        fieldElement = CreateGradientEditor(node, prop, resolvedLabel);
                        break;
                    case DisplayType.Spring:
                        fieldElement = CreateSpringEditor(node, prop, resolvedLabel);
                        break;
                    case DisplayType.EnumBadgeGrid:
                        fieldElement = CreateEnumBadgeGrid(node, prop, resolvedLabel);
                        break;
                    case DisplayType.Rating:
                        fieldElement = CreateRating(node, prop, resolvedLabel);
                        break;
                    case DisplayType.TagChips:
                        fieldElement = CreateTagChips(node, prop, resolvedLabel);
                        break;
                    case DisplayType.XYPad:
                        fieldElement = CreateXYPad(node, prop, resolvedLabel);
                        break;
                    case DisplayType.EulerAngles:
                        fieldElement = CreateEulerAngles(node, prop, resolvedLabel);
                        break;
                    case DisplayType.VectorSliders:
                        fieldElement = CreateVectorSliders(node, prop, resolvedLabel);
                        break;
#if SASQ_CUSTOM_WIDGET
                    case DisplayType.CustomWidget:
                        fieldElement = CreateCustomWidget(node, prop, resolvedLabel, resolvedPropertyPath, serializedObject, designMode, designInteract);
                        break;
#endif
                    case DisplayType.Dictionary:
                        // The Sasquatch table over Unity 6.6's native Dictionary<K, V>. Unconditional (not
                        // #if-guarded) on purpose: CreateDictionaryTable degrades to a plain PropertyField
                        // when the property is not a serialized dictionary, which is exactly the situation on
                        // every editor older than 6.6 — so a layout authored on 6.6 opens harmlessly on 6.0
                        // instead of the case vanishing and the node falling into Default.
                        fieldElement = CreateDictionaryTable(node, prop, hideLabel ? "" : resolvedLabel, designMode);
                        break;
                    case DisplayType.Default:
                    default:
                        // Plain Color field opting into HDR and/or a hidden alpha channel (no [ColorUsage] needed).
                        // Only diverges from PropertyField when the user set a non-stock option, so an untouched
                        // Color field stays a plain PropertyField (still honoring a real attribute).
                        if (prop.propertyType == SerializedPropertyType.Color &&
                            (node.ColorFieldMode == ColorFieldMode.HDR || !node.ColorShowAlpha))
                        {
                            fieldElement = CreateColorField(node, prop, resolvedLabel);
                            break;
                        }
                        // Stepper: a numeric Default field gets −/+ buttons that step at native precision.
                        if (node.ShowStepper && IsNumericProperty(prop))
                        {
                            fieldElement = CreateStepperField(node, prop, hideLabel, out alignmentTarget);
                            break;
                        }
                        // Quaternion's Default drawer is an IMGUI fallback (a px-offset container UITK styling
                        // can't reach — the label column can't align it and its text renders soft, plus it
                        // overflows narrow hosts). Always render the same euler Vector3Field the EulerAngles
                        // widget uses instead: a real UITK field the label column drives (aligned + crisp),
                        // with the axis pips dressing its component fields when they're on.
                        if (prop.propertyType == SerializedPropertyType.Quaternion)
                        {
                            var eulerField = CreateEulerAngles(node, prop,
                                hideLabel ? "" : (node.EffectiveLabelOverride ?? resolvedLabel));
                            if (node.VectorAxisPips) AxisPips.Attach(eulerField, prop);
                            fieldElement = eulerField;
                            break;
                        }

                        var propField = new PropertyField(prop);
                        if (hideLabel)
                        {
                            propField.label = "";
                        }
                        else if (node.EffectiveLabelOverride != null)
                        {
                            propField.label = node.EffectiveLabelOverride;
                        }

                        // Axis color pips: X/Y/Z tinted markers inside each component field of a
                        // vector-family property (Unity handle colors). No-op for other types.
                        if (node.VectorAxisPips)
                        {
                            AxisPips.Attach(propField, prop);
                        }

                        if (node.CreateAssetEnabled && AssetCreateUtility.IsEligible(prop, out var createFt))
                        {
                            // Row wrapper {field + Create button}. Alignment must ride on the INNER
                            // PropertyField (see alignmentTarget below) — the label-column USS behavior
                            // doesn't reach through a plain wrapper.
                            var createRow = new VisualElement();
                            createRow.style.flexDirection = FlexDirection.Row;
                            createRow.style.alignItems = Align.Center;
                            // flex-basis:auto would measure the field's content (a long asset name could push
                            // the Create button past the inspector edge) — width comes from the row instead.
                            propField.style.flexGrow = 1;
                            propField.style.flexShrink = 1;
                            propField.style.flexBasis = 0f;
                            propField.style.minWidth = 0;

                            var createBtn = AssetCreateUtility.CreateButton(prop, createFt);
                            AssetCreateUtility.AttachVisibility(createBtn, prop);
                            createBtn.style.marginLeft = 4;

                            createRow.Add(propField);
                            createRow.Add(createBtn);
                            alignmentTarget = propField;
                            fieldElement = createRow;
                        }
                        else
                        {
                            fieldElement = propField;
                        }
                        break;
                }
            }

            // A PropertyField builds its REAL field during BIND — i.e. one binding tick AFTER the first layout
            // pass — so for exactly one frame the row measures ZERO and every widget below it sits a row too
            // high, then snaps down. That one-frame jolt is the "pop" on any canvas rebuild (the designer's
            // Interact / Design-Mode toggles, card selection, a live inspector re-panel). Reserve the row's
            // height up front so the rest of the layout is already in its final place on frame 0.
            ReserveDeferredFieldHeight(fieldElement as PropertyField, prop);
            ReserveDeferredFieldHeight(alignmentTarget as PropertyField, prop);

            if (fieldElement != null && node.IsReadOnly)
            {
                fieldElement.SetEnabled(false);
                // SetEnabled(false) dims the field but does NOT remove Unity's numeric label drag-scrubber
                // (FieldMouseDragger on IntegerField/FloatField.labelElement), so a read-only number could
                // still be changed by dragging its label. Swallow pointer-down in the capture phase for
                // numeric fields — a read-only number has nothing to interact with, so this is safe and
                // bulletproof (no lazy-build timing gap like a pickingMode sweep would have).
                if (IsNumericProperty(prop))
                {
                    fieldElement.RegisterCallback<PointerDownEvent>(e => e.StopPropagation(), TrickleDown.TrickleDown);
                }
                // Stock Unity leaves the slide-scrubber (<>) cursor on a numeric field's label even when the
                // field is disabled — the label keeps its "--with-dragger" class, which the theme keys the cursor
                // off. On a read-only field that wrongly implies the value is editable, so drop the class and let
                // the cursor revert to a plain arrow (a no-op on fields with no numeric dragger).
                RemoveDraggerCursor(fieldElement);
            }

            // The dim SetEnabled(false) buys is subtree opacity, and UI Toolkit composites that PER
            // ELEMENT — so a slider knob goes see-through and the track reads straight through it.
            // Rebuild the same dim in colour instead, leaving the knobs opaque.
            //
            // Armed for EVERY field, not just read-only ones: a Toggle Group's bound bool and a condition's
            // Enable/Disable outcome disable the field from ABOVE, and either can flip long after build.
            // The pass costs one bool read on an enabled field and no-ops on a field with no slider in it,
            // which is also what keeps a [Range] Default field (Unity builds its slider lazily) covered
            // without a display-type list to keep in sync.
            if (fieldElement != null)
            {
                KeepSliderKnobsSolidWhileDisabled(fieldElement, node);
            }

            // Prefix / suffix affixes. ONE hook for every display type — including Default and every
            // auto-routed guard above the switch — so there is no per-widget list to keep in sync. Runs before
            // the label-column hookup because an outside affix mounts INSIDE the field's input container (or as
            // a sibling of its text box), never as a new row child, so the column engine sees the same shape
            // either way. No-ops instantly when the node carries no affixes.
            if (fieldElement != null)
            {
                // isTableCell, NOT forceHideLabel: only a real table cell has a column header that can
                // carry the affix instead. See the param doc on this method.
                FieldAffixes.Apply(alignmentTarget ?? fieldElement, node, prop, isTableCell);
            }

            // A CELL (forceHideLabel) has no label of its own -- the column header carries it -- so it never
            // joins the inspector's label column. But a cell whose value is a COMPOUND (a [Serializable]
            // struct, a nested List) renders as a foldout full of STOCK rows, and those rows carry Unity's
            // own `unity-base-field__aligned` class. Left alone, Unity's aligned writer measures them against
            // the InspectorElement and subtracts how far right the cell starts: on a two-column dictionary
            // table that is ~300px, which drives the label width negative and clamps it to ZERO. The label is
            // still there, 3px wide, and paints as a single truncation dot beside a full-width field -- while
            // the designer canvas, which has no InspectorElement to measure against, skips the writer entirely
            // and draws the labels correctly. Same layout, two surfaces, opposite results: the exact WYSIWYG
            // break the in-track label scope exists to prevent.
            //
            // So a compound cell gets the column engine too. It adopts nothing of its own (a hidden-label
            // field drops its label element, so DirectLabel returns null), strips the stock aligned class off
            // the nested rows, and fits ONE gutter to them inside the cell's `sasq-label-scope` track -- the
            // cell containers tag themselves, in lockstep, at the three sites that build one.
            //
            // Gated on the property SHAPE, not on being a cell: a scalar cell has no nested rows to rescue,
            // and attaching there would buy every cell in every table a standing geometry callback and a
            // subtree walk per sweep for nothing.
            bool cellHasNestedRows = forceHideLabel == true && prop != null
                                     && prop.propertyType == SerializedPropertyType.Generic
                                     && prop.hasVisibleChildren;
            if (fieldElement != null && node.UseStandardAlignment && cellHasNestedRows)
            {
                FieldLabelColumn.AttachToRow(alignmentTarget ?? fieldElement, node);
            }
            else if (fieldElement != null && node.UseStandardAlignment && forceHideLabel != true)
            {
                var alignTarget = alignmentTarget ?? fieldElement;
                // Label-above stacks the label on its own row (its own USS class); otherwise the field joins the
                // inspector's aligned left label column. The two are mutually exclusive.
                //
                // Gated on the display type, because "mutually exclusive" cuts both ways: a widget the styling
                // cannot reach (a card, a graph well — see DisplayTypeCatalog.SupportsLabelAbove) would leave
                // the column WITHOUT gaining a stacked label, and just sit misaligned against its neighbours.
                // The option is hidden for those kinds, but a layout authored before it was hidden still
                // carries the flag, so the renderer has to be the one that makes it inert.
                // FIELD TYPE, not the kind alone. The SAME titled table is reached by a second route -- a
                // SerializedDictionary field keeps DisplayType.Default and is auto-routed at the top of this
                // method -- so keying on the kind meant Label Above was offered AND honoured there, stacking a
                // second copy of the field's name over the table's own header. That is precisely the
                // duplication LabelAboveInert exists to prevent. Resolved lazily, inside the LabelAbove gate,
                // so an inspector full of ordinary fields pays no reflection for it.
                if (node.LabelAbove && !hideLabel
                    && DisplayTypeCatalog.SupportsLabelAbove(node.DisplayType, ResolveBoundFieldTypeForCatalog(serializedObject, resolvedPropertyPath)))
                {
                    alignTarget.AddToClassList("sasq-label-above");
                }
                else
                {
                    // One column engine for every row — custom wrappers AND stock-built controls.
                    // Deliberately no stock aligned class here: the engine strips it from anything
                    // that gains it (PropertyField internals) and drives all labels itself.
                    FieldLabelColumn.AttachToRow(alignTarget, node);
                }
            }

            ApplyDisplayStyleClassesAndInlineStyles(fieldElement, node, prop);

            // Tooltip resolution: blanked → cleared; override → wins; unset → fall back to any [Tooltip] in C#.
            // Claimed for the whole widget at one stable anchor (see TooltipAnchor) — the resolved value has to
            // beat the tooltip Unity stamps on the bound control itself, and a claim does that at hover time
            // instead of racing PropertyField's lazily built inner field.
            if (fieldElement != null)
            {
                if (node.IsTooltipHidden)
                {
                    // Explicitly blanked → claim the EMPTY string rather than skipping. The control surfaces
                    // the C# [Tooltip] on its own, so "don't apply ours" would not suppress it; an empty claim
                    // does, and keeps suppressing it however late the control is built.
                    TooltipAnchor.ClaimField(fieldElement, string.Empty);
                }
                else
                {
                    // A designer override wins; otherwise fall back to any [Tooltip] declared in C#.
                    string effectiveTooltip = node.Tooltip;
                    if (string.IsNullOrEmpty(effectiveTooltip))
                    {
                        var targetObj = serializedObject.targetObject;
                        if (targetObj != null)
                        {
                            var fieldInfo = GetFieldInfoFromPropertyPath(targetObj.GetType(), resolvedPropertyPath, out _);
                            if (fieldInfo != null &&
                                Attribute.GetCustomAttribute(fieldInfo, typeof(TooltipAttribute)) is TooltipAttribute ta)
                            {
                                effectiveTooltip = ta.tooltip;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(effectiveTooltip))
                    {
                        TooltipAnchor.ClaimField(fieldElement, effectiveTooltip);
                    }
                }
            }

            // A code [Space] is ALWAYS represented by a first-class Space widget in our system, never by Unity's
            // native SpaceDrawer decorator — so unconditionally hide the decorator inside a Default widget's
            // PropertyField. Otherwise the native gap draws in addition to our widget (doubling), or — on a
            // field whose [Space] hasn't been imported yet — draws as an untouchable gap the user can't manage.
            // The suppression is a cheap no-op on fields without a [Space] (the "unity-space-drawer" query finds
            // nothing). Owned Space widgets are the single, editable source of the gap.
            if (fieldElement != null)
            {
                SuppressSpaceDecorator(fieldElement);
            }

            // Header OVERRIDE: when the designer sets an explicit header, render it as a bold label above the
            // field (for every widget kind) and suppress the field's native PropertyField decorator header so
            // it isn't shown twice. A field with only a code [Header] and no override keeps its existing
            // rendering (Default widgets show it natively via PropertyField; the import maps [Header] to a
            // section) — the designer surfaces that code value as the Header field's greyed default. Rendering
            // the code header here too would double up with those, so we deliberately render on override only.
            // An explicitly blanked header (HiddenSentinel) suppresses the field's native [Header] decorator
            // without rendering one of ours; an unset header keeps the native rendering; a custom value wins.
            //
            // If the widget is not Default, Unity's PropertyField native header decorators do not run. Therefore,
            // we resolve the C# [Header] attribute and wrap the element dynamically when the header is unset.
            VisualElement result = fieldElement;
            if (fieldElement != null && node.IsHeaderHidden)
            {
                SuppressDecoratorHeader(fieldElement);
            }
            else if (fieldElement != null && node.EffectiveHeader != null)
            {
                result = WrapWithHeader(fieldElement, node.EffectiveHeader);
            }
            // Skipped for cells and collection elements (forceHideLabel), same as the help chip below: Unity
            // resolves an element path's FieldInfo to the ARRAY FIELD, so a [Header] written once above a list
            // would otherwise come back attached to every element in it.
            else if (fieldElement != null && node.DisplayType != DisplayType.Default
                     && string.IsNullOrEmpty(node.Header) && forceHideLabel != true)
            {
                var codeHeaderAttr = GetHeaderAttribute(prop);
                if (codeHeaderAttr != null && !string.IsNullOrEmpty(codeHeaderAttr.header))
                {
                    result = WrapWithHeader(fieldElement, codeHeaderAttr.header);
                }
            }

            // Per-field help chip: a "?" beside the field that reveals a markdown help card below it. Opt-in via
            // node.ShowHelpChip (+ non-empty help text); skipped for table cells (forceHideLabel) where there's no room.
            if (result != null && node.ShowHelpChip && !string.IsNullOrEmpty(node.HelpText) && forceHideLabel != true)
            {
                result = WrapWithHelpChip(result, node);
            }

            // The property path, so the right-click menu can find it on a widget Unity never bound. Both the
            // inner field and the outer wrapper get it — a click can land on either, and the menu walks up.
            StampPropertyPath(fieldElement ?? result, prop);
            StampPropertyPath(result, prop);

            // The prefab-override bar, for the widgets Unity cannot mark itself. Last, so it lands on whatever
            // the wrappers above ended up as — the element the user actually sees as "the field".
            ArmPrefabOverrideBar(fieldElement ?? result, prop);

            return result;
        }

        // ===== The property path a widget renders, carried on the element =====
        // The right-click menu's whole property block — Copy Property Path / Copy Value / Paste Value,
        // Apply-to-Prefab / Revert-to-Prefab, and the field's own [ContextMenuItem] entries — hangs off
        // QuickMenuNativeEssentials.ResolveBindingPath finding a path for the clicked pixel. That used to look
        // ONLY for an `IBindable.bindingPath`, which a hand-drawn widget never has: the Enum Badge Grid,
        // Progress Bar, Rating, Gradient, Tag Chips and friends do not call BindProperty — that is the same
        // premise ArmPrefabOverrideBar below is built on. So the path came back null and the entire block was
        // skipped, silently, on exactly those widgets.
        //
        // Stamping it here is exact where a re-derivation would not be: `prop` is the real property this widget
        // was built for, already resolved through collection rows and nested paths. It is also the one place
        // that runs for EVERY widget kind on BOTH render paths.

        // A WEAK-KEYED SIDE TABLE, not `userData` and not `SetProperty`:
        //   * `VisualElement.SetProperty/GetProperty` are INTERNAL in 6000.0 — using them would be a §2.5.g
        //     violation in shipped code, and they are Unity's to move on a version hop.
        //   * `userData` is already carrying a different payload type at ~40 sites (LayoutNode on canvas
        //     elements, choice lists on dropdown inputs, ContainerNode on frames…). Writing a string into it
        //     on a field element would clobber whichever of those got there first, silently.
        // ConditionalWeakTable holds its keys weakly, so an entry dies with the element it describes and a
        // rebuilt inspector cannot leak them.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, StampedPath>
            PropertyPathByElement = new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, StampedPath>();

        /// <summary>
        /// A path plus the object it belongs to. The OWNER is not optional bookkeeping: the Inline Editor
        /// builds its nested fields against a <b>different</b> SerializedObject (a `new SerializedObject(subObj)`),
        /// so a path stamped in there — "damage", say — can also name a real field on the OUTER object. A
        /// consumer that resolved it against the outer object would then act on the wrong target, and one of
        /// those actions is Apply to Prefab, which writes to an asset. Recorded as an instance ID rather than a
        /// reference so a stale entry can never resurrect a destroyed object. Via <see cref="UnityObjectId"/>,
        /// never a bare GetInstanceID() — that is CS0619 error-obsolete on 6000.5 and would stop the whole
        /// package compiling there (TestNoDirectInstanceIdCalls guards exactly this). Per-session only, which
        /// is all a weak-table entry outliving nothing needs.
        /// </summary>
        private sealed class StampedPath
        {
            public string Path;
            public int OwnerInstanceId;
        }

        /// <summary>Records <paramref name="prop"/>'s absolute path against <paramref name="element"/>. Null-safe and idempotent.</summary>
        private static void StampPropertyPath(VisualElement element, SerializedProperty prop)
        {
            if (element == null || prop == null) return;
            // A copy of the STRING, not the property: the SerializedProperty handed to a widget factory is an
            // iterator others may advance, so anything read later would be the wrong field.
            string path = prop.propertyPath;
            if (string.IsNullOrEmpty(path)) return;

            var owner = prop.serializedObject != null ? prop.serializedObject.targetObject : null;
            if (owner == null) return;   // nothing to key the ownership check on — better no stamp than a blind one

            PropertyPathByElement.Remove(element);
            PropertyPathByElement.Add(element, new StampedPath
            {
                Path = path,
                OwnerInstanceId = UnityObjectId.Of(owner),
            });
        }

        /// <summary>
        /// The path the builder recorded for <paramref name="element"/>, or null — including when the stamp
        /// belongs to a DIFFERENT object than <paramref name="expectedOwner"/> (see <see cref="StampedPath"/>).
        /// Callers must treat a mismatch as "no path here", never walk past it looking for a friendlier one:
        /// the next stamp up the tree describes the field that CONTAINS the inline editor, which is not what
        /// the user right-clicked.
        /// </summary>
        internal static string ReadStampedPropertyPath(VisualElement element, UnityEngine.Object expectedOwner,
            out bool ownerMismatch)
        {
            ownerMismatch = false;
            if (element == null) return null;
            if (!PropertyPathByElement.TryGetValue(element, out var stamp) || stamp == null) return null;
            if (expectedOwner != null && stamp.OwnerInstanceId != UnityObjectId.Of(expectedOwner))
            {
                ownerMismatch = true;
                return null;
            }
            return stamp.Path;
        }

        // ===== The prefab-override bar on a CUSTOM-DRAWN widget =====
        // Unity paints the bold label + blue left bar from inside its own two-way binding: the binding sets
        // `unity-binding--prefab-override` on the field it drives whenever that property differs from the prefab.
        // A widget that does NOT go through BindProperty — the Enum Badge Grid, the Progress Bar, the Rating pips,
        // the Gradient/Curve wells, the Tag Chips — is therefore invisible to it, and a genuinely overridden value
        // shows NOTHING. Measured on a DemoPlayerStats prefab instance with 5 real overrides: only the 2 natively
        // bound fields carried a bar; affinity, health and masteryStars showed no sign of being overridden at all.
        //
        // Rather than fake a bar everywhere, this DEFERS to Unity wherever Unity is already doing the job, and the
        // "is Unity doing it?" test is STRUCTURAL: does this field contain a control with a bindingPath, i.e. one
        // Unity's binding drives? Deliberately not "has Unity's class appeared yet" — that class lands on a LATER
        // binding tick, so any check at build time (or on the first geometry pass) concludes "no" and doubles the
        // bar on every natively bound row. Measured: it put both classes on the same Slider.
        //
        // The mark is our OWN class with our OWN rule in SasquatchWidgets.uss — never a borrowed Unity USS name
        // that is theirs to rename on a version hop.

        /// <summary>Our marker; carries the bar (see SasquatchWidgets.uss).</summary>
        internal const string PrefabOverrideClass = "sasq-prefab-override";
        private const string PrefabOverrideArmedClass = "sasq-prefab-override-armed";

        /// <summary>
        /// Keeps <paramref name="fieldElement"/>'s prefab-override bar in sync with <paramref name="prop"/>.
        /// No-op — and nearly free — for anything that is not part of a prefab instance, and for anything Unity's
        /// own binding already marks.
        /// </summary>
        private static void ArmPrefabOverrideBar(VisualElement fieldElement, SerializedProperty prop)
        {
            if (fieldElement == null || prop == null) return;
            var target = prop.serializedObject != null ? prop.serializedObject.targetObject : null;
            if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target)) return;
            if (fieldElement.ClassListContains(PrefabOverrideArmedClass)) return;
            if (HasNativelyBoundControl(fieldElement)) return; // Unity owns this field's bar
            fieldElement.AddToClassList(PrefabOverrideArmedClass);

            // A stable copy: the SerializedProperty handed to a widget factory is an iterator others may advance.
            string path = prop.propertyPath;
            var so = prop.serializedObject;

            Action sync = () =>
            {
                var live = so.FindProperty(path);
                // The property can go missing under a rebuilt/rebound tree (a removed component, a shrunk array).
                fieldElement.EnableInClassList(PrefabOverrideClass, live != null && live.prefabOverride);
            };

            // Tracked on the whole SerializedObject, NOT on the property. The override FLAG can flip while the
            // VALUE stands still — "Apply to Prefab" is exactly that: the number stays put and the override
            // simply stops existing — so a per-property tracker would sit there showing a bar for an override
            // that no longer exists. TrackSerializedObjectValue keys off the object's revision, which both Apply
            // and Revert bump. Geometry is the second net, for a relayout that arrives without either. Both run
            // one FindProperty and a class toggle, and only ever on a prefab instance's custom-drawn fields.
            fieldElement.TrackSerializedObjectValueWithRefresh(so, _ => sync());
            fieldElement.RegisterCallback<GeometryChangedEvent>(_ => sync());
            sync();
        }

        /// <summary>
        /// True when this field contains a control Unity's binding drives (anything with a <c>bindingPath</c>) —
        /// in which case Unity paints the override bar itself and we must stay out of the way. Evaluated at BUILD
        /// time, which is safe precisely because every <c>BindProperty</c> in the widget factory is synchronous.
        /// </summary>
        private static bool HasNativelyBoundControl(VisualElement element)
        {
            var bindable = element as IBindable;
            if (bindable != null && !string.IsNullOrEmpty(bindable.bindingPath)) return true;
            for (int i = 0; i < element.childCount; i++)
                if (HasNativelyBoundControl(element[i])) return true;
            return false;
        }

        // Wraps a field with a per-field "?" help chip and a collapsible markdown help card below it (shared visual
        // with the Info/Warning chip mode). The chip is inserted INTO the field's own base-field (after the
        // label for Left, after the input for Right) so the field's input keeps shrinking to fit — a plain sibling
        // row would instead force a multi-component field (Vector3, …) to its natural width and blow the inspector
        // into horizontal scroll. HelpChipAlignment defaults to Right.
        private static VisualElement WrapWithHelpChip(VisualElement fieldElement, BindingNode node)
        {
            var parts = SasquatchHelpChip.Build(node.HelpText);
            parts.Chip.style.flexShrink = 0;
            parts.Chip.style.marginLeft = 4;
            parts.Chip.style.marginRight = 2;
            // Apply the per-field help text styling to the card's label (coexists with the markdown tags).
            if (parts.Card.childCount > 0 && parts.Card[0] is Label cardLabel)
                TextStyleApplier.Apply(cardLabel, node.HelpTextStyle);

            bool right = node.HelpChipAlignment != ImageAlignment.Left; // default (Right) or Center → right edge

            var column = new VisualElement();
            column.style.flexDirection = FlexDirection.Column;
            column.Add(fieldElement);
            column.Add(parts.Card);

            bool placed = false;
            void Place()
            {
                if (placed) return;
                var baseField = fieldElement.ClassListContains("unity-base-field")
                    ? fieldElement
                    : fieldElement.Q(className: "unity-base-field");
                if (baseField == null) return;
                var label = baseField.Q(className: "unity-base-field__label");
                if (right)
                {
                    baseField.Add(parts.Chip); // far right, after the input (which stays flexible)
                    placed = true;
                }
                else if (label != null && label.parent == baseField)
                {
                    baseField.Insert(baseField.IndexOf(label) + 1, parts.Chip); // just after the label
                    placed = true;
                }
            }
            Place(); // custom widgets build their base-field synchronously; PropertyField builds lazily on bind
            fieldElement.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (placed) return;
                Place();
                if (!placed)
                {
                    // No base-field (e.g. a bare element): float the chip at the top-right instead of pushing it
                    // into the flow (which would reflow/overflow).
                    parts.Chip.style.position = Position.Absolute;
                    parts.Chip.style.right = 2;
                    parts.Chip.style.top = 2;
                    parts.Chip.style.marginLeft = 0;
                    column.style.position = Position.Relative;
                    column.Add(parts.Chip);
                    placed = true;
                }
            });
            return column;
        }

        // Wraps a field with a bold section header above it (mirrors a C# [Header]). Suppresses the field's
        // own native PropertyField decorator header (built lazily on bind) so it isn't rendered twice for a
        // Default widget; a no-op for custom widgets, which have no decorator container.
        private static VisualElement WrapWithHeader(VisualElement fieldElement, string headerText)
        {
            SuppressDecoratorHeader(fieldElement);

            var wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Column;

            var headerLabel = new Label(headerText);
            headerLabel.AddToClassList("sasq-field-header");
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLabel.style.color = WidgetPalette.LabelSecondary;
            headerLabel.style.marginTop = 6;
            headerLabel.style.marginBottom = 2;
            wrapper.Add(headerLabel);
            wrapper.Add(fieldElement);
            return wrapper;
        }

        // The height a stock UITK field row occupies. Measured against the real thing rather than assumed:
        // singleLineHeight is the field, +1 covers the base-field row's own vertical margins.
        private static float StockFieldRowHeight => EditorGUIUtility.singleLineHeight + 1f;

        /// <summary>
        /// How tall the bound property's stock field will be once <see cref="PropertyField"/> has built it.
        /// Only the row COUNT is predicted — UI Toolkit lays the compound value types out as fixed multi-row
        /// fields, everything else is one row.
        /// </summary>
        private static float StockFieldReservedHeight(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.RectInt:
                    return StockFieldRowHeight * 2f;
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.BoundsInt:
                    return StockFieldRowHeight * 3f;
                default:
                    return StockFieldRowHeight;
            }
        }

        /// <summary>
        /// Holds a not-yet-bound <see cref="PropertyField"/> open at its expected row height so the first
        /// painted frame lays out at the FINAL position. Without it the field measures 0 until its inner
        /// control arrives on the next binding tick, and everything below it visibly jumps down a row.
        /// The floor is released the moment the real control lands, so it can never fight the field's own
        /// height — and a bounded tick budget means a property that renders nothing (a decorator-only or
        /// fully-hidden drawer) can't hold the reservation forever.
        /// </summary>
        private static void ReserveDeferredFieldHeight(PropertyField field, SerializedProperty prop)
        {
            // Already populated (a re-used field) — nothing is deferred, so nothing to reserve.
            if (field == null || prop == null || field.childCount > 0) return;

            field.style.minHeight = StockFieldReservedHeight(prop);

            int ticks = 0;
            IVisualElementScheduledItem release = null;
            release = field.schedule.Execute(() =>
            {
                // A GeometryChangedEvent is NOT usable as the release trigger: the reservation makes the
                // field's own box the right size ALREADY, so the arriving child changes nothing to report.
                if (field.childCount == 0 && ++ticks < 30) return;
                field.style.minHeight = StyleKeyword.Null;
                release.Pause();
                AnnounceDeferredFieldArrival(field);
            }).Every(0);
        }

        /// <summary>
        /// Tells the row it has finished filling in. THE RESERVATION SWALLOWS A SIGNAL, and this puts it back:
        /// a deferred field used to grow 0 → one row when its control landed, and that resize fired a real
        /// <see cref="GeometryChangedEvent"/> which several systems quietly depend on — the label-column engine
        /// re-fits the gutter on it, and it is what let a bind-built inner label be measured at all. Hold the
        /// box at the right size from frame 0 (which is the whole point) and that event can never fire, because
        /// nothing ever resizes. So re-announce it, honestly shaped: the CONTENT did grow from nothing.
        ///
        /// Without this, a Default field's label silently keeps whatever width the first (label-less) fit pass
        /// computed — measured as a track gutter stuck at 120px instead of fitting its own 89px labels.
        /// </summary>
        private static void AnnounceDeferredFieldArrival(VisualElement field)
        {
            var settled = field.layout;
            if (float.IsNaN(settled.width)) return;
            var wasEmpty = new Rect(settled.x, settled.y, settled.width, 0f);
            using (var evt = GeometryChangedEvent.GetPooled(wasEmpty, settled))
            {
                evt.target = field;
                field.SendEvent(evt);
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // WHY THE THREE SUPPRESSORS BELOW ARE NOT ONE-SHOT (2026-08-13 perf pass).
        //
        // Each looks like a candidate for unregistering after its first successful application — the work
        // reads as irreversible (a display:None cannot un-hide itself, a stripped class cannot come back).
        // It is not: a PropertyField CLEARS AND REBUILDS its children when its serialized binding executes,
        // which is measured elsewhere in this codebase (see ConditionBinder's overlay self-heal, which exists
        // for exactly that clear and notes that even a one-shot re-apply can lose the race with it). A
        // rebuilt subtree brings a fresh decorator / a fresh dragger label with default styling, so a latched
        // handler would leave a [Space] decorator visible or a drag cursor on a read-only field, permanently.
        //
        // The cost was also measured before assuming: stripping the whole SuppressSpaceDecorator family
        // (82 live handlers) from the callback registry during the 2026-08-11 hot-path audit saved 4µs of a
        // 1,608µs Sasquatch layout pass. Not worth a class of visual regression.
        // ---------------------------------------------------------------------------------------------------

        // Unity's numeric drag-scrubber cursor lives on this label class; removing it reverts the label to a
        // plain arrow. The class also survives SetEnabled(false), so a read-only numeric field keeps the cursor
        // unless we strip it here.
        private const string DraggerLabelClass = "unity-base-field__label--with-dragger";

        // Removes the numeric drag-scrubber slide cursor from a field's label(s) (built lazily inside a
        // PropertyField, hence deferred). A no-op on non-numeric fields, whose labels never carry the class.
        private static void RemoveDraggerCursor(VisualElement fieldElement)
        {
            fieldElement.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                fieldElement.Query<Label>(className: DraggerLabelClass)
                            .ForEach(l => l.RemoveFromClassList(DraggerLabelClass));
            });
        }

        // Hides a field's native [Space] decorator element(s) (built lazily on bind). Scoped to Unity's own
        // "unity-space-drawer" class so a [Header] label or custom decorator sharing the container keeps
        // rendering. Shared with the collection Default path, which builds its PropertyField in LayoutTreeBuilder.
        internal static void SuppressSpaceDecorator(VisualElement fieldElement)
        {
            fieldElement.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                fieldElement.Query<VisualElement>(className: "unity-space-drawer")
                            .ForEach(d => d.style.display = DisplayStyle.None);
            });
        }

        // Hides a field's native PropertyField decorator header(s) (built lazily on bind) — used both when we
        // render our own header above the field and when the designer has explicitly blanked the header.
        private static void SuppressDecoratorHeader(VisualElement fieldElement)
        {
            fieldElement.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                fieldElement.Query<VisualElement>(className: "unity-decorator-drawers-container")
                            .ForEach(d => d.style.display = DisplayStyle.None);
            });
        }

        private static RangeAttribute GetRangeAttribute(SerializedProperty prop)
        {
            // [Range] only makes sense on a single float/int — Unity's own drawer errors ("Use Range with
            // float or int") on a vector. So we deliberately ignore it for vector properties: their per-axis
            // min/max comes from the designer's Range Min/Max instead. That keeps the bounds in the layout
            // rather than the user's code, so reverting to the default inspector never leaves a broken
            // [Range]-on-a-vector field behind.
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.Vector4:
                    return null;
            }

            var targetObj = prop.serializedObject.targetObject;
            if (targetObj == null) return null;

            var fieldInfo = GetFieldInfoFromPropertyPath(targetObj.GetType(), prop.propertyPath, out _);
            if (fieldInfo != null)
            {
                return Attribute.GetCustomAttribute(fieldInfo, typeof(RangeAttribute)) as RangeAttribute;
            }
            return null;
        }

        /// <summary>
        /// The field's <c>[Min]</c> attribute, if any. Unity's own MinDrawer coerces the DRAWN value to it, so any
        /// widget that writes the property behind the drawer's back has to honour it too or the stored data and
        /// what the inspector shows silently disagree.
        /// </summary>
        private static MinAttribute GetMinAttribute(SerializedProperty prop)
        {
            var targetObj = prop?.serializedObject != null ? prop.serializedObject.targetObject : null;
            if (targetObj == null) return null;

            var fieldInfo = GetFieldInfoFromPropertyPath(targetObj.GetType(), prop.propertyPath, out _);
            return fieldInfo != null
                ? Attribute.GetCustomAttribute(fieldInfo, typeof(MinAttribute)) as MinAttribute
                : null;
        }

        private static void ResolveLimits(BindingNode node, SerializedProperty prop, out float min, out float max, float fallbackMin = 0f, float fallbackMax = 100f)
        {
            float defaultMin = fallbackMin;
            float defaultMax = fallbackMax;
            var range = GetRangeAttribute(prop);
            if (range != null)
            {
                defaultMin = range.min;
                defaultMax = range.max;
            }

            min = node.RangeMin.Resolve(defaultMin);
            max = node.RangeMax.Resolve(defaultMax);
        }

        private static HeaderAttribute GetHeaderAttribute(SerializedProperty prop)
        {
            var targetObj = prop.serializedObject.targetObject;
            if (targetObj == null) return null;

            var fieldInfo = GetFieldInfoFromPropertyPath(targetObj.GetType(), prop.propertyPath, out _);
            if (fieldInfo != null)
            {
                return Attribute.GetCustomAttribute(fieldInfo, typeof(HeaderAttribute)) as HeaderAttribute;
            }
            return null;
        }

        /// <summary>
        /// The declared type behind a resolved property path, or null when it cannot be resolved — the shape
        /// <see cref="DisplayTypeCatalog"/>'s field-type-aware predicates take. Prefers the RESOLVED element
        /// type over the FieldInfo's own, for the same reason
        /// <see cref="IsSerializedDictionaryProperty"/> does: for an element path like
        /// <c>tables.Array.data[0]</c> the FieldInfo is the LIST field, whose FieldType is
        /// <c>List&lt;SerializedDictionary&lt;K,V&gt;&gt;</c> and answers every type question wrongly.
        /// <para>Null-safe on every input, and deliberately cheap to CALL but not free to RUN — gate it
        /// behind whatever flag actually needs the answer rather than resolving on every field.</para>
        /// </summary>
        internal static Type ResolveBoundFieldTypeForCatalog(SerializedObject serializedObject, string resolvedPropertyPath)
        {
            if (serializedObject == null || string.IsNullOrEmpty(resolvedPropertyPath)) return null;
            UnityEngine.Object target;
            try { target = serializedObject.targetObject; }
            catch { return null; } // a dead SerializedObject is non-null but throws on .targetObject
            if (target == null) return null;
            var field = GetFieldInfoFromPropertyPath(target.GetType(), resolvedPropertyPath, out var resolvedType);
            return resolvedType ?? field?.FieldType;
        }

        /// <summary>
        /// Resolves the reflected <see cref="System.Reflection.FieldInfo"/> for a serialized <paramref name="propertyPath"/>,
        /// walking nested types and collection elements; also outputs the resolved <paramref name="fieldType"/>.
        /// </summary>
        public static System.Reflection.FieldInfo GetFieldInfoFromPropertyPath(Type type, string propertyPath, out Type fieldType)
        {
            fieldType = type;
            System.Reflection.FieldInfo fieldInfo = null;
            string[] parts = propertyPath.Split('.');

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];

                // If this is "Array", the next one is "data[X]"
                if (part == "Array")
                {
                    // The next part is data[X]
                    i++; // skip data[X]
                    if (fieldType.IsArray)
                    {
                        fieldType = fieldType.GetElementType();
                    }
                    else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>))
                    {
                        fieldType = fieldType.GetGenericArguments()[0];
                    }
                    continue;
                }

                // Get field
                fieldInfo = GetFieldIncludingBaseTypes(fieldType, part);
                if (fieldInfo == null)
                {
                    return null;
                }
                fieldType = fieldInfo.FieldType;
            }

            return fieldInfo;
        }

        private static System.Reflection.FieldInfo GetFieldIncludingBaseTypes(Type type, string fieldName)
        {
            var t = type;
            while (t != null)
            {
                var fieldInfo = t.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (fieldInfo != null)
                {
                    return fieldInfo;
                }
                t = t.BaseType;
            }
            return null;
        }

        // Marks unit-suffix labels so the generic per-node text restyling sweeps (TextStyleApplier /
        // MinMaxSlider's StyleLabels) can skip them — the suffix must stay dim regardless of FieldTextStyle.
        internal const string UnitSuffixClass = "sasq-unit-suffix";

        // Dim grey for unit-suffix labels; explicit (not inherited) so a node's FieldTextStyle cascade
        // can't recolor the suffix into looking like part of the value.
        //
        // A PROPERTY, not a readonly field, and routed through WidgetPalette so it follows the editor skin.
        // It was a pinned #999 for both skins — the one grey that never flipped while every other muted
        // string in the package did, leaving every unit the wrong colour on the light skin. LabelDim's DARK
        // value is byte-identical to that old constant (0.60), so dark renders pixel-for-pixel as before and
        // no golden/render baseline moves; only light changes, which is the bug. It must stay a property:
        // the value is written INLINE onto the label at build time, so a field would bake one skin's colour
        // into the domain and a skin flip + rebuild would reproduce it.
        internal static Color UnitSuffixColor => WidgetPalette.LabelDim;

        /// <summary>
        /// Builds the dim unit label ("%", " m/s") the numeric widgets append after their value display.
        /// Returns null for a null/empty suffix so callers can skip mounting entirely.
        /// </summary>
        internal static Label CreateUnitSuffixLabel(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return null;
            var label = new Label(suffix)
            {
                pickingMode = PickingMode.Ignore
            };
            label.AddToClassList(UnitSuffixClass);
            label.style.flexShrink = 0;
            label.style.marginLeft = 4;
            label.style.fontSize = 10;
            label.style.color = UnitSuffixColor;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            return label;
        }

        private static void ApplyDisplayStyleClassesAndInlineStyles(VisualElement element, BindingNode node, SerializedProperty prop)
        {
            if (element == null || node == null) return;

            var style = node.DisplayStyleData;

            // Size class
            if (style.size == DisplaySize.Compact)
            {
                element.AddToClassList("sq-size--compact");
            }
            else if (style.size == DisplaySize.Large)
            {
                element.AddToClassList("sq-size--large");
            }
        }

        /// <summary>
        /// True when this property should render with the custom UnityEvent card drawer — i.e. the user
        /// explicitly chose <see cref="DisplayType.UnityEvent"/> on a field deriving from <c>UnityEventBase</c>
        /// (an <c>m_PersistentCalls</c> signature check is the fallback when the type can't be resolved).
        /// <see cref="DisplayType.Default"/> deliberately falls through to Unity's standard UnityEvent drawer.
        /// </summary>
        public static bool IsUnityEventProperty(BindingNode node, SerializedProperty prop,
            string resolvedPropertyPath, SerializedObject serializedObject)
        {
            if (node.DisplayType != DisplayType.UnityEvent) return false;
            if (prop.propertyType != SerializedPropertyType.Generic) return false;

            var target = serializedObject.targetObject;
            if (target != null)
            {
                GetFieldInfoFromPropertyPath(target.GetType(), resolvedPropertyPath, out var fieldType);
                if (fieldType != null)
                {
                    if (fieldType.IsArray) fieldType = fieldType.GetElementType();
                    else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                        fieldType = fieldType.GetGenericArguments()[0];
                    return typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(fieldType);
                }
            }
            return prop.FindPropertyRelative("m_PersistentCalls") != null;
        }

        // ── Multi-object safe writes ────────────────────────────────────────────────────────────────────
        // Reading a value off a MULTI-EDIT SerializedProperty returns the FIRST object's value. So the
        // read-modify-write shape every hand-drawn composite widget reaches for —
        //     var v = Read(); v[axis] = evt.newValue; Write(v);
        // — stamps object A's OTHER components onto every selected object, silently destroying their data.
        // Unity's own vector field never does this: it writes the changed SUB-property and leaves the
        // siblings alone. These two helpers are how a Sasquatch widget does the same.

        private static readonly string[] VectorAxisNames = { "x", "y", "z", "w" };

        /// <summary>
        /// Writes ONE component of a vector-family property (<c>x</c>/<c>y</c>/<c>z</c>/<c>w</c>), leaving the
        /// other components untouched on every selected object — the multi-edit-safe replacement for a
        /// whole-vector read-modify-write. Returns false when the sub-property can't be resolved or isn't
        /// numeric, so the caller can fall back to its own whole-vector write. Does NOT Update/Apply: the
        /// caller owns the SerializedObject round trip.
        /// </summary>
        internal static bool WriteVectorAxis(SerializedProperty prop, int axis, float value)
        {
            if (prop == null || axis < 0 || axis >= VectorAxisNames.Length) return false;
            SerializedProperty comp;
            try { comp = prop.FindPropertyRelative(VectorAxisNames[axis]); }
            catch { return false; } // SerializedObject disposed mid-teardown
            if (comp == null) return false;
            if (comp.propertyType == SerializedPropertyType.Float) comp.floatValue = value;
            else if (comp.propertyType == SerializedPropertyType.Integer) comp.intValue = Mathf.RoundToInt(value);
            else return false;
            return true;
        }

        /// <summary>
        /// Runs <paramref name="mutate"/> against EVERY selected object's OWN copy of <paramref name="prop"/>.
        /// For a value that can't be expressed as a per-component write — a quaternion rebuilt from euler
        /// degrees, a vector rescaled to unit length — each object's result has to be derived from its own
        /// data, or the whole selection collapses onto the first object's value. A single-object selection
        /// takes the direct path (no extra SerializedObject). The caller's SerializedObject is re-synced
        /// afterwards so the widget's own refresh reads the new values.
        /// </summary>
        internal static void MutatePerTarget(SerializedProperty prop, Action<SerializedProperty> mutate)
        {
            if (prop == null || mutate == null) return;
            var so = prop.serializedObject;
            if (so == null) return;

            var targets = so.targetObjects;
            if (targets == null || targets.Length <= 1)
            {
                so.Update();
                mutate(prop);
                so.ApplyModifiedProperties();
                return;
            }

            string path = prop.propertyPath;
            foreach (var t in targets)
            {
                if (t == null) continue;
                var one = new SerializedObject(t);
                var p = one.FindProperty(path);
                if (p == null) continue;
                mutate(p);
                one.ApplyModifiedProperties();
            }
            // The multi-edit SerializedObject still holds its pre-write cache (including the
            // hasMultipleDifferentValues verdict the mixed chrome reads from).
            so.SetIsDifferentCacheDirty();
            so.Update();
        }
    }
}
