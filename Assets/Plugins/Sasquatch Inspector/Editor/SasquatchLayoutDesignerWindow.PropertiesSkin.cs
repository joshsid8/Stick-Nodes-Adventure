using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// "Rails &amp; Rhythm" — the Properties panel's look, applied as ONE idempotent pass over the built tree
    /// rather than at the ~300 control construction sites.
    ///
    /// <para><b>Why a pass and not a row factory.</b> The panel is assembled by dozens of per-widget builders
    /// that each hand-roll their rows, and there are 22 section foldouts built at 22 different call sites (16
    /// through <c>CreateFoldoutHeader</c>, 6 inline). Styling at the call sites would mean 22 lockstep edits
    /// plus a standing rule that every future section remembers to opt in — exactly the drift that produced
    /// ten different margin values in the first place. Walking the finished tree instead means a section added
    /// next year is styled without knowing this file exists.</para>
    ///
    /// <para><b>What it guarantees.</b> One input column: a label is pinned to <c>column − itsOwnIndent</c>, so
    /// every field in the panel starts at the same x however deep it sits. One rail per section, with the
    /// <see cref="PropertiesPanelSkin.RailPad"/> gap that keeps a nested chevron off its parent's line. Two
    /// header tiers that visibly change with open/closed state.</para>
    ///
    /// <para><b>Why it is safe to re-run.</b> Every write is a constant or is derived from geometry the pass
    /// does not itself move (a label's x depends on its ancestors' padding, never on its own width), and
    /// <c>ToUpperInvariant</c> is idempotent. It is driven by <see cref="GeometryChangedEvent"/> on the scroll
    /// view, which is what makes it self-healing: a properties-search force-expand, a splitter drag, a widget
    /// kind change that reveals new rows — all of them are geometry changes, so newly-live rows are styled
    /// without any builder having to call back in.</para>
    ///
    /// <para><b>What it deliberately does NOT touch:</b> <c>display</c> (the search and the builders' own
    /// gating own that), <c>opacity</c> (<c>ApplyDimWithSolidKnobs</c> owns it for the inherit/override dim),
    /// row <c>borderLeft*</c> (the properties-search match stripe owns it — this is why the rail lives on the
    /// section CONTENT and never on a row), and any field that is not a full-width row (a Vector3's axis
    /// sub-fields, the four sides of the spacing control).</para>
    /// </summary>
    public partial class SasquatchLayoutDesignerWindow
    {
        private const string SectionRuleName = "sasq-section-rule";

        /// <summary>
        /// Marks a section as holding non-default content. A collapsed section is normally dimmed — that is how
        /// you read open-vs-closed at a glance — but dimming one that the user has actually customised hides the
        /// fact that they customised it. This class exempts it: the header stays at full strength while closed.
        /// It is the same language the rows already speak, where a dimmed control means "inheriting the default"
        /// and a bright one means "you set this".
        /// </summary>
        internal const string SectionActiveClass = "sasq-section-active";

        /// <summary>
        /// A field only takes the pinned label column when it is a full-width ROW. Anything narrower than this
        /// fraction of its own parent is one of several controls sharing a line (spacing's Top/Right/Bottom/Left,
        /// a segmented picker's cells) — pinning those to a 130px label would wreck them. Measured, not
        /// hard-coded per call site, so a new side-by-side control is excluded automatically.
        /// </summary>
        private const float FullWidthRowFraction = 0.6f;

        /// <summary>Shortest text that reads as a help paragraph rather than a caption.</summary>
        private const int HelpTextMinLength = 40;

        private bool _propertiesSkinHooked;

        /// <summary>
        /// Arms the styling pass. Called once from <c>CreateGUI</c> — the ScrollView itself survives every
        /// <c>RefreshProperties</c> (which only clears its content), so one registration covers every rebuild.
        /// </summary>
        private void HookPropertiesSkin()
        {
            if (_propertiesSkinHooked || _propertiesContainer == null) return;
            _propertiesSkinHooked = true;

            // On the CONTENT container, not the ScrollView. The ScrollView is flexGrow:1 inside a fixed pane,
            // so its own rect does not change when the panel is rebuilt — only the content's does. Registering
            // on the wrong one means the pass never runs after a selection change.
            _propertiesContainer.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => StylePropertiesPanel());
            // The ScrollView still matters for a splitter drag: the column width is what sets the label column.
            _propertiesContainer.RegisterCallback<GeometryChangedEvent>(_ => StylePropertiesPanel());
        }

        /// <summary>
        /// The pass. Safe to call at any time, as often as you like; a no-op before the panel has been laid out.
        /// </summary>
        internal void StylePropertiesPanel()
        {
            if (_propertiesContainer == null) return;
            var root = _propertiesContainer.contentContainer;
            if (root == null || root.panel == null) return;

            float contentWidth = root.resolvedStyle.width;
            if (contentWidth <= 1f) return; // not laid out yet — the next geometry pass will catch it

            // A colour swatch used to run under the scrollbar. Give the body a gutter instead.
            _propertiesContainer.style.paddingRight = PropertiesPanelSkin.ScrollGutter;

            float column = PropertiesPanelSkin.ColumnFor(contentWidth);

            StyleBranch(root, 0);
            PinFieldLabels(root, column);
        }

        /// <summary>
        /// Walks one level of the panel, styling the sections it finds and recursing into their content.
        /// <paramref name="depth"/> is the nesting level of the CHILDREN being visited: 0 for the panel's own
        /// top-level sections, 1 for anything inside one of those, and so on.
        /// </summary>
        private static void StyleBranch(VisualElement parent, int depth)
        {
            int index = 0;
            foreach (var child in parent.Children())
            {
                if (child is Foldout foldout)
                {
                    StyleSection(foldout, depth, first: index == 0);
                    var content = SectionContent(foldout);
                    if (content != null) StyleBranch(content, depth + 1);
                }
                else
                {
                    if (child is Label label) StyleHelpParagraph(label, depth);
                    else if (child is Button button) StyleActionButton(button);
                    // Not a section, so the depth is unchanged for anything nested inside it (a plain
                    // wrapper VisualElement groups rows without indenting them).
                    StyleBranch(child, depth);
                }
                index++;
            }
        }

        /// <summary>
        /// One section header + its rail. Depth 0 is a top-level section (small, tracked, uppercased, with a
        /// rule to the right edge); anything deeper is a nested one (bold at body size, sentence case). Both
        /// change colour with open/closed, which is the whole point — the chevron was previously the only tell.
        /// </summary>
        private static void StyleSection(Foldout foldout, int depth, bool first)
        {
            bool open = foldout.value;
            bool topLevel = depth == 0;

            // Bold on the FOLDOUT inherits down into every row inside it. The section title is what should be
            // bold, not the fields under it — this is why the old panel read as one undifferentiated weight.
            foldout.style.unityFontStyleAndWeight = FontStyle.Normal;
            foldout.style.marginTop = first
                ? PropertiesPanelSkin.GapTight
                : (topLevel ? PropertiesPanelSkin.GapSection : PropertiesPanelSkin.GapSub);
            foldout.style.marginBottom = 0;

            var title = SectionTitle(foldout);
            if (title != null)
            {
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                if (topLevel)
                {
                    // Uppercased in place. UI Toolkit has no text-transform, and the search reads this text
                    // only to build a lower-cased haystack (BuildPropHaystack), so the case never reaches a
                    // comparison. ToUpperInvariant is idempotent, which is what keeps the pass re-runnable.
                    if (!string.IsNullOrEmpty(title.text))
                    {
                        string upper = title.text.ToUpperInvariant();
                        if (!string.Equals(title.text, upper)) title.text = upper;
                    }
                    title.style.fontSize = PropertiesPanelSkin.SectionFontSize;
                    title.style.letterSpacing = PropertiesPanelSkin.SectionTracking;
                    ApplySectionTitleColor(title, foldout, topLevel: true);
                    EnsureSectionRule(title, open);
                }
                else
                {
                    title.style.fontSize = PropertiesPanelSkin.SubSectionFontSize;
                    title.style.letterSpacing = 0f;
                    ApplySectionTitleColor(title, foldout, topLevel: false);
                }
            }

            // The chevron carries the state too, so the header reads dim-and-quiet as a whole when closed.
            var checkmark = foldout.Q<VisualElement>(className: "unity-foldout__checkmark");
            if (checkmark != null) checkmark.style.opacity = open ? 1f : 0.65f;

            // The rail. Unity's default is a bare 15px content margin with no line; this replaces it with the
            // indent + hairline pair, so depth is something you SEE rather than something you infer from how
            // far a row is pushed over. Left border only — a row's left border belongs to the search stripe.
            var content = SectionContent(foldout);
            if (content != null)
            {
                content.style.marginLeft = 0;
                content.style.paddingLeft = PropertiesPanelSkin.RailPad;
                content.style.borderLeftWidth = PropertiesPanelSkin.RailWidth;
                content.style.borderLeftColor = DesignerPalette.TreeGuide;
                content.style.marginTop = PropertiesPanelSkin.GapTight;
                content.style.marginBottom = 0;
            }
        }

        /// <summary>
        /// A quiet in-section divider: a small faint word with a hairline running out to the right edge. For
        /// splitting one long section into readable runs WITHOUT another fold — a third nesting level would
        /// render identically to the second (the skin has two header tiers), and folding is the wrong tool when
        /// the user wants to SEE both groups at once.
        ///
        /// <para>Deliberately quieter than a real section header: sentence case, not bold, faint ink and the
        /// faint separator, so it reads as "a break in this list" rather than "a section you can collapse".</para>
        ///
        /// <para>It is inert to the properties filter by construction — a caption holds no field, so the search
        /// classifies it as decorative and never hides it. That is the existing rule for captions in this panel,
        /// and it means a filtered section can show a caption over rows that all matched elsewhere; the caption
        /// naming which side a match belongs to is worth more than that edge looking tidy.</para>
        /// </summary>
        internal static VisualElement MakeGroupCaption(string text, bool first = false)
        {
            var row = new VisualElement { name = "sasq-group-caption", pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = first ? PropertiesPanelSkin.GapRow : PropertiesPanelSkin.GapSection;
            row.style.marginBottom = PropertiesPanelSkin.GapTight;

            var label = new Label(text);
            label.style.fontSize = PropertiesPanelSkin.SectionFontSize;
            label.style.letterSpacing = PropertiesPanelSkin.SectionTracking;
            label.style.color = DesignerPalette.TextFaint;
            label.style.unityFontStyleAndWeight = FontStyle.Normal;
            label.style.flexGrow = 0;
            label.style.flexShrink = 0;
            row.Add(label);

            var rule = new VisualElement { pickingMode = PickingMode.Ignore };
            rule.style.height = 1;
            rule.style.flexGrow = 1;
            rule.style.flexShrink = 1;
            rule.style.minWidth = 0;
            rule.style.marginLeft = PropertiesPanelSkin.GapSub;
            rule.style.backgroundColor = DesignerPalette.SeparatorFaint;
            row.Add(rule);

            return row;
        }

        /// <summary>The label a section shows its title in.</summary>
        private static Label SectionTitle(Foldout foldout)
            => foldout.Q<Label>(className: "unity-foldout__text") ?? foldout.Q<Toggle>()?.Q<Label>();

        /// <summary>
        /// The one rule for a section header's colour, so the state cues can't drift apart between tiers:
        /// open reads live, closed reads dim — UNLESS the section is marked active, which keeps a customised
        /// section legible while folded away.
        /// </summary>
        private static void ApplySectionTitleColor(Label title, Foldout foldout, bool topLevel)
        {
            bool live = foldout.value || foldout.ClassListContains(SectionActiveClass);
            var liveColor = topLevel ? DesignerPalette.TextMuted : DesignerPalette.TextHeader;
            title.style.color = live ? liveColor : DesignerPalette.TextFaint;
        }

        /// <summary>
        /// Flags a section as carrying values the user set, and repaints its header immediately.
        ///
        /// <para>Repainting here rather than leaving it to the next pass is deliberate: a colour change moves
        /// no geometry, so the <see cref="GeometryChangedEvent"/> that drives the pass would not fire and the
        /// header would sit stale until something else happened to re-lay-out the panel. Callers pass
        /// <paramref name="topLevel"/> rather than having this re-derive depth — the caller built the section
        /// and knows, and a second depth derivation is a second thing that can disagree with the first.</para>
        /// </summary>
        internal static void MarkSectionActive(Foldout foldout, bool active, bool topLevel)
        {
            if (foldout == null) return;
            foldout.EnableInClassList(SectionActiveClass, active);
            var title = SectionTitle(foldout);
            if (title != null) ApplySectionTitleColor(title, foldout, topLevel);
        }

        /// <summary>
        /// The hairline that runs from a top-level section's title to the right edge. Added once and then only
        /// re-tinted, so the pass can run every geometry frame without growing the tree. Identified by name
        /// rather than by position — the header row's child order is Unity's, not ours.
        /// </summary>
        private static void EnsureSectionRule(Label title, bool open)
        {
            var host = title.parent;
            if (host == null) return;

            var rule = host.Q<VisualElement>(SectionRuleName);
            if (rule == null)
            {
                rule = new VisualElement { name = SectionRuleName, pickingMode = PickingMode.Ignore };
                rule.style.height = 1;
                rule.style.flexGrow = 1;
                rule.style.flexShrink = 1;
                rule.style.minWidth = 0;
                rule.style.marginLeft = PropertiesPanelSkin.GapSub;
                rule.style.marginRight = PropertiesPanelSkin.GapRow;
                rule.style.alignSelf = Align.Center;
                host.Add(rule);
            }

            // The title must stop growing or it eats the row and the rule never appears.
            title.style.flexGrow = 0;
            title.style.flexShrink = 0;
            host.style.flexGrow = 1;
            rule.style.backgroundColor = open ? DesignerPalette.BorderMid : DesignerPalette.DividerLine;
        }

        /// <summary>
        /// An inline note inside a section — the Conditionals explainer and its siblings. These inherited the
        /// section's bold and sat at full body size, so a paragraph of guidance shouted louder than the rows it
        /// was explaining. Length is the test: a short Label is a caption or a value read-out and is left alone.
        /// </summary>
        private static void StyleHelpParagraph(Label label, int depth)
        {
            if (depth == 0) return;                                        // not inside any section
            if (label.ClassListContains("unity-base-field__label")) return; // a row label, handled elsewhere
            if (label.ClassListContains("unity-foldout__text")) return;     // a section title
            if (string.IsNullOrEmpty(label.text) || label.text.Length < HelpTextMinLength) return;

            label.style.unityFontStyleAndWeight = FontStyle.Normal;
            label.style.fontSize = PropertiesPanelSkin.RowFontSize;
            label.style.color = DesignerPalette.TextFaint;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = PropertiesPanelSkin.GapSub;
        }

        /// <summary>
        /// The "+ Add …" actions. Keyed on the leading "+" because that is what every one of them already has
        /// in common.
        ///
        /// This sets SIZE and RHYTHM only — height, type scale, the gap above — and deliberately paints no
        /// face. They were briefly inset ghosts (transparent fill, hairline border) to keep them from
        /// outweighing the rows above, but an inline <c>backgroundColor</c> also kills Unity's stock
        /// <c>:hover</c>/<c>:active</c> button USS, so they read as dead chrome: no hover tint, no press.
        /// Leaving the face alone hands them back the standard grey editor button — the same look and the
        /// same free hover/press states as the toolbar's "Export Editor", which is what the rest of the
        /// window's neutral actions already wear. Note the states have to come from USS here, because these
        /// buttons stretch to the panel width and so stay opted OUT of the transform-scale spring sweep
        /// (scaling a full-width button paints its rounded edge past the panel).
        /// </summary>
        private static void StyleActionButton(Button button)
        {
            if (button.text == null || !button.text.StartsWith("+")) return;

            // A modifier chip ("+ condition", "+ group") also leads with "+", but it is a SMALLER, quieter
            // control that owns its own metrics and its own hover paint — see MakeModifierChip. Applying the
            // primary action's height and type here would flatten that distinction on the next geometry pass.
            if (button.ClassListContains(ModifierChipClass)) return;

            button.style.height = PropertiesPanelSkin.ActionButtonHeight;
            button.style.fontSize = PropertiesPanelSkin.RowFontSize;
            button.style.marginTop = PropertiesPanelSkin.GapTight;
        }

        /// <summary>
        /// Pins every full-width row's label so the input column is a straight line down the panel.
        ///
        /// The width is <c>column − the label's own x</c>, which is what lets nesting indent the label while the
        /// field stays put: a row two levels deep has a narrower label, not a field pushed 24px right. Run flat
        /// over the whole panel rather than through the recursion, because the eligibility test is measured
        /// (see <see cref="FullWidthRowFraction"/>) and needs the laid-out geometry either way.
        /// </summary>
        private static void PinFieldLabels(VisualElement root, float column)
        {
            root.Query<Label>(className: "unity-base-field__label").ForEach(label =>
            {
                var field = NearestFieldAncestor(label, root);
                if (field == null) return;

                // A field inside another field is a composite's sub-control (Vector3's x/y/z, a Rect's w/h).
                // Its label belongs to the composite's internal layout, not to the panel's column.
                if (NearestFieldAncestor(field, root) != null) return;

                var host = field.parent;
                if (host == null) return;

                float hostWidth = host.resolvedStyle.width;
                float fieldWidth = field.resolvedStyle.width;
                if (hostWidth <= 1f || fieldWidth <= 1f) return;                       // hidden or unlaid
                if (fieldWidth < hostWidth * FullWidthRowFraction) return;             // shares its line

                float indentX = label.ChangeCoordinatesTo(root, Vector2.zero).x;
                if (indentX < 0f || indentX > column) return;                          // off-column; leave alone

                float width = PropertiesPanelSkin.LabelWidthAt(column, indentX);
                label.style.width = width;
                label.style.minWidth = width;
                label.style.maxWidth = width;
                label.style.flexBasis = width;
                label.style.flexGrow = 0;
                label.style.flexShrink = 0;

                label.style.fontSize = PropertiesPanelSkin.RowFontSize;
                label.style.color = DesignerPalette.TextMuted;
                label.style.unityFontStyleAndWeight = FontStyle.Normal;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;

                // A pinned column means a long label can no longer push the field over, so it has to give.
                // The full text stays reachable on hover — never silently lost.
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                if (string.IsNullOrEmpty(label.tooltip)) label.tooltip = label.text;
            });
        }

        /// <summary>
        /// The element a section's rows live in — the one that carries the rail. Resolved by CLASS with the
        /// content container as the fallback, and resolved the same way everywhere: styling one element while
        /// walking a different one would put the rail on a box that holds nothing.
        /// </summary>
        private static VisualElement SectionContent(Foldout foldout)
            => foldout.Q<VisualElement>(className: "unity-foldout__content") ?? foldout.contentContainer;

        /// <summary>
        /// Test seam: runs the pass over an arbitrary tree at an explicit column, with no live window and no
        /// geometry gate. Section styling needs no layout, so a synthetic tree exercises it fully; the label
        /// pinning is geometry-driven and simply finds nothing to do off-panel (covered by the live UI test).
        /// </summary>
        internal static void DebugApplyPropertiesSkin(VisualElement root, float column)
        {
            if (root == null) return;
            StyleBranch(root, 0);
            PinFieldLabels(root, column);
        }

        /// <summary>
        /// The closest ancestor carrying <c>unity-base-field</c>, stopping at <paramref name="boundary"/>.
        /// Starts at the element's PARENT, so passing a field returns its enclosing field (or null when it is
        /// already top-level) — which is how the composite-sub-control test above reads.
        /// </summary>
        private static VisualElement NearestFieldAncestor(VisualElement from, VisualElement boundary)
        {
            for (var p = from.parent; p != null && p != boundary; p = p.parent)
            {
                if (p.ClassListContains("unity-base-field")) return p;
            }
            return null;
        }
    }
}
