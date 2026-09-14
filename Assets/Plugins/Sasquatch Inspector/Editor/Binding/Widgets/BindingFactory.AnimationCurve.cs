using System;
using System.Collections.Generic;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        // AnimationCurveEditor widget: a modern, spring-driven alternative to Unity's CurveField. The bound curve
        // renders into a Painter2D graph well (SasquatchCurveControl) with draggable keyframes + tangent handles,
        // a square grid with axis numbers, a clickable preset swatch gallery, optional editable Max-X/Max-Y, and
        // an expand button to a freely-resizable editor window. Inline shows the well directly; Expandable and
        // ExpandableWithPreview wrap it in an expandable group box (the latter keeps a small preview visible).
        private static VisualElement CreateAnimationCurveEditor(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            if (prop.propertyType != SerializedPropertyType.AnimationCurve)
                return new PropertyField(prop) { label = resolvedLabel };

            bool editableInline = !node.CurveReadOnlyInline && !node.IsReadOnly;

            var control = new SasquatchCurveControl(prop, node, editableInline);

            var well = BuildCurveWell(control, node);
            MixedValueUI.AttachGraphGhost(well, control, prop); // honest multi-object mixed: ghost + em-dash

            var content = new VisualElement();
            content.Add(well);
            if (editableInline) content.Add(BuildPresetStrip(control, WidgetPalette.ResolveLegacy(
                node.CurveLineColor, WidgetPalette.CurveLineDarkDefault, WidgetPalette.CurveLineLight)));
            content.Add(BuildCurveActionRow(control, includeExpand: true,
                onExpand: () => SasquatchCurveEditorWindow.Open(prop, node), reserveSpace: false));

            VisualElement root = new VisualElement();
            var mode = node.CurveEditorMode;

            if (mode == CurveEditorMode.Expandable || mode == CurveEditorMode.ExpandableWithPreview)
            {
                VisualElement headerExtra = null;
                if (mode == CurveEditorMode.ExpandableWithPreview)
                {
                    var previewWell = new VisualElement();
                    previewWell.AddToClassList("sasq-curve-well");
                    previewWell.AddToClassList("sasq-curve-preview");
                    // No fixed width: BuildExpandableGroup flex-grows the preview from the shared label
                    // column to the header's right edge, so it tracks the inspector like every other field's
                    // input box. The floor keeps it readable in a very narrow inspector.
                    previewWell.style.minWidth = 80;
                    previewWell.style.height = 22;
                    var preview = new SasquatchCurveControl(prop, node, editable: false) { Minimal = true };
                    previewWell.Add(preview);
                    headerExtra = previewWell;
                }

                string title = string.IsNullOrEmpty(resolvedLabel) ? "Curve" : resolvedLabel;
                root.Add(BuildExpandableGroup(title, headerExtra, content,
                    stateKey: FoldoutStateStore.Key(prop.serializedObject.targetObject, prop.propertyPath, title),
                    fallbackExpanded: node.WidgetDefaultToOpen));
            }
            else // Inline
            {
                if (!string.IsNullOrEmpty(resolvedLabel))
                {
                    var lbl = new Label(resolvedLabel);
                    lbl.AddToClassList("sasq-curve-label");
                    // Tall custom widget with no unity-base-field of its own: without an anchor the
                    // universal affix decorator would park the suffix at the very BOTTOM of the widget.
                    lbl.AddToClassList(FieldAffixes.AffixAnchorClass);
                    root.Add(lbl);
                }
                root.Add(content);
            }

            root.AddToClassList("sasq-animation-curve-field");
            return root;
        }

        /// <summary>Marks a group box drawn in List row style (flush, no box, hairline-parted).</summary>
        internal const string ListRowClass = "sasq-group--list";
        /// <summary>Toggled on a List-style box while the next row down is another one, so the hairline is
        /// drawn BETWEEN rows only.</summary>
        internal const string ListRuleClass = "sasq-group--list-rule";

        // A separator's job is to part two adjacent rows, so the hairline is drawn only when the next visible
        // thing below really is another list row — a trailing rule under the last one reads as "there's more
        // below" (the same call ApplyRowSeparator makes for collection rows). The box is not itself the layout
        // sibling: each widget wraps it in a root (.sasq-animation-curve-field and friends), so walk up through
        // single-child wrappers first to find the element that actually sits in the parent's row order.
        private static void AttachListRowRule(VisualElement box)
        {
            void Sync()
            {
                var el = box;
                while (el.parent != null && el.parent.childCount == 1) el = el.parent;

                bool ruled = false;
                var parent = el.parent;
                if (parent != null)
                {
                    for (int i = parent.IndexOf(el) + 1; i < parent.childCount; i++)
                    {
                        var sib = parent.ElementAt(i);
                        if (sib.resolvedStyle.display == DisplayStyle.None) continue; // conditions can hide a row
                        ruled = sib.ClassListContains(ListRowClass)
                                || sib.Q<VisualElement>(className: ListRowClass) != null;
                        break;
                    }
                }
                box.EnableInClassList(ListRuleClass, ruled);
            }

            // One deferred pass after attach: at construction time the box has no parent yet, and a sibling
            // built after us would not exist. Geometry re-fires it when a condition shows/hides a neighbor.
            box.RegisterCallback<AttachToPanelEvent>(_ => box.schedule.Execute(Sync).StartingIn(0));
            box.RegisterCallback<GeometryChangedEvent>(_ => Sync());
        }

        // ----- Shared expandable group box -----
        // The ONE encased "foldout" box used by the Animation Curve, Gradient, and Particle (Expandable) widgets
        // so all three are visually identical. It mirrors the expandable container shell LayoutTreeBuilder
        // builds for an expandable ContainerNode EXACTLY — same .sasq-group/.sasq-group-header/
        // .sasq-group-chevron/.sasq-group-content USS classes (the single source of truth for the look; no inline
        // borders/radii), the same caret + spring-fold animation, and the same header-bottom-corner radius
        // animation (square while open so header+body read as one unit, rounded while collapsed).
        //
        // headerMainOverride: when supplied (the particle's bound ObjectField) it takes the header's main slot
        // instead of a title Label, and its own pointer/click events are stopped so it stays editable while the
        // rest of the header bar + the caret still toggle the fold.
        // stateKey/fallbackExpanded: open-state is remembered durably via FoldoutStateStore (mimics Unity's
        // native foldout memory); fallbackExpanded is the author-time default used until the user toggles it.
        // onExpandedChanged(expanded, userInitiated): invoked once with the seeded state (userInitiated=false)
        // and again on every user toggle (userInitiated=true) — lets the particle widget drive render/play.
        internal static VisualElement BuildExpandableGroup(
            string title, VisualElement headerExtra, VisualElement content,
            string stateKey, bool fallbackExpanded,
            VisualElement headerMainOverride = null,
            System.Action<bool, bool> onExpandedChanged = null,
            bool listRowEligible = true)
        {
            var box = new VisualElement();
            box.AddToClassList("sasq-group");
            // The List row style is for groups that ARE a field row in the inspector's vertical run (curve,
            // gradient, asset preview, particle). A nested disclosure inside another widget keeps its box —
            // dropping the frame there would read as content escaping its parent, not as a tidier list.
            bool listStyle = listRowEligible
                             && SasquatchSettingsManager.Current.ExpandableRowStyle == ExpandableRowStyle.List;
            if (listStyle) box.AddToClassList(ListRowClass);

            var header = new VisualElement();
            header.AddToClassList("sasq-group-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            var chevron = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronRight, 10, WidgetPalette.ChevronDim);
            chevron.AddToClassList("sasq-group-chevron");
            header.Add(chevron);

            if (headerMainOverride != null)
            {
                headerMainOverride.style.flexGrow = 1;
                // GROW is not enough — it also has to be able to give width BACK. Unity ships a BaseField as
                // flexShrink 0 with a content-derived basis, so an ObjectField handed in here sized itself to
                // its content (measured 282px) and refused to shrink, painting 65px past a 238px header on a
                // 300pt inspector. flexGrow only ever hands out SPARE width; nothing was asking for the
                // reverse. Shared by the curve, gradient, asset-preview and particle rows, so all four were
                // wrong in the same way and are fixed together here.
                headerMainOverride.style.flexShrink = 1;
                headerMainOverride.style.minWidth = 0;
                header.Add(headerMainOverride);
                // NOTE: we do NOT blanket-StopPropagation the override — the field covers the whole header
                // bar, so that would leave the caret as the only way to fold. The CALLER stops propagation on just the
                // interactive sub-part (e.g. an ObjectField's input box) so the rest of the header bar still
                // toggles. See BindingFactory.ParticleSystem's StopHeaderFieldInput.
            }
            else
            {
                var titleLabel = new Label(title);
                titleLabel.AddToClassList("sasq-group-title");
                // The affix anchor, for the same reason the INLINE branches stamp it on their own label: a card
                // has no unity-base-field, so without an anchor the decorator parks the affix at the very
                // bottom of the whole card. Only the Inline shapes stamped it, so Curve / Gradient / Spring in
                // EXPANDABLE mode dropped their unit below the graph well instead of beside the title.
                // Stamped here rather than at each call site so every expandable card gets it for free.
                titleLabel.AddToClassList(FieldAffixes.AffixAnchorClass);
                titleLabel.pickingMode = PickingMode.Ignore;
                // Truncate a long title ("…") instead of letting it overrun the header-preview
                // (ExpandableWithPreview) that sits to its right.
                titleLabel.style.flexShrink = 1;
                titleLabel.style.overflow = Overflow.Hidden;
                titleLabel.style.whiteSpace = WhiteSpace.NoWrap;
                titleLabel.style.textOverflow = TextOverflow.Ellipsis;

                if (headerExtra != null)
                {
                    // ExpandableWithPreview: title + preview ride the SHARED label column, so the preview's
                    // left edge lands exactly where a sibling field's input box starts instead of floating
                    // right-anchored off a fixed preview width (which only agreed with the column at one
                    // inspector width and drifted ~70px from it on a wide one).
                    // The align row — not the header — is what joins the column: the column drives a label
                    // sitting flush at its row's left edge, and the title starts one chevron in from the
                    // header's edge. This puts the align row's left edge at the same offset a
                    // headerMainOverride ObjectField has, so both header shapes land on one column.
                    var alignRow = new VisualElement();
                    alignRow.AddToClassList(FieldLabelColumn.ColumnRowClass);
                    alignRow.style.flexDirection = FlexDirection.Row;
                    alignRow.style.alignItems = Align.Center;
                    alignRow.style.flexGrow = 1;
                    alignRow.pickingMode = PickingMode.Ignore; // the header bar owns the fold click

                    titleLabel.AddToClassList(FieldLabelColumn.ColumnLabelClass);
                    alignRow.Add(titleLabel);

                    headerExtra.style.flexGrow = 1;    // fill the column edge → header's right edge
                    headerExtra.style.flexShrink = 1;
                    headerExtra.style.marginLeft = 0;  // the column IS the gutter; stock inputs get no extra
                    alignRow.Add(headerExtra);
                    headerExtra = null;                // consumed by the align row

                    header.Add(alignRow);
                }
                else
                {
                    titleLabel.style.flexGrow = 1;
                    header.Add(titleLabel);
                }
            }

            if (headerExtra != null)
            {
                headerExtra.style.flexShrink = 0; // keep the header-preview at its full width
                headerExtra.style.marginLeft = 8; // gap between the (possibly ellipsized) title and the preview
                header.Add(headerExtra);
            }
            box.Add(header);

            var groupContent = new VisualElement();
            groupContent.AddToClassList("sasq-group-content");
            groupContent.style.flexDirection = FlexDirection.Column;
            groupContent.Add(content);
            box.Add(groupContent);

            if (listStyle) AttachListRowRule(box);

            bool expanded = FoldoutStateStore.Get(stateKey, fallbackExpanded);
            void Apply(bool animate)
            {
                EditorSpringFX.SpringCaret(chevron, expanded, animate);
                // List rows have no header border or fill, so there is no corner to square off — and an
                // inline radius would beat the USS that zeroes it.
                if (!listStyle)
                {
                    header.style.borderBottomLeftRadius = expanded ? 0 : 4;
                    header.style.borderBottomRightRadius = expanded ? 0 : 4;
                }
                if (animate) EditorSpringFX.Fold(groupContent, expanded);
                else groupContent.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Apply(false);
            onExpandedChanged?.Invoke(expanded, false);

            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                expanded = !expanded;
                Apply(true);
                FoldoutStateStore.Set(stateKey, expanded);
                onExpandedChanged?.Invoke(expanded, true);
                evt.StopPropagation();
            });
            return box;
        }

        // Applies the node's border color + width to a curve well (mirrors ApplyGradientWellBorder so the two
        // sibling wells stay visually identical). Unset color resolves to the theme border, so the defaults
        // (width 1, theme color) reproduce the .sasq-curve-well USS in both skins with no visual change.
        internal static void ApplyCurveWellBorder(VisualElement well, BindingNode node)
        {
            float w = Mathf.Clamp(node.CurveBorderWidth, 0f, 6f);
            well.style.borderTopWidth = w;
            well.style.borderBottomWidth = w;
            well.style.borderLeftWidth = w;
            well.style.borderRightWidth = w;
            Color c = node.CurveBorderColor.Resolve(WidgetPalette.Border);
            well.style.borderTopColor = c;
            well.style.borderBottomColor = c;
            well.style.borderLeftColor = c;
            well.style.borderRightColor = c;
        }

        // ----- Well + resize handle -----
        private static VisualElement BuildCurveWell(SasquatchCurveControl control, BindingNode node)
        {
            var well = new VisualElement();
            well.AddToClassList("sasq-curve-well");
            ApplyCurveWellBorder(well, node);
            well.Add(control);

            if (!node.CurveResizable)
            {
                well.style.height = Mathf.Clamp(node.CurveWellHeight, 24f, 160f);
                return well;
            }

            well.style.height = Mathf.Clamp(node.CurveWellHeight, 24f, 600f);

            var wrap = new VisualElement();
            wrap.Add(well);

            var handle = new VisualElement();
            handle.AddToClassList("sasq-curve-resize-handle");
            handle.AddToClassList(LayoutTreeBuilder.DesignKeepInteractiveClass); // resize works in design mode too
            var grip = new GripGlyph();
            grip.style.alignSelf = Align.Center;
            handle.Add(grip);
            handle.RegisterCallback<PointerEnterEvent>(_ => grip.Tint = GripGlyph.HoverTint);
            handle.RegisterCallback<PointerLeaveEvent>(_ => grip.Tint = GripGlyph.DefaultTint);
            AttachCurveResize(handle, well, node);
            wrap.Add(handle);
            return wrap;
        }

        private static void AttachCurveResize(VisualElement handle, VisualElement well, BindingNode node)
        {
            var onEdited = LayoutTreeBuilder.CurrentOnLayoutEdited;
            bool resizing = false;
            float startY = 0f, startH = 0f;

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                resizing = true;
                startY = evt.position.y;
                startH = well.resolvedStyle.height > 1f ? well.resolvedStyle.height : node.CurveWellHeight;
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                float raw = Mathf.Clamp(startH + (evt.position.y - startY), 24f, 600f);
                well.style.height = raw;
                node.CurveWellHeight = raw;
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (handle.HasPointerCapture(evt.pointerId)) handle.ReleasePointer(evt.pointerId);
                onEdited?.Invoke();
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => resizing = false);
        }

        // ----- Two stacked action rows: keeps the selected-key controls from overflowing at default
        // inspector width by splitting them across two lines instead of one wrapping line:
        //   row 1  Tangents [Both|One] │ [Auto][Flat][Linear][Constant]
        //   row 2  Time [__]  Value [__]                          [Frame][Edit]
        // The tangent row and the Time/Value group only apply to a selected key; with reserveSpace they stay
        // laid out (just hidden) so selecting/deselecting never resizes the graph above. Frame/Edit sit on the
        // right of row 2 and stay visible whether or not a key is selected.
        internal static VisualElement BuildCurveActionRow(SasquatchCurveControl control, bool includeExpand, Action onExpand, bool reserveSpace)
        {
            var root = new VisualElement();
            root.AddToClassList("sasq-curve-actionrow");

            // --- row 1: tangent shaping (key-only) ---
            var tangentRow = new VisualElement();
            tangentRow.AddToClassList("sasq-curve-tangentrow");

            var cap = new Label("Tangents");
            cap.AddToClassList("sasq-curve-cap");
            cap.tooltip = "How the curve bends through the selected key. Drag the blue handles to shape it.";
            tangentRow.Add(cap);

            // Handle-drag mode toggle, FIRST and visually set apart: Both (move handles together, default,
            // highlighted) ↔ One (break them apart). Alt while dragging temporarily inverts this.
            Button handleBtn = null;
            handleBtn = new Button(() => control.BreakHandles = !control.BreakHandles)
            {
                tooltip = "Both: drag moves both handles together. One: drag moves a single handle (broken). Alt-drag temporarily does the other."
            };
            handleBtn.AddToClassList("sasq-curve-btn");
            handleBtn.AddToClassList("sasq-curve-handlebtn");
            tangentRow.Add(handleBtn);

            var divider = new VisualElement();
            divider.AddToClassList("sasq-curve-divider");
            tangentRow.Add(divider);

            var tips = new Dictionary<CurveTangentMode, string>
            {
                { CurveTangentMode.Auto, "Smooth, clamped so the curve never overshoots between keys." },
                { CurveTangentMode.Flat, "Horizontal tangents (an ease in/out)." },
                { CurveTangentMode.Linear, "Straight lines to the neighbouring keys." },
                { CurveTangentMode.Constant, "Hold the value, then jump (stepped)." },
            };
            var modeButtons = new List<(CurveTangentMode mode, Button btn)>();
            foreach (var m in new[] { CurveTangentMode.Auto, CurveTangentMode.Flat, CurveTangentMode.Linear, CurveTangentMode.Constant })
            {
                var mm = m;
                var b = new Button(() => control.SetSelectedTangentMode(mm)) { text = m.ToString(), tooltip = tips[m] };
                b.AddToClassList("sasq-curve-btn");
                modeButtons.Add((m, b));
                tangentRow.Add(b);
            }
            root.Add(tangentRow);

            // --- row 2: Time/Value (key-only) on the left, Frame/Edit (always) on the right ---
            var fieldsRow = new VisualElement();
            fieldsRow.AddToClassList("sasq-curve-fieldsrow");

            var keyFields = new VisualElement();
            keyFields.AddToClassList("sasq-curve-keyfields");
            var timeWrap = MakeMiniFloat("Time", out var timeField);
            var valueWrap = MakeMiniFloat("Value", out var valueField);
            valueWrap.style.marginLeft = 12; // a clear gap so the two read as a Time/Value pair
            keyFields.Add(timeWrap);
            keyFields.Add(valueWrap);
            fieldsRow.Add(keyFields);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            fieldsRow.Add(spacer);

            // --- always-present actions: Frame (icon) + optional Edit (icon) ---
            var frame = new Button(control.FrameAll) { tooltip = "Frame the whole curve (F)" };
            frame.AddToClassList("sasq-curve-btn");
            frame.AddToClassList("sasq-curve-iconbtn");
            frame.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Scan, 13));
            fieldsRow.Add(frame);

            if (includeExpand && onExpand != null)
            {
                Button expand = null;
                expand = new Button(() => { EditorSpringFX.PressBounce(expand); onExpand(); }) { tooltip = "Open the resizable curve editor" };
                expand.AddToClassList("sasq-curve-btn");
                expand.AddToClassList("sasq-curve-iconbtn");
                expand.Add(SasquatchIcons.Create(SasquatchIcons.Ids.ExternalLink, 13));
                fieldsRow.Add(expand);
            }
            root.Add(fieldsRow);

            bool refreshing = false;
            void Refresh()
            {
                bool sel = control.HasSelection;
                // The tangent row + Time/Value group only apply to a selected key. Frame/Edit stay put on row 2.
                if (reserveSpace)
                {
                    tangentRow.style.visibility = sel ? Visibility.Visible : Visibility.Hidden;
                    keyFields.style.visibility = sel ? Visibility.Visible : Visibility.Hidden;
                }
                else
                {
                    tangentRow.style.display = sel ? DisplayStyle.Flex : DisplayStyle.None;
                    keyFields.style.display = sel ? DisplayStyle.Flex : DisplayStyle.None;
                }

                // Handle-mode toggle reflects the live state (kept in sync across both editors via Changed).
                handleBtn.text = control.BreakHandles ? "One" : "Both";
                handleBtn.EnableInClassList("sasq-curve-btn--active", !control.BreakHandles);

                if (!sel) return;
                refreshing = true;
                var mode = control.SelectedTangentMode();
                foreach (var (m, b) in modeButtons)
                    b.EnableInClassList("sasq-curve-btn--active", m == mode);
                timeField.SetValueWithoutNotify(control.SelectedTime);
                valueField.SetValueWithoutNotify(control.SelectedValue);
                refreshing = false;
            }

            timeField.RegisterValueChangedCallback(e => { if (!refreshing) control.SetSelectedKeyTime(e.newValue); });
            valueField.RegisterValueChangedCallback(e => { if (!refreshing) control.SetSelectedKeyValue(e.newValue); });

            control.Changed += Refresh;
            Refresh();
            return root;
        }

        // ----- Preset swatch gallery from the shared library -----
        internal static VisualElement BuildPresetStrip(SasquatchCurveControl control, Color lineColor)
        {
            var outer = new VisualElement();
            outer.AddToClassList("sasq-curve-swatch-row");

            void Rebuild()
            {
                outer.Clear();
                var lib = SasquatchCurvePresetLibrary.Instance;
                for (int i = 0; i < lib.Presets.Count; i++)
                {
                    int idx = i;
                    var entry = lib.Presets[i];
                    var sw = BuildSwatch(entry.name, entry.curve, lineColor, () => ApplyPresetCurve(control, entry.curve));
                    sw.AddManipulator(new ContextualMenuManipulator(m =>
                    {
                        m.menu.AppendAction("Apply", _ => ApplyPresetCurve(control, entry.curve));
                        m.menu.AppendAction("Delete Preset", _ => { lib.RemoveAt(idx); Rebuild(); });
                    }));
                    outer.Add(sw);
                }
                outer.Add(BuildAddSwatch(() =>
                {
                    var lib2 = SasquatchCurvePresetLibrary.Instance;
                    lib2.Add("Preset " + (lib2.Presets.Count + 1), control.SnapshotCurve());
                    Rebuild();
                }));
                outer.Add(BuildPresetMenuButton(control, Rebuild));
            }
            Rebuild();
            return outer;
        }

        private static VisualElement BuildSwatch(string tooltip, AnimationCurve shape, Color line, Action onClick)
        {
            var el = new VisualElement { tooltip = tooltip };
            el.AddToClassList("sasq-curve-swatch");
            el.generateVisualContent += mgc => DrawSwatch(mgc, el.contentRect, shape, line);
            el.RegisterCallback<ClickEvent>(_ => onClick());
            return el;
        }

        private static VisualElement BuildAddSwatch(Action onClick)
        {
            var el = new VisualElement { tooltip = "Save the current curve as a new preset" };
            el.AddToClassList("sasq-curve-swatch");
            el.AddToClassList("sasq-curve-swatch-add");
            var plus = new Label("+");
            plus.AddToClassList("sasq-curve-swatch-plus");
            el.Add(plus);
            el.RegisterCallback<ClickEvent>(_ => onClick());
            return el;
        }

        private static VisualElement BuildPresetMenuButton(SasquatchCurveControl control, Action rebuild)
        {
            var btn = new Button(() =>
            {
                var lib = SasquatchCurvePresetLibrary.Instance;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Add Current Curve as Preset"), false, () =>
                {
                    lib.Add("Preset " + (lib.Presets.Count + 1), control.SnapshotCurve());
                    rebuild();
                });
                menu.AddItem(new GUIContent("Add Factory Presets"), false, () => { lib.AddFactoryPresets(); rebuild(); });
                menu.AddItem(new GUIContent("Import Unity Presets"), false, () =>
                {
                    var (added, skipped) = UnityPresetImporter.ImportCurves(lib);
                    EditorUtility.DisplayDialog("Import Unity Presets",
                        $"Imported {added} new preset(s), skipped {skipped} duplicate(s).", "OK");
                    rebuild();
                });
                menu.AddItem(new GUIContent("Reset to Factory Presets"), false, () => { lib.ResetToFactory(); rebuild(); });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Reveal Library Asset"), false, () => lib.Reveal());
                menu.ShowAsContext();
            })
            { tooltip = "Preset library options" };
            btn.AddToClassList("sasq-curve-btn");
            btn.AddToClassList("sasq-curve-iconbtn");
            btn.Add(BuildDotsGlyph()); // a real 3-dot glyph (the ⋮ font char renders as a bar in some fonts)
            return btn;
        }

        // Three vertical dots drawn with Painter2D fills — reliable at any DPI (unlike the ⋮ font glyph).
        private static VisualElement BuildDotsGlyph()
        {
            var dots = new VisualElement { pickingMode = PickingMode.Ignore };
            dots.style.width = 4;
            dots.style.height = 14;
            dots.style.flexShrink = 0;
            var dotColor = WidgetPalette.LabelSecondary; // captured: the paint callback runs after the build scope
            dots.generateVisualContent += mgc =>
            {
                var r = dots.contentRect;
                if (r.width < 1f || r.height < 1f) return;
                var p = mgc.painter2D;
                p.fillColor = dotColor;
                float cx = r.width * 0.5f;
                for (int i = 0; i < 3; i++)
                {
                    float cy = r.height * (0.5f + (i - 1) * 0.32f);
                    p.BeginPath();
                    p.Arc(new Vector2(cx, cy), 1.35f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                }
            };
            return dots;
        }

        private static void DrawSwatch(MeshGenerationContext mgc, Rect r, AnimationCurve shape, Color line)
        {
            if (r.width < 2f || r.height < 2f || shape == null || shape.length == 0) return;
            var p = mgc.painter2D;

            float t0 = shape[0].time, t1 = shape[shape.length - 1].time;
            if (t1 - t0 < 1e-4f) t1 = t0 + 1f;
            float lo = float.MaxValue, hi = float.MinValue;
            const int n = 24;
            for (int i = 0; i <= n; i++)
            {
                float v = shape.Evaluate(Mathf.Lerp(t0, t1, i / (float)n));
                if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
            }
            if (lo > hi) { lo = 0f; hi = 1f; }
            if (hi - lo < 1e-3f) { hi = lo + 1f; }
            float pad = (hi - lo) * 0.16f; lo -= pad; hi += pad;

            const float inset = 4f;
            Vector2 Map(float tt, float vv)
            {
                float x = inset + (tt - t0) / (t1 - t0) * (r.width - 2f * inset);
                float y = inset + (1f - (vv - lo) / (hi - lo)) * (r.height - 2f * inset);
                return new Vector2(x, y);
            }

            p.strokeColor = line;
            p.lineWidth = 1.5f;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            for (int i = 0; i <= n; i++)
            {
                float tt = Mathf.Lerp(t0, t1, i / (float)n);
                var pt = Map(tt, shape.Evaluate(tt));
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.Stroke();
        }

        private static void ApplyPresetCurve(SasquatchCurveControl control, AnimationCurve preset)
        {
            control.MorphTo(ScaleCurveToSpan(preset, control.SnapshotCurve()), "Apply Curve Preset");
        }

        // Remaps a preset (authored over its own domain) onto the current curve's time span, keeping values
        // as-authored (scaling tangents to match the new time scale) and preserving tangent modes + wrap modes.
        private static AnimationCurve ScaleCurveToSpan(AnimationCurve preset, AnimationCurve target)
        {
            if (preset == null || preset.length == 0) return new AnimationCurve();

            float t0 = 0f, t1 = 1f;
            if (target != null && target.length > 0)
            {
                t0 = target[0].time;
                t1 = target[target.length - 1].time;
                if (t1 - t0 < 1e-4f) t1 = t0 + 1f;
            }

            var pk = preset.keys;
            float pt0 = pk[0].time, pt1 = pk[pk.Length - 1].time;
            if (pt1 - pt0 < 1e-4f) pt1 = pt0 + 1f;
            float f = (t1 - t0) / (pt1 - pt0);
            if (f <= 1e-6f) f = 1f;

            var nk = new Keyframe[pk.Length];
            for (int i = 0; i < pk.Length; i++)
            {
                var k = pk[i];
                var x = new Keyframe(t0 + (k.time - pt0) * f, k.value, k.inTangent / f, k.outTangent / f)
                {
                    weightedMode = k.weightedMode,
                    inWeight = k.inWeight,
                    outWeight = k.outWeight
                };
                nk[i] = x;
            }
            var c = new AnimationCurve(nk);
            for (int i = 0; i < pk.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.GetKeyLeftTangentMode(preset, i));
                AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.GetKeyRightTangentMode(preset, i));
                AnimationUtility.SetKeyBroken(c, i, AnimationUtility.GetKeyBroken(preset, i));
            }
            c.preWrapMode = target != null ? target.preWrapMode : WrapMode.ClampForever;
            c.postWrapMode = target != null ? target.postWrapMode : WrapMode.ClampForever;
            return c;
        }

        private static VisualElement MakeMiniFloat(string label, out FloatField field)
        {
            var row = new VisualElement();
            row.AddToClassList("sasq-curve-numrow");
            var l = new Label(label);
            l.AddToClassList("sasq-curve-cap");
            field = new FloatField();
            field.AddToClassList("sasq-curve-num");
            row.Add(l);
            row.Add(field);
            return row;
        }
    }
}
