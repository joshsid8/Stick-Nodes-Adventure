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
    public static partial class BindingFactory
    {
        // Sub-objects whose inline layout is currently mid-Build, to break reference cycles —
        // A.ref → B and B.ref → A would otherwise recurse Build until the stack overflows.
        private static readonly HashSet<UnityEngine.Object> _inlineBuildsInFlight = new HashSet<UnityEngine.Object>();

        // The nested Build call shared by both inline-editor looks. Wires what a nested layout needs to
        // behave like a real inspector: rebuildSubEditor as onRebuildRequired (collection +/− and
        // row-menu ops are structural — without a live callback they mutate the array but the rows
        // never refresh), and a persist hook so live layout edits (text-area / table-column resizes)
        // save against the NESTED layout asset, mirroring VisualInspectorEditor's root wiring.
        internal static VisualElement BuildNestedInlineLayout(
            InspectorLayoutAsset nestedLayout, SerializedObject subSO, Action rebuildSubEditor, bool firstBuild)
        {
            var target = subSO.targetObject;
            if (!_inlineBuildsInFlight.Add(target))
            {
                return new UnityEngine.UIElements.HelpBox(
                    $"Inline editor skipped: '{target.name}' is already open in an outer inline editor (circular reference).",
                    UnityEngine.UIElements.HelpBoxMessageType.Warning);
            }
            try
            {
                var view = LayoutTreeBuilder.Build(
                    nestedLayout,
                    subSO,
                    rebuildSubEditor,
                    playAppearAnimations: firstBuild,
                    onLayoutEdited: () => LayoutRegistry.PersistLayoutEdit(nestedLayout));

                // The nested body is a real inspector rendering of a real layout, so its Script Header builds a
                // real magnifier — but NOTHING wired it. Attach is called by the two root render paths and the
                // Showcase, none of which reach in here, so the in-line editor's magnifier was a button with no
                // handler: it revealed on hover, it depressed, and it did nothing. The engine is per-BODY (its
                // index, its saved query, its accent frame all hang off the root it was given), so the nested
                // body needs its own instance rather than a share of the host's.
                //
                // Attached HERE, in the shared nested-build helper, so both in-line looks (expandable and
                // legacy) and therefore BOTH render paths get it from one wiring — the same reason
                // WireNestedLayoutSync lives in this file.
                //
                // SCOPE: `view` is the whole contract. LayoutSearchFilter only ever writes to elements the
                // index handed it, InspectorSearchIndex.Build stops at nested-editor boundaries, and
                // SearchActiveOverlay frames the root it is given — so this filter can reach the embedded
                // object's fields and nothing else. The host's own fields are not hidden, not counted in the
                // n/m readout, and not framed.
                //
                // Returns null by itself when the nested layout has no search-enabled Script Header (no header,
                // or ShowSearch off) — the same "then there is no field search here" answer the root paths get.
                // Design canvases likewise resolve to null: BuildScriptHeaderElement returns before registering
                // its handles in design mode, so there is nothing for Attach to find.
                InspectorFieldSearch.Attach(view, target);

                return view;
            }
            finally
            {
                _inlineBuildsInFlight.Remove(target);
            }
        }

        /// <summary>
        /// Keeps a nested in-line editor in step with the EMBEDDED type's layout. The post-save refresh
        /// (<see cref="SasquatchEditorRegistrar.RefreshInspectorsForType"/>) matches on each inspector row's OWN
        /// type, so a nested editor drawing another type's layout is invisible to it: designing the embedded
        /// script from the pencil above and saving left the nested fields stale until the user deselected and
        /// reselected the host object. Rebuilding just this subtree — rather than widening the sweep to the whole
        /// host inspector — keeps the rest of the inspector (and its scroll position) untouched.
        ///
        /// <para>Lives in the shared widget builder, so BOTH render paths get it from one wiring.</para>
        /// </summary>
        internal static void WireNestedLayoutSync(VisualElement scope, SerializedProperty prop, Action rebuild)
        {
            // Design-time canvases (designer preview, Showcase, tutorial, export preview) deliberately never
            // auto-sync. Captured here because CurrentDesignMode is only set DURING Build.
            if (LayoutTreeBuilder.CurrentDesignMode) return;

            Action<Type> onLayoutChanged = null;
            void Unsubscribe() => SasquatchEditorRegistrar.LayoutChangedForType -= onLayoutChanged;

            onLayoutChanged = savedType =>
            {
                if (savedType == null) return;

                UnityEngine.Object subObj;
                Type hostType;
                try
                {
                    // NEVER reach for objectReferenceValue before proving the property is still live. A stale
                    // SerializedProperty does not throw on that read — Unity LOGS "type is not a supported pptr
                    // value", which no try/catch can see and a test run scores as a failure. A destroyed host is
                    // also permanent (unlike a null panel, which a window-tab re-parent produces for one event),
                    // so it is the one signal on which a handler that outlived its element may unhook itself.
                    var host = prop.serializedObject != null ? prop.serializedObject.targetObject : null;
                    if (host == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        Unsubscribe();
                        return;
                    }
                    hostType = host.GetType();
                    subObj = prop.objectReferenceValue;
                }
                catch (Exception) { Unsubscribe(); return; }   // SerializedObject disposed mid-teardown

                if (subObj == null) return;    // nothing embedded ⇒ nothing to re-draw
                // The HOST inspector already rebuilds itself for its OWN type's layout change (the fallback
                // sweep / displacement invalidate). Stepping in there would build this subtree twice and throw
                // the first one away.
                if (SasquatchEditorRegistrar.IsLayoutRelevant(hostType, savedType)) return;
                if (!SasquatchEditorRegistrar.IsLayoutRelevant(subObj.GetType(), savedType)) return;

                rebuild();
            };

            SasquatchEditorRegistrar.LayoutChangedForType += onLayoutChanged;
            // A static event outlives the element, so the handler MUST go when the subtree really leaves — but a
            // detach is not a teardown (a window-tab drag re-parents the whole tree), hence OnRealDetach.
            PanelLifetime.OnRealDetach(scope, Unsubscribe);
        }

        /// <summary>
        /// The little pencil on an in-line editor that opens the EMBEDDED script's own layout in the designer.
        /// This is where you actually notice you want it: you're looking at the nested fields, and styling them
        /// otherwise means abandoning what you're doing and hunting the script down on some other object.
        ///
        /// Lives here, in the shared widget builder, so BOTH render paths get it from one wiring —
        /// VisualInspectorEditor (fallback) and SasquatchForceTakeover (displacement) both come through
        /// <c>LayoutTreeBuilder.Build</c>. Hidden in the designer's own canvas: the designer has its own two
        /// ways in (the hierarchy right-click and the Properties row), and a third that retargets the very
        /// window you're standing in would just be confusing.
        ///
        /// Targets the type of the object CURRENTLY assigned — that's what's rendering in front of you — so it
        /// hides itself whenever the field is empty or holds something we don't edit, and re-syncs on every
        /// reference swap. Internal so a test can build one directly.
        /// </summary>
        internal static VisualElement CreateInlineEditorDesignerLink(SerializedProperty prop)
        {
            var pencil = new Button { tooltip = "Design this script's inspector layout" };
            pencil.AddToClassList("sasq-inline-design-link");
            pencil.style.width = 20;
            pencil.style.height = 18;
            pencil.style.flexShrink = 0;
            pencil.style.marginLeft = 2;
            pencil.style.marginRight = 0;
            pencil.style.marginTop = 0;
            pencil.style.marginBottom = 0;
            pencil.style.paddingLeft = 0;
            pencil.style.paddingRight = 0;
            pencil.style.justifyContent = Justify.Center;
            pencil.style.alignItems = Align.Center;
            pencil.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Pencil, 11, WidgetPalette.LabelDim));
            EditorSpringFX.ButtonSpring(pencil);

            // The header row toggles the fold on pointer-down; without this, opening the designer would also
            // collapse the editor you were looking at.
            pencil.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());

            UnityEngine.Object Target()
            {
                if (LayoutTreeBuilder.CurrentDesignMode) return null;
                UnityEngine.Object obj;
                try { obj = prop.objectReferenceValue; }
                catch (Exception) { return null; }   // disposed SerializedObject mid-teardown
                if (obj == null) return null;
                return SasquatchContextMenu.IsTypeEditable(obj.GetType()) ? obj : null;
            }

            void Sync()
            {
                var obj = Target();
                pencil.style.display = obj != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (obj != null) pencil.tooltip = $"Design {obj.GetType().Name}'s inspector layout";
            }

            pencil.clicked += () =>
            {
                var obj = Target();
                if (obj != null) SasquatchLayoutDesignerWindow.OpenWindowForType(obj.GetType(), obj);
            };

            Sync();
            // Owns its own tracker so no caller has to remember to re-sync it on a reference swap. The
            // TrackedRefresh wrapper is mandatory: a bare TrackPropertyValue silently stops firing after the
            // element is reattached to a panel.
            pencil.TrackPropertyValueWithRefresh(prop, _ => Sync());
            return pencil;
        }

        private static VisualElement CreateInlineEditorWrapper(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            // In-Line Editor only applies to a UnityEngine.Object reference. If the bound property isn't one
            // (e.g. the field's type changed under a saved layout that still carries this widget kind), fall
            // back to a plain field instead of throwing on prop.objectReferenceValue below — matching the
            // guard that CreateAssetPreview / CreateAudioPreview already have.
            // resolvedLabel already folds in BOTH the node's own label state AND the caller's forceHideLabel
            // (a table cell or a collection row, where the column header / [index] carries the name instead).
            // Reading node.IsLabelHidden directly here would ignore the caller and re-label a row "Element 0".
            if (prop.propertyType != SerializedPropertyType.ObjectReference)
            {
                var fallback = new PropertyField(prop);
                if (resolvedLabel != null) fallback.label = resolvedLabel;
                return fallback;
            }

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;

            var baseField = new PropertyField(prop);
            if (resolvedLabel != null) baseField.label = resolvedLabel;

            // Optional "Create" button shown while the reference is null (per-binding opt-in; SO fields only).
            // Assignment flows through the existing TrackPropertyValue rebuild, so no extra refresh wiring.
            Button createBtn = null;
            if (node.CreateAssetEnabled && AssetCreateUtility.IsEligible(prop, out var createFieldType))
            {
                createBtn = AssetCreateUtility.CreateButton(prop, createFieldType);
                AssetCreateUtility.AttachVisibility(createBtn, prop);
            }

            // When Expandable, the embedded editor sits under a clickable chevron header that the dev can
            // collapse/expand in the inspector (mirrors the CollectionNode "sasq-group" pattern). DefaultToOpen
            // sets the initial state. When not expandable, the embedded fields render directly (legacy look).
            bool expandable = node.Expandable;
            // Open/closed is per-USER view state, remembered durably (EditorPrefs) exactly like every other
            // collapsible site in the inspector — containers, collection headers, the curve/gradient/asset/
            // particle group boxes. This widget was the ONE that never joined FoldoutStateStore, so its state
            // lived only in the closure: an inspector rebuild — which a layout save, closing the designer, or
            // any selection change performs — reset it to DefaultToOpen, and an editor the user had opened
            // "closed all by itself". DefaultToOpen stays the AUTHOR's default; a toggle by this user wins.
            // Keyed on the bound property path (not the assigned object's name, which moves on reassignment),
            // so a per-row editor inside a collection remembers each row independently. Design-time canvases
            // pass a null key ⇒ non-persistent, always seeded from the author's default.
            string foldStateKey = LayoutTreeBuilder.CurrentDesignMode
                ? null
                : FoldoutStateStore.Key(
                    prop.serializedObject != null ? prop.serializedObject.targetObject : null,
                    prop.propertyPath, resolvedLabel ?? string.Empty);
            bool expanded = FoldoutStateStore.Get(foldStateKey, node.DefaultToOpen);
            // Whether the embedded editor has actually been built yet. An expandable editor that starts
            // COLLAPSED defers its build to the first open (see below) — the difference between one in-line
            // editor and a whole collection page of them.
            bool fieldsBuilt = false;
            bool firstBuild = true; // declared here so the header's toggle callback can reach RebuildInlineFields

            // Holder for the embedded sub-editor fields; cleared + rebuilt whenever the reference changes.
            var subContainer = new VisualElement();
            // Mark the nested-editor scope so the header scan never mistakes the inlined object's m_Script for
            // the ROW's script — that mis-types the row, which then fails tracker correlation and loses both
            // its pencil and any takeover (see SasquatchInspectorHeaderPencil.ReadScriptType).
            subContainer.AddToClassList(SasquatchInspectorHeaderPencil.NestedEditorScopeClass);
            subContainer.style.flexDirection = FlexDirection.Column;

            if (expandable)
            {
                // Single continuous dark outline that STARTS at the header and wraps the WHOLE widget
                // (header + object field + embedded fields), mirroring the collection group look so it all
                // reads as one box. The header and content keep their backgrounds but drop their individual
                // side/corner borders so this one outline is the only visible border; overflow:Hidden clips
                // their square corners to the rounded box.
                var groupOutline = WidgetPalette.BorderStrong; // #1e1e1e on dark

                var groupBox = new VisualElement();
                groupBox.AddToClassList("sasq-group");
                groupBox.style.marginTop = 4;
                groupBox.style.marginBottom = 4;
                groupBox.style.borderTopWidth = 1;
                groupBox.style.borderBottomWidth = 1;
                groupBox.style.borderLeftWidth = 1;
                groupBox.style.borderRightWidth = 1;
                groupBox.style.borderTopColor = groupOutline;
                groupBox.style.borderBottomColor = groupOutline;
                groupBox.style.borderLeftColor = groupOutline;
                groupBox.style.borderRightColor = groupOutline;
                groupBox.style.borderTopLeftRadius = 4;
                groupBox.style.borderTopRightRadius = 4;
                groupBox.style.borderBottomLeftRadius = 4;
                groupBox.style.borderBottomRightRadius = 4;
                groupBox.style.overflow = Overflow.Hidden;

                var headerRow = new VisualElement();
                headerRow.AddToClassList("sasq-group-header");
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.alignItems = Align.Center;
                // Drop the header's own box border (the groupBox outline handles it); keep only a bottom
                // separator line between the header and the content (toggled in ApplyExpanded).
                headerRow.style.borderTopWidth = 0;
                headerRow.style.borderLeftWidth = 0;
                headerRow.style.borderRightWidth = 0;
                headerRow.style.borderBottomColor = groupOutline;
                headerRow.style.borderTopLeftRadius = 0;
                headerRow.style.borderTopRightRadius = 0;

                var chevron = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronRight, 10, WidgetPalette.ChevronBright);
                chevron.AddToClassList("sasq-group-chevron"); // single glyph; ApplyExpanded spins it 0\u00b0\u219490\u00b0
                chevron.pickingMode = PickingMode.Ignore;
                chevron.style.flexShrink = 0;
                chevron.style.marginRight = 4;
                headerRow.Add(chevron);

                var titleLabel = new Label();
                titleLabel.AddToClassList("sasq-group-title");
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.flexGrow = 1;
                titleLabel.style.color = WidgetPalette.LabelPrimary;
                headerRow.Add(titleLabel);

                if (createBtn != null)
                {
                    // Right-aligned in the header (title has flexGrow). Swallow the pointer-down so a
                    // button click doesn't ALSO toggle the fold via the header's own handler.
                    createBtn.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                    headerRow.Add(createBtn);
                }

                // Sits last so it lands at the header's right edge, after any Create button. Hides itself
                // when there's nothing assigned, so an empty in-line editor keeps the header it always had.
                headerRow.Add(CreateInlineEditorDesignerLink(prop));

                groupBox.Add(headerRow);

                // Content area — holds the object field + embedded fields, sitting directly under the header
                // inside the same outline. Drop its own side/corner borders so the groupBox outline is the
                // only visible border.
                var contentTarget = new VisualElement();
                contentTarget.AddToClassList("sasq-group-content");
                contentTarget.style.flexDirection = FlexDirection.Column;
                contentTarget.style.borderLeftWidth = 0;
                contentTarget.style.borderRightWidth = 0;
                contentTarget.style.borderBottomWidth = 0;
                contentTarget.style.borderBottomLeftRadius = 0;
                contentTarget.style.borderBottomRightRadius = 0;
                contentTarget.Add(baseField);
                contentTarget.Add(subContainer);
                groupBox.Add(contentTarget);

                container.Add(groupBox);

                void ApplyExpanded(bool animate)
                {
                    EditorSpringFX.SpringCaret(chevron, expanded, animate);
                    // Separator under the header only while expanded; collapsed leaves the lone header bar.
                    headerRow.style.borderBottomWidth = expanded ? 1 : 0;
                    // Spring the content open/closed like a foldout; instant on (re)build so a refresh doesn't animate.
                    if (animate) EditorSpringFX.Fold(contentTarget, expanded);
                    else contentTarget.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                }

                headerRow.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    expanded = !expanded;
                    // First open of a row that was built collapsed: its editor was never built (see the
                    // deferral below). Build it now, then animate — the fields have to exist before the fold.
                    if (expanded && !fieldsBuilt) RebuildInlineFields();
                    ApplyExpanded(true);
                    // Persist AFTER the state settles, and only from a real user toggle — RebuildInlineFields
                    // calls ApplyExpanded too, and writing from there would re-persist on every rebuild.
                    FoldoutStateStore.Set(foldStateKey, expanded);
                    evt.StopPropagation();
                });

                // Local function (not a lambda) so it can pass ITSELF as the nested layout's rebuild
                // callback: collection +/− and row-menu operations are structural, so the sub-editor
                // must rebuild for the new rows to exist — a SerializedObject.Update() alone leaves
                // the old row set on screen, which read as "the buttons don't work".
                void RebuildInlineFields()
                {
                    fieldsBuilt = true;
                    subContainer.Clear();

                    var subObj = prop.objectReferenceValue;
                    titleLabel.text = !string.IsNullOrEmpty(resolvedLabel)
                        ? resolvedLabel
                        : (subObj != null && !string.IsNullOrEmpty(subObj.name) ? subObj.name : "Inline Editor");

                    if (subObj == null)
                    {
                        // No reference yet: keep the box + object field so the user can assign one; just
                        // skip the (non-existent) embedded fields.
                        ApplyExpanded(false);
                        return;
                    }

                    var subSO = new SerializedObject(subObj);

                    if (LayoutRegistry.TryGet(subObj.GetType(), subObj, out var nestedLayout))
                    {
                        subContainer.Add(BuildNestedInlineLayout(nestedLayout, subSO, RebuildInlineFields, firstBuild));
                    }
                    else
                    {
                        InspectorElement.FillDefaultInspector(subContainer, subSO, null);
                    }
                    firstBuild = false;

                    subContainer.Bind(subSO);
                    ApplyExpanded(false);
                }

                if (expanded)
                {
                    RebuildInlineFields();
                }
                else
                {
                    // Collapsed on build ⇒ don't build the embedded editor at all. Nothing is visible, and a
                    // collection page can hold 25 of these — building 25 nested inspectors nobody has opened
                    // is what would make per-row in-line editing feel slow. The header still needs its title.
                    var subObj = prop.objectReferenceValue;
                    titleLabel.text = !string.IsNullOrEmpty(resolvedLabel)
                        ? resolvedLabel
                        : (subObj != null && !string.IsNullOrEmpty(subObj.name) ? subObj.name : "Inline Editor");
                    ApplyExpanded(false);
                }
                // A reference swapped from outside re-syncs the header either way; the fields only rebuild
                // when they exist (an unopened row stays deferred).
                container.TrackPropertyValueWithRefresh(prop, _ =>
                {
                    if (fieldsBuilt) RebuildInlineFields();
                    else
                    {
                        var swapped = prop.objectReferenceValue;
                        titleLabel.text = !string.IsNullOrEmpty(resolvedLabel)
                            ? resolvedLabel
                            : (swapped != null && !string.IsNullOrEmpty(swapped.name) ? swapped.name : "Inline Editor");
                    }
                });

                // The embedded type's layout was saved / reverted elsewhere: rebuild against it. An unopened row
                // has nothing built yet and simply picks the new layout up on its first open.
                WireNestedLayoutSync(container, prop, () => { if (fieldsBuilt) RebuildInlineFields(); });

                return container;
            }

            // Non-expandable (legacy look): the object field followed by the embedded fields in an inset box.
            container.Add(baseField);

            // This look has no header to hang the designer link off, so it shares the Create button's
            // right-aligned row — built whether or not there IS a Create button, since both children hide
            // themselves independently and an empty row costs 2px of margin.
            var actionRow = new VisualElement();
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.justifyContent = Justify.FlexEnd;
            actionRow.style.marginTop = 2;
            if (createBtn != null) actionRow.Add(createBtn);
            actionRow.Add(CreateInlineEditorDesignerLink(prop));
            container.Add(actionRow);

            subContainer.style.marginLeft = 15;
            subContainer.style.marginTop = 4;
            subContainer.style.marginBottom = 4;
            subContainer.style.paddingTop = 4;
            subContainer.style.paddingBottom = 4;
            subContainer.style.paddingLeft = 8;
            subContainer.style.paddingRight = 8;
            subContainer.style.backgroundColor = WidgetPalette.InlineSubBg;
            subContainer.style.borderTopLeftRadius = 4;
            subContainer.style.borderTopRightRadius = 4;
            subContainer.style.borderBottomLeftRadius = 4;
            subContainer.style.borderBottomRightRadius = 4;
            subContainer.style.borderTopWidth = 1;
            subContainer.style.borderBottomWidth = 1;
            subContainer.style.borderLeftWidth = 1;
            subContainer.style.borderRightWidth = 1;
            subContainer.style.borderTopColor = WidgetPalette.InlineSubBorder;
            subContainer.style.borderBottomColor = WidgetPalette.InlineSubBorder;
            subContainer.style.borderLeftColor = WidgetPalette.InlineSubBorder;
            subContainer.style.borderRightColor = WidgetPalette.InlineSubBorder;
            container.Add(subContainer);

            // Same self-referencing rebuild contract as the expandable path above.
            bool firstLegacyBuild = true;
            void RebuildLegacy()
            {
                subContainer.Clear();

                if (prop.objectReferenceValue == null)
                {
                    subContainer.style.display = DisplayStyle.None;
                    return;
                }

                var subObj = prop.objectReferenceValue;
                var subSO = new SerializedObject(subObj);

                if (LayoutRegistry.TryGet(subObj.GetType(), subObj, out var nestedLayout))
                {
                    subContainer.Add(BuildNestedInlineLayout(nestedLayout, subSO, RebuildLegacy, firstLegacyBuild));
                }
                else
                {
                    InspectorElement.FillDefaultInspector(subContainer, subSO, null);
                }
                firstLegacyBuild = false;

                subContainer.Bind(subSO);
                subContainer.style.display = DisplayStyle.Flex;
            }

            RebuildLegacy();
            container.TrackPropertyValueWithRefresh(prop, _ => RebuildLegacy());
            WireNestedLayoutSync(container, prop, RebuildLegacy);

            return container;
        }

        // ---- Polymorphic [SerializeReference] support ---------------------------------------------------
        // A managed-reference field (interface / abstract base marked [SerializeReference]) is a sibling of
        // the inline editor: instead of an ObjectField swapping a UnityEngine.Object, a type picker chooses a
        // concrete implementor, instantiates it inline (Activator.CreateInstance → managedReferenceValue), and
        // its fields are drawn from child property paths on the SAME SerializedObject.

        /// <summary>
        /// Resolves the DECLARED (base) type of a managed-reference property — the interface/abstract type
        /// whose concrete implementors populate the picker. Primary source is the property's
        /// managedReferenceFieldTypename (correct for List/array elements too); falls back to resolving the
        /// declared C# field type from the property path. Internal so the tests can drive it.
        /// </summary>
        internal static Type ResolveManagedReferenceBaseType(SerializedProperty prop)
        {
            if (prop == null) return null;

            var fromTypename = TypeFromManagedReferenceTypename(prop.managedReferenceFieldTypename);
            if (fromTypename != null) return fromTypename;

            var target = prop.serializedObject != null ? prop.serializedObject.targetObject : null;
            if (target != null)
            {
                GetFieldInfoFromPropertyPath(target.GetType(), prop.propertyPath, out var fieldType);
                return fieldType;
            }
            return null;
        }

        // Parses Unity's "<assemblyName> <fullTypeName>" managed-reference typename into a Type.
        private static Type TypeFromManagedReferenceTypename(string typename)
        {
            if (string.IsNullOrEmpty(typename)) return null;
            int space = typename.IndexOf(' ');
            if (space <= 0 || space >= typename.Length - 1) return null;
            string asm = typename.Substring(0, space);
            string full = typename.Substring(space + 1);

            var direct = Type.GetType($"{full}, {asm}");
            if (direct != null) return direct;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = assembly.GetType(full);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>
        /// Sets a managed-reference property's value to a fresh instance of <paramref name="concreteType"/>
        /// (or null to clear), committing the change. Internal so type selection can also be driven directly,
        /// without opening the picker popup window.
        /// </summary>
        internal static void SetManagedReferenceType(SerializedProperty prop, Type concreteType)
        {
            if (prop == null) return;
            prop.managedReferenceValue = concreteType == null ? null : Activator.CreateInstance(concreteType);
            prop.serializedObject.ApplyModifiedProperties();
        }

        private static VisualElement CreateSerializeReferenceWrapper(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;

            // Not actually a managed reference (e.g. the field type changed under an explicit Polymorphic
            // widget): fall back to a plain field so the binding still renders rather than breaking.
            if (prop.propertyType != SerializedPropertyType.ManagedReference)
            {
                var fallback = new PropertyField(prop);
                if (node.IsLabelHidden) fallback.label = "";
                else if (node.EffectiveLabelOverride != null) fallback.label = node.EffectiveLabelOverride;
                container.Add(fallback);
                return container;
            }

            const string NoneLabel = "None";

            var baseType = ResolveManagedReferenceBaseType(prop);
            var concreteTypes = SasquatchReflectionUtility.GetAssignableConcreteTypes(baseType);

            // Display name -> Type, disambiguating any friendly-name collisions with the namespace.
            var nameToType = new Dictionary<string, Type>();
            var choices = new List<string> { NoneLabel };
            foreach (var t in concreteTypes)
            {
                string name = SasquatchReflectionUtility.GetFriendlyTypeName(t);
                if (nameToType.ContainsKey(name)) name = $"{name} ({t.Namespace})";
                while (nameToType.ContainsKey(name)) name += " ";
                nameToType[name] = t;
                choices.Add(name);
            }

            string CurrentTypeName()
            {
                var val = prop.managedReferenceValue;
                return val == null ? NoneLabel : SasquatchReflectionUtility.GetFriendlyTypeName(val.GetType());
            }

            // Inset box holding the chosen instance's fields (mirrors the legacy inline-editor look).
            var subContainer = new VisualElement();
            subContainer.AddToClassList("sasq-serialize-reference-fields");
            subContainer.style.flexDirection = FlexDirection.Column;
            subContainer.style.marginLeft = 15;
            subContainer.style.marginTop = 4;
            subContainer.style.marginBottom = 4;
            subContainer.style.paddingTop = 4;
            subContainer.style.paddingBottom = 4;
            subContainer.style.paddingLeft = 8;
            subContainer.style.paddingRight = 8;
            subContainer.style.backgroundColor = WidgetPalette.InlineSubBg;
            subContainer.style.borderTopLeftRadius = 4;
            subContainer.style.borderTopRightRadius = 4;
            subContainer.style.borderBottomLeftRadius = 4;
            subContainer.style.borderBottomRightRadius = 4;
            subContainer.style.borderTopWidth = 1;
            subContainer.style.borderBottomWidth = 1;
            subContainer.style.borderLeftWidth = 1;
            subContainer.style.borderRightWidth = 1;
            subContainer.style.borderTopColor = WidgetPalette.InlineSubBorder;
            subContainer.style.borderBottomColor = WidgetPalette.InlineSubBorder;
            subContainer.style.borderLeftColor = WidgetPalette.InlineSubBorder;
            subContainer.style.borderRightColor = WidgetPalette.InlineSubBorder;

            Label valueLabel = null;

            void RebuildInlineFields()
            {
                subContainer.Clear();
                bool mixed = MixedValueUI.IsMixed(prop);
                if (valueLabel != null) MixedValueUI.SetText(valueLabel, mixed, CurrentTypeName());

                // Mixed: types differ across the selection — don't draw the first object's fields as if shared.
                if (mixed || prop.managedReferenceValue == null)
                {
                    subContainer.style.display = DisplayStyle.None;
                    return;
                }

                // Draw the chosen instance's direct child fields, scoped under the managed-reference
                // property on the SAME SerializedObject (the value lives inline — no new SerializedObject).
                // Bounded child iteration excludes Unity's own managed-reference chrome / duplicate picker.
                var iterator = prop.Copy();
                var end = iterator.GetEndProperty();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
                {
                    enterChildren = false;
                    subContainer.Add(new PropertyField(iterator.Copy()));
                }

                subContainer.Bind(prop.serializedObject);
                subContainer.style.display = DisplayStyle.Flex;
            }

            // Reuse the styled searchable dropdown (its popup also stamps `choices` into userData).
            // On select: map the display name back to a Type, (re)instantiate, and rebuild.
            bool codePeek = node == null || node.PolymorphicCodePeek;
            // An implementation of a [SerializeReference] base is almost always written alongside the script
            // that declares the field, not in a file of its own — so the host type is how its source is found.
            var hostType = prop.serializedObject != null && prop.serializedObject.targetObject != null
                ? prop.serializedObject.targetObject.GetType() : null;
            var pickerRow = CreateSearchableDropdown(node, resolvedLabel, choices, CurrentTypeName(), selected =>
            {
                Type chosen = (selected == NoneLabel || !nameToType.TryGetValue(selected, out var t)) ? null : t;
                SetManagedReferenceType(prop, chosen);
                RebuildInlineFields();
            }, out valueLabel,
            // "Which implementation is this?" is the whole question this picker asks, and the answer is in the
            // class itself — so hovering a choice shows its declaration. None has no source, and a type whose
            // file can't be resolved simply shows nothing.
            codePeek: !codePeek ? (Func<string, string>)null
                : selected => selected != NoneLabel && nameToType.TryGetValue(selected, out var hovered)
                    ? MethodSourcePreview.GetTypeSnippet(hovered, hostType)
                    : null);

            container.Add(pickerRow);
            container.Add(subContainer);

            RebuildInlineFields();
            // Rebuild when the value changes externally (undo, reset, another inspector) so the picker label
            // and inline fields stay in sync.
            container.TrackPropertyValueWithRefresh(prop, _ => RebuildInlineFields());

            return container;
        }

    }
}
