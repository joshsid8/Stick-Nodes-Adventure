using System;
using System.Collections.Generic;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The "Prefix &amp; Suffix" properties section. Affixes apply to EVERY Display Type, so unlike the
    /// per-widget option blocks this section lives outside <c>displayStyleFoldout</c> and is never touched by
    /// the Display-Type re-sync sweep — there is no visibility rule to keep in lockstep.
    /// </summary>
    public partial class SasquatchLayoutDesignerWindow
    {
        [SerializeField] private bool _affixExpanded;
        // Split by TASK, not by side: writing an affix (text / icon / placement) is a different job from
        // styling one, and the styling half is five rows per side that are almost always left at default.
        // Content sections open, styling closed — 16 flat rows became 6 rows and three headers.
        [SerializeField] private bool _affixPrefixExpanded = true;
        [SerializeField] private bool _affixSuffixExpanded = true;
        [SerializeField] private bool _affixStylingExpanded = false;

        /// <summary>Builds the whole section for the selected binding, or null when it has no meaning here.</summary>
        /// <param name="caps">
        /// The capability set the rows are gated on. Null resolves to this node's own
        /// <see cref="FieldAffixes.CapabilitiesFor"/>. A multi-selection passes
        /// <see cref="FieldAffixes.Intersect"/> instead, so the section offers only what EVERY selected field
        /// can honour — the product rule is "honoured, or not offered", and in multi-edit the widest honest
        /// answer is the intersection.
        /// </param>
        /// <param name="extraToggleGate">
        /// Null on a single selection. In multi-edit, an extra "does every selected node qualify" test the two
        /// trailing toggles (Repeat on Each Component, Show in Column Header) must also pass — those are gated
        /// on properties of the node, and offering them because the PRIMARY qualifies would fan a setting onto
        /// followers it means nothing on.
        /// </param>
        private VisualElement BuildAffixSection(BindingNode bindingNode,
                                                FieldAffixes.AffixCapabilities? caps = null,
                                                Func<Func<BindingNode, bool>, bool> extraToggleGate = null)
        {
            if (bindingNode == null) return null;
            // The ONE capability query — shared with the quick-edit popup and the renderers, so a control is
            // never drawn here that the renderer will silently ignore. Any == false means nothing on this
            // widget can carry an affix at all (a Progress Bar with no read-out, a Slider with its value
            // field hidden), so the section does not appear.
            var resolvedCaps = caps ?? FieldAffixes.CapabilitiesFor(bindingNode, ResolveSelectedNodeFieldType(bindingNode));
            if (!resolvedCaps.Any) return null;

            var foldout = new Foldout();
            foldout.text = "Prefix & Suffix";
            foldout.value = _affixExpanded;
            foldout.style.marginTop = 10;
            foldout.style.unityFontStyleAndWeight = FontStyle.Bold;
            // Register on the foldout's OWN toggle so child toggles inside the content — whose ChangeEvent<bool>
            // also bubbles — aren't mistaken for the foldout's expand state (the pattern every section uses).
            foldout.Q<Toggle>()?.RegisterValueChangedCallback(evt => _affixExpanded = evt.newValue);
            SasquatchIcons.SkinFoldout(foldout);

            var intro = new Label(resolvedCaps.Icon
                ? "Text or an icon on either side of the field. Works on every Display Type."
                : "Text on either side of the value. This widget draws its read-out as a single line of text, "
                + "so an icon, a pill, per-side styling and placement do not apply here.");
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.unityFontStyleAndWeight = FontStyle.Normal;
            intro.style.opacity = 0.7f;
            intro.style.marginBottom = 6;
            foldout.Add(intro);

            // ONE styling section holding both sides, rather than one per side. Three reasons: it keeps the
            // nesting two deep instead of three (the section skin has two header tiers, so a third level would
            // be indistinguishable from the second); it is one thing to open instead of two; and prefix and
            // suffix styling end up ADJACENT, which is what you want when keeping the pair consistent. It also
            // means the rows keep their "Prefix …" / "Suffix …" labels, so the properties search — whose
            // haystack is the leaf label alone — still answers a two-word query like "suffix color".
            var prefixSection = CreatePersistedSection("Prefix", _affixPrefixExpanded, v => _affixPrefixExpanded = v);
            var suffixSection = CreatePersistedSection("Suffix", _affixSuffixExpanded, v => _affixSuffixExpanded = v);
            var stylingSection = CreatePersistedSection("Styling", _affixStylingExpanded, v => _affixStylingExpanded = v);

            // A collapsed section is dimmed, which would hide the fact that the user HAS styled an affix.
            // Re-derived from the model on every styling edit (each row's onValueChanged runs this), so it
            // tracks a reset back to default as well as a change away from it.
            void SyncStylingActive() => MarkSectionActive(stylingSection,
                AffixHasStyleOverride(bindingNode.PrefixAffix) || AffixHasStyleOverride(bindingNode.SuffixAffix),
                topLevel: false);

            BuildAffixSide(bindingNode, "Prefix", true, resolvedCaps, prefixSection, stylingSection, SyncStylingActive);
            BuildAffixSide(bindingNode, "Suffix", false, resolvedCaps, suffixSection, stylingSection, SyncStylingActive);
            SyncStylingActive();

            foldout.Add(prefixSection);
            foldout.Add(suffixSection);
            // Capabilities gate every styling row, so a widget that supports none of them would otherwise be
            // left with an empty "Styling" header standing over nothing.
            foldout.Add(stylingSection);
            SyncSectionVisibility(stylingSection);

            // Per-component only means anything on a field that draws several number boxes on one row.
            if (QualifiesForAffixToggle(bindingNode, extraToggleGate,
                n => IsCompositeFieldType(ResolveSelectedNodeFieldType(n))))
            {
                var perComponent = new Toggle("Repeat on Each Component");
                perComponent.tooltip = "On a Vector / Rect / Bounds field, repeat the affixes on every component box "
                                     + "(\"5 m, 2 m, 9 m\"). Off puts a single pair at the end of the whole row.";
                perComponent.value = bindingNode.AffixPerComponent;
                perComponent.style.unityFontStyleAndWeight = FontStyle.Normal;
                perComponent.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(_clonedLayoutAsset, "Toggle Affix Per Component");
                    bindingNode.AffixPerComponent = evt.newValue;
                    OnModification();
                    RefreshPreview();
                });
                foldout.Add(perComponent);
            }

            // Column-header mode is a TABLE concept; offering it on a normal row would just confuse.
            if (QualifiesForAffixToggle(bindingNode, extraToggleGate, IsInsideCollectionItemLayout))
            {
                var headerMode = new Toggle("Show in Column Header");
                headerMode.tooltip = "In a Table, show the affix ONCE in the column header (\"Weight (kg)\") instead "
                                   + "of in every cell. Ignored outside a table.";
                headerMode.value = bindingNode.AffixInColumnHeader;
                headerMode.style.unityFontStyleAndWeight = FontStyle.Normal;
                headerMode.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(_clonedLayoutAsset, "Toggle Affix In Column Header");
                    bindingNode.AffixInColumnHeader = evt.newValue;
                    OnModification();
                    RefreshPreview();
                });
                foldout.Add(headerMode);
            }

            return foldout;
        }

        /// <summary>
        /// Whether one of the two trailing affix toggles should be offered. On a single selection that is just
        /// <paramref name="test"/> against the node. In multi-edit the caller supplies
        /// <paramref name="gate"/>, which re-runs the same test across the WHOLE selection and answers only
        /// when every node passes — the toggle fans out, so offering it on the primary's say-so would write a
        /// table-only or vector-only setting onto followers that are neither.
        /// </summary>
        private static bool QualifiesForAffixToggle(BindingNode node,
                                                    Func<Func<BindingNode, bool>, bool> gate,
                                                    Func<BindingNode, bool> test)
            => gate != null ? gate(test) : test(node);

        /// <summary>True when any of this affix's five STYLE rows carries a user value rather than the default.</summary>
        private static bool AffixHasStyleOverride(FieldAffix a)
            => a.gap.isOverridden
            || a.style.color.isOverridden
            || a.style.fontSize.isOverridden
            || a.style.fontStyle.isOverridden
            || a.pillColor.isOverridden;

        /// <summary>
        /// One side of the pair. <paramref name="isPrefix"/> selects which <see cref="FieldAffix"/> it drives.
        /// Fills TWO containers: <paramref name="contentBox"/> takes what you reach for (text, icon, placement),
        /// <paramref name="stylingBox"/> takes the five appearance rows — which is the split that turned this
        /// section from a flat wall of 16 rows into something you can read.
        /// </summary>
        private void BuildAffixSide(BindingNode bindingNode, string title, bool isPrefix,
                                    FieldAffixes.AffixCapabilities caps,
                                    VisualElement contentBox, VisualElement stylingBox, Action onStyleChanged)
        {
            FieldAffix Get() => isPrefix ? bindingNode.PrefixAffix : bindingNode.SuffixAffix;
            void Set(FieldAffix a)
            {
                // FieldAffix is a struct, so every edit is get-mutate-SET — assigning through the property is
                // the only thing that writes the node (same rule as LayoutStyle / DisplayStyleData).
                if (isPrefix) bindingNode.PrefixAffix = a;
                else bindingNode.SuffixAffix = a;
            }

            // ---- text ----
            var textField = new TextField(title);
            textField.style.unityFontStyleAndWeight = FontStyle.Normal;
            textField.tooltip = isPrefix
                ? "Text shown before the value (\"$\"). Spacing is handled by Gap — no need to type your own."
                : "Text shown after the value (\"kg\", \"m/s\", \"%\"). Spacing is handled by Gap — no need to type your own.";
            textField.value = Get().text ?? "";
            textField.RegisterValueChangedCallback(evt =>
                HandleDebouncedTextEdit("Change Affix Text",
                    () => { var a = Get(); a.text = evt.newValue; Set(a); }, liveRebuild: true));
            FlushTextEditOnBlur(textField);
            contentBox.Add(textField);

            // ---- icon ----
            var iconRow = new VisualElement();
            // Carries the base-field classes even though it is hand-rolled, so it joins the panel's shared
            // input column instead of keeping its own hard-coded label width — which, now that every other row
            // lines up, would read as the one crooked row in the section. Same trick as BuildImageAlignmentRow.
            // It also promotes the row to a real search LEAF, so a query that doesn't match it now hides it
            // rather than leaving "Prefix Icon" standing over a filtered section.
            iconRow.AddToClassList("unity-base-field");
            iconRow.style.flexDirection = FlexDirection.Row;
            iconRow.style.alignItems = Align.Center;
            iconRow.style.marginBottom = 4;

            var iconCaption = new Label(title + " Icon");
            iconCaption.AddToClassList("unity-base-field__label");
            iconCaption.style.unityFontStyleAndWeight = FontStyle.Normal;
            iconRow.Add(iconCaption);

            var iconPreviewHost = new VisualElement();
            iconPreviewHost.style.width = 18;
            iconPreviewHost.style.alignItems = Align.Center;
            iconRow.Add(iconPreviewHost);

            var iconPickButton = new Button { tooltip = "Pick an icon to show beside the text (or on its own)." };
            iconPickButton.style.flexGrow = 1;
            iconRow.Add(iconPickButton);

            var iconClearButton = new Button { text = "✕", tooltip = "Remove the icon." };
            iconRow.Add(iconClearButton);

            void SyncIconRow()
            {
                string id = Get().iconId ?? "";
                bool hasIcon = !string.IsNullOrEmpty(id);
                iconPickButton.text = hasIcon ? id : "None…";
                iconClearButton.style.display = hasIcon ? DisplayStyle.Flex : DisplayStyle.None;
                iconPreviewHost.Clear();
                if (hasIcon) iconPreviewHost.Add(SasquatchIcons.Create(id, 14f));
            }

            void SetIcon(string id, string undoName)
            {
                Undo.RecordObject(_clonedLayoutAsset, undoName);
                var a = Get();
                a.iconId = id;
                Set(a);
                SyncIconRow();
                OnModification();
                RefreshPreview();
            }

            iconPickButton.clicked += () =>
            {
                var choices = new List<string>(SasquatchIcons.AllIds);
                UnityEditor.PopupWindow.Show(iconPickButton.worldBound,
                    new SearchableDropdownPopup(null, title + " Icon", choices, Get().iconId,
                        selected => SetIcon(selected, "Change Affix Icon"),
                        itemIcon: id => SasquatchIcons.Create(id, 14f), anchor: iconPickButton));
            };
            iconClearButton.clicked += () => SetIcon("", "Remove Affix Icon");
            SyncIconRow();
            if (caps.Icon) contentBox.Add(iconRow);

            // ---- placement ----
            // A widget with no text box can never honour "Inside Field", so the CHOICE is removed rather than
            // offered and quietly downgraded. Uses a plain dropdown in that case because EnumField always
            // shows every member of the enum.
            var mountField = new EnumField(title + " Placement", Get().mount);
            mountField.style.unityFontStyleAndWeight = FontStyle.Normal;
            mountField.tooltip = "Auto puts the affix inside the input box on fields that have one and beside the "
                               + "control on fields that don't. Inside Field forces the overlay; Outside Field "
                               + "always keeps it out of the box. A widget with no text box always renders outside.";
            mountField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_clonedLayoutAsset, "Change Affix Placement");
                var a = Get();
                a.mount = (AffixMount)evt.newValue;
                Set(a);
                OnModification();
                RefreshPreview();
            });
            if (caps.Placement)
            {
                if (caps.InsideMount) contentBox.Add(mountField);
                else contentBox.Add(BuildMountRowWithoutInside(title, Get, Set));
            }

            // ---- styling ----
            // Every row here refreshes the preview AND re-derives whether "Styling" still counts as customised,
            // so folding the section away never loses the fact that something inside it was set. The reset
            // button routes through this same callback, so clearing the last override dims the header again.
            void OnStyleEdited()
            {
                RefreshPreview();
                onStyleChanged?.Invoke();
            }

            // Ten near-identical rows in one list is a wall, and every label already starts with the side's
            // name — so the eye has to READ to find the boundary instead of seeing it. A caption + hairline
            // marks where Prefix ends and Suffix begins. Only emitted when this side actually contributes
            // rows: on a widget whose capabilities gate all five off, the caption would head an empty run.
            bool anyStyling = caps.Gap || caps.TextStyle || caps.Pill;
            if (anyStyling) stylingBox.Add(MakeGroupCaption(title, first: isPrefix));

            if (caps.Gap) stylingBox.Add(CreateOverridableFloatElement(title + " Gap",
                () => Get().gap,
                v => { var a = Get(); a.gap = v; Set(a); },
                OnStyleEdited, minValue: 0f));

            if (caps.TextStyle) stylingBox.Add(CreateOverridableColorElement(title + " Color",
                () => Get().style.color,
                v => { var a = Get(); var ts = a.style; ts.color = v; a.style = ts; Set(a); },
                OnStyleEdited));

            if (caps.TextStyle) stylingBox.Add(CreateOverridableFloatElement(title + " Size",
                () => Get().style.fontSize,
                v => { var a = Get(); var ts = a.style; ts.fontSize = v; a.style = ts; Set(a); },
                OnStyleEdited, minValue: 1f));

            if (caps.TextStyle) stylingBox.Add(CreateOverridableEnumElement<FontStyle>(title + " Font Style",
                () => Get().style.fontStyle,
                v => { var a = Get(); var ts = a.style; ts.fontStyle = v; a.style = ts; Set(a); },
                OnStyleEdited));

            var pillRow = CreateOverridableColorElement(title + " Pill",
                () => Get().pillColor,
                v => { var a = Get(); a.pillColor = v; Set(a); },
                OnStyleEdited);
            pillRow.tooltip = "Unset = plain text. Set = the affix rides a soft rounded pill in this color.";
            if (caps.Pill) stylingBox.Add(pillRow);
        }

        /// <summary>
        /// The Placement row for a widget with no text box: Auto and Outside Field only. An
        /// <see cref="EnumField"/> cannot omit a member, and offering "Inside Field" on a Toggle or a colour
        /// swatch would be a control that is drawn, stored, and then silently ignored by the renderer.
        /// </summary>
        /// <remarks>
        /// A node that ALREADY carries InsideField (authored before this rule, or copied from a field that
        /// supports it) keeps its stored value until the user picks something else — the dropdown shows the
        /// honest rendered result rather than rewriting the user's data behind their back.
        /// </remarks>
        private VisualElement BuildMountRowWithoutInside(string title, Func<FieldAffix> get, Action<FieldAffix> set)
        {
            var choices = new List<string> { "Auto", "Outside Field" };
            var current = get().mount;
            string shown = current == AffixMount.OutsideField ? "Outside Field" : "Auto";

            var row = new PopupField<string>(title + " Placement", choices, shown);
            row.style.unityFontStyleAndWeight = FontStyle.Normal;
            row.tooltip = "Auto and Outside Field behave identically here: this widget draws no text box, so an "
                        + "affix always sits beside the control. Inside Field is not offered for that reason.";
            row.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_clonedLayoutAsset, "Change Affix Placement");
                var a = get();
                a.mount = evt.newValue == "Outside Field" ? AffixMount.OutsideField : AffixMount.Auto;
                set(a);
                OnModification();
                RefreshPreview();
            });
            return row;
        }

        /// <summary>True for field types Unity draws as several number boxes on one row.</summary>
        private static bool IsCompositeFieldType(Type t)
        {
            if (t == null) return false;
            return t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4)
                || t == typeof(Vector2Int) || t == typeof(Vector3Int)
                || t == typeof(Rect) || t == typeof(RectInt)
                || t == typeof(Bounds) || t == typeof(BoundsInt)
                || t == typeof(Quaternion);
        }

        /// <summary>True when the node lives inside some collection's item layout, i.e. it can be a table column.</summary>
        private bool IsInsideCollectionItemLayout(LayoutNode node)
        {
            var root = _clonedLayoutAsset != null ? _clonedLayoutAsset.Root : null;
            return root != null && ContainsInItemLayout(root, node, false);
        }

        private static bool ContainsInItemLayout(LayoutNode current, LayoutNode target, bool insideItemLayout)
        {
            if (current == null) return false;
            if (insideItemLayout && ReferenceEquals(current, target)) return true;

            if (current is CollectionNode collection)
            {
                // Everything below an ItemLayout is per-element, which is exactly what a table column is.
                if (ContainsInItemLayout(collection.ItemLayout, target, true)) return true;
            }

            if (current is ContainerNode container && container.Children != null)
            {
                foreach (var child in container.Children)
                    if (ContainsInItemLayout(child, target, insideItemLayout)) return true;
            }

            return false;
        }
    }
}
