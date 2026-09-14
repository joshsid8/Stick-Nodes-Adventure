using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The one label column for every field row Sasquatch builds — custom wrappers AND stock-built
    /// controls (BaseFields, PropertyField internals). Left alone, stock rows ride the editor's own
    /// proportional aligned-label machinery while custom wrappers sit at the theme's fixed label
    /// width: two visibly disagreeing columns in one inspector. So each row's handler strips the
    /// stock aligned class from anything that carries it (the stock width-writer gates on that
    /// class, so removing it disengages cleanly) and drives the labels itself with one mirrored
    /// formula (ratio · root width − padding − row indent, floored), optionally capped by the Label
    /// Column project setting so a wide inspector grows the FIELD, not the label gutter.
    ///
    /// Rows whose Align in Parent is overridden LEAVE the column entirely and hug their content —
    /// the override keeps meaning something, and the stock formula's failure mode (a right-aligned
    /// row's huge x-offset drove its label width negative → the label vanished) can't happen.
    ///
    /// Inside a TRACK (a Columns/Grid cell, a Frame's content, a split pane — anything tagged
    /// <see cref="ScopeClass"/>) the mirrored stock formula is the wrong answer and the column is
    /// FITTED to its own rows instead; see <see cref="RecomputeScope"/>.
    /// </summary>
    internal static class FieldLabelColumn
    {
        // Mirror of the stock aligned-label constants (column ratio of the root width, fixed padding
        // reserve, minimum label width). Verified live against stock rows on 6000.0 (sub-pixel) — if
        // a future editor shifts its column, retune to keep takeover inspectors matching their
        // stock neighbors below the cap.
        internal const float Ratio = 0.45f;
        internal const float ExtraPadding = 37f;
        internal const float MinLabelWidth = 123f;

        /// <summary>Marks a field row some handler already owns, so an ancestor's sweep never adopts
        /// it too (two writers measuring against different roots would fight).</summary>
        internal const string OwnedClass = "sasq-label-column-owned";

        /// <summary>Opts a hand-built (non-BaseField) row into the column. The element carrying this must be
        /// positioned so its LEFT EDGE is where the label starts — the column drives a label width, and every
        /// BaseField it was written for has its label flush at the row's own left edge. A header that leads
        /// with a chevron therefore wraps title+content in an inner align row rather than tagging the header.
        /// Its label is the direct child carrying <see cref="ColumnLabelClass"/>.</summary>
        internal const string ColumnRowClass = "sasq-column-row";

        /// <summary>The label inside a <see cref="ColumnRowClass"/> row that the column drives.</summary>
        internal const string ColumnLabelClass = "sasq-column-label";

        /// <summary>Marks an element that owns its own label column: a Columns/Grid cell, a Frame's content
        /// child, a split pane's subtree — a narrow horizontal TRACK. Applied by the builder wherever it
        /// already decides "my children are column cells", so the two stay one rule.</summary>
        internal const string ScopeClass = "sasq-label-scope";

        private const string UnityAlignedClass = "unity-base-field__aligned";
        private const string BuildRootClass = "sasq-build-root";
        private const string InspectorElementClass = "unity-inspector-element";
        private const string DesignCardClass = "designer-card";
        private const string ColumnsRowClass = "sasq-columns-content-target";
        private const string GridRowClass = "sasq-grid-row";

        // ---- in-track (scoped) column tuning ------------------------------------------------------------
        // Breathing room between a fitted label and the field beside it.
        private const float ScopeLabelGap = 6f;
        // A fitted gutter may never eat more than this share of its track — a long label ellipsizes (or the
        // track stacks) rather than starving the field it belongs to.
        private const float ScopeMaxRatio = 0.45f;
        private const float ScopeMinLabelWidth = 24f;
        // Below this much room for the input, beside-the-label stops being usable and the track stacks.
        // Deliberately the same number as LayoutTreeBuilder.ColumnCellMinFieldWidth: a cell-mode field and a
        // scoped row in the same narrow track must give up at the same width, or one stacks and one doesn't.
        private const float ScopeMinFieldWidth = 90f;
        // Once stacked, only go back beside with this much extra room — the same dead-zone the column-cell
        // path uses, for the same reason (a flip changes the row's height, which can change its own width).
        private const float ScopeStackHysteresisPx = 16f;
        // The existing per-node "Label Above Field" styling, reused as the stack fallback.
        private const string StackedClass = "sasq-label-above";

        /// <summary>The column width for a row whose left edge sits <paramref name="rowOffset"/> px right of
        /// the measurement root's content edge. The cap (when the setting says Capped) applies to the COLUMN,
        /// before the indent subtraction, so nested rows' inputs stay in register with top-level ones.</summary>
        internal static float ComputeLabelWidth(float rootWidth, float rowOffset)
        {
            float column = rootWidth * Ratio - ExtraPadding;
            var settings = SasquatchSettingsManager.Current;
            if (settings.LabelColumnMode == LabelColumnMode.Capped)
                column = Mathf.Min(column, settings.MaxLabelColumnWidth);
            return Mathf.Max(column - rowOffset, MinLabelWidth);
        }

        /// <summary>
        /// Caps a stock-formula label width by the width of the row it actually lives in.
        /// <para>
        /// <see cref="ComputeLabelWidth"/> measures against the INSPECTOR, which is right for an ordinary row
        /// that spans it — but a row can be narrower than that: the node width grip
        /// (<c>MakeWidthResizable</c> + a stored <c>Style.width</c>) pins a field to an authored size. The
        /// stock column knows nothing about it, and its <see cref="MinLabelWidth"/> floor alone (123px) can
        /// exceed what the row can afford — an authored 220px slider handed its label 180px, leaving ~40px
        /// for track + value + unit suffix and pushing the suffix ~49px past the field's right edge.
        /// </para>
        /// <para>
        /// So the label never takes more than <see cref="Ratio"/> of its OWN row — the same share the stock
        /// column takes of the inspector, and the same rule <see cref="RecomputeScope"/> applies inside a
        /// track. Inert for a normal row: a full-width row's own share is larger than the stock column, so
        /// the cap never binds and existing layouts measure exactly as before.
        /// </para>
        /// </summary>
        private static float ClampToOwnRow(float width, VisualElement fieldRow)
        {
            float own = fieldRow.resolvedStyle.width;
            if (float.IsNaN(own) || own <= 1f) return width; // not laid out yet — geometry fires again
            return Mathf.Min(width, Mathf.Max(ScopeMinLabelWidth, own * Ratio));
        }

        private sealed class AdoptedRow
        {
            public VisualElement FieldRow;
            public Label Label;
            public float LastApplied = -1f;
            /// <summary>The track this row registered with, or null for a top-level row. Set once the row's
            /// measurement root turns out to carry <see cref="ScopeClass"/>.</summary>
            public VisualElement Scope;
            /// <summary>Layout-space left offset inside <see cref="Scope"/>'s content box — an indented row
            /// gets a narrower label so every input edge in the track stays in register.</summary>
            public float ScopeOffset;
            public bool Stacked;

            // Memo for this row's measured label width. See MeasuredLabelWidth.
            public string MeasuredText;
            public float MeasuredFontSize = -1f;
            public FontStyle MeasuredFontStyle;
            public Object MeasuredFont;
            public float MeasuredWidth = -1f;
        }

        /// <summary>
        /// This row's label width, measured once per (text, font) and reused until one of them moves.
        ///
        /// <para>WHY THIS IS MEMOISED. <c>MeasureTextSize</c> is one of UI Toolkit's most expensive
        /// primitives — measured live on 6000.0 at <b>10.7 µs per call</b>, roughly four times the cost of an
        /// entire clean layout pass (2.7 µs). <see cref="RecomputeScope"/> measures EVERY row in the track,
        /// and it runs on the geometry event of every row in that track, so the calls fan out with the square
        /// of the track's row count. Measured on the 75-row LabelColumnLab fixture: stripping this file's
        /// geometry handlers cut a live resize pass from 4,238 µs to 3,110 µs — <b>1,129 µs/frame, 70% of
        /// everything Sasquatch's geometry handlers cost</b> on that layout.</para>
        ///
        /// <para>WHY IT IS SAFE. The measurement is a pure function of the label string and the resolved font
        /// (size, weight/style, face). A width drag — the case that pays this cost every frame — changes
        /// none of them; only <c>trackWidth</c> moves, and that is read outside the loop. So the memo returns
        /// exactly the number the call would have returned. The fit still runs IN FULL on every geometry
        /// pass, at the same rate, producing the same gutter: this is an input-equality skip, NOT a throttle,
        /// a debounce, or a deferral.</para>
        ///
        /// <para>A zero measurement is NEVER cached — <see cref="RecomputeScope"/> reads <c>0</c> as "font not
        /// resolved yet, bail and wait for the next geometry event", so memoising it would pin the row to a
        /// phantom width forever.</para>
        /// </summary>
        private static float MeasuredLabelWidth(AdoptedRow a, string labelText)
        {
            var rs = a.Label.resolvedStyle;
            float fontSize = rs.fontSize;
            var fontStyle = rs.unityFontStyleAndWeight;

            // THE FACE, resolved in the SAME precedence the rest of this package already uses for exactly
            // this question — definition first (asset, then font), and only then the legacy `unityFont`.
            // See AutocompleteTextField.cs:764-765 and ExportedWidgetKit.cs:9789-9790, :10068-10069.
            //
            // Getting this order wrong silently defeats the memo: a user font override is written by
            // TextStyleApplier.cs:45 as `FontDefinition.FromFont(text.font)`, which populates
            // `unityFontDefinition.font` and leaves `.fontAsset` NULL. Reading `unityFont` first (or reading
            // only `.fontAsset`) therefore misses the one channel this product ever writes, and the track
            // would keep a gutter measured in the editor's default font after the user picked their own.
            var fontDef = rs.unityFontDefinition;
            Object font = (Object)fontDef.fontAsset ?? (Object)fontDef.font ?? rs.unityFont;

            if (a.MeasuredWidth >= 0f
                && a.MeasuredFontSize == fontSize
                && a.MeasuredFontStyle == fontStyle
                && ReferenceEquals(a.MeasuredFont, font)
                && string.Equals(a.MeasuredText, labelText, System.StringComparison.Ordinal))
                return a.MeasuredWidth;

            float width = a.Label.MeasureTextSize(labelText, 0f,
                VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;

            if (width > 0f)
            {
                a.MeasuredText = labelText;
                a.MeasuredFontSize = fontSize;
                a.MeasuredFontStyle = fontStyle;
                a.MeasuredFont = font;
                a.MeasuredWidth = width;
            }
            return width;
        }

        /// <summary>Every driven row in one track. Rows arrive from MANY per-row handlers (each field row
        /// attaches its own), so the fit — one gutter wide enough for the widest label in the track — has to
        /// live on the track, not in any single row's closure.</summary>
        private sealed class ScopeState
        {
            public readonly List<AdoptedRow> Rows = new List<AdoptedRow>();
            /// <summary>What this track alone concluded — before its row-mates get a vote.</summary>
            public bool Want;
            /// <summary>What is currently applied (the row-wide consensus), and the hysteresis reference.</summary>
            public bool Stacked;
            /// <summary>Last measured fit, so a row-mate's flip can re-apply this track without re-measuring.</summary>
            public float Gutter;
            public bool Measured;
        }

        private static readonly ConditionalWeakTable<VisualElement, ScopeState> _scopes =
            new ConditionalWeakTable<VisualElement, ScopeState>();

        /// <summary>
        /// Attaches column management to a node's row element. Handles the row itself (custom wrapper
        /// or directly-built BaseField) plus any descendant carrying the stock aligned class (a
        /// PropertyField builds its inner field lazily WITH that class; property drawers may add it
        /// too). Re-sweeps on geometry changes and once after attach, so bind-built and rebuilt
        /// internals are caught. Descendants inside a NESTED build root are skipped — the nested
        /// tree's own handlers own those, measured against their own root.
        /// </summary>
        internal static void AttachToRow(VisualElement row, LayoutNode node)
        {
            if (row == null || node == null) return;
            bool drive = !node.Style.alignSelf.isOverridden;

            var adopted = new List<AdoptedRow>();
            VisualElement cachedRoot = null;
            bool cachedRootIsScope = false;

            void AdoptOrRelease(VisualElement fieldRow)
            {
                var label = DirectLabel(fieldRow);
                if (label == null) return;
                if (drive)
                {
                    if (fieldRow.ClassListContains(OwnedClass)) return;
                    fieldRow.AddToClassList(OwnedClass);
                    // Re-sweep when THIS adopted field's own geometry shifts, not only when the outer row we
                    // listen on below resizes: toggling a 1px border on the field (warning-highlight condition
                    // outcome / validation outline) pushes its aligned content inward by the border width WITHOUT
                    // changing the row's width, so no row-level GeometryChanged fires — the column then stayed
                    // ~1px off on the bordered row until the inspector was resized. The row itself already carries
                    // the bottom callback, so skip it here to avoid a duplicate. Bounded by the ≥0.5px write guard.
                    if (fieldRow != row) fieldRow.RegisterCallback<GeometryChangedEvent>(_ => Sweep());
                    adopted.Add(new AdoptedRow { FieldRow = fieldRow, Label = label });
                }
                else
                {
                    // Alignment-override rows hug their content: clear anything the stock machinery
                    // (or a previous drive) already wrote so the label sizes naturally.
                    label.style.width = StyleKeyword.Null;
                    label.style.minWidth = StyleKeyword.Null;
                    label.style.maxWidth = StyleKeyword.Null;
                }
            }

            bool InsideNestedBuildRoot(VisualElement el)
            {
                for (var v = el; v != null && v != row; v = v.parent)
                    if (v.ClassListContains(BuildRootClass)) return true;
                return false;
            }

            // ONE descendant walk that answers BOTH discovery questions, instead of two UQuery passes over
            // the same subtree. Measured on 6000.0: the pair of `Query<>(className:).ToList()` calls below
            // cost 3.2-3.8 us PER ROW PER SWEEP, and Sweep runs on the geometry event of the row AND of every
            // field row it adopted — so a live resize pays it for every row, every frame. In steady state both
            // queries return EMPTY (measured: 0 aligned carriers and 0 column-rows still present on
            // LabelColumnLab, ShotDisplayTypes and ShotTables alike), because the first sweep already stripped
            // the aligned class; the walk exists purely to CATCH a carrier that reappears when a PropertyField
            // builds or rebinds its internals. Halving the number of walks halves that standing cost without
            // changing what is discovered.
            //
            // Semantics are identical to the two queries: UQuery searches DESCENDANTS only (the row itself is
            // handled by the AdoptOrRelease(row) call above), and both lists are filled before either is
            // processed — so stripping the aligned class below still cannot affect what this walk found.
            // Fresh lists per call, deliberately: a width write re-enters layout, and a shared buffer would
            // not survive a re-entrant sweep.
            void CollectTargets(VisualElement el, List<VisualElement> alignedCarriers, List<VisualElement> columnRows)
            {
                for (int i = 0; i < el.hierarchy.childCount; i++)
                {
                    var child = el.hierarchy[i];
                    if (child.ClassListContains(UnityAlignedClass)) alignedCarriers.Add(child);
                    if (child.ClassListContains(ColumnRowClass)) columnRows.Add(child);
                    CollectTargets(child, alignedCarriers, columnRows);
                }
            }

            void Sweep()
            {
                AdoptOrRelease(row);

                var carriers = new List<VisualElement>();
                var customRows = new List<VisualElement>();
                CollectTargets(row, carriers, customRows);

                // Strip the stock aligned class wherever it (re)appears; removing it disengages the
                // stock label-width writer (it gates on the class), then the label is ours to drive.
                foreach (var fieldEl in carriers)
                {
                    // HANDS OFF a nested tree's rows — including the STRIP. This used to strip first and skip
                    // adopting second, which is worse than doing nothing: the aligned class IS the marker the
                    // nested tree's own sweep queries for, so stripping it here left that sweep with nothing to
                    // find and the row was never adopted by anyone. Whoever swept first won, which is why a full
                    // build (nested sweep first) looked right and an in-place rebuild did not.
                    //
                    // Measured on the demo Weapon Rack: after one RebuildNodeInPlace the in-line editor's
                    // `weaponName` row lost its adoption (14 → 13 owned). Its label then contributed nothing to
                    // the track's gutter, so `need` fell under trackWidth, the track stopped stacking (3 → 2
                    // sasq-label-above), the row collapsed 36px → 18px and the card lost 15px (854 → 839) — the
                    // "pop" when typing in a container Title or an in-line editor's Label.
                    if (InsideNestedBuildRoot(fieldEl)) continue;
                    fieldEl.RemoveFromClassList(UnityAlignedClass);
                    AdoptOrRelease(fieldEl);
                }

                // Hand-built rows that opted in explicitly (expandable widget headers: the title is a plain
                // Label, never a BaseField, so nothing above would ever have found it).
                foreach (var customRow in customRows)
                {
                    if (InsideNestedBuildRoot(customRow)) continue;
                    AdoptOrRelease(customRow);
                }

                if (!drive || adopted.Count == 0) return;

                if (cachedRoot == null || cachedRoot.panel != row.panel)
                {
                    // Measure against what stock rows measure against so the columns land in register:
                    // the InspectorElement when inside an Inspector window; otherwise (designer canvas,
                    // showcase, popups) the nearest build root — self-consistent, nothing stock to match.
                    cachedRoot = null;
                    cachedRootIsScope = false;
                    for (var v = row.parent; v != null; v = v.parent)
                    {
                        // A track cell is reached FIRST on the way up and wins outright: a row that starts
                        // 228px into a 262px-wide column can never share the inspector's main column (that
                        // column's inputs begin left of where the track even starts), so the stock formula
                        // just floors out at MinLabelWidth and burns half the track on a gutter.
                        if (v.ClassListContains(ScopeClass)) { cachedRoot = v; cachedRootIsScope = true; break; }
                        if (v.ClassListContains(InspectorElementClass)) { cachedRoot = v; break; }
                        if (cachedRoot == null && v.ClassListContains(BuildRootClass)) cachedRoot = v;
                    }
                }
                if (cachedRoot == null) return;

                if (cachedRootIsScope)
                {
                    // Hand these rows to the track and let it fit ONE gutter across all of them (rows in a
                    // track come from many handlers, so no single closure can see the whole column).
                    var state = _scopes.GetValue(cachedRoot, _ => new ScopeState());
                    for (int i = adopted.Count - 1; i >= 0; i--)
                    {
                        var a = adopted[i];
                        if (a.Label.panel == null) { adopted.RemoveAt(i); continue; }
                        if (a.Scope == cachedRoot) continue;
                        a.Scope = cachedRoot;
                        state.Rows.Add(a);
                    }
                    RecomputeScope(cachedRoot, state);
                    return;
                }

                float rootWidth = cachedRoot.resolvedStyle.width;
                if (float.IsNaN(rootWidth) || rootWidth <= 0f) return;
                float rootPaddingLeft = cachedRoot.resolvedStyle.paddingLeft;

                for (int i = adopted.Count - 1; i >= 0; i--)
                {
                    var a = adopted[i];
                    if (a.Label.panel == null) { adopted.RemoveAt(i); continue; }

                    float rowOffset = LayoutOffsetFrom(a.FieldRow, cachedRoot) - rootPaddingLeft;
                    if (float.IsNaN(rowOffset)) continue;
                    float w = ComputeLabelWidth(rootWidth, rowOffset);
                    w = ClampToOwnRow(w, a.FieldRow);
                    // Width writes re-enter layout; only real changes may write or this would loop.
                    if (Mathf.Abs(w - a.LastApplied) < 0.5f) continue;
                    a.LastApplied = w;
                    a.Label.style.width = w;
                    a.Label.style.minWidth = w;
                    a.Label.style.maxWidth = w;
                }
            }

            row.RegisterCallback<GeometryChangedEvent>(_ => Sweep());
            // Bind-built internals can appear without changing the row's outer size — one deferred
            // sweep after attach catches the initial bind; content-changing rebinds re-fire geometry.
            row.RegisterCallback<AttachToPanelEvent>(_ => row.schedule.Execute(Sweep).StartingIn(0));
        }

        /// Layout-space left offset of el from root's border box (NaN when el isn't under root). worldBound
        /// would bake an active TRANSFORM into the column: a condition reveal pops the row in with a centered
        /// scale spring, the display flip fires the one geometry event mid-animation while the row is still
        /// shrunken (left edge pulled inward → offset inflated → label ~20px too narrow), and the spring's
        /// settle is transform-only so no later event ever corrects it — until an inspector resize.
        /// layout.x ignores transforms.
        private static float LayoutOffsetFrom(VisualElement el, VisualElement root)
        {
            float x = 0f;
            for (var v = el; v != root; v = v.parent)
            {
                if (v == null) return float.NaN;
                x += v.layout.x;
            }
            return x;
        }

        /// <summary>
        /// Sizes one TRACK's column. Mirroring the stock formula here is meaningless — a track has no stock
        /// neighbours to line up with, and the formula's indent subtraction floors out anyway — so the gutter
        /// is FITTED to the track's own rows: as wide as the widest label needs and no wider. Every row still
        /// shares that one gutter (a stack of three fields reads as a column, not a ragged staircase), and an
        /// indented row's label is narrowed by its indent so the INPUT edges stay in register.
        ///
        /// When even a fitted gutter can't leave a usable input, the whole track stacks its labels above
        /// instead — all of it, so one track never mixes beside- and above-label rows, and every track
        /// SHARING A ROW with it follows (see <see cref="ApplyRowStackConsensus"/>).
        /// </summary>
        private static void RecomputeScope(VisualElement scope, ScopeState state)
        {
            float scopeWidth = scope.resolvedStyle.width;
            if (float.IsNaN(scopeWidth) || scopeWidth <= 0f) return;

            float padLeft = scope.resolvedStyle.paddingLeft;
            float padRight = scope.resolvedStyle.paddingRight;
            // Canvas/live parity: in Design mode the cell IS its designer card, and .designer-card's 20px
            // padding-left is grip reserve that does not exist in the live inspector. Offsets are measured
            // from the content box either way (so labels come out identical), but the room available to the
            // FIELDS — which is what decides stacking — has to add that reserve back, or the canvas stacks a
            // track the inspector renders beside. Only when the padding IS the reserve: a Card cell's own
            // inline padding wins over the USS, and that padding is real on both surfaces.
            float reserve = scope.ClassListContains(DesignCardClass)
                            && scope.style.paddingLeft.keyword == StyleKeyword.Null
                ? padLeft
                : 0f;
            // ...and every card ABOVE this scope, for the same reason: the canvas cards every NODE, so the
            // track's own card is never the only one between it and the build root. Adding back one level
            // and stopping closes exactly HALF the gap — measured at ppp=1, the canvas stacked a track the
            // inspector rendered beside across 6 swept widths at nesting depth 1 and 17 at depth 3. Same
            // shape as FieldAffixes.DesignCardReserve, which is the site that got this right first.
            float ancestorReserve = 0f, outermostCardWidth = 0f;
            for (var p = scope.parent; p != null; p = p.parent)
            {
                if (p.ClassListContains(DesignCardClass))
                {
                    var cs = p.resolvedStyle;
                    ancestorReserve += cs.paddingLeft + cs.paddingRight
                                       + cs.borderLeftWidth + cs.borderRightWidth;
                    outermostCardWidth = cs.width;   // keeps overwriting, so it ends on the OUTERMOST
                }
                // Stop AFTER counting: the outermost card can carry the build-root class itself. Above a
                // nested build root the width belongs to another field, not this track.
                if (p.ClassListContains(BuildRootClass)) break;
            }
            float trackWidth = scopeWidth - padLeft - padRight + reserve + ancestorReserve;
            // CAP at the outermost card, which occupies exactly the slot this track fills live — so no
            // live-equivalent answer can be wider. Without the ceiling a scope already pinned at its own
            // floor keeps accumulating reserve past width the card does not have, and the canvas stops
            // deciding at all (that over-correction is what the affix cap exists to prevent).
            if (ancestorReserve > 0f && outermostCardWidth > 0f)
                trackWidth = Mathf.Min(trackWidth, outermostCardWidth);
            if (trackWidth <= 0f) return;

            float fitted = 0f;
            bool measured = false;
            for (int i = state.Rows.Count - 1; i >= 0; i--)
            {
                var a = state.Rows[i];
                if (a.Label.panel == null || a.Scope != scope) { state.Rows.RemoveAt(i); continue; }

                float offset = LayoutOffsetFrom(a.FieldRow, scope) - padLeft;
                if (float.IsNaN(offset)) continue;
                a.ScopeOffset = Mathf.Max(offset, 0f);

                // A BLANKED label is empty, and an empty string measures 0 — exactly what an unresolved
                // font reads as. Ask which it is BEFORE bailing below: a hidden label treated as "not ready
                // yet" returned out of the whole track on every pass, so a real label sharing the track kept
                // whatever width the stock aligned writer happened to leave on its (PropertyField-built)
                // inner field — 0px in an Inspector that ran before its context width resolved, so the label
                // vanished; a wide one on the designer canvas, so the row read as squished. A blanked label
                // asks for no gutter, so it contributes nothing and the fit carries on without it.
                string labelText = a.Label.text ?? string.Empty;
                if (labelText.Length == 0) continue;

                float textWidth = MeasuredLabelWidth(a, labelText);
                // Font not resolved yet — bail rather than fit to a phantom 0-width column. Geometry fires
                // again once it is.
                if (textWidth <= 0f) return;
                fitted = Mathf.Max(fitted, textWidth + a.ScopeOffset);
                measured = true;
            }
            if (!measured) return;

            float gutter = Mathf.Ceil(fitted) + ScopeLabelGap;
            gutter = Mathf.Min(gutter, trackWidth * ScopeMaxRatio);
            var settings = SasquatchSettingsManager.Current;
            if (settings.LabelColumnMode == LabelColumnMode.Capped)
                gutter = Mathf.Min(gutter, settings.MaxLabelColumnWidth);
            gutter = Mathf.Max(gutter, ScopeMinLabelWidth);

            // Field room is gutter-independent of a row's indent (the label absorbs it), so one decision
            // covers the track.
            float need = gutter + ScopeMinFieldWidth;
            state.Gutter = gutter;
            state.Measured = true;
            state.Want = state.Stacked
                ? need + ScopeStackHysteresisPx > trackWidth
                : need > trackWidth;

            ApplyRowStackConsensus(scope, state);
        }

        /// <summary>
        /// Settles the beside-vs-above decision for every track SHARING a visual row with this one: any track
        /// that needs to stack takes them all with it, and the column CELLS in that row (which run the same
        /// decision on their own hugging labels) are folded in from both directions. Without this, a row of
        /// equal-width columns flips one track at a time as the inspector narrows — the mixed row that made
        /// multi-column layouts read as a jumble, and the reason the cell path grew its own consensus.
        /// </summary>
        private static void ApplyRowStackConsensus(VisualElement scope, ScopeState state)
        {
            var rowContainer = RowContainerOf(scope);
            if (rowContainer == null)
            {
                ApplyScope(scope, state, state.Want);
                return;
            }

            var peers = new List<VisualElement>();
            CollectRowScopes(rowContainer, peers);

            bool stack = state.Want;
            foreach (var peer in peers)
            {
                if (peer == scope) continue;
                ScopeState peerState;
                if (_scopes.TryGetValue(peer, out peerState) && peerState.Measured) stack |= peerState.Want;
            }
            stack |= LayoutTreeBuilder.RowCellsWantStacking(rowContainer);

            ApplyScope(scope, state, stack);
            foreach (var peer in peers)
            {
                if (peer == scope) continue;
                ScopeState peerState;
                if (_scopes.TryGetValue(peer, out peerState) && peerState.Measured && peerState.Stacked != stack)
                    ApplyScope(peer, peerState, stack);
            }
            // Tell the cells in this row what the tracks decided; they OR it into their own consensus.
            LayoutTreeBuilder.SetRowScopeStackDemand(rowContainer, stack);
        }

        /// <summary>The visual row a track sits in — a Columns container's cell row or one Grid row. Null for a
        /// track that is nobody's row-mate (a Frame's content, a split pane).</summary>
        private static VisualElement RowContainerOf(VisualElement scope)
        {
            for (var p = scope.parent; p != null; p = p.parent)
            {
                if (p.ClassListContains(ColumnsRowClass) || p.ClassListContains(GridRowClass)) return p;
                if (p.ClassListContains(BuildRootClass)) break;
            }
            return null;
        }

        /// <summary>Tracks under this row, stopping at any NESTED row — an inner container's tracks form their
        /// own row and decide for themselves. Twin of LayoutTreeBuilder.CollectRowCellPlacements.</summary>
        private static void CollectRowScopes(VisualElement root, List<VisualElement> result)
        {
            foreach (var child in root.Children())
            {
                if (child.ClassListContains(ScopeClass)) { result.Add(child); continue; }
                if (child.ClassListContains(ColumnsRowClass) || child.ClassListContains(GridRowClass)) continue;
                CollectRowScopes(child, result);
            }
        }

        private static void ApplyScope(VisualElement scope, ScopeState state, bool stack)
        {
            state.Stacked = stack;
            float gutter = state.Gutter;

            foreach (var a in state.Rows)
            {
                // Only a real base field restyles into a stacked row (.sasq-label-above targets its label
                // and input parts). A hand-built column row has no such parts, so it keeps the fitted
                // gutter — which in a track this narrow is already hugging its text.
                bool canStack = stack && a.FieldRow.ClassListContains("unity-base-field");
                if (canStack != a.Stacked)
                {
                    a.Stacked = canStack;
                    if (canStack) a.FieldRow.AddToClassList(StackedClass);
                    else a.FieldRow.RemoveFromClassList(StackedClass);
                    a.LastApplied = -1f;
                }

                if (a.Stacked)
                {
                    // Inline widths beat the class's USS, so hand the label back to its own text.
                    if (a.Label.style.width.keyword == StyleKeyword.Null) continue;
                    a.Label.style.width = StyleKeyword.Null;
                    a.Label.style.minWidth = StyleKeyword.Null;
                    a.Label.style.maxWidth = StyleKeyword.Null;
                    continue;
                }

                // A blanked label reserves no gutter (it is display:none), so it never gets a width — the
                // same rows the fit above skips, kept in step so a hidden label can't indent its own row.
                if (string.IsNullOrEmpty(a.Label.text)) continue;

                float w = Mathf.Max(gutter - a.ScopeOffset, ScopeMinLabelWidth);
                // Width writes re-enter layout; only real changes may write or this would loop.
                if (Mathf.Abs(w - a.LastApplied) < 0.5f) continue;
                a.LastApplied = w;
                a.Label.style.width = w;
                a.Label.style.minWidth = w;
                a.Label.style.maxWidth = w;
            }
        }

        /// <summary>The label this row drives, or null when the row isn't one the column owns. Also the
        /// first thing <see cref="TooltipAnchor"/> asks for: the label a row's tooltip hangs off is the
        /// same label the column measures, so the two can never disagree about where a row is named.</summary>
        internal static Label DirectLabel(VisualElement fieldRow)
        {
            if (fieldRow.ClassListContains(ColumnRowClass))
            {
                foreach (var child in fieldRow.Children())
                {
                    if (child is Label cl && cl.ClassListContains(ColumnLabelClass)) return cl;
                }
                return null;
            }
            if (!fieldRow.ClassListContains("unity-base-field")) return null;
            foreach (var child in fieldRow.Children())
            {
                if (child is Label l && l.ClassListContains("unity-base-field__label")) return l;
            }
            return null;
        }
    }
}
