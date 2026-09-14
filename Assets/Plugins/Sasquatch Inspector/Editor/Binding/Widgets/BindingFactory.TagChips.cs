using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Tag Chips widget: edits a whole string collection (<c>string[]</c> / <c>List&lt;string&gt;</c>) as a
    /// wrapping row of editable/removable chips plus a "+" that trails the last chip and adds a new chip you
    /// type straight into. Bound to the ARRAY property (not an element), so it mutates
    /// <see cref="SerializedProperty"/> array structure directly (insert/delete). Falls back to a plain field
    /// for any non-string-array property (fuzzer-safe).
    /// </summary>
    public static partial class BindingFactory
    {
        internal static VisualElement CreateTagChips(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            bool isStringArray = prop.propertyType == SerializedPropertyType.Generic
                                 && prop.isArray && prop.arrayElementType == "string";
            if (!isStringArray)
                return new PropertyField(prop) { label = resolvedLabel };

            var wrapper = new VisualElement();
            wrapper.AddToClassList("unity-base-field");

            var labelEl = new Label(resolvedLabel);
            labelEl.AddToClassList("unity-base-field__label");
            labelEl.AddToClassList("unity-label");
            if (string.IsNullOrEmpty(resolvedLabel)) labelEl.style.display = DisplayStyle.None;
            wrapper.Add(labelEl);

            // Chips cluster left and wrap; the round "+" rides INSIDE the wrap row, trailing the last
            // chip (re-appended by every rebuild). Parked at the input column's own right edge instead, the
            // growing chip row pushes it hundreds of px from the chips at inspector widths, which reads as
            // "there is no add button".
            var input = new VisualElement();
            input.AddToClassList("unity-base-field__input");
            input.style.flexDirection = FlexDirection.Row;
            input.style.alignItems = Align.FlexStart;
            wrapper.Add(input);

            var chipRow = new VisualElement();
            chipRow.style.flexDirection = FlexDirection.Row;
            chipRow.style.flexWrap = Wrap.Wrap;
            chipRow.style.alignItems = Align.Center;
            chipRow.style.flexGrow = 1;
            input.Add(chipRow);

            Button addBtn = null; // pre-declared: RebuildChips re-appends it, and its own click handler calls RebuildChips
            addBtn = new Button(() =>
            {
                prop.serializedObject.Update();
                int n = prop.arraySize;
                prop.InsertArrayElementAtIndex(n);
                prop.GetArrayElementAtIndex(n).stringValue = "";
                prop.serializedObject.ApplyModifiedProperties();
                RebuildChips(editIndex: n);   // add an empty chip and immediately edit it
            })
            { text = "+" };
            addBtn.tooltip = "Add a tag";
            addBtn.style.width = 22; addBtn.style.height = 20; addBtn.style.minWidth = 22;
            addBtn.style.paddingLeft = 0; addBtn.style.paddingRight = 0;
            addBtn.style.marginLeft = 2; addBtn.style.marginTop = 0;
            addBtn.style.marginBottom = 3; // matches the chips' wrap-row bottom margin
            addBtn.style.flexShrink = 0;
            addBtn.style.borderTopLeftRadius = 10; addBtn.style.borderTopRightRadius = 10;
            addBtn.style.borderBottomLeftRadius = 10; addBtn.style.borderBottomRightRadius = 10;

            // A snapshot of the array as last rendered. TrackPropertyValue rebuilds ONLY when the property differs
            // from this — so our OWN writes (add / delete / in-place edit) never rebuild the row out from under a
            // chip you just clicked to edit next. A full rebuild on commit destroys the chip you clicked before
            // its click can open it, so swapping between edits would take two clicks.
            string[] lastKnown = null;
            string[] Snapshot()
            {
                prop.serializedObject.Update();
                if (MixedValueUI.IsMixed(prop)) return null;
                var arr = new string[prop.arraySize];
                for (int i = 0; i < prop.arraySize; i++) arr[i] = prop.GetArrayElementAtIndex(i).stringValue;
                return arr;
            }
            bool SameAsLast(string[] a)
            {
                if (a == null || lastKnown == null) return a == lastKnown;
                if (a.Length != lastKnown.Length) return false;
                for (int i = 0; i < a.Length; i++) if (a[i] != lastKnown[i]) return false;
                return true;
            }

            void RebuildChips(int editIndex = -1)
            {
                chipRow.Clear(); // also detaches the "+", re-appended at the end of every path below
                prop.serializedObject.Update();
                if (MixedValueUI.IsMixed(prop))
                {
                    var mixedLabel = new Label(MixedValueUI.Dash);
                    mixedLabel.style.color = WidgetPalette.Mixed;
                    chipRow.Add(mixedLabel);
                    chipRow.Add(addBtn);
                    lastKnown = null;
                    return;
                }

                var editStarters = new List<System.Action>();

                for (int i = 0; i < prop.arraySize; i++)
                {
                    int index = i;
                    string value = prop.GetArrayElementAtIndex(i).stringValue;

                    var chip = new VisualElement();
                    chip.style.flexDirection = FlexDirection.Row;
                    chip.style.alignItems = Align.Center;
                    chip.style.backgroundColor = WidgetPalette.ControlBg;
                    chip.style.borderTopLeftRadius = 9;
                    chip.style.borderTopRightRadius = 9;
                    chip.style.borderBottomLeftRadius = 9;
                    chip.style.borderBottomRightRadius = 9;
                    chip.style.paddingLeft = 8;
                    chip.style.paddingRight = 4;
                    chip.style.paddingTop = 1;
                    chip.style.paddingBottom = 1;
                    chip.style.marginRight = 4;
                    chip.style.marginBottom = 3;
                    // Hover feedback: a subtle accent-tinted background + a springy scale lift.
                    Color chipRest = WidgetPalette.ControlBg;
                    Color chipHover = Color.Lerp(chipRest, WidgetPalette.Accent, 0.30f);
                    chip.RegisterCallback<PointerEnterEvent>(_ => chip.style.backgroundColor = chipHover);
                    chip.RegisterCallback<PointerLeaveEvent>(_ => chip.style.backgroundColor = chipRest);
                    EditorSpringFX.HoverScale(chip, 1.06f);

                    var text = new Label(string.IsNullOrEmpty(value) ? "—" : value);
                    text.style.fontSize = 11;
                    text.style.color = WidgetPalette.LabelPrimary;
                    text.tooltip = "Click to edit";

                    var remove = new Label("✕");
                    remove.style.fontSize = 10;
                    remove.style.marginLeft = 5;
                    remove.style.color = WidgetPalette.LabelSecondary;
                    remove.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        prop.serializedObject.Update();
                        if (index < prop.arraySize)
                        {
                            prop.DeleteArrayElementAtIndex(index);
                            prop.serializedObject.ApplyModifiedProperties();
                        }
                        RebuildChips();
                        evt.StopPropagation();
                    });

                    // Edit in place: swap the label for a FIXED-width field (long tags scroll → no resize jank
                    // while typing) and commit on Enter/blur WITHOUT rebuilding, so clicking another chip to edit
                    // it (which blurs+commits this one) leaves that chip intact for the click.
                    bool editing = false;
                    System.Action enterEdit = () =>
                    {
                        if (editing) return;
                        editing = true;
                        string cur = index < prop.arraySize ? prop.GetArrayElementAtIndex(index).stringValue : value;
                        var edit = new TextField { value = cur, isDelayed = true };
                        edit.style.fontSize = 11;
                        edit.style.width = 100;
                        edit.style.marginTop = 0; edit.style.marginBottom = 0;
                        int slot = chip.IndexOf(text);
                        text.style.display = DisplayStyle.None;
                        chip.Insert(slot, edit);
                        edit.schedule.Execute(() => { edit.Focus(); edit.SelectAll(); }).StartingIn(0);

                        bool done = false;
                        void Commit()
                        {
                            if (done) return;
                            done = true;
                            string nv = (edit.value ?? "").Trim();
                            prop.serializedObject.Update();
                            if (index >= prop.arraySize) { RebuildChips(); return; }
                            if (string.IsNullOrEmpty(nv))
                            {
                                // Cleared → remove the chip (structural change → rebuild).
                                prop.DeleteArrayElementAtIndex(index);
                                prop.serializedObject.ApplyModifiedProperties();
                                RebuildChips();
                                return;
                            }
                            prop.GetArrayElementAtIndex(index).stringValue = nv;
                            prop.serializedObject.ApplyModifiedProperties();
                            // In-place: update label, drop the field, restore the label — NO rebuild. Refresh the
                            // snapshot so the deferred TrackPropertyValue treats this (our own) write as a no-op.
                            text.text = nv;
                            edit.RemoveFromHierarchy();
                            text.style.display = DisplayStyle.Flex;
                            editing = false;
                            lastKnown = Snapshot();
                        }
                        edit.RegisterCallback<FocusOutEvent>(_ => Commit());
                        edit.RegisterValueChangedCallback(_ => Commit()); // isDelayed → fires on Enter/blur
                    };

                    text.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        enterEdit();
                        evt.StopPropagation();
                    });

                    chip.Add(text);
                    chip.Add(remove);
                    chipRow.Add(chip);
                    editStarters.Add(enterEdit);
                }

                chipRow.Add(addBtn); // trail the last chip, wrapping with the chips

                lastKnown = Snapshot();

                // Auto-enter edit on a freshly-added chip (from the "+" button), deferred so it's attached first.
                if (editIndex >= 0 && editIndex < editStarters.Count)
                {
                    int ei = editIndex;
                    chipRow.schedule.Execute(() => editStarters[ei]()).StartingIn(0);
                }
            }

            RebuildChips();
            // Rebuild only on EXTERNAL changes (undo, another inspector) — our own edits update lastKnown, so they
            // never trigger a rebuild that would wipe an in-progress / just-clicked chip.
            wrapper.TrackPropertyValueWithRefresh(prop, _ => { if (!SameAsLast(Snapshot())) RebuildChips(); });
            return wrapper;
        }
    }
}
