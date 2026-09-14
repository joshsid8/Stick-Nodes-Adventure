using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Custom UI Toolkit drawer for UnityEvent / UnityEvent&lt;T&gt; fields — a searchable replacement
    /// for Unity's built-in IMGUI UnityEvent drawer. Each listener is a card: call-state, a target ObjectField,
    /// a searchable function picker (components + methods + property setters),
    /// and a typed argument field. Plus an Invoke/Test button, per-listener Duplicate, and reorder. All state is
    /// read/written via SerializedProperty (m_PersistentCalls.m_Calls...), so Undo + multi-edit work for free.
    /// </summary>
    public static class UnityEventWidget
    {
        private static readonly string[] CallStateOptions = { "Off", "Editor And Runtime", "Runtime Only" };

        /// <summary>Builds the custom UnityEvent card drawer for <paramref name="prop"/> (searchable function picker, per-listener call-state, typed args, invoke/duplicate/reorder).</summary>
        public static VisualElement Create(BindingNode node, SerializedProperty prop, string label)
        {
            var serializedObject = prop.serializedObject;

            // Fuzzer-safe fallback, like every other widget: degrade to a plain field for a property this
            // drawer can't drive. DisplayType.UnityEvent left on a field that ISN'T a UnityEvent still routes
            // here (BindingFactory's switch is reached exactly when IsUnityEventProperty said no), and every
            // structural op below dereferences the m_PersistentCalls array — the header "+" NREs on the first
            // click. A stale binding should look wrong, not throw.
            if (prop.FindPropertyRelative("m_PersistentCalls.m_Calls") == null)
                return new PropertyField(prop) { label = label };

            bool compact = node != null && node.UnityEventCompact;
            bool codePeek = node == null || node.UnityEventCodePeek;

            // Dynamic argument types from the event's UnityEvent<...> base (empty for plain UnityEvent).
            Type[] dynamicArgs = Type.EmptyTypes;
            var ownerType = serializedObject.targetObject != null ? serializedObject.targetObject.GetType() : null;
            if (ownerType != null)
            {
                BindingFactory.GetFieldInfoFromPropertyPath(ownerType, prop.propertyPath, out var fieldType);
                if (fieldType != null) dynamicArgs = UnityEventReflection.GetEventArgTypes(fieldType);
            }
            string argSuffix = dynamicArgs.Length == 0 ? "" : " (" + string.Join(", ", dynamicArgs.Select(FriendlyType)) + ")";
            // Same types, bare — the picker's "Dynamic  float" section heading wants no parens.
            string dynamicLabel = dynamicArgs.Length == 0 ? "" : string.Join(", ", dynamicArgs.Select(FriendlyType));

            var root = new VisualElement();
            root.AddToClassList("sasq-uevent");

            // ---- Header: chevron, name, count badge, invoke, add ----
            bool expanded = node == null || node.UnityEventDefaultToOpen;

            var header = new VisualElement();
            header.AddToClassList("sasq-uevent-header");

            var chevron = new Label(expanded ? "▾" : "▸");
            chevron.AddToClassList("sasq-uevent-chevron");
            header.Add(chevron);

            var title = new Label(string.IsNullOrEmpty(label) ? "Event" : label);
            title.AddToClassList("sasq-uevent-title");
            // This card builds no unity-base-field, so the universal affix decorator has nothing to hang a
            // prefix/suffix off. Declaring the title as the anchor makes them mount as its siblings in the
            // header instead of landing at the very top and bottom of the whole card.
            title.AddToClassList(FieldAffixes.AffixAnchorClass);
            header.Add(title);

            var typeHint = new Label(argSuffix);
            typeHint.AddToClassList("sasq-uevent-typehint");
            header.Add(typeHint);

            var badge = new Label();
            badge.AddToClassList("sasq-uevent-badge");
            header.Add(badge);

            var hSpacer = new VisualElement();
            hSpacer.style.flexGrow = 1;
            header.Add(hSpacer);

            var invokeBtn = IconButton(SasquatchIcons.Ids.Play, "Invoke now (runs Editor & Runtime calls in edit mode)");
            invokeBtn.clicked += () =>
            {
                // EVERY selected object, like the package's own Button and property invokers
                // (ButtonInvoker.InvokeButtonMethod loops targetObjects). Invoking only the first would
                // silently skip N-1 objects while the button reads as having run for the whole selection.
                var invokeTargets = serializedObject.targetObjects;
                if (invokeTargets == null || invokeTargets.Length == 0)
                {
                    UnityEventReflection.InvokeInEditor(serializedObject.targetObject, prop.propertyPath);
                }
                else
                {
                    foreach (var t in invokeTargets)
                        if (t != null) UnityEventReflection.InvokeInEditor(t, prop.propertyPath);
                }
                EditorSpringFX.PressBounce(invokeBtn);
            };
            header.Add(invokeBtn);

            var addBtn = IconButton(null, "Add listener");
            addBtn.text = "+";
            addBtn.Q<SasquatchIconElement>()?.RemoveFromHierarchy();
            header.Add(addBtn);

            // Optional designer accent: tint the header bar when overridden.
            if (node != null && node.UnityEventAccentColor.isOverridden)
                header.style.backgroundColor = node.UnityEventAccentColor.value;

            root.Add(header);

            // ---- Body: the listener cards ----
            var body = new VisualElement();
            body.AddToClassList("sasq-uevent-body");
            body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            root.Add(body);

            SerializedProperty Calls() => prop.FindPropertyRelative("m_PersistentCalls.m_Calls");

            // Collapsed, a bare "(3)" claims three things happen — and a listener with no function on it is
            // exactly the state you CANNOT see while the body is shut. So the count splits into wired/total
            // whenever the two differ, and stays a plain total the rest of the time (which is nearly always,
            // so the common case picks up no extra noise).
            void UpdateBadge()
            {
                var c = Calls();
                if (c == null) { badge.text = "(0)"; badge.tooltip = string.Empty; return; }

                int total = c.arraySize, wired = 0;
                for (int i = 0; i < total; i++)
                {
                    var call = c.GetArrayElementAtIndex(i);
                    if (HasFunction(call.FindPropertyRelative("m_Target"),
                                    call.FindPropertyRelative("m_MethodName"))) wired++;
                }

                badge.text = wired == total ? $"({total})" : $"({wired}/{total})";
                badge.tooltip = wired == total
                    ? string.Empty
                    : $"{wired} of {total} listeners have a function assigned.";
            }

            // What the cards were last drawn from — the external-resize sync compares against it so the
            // widget's own +/Delete/Duplicate (which already rebuilt) never trigger a second rebuild.
            int renderedCallCount = -1;

            void RebuildBody()
            {
                body.Clear();
                var calls = Calls();
                renderedCallCount = calls != null ? calls.arraySize : -1;
                if (calls == null) return;

                if (calls.arraySize == 0)
                {
                    var empty = new Label("No listeners yet — press + to add one.");
                    empty.AddToClassList("sasq-uevent-empty");
                    body.Add(empty);
                }
                else
                {
                    for (int i = 0; i < calls.arraySize; i++)
                        body.Add(BuildCard(i));
                }
                UpdateBadge();
            }

            // After an in-place reorder (no full rebuild) each card ELEMENT stays in its DOM slot while the DATA
            // at that slot changes. Deterministically re-sync each card's dynamic bits to the listener now at its
            // slot — relying on TrackPropertyValue here is unreliable because the drag reparents the card (its
            // trackers re-baseline across the detach and can miss the change). Target ObjectFields reflow on their
            // own via BindProperty; the arg field is rebuilt only when its slot's call mode actually changed.
            void SyncCardsAfterReorder()
            {
                var calls = Calls();
                if (calls == null) return;
                int i = 0;
                foreach (var card in body.Children())
                {
                    if (!card.ClassListContains("sasq-uevent-card")) continue;
                    if (i >= calls.arraySize) break;

                    var callProp = calls.GetArrayElementAtIndex(i);
                    var tProp = callProp.FindPropertyRelative("m_Target");
                    var mNameProp = callProp.FindPropertyRelative("m_MethodName");
                    var modeP = callProp.FindPropertyRelative("m_Mode");
                    var csProp = callProp.FindPropertyRelative("m_CallState");

                    UpdateCardStateClass(card, csProp.enumValueIndex);
                    UpdateCardWiredClass(card, tProp, mNameProp);

                    var mb = card.Q<Button>(className: "sasq-uevent-method");
                    if (mb != null) PaintMethodButton(mb, tProp, mNameProp, modeP);

                    // Q walks the whole subtree, so this still finds the clear control now that it lives
                    // INSIDE the method button rather than beside it.
                    var cb = card.Q<Button>(className: "sasq-uevent-clear");
                    if (cb != null) cb.style.display = HasFunction(tProp, mNameProp) ? DisplayStyle.Flex : DisplayStyle.None;

                    var argSlot = card.Q<VisualElement>(className: "sasq-uevent-argslot");
                    if (argSlot != null)
                    {
                        string signature = ArgSignature(callProp);
                        if (!(argSlot.userData is string prev) || prev != signature)
                            FillArgSlot(argSlot, callProp);
                    }
                    i++;
                }
                UpdateBadge();
            }

            VisualElement BuildCard(int index)
            {
                var calls = Calls();
                var callProp = calls.GetArrayElementAtIndex(index);
                var targetProp = callProp.FindPropertyRelative("m_Target");
                var methodProp = callProp.FindPropertyRelative("m_MethodName");
                var modeProp = callProp.FindPropertyRelative("m_Mode");
                var callStateProp = callProp.FindPropertyRelative("m_CallState");
                var argsProp = callProp.FindPropertyRelative("m_Arguments");

                var card = new VisualElement();
                card.AddToClassList("sasq-uevent-card");
                if (compact) card.AddToClassList("sasq-uevent-card--compact");
                card.userData = index;

                // A card OUTLIVES ITS DATA for one refresh pass. When the listener array shrinks from
                // outside, TrackedRefresh.RefreshSubtree snapshots every recorded refresher in the subtree
                // UP FRONT and then invokes them — so the refreshers belonging to cards past the new end run
                // before the rebuild has a chance to remove them, and each one reads a SerializedProperty
                // whose slot no longer resolves. Unity logs that natively ("type is not a supported pptr
                // value"), which no try/catch can intercept: MEASURED at 8 errors for a 3→1 shrink.
                //
                // Every per-card tracker goes through this wrapper so a future one cannot forget the guard.
                // Cheap: one arraySize read per fire.
                void TrackCard(VisualElement owner, SerializedProperty watched, Action<SerializedProperty> onChanged)
                {
                    owner.TrackPropertyValueWithRefresh(watched, changed =>
                    {
                        try
                        {
                            var live = Calls();
                            if (live == null || index >= live.arraySize) return; // slot gone — the rebuild replaces this card
                        }
                        catch { return; } // SerializedObject died with its inspector
                        onChanged(changed);
                    });
                }

                // Left-edge color bar reflects the call-state at a glance; keep it honest on undo/multi-edit.
                // An UNWIRED listener suppresses it entirely (see UpdateCardWiredClass) — a call state on a
                // listener with no function is a status light on something that can never run.
                UpdateCardStateClass(card, callStateProp.enumValueIndex);
                TrackCard(card, callStateProp, p => UpdateCardStateClass(card, p.enumValueIndex));

                // Optional designer card background — applies to EVERY listener card when overridden.
                if (node != null && node.UnityEventCardColor.isOverridden)
                    card.style.backgroundColor = node.UnityEventCardColor.value;

                // Row 1: [reorder grip] target ObjectField + gear options + delete
                var row1 = new VisualElement();
                row1.AddToClassList("sasq-uevent-row");

                // Same drag handle the collection lists use (device-pixel-snapped GripGlyph, hover tint).
                var grip = LayoutTreeBuilder.MakeReorderGrip();
                row1.Add(grip);

                var targetField = new ObjectField { objectType = typeof(UnityEngine.Object), allowSceneObjects = true };
                targetField.AddToClassList("sasq-uevent-target");
                targetField.style.flexGrow = 1;
                targetField.BindProperty(targetProp);
                row1.Add(targetField);

                var gearBtn = IconButton(SasquatchIcons.Ids.Settings, "Listener options (call state, reorder, duplicate)");
                gearBtn.clicked += () => OpenListenerMenu(gearBtn, card, index);
                row1.Add(gearBtn);

                // The card carries two remove-ish controls and they used to be identically neutral, so nothing
                // said which one was the big one. Only the whole-listener destroyer reddens on hover.
                var delBtn = IconButton(SasquatchIcons.Ids.Trash2, "Delete this listener entirely");
                MakeDangerOnHover(delBtn);
                delBtn.clicked += () => Delete(index);
                row1.Add(delBtn);

                card.Add(row1);

                // Row 2: searchable method picker (+ inline static argument when the mode has one)
                var row2 = new VisualElement();
                row2.AddToClassList("sasq-uevent-row");

                var methodBtn = new Button();
                methodBtn.AddToClassList("sasq-uevent-method");
                methodBtn.style.flexGrow = 3;
                BuildMethodButtonContent(methodBtn);

                // Unassigns the function in one click, instead of reopening the picker to find "No Function".
                //
                // It sits INSIDE the function well, the way a search field's clear button does. As a sibling
                // after the button it landed in the same right-hand column as the card's Trash, at the same
                // weight, and read as the same class of action — it was mistaken for "remove this listener"
                // (which is what a ✕ means nearly everywhere else). Inside the well its scope is self-evident:
                // it clears the thing it is sitting in. Unlink rather than ✕ for the same reason — this
                // UNBINDS a function, it does not destroy the listener.
                var clearBtn = IconButton(SasquatchIcons.Ids.Unlink, "Clear this function — keeps the object above");
                clearBtn.AddToClassList("sasq-uevent-clear");
                clearBtn.style.width = 16;
                clearBtn.style.height = 14;
                clearBtn.style.marginLeft = 4;
                clearBtn.style.marginRight = 1;
                // A Button nested in a Button. Clickable acts on PointerDown/Up, so IconButton's ClickEvent
                // stop is NOT enough on its own — the press would still bubble out and open the picker. These
                // run after Button's own ctor-registered Clickable callbacks (same element, same phase,
                // registration order), so the clear still fires and the outer button never sees the press.
                clearBtn.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                clearBtn.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
                clearBtn.clicked += () =>
                {
                    ClearMethod(Calls().GetArrayElementAtIndex(index), keepTarget: true);
                    serializedObject.ApplyModifiedProperties();
                    RebuildBody();
                };

                // Before the caret, so the well reads "[ name … clear ▾ ]" — the caret stays the button's
                // rightmost element, since it is what says the whole well opens a menu.
                var caretLbl = methodBtn.Q<Label>(className: "sasq-uevent-method-caret");
                methodBtn.Insert(caretLbl != null ? methodBtn.IndexOf(caretLbl) : methodBtn.childCount, clearBtn);

                void RefreshMethodLabel()
                {
                    PaintMethodButton(methodBtn, targetProp, methodProp, modeProp);
                    UpdateCardWiredClass(card, targetProp, methodProp);
                    clearBtn.style.display = HasFunction(targetProp, methodProp) ? DisplayStyle.Flex : DisplayStyle.None;
                    UpdateBadge();
                }
                RefreshMethodLabel();
                TrackCard(methodBtn, methodProp, _ => RefreshMethodLabel());
                TrackCard(methodBtn, targetProp, _ => RefreshMethodLabel());
                methodBtn.clicked += () => OpenMethodPicker(methodBtn, index, dynamicArgs, dynamicLabel, RebuildBody);
                row2.Add(methodBtn);

                // Arg slot: the static-argument field. Tagged (+ its built mode stored in userData) so the
                // post-reorder resync rebuilds it only when the listener now at this slot needs a different
                // field type. TrackPropertyValue covers non-reorder mode changes (undo / multi-edit); reorder
                // goes through SyncCardsAfterReorder because the drag's reparenting makes the tracker unreliable.
                var argSlot = new VisualElement();
                argSlot.AddToClassList("sasq-uevent-argslot");
                argSlot.style.flexDirection = FlexDirection.Row;
                FillArgSlot(argSlot, callProp);
                TrackCard(card, modeProp, _ => FillArgSlot(argSlot, callProp));
                // Picking a different Object-argument method rewrites m_ObjectArgumentAssemblyTypeName without
                // touching the mode, so the slot has to re-evaluate on the METHOD too.
                TrackCard(card, methodProp, _ => FillArgSlot(argSlot, callProp));
                row2.Add(argSlot);

                card.Add(row2);

                // Drag the grip to reorder listeners — reuses the live-inspector collection reorder (float +
                // placeholder + spring settle), committing via MoveArrayElement on drop. NO full rebuild (so no
                // PopIn fade / style jump): target ObjectFields reflow via BindProperty, and SyncCardsAfterReorder
                // deterministically re-syncs each card's method label, color bar, and arg field to its new
                // listener — TrackPropertyValue alone is unreliable across the drag's reparenting.
                CollectionRowReorder.Attach(grip, card, body, Calls(), serializedObject, SyncCardsAfterReorder);

                if (EditorSpringFX.MotionEnabled) EditorSpringFX.PopIn(card, 0.96f, 0.35f);
                return card;
            }

            // Returns the typed static-argument field directly (no label/wrapper); the caller sizes it
            // inline on Row 2. Returns null for Void / EventDefined (dynamic) modes, which have no arg.
            VisualElement BuildArgField(SerializedProperty callProp)
            {
                var mode = (PersistentListenerMode)callProp.FindPropertyRelative("m_Mode").enumValueIndex;
                var argsProp = callProp.FindPropertyRelative("m_Arguments");

                switch (mode)
                {
                    case PersistentListenerMode.Int:
                    {
                        // An int parameter that declares (or infers) an enum draws as a dropdown. The stored
                        // bytes are unchanged — still m_IntArgument under mode Int — so the listener fires
                        // exactly as before and Unity's own drawer still opens it as a number.
                        var enumType = ResolveCallEnumType(callProp);
                        if (enumType != null) return BuildEnumArgField(enumType, argsProp);

                        var f = new IntegerField();
                        f.BindProperty(argsProp.FindPropertyRelative("m_IntArgument"));
                        return f;
                    }
                    case PersistentListenerMode.Float:
                    {
                        var f = new FloatField();
                        f.BindProperty(argsProp.FindPropertyRelative("m_FloatArgument"));
                        return f;
                    }
                    case PersistentListenerMode.String:
                    {
                        var f = new TextField();
                        f.BindProperty(argsProp.FindPropertyRelative("m_StringArgument"));
                        return f;
                    }
                    case PersistentListenerMode.Bool:
                    {
                        var f = new Toggle();
                        f.BindProperty(argsProp.FindPropertyRelative("m_BoolArgument"));
                        return f;
                    }
                    case PersistentListenerMode.Object:
                    {
                        var f = new ObjectField { allowSceneObjects = true };
                        var typeName = argsProp.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue;
                        f.objectType = ResolveType(typeName) ?? typeof(UnityEngine.Object);
                        f.BindProperty(argsProp.FindPropertyRelative("m_ObjectArgument"));
                        return f;
                    }
                    default:
                        return null; // Void / EventDefined (dynamic) — no static argument
                }
            }

            // The slot's identity is the MODE **plus the argument's concrete type**, not the mode alone.
            // Switching between two Object-mode methods that take different types (Highlight(GameObject) ->
            // Highlight(Material)) leaves the mode untouched, so a mode-only key would keep the old
            // ObjectField and go on filtering the picker to the wrong type. Enum arguments have the same
            // problem one level deeper: two Int-mode calls can want two different dropdowns — or a dropdown
            // and a plain number box — with the mode identical in every case.
            //
            // WIRED-NESS is part of the key too, and not as a nicety: ClearMethod parks m_Mode at EventDefined,
            // so an unwired listener and a live dynamic one BOTH read as mode 3. A mode-only key hashes them
            // identically, and SyncCardsAfterReorder — which skips the rebuild when the key matches — would
            // leave the "dynamic" tag sitting on a card that lost its function during the drag.
            string ArgSignature(SerializedProperty callProp)
            {
                var mode = (PersistentListenerMode)callProp.FindPropertyRelative("m_Mode").enumValueIndex;
                string objType = mode == PersistentListenerMode.Object
                    ? callProp.FindPropertyRelative("m_Arguments")
                              .FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue
                    : string.Empty;
                string enumType = mode == PersistentListenerMode.Int
                    ? ResolveCallEnumType(callProp)?.FullName ?? string.Empty
                    : string.Empty;
                return $"{(int)mode}:{objType}:{enumType}:{(IsDynamicCall(callProp) ? "d" : "")}";
            }

            // Builds the typed arg field into the slot for this call, recording the signature in userData so
            // callers can skip a rebuild when it hasn't changed (the existing field's BindProperty reflows the
            // value alone).
            void FillArgSlot(VisualElement argSlot, SerializedProperty callProp)
            {
                argSlot.Clear();
                argSlot.userData = ArgSignature(callProp);
                var argEl = BuildArgField(callProp);
                if (argEl != null)
                {
                    argEl.style.flexGrow = 1;
                    argSlot.style.flexGrow = 2;
                    argSlot.style.marginLeft = 4;
                    argSlot.Add(argEl);
                    return;
                }

                // No static field — and TWO very different listeners land here. A dynamic call (the event
                // hands its own value over at invoke time) and a static call to a parameterless method both
                // left an identical empty gap, so the card never said which one you were looking at; Unity's
                // stock drawer doesn't either. Tagging the dynamic one turns this column into a single
                // question with a single answer: where does the argument come from — a box you type in, or
                // the event. The blank that remains then means exactly one thing (this method takes nothing),
                // which needs no tag of its own; labelling it too would just re-add the noise.
                if (IsDynamicCall(callProp))
                {
                    var tag = new Label(DynamicTagText);
                    tag.AddToClassList(DynamicTagClass);
                    // Inline, straight off the token the PICKER's dynamic column paints with, so the two
                    // surfaces cannot drift apart — the picker is where the reader learns that blue means
                    // "the event supplies it", and this tag is only worth anything if it is the same blue.
                    // Same reasoning (and same mechanism) as the broken-listener amber on the name label.
                    tag.style.color = WidgetPalette.AccentIcon;
                    tag.tooltip = string.IsNullOrEmpty(dynamicLabel)
                        ? "This argument is supplied by the event when it fires."
                        : $"The event supplies this listener's {dynamicLabel} when it fires.";
                    // Content-width, not the arg field's 2/5 column: the method well is the thing worth the
                    // space, and a short tag stretched across a fifth of the row reads as a broken field.
                    argSlot.style.flexGrow = 0;
                    argSlot.style.marginLeft = 4;
                    argSlot.Add(tag);
                    return;
                }

                argSlot.style.flexGrow = 0;
                argSlot.style.marginLeft = 0;
            }

            void OpenMethodPicker(Button anchor, int index, Type[] dynArgs, string dynLabel, Action rebuild)
            {
                var callProp = Calls().GetArrayElementAtIndex(index);
                var target = callProp.FindPropertyRelative("m_Target").objectReferenceValue;
                if (target == null) return;

                var entries = UnityEventReflection.GetMethodCandidates(target, dynArgs);

                // Reopening a configured listener lands ON its function (highlighted, scrolled to, its group
                // expanded) instead of at the top of the list with nothing selected.
                int current = UnityEventReflection.FindEntryIndex(entries, target,
                    callProp.FindPropertyRelative("m_MethodName").stringValue,
                    (PersistentListenerMode)callProp.FindPropertyRelative("m_Mode").enumValueIndex);

                // Rows carry the entry INDEX, so overloads and same-typed components never have to be told
                // apart by their display string.
                UnityEventMethodPicker.Show(anchor, entries, current, dynLabel, picked =>
                {
                    // Re-fetch: serialized properties can go stale across the popup.
                    var freshCall = Calls().GetArrayElementAtIndex(index);
                    if (picked < 0 || picked >= entries.Count) ClearMethod(freshCall, keepTarget: true);
                    else ApplyMethodEntry(freshCall, entries[picked]);
                    serializedObject.ApplyModifiedProperties();
                    rebuild();
                }, codePeek);
            }

            // Gear menu: checkable call-state, plus reorder/duplicate (declutters the card to 2 rows).
            void OpenListenerMenu(Button anchor, VisualElement card, int index)
            {
                var menu = new GenericMenu();
                var callProp = Calls().GetArrayElementAtIndex(index);
                int current = Mathf.Clamp(callProp.FindPropertyRelative("m_CallState").enumValueIndex, 0, 2);

                for (int s = 0; s < CallStateOptions.Length; s++)
                {
                    int state = s; // capture
                    menu.AddItem(new GUIContent(CallStateOptions[s]), current == state, () =>
                    {
                        // Re-fetch: serialized properties can go stale across the menu delay.
                        Calls().GetArrayElementAtIndex(index).FindPropertyRelative("m_CallState").enumValueIndex = state;
                        serializedObject.ApplyModifiedProperties();
                        UpdateCardStateClass(card, state);
                    });
                }

                menu.AddSeparator("");

                if (index > 0) menu.AddItem(new GUIContent("Move Up"), false, () => Move(index, -1));
                else menu.AddDisabledItem(new GUIContent("Move Up"));

                if (index < Calls().arraySize - 1) menu.AddItem(new GUIContent("Move Down"), false, () => Move(index, 1));
                else menu.AddDisabledItem(new GUIContent("Move Down"));

                menu.AddItem(new GUIContent("Duplicate"), false, () => Duplicate(index));

                menu.DropDown(anchor.worldBound);
            }

            // ---- Structural ops ----
            void AddCall()
            {
                var calls = Calls();
                int i = calls.arraySize;
                calls.arraySize++;
                ClearMethod(calls.GetArrayElementAtIndex(i));
                calls.GetArrayElementAtIndex(i).FindPropertyRelative("m_CallState").enumValueIndex = (int)UnityEventCallState.RuntimeOnly;
                serializedObject.ApplyModifiedProperties();
                RebuildBody();
            }
            void Duplicate(int index)
            {
                Calls().InsertArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                RebuildBody();
            }
            void Delete(int index)
            {
                Calls().DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                RebuildBody();
            }
            void Move(int index, int dir)
            {
                var calls = Calls();
                int j = index + dir;
                if (j < 0 || j >= calls.arraySize) return;
                calls.MoveArrayElement(index, j);
                serializedObject.ApplyModifiedProperties();
                RebuildBody();
            }

            addBtn.clicked += AddCall;

            // Header expand/collapse
            header.RegisterCallback<ClickEvent>(_ =>
            {
                expanded = !expanded;
                chevron.text = expanded ? "▾" : "▸";
                EditorSpringFX.Fold(body, expanded);
            });

            RebuildBody();

            // An external change to the listener array (undo of an add/delete, a paste, a script mutation,
            // another inspector on the same object) has to REBUILD the cards, not just relabel the badge.
            // Each card closes over its slot index and re-fetches `Calls().GetArrayElementAtIndex(index)` on
            // every action, so a shrunk array leaves cards addressing out-of-range slots (Delete/gear/picker
            // throw) and a reordered one leaves them addressing the WRONG listener — a Delete that silently
            // removes a different callback than the one you clicked.
            //
            // This routes through the collection widget's external-size sync rather than a tracker of its
            // own, because the two non-obvious parts are already solved there and MEASURED: a
            // TrackPropertyValue on the PARENT (`onTest`) does not fire for a child array's resize at all,
            // and an undo restores the data outside the tracker's change-vs-baseline comparison, so it needs
            // its own Undo.undoRedoPerformed hook (released on a REAL detach, so a re-parent doesn't kill it).
            // Rebuilds only on a real SIZE change, so an ordinary value edit can't tear the cards out from
            // under a focused field.
            LayoutTreeBuilder.AttachExternalArraySizeSync(root, Calls(), designMode: false,
                renderedCount: () => renderedCallCount, rebuildRowsInPlace: RebuildBody);

            // Value-only changes (a listener retargeted elsewhere) leave the count alone; keep the badge honest.
            root.TrackPropertyValueWithRefresh(prop, _ => UpdateBadge());

            return root;
        }

        // ----- helpers -----

        /// <summary>The enum a saved call's int argument really means, or null for a plain int.</summary>
        private static Type ResolveCallEnumType(SerializedProperty callProp)
        {
            var mode = (PersistentListenerMode)callProp.FindPropertyRelative("m_Mode").enumValueIndex;
            if (mode != PersistentListenerMode.Int) return null;
            return UnityEventReflection.ResolveArgEnumType(
                callProp.FindPropertyRelative("m_Target").objectReferenceValue,
                callProp.FindPropertyRelative("m_MethodName").stringValue);
        }

        /// <summary>
        /// The dropdown for an enum-backed int argument — a mask field for a <c>[Flags]</c> enum, a single pick
        /// otherwise.
        ///
        /// HAND-WIRED, not BindProperty'd: binding an enum control needs an enum-typed SerializedProperty and
        /// <c>m_IntArgument</c> is a plain int, which is exactly the point — writing back through it keeps
        /// Unity's own drawer and the runtime invoke path on identical data.
        /// </summary>
        private static VisualElement BuildEnumArgField(Type enumType, SerializedProperty argsProp)
        {
            var intProp = argsProp.FindPropertyRelative("m_IntArgument");
            bool flags = UnityEventReflection.IsFlagsEnum(enumType);

            // Seed with a DEFINED member, then push the real value in: a legacy listener (or one whose enum has
            // since had members removed) can hold a value the type no longer names, and constructing the field
            // straight from that is not a supported shape.
            Enum seed = UnityEventReflection.FirstEnumValue(enumType);
            BaseField<Enum> field = flags ? new EnumFlagsField(seed) : (BaseField<Enum>)new EnumField(seed);
            field.SetValueWithoutNotify(UnityEventReflection.ToEnumValue(enumType, intProp.intValue));

            field.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == null) return;
                intProp.intValue = UnityEventReflection.EnumToInt(evt.newValue);
                intProp.serializedObject.ApplyModifiedProperties();
            });

            // Nothing reflows a hand-wired control for us, so undo / multi-edit / an external write have to be
            // pushed back in explicitly.
            field.TrackPropertyValueWithRefresh(intProp,
                p => field.SetValueWithoutNotify(UnityEventReflection.ToEnumValue(enumType, p.intValue)));

            return field;
        }

        private static void ApplyMethodEntry(SerializedProperty callProp, UnityEventReflection.MethodEntry entry)
        {
            callProp.FindPropertyRelative("m_Target").objectReferenceValue = entry.Target;
            callProp.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue =
                UnityEventReflection.UnityStyleTypeName(entry.Target.GetType());
            callProp.FindPropertyRelative("m_MethodName").stringValue = entry.MethodName;
            callProp.FindPropertyRelative("m_Mode").enumValueIndex = (int)entry.Mode;

            var args = callProp.FindPropertyRelative("m_Arguments");
            args.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue =
                entry.Mode == PersistentListenerMode.Object && entry.ArgType != null
                    ? UnityEventReflection.UnityStyleTypeName(entry.ArgType)
                    : "";

            // A newly picked enum argument has to land on a REAL member. The int carried over from whatever
            // function this listener pointed at before is very unlikely to name one, and a dropdown opening
            // blank reads as broken. (Flags enums accept any combination, so they are left alone.)
            if (entry.ArgEnumType != null)
            {
                var intProp = args.FindPropertyRelative("m_IntArgument");
                if (!UnityEventReflection.IsDefinedEnumValue(entry.ArgEnumType, intProp.intValue))
                    intProp.intValue = UnityEventReflection.EnumToInt(
                        UnityEventReflection.FirstEnumValue(entry.ArgEnumType));
            }
        }

        /// <summary>
        /// Resets a persistent call. <paramref name="keepTarget"/> keeps the object the listener points at —
        /// which is what "No Function" and the clear-✕ want: you are unassigning the FUNCTION, not throwing
        /// away the object you just dragged in (Unity's own drawer keeps it too). Only <c>AddCall</c>, which is
        /// initialising a brand-new empty listener, wants the target gone as well.
        /// </summary>
        private static void ClearMethod(SerializedProperty callProp, bool keepTarget = false)
        {
            if (!keepTarget)
            {
                callProp.FindPropertyRelative("m_Target").objectReferenceValue = null;
                callProp.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = "";
            }
            callProp.FindPropertyRelative("m_MethodName").stringValue = "";
            callProp.FindPropertyRelative("m_Mode").enumValueIndex = (int)PersistentListenerMode.EventDefined;
            var args = callProp.FindPropertyRelative("m_Arguments");
            args.FindPropertyRelative("m_ObjectArgument").objectReferenceValue = null;
            args.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = "";
            args.FindPropertyRelative("m_IntArgument").intValue = 0;
            args.FindPropertyRelative("m_FloatArgument").floatValue = 0f;
            args.FindPropertyRelative("m_StringArgument").stringValue = "";
            args.FindPropertyRelative("m_BoolArgument").boolValue = false;
        }

        /// <summary>
        /// The method name alone, nicified ("Add Score"), or the empty-state prompt.
        ///
        /// "Select a function…" rather than Unity's "No Function": Unity's is a dropdown with obvious dropdown
        /// chrome, so a status reads fine there — ours is a recessed well, and the phrasing is what says it is
        /// clickable. The picker keeps "No Function" on its own row, where it genuinely IS a choice ("unassign").
        /// </summary>
        internal const string NoFunctionPrompt = "Select a function…";

        private static string MethodDisplay(SerializedProperty targetProp, SerializedProperty methodProp)
        {
            var target = targetProp.objectReferenceValue;
            if (target == null) return NoFunctionPrompt;
            string method = methodProp.stringValue;
            if (string.IsNullOrEmpty(method)) return NoFunctionPrompt;
            return method.StartsWith("set_", StringComparison.Ordinal)
                ? ObjectNames.NicifyVariableName(method.Substring(4))
                : ObjectNames.NicifyVariableName(method);
        }

        /// <summary>
        /// The breadcrumb prefix ("Event Picker Audio #2  ▸  "), or empty. Shown ONLY when it says something
        /// the ObjectField directly above doesn't: picking a function re-points m_Target at the component that
        /// owns it, so the field already names the type and repeating it here was the single biggest source of
        /// visual noise in the card. It survives exactly where it earns its place — several components of one
        /// type, where only the "#n" identifies which instance.
        /// </summary>
        private static string MethodPrefix(SerializedProperty targetProp, SerializedProperty methodProp)
        {
            var target = targetProp.objectReferenceValue;
            if (target == null || string.IsNullOrEmpty(methodProp.stringValue)) return string.Empty;
            if (!UnityEventReflection.TargetIsAmbiguous(target)) return string.Empty;
            return $"{UnityEventReflection.TargetDisplayName(target)}  ▸  ";
        }

        private const string MethodPrefixClass = "sasq-uevent-method-prefix";
        private const string MethodNameClass = "sasq-uevent-method-name";
        internal const string MethodNameEmptyClass = "sasq-uevent-method-name--empty";
        internal const string CardUnwiredClass = "sasq-uevent-card--unwired";

        /// <summary>
        /// Fills the function button with its three parts: a dim breadcrumb prefix (usually hidden), the bold
        /// method name, and a caret at the right edge. The caret is what says "this opens a menu" — the button
        /// otherwise shares the fill, border and height of the ObjectField above it. All three ignore picking
        /// so the Button's own Clickable still owns the row.
        /// </summary>
        private static void BuildMethodButtonContent(Button btn)
        {
            btn.text = string.Empty;

            var prefix = new Label();
            prefix.AddToClassList(MethodPrefixClass);
            prefix.pickingMode = PickingMode.Ignore;
            btn.Add(prefix);

            var name = new Label();
            name.AddToClassList(MethodNameClass);
            name.pickingMode = PickingMode.Ignore;
            btn.Add(name);

            var caret = new Label("▾");
            caret.AddToClassList("sasq-uevent-method-caret");
            caret.pickingMode = PickingMode.Ignore;
            btn.Add(caret);
        }

        /// <summary>Whether this listener actually points at something (a target AND a method name).</summary>
        private static bool HasFunction(SerializedProperty targetProp, SerializedProperty methodProp)
            => targetProp.objectReferenceValue != null && !string.IsNullOrEmpty(methodProp.stringValue);

        internal const string DynamicTagClass = "sasq-uevent-dynamic";
        /// <summary>Lowercase and unbracketed, matching the picker's "Dynamic" section — one vocabulary for
        /// one concept. Blue + bold already does the work of setting it apart; brackets would only make it
        /// read as a placeholder token rather than a statement about this listener.</summary>
        internal const string DynamicTagText = "dynamic";

        /// <summary>
        /// Whether this listener takes its argument FROM the event (PersistentListenerMode.EventDefined) —
        /// what the card tags "dynamic".
        ///
        /// The HasFunction gate is load-bearing, not defensive. ClearMethod parks m_Mode at EventDefined, so
        /// EVERY blank listener stores the dynamic mode: a fresh "+", and one you just unlinked. Reading the
        /// mode alone would hang a confident blue "dynamic" beside "Select a function…" — a status badge on a
        /// listener that does nothing, which is precisely the false signal the card's status stripe carried
        /// until it was gated on wired too.
        /// </summary>
        private static bool IsDynamicCall(SerializedProperty callProp)
        {
            if (!HasFunction(callProp.FindPropertyRelative("m_Target"),
                             callProp.FindPropertyRelative("m_MethodName"))) return false;
            return (PersistentListenerMode)callProp.FindPropertyRelative("m_Mode").enumValueIndex
                   == PersistentListenerMode.EventDefined;
        }

        /// <summary>
        /// Paints the method button: its label, its enabled state, and — the point of this helper — whether the
        /// saved call still RESOLVES. A renamed or deleted method leaves m_MethodName behind, which would
        /// otherwise render a dead listener exactly like a healthy one. Broken reads amber with a tooltip
        /// naming the member; the label keeps the stale name, since that is the clue to what it pointed at.
        ///
        /// Called from the two places that re-derive a card's method label: RefreshMethodLabel (build + property
        /// trackers) and SyncCardsAfterReorder (the in-place drag resync, where trackers are unreliable).
        /// </summary>
        private static void PaintMethodButton(Button btn, SerializedProperty targetProp,
            SerializedProperty methodProp, SerializedProperty modeProp)
        {
            // Two-tone content lives in CHILD labels, not the Button's own text: the method name has to
            // out-weigh its breadcrumb, and a Button draws one uniform run.
            var prefixLbl = btn.Q<Label>(className: MethodPrefixClass);
            var nameLbl = btn.Q<Label>(className: MethodNameClass);
            string prefix = MethodPrefix(targetProp, methodProp);

            prefixLbl.text = prefix;
            prefixLbl.style.display = string.IsNullOrEmpty(prefix) ? DisplayStyle.None : DisplayStyle.Flex;
            nameLbl.text = MethodDisplay(targetProp, methodProp);
            btn.SetEnabled(targetProp.objectReferenceValue != null);

            // An empty listener used to render its emptiness in the LOUDEST voice the card has — bold, at the
            // brightest text token — so at a glance it weighed exactly as much as a real function. Drop it to
            // the dim italic the "No listeners yet" line already uses: bold+bright means content, dim+italic
            // means placeholder, and the eye sorts the two on its first pass without reading either.
            nameLbl.EnableInClassList(MethodNameEmptyClass, !HasFunction(targetProp, methodProp));

            bool broken = !UnityEventReflection.IsMethodResolvable(
                targetProp.objectReferenceValue,
                methodProp.stringValue,
                (PersistentListenerMode)modeProp.enumValueIndex);

            // Amber lands on the NAME (the thing that no longer resolves), not the whole button.
            nameLbl.style.color = broken
                ? new StyleColor(WidgetPalette.WarningCalloutText)
                : new StyleColor(StyleKeyword.Null);   // back to whatever the stylesheet says
            btn.tooltip = broken
                ? $"'{methodProp.stringValue}' no longer exists on {targetProp.objectReferenceValue.GetType().Name} " +
                  "(renamed, deleted, or its signature changed) — pick a function again."
                : string.Empty;
        }

        // Swaps the card's call-state modifier class, which paints the left-edge color bar.
        private static void UpdateCardStateClass(VisualElement card, int stateIndex)
        {
            card.RemoveFromClassList("sasq-uevent-card--state-off");
            card.RemoveFromClassList("sasq-uevent-card--state-editor-runtime");
            card.RemoveFromClassList("sasq-uevent-card--state-runtime-only");
            // UnityEventCallState: Off=0, EditorAndRuntime=1, RuntimeOnly=2 (matches CallStateOptions order).
            card.AddToClassList(stateIndex switch
            {
                1 => "sasq-uevent-card--state-editor-runtime",
                2 => "sasq-uevent-card--state-runtime-only",
                _ => "sasq-uevent-card--state-off",
            });
        }

        /// <summary>
        /// Flags a listener that has no function on it, which drops the card's status stripe entirely (the USS
        /// rule paints border-left back to the card's own border colour).
        ///
        /// So: stripe present = this listener does something. That is readable peripherally at a glance, needs
        /// no colour vocabulary, survives colour blindness, and leaves the three call-state hues meaning exactly
        /// what they meant before — they just stop appearing on listeners that can never fire. Deliberately NOT
        /// the amber a broken listener wears: an empty listener you added two seconds ago is not an error, and
        /// warning-colouring a normal intermediate state is how people learn to ignore warnings.
        ///
        /// Note the class only touches border-left-color, never the fill — UnityEventCardColor writes the card
        /// background as an INLINE style, which beats any stylesheet, so a background-based signal would vanish
        /// for anyone who tinted their cards in the designer.
        /// </summary>
        private static void UpdateCardWiredClass(VisualElement card,
            SerializedProperty targetProp, SerializedProperty methodProp)
            => card.EnableInClassList(CardUnwiredClass, !HasFunction(targetProp, methodProp));

        /// <summary>
        /// Reddens an icon button's glyph while the pointer is on it, marking the control that destroys
        /// something outright. USS cannot do this: SasquatchIconElement paints from its Tint, so a `color`
        /// rule on the button has no effect. Resting tint is re-read from the palette on leave rather than
        /// cached, so a theme flip mid-session cannot strand the glyph on the old skin's grey.
        /// </summary>
        private static void MakeDangerOnHover(Button btn)
        {
            var glyph = btn.Q<SasquatchIconElement>();
            if (glyph == null) return;
            btn.RegisterCallback<PointerEnterEvent>(_ => glyph.Tint = WidgetPalette.DangerText);
            btn.RegisterCallback<PointerLeaveEvent>(_ => glyph.Tint = SasquatchIcons.DefaultTint);
        }

        private static Button IconButton(string iconId, string tooltip)
        {
            var b = new Button();
            b.AddToClassList("sasq-uevent-iconbtn");
            if (iconId != null) b.Add(SasquatchIcons.Create(iconId, 11));
            b.tooltip = tooltip;
            b.style.width = 20;
            b.style.height = 18;
            b.style.paddingLeft = 0; b.style.paddingRight = 0; b.style.paddingTop = 0; b.style.paddingBottom = 0;
            b.style.marginLeft = 2; b.style.marginRight = 0; b.style.marginTop = 0; b.style.marginBottom = 0;
            b.style.flexShrink = 0;
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.Center;
            b.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            return b;
        }

        private static Type ResolveType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            var t = Type.GetType(typeName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(typeName);
                if (t != null) return t;
            }
            // typeName may be "FullName, Assembly" — try the FullName part across assemblies.
            int comma = typeName.IndexOf(',');
            if (comma > 0)
            {
                string full = typeName.Substring(0, comma).Trim();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(full);
                    if (t != null) return t;
                }
            }
            return null;
        }

        private static string FriendlyType(Type t) =>
            t == typeof(int) ? "int" :
            t == typeof(float) ? "float" :
            t == typeof(string) ? "string" :
            t == typeof(bool) ? "bool" : t.Name;
    }
}
