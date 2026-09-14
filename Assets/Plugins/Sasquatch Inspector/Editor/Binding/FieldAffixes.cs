using System;
using System.Collections.Generic;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The ONE place prefixes and suffixes are mounted onto a built widget. Hooked from
    /// <c>BindingFactory.CreateBindingField</c>'s shared tail, so every <see cref="DisplayType"/> — including
    /// <see cref="DisplayType.Default"/> and every auto-routed guard (custom drawer, managed reference,
    /// UnityEvent, Spring) — gets affixes without a per-widget switch to keep in sync. Both live render paths
    /// reach that factory through <c>LayoutTreeBuilder.Build</c>, so there is no two-path parity gap here.
    /// <para>
    /// Slider / MinMaxSlider / ProgressBar are DELIBERATELY excluded (see <see cref="OwnsItsAffixes"/>): they
    /// carry one or two value read-outs of their own, so a single affix at the end of the row would read worse
    /// than what they already do. They mount the same <see cref="FieldAffix"/> model themselves.
    /// </para>
    /// </summary>
    internal static class FieldAffixes
    {
        /// <summary>Marks every element this class creates, so restyle sweeps and tests can find/skip them.</summary>
        internal const string AffixClass = "sasq-affix";
        /// <summary>Prefix-side marker (carried alongside <see cref="AffixClass"/>).</summary>
        internal const string PrefixClass = "sasq-affix--prefix";
        /// <summary>Suffix-side marker (carried alongside <see cref="AffixClass"/>).</summary>
        internal const string SuffixClass = "sasq-affix--suffix";

        /// <summary>
        /// Opt-in anchor for CARD-style widgets that build no <c>unity-base-field</c> at all (the UnityEvent
        /// drawer is the first). Stamp it on the element the affixes should hug — usually the card's title —
        /// and they mount as its siblings instead of being dumped at the top and bottom of the whole card.
        /// </summary>
        internal const string AffixAnchorClass = "sasq-affix-anchor";

        /// <summary>
        /// Marks the mini row a read-out widget builds to hold its value box and that box's affixes.
        /// <para>
        /// It is also the LANDING SITE for an inside affix that runs out of room. The ordinary demote drops
        /// the overlay in beside the box it was overlaying — fine on a normal field, unsurvivable on a
        /// read-out whose width its own widget PINS (a slider's inline field, a min-max value box sized from
        /// the LIMITS so the track's ends do not slide as the digits change). A pinned box cannot grow to
        /// hold a new in-flow child, so the affix paints straight past the field and summons
        /// <see cref="HorizontalOverflowRescue"/>'s rail. This row is flexible, it already holds an OUTSIDE
        /// affix correctly, and it is what the min-max fold measures — so it is the one place a demote can
        /// always land.
        /// </para>
        /// </summary>
        internal const string PairRowClass = "sasq-affix-pair";

        // Stamped on a mount HOST (not on the affix itself) so a re-arm pass can tell "already decorated" from
        // "the widget rebuilt its internals and dropped my affix". A PropertyField rebuilds on rebind, taking
        // its children — and the marker — with it, which is exactly the retry signal we want.
        private const string HostPrefixMarker = "sasq-affix-host--prefix";
        private const string HostSuffixMarker = "sasq-affix-host--suffix";

        // Unity's own class names, used only for structural queries (public USS API, not internals).
        internal const string BaseFieldClass = "unity-base-field";
        internal const string BaseFieldInputClass = "unity-base-field__input";
        internal const string TextInputClass = "unity-base-text-field__input";
        /// <summary>Unity's own class on the inline number box a Slider/SliderInt puts beside its track
        /// (BaseSlider's public USS API, exactly like the four names above — a class-name string, never a
        /// named or cast internal type, so §2.5.g is untouched). The one shape where a field's value box is
        /// not the element carrying <see cref="BaseFieldInputClass"/>; see <see cref="FindSliderReadout"/>.</summary>
        internal const string SliderTextFieldClass = "unity-base-slider__text-field";
        private const string NestedRootClass = "sasq-build-root";

        /// <summary>The designer canvas wraps EVERY node in one of these; the live inspector has none.
        /// A class-name string, never a named or cast internal type, so §2.5.g is untouched.</summary>
        private const string DesignCardClass = "designer-card";

        private const float DefaultGap = 4f;
        private const float DefaultFontSize = 10f;
        // Below this many pixels left for the VALUE, an inside affix stops being decoration and starts eating
        // the number. A slider's inline field and a Vector3's axis boxes are ~52px wide, so a pair of
        // one-character affixes reserved 36px of that and left 16 — enough for two digits.
        private const float MinValueRoom = 30f;

        /// <summary>
        /// The widest an affix may be and still mount INSIDE a read-out whose box its widget pinned — twice
        /// the room the number itself is guaranteed. Past that it stops being decoration in the box and
        /// becomes a second field, so it goes beside the number instead, where <see cref="AffixMount.Auto"/>
        /// already puts it.
        /// <para>
        /// This is not a nicety. <see cref="ReserveInsideRoom"/> grows a pinned box to fit the affix, and
        /// <c>minWidth</c> is a HARD floor — flexbox will not shrink below it, so the row simply paints past
        /// the inspector instead. The narrow-box demote in <see cref="MountInside"/> cannot catch that,
        /// because the reservation is exactly what stopped the box being narrow: measured 2026-08-28, a
        /// 14px "per second" grew a 50px slider read-out to 158px, stayed <c>Absolute</c> at every width
        /// swept, and pushed a 160px lane <b>96px</b> over into
        /// <see cref="HorizontalOverflowRescue"/>'s rail. Deciding it here, from the same estimate the
        /// reservation uses, means the two can never disagree.
        /// </para>
        /// <para>
        /// MEASURED, so the split falls where a user would put it. Estimates: "kg" 20, "m/s" 28, "kg" on a
        /// pill 30, "USD" at 12px on a pill 43 — every unit anyone actually types stays inside. "kilograms"
        /// 76, "metres/second" 108, a 14px "per second" 116, and the pill + 16px + icon stack 75 all go
        /// outside, and all of them are wider than the 50px box they were being poured into.
        /// </para>
        /// </summary>
        private const float MaxInsideAffixWidth = 2f * MinValueRoom;

        /// <summary>The pill's own horizontal padding, both sides. MEASURED (5px per side), not guessed —
        /// the 8 this replaced came from a hunt whose numbers are recorded as invented.</summary>
        private const float PillPadding = 10f;

        /// <summary>Width of the widest glyph anyone puts in a unit, as a multiple of the font size. Measured
        /// on a bold "m": 11.5px at size 9 = 1.28. Rounded up, because this is a floor whose whole job is to
        /// stop a short affix under-reserving.</summary>
        private const float WidestGlyphFactor = 1.35f;

        /// <summary>
        /// True for the three widgets that mount affixes themselves because they own multiple value read-outs.
        /// Keep in lockstep with the mounting code in BindingFactory.Slider / .MinMaxSlider / .ProgressBar.
        /// </summary>
        internal static bool OwnsItsAffixes(DisplayType kind) =>
            kind == DisplayType.Slider || kind == DisplayType.MinMaxSlider || kind == DisplayType.ProgressBar;

        /// <summary>True when this property draws several number boxes on one row, so a per-component affix is meaningful.</summary>
        internal static bool IsCompositeProperty(SerializedProperty prop)
        {
            if (prop == null) return false;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.BoundsInt:
                case SerializedPropertyType.Quaternion:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Mounts <paramref name="node"/>'s prefix/suffix onto <paramref name="searchRoot"/>. Cheap no-op when
        /// neither side has content, which is the overwhelmingly common case.
        /// </summary>
        /// <param name="searchRoot">The element to decorate — pass the alignment target when the widget returned
        /// a plain wrapper, so the affix lands on the real field rather than the wrapper.</param>
        /// <param name="isTableCell">True for a table cell; combined with <see cref="BindingNode.AffixInColumnHeader"/>
        /// it suppresses the per-cell affix in favour of the column header.</param>
        internal static void Apply(VisualElement searchRoot, BindingNode node, SerializedProperty prop, bool isTableCell)
        {
            if (searchRoot == null || node == null) return;
            if (OwnsItsAffixes(node.DisplayType)) return;

            // PER SIDE, never a whole-node early-out: header mode only lifts a side the header can actually
            // render, so an icon-only side is still this cell's job. See SuppressedByColumnHeader.
            var prefix = PrefixFor(node, isTableCell);
            var suffix = SuffixFor(node, isTableCell);
            if (prefix.IsEmpty && suffix.IsEmpty) return;

            bool perComponent = node.AffixPerComponent && IsCompositeProperty(prop);

            // What we have actually mounted, per side. The host marker alone is NOT enough: a widget that
            // lazily builds an inner base field (the curve editor) can present a DIFFERENT outermost host on a
            // later pass, and marking only the first host let the re-arm mount a second copy on the second one.
            // Tracking the elements — and re-mounting only once a side has been fully orphaned by a rebuild —
            // makes the pass idempotent no matter how the widget reshuffles underneath us.
            var mountedPrefix = new List<VisualElement>();
            var mountedSuffix = new List<VisualElement>();

            // One pass; re-armed below because a PropertyField builds its real field during BIND — a tick after
            // the first layout — so the first attempt legitimately finds nothing to decorate.
            void Mount(bool allowRootFallback)
            {
                mountedPrefix.RemoveAll(e => !StillUnder(e, searchRoot));
                mountedSuffix.RemoveAll(e => !StillUnder(e, searchRoot));
                bool needPrefix = !prefix.IsEmpty && mountedPrefix.Count == 0;
                bool needSuffix = !suffix.IsEmpty && mountedSuffix.Count == 0;
                if (!needPrefix && !needSuffix) return;

                // A side with nothing left mounted is about to mount again — but MountOne refuses any host
                // still carrying that side's marker, and the marker is stamped on the HOST, which routinely
                // outlives the affix (an internal rebuild replaces the field's children, not the field). Left
                // stale, the marker made the loss permanent: the widget re-binds, the affix is gone, and every
                // re-arm pass declines to replace it. Clearing is safe precisely because it is gated on the
                // side being FULLY orphaned — there is no surviving copy to duplicate.
                if (needPrefix) ClearHostMarkers(searchRoot, HostPrefixMarker);
                if (needSuffix) ClearHostMarkers(searchRoot, HostSuffixMarker);

                var anchor = FindAnchor(searchRoot);
                var hosts = ResolveHosts(searchRoot, perComponent, anchor, allowRootFallback);
                if (hosts.Count == 0) return;
                foreach (var host in hosts)
                {
                    if (needPrefix)
                    {
                        var e = MountOne(host, prefix, true, anchor, searchRoot);
                        if (e != null) mountedPrefix.Add(e);
                    }
                    if (needSuffix)
                    {
                        var e = MountOne(host, suffix, false, anchor, searchRoot);
                        if (e != null) mountedSuffix.Add(e);
                    }
                }
            }

            // First pass, before the widget has necessarily built itself: real field shapes only.
            Mount(false);

            // Re-arm: attach (the bind tick has landed by the deferred pass) and geometry (an internal rebuild
            // drops our affix along with the marker). Both are idempotent — the bookkeeping gates re-entry.
            // By now a lazy widget has built, so an unrecognisable shape really is unrecognisable and the root
            // fallback is the honest answer rather than a premature guess.
            searchRoot.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Mount(false);
                searchRoot.schedule.Execute(() => Mount(true));
            });
            // `true` on purpose, and NOT gated behind the scheduled bind tick. A geometry pass only happens
            // once the tree is attached and laid out, and CARD widgets with no unity-base-field at all — the
            // InlineEditor card is the clearest — have nothing but the root to mount on, so gating this
            // starves them completely: measured, InlineEditor rendered NEITHER side.
            // The premature-wrapper trap this looks like it should guard is already handled by the
            // synchronous first pass being Mount(false); see trap 2 in the decorator's host-resolution notes,
            // which specifies exactly this split.
            searchRoot.RegisterCallback<GeometryChangedEvent>(_ => Mount(true));
        }

        /// <summary>
        /// True while <paramref name="e"/> is still part of the tree we decorated. Replaces a bare
        /// <c>parent == null</c> orphan test, which is one level too shallow: when a displaced inspector body
        /// is re-homed, the affix moves WITH its parent, so every element still reports a parent while none of
        /// them is on screen any more. The bookkeeping then believes the side is mounted and never replaces it
        /// — every affix vanished on a deselect/re-select inside the rebuild window.
        /// </summary>
        private static bool StillUnder(VisualElement e, VisualElement root)
        {
            for (var walk = e; walk != null; walk = walk.parent)
            {
                if (walk == root) return true;
            }
            return false;
        }

        /// <summary>Strips one side's host marker from the whole subtree, so a fully-orphaned side can mount
        /// again. Stops at a nested build root for the same reason <see cref="CollectTextInputs"/> does.</summary>
        private static void ClearHostMarkers(VisualElement root, string marker)
        {
            if (root == null) return;
            root.RemoveFromClassList(marker);
            for (int i = 0; i < root.childCount; i++)
            {
                if (root[i].ClassListContains(NestedRootClass)) continue;
                ClearHostMarkers(root[i], marker);
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // Host resolution
        // ---------------------------------------------------------------------------------------------------

        /// <summary>The opt-in card anchor, if this widget declares one. See <see cref="AffixAnchorClass"/>.</summary>
        private static VisualElement FindAnchor(VisualElement root)
        {
            if (root.ClassListContains(AffixAnchorClass)) return root;
            return FindDescendant(root, AffixAnchorClass);
        }

        /// <summary>
        /// Finds the container(s) to decorate. Per-component returns every text box on the row (a Vector3Field's
        /// three FloatFields, a Bounds' six); a declared anchor wins next; otherwise the single outermost field
        /// input. The last resort is the root itself, so a widget that builds no recognisable field shape still
        /// shows its affixes rather than silently dropping them.
        /// </summary>
        private static List<VisualElement> ResolveHosts(VisualElement root, bool perComponent, VisualElement anchor,
                                                       bool allowRootFallback)
        {
            var result = new List<VisualElement>();

            if (perComponent)
            {
                CollectTextInputs(root, result);
                // One box is not a composite in practice (the widget may not have built its parts yet) — fall
                // through to the single-host path rather than decorating a half-built row.
                if (result.Count > 1) return result;
                result.Clear();
            }

            // A card widget points at the element its affixes should hug; mount into that element's row.
            if (anchor != null && anchor.parent != null)
            {
                result.Add(anchor.parent);
                return result;
            }

            var baseField = FindOuterBaseField(root);
            var input = baseField != null ? FindChildByClass(baseField, BaseFieldInputClass) : null;
            if (input != null) result.Add(input);
            else if (baseField != null) result.Add(baseField);
            else if (allowRootFallback) result.Add(root);
            return result;
        }

        // How far below the search root a field's OWN base field can legitimately sit. A PropertyField puts it
        // at depth 1 and a single wrapper at depth 2; anything deeper belongs to a sub-control, not to this
        // field. Unbounded search grabbed the curve editor's KEYFRAME number box six levels down and mounted
        // the affix inside the graph well.
        private const int MaxBaseFieldDepth = 2;

        /// <summary>The outermost element carrying <c>unity-base-field</c> — the root itself, or a SHALLOW descendant.</summary>
        private static VisualElement FindOuterBaseField(VisualElement root)
        {
            if (root.ClassListContains(BaseFieldClass)) return root;
            return FindDescendant(root, BaseFieldClass, MaxBaseFieldDepth);
        }

        /// <summary>
        /// Breadth-first descendant search that never crosses into a NESTED build root (an inline editor owns
        /// its own rows). <paramref name="maxDepth"/> of 0 means unbounded.
        /// </summary>
        private static VisualElement FindDescendant(VisualElement root, string cls, int maxDepth = 0)
        {
            var queue = new Queue<KeyValuePair<VisualElement, int>>();
            for (int i = 0; i < root.childCount; i++)
                queue.Enqueue(new KeyValuePair<VisualElement, int>(root[i], 1));
            while (queue.Count > 0)
            {
                var entry = queue.Dequeue();
                var e = entry.Key;
                if (e.ClassListContains(NestedRootClass)) continue;
                if (e.ClassListContains(cls)) return e;
                if (maxDepth > 0 && entry.Value >= maxDepth) continue;
                for (int i = 0; i < e.childCount; i++)
                    queue.Enqueue(new KeyValuePair<VisualElement, int>(e[i], entry.Value + 1));
            }
            return null;
        }

        /// <summary>A direct-child lookup that falls back to a SHALLOW descendant search, for the same reason as <see cref="MaxBaseFieldDepth"/>.</summary>
        private static VisualElement FindChildByClass(VisualElement parent, string cls)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent[i].ClassListContains(cls)) return parent[i];
            return FindDescendant(parent, cls, MaxBaseFieldDepth);
        }

        /// <summary>Every text-input box under <paramref name="root"/> — the component boxes of a composite field.</summary>
        private static void CollectTextInputs(VisualElement root, List<VisualElement> into)
        {
            if (root.ClassListContains(TextInputClass)) { into.Add(root); return; }
            var queue = new Queue<VisualElement>();
            for (int i = 0; i < root.childCount; i++) queue.Enqueue(root[i]);
            while (queue.Count > 0)
            {
                var e = queue.Dequeue();
                if (e.ClassListContains(NestedRootClass)) continue;
                if (e.ClassListContains(TextInputClass)) { into.Add(e); continue; }
                for (int i = 0; i < e.childCount; i++) queue.Enqueue(e[i]);
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // Mounting
        // ---------------------------------------------------------------------------------------------------

        /// <summary>Mounts one side onto one host; returns the mounted element, or null when the host already carries this side.</summary>
        private static VisualElement MountOne(VisualElement host, FieldAffix affix, bool isPrefix, VisualElement anchor, VisualElement boundary = null)
        {
            string marker = isPrefix ? HostPrefixMarker : HostSuffixMarker;
            if (host.ClassListContains(marker)) return null;

            bool hostIsTextBox = host.ClassListContains(TextInputClass);
            // The host must BE the text box — never go hunting for one underneath it. A descendant search
            // finds text inputs that belong to a sub-control rather than to this field's value: the curve
            // editor's keyframe time/value boxes made a CurveField look overlayable, and the affix ended up
            // absolutely positioned inside the graph well. Unity puts unity-base-text-field__input on the very
            // same element as unity-base-field__input for every scalar field, so this loses nothing real.
            var textBox = hostIsTextBox ? host : null;

            // ...with ONE exception, and it is Unity's own doing. A stock slider — a [Range] attribute on an
            // int or a float, rendered by DisplayType.Default through a plain PropertyField — is the single
            // shape where unity-base-field__input is NOT the value box: it is the ROW holding the track, and
            // the number lives in an inline TextField beside it. So `textBox` came back null, the line below
            // read that as "boxless widget", and every explicit Inside affix degraded to Outside without a
            // word. Reported as "I have the suffix set to 'inside the field' and it's very clearly NOT inside
            // the field" on a [Range(0,99)] int; measured 2026-08-30, the affix landed in the input row as an
            // in-flow sibling of the drag container, pixel-identical to an Outside mount.
            //
            // This is NOT the descendant search the comment above forbids: it is a DIRECT-CHILD test for
            // Unity's own BaseSlider text-field class, so it cannot reach into a sub-control the way the
            // curve editor's keyframe boxes were reached. See TestAffixStockSliderExposesItsInlineBox for the
            // structural facts this rests on.
            var sliderReadout = hostIsTextBox ? null : FindSliderReadout(host);

            // Auto: inside when there is a real text box to sit in, outside when there is not (a Toggle,
            // ColorField, CurveField or ObjectField has nowhere sensible to overlay).
            bool inside = affix.mount == AffixMount.InsideField
                          || (affix.mount == AffixMount.Auto && textBox != null);

            // The stock slider's box is PINNED (Unity's theme fixes unity-base-slider__text-field at 50px)
            // exactly like the Slider widget's own read-out, so it must go through MountOnReadout rather than
            // straight to MountInside: that is where the MaxInsideAffixWidth budget, the two-element
            // reservation and the demote landing site live, and skipping any of them reproduces the measured
            // spill recorded on MaxInsideAffixWidth. AffixMount.Auto deliberately stays OUTSIDE here, matching
            // both MountOnReadout and DisplayType.Slider — beside the number is the look every existing layout
            // was authored against, and a 50px box demotes a realistic unit straight back out anyway.
            if (sliderReadout != null && affix.mount == AffixMount.InsideField)
            {
                host.AddToClassList(marker);
                // The pair row is the slider's own unity-base-field__input, which is already where an outside
                // affix goes — so a demote lands exactly where the user would otherwise have seen it, instead
                // of inside the pinned TextField where it would paint past the field.
                var beside = MountOnReadout(sliderReadout, affix, isPrefix, host, out var mountedOnReadout);
                // Non-null means the budget (or a missing box) handed it back for us to place beside the
                // number. `mountedOnReadout` is returned either way: a null return here would read as "this
                // side is not mounted", clear the host marker on the next re-arm and mount a SECOND copy.
                if (beside != null) MountOutside(host, false, beside, isPrefix, boundary);
                return mountedOnReadout;
            }

            if (inside && textBox == null) inside = false; // explicit Inside on a boxless widget degrades quietly

            var element = Build(affix, isPrefix, inside);
            host.AddToClassList(marker);

            if (inside) MountInside(textBox, element, affix, isPrefix);
            else if (anchor != null) MountBesideAnchor(host, anchor, element, isPrefix);
            else MountOutside(host, hostIsTextBox, element, isPrefix, boundary);
            return element;
        }

        /// <summary>
        /// A stock slider's inline number box: the DIRECT child of <paramref name="host"/> carrying Unity's
        /// own <c>unity-base-slider__text-field</c>, or null when this host is not a slider input row.
        /// </summary>
        /// <remarks>
        /// Direct children only, and one specific public USS class — deliberately the tightest lookup that can
        /// answer the question. <see cref="FindDescendant"/> would also find it, and would also find every
        /// number box belonging to a sub-control, which is the bug <see cref="MountOne"/>'s "must BE the text
        /// box" rule exists to prevent. A slider that stops showing its input field simply has no such child
        /// and falls back to the ordinary outside mount.
        /// </remarks>
        private static VisualElement FindSliderReadout(VisualElement host)
        {
            if (host == null) return null;
            for (int i = 0; i < host.childCount; i++)
                if (host[i].ClassListContains(SliderTextFieldClass)) return host[i];
            return null;
        }

        /// <summary>Places the affix immediately before (prefix) or after (suffix) a card's declared anchor.</summary>
        private static void MountBesideAnchor(VisualElement host, VisualElement anchor, VisualElement element, bool isPrefix)
        {
            var walk = anchor;
            while (walk != null && walk.parent != host) walk = walk.parent;
            if (walk == null) { host.Add(element); return; }
            host.Insert(isPrefix ? host.IndexOf(walk) : host.IndexOf(walk) + 1, element);
            WrapAnchorRowIfHostIsAColumn(host, walk, element, isPrefix);
        }

        /// <summary>Marks the row this class creates to sit an anchored affix beside its anchor.</summary>
        private const string AnchorRowClass = "sasq-affix-anchor-row";

        /// <summary>
        /// Puts an anchored affix on the SAME LINE as its anchor when the host lays its children out in a
        /// column.
        /// </summary>
        /// <remarks>
        /// <c>MountBesideAnchor</c> inserts the affix as a sibling of the anchor, which is right in a row and
        /// wrong in a column: a plain <c>VisualElement</c> defaults to <c>FlexDirection.Column</c>, so a card
        /// that stamps its anchor on a title label stacked "pre" on its own line above the title and "suf" on
        /// its own line below it. Hopping OUT the way <see cref="HopOutOfAColumnHost"/> does is not the answer
        /// here — the whole point of an anchor is to hug that specific element — so the anchor and the affix
        /// get wrapped in a row instead.
        /// <para>
        /// Deferred to the first real geometry pass for the same reason as the other direction check:
        /// <c>resolvedStyle</c> is meaningless on the synchronous build pass, where an unattached element
        /// reports the default.
        /// </para>
        /// <para>
        /// The second side finds the row the first side made and joins it, rather than wrapping the wrap.
        /// </para>
        /// </remarks>
        private static void WrapAnchorRowIfHostIsAColumn(VisualElement host, VisualElement anchorChild,
                                                         VisualElement element, bool isPrefix)
        {
            bool settled = false;
            host.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (settled) return;

                // Already wrapped by the other side — join that row instead of nesting another.
                var maybeRow = anchorChild.parent;
                if (maybeRow != null && maybeRow.ClassListContains(AnchorRowClass))
                {
                    settled = true;
                    if (element.parent == maybeRow) return;
                    element.RemoveFromHierarchy();
                    int at = maybeRow.IndexOf(anchorChild);
                    maybeRow.Insert(isPrefix ? at : at + 1, element);
                    return;
                }

                var dir = host.resolvedStyle.flexDirection;
                if (dir == FlexDirection.Row || dir == FlexDirection.RowReverse) { settled = true; return; }
                if (anchorChild.parent != host || element.parent != host) return; // not settled yet

                settled = true;
                int index = host.IndexOf(anchorChild);
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList(AnchorRowClass);
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                element.RemoveFromHierarchy();
                anchorChild.RemoveFromHierarchy();
                if (isPrefix) { row.Add(element); row.Add(anchorChild); }
                else { row.Add(anchorChild); row.Add(element); }
                host.Insert(index, row);
            });
        }

        /// <summary>
        /// Overlays the affix on the input box's leading/trailing edge and reserves padding so typed text can
        /// never slide under it.
        /// </summary>
        /// <remarks>
        /// The overlay is a SIBLING of the box, never a child of it. UI Toolkit clips an
        /// <c>overflow: hidden</c> element's children to its CONTENT box — inside the padding, unlike CSS —
        /// and the reservation below grows exactly that padding, so an affix parented to the box scissors
        /// ITSELF out of view the instant the reservation lands. Measured live: a box at x 198 w 334 with 18px
        /// reserved per side clipped its children to x 217 w 296 while the prefix sat at x 203 w 10 — mounted,
        /// styled, sized, Visible, opacity 1, and drawing absolutely nothing. One level up, the affix is still
        /// geometrically INSIDE the box, so no ancestor can clip it either, and the padding keeps doing its
        /// other job of holding the text off the affix.
        /// </remarks>
        /// <param name="demotePlacement">Where a demote should put the affix, when the caller owns a better
        /// answer than "beside the box". <see cref="MountOnReadout"/> passes one so a read-out's affix lands in
        /// the widget's pair row (see <see cref="PairRowClass"/>); everything else leaves it null and gets the
        /// ordinary sibling demote.</param>
        private static void MountInside(VisualElement textBox, VisualElement element, FieldAffix affix, bool isPrefix,
                                        System.Action<VisualElement, bool> demotePlacement = null)
        {
            float gap = affix.gap.Resolve(DefaultGap);
            var overlayHost = textBox.parent;
            // No sibling slot to overlay from. Cannot happen for a real field — the box is always the base
            // field's input child — but a bare box handed straight to the decorator must still show something.
            if (overlayHost == null)
            {
                if (isPrefix) element.style.marginRight = gap; else element.style.marginLeft = gap;
                MountOutside(textBox, true, element, isPrefix);
                return;
            }

            element.style.position = Position.Absolute;
            element.style.justifyContent = Justify.Center;
            // Margins are the OUTSIDE-mount spacing vehicle; absolute placement uses left/right instead.
            element.style.marginLeft = 0;
            element.style.marginRight = 0;
            overlayHost.Add(element);

            bool demoted = false;
            // The affix's own measured width, cached from its geometry event. The reservation needs it, and
            // the BOX's geometry event has no way to ask for it.
            float affixWidth = 0f;

            PlaceOverlay(textBox, element, gap, isPrefix);

            // Recomputes the padding reservation and, if the value can no longer fit, gives way.
            //
            // Called from BOTH geometry events on purpose. It used to live only in the AFFIX's callback, which
            // meant the decision was made once — when the affix measured — and never revisited. A box that
            // SHRANK afterwards (a narrowed inspector, a dragged label column, a resized container) never
            // resized the affix, so nothing re-evaluated and the stale reservation ate the number. Measured on
            // a "USD" prefix squeezed to 96px: box 36, padding 32, leaving TWO pixels for the value.
            void Reserve()
            {
                if (demoted || affixWidth <= 0f) return;

                // AxisPips claims a 9px lane at the box's RIGHT edge and reserves it by writing the SAME
                // paddingRight this writes. Adding the lane keeps both: the pip owns the outer 9px, the unit
                // sits just inside it.
                float lane = ReservedTrailingLane(textBox, isPrefix);
                float pad = affixWidth + gap * 2f + lane;

                // On a narrow box the reservation would leave no room for the value itself. Downgrade to an
                // outside mount instead — a one-way flip, so this can never oscillate with the layout pass
                // that caused it. The OTHER side's padding counts too, or a pair of affixes each measures
                // itself as affordable and together they still starve the number.
                // Canvas/live parity. The demote is a WIDTH decision, and on the designer canvas the box is
                // narrowed by the .designer-card wrappers that exist only there - so deciding from the raw
                // box demoted a unit the live inspector keeps inside, and the author tuned the affix against
                // a preview the buyer never sees. Measured: a default "kg" demoted on the canvas from 98px
                // while live held it inside down to 56. Adding the reserve back is a no-op live (no cards).
                float boxWidth = textBox.resolvedStyle.width;
                float cardReserve = DesignCardReserve(textBox, out float outerCardWidth);
                // CAP at the outermost card. Handing the reserve back unconditionally over-corrects once the
                // box stops shrinking: a squeezed box hits its own minWidth floor and holds there, so
                // box + reserve keeps climbing past width the card does not actually have and the canvas
                // stopped demoting at ALL. The outermost card occupies exactly the slot the bare field fills
                // live, so its width is the ceiling on any live-equivalent answer.
                if (cardReserve > 0f) boxWidth = Mathf.Min(boxWidth + cardReserve, outerCardWidth);
                float otherPad = isPrefix ? textBox.resolvedStyle.paddingRight : textBox.resolvedStyle.paddingLeft;
                if (boxWidth > 0f && boxWidth - pad - otherPad < MinValueRoom)
                {
                    demoted = true;
                    // A read-out widget hands us a landing site of its own. Dropping the affix beside the box
                    // it was overlaying would put it inside a container that widget PINS, which cannot widen —
                    // so it paints past the field instead of giving way. See PairRowClass.
                    if (demotePlacement != null)
                    {
                        UnpinOverlay(textBox, element, gap, isPrefix);
                        demotePlacement(element, isPrefix);
                    }
                    else DemoteToOutside(textBox, element, gap, isPrefix);
                    return;
                }

                if (isPrefix)
                {
                    if (Mathf.Abs(textBox.resolvedStyle.paddingLeft - pad) > 0.5f) textBox.style.paddingLeft = pad;
                }
                else
                {
                    if (Mathf.Abs(textBox.resolvedStyle.paddingRight - pad) > 0.5f) textBox.style.paddingRight = pad;
                }
            }

            // The box can MOVE or RESIZE without the affix ever changing size — a label-column drag slides it
            // across the row, a narrowed inspector squeezes it — so re-place AND re-evaluate from here too.
            textBox.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (demoted) return;
                PlaceOverlay(textBox, element, gap, isPrefix);
                Reserve();
            });
            overlayHost.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (demoted) return;
                PlaceOverlay(textBox, element, gap, isPrefix);
                Reserve();
            });

            // The reservation can only be computed once the affix has measured; the half-pixel guard inside
            // Reserve keeps a padding write from looping back through layout.
            element.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                affixWidth = evt.newRect.width;
                Reserve();
            });
        }

        /// <summary>
        /// Width already claimed at the box's TRAILING edge by another decoration, so a suffix can sit inside
        /// it rather than on top of it. Today that is only <see cref="AxisPips"/>, which pins a coloured lane
        /// with <c>position: absolute</c> on the FIELD root while reserving it via the input's paddingRight.
        /// Prefixes are unaffected — the pip is a right-edge decoration only.
        /// </summary>
        private static float ReservedTrailingLane(VisualElement textBox, bool isPrefix)
        {
            if (isPrefix || textBox == null) return 0f;
            // The pip is a sibling of the input on the field root, so look at the box and its two ancestors —
            // the same shallow window MaxBaseFieldDepth uses, for the same "never leave this widget" reason.
            var walk = textBox;
            for (int i = 0; i <= MaxBaseFieldDepth && walk != null; i++, walk = walk.parent)
            {
                if (FindChildByClass(walk, AxisPips.PipClass) != null) return AxisPips.LaneWidth;
            }
            return 0f;
        }

        /// <summary>
        /// The horizontal room the DESIGNER CANVAS takes out of this field's slot, summed over every
        /// <see cref="DesignCardClass"/> between the box and the build root. Zero in the live inspector,
        /// which has no cards - so the caller can add it back unconditionally.
        /// </summary>
        /// <remarks>
        /// EVERY card, not just the nearest one. The canvas cards every NODE, so a binding one container
        /// deep sits inside two of them and a binding three deep inside four - measured at ppp=1, a 400px
        /// row gave the field 356 / 334 / 312px at depths 1 / 2 / 3 while the live inspector held a flat 400.
        /// Walking to the first card and stopping (which is what LayoutTreeBuilder.BuildColumnCellField and
        /// FieldLabelColumn each do for their own decisions) closes exactly HALF the gap; that was run as a
        /// negative control and left 11 of 22 disagreeing widths behind.
        /// <para>Counts the level it stops ON: the outermost card can carry the build-root class too, and
        /// its reserve is just as real as any other's.</para>
        /// </remarks>
        private static float DesignCardReserve(VisualElement textBox, out float outermostCardWidth)
        {
            outermostCardWidth = 0f;
            float reserve = 0f;
            for (var p = textBox?.parent; p != null; p = p.parent)
            {
                if (p.ClassListContains(DesignCardClass))
                {
                    var cs = p.resolvedStyle;
                    reserve += cs.paddingLeft + cs.paddingRight + cs.borderLeftWidth + cs.borderRightWidth;
                    outermostCardWidth = cs.width;   // keeps overwriting, so it ends on the OUTERMOST
                }
                // Stop AFTER counting: a nested build owns its own slot, and anything above it is another
                // field's problem - the same boundary FindDescendant refuses to cross.
                if (p.ClassListContains(NestedRootClass)) break;
            }
            return reserve;
        }

        private static void PlaceOverlay(VisualElement textBox, VisualElement element, float gap, bool isPrefix)
        {
            var host = element.parent;
            if (host == null) return;
            // The widget rebuilt and left this overlay pinned to a dead box. Orphan it, so the per-side
            // bookkeeping in Apply reads "needs re-mounting" instead of leaving a stale affix mid-row.
            if (textBox.parent == null) { element.RemoveFromHierarchy(); return; }

            var box = textBox.layout;
            var hostBox = host.layout;
            if (float.IsNaN(box.x) || float.IsNaN(box.width) || float.IsNaN(hostBox.width)) return;
            var bs = textBox.resolvedStyle;
            var hs = host.resolvedStyle;

            float top = box.y + bs.borderTopWidth - hs.borderTopWidth;
            float bottom = (hostBox.height - hs.borderBottomWidth) - (box.yMax - bs.borderBottomWidth);
            if (NeedsWrite(element.resolvedStyle.top, top)) element.style.top = top;
            if (NeedsWrite(element.resolvedStyle.bottom, bottom)) element.style.bottom = bottom;

            if (isPrefix)
            {
                float left = box.x + bs.borderLeftWidth - hs.borderLeftWidth + gap;
                if (NeedsWrite(element.resolvedStyle.left, left)) element.style.left = left;
            }
            else
            {
                // + the pip lane, so the unit stops where AxisPips' coloured bar begins instead of overlapping
                // it. Zero for every field that has no pip, which is the overwhelmingly common case.
                float right = (hostBox.width - hs.borderRightWidth) - (box.xMax - bs.borderRightWidth)
                              + gap + ReservedTrailingLane(textBox, false);
                if (NeedsWrite(element.resolvedStyle.right, right)) element.style.right = right;
            }
        }

        /// <summary>An unset offset resolves to NaN, and every NaN comparison is false — so the guard has to
        /// test for it explicitly or the very first write is the one that gets skipped.</summary>
        private static bool NeedsWrite(float current, float target) =>
            float.IsNaN(current) || Mathf.Abs(current - target) > 0.5f;

        /// <summary>Hands back the padding the overlay had reserved and drops its absolute pinning, so it can
        /// go back into the flow somewhere. Says nothing about WHERE — that is the caller's, because the right
        /// answer differs between an ordinary field and a read-out whose box its widget pinned.</summary>
        private static void UnpinOverlay(VisualElement textBox, VisualElement element, float gap, bool isPrefix)
        {
            if (isPrefix) textBox.style.paddingLeft = StyleKeyword.Null;
            else textBox.style.paddingRight = StyleKeyword.Null;

            element.style.position = Position.Relative;
            element.style.top = StyleKeyword.Null;
            element.style.bottom = StyleKeyword.Null;
            element.style.left = StyleKeyword.Null;
            element.style.right = StyleKeyword.Null;
            element.style.justifyContent = StyleKeyword.Null;
            if (isPrefix) element.style.marginRight = gap; else element.style.marginLeft = gap;
        }

        /// <summary>
        /// Puts a demoted read-out affix into the caller's pair row, immediately beside the number. Returns
        /// false when there is no such row — or the box does not live in it — so the caller can fall back to
        /// the ordinary sibling demote rather than strand the affix.
        /// </summary>
        /// <remarks>
        /// Deliberately does NOT call <see cref="EscalateIfTheRowHasNoSlack"/>: the pair row IS the escape
        /// hatch, and one more hop would take the affix straight back out of the row that was built to hold it.
        /// </remarks>
        private static bool PlaceBesideReadout(VisualElement pairRow, VisualElement textBox, VisualElement element, bool isPrefix)
        {
            if (pairRow == null || textBox == null) return false;
            // The box sits a level or two down inside the row — a slider's inline text field is the base
            // field's input child — so climb to whichever DIRECT child of the row contains it.
            var walk = textBox;
            while (walk != null && walk.parent != pairRow) walk = walk.parent;
            if (walk == null) return false;

            element.RemoveFromHierarchy();
            int idx = pairRow.IndexOf(walk);
            pairRow.Insert(isPrefix ? idx : idx + 1, element);
            return true;
        }

        /// <summary>Turns an overlay on a box too narrow to hold it back into an in-flow sibling, and gives
        /// back the padding it had reserved. The element is already a child of the box's parent (see
        /// <see cref="MountInside"/>); this only drops the absolute pinning and moves it to the right index.</summary>
        private static void DemoteToOutside(VisualElement textBox, VisualElement element, float gap, bool isPrefix)
        {
            UnpinOverlay(textBox, element, gap, isPrefix);

            var parent = textBox.parent;
            if (parent == null) return;
            // Re-parented synchronously, so the mount bookkeeping never sees a null parent and never
            // mistakes this for a rebuild that needs a fresh copy.
            element.RemoveFromHierarchy();
            int idx = parent.IndexOf(textBox);
            parent.Insert(isPrefix ? idx : idx + 1, element);

            EscalateIfTheRowHasNoSlack(textBox, element, isPrefix);
        }

        /// <summary>
        /// A demote assumes the box's own field has slack to give. A widget that PINS its value box has none:
        /// the VectorSliders axis field is a fixed 52px with <c>overflow: hidden</c>, so a prefix, the box and
        /// a suffix share those 52px — measured live, a 3-character unit left the number <b>6px</b> wide and
        /// clipped the far affix against the pin. One more hop outward puts the affix in the axis ROW, whose
        /// slider is flexible and absorbs it.
        /// <para>
        /// Measured, never predicted: a container that can grow simply never crushes the box, so this cannot
        /// fire there and the ordinary composite field is untouched. One extra hop, one way — after the move
        /// the box widens again, and the flag has already latched, so it cannot oscillate.
        /// </para>
        /// </summary>
        private static void EscalateIfTheRowHasNoSlack(VisualElement textBox, VisualElement element, bool isPrefix)
        {
            bool escalated = false;
            textBox.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (escalated) return;

                // A box that is HIDDEN or has not been laid out yet measures 0, and 0 is not "narrow" — it is
                // "no answer yet". Treating it as narrow made a collapsed foldout, an unselected tab or the
                // very first pre-layout tick escalate an affix out of a field that has all the room in the
                // world, and the one-way latch made that permanent: expand the container again and the unit
                // never comes back. Only a box that is really on screen and really too small may escalate.
                if (evt.newRect.width <= 0f || evt.newRect.height <= 0f) return;
                if (textBox.resolvedStyle.display == DisplayStyle.None) return;
                if (evt.newRect.width >= MinValueRoom) return;

                var field = element.parent;
                var outer = field != null ? field.parent : null;
                if (outer == null) return;

                // The escape hatch only works if the destination lays its children out in a ROW — the whole
                // point is to land beside the value (`X --slider-- PRE [52] SUF`). VectorSliders' axis row is
                // a row; almost nothing else is. Escalating into a COLUMN parent stacks the unit on its own
                // LINE above or below the number, which is worse than the starvation it was avoiding, and the
                // latch makes that permanent too. Refuse rather than make it worse.
                var dir = outer.resolvedStyle.flexDirection;
                if (dir != FlexDirection.Row && dir != FlexDirection.RowReverse) return;

                escalated = true;
                element.RemoveFromHierarchy();
                int at = outer.IndexOf(field);
                outer.Insert(isPrefix ? at : at + 1, element);
            });
        }

        /// <summary>
        /// Places the affix beside the control. When the host IS the bordered text box the affix has to become a
        /// SIBLING of it (otherwise "outside" would render inside the box); otherwise it goes inside the field's
        /// input container, which is what keeps the label-column metrics untouched.
        /// </summary>
        private static void MountOutside(VisualElement host, bool hostIsTextBox, VisualElement element, bool isPrefix, VisualElement boundary = null)
        {
            if (hostIsTextBox && host.parent != null)
            {
                var parent = host.parent;
                int idx = parent.IndexOf(host);
                parent.Insert(isPrefix ? idx : idx + 1, element);
                // An explicit Outside mount lands in the same spot a demote does and inherits the same
                // problem, so it needs the same escape hatch. It never reaches the demote code because it
                // never overlaid anything in the first place.
                EscalateIfTheRowHasNoSlack(host, element, isPrefix);
                return;
            }

            if (isPrefix) host.Insert(0, element);
            else host.Add(element);
            HopOutOfAColumnHost(host, element, isPrefix, boundary);
        }

        /// <summary>
        /// Moves an outside affix out of a host that lays its children out in a COLUMN, into the row that
        /// contains it.
        /// </summary>
        /// <remarks>
        /// <c>MountOutside</c> assumed every host is horizontal, so a plain <c>Insert(0)</c> / <c>Add</c> was
        /// enough. It is not: Vector Sliders, XY Pad, Color Palette, Rich Text and the Above/Below Asset,
        /// Particle and VFX previews all deliberately set their <c>unity-base-field__input</c> to
        /// <c>FlexDirection.Column</c>. A child of one of those gets its own full-width LINE — so
        /// "Repeat on Each Component = OFF" rendered the unit under the Z slider instead of honouring its own
        /// tooltip, "a single pair at the end of the whole row".
        /// <para>
        /// DEFERRED to the first real geometry pass on purpose. <c>resolvedStyle</c> is not meaningful on the
        /// synchronous build pass — the host is not in a panel yet and reports the default Row — so deciding
        /// this at mount time would take the wrong branch every time and then never revisit it, because the
        /// mount bookkeeping sees a mounted side and skips.
        /// </para>
        /// <para>One-way and latched, like the demote: once it has hopped (or decided it need not), it stops
        /// looking, so it cannot oscillate with the layout pass that triggered it.</para>
        /// </remarks>
        private static void HopOutOfAColumnHost(VisualElement host, VisualElement element, bool isPrefix, VisualElement boundary)
        {
            bool settled = false;
            host.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (settled) return;
                var dir = host.resolvedStyle.flexDirection;
                if (dir != FlexDirection.Column && dir != FlexDirection.ColumnReverse) { settled = true; return; }

                var outer = host.parent;
                if (outer == null) return;   // not parented yet: try again next pass
                // NEVER hop above the subtree we were asked to decorate. When the host IS that root (the
                // re-arm's root fallback, e.g. an InlineEditor card), its parent belongs to the row around
                // the widget — moving there puts the affix outside the field entirely, where the mount
                // bookkeeping's StillUnder check then prunes it and nothing renders at all.
                if (boundary != null && !StillUnder(outer, boundary)) { settled = true; return; }
                var outerDir = outer.resolvedStyle.flexDirection;
                // Only a ROW is an improvement. Hopping into another column just moves the problem up a level.
                if (outerDir != FlexDirection.Row && outerDir != FlexDirection.RowReverse) { settled = true; return; }

                settled = true;
                // Synchronous re-parent, so the per-side bookkeeping in Apply never observes a null parent and
                // never mistakes this for a rebuild that needs a fresh copy.
                element.RemoveFromHierarchy();
                int at = outer.IndexOf(host);
                outer.Insert(isPrefix ? at : at + 1, element);
            });
        }

        /// <summary>
        /// Mounts one side's affix for a widget that owns its own value read-out (Slider / MinMaxSlider), which
        /// cannot go through <see cref="Apply"/> because its read-out is not the field's own input box.
        /// <para>
        /// <see cref="AffixMount.InsideField"/> has to mean the same thing here as everywhere else, so when the
        /// read-out really is (or contains) a text box the affix is overlaid on that box exactly as
        /// <see cref="Apply"/> would have done — and this returns <c>null</c>, because there is nothing left for
        /// the caller to place. Otherwise it returns the free-standing element for the caller to insert beside
        /// the read-out. Also returns null for an empty side, so an "is there anything to mount" check on the
        /// result stays valid.
        /// </para>
        /// </summary>
        /// <remarks>
        /// The three read-out widgets used to build unconditionally-OUTSIDE elements here, so picking
        /// "Inside field" on a Slider or Min-Max silently rendered outside the value box — the one placement
        /// the setting exists to choose. A read-only Min-Max read-out is a plain Label with no box to sit in,
        /// which degrades to outside on its own, the same quiet fallback <see cref="MountOne"/> makes for a
        /// Toggle or ColorField.
        /// <para>
        /// <see cref="AffixMount.Auto"/> deliberately stays OUTSIDE here, unlike the shared decorator, which
        /// resolves Auto to inside wherever there is a text box. These read-outs are pinned narrow — a
        /// slider's inline field is 50px and its companion 56px — so an inside pair reserves a third of the
        /// box and a longer unit demotes straight back out again. Beside the number is both the better
        /// default and the look every existing layout was authored against; the manual says so (§2.24).
        /// </para>
        /// </remarks>
        /// <param name="pairRow">The caller's mini row around this read-out (see <see cref="NewPairRow"/>).
        /// An inside affix places nothing in it, but a LATER demote lands there instead of inside the pinned
        /// box — which is the difference between giving way and painting past the field. Pass it whenever the
        /// widget builds one; null keeps the ordinary sibling demote.</param>
        internal static VisualElement MountOnReadout(VisualElement readout, FieldAffix affix, bool isPrefix,
                                                    VisualElement pairRow = null)
            => MountOnReadout(readout, affix, isPrefix, pairRow, out _);

        /// <inheritdoc cref="MountOnReadout(VisualElement, FieldAffix, bool, VisualElement)"/>
        /// <param name="mounted">The element that was BUILT, whether it ended up overlaid inside the box or
        /// was handed back for the caller to place. The three read-out widgets do not need it — they place
        /// whatever they are given and forget. <see cref="MountOne"/> does: its mount bookkeeping treats a
        /// null as "this side is not mounted", which clears the host marker on the next re-arm and mounts a
        /// second copy on every pass.</param>
        internal static VisualElement MountOnReadout(VisualElement readout, FieldAffix affix, bool isPrefix,
                                                    VisualElement pairRow, out VisualElement mounted)
        {
            mounted = null;
            if (affix.IsEmpty) return null;
            var textBox = FindTextBoxOf(readout);
            if (affix.mount != AffixMount.InsideField || textBox == null)
                return mounted = Build(affix, isPrefix, false);

            // Too wide to live in the box: hand it back for the caller's pair row instead of reserving room
            // this read-out is not allowed to give. See MaxInsideAffixWidth for the measured spill this
            // prevents — the reservation below cannot be walked back once the row has been widened by it.
            if (EstimateAffixWidth(affix) > MaxInsideAffixWidth) return mounted = Build(affix, isPrefix, false);

            // Make room BEFORE the first layout. MEASURED on a Slider read-out, which is the tightest case:
            //
            //   text='x'   affixW=9   pad=17  boxWidth=50  otherPad=2  room=31  floor=30  -> keep
            //   text='kg'  affixW=15  pad=23  boxWidth=50  otherPad=2  room=25  floor=30  -> DEMOTE
            //
            // A one-character affix clears the floor by ONE pixel; two characters miss by five. So every unit
            // anyone actually types demoted straight back outside and "Inside field" rendered identically to
            // "Outside field". Only "x" survived — which is exactly what the old test asserted with.
            //
            // Two things this MUST get right, both learned the hard way:
            //  * The target is the INNER unity-base-text-field__input, because that is the element the demote
            //    measures. Growing the outer .unity-base-slider__text-field does nothing: the inner box is
            //    flexGrow 0, so it never expands to fill its parent.
            //  * The budget must clear pad + otherPad + MinValueRoom. otherPad is a real 2px here, and an
            //    estimate that omits it lands just under the floor and changes nothing.
            // Sizing here (build time, before any layout) rather than from a GeometryChangedEvent also avoids
            // perturbing the very pass that places the overlay, which left the padding and the pin stale.
            // BOTH elements, not just the inner one. Measured: reserving only on the inner
            // unity-base-text-field__input grew it to 62 while its parent stayed pinned at 50, so the box
            // overflowed its own container and the affix rendered outside the parent's content rect. The
            // read-out root has to carry the same reservation for the row to stay coherent — the slider's
            // track is flexible and gives up the width.
            float boxClaim = ReserveInsideRoom(textBox, affix);
            float outerClaim = !ReferenceEquals(readout, textBox) ? ReserveInsideRoom(readout, affix) : 0f;

            var element = Build(affix, isPrefix, true);
            mounted = element;
            float gap = affix.gap.Resolve(DefaultGap);
            // The reservation is what USUALLY keeps the affix inside (EstimateAffixWidth errs high on purpose),
            // but a long enough unit legitimately runs out of room on a 50px read-out. When it does, the affix
            // has to leave the box entirely — including handing the reserved width back, or a box this widget
            // PINNED stays inflated for an affix that is no longer in it.
            MountInside(textBox, element, affix, isPrefix, (el, pfx) =>
            {
                ReleaseInsideRoom(textBox, boxClaim);
                if (!ReferenceEquals(readout, textBox)) ReleaseInsideRoom(readout, outerClaim);
                if (!PlaceBesideReadout(pairRow, textBox, el, pfx))
                    DemoteToOutside(textBox, el, gap, pfx);
            });
            return null;
        }

        /// <summary>The read-out's own text box: the element itself, or the input box a FloatField/TextField
        /// builds one or two levels down. Depth-bounded for the same reason <see cref="MaxBaseFieldDepth"/> is:
        /// an unbounded search finds boxes belonging to a sub-control rather than to this value.</summary>
        private static VisualElement FindTextBoxOf(VisualElement readout)
        {
            if (readout == null) return null;
            if (readout.ClassListContains(TextInputClass)) return readout;
            return FindDescendant(readout, TextInputClass, MaxBaseFieldDepth);
        }

        /// <summary>
        /// The mini row a read-out widget wraps its value box in, so prefix, number and suffix stay glued
        /// together. Built by the CALLER — Slider and MinMaxSlider — and handed straight back to
        /// <see cref="MountOnReadout"/> as the demote landing site; see <see cref="PairRowClass"/> for why an
        /// inside mount needs one even though it places nothing in the row itself.
        /// </summary>
        internal static VisualElement NewPairRow()
        {
            var pair = new VisualElement();
            pair.AddToClassList(PairRowClass);
            pair.style.flexDirection = FlexDirection.Row;
            pair.style.alignItems = Align.Center;
            pair.style.flexShrink = 0;
            return pair;
        }

        /// <summary>
        /// Inserts <paramref name="toInsert"/> as a direct child of <paramref name="container"/>, immediately
        /// before whichever child contains <paramref name="anchor"/>. Appends when the anchor is not inside the
        /// container at all, so a widget whose internals move still gets its affix.
        /// </summary>
        internal static void InsertBefore(VisualElement container, VisualElement anchor, VisualElement toInsert)
        {
            if (container == null || toInsert == null) return;
            var walk = anchor;
            while (walk != null && walk.parent != container) walk = walk.parent;
            if (walk == null) { container.Add(toInsert); return; }
            container.Insert(container.IndexOf(walk), toInsert);
        }

        // ---------------------------------------------------------------------------------------------------
        // Construction and styling
        // ---------------------------------------------------------------------------------------------------

        /// <summary>Builds the affix element: an optional icon and an optional label in a row, styled and pilled.</summary>
        private static VisualElement Build(FieldAffix affix, bool isPrefix, bool inside)
        {
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.AddToClassList(AffixClass);
            root.AddToClassList(isPrefix ? PrefixClass : SuffixClass);
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.flexShrink = 0;

            var ts = affix.style;
            float fontSize = ts.fontSize.isOverridden && ts.fontSize.value > 0f ? ts.fontSize.value : DefaultFontSize;
            Color ink = ts.color.Resolve(BindingFactory.UnitSuffixColor);
            float gap = affix.gap.Resolve(DefaultGap);

            bool hasText = !string.IsNullOrEmpty(affix.text);
            bool hasIcon = !string.IsNullOrEmpty(affix.iconId);

            // Icon sits on the OUTER edge (leads a prefix, trails a suffix) so the text always stays nearest
            // the value it qualifies.
            if (hasIcon && isPrefix) root.Add(BuildIcon(affix.iconId, fontSize, ink, hasText ? 2f : 0f, true));

            if (hasText)
            {
                var label = new Label(affix.text) { pickingMode = PickingMode.Ignore };
                // Keeps the legacy class so the existing FieldTextStyle / MinMaxLabelTextStyle skip guards
                // (which test TextElements for it) leave affix text alone.
                label.AddToClassList(BindingFactory.UnitSuffixClass);
                label.AddToClassList(AffixClass);
                label.style.flexShrink = 0;
                label.style.fontSize = fontSize;
                label.style.color = ink;
                label.style.unityTextAlign = ts.align.Resolve(TextAnchor.MiddleLeft);
                if (ts.fontStyle.isOverridden) label.style.unityFontStyleAndWeight = ts.fontStyle.value;
                if (ts.font != null) label.style.unityFont = ts.font;
                root.Add(label);
            }

            if (hasIcon && !isPrefix) root.Add(BuildIcon(affix.iconId, fontSize, ink, hasText ? 2f : 0f, false));

            if (affix.pillColor.isOverridden)
            {
                root.style.backgroundColor = affix.pillColor.value;
                root.style.paddingLeft = 5;
                root.style.paddingRight = 5;
                root.style.paddingTop = 1;
                root.style.paddingBottom = 1;
                root.style.borderTopLeftRadius = 6;
                root.style.borderTopRightRadius = 6;
                root.style.borderBottomLeftRadius = 6;
                root.style.borderBottomRightRadius = 6;
            }

            if (!inside)
            {
                if (isPrefix) root.style.marginRight = gap; else root.style.marginLeft = gap;
            }

            return root;
        }

        private static VisualElement BuildIcon(string iconId, float fontSize, Color ink, float innerGap, bool isLeading)
        {
            var icon = SasquatchIcons.Create(iconId, Mathf.Max(10f, fontSize * 1.2f), ink);
            icon.pickingMode = PickingMode.Ignore;
            icon.style.flexShrink = 0;
            icon.AddToClassList(AffixClass);
            if (innerGap > 0f)
            {
                if (isLeading) icon.style.marginRight = innerGap;
                else icon.style.marginLeft = innerGap;
            }
            return icon;
        }

        // ---------------------------------------------------------------------------------------------------
        // Text helpers (shared with the table-header mode and the three self-mounting widgets)
        // ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Folds a node's affix TEXT into a plain string for contexts that can only render text — the
        /// ProgressBar's interpolated read-out. Icons and styling are dropped by definition.
        /// </summary>
        internal static string Decorate(BindingNode node, string value) => Decorate(node, value, false);

        /// <inheritdoc cref="Decorate(BindingNode, string)"/>
        internal static string Decorate(BindingNode node, string value, bool isTableCell)
        {
            if (node == null) return value;
            // Header-mode suppression rides inside these two, per side — a read-out is text-only, so a side
            // the header did NOT take (icon-only) still contributes nothing here and folds to "".
            string pre = PrefixFor(node, isTableCell).text;
            string suf = SuffixFor(node, isTableCell).text;
            // The gap is a PIXEL setting everywhere else, and a folded read-out is a single string with no
            // element to hang padding on — so it renders as a space, or as nothing when the user asked for
            // zero. It used to concatenate bare, which is why a ProgressBar showed "12kg" while every other
            // widget showed "12 kg", and made the Gap control look broken on that widget alone.
            // …but never DOUBLE it. The legacy `unitSuffix` contract asked callers to type their own leading
            // space (" HP"), and those strings fold forward into suffixAffix.text verbatim and must keep
            // rendering byte-identically — adding a second space turned "42 HP" into "42  HP" for every
            // layout authored before affixes existed.
            string preGap = node.PrefixAffix.gap.Resolve(DefaultGap) > 0f && !EndsWithSpace(pre) ? " " : "";
            string sufGap = node.SuffixAffix.gap.Resolve(DefaultGap) > 0f && !StartsWithSpace(suf) ? " " : "";
            if (!string.IsNullOrEmpty(pre)) value = pre + preGap + value;
            if (!string.IsNullOrEmpty(suf)) value += sufGap + suf;
            return value;
        }

        /// <summary>
        /// Grows a pinned value box at BUILD time so an explicit inside affix fits without starving the
        /// number. Additive across the two sides, and only ever grows.
        /// </summary>
        /// <returns>The width booked, so a later demote can hand exactly that back — see
        /// <see cref="ReleaseInsideRoom"/>. Zero when nothing was reserved.</returns>
        private static float ReserveInsideRoom(VisualElement textBox, FieldAffix affix)
        {
            if (textBox == null) return 0f;
            float claim = EstimateAffixWidth(affix) + affix.gap.Resolve(DefaultGap) * 2f;
            if (claim <= 0f) return 0f;
            // Do NOT test for a single keyword here. An unset minWidth does not necessarily report
            // StyleKeyword.Undefined — measured, it fell through to `.value.value` and returned 0, so the
            // first side started from a baseline of ZERO and reserved 28px on a box that was already 50px
            // wide. Completely inert, and indistinguishable from "the fix does not work" without a log.
            // Treat any non-positive existing value as "nobody has reserved yet".
            // StyleKeyword.Undefined means "a real Length is set", NOT "unset" — this reads backwards and it
            // cost two rounds. Unset reports some other keyword with a 0 value; once ReserveInsideRoom has
            // written a length, the keyword flips to Undefined and .value.value holds it. Getting this
            // inverted meant the SECOND side never saw the first side's reservation, so a prefix+suffix pair
            // booked only one side's worth of room and then starved each other on the other side's padding
            // (measured: minWidth stayed 70 where it needed ~106, otherPad 31, room 9, floor 30).
            var cur = textBox.style.minWidth;
            float existing = cur.keyword == StyleKeyword.Undefined ? cur.value.value : 0f;
            // First side to ask starts from the floor the NUMBER must keep, plus the box's own base padding
            // (measured at 2px per side on Unity's text input) so the budget is not short by exactly that.
            float baseline = existing > 0f ? existing : MinValueRoom + 4f;
            textBox.style.minWidth = baseline + claim;
            return claim;
        }

        /// <summary>
        /// Gives back what <see cref="ReserveInsideRoom"/> booked for ONE side, once that side's affix has left
        /// the box. Subtractive rather than a clear, so a prefix demoting does not also throw away the room the
        /// suffix is still sitting in; once both are gone the pin goes entirely and the box returns to the
        /// width its own widget chose.
        /// </summary>
        private static void ReleaseInsideRoom(VisualElement textBox, float claim)
        {
            if (textBox == null || claim <= 0f) return;
            // Undefined means a real Length IS set — the same reading that cost two rounds in the reserve
            // above. Any other keyword means nobody wrote here, so there is nothing to give back.
            var cur = textBox.style.minWidth;
            if (cur.keyword != StyleKeyword.Undefined) return;
            float left = cur.value.value - claim;
            if (left <= MinValueRoom + 4f + 0.5f) textBox.style.minWidth = StyleKeyword.Null;
            else textBox.style.minWidth = left;
        }

        /// <summary>
        /// Width an affix will occupy, estimated before it has been laid out. Calibrated against measured
        /// values: 'x' rendered 9px and 'kg' 15px at fontSize 10 (~7.5px/char), and an icon-only affix
        /// measured 22px. Errs HIGH on purpose — under-estimating here silently reinstates the demote.
        /// </summary>
        /// <remarks>
        /// It stopped erring high in two places, and both were measured rather than reasoned (2026-08-28):
        /// <list type="bullet">
        /// <item><description>
        /// <b>A pill is +10, not +8.</b> Measured on a real panel: the pill's own Label sits at x:5 inside a
        /// 21.5px affix, so the horizontal padding is 5 per side. The +8 came from the same 2026-08-21 hunt
        /// whose other guessed numbers are recorded as wrong.
        /// </description></item>
        /// <item><description>
        /// <b>An average per-character width cannot describe a ONE-character affix.</b> 0.80 x size is a fair
        /// mean over a word; a single WIDE glyph blows straight through it. Measured, a bold 9px "m" renders
        /// <b>11.5px</b> where the average predicts 7.2 — and the whole estimate was then 2.3px short of the
        /// affix it was reserving for, which put the demote decision 0.5px on the wrong side of
        /// <see cref="MinValueRoom"/>. The floor is a wide glyph at this size, so short text can no longer
        /// under-reserve.
        /// </description></item>
        /// </list>
        /// <para>
        /// WHY 0.5px MATTERED, and why this is not cosmetic. A read-out whose width its widget PINS (a
        /// slider's inline field, a min-max value box sized from the limits so the track's ends do not move
        /// with the digits) cannot grow to hold a demoted affix: the demote turns the overlay into an in-flow
        /// SIBLING inside a box that is not allowed to widen, so it paints straight past the field and
        /// summons <see cref="HorizontalOverflowRescue"/>'s rail — which the min-max fold cannot prevent
        /// either, because the fold measures the mount's <c>layout.width</c> and the spilled affix is not in
        /// it. Reserving honestly keeps the affix inside, where it fits. The demote-into-a-pinned-host path
        /// is still reachable with a long enough unit and is tracked separately.
        /// </para>
        /// </remarks>
        private static float EstimateAffixWidth(FieldAffix affix)
        {
            float size = affix.style.fontSize.isOverridden && affix.style.fontSize.value > 0f
                ? affix.style.fontSize.value : DefaultFontSize;
            float w = 0f;
            if (!string.IsNullOrEmpty(affix.text))
                // Whichever is larger: the per-character mean (right for a word) or one wide glyph (right for
                // the one- and two-character units that are most of what anyone types).
                w += Mathf.Max(affix.text.Length * size * 0.80f, size * WidestGlyphFactor);
            if (!string.IsNullOrEmpty(affix.iconId)) w += size * 2.2f;   // measured 22px at size 10
            if (affix.pillColor.isOverridden) w += PillPadding;          // the pill's own horizontal padding
            if (w > 0f) w += 4f;                                         // headroom; landing on the floor is what kept failing
            return w;
        }

        private static bool StartsWithSpace(string s) => !string.IsNullOrEmpty(s) && char.IsWhiteSpace(s[0]);
        private static bool EndsWithSpace(string s) => !string.IsNullOrEmpty(s) && char.IsWhiteSpace(s[s.Length - 1]);

        /// <summary>
        /// What an affix can actually DO on a given node. The ONE place that answers it, so the designer, the
        /// quick-edit popup, the live renderers and the exporter cannot drift into offering a control that
        /// silently does nothing.
        /// </summary>
        /// <remarks>
        /// The rule, from the product owner: a prefix/suffix option is either honoured, or it is not offered.
        /// Never drawn-and-ignored. Anything this reports as unsupported must be HIDDEN in the authoring UI,
        /// not disabled and not silently dropped at render time.
        /// </remarks>
        internal struct AffixCapabilities
        {
            /// <summary>False hides the whole Prefix &amp; Suffix section — nothing on this widget can carry one.</summary>
            public bool Any;
            /// <summary>Affix TEXT renders.</summary>
            public bool Text;
            /// <summary>An icon can be drawn beside the text.</summary>
            public bool Icon;
            /// <summary>The affix can ride a pill.</summary>
            public bool Pill;
            /// <summary>Per-side colour / size / font style apply.</summary>
            public bool TextStyle;
            /// <summary>The Placement row means anything at all.</summary>
            public bool Placement;
            /// <summary>"Inside Field" is reachable — i.e. this widget really has a text box to overlay.</summary>
            public bool InsideMount;
            /// <summary>The Gap setting changes anything.</summary>
            public bool Gap;
        }

        /// <summary>
        /// Display types that never build a text input, so an affix can only ever sit BESIDE the control and
        /// "Inside Field" is a promise the renderer cannot keep. <c>Default</c> is deliberately absent: it
        /// depends on the bound property type, which is not knowable here, and <see cref="MountOne"/> already
        /// degrades it correctly at render time.
        /// </summary>
        private static bool HasNoTextBox(DisplayType kind)
        {
            switch (kind)
            {
                case DisplayType.Toggle:
                case DisplayType.ColorPalette:
                case DisplayType.EnumBadgeGrid:
                case DisplayType.TagChips:
                case DisplayType.Rating:
                case DisplayType.UnityEvent:
                case DisplayType.Spring:
                case DisplayType.AudioPreview:
                case DisplayType.AssetPreview:
                case DisplayType.ParticleSystemPreview:
                case DisplayType.AnimationCurveEditor:
                case DisplayType.Gradient:
                case DisplayType.InlineEditor:
                case DisplayType.SerializeReference:
                case DisplayType.DropDown:
                    return true;
                default:
                    return false;
            }
        }

        internal static AffixCapabilities CapabilitiesFor(BindingNode node) => CapabilitiesFor(node, null);

        /// <summary>
        /// The field-type-aware overload. Everything below decides from the DISPLAY TYPE, which cannot see the
        /// second route to the dictionary table: a <c>SerializedDictionary</c> field keeps
        /// <see cref="DisplayType.Default"/> and <c>BindingFactory</c> auto-routes it there. Pass the resolved
        /// field type wherever you have one — <c>null</c> keeps the old, kind-only answer.
        /// </summary>
        internal static AffixCapabilities CapabilitiesFor(BindingNode node, Type fieldType)
        {
            var all = new AffixCapabilities
            {
                Any = true, Text = true, Icon = true, Pill = true,
                TextStyle = true, Placement = true, InsideMount = true, Gap = true,
            };
            if (node == null) return all;

            // A DICTIONARY TABLE is a card widget by every test that matters here: it shows no single value a
            // unit could belong to, it has no text box for "Inside Field" to overlay, and a prefix or suffix
            // typed on it mounted a stray line above or below the WHOLE Key/Value frame, decorating nothing.
            // Keyed on DisplayTypeCatalog.ShowsDictionaryTable rather than on DisplayType.Dictionary so the
            // SerializedDictionary-on-Default route is covered too - the same predicate the Dictionary Style
            // and Label Above gates use, so all three cannot disagree about what this field is.
            //
            // Hiding the section does NOT delete an affix an older layout already carries; the drawer's title
            // declares an affix anchor so an existing one mounts on the header's own line. Same contract as
            // UnityEvent and the curve editor.
            if (DisplayTypeCatalog.ShowsDictionaryTable(node.DisplayType, fieldType))
                return default(AffixCapabilities);

            switch (node.DisplayType)
            {
                case DisplayType.ProgressBar:
                {
                    // The read-out is ONE interpolated string. Text and Gap (rendered as a space) survive that;
                    // an icon, a pill, per-side styling and a placement have nowhere to live in a string.
                    // Percentage and ValueOnly and ValueAndMax all show text, so all three carry the affix —
                    // None shows no read-out at all, so nothing on this widget can.
                    bool visible = node.ProgressBarTextMode != ProgressBarTextMode.None;
                    return new AffixCapabilities
                    {
                        Any = visible, Text = visible, Gap = visible,
                        Icon = false, Pill = false, TextStyle = false,
                        Placement = false, InsideMount = false,
                    };
                }
                case DisplayType.Slider:
                {
                    // The affix hangs off the slider's value read-out. Hide the value and there is no number
                    // left to qualify, so the whole section is meaningless.
                    if (node.DisplayStyleData.sliderHideField) return default(AffixCapabilities);
                    return all;
                }
                case DisplayType.MinMaxSlider:
                {
                    // The affix hangs off the min/max read-outs. Hide those and there is no number left to
                    // qualify, so the whole section is meaningless — the same test the Slider case applies to
                    // sliderHideField. (Offering the section here accepted a unit that then rendered nowhere.)
                    if (node.DisplayStyleData.minMaxHideValues) return default(AffixCapabilities);
                    // Read-only ends are plain Labels with no box to overlay, so Inside Field cannot be kept.
                    // Read the LIVE flag, and note it is INVERTED (DisplayStyleData: false = editable = default),
                    // so the shipping default reaches InsideMount = true. This deliberately does NOT read
                    // minMaxEditableLabels: that is the pre-flip legacy twin, documented DEAD and kept serialized
                    // only so old layouts keep their bytes. Because it defaults false and nothing ever writes it,
                    // reading it pinned InsideMount to false and made "Inside Field" unreachable on EVERY min-max
                    // slider — the Placement popup silently degraded to a two-item Auto/Outside list.
                    all.InsideMount = !node.DisplayStyleData.minMaxReadOnlyValues;
                    return all;
                }
                // CARD widgets render a shape, a ramp, a config block or a callback list — none of them shows
                // a VALUE for a unit to qualify, which is the whole job of a prefix or suffix. Offering the
                // section here produced a "pre" on its own line above the card's title and a "suf" on its own
                // line below it, decorating nothing.
                //
                // Note how this differs from a Percentage progress bar, which DOES keep its affix: "63%" is a
                // value, so "63% charged" means something. The test is not "does the widget look complicated",
                // it is "is there a value on screen that a unit could belong to".
                case DisplayType.AnimationCurveEditor:
                case DisplayType.Gradient:
                case DisplayType.Spring:
                case DisplayType.UnityEvent:
                    return default(AffixCapabilities);

                default:
                    if (HasNoTextBox(node.DisplayType)) all.InsideMount = false;
                    return all;
            }
        }

        /// <summary>
        /// The capabilities a MULTI-SELECTION shares: a boolean AND of <see cref="CapabilitiesFor"/> over every
        /// node in it. The designer offers a Prefix &amp; Suffix row to several fields at once only when every
        /// one of them can honour it — a Progress Bar beside a Slider intersects down to Text and Gap, which is
        /// exactly what a Progress Bar's single-string read-out can carry.
        /// <para>Empty selection returns <c>default</c> (<c>Any == false</c>), not "everything": there is no
        /// node to honour anything. This keeps the caller's <c>if (!caps.Any) return null</c> gate correct
        /// without a second emptiness check.</para>
        /// </summary>
        /// <param name="fieldTypeOf">
        /// Optional resolver for each node's declared field type, threaded straight into
        /// <see cref="CapabilitiesFor(BindingNode, Type)"/>. Null keeps the kind-only answer, which cannot see
        /// a <c>SerializedDictionary</c> auto-routed to the dictionary table on <c>Default</c>.
        /// </param>
        internal static AffixCapabilities Intersect(IEnumerable<BindingNode> nodes, Func<BindingNode, Type> fieldTypeOf = null)
        {
            AffixCapabilities acc = default(AffixCapabilities);
            bool first = true;

            if (nodes != null)
            {
                foreach (var n in nodes)
                {
                    if (n == null) continue;
                    var c = CapabilitiesFor(n, fieldTypeOf != null ? fieldTypeOf(n) : null);
                    if (first) { acc = c; first = false; continue; }

                    acc.Any &= c.Any;
                    acc.Text &= c.Text;
                    acc.Icon &= c.Icon;
                    acc.Pill &= c.Pill;
                    acc.TextStyle &= c.TextStyle;
                    acc.Placement &= c.Placement;
                    acc.InsideMount &= c.InsideMount;
                    acc.Gap &= c.Gap;
                }
            }

            return first ? default(AffixCapabilities) : acc;
        }

        /// <summary>
        /// The ONE answer to "does the column header carry this affix instead of the field?". Every renderer
        /// asks this — the shared decorator AND the three read-out widgets that opt out of it — so the rule
        /// cannot drift between them again.
        /// <para>
        /// Keyed on a REAL table cell, never on <c>forceHideLabel</c>: Columns / Grid / Frame cells, element
        /// template rows, SerializedDictionary rows and ColorPalette swatches all hide their label too and
        /// have no header anywhere, so suppressing there deletes the affix outright.
        /// </para>
        /// <para>
        /// Per SIDE, and keyed on TEXT, because the header carries the affix by folding it into its own LABEL
        /// STRING (<see cref="DecorateColumnHeader"/>) and a string can hold nothing else. An ICON-ONLY affix
        /// folds to nothing, so a whole-node answer deleted it outright: the header came back undecorated AND
        /// the cell was suppressed, live and in every exported editor, with nothing to warn the author. The
        /// two sides also have to answer separately — an icon-only prefix beside a "kg" suffix keeps its cell
        /// copy while the suffix moves up into the header.
        /// </para>
        /// </summary>
        internal static bool SuppressedByColumnHeader(BindingNode node, bool isTableCell, bool isPrefix)
            => isTableCell && node != null && node.AffixInColumnHeader
               && !string.IsNullOrEmpty((isPrefix ? node.PrefixAffix : node.SuffixAffix).text);

        /// <summary>This node's prefix as the caller's context should see it — empty when the header carries it.</summary>
        internal static FieldAffix PrefixFor(BindingNode node, bool isTableCell)
            => node == null || SuppressedByColumnHeader(node, isTableCell, true) ? default(FieldAffix) : node.PrefixAffix;

        /// <summary>This node's suffix as the caller's context should see it — empty when the header carries it.</summary>
        internal static FieldAffix SuffixFor(BindingNode node, bool isTableCell)
            => node == null || SuppressedByColumnHeader(node, isTableCell, false) ? default(FieldAffix) : node.SuffixAffix;

        /// <summary>
        /// The column-header form: "Weight (kg)". Returns <paramref name="header"/> unchanged when the node does
        /// not opt into header mode or has no affix text.
        /// </summary>
        // A column header's unit is measured UNCAPPED and the result becomes the column's flex BASIS
        // (LayoutTreeBuilder.MeasureTableColumnFitWidth -> fitBasis), while every cell carries
        // flexShrink 1 - so an over-long unit does not merely widen its own column, it takes width out of
        // every other one in proportion. Measured at ppp=1 on a 6-column table pinned at 640px: a 10-char
        // unit cost the other five 21% of their width, a 40-char unit cost them 50% and left the string
        // column at 32px (about three characters).
        //
        // 12 characters, because that is "milliseconds" exactly - the longest unit anyone plausibly types
        // still renders in full, and only the pathological ones are trimmed. NOT a width cap: reusing the
        // value side's 360f ceiling would have done nothing at all, since the 40-char case only reached
        // 278px. The damage is linear growth against a fixed table, not a missed ceiling.
        private const int MaxColumnHeaderUnitChars = 12;

        internal static string DecorateColumnHeader(BindingNode node, string header)
        {
            if (node == null || !node.AffixInColumnHeader) return header;
            string pre = node.PrefixAffix.text;
            string suf = node.SuffixAffix.text;
            string unit = ((pre ?? "") + (suf ?? "")).Trim();
            if (string.IsNullOrEmpty(unit)) return header;
            // Trimmed for DISPLAY only - the node's own affix text is untouched, so the cells, the style
            // clipboard and the emitter all still carry what the author typed.
            if (unit.Length > MaxColumnHeaderUnitChars)
                unit = unit.Substring(0, MaxColumnHeaderUnitChars) + "…";
            return string.IsNullOrEmpty(header) ? "(" + unit + ")" : header + " (" + unit + ")";
        }
    }
}
