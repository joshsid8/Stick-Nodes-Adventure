using System.Collections.Generic;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// UI Toolkit drawer for <see cref="SerializedDictionary{TKey,TValue}"/> (matched via the non-generic
    /// <see cref="SerializedDictionaryBase"/> so one drawer covers every closed generic). Renders a polished
    /// two-column Key / Value table over the serialized <c>entries</c> array, with live duplicate-key detection,
    /// drag-to-reorder, a per-row menu, and full-fidelity value editors (a value column of colours shows real
    /// swatches; a value column of <c>SpringConfig</c> shows the Spring widget — anything a Sasquatch field can
    /// draw, a dictionary value can too).
    /// <para>
    /// Being a plain <see cref="CustomPropertyDrawer"/>, it is honoured by both Sasquatch render paths (fallback +
    /// displacement, via <c>PropertyField</c>) AND by a stock Unity inspector when the tool is inactive.
    /// </para>
    /// </summary>
    [CustomPropertyDrawer(typeof(SerializedDictionaryBase), true)]
    public class SerializedDictionaryDrawer : PropertyDrawer
    {
        private const float GripWidth = 18f;
        private const float DividerWidth = 7f;
        private const float DeleteWidth = 24f;
        private const float CellMinWidth = 56f;

        // LANE RESERVATIONS for the column header. A header spacer must equal what the row's chrome actually
        // OCCUPIES — the element's width PLUS its margins — and the two numbers live in different files with
        // nothing tying them together. That is exactly the trap recorded in table-header-lane-reservations,
        // where an 18f constant for a 20px grip put every collection-table column 2px right of its own header.
        //
        // Here BOTH lanes were under-reserved, and the errors nearly cancelled: the header started 6px early
        // and then handed its flex lanes 11px more room to share, so "Key" sat 6px left of the key field
        // while "Value" landed within half a pixel and hid the fault. Fixing only the grip lane would make it
        // WORSE (the header's fixed total would go to 48 against the row's 53, pushing the value column
        // 2.5px out of register), so the two must move together.
        //
        //   grip: .sasq-reorder-grip is margin 2/4 in USS, and the width is overridden inline to GripWidth.
        //   ✕   : StyleRowButton adds a 2px marginLeft, and the theme's own .unity-button carries 3px on the
        //         right — the ✕ is the row's last child, so that margin is real occupied space.
        private const float GripLaneWidth = GripWidth + 2f + 4f;
        private const float DeleteLaneWidth = DeleteWidth + 2f + 3f;
        // Mapping: the key chip lane and the arrow gutter between the two columns. The chip lane is FIXED on
        // purpose - a lane that grew with the longest key would reshape every row as keys were typed, and the
        // style is sold for short, enum-ish keys. 118px holds roughly 16 characters at the editor font.
        private const float MappingChipLaneWidth = 118f;
        private const float MappingArrowLaneWidth = 20f;

        /// <summary>
        /// Entry count at which the table grows its own search box.
        /// <para>
        /// AUTO-REVEALED rather than a per-field toggle, on purpose. A collection's search is opt-in because a
        /// <c>CollectionNode</c> already carries a pile of view settings and the designer has somewhere to put
        /// one more; a dictionary reaches this drawer from FOUR routes (the display type, the auto-route, a
        /// stock inspector with no layout at all, and an exported editor), and only two of them have a
        /// LayoutNode to read a flag from. A setting that silently does nothing on half its routes is exactly
        /// the dead control the rest of this feature is careful not to ship.
        /// </para>
        /// <para>
        /// Ten because below it every row is already on screen, so a search box would be chrome that answers a
        /// question nobody has. The inspector's own header search deliberately stops at a collection's
        /// boundary and hands off to its row search; this is the dictionary's half of that hand-off.
        /// </para>
        /// </summary>
        private const int SearchRevealThreshold = 10;

        /// <summary>
        /// Everything that differs between the two dictionary shapes this table can drive. The table itself is
        /// shape-agnostic: it only needs an array of pairs and the relative names of each pair's two halves.
        /// <list type="bullet">
        /// <item><b><see cref="SerializedDictionary{TKey,TValue}"/></b> — array is the <c>entries</c> child,
        /// halves are <c>Key</c>/<c>Value</c> (capitalised: they are real C# fields on <c>Entry</c>).</item>
        /// <item><b>Unity 6.6's native <c>Dictionary&lt;K,V&gt;</c></b> — the bound property IS the array
        /// (<c>isArray</c> is true on it), and the halves are <c>key</c>/<c>value</c>, LOWERCASE. Verified
        /// against 6000.6.0f1: <c>FindPropertyRelative("Key")</c> returns null there.</item>
        /// </list>
        /// </summary>
        internal sealed class TableSpec
        {
            /// <summary>Relative path from a pair element to its key. "Key" for ours, "key" for Unity's.</summary>
            public string KeyRelative = "Key";
            /// <summary>Relative path from a pair element to its value.</summary>
            public string ValueRelative = "Value";
            /// <summary>Column header text. Unity's <c>[DictionaryDisplay]</c> can override these.</summary>
            public string KeyLabel = "Key";
            public string ValueLabel = "Value";
            /// <summary>Share of the row the KEY column takes, 0.01–0.99. Unity's attribute can override it.</summary>
            public float KeyColumnFraction = 0.5f;
            /// <summary>EditorPrefs namespace for the expand state, so the two shapes cannot collide.</summary>
            public string ExpandPrefix = "SasquatchInspector.SerializedDictionary.exp:";
            /// <summary>Header text. Null means "use the property's own display name" (the drawer's case);
            /// the display type passes the node's resolved label so a Label override still wins.</summary>
            public string Title;
            /// <summary>
            /// Blank the KEY of every row this table creates — the Add button, and the row menu's Duplicate.
            /// <para>
            /// Unity grows an array by CLONING the previous last element, and <c>InsertArrayElementAtIndex</c>
            /// clones in place, so without this every new dictionary row arrives carrying a key that already
            /// exists: red, and discarded by the runtime, before the user has typed anything. Unity's own
            /// dictionary <c>+</c> adds a BLANK row, and Duplicate on a dictionary can only sensibly mean
            /// "copy the VALUE, give me a fresh key" — a key is unique by definition.
            /// </para>
            /// <para>
            /// Default TRUE so both shapes agree. It used to be true for Unity's native dictionary only, which
            /// left the bonus <see cref="SerializedDictionary{TKey,TValue}"/> handing out a guaranteed-duplicate
            /// row from the same button in the same table.
            /// </para>
            /// </summary>
            public bool ClearKeyOnNewRow = true;

            /// <summary>
            /// True when this table is drawn on the Layout Designer's canvas rather than in a real inspector.
            /// The canvas already speaks hover ("this is the node you would select"), so the row frame's rail
            /// must not light up as well — two hover languages on the same pixels. The collection table makes
            /// exactly this call at its own <c>ApplyRowSeparator</c> site; the dictionary had no flag to make it
            /// with. (Interactivity itself is handled above us by <c>SuppressInteractionPersistently</c>, which
            /// is why this is only about the rail.)
            /// </summary>
            public bool DesignMode;
            /// <summary>
            /// Authoritative duplicate/ignored-row source, when the platform has one. Unity 6.6 exposes
            /// <c>SerializedProperty.GetDictionaryIgnoredEntries()</c> — exactly the rows its RUNTIME will drop
            /// — which beats re-deriving them from key text. Null falls back to <see cref="KeyToken"/> counting
            /// (all this package had before 6.6, and still the only option for our own type).
            /// </summary>
            public System.Func<int[]> DuplicateIndices;
            /// <summary>Rows whose key is null/unset; Unity keeps them as placeholders and drops them at runtime.</summary>
            public System.Func<int[]> NullKeyIndices;

            /// <summary>
            /// Container-level look: the shell, the column header, the density, how the key presents itself.
            /// <see cref="DictionaryTableStyle.Table"/> is what this widget always drew, so the default keeps
            /// every existing dictionary (and the stock-inspector drawer path) pixel-identical.
            /// </summary>
            public DictionaryTableStyle Style = DictionaryTableStyle.Table;

            /// <summary>
            /// Per-row surface: bare, a hairline between rows, or a tray per row with a left edge rail.
            /// Deliberately the SAME enum a collection uses, painted through the very same
            /// <c>LayoutTreeBuilder.ApplyRowSeparator</c> — a dictionary row frame and a list row frame are one
            /// decision point, so they cannot drift apart.
            /// </summary>
            public CollectionRowSeparator RowSeparator = CollectionRowSeparator.None;

            /// <summary>Tint for the row frame (hairline / rail). Unset resolves to the theme.</summary>
            public OverridableColor SeparatorColor;

            /// <summary>
            /// The bindings that style each row's KEY and VALUE cell — a Display Type and its full option set,
            /// so a value column of Colors can be a Color Palette, a float column a slider, and so on. Null
            /// (the drawer-only and stock-inspector routes, which have no layout node above them) builds the
            /// cell from a bare <c>BindingNode</c>, which is exactly what this table always drew.
            /// <para>
            /// The template's own <c>PropertyPath</c> is empty by convention and is never read here: the cell
            /// is built against the RESOLVED path of this row's half, so ONE authored template is correct for
            /// both dictionary shapes even though they name their halves differently
            /// (<see cref="KeyRelative"/> / <see cref="ValueRelative"/>).
            /// </para>
            /// </summary>
            public BindingNode KeyTemplate;
            public BindingNode ValueTemplate;

            /// <summary>
            /// Set when this table is being drawn with NO Sasquatch layout above it — the stock-inspector,
            /// exported-editor and foreign-<c>[CustomEditor]</c> paths, i.e. everything that reaches this
            /// drawer through <see cref="CreatePropertyGUI"/> rather than through the Dictionary display
            /// type. It makes the row menu install a real <c>ContextualMenuManipulator</c>, because the
            /// contributor registry those four commands normally use is only ever read by
            /// <c>QuickMenuController</c>, which only a live layout installs.
            /// </summary>
            public bool StandaloneRowMenu;
        }

        private static readonly TableSpec OwnTypeSpec = new TableSpec();

        /// <summary>
        /// <see cref="OwnTypeSpec"/>'s twin for the drawer's own entry point. Both are shared statics and
        /// must stay immutable — writing per-field state (a Title) onto either would leak it onto every
        /// dictionary drawn afterwards in the session.
        /// </summary>
        private static readonly TableSpec StandaloneSpec = new TableSpec { StandaloneRowMenu = true };

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var entriesProp = property?.FindPropertyRelative("entries");
            if (entriesProp == null || !entriesProp.isArray)
            {
                // Not our serialized shape (should never happen) — never render blank; fall back to the default.
                return new PropertyField(property);
            }

            // HONOUR THE HOSTING FIELD'S LABEL. A renamed Label reaches a drawer as the PropertyField's own
            // label, and handing BuildTable the shared static spec (Title == null) threw it away and fell
            // back to the property's name — so a field renamed in the designer kept its old header, and an
            // exported editor (which emits `field.label = "…"`) did too, because the emitter was right and
            // the drawer ignored it.
            //
            // OwnTypeSpec is a SHARED STATIC and must never be mutated: writing a Title onto it would leak
            // that rename onto every other dictionary drawn afterwards in the session.
            string label = preferredLabel;
            if (string.IsNullOrEmpty(label) || label == property.displayName)
                return BuildTable(property, entriesProp, StandaloneSpec);

            return BuildTable(property, entriesProp, new TableSpec { Title = label, StandaloneRowMenu = true });
        }

        /// <summary>
        /// The Key/Value table itself. <paramref name="labelProp"/> supplies the title and the expand-state key;
        /// <paramref name="arrayProp"/> is the array of pairs actually edited. Shared by this drawer and by the
        /// <c>Dictionary</c> display type, which points it at a native Unity 6.6 dictionary.
        /// </summary>
        internal static VisualElement BuildTable(SerializedProperty labelProp, SerializedProperty arrayProp, TableSpec spec)
        {
            spec = spec ?? OwnTypeSpec;

            var root = new VisualElement();
            root.style.marginTop = 1;
            root.style.marginBottom = 3;
            // Standalone-safe: in the two live inspector paths the widget sheet + skin tokens are already on an
            // ancestor, but a stock Unity inspector (Sasquatch inactive) has neither — pull them in so the
            // sasq-* classes and --sasq-* variables resolve either way.
            SasquatchSkin.ApplyWithWidgetSheet(root);

            var serializedObject = labelProp.serializedObject;

            // --- persisted expand/collapse state -------------------------------------------------------------
            var target = serializedObject.targetObject;
            string expandKey = spec.ExpandPrefix + (target != null ? target.GetType().FullName : "?") + ":" + labelProp.propertyPath;
            bool expanded = EditorPrefs.GetBool(expandKey, true);

            // --- per-row bookkeeping for live duplicate-key detection ---------------------------------------
            // RENDERED rows, in render order. With a search query active these are a SUBSET of the array.
            var keyProps = new List<SerializedProperty>();
            var keyCells = new List<VisualElement>();
            var keyWarns = new List<VisualElement>();
            // Raw array index per rendered row - the bridge between the two. Every duplicate/empty-key answer
            // is stated in RAW indices (Unity's GetDictionaryIgnoredEntries already is), and every paint
            // target is a rendered slot; without this list the two silently mean the same number only while
            // nothing is filtered.
            var rowIndices = new List<int>();
            // EVERY row's key, raw order, filtered or not. The duplicate scan must see the WHOLE dictionary:
            // a duplicate key that a search query happens to hide is still a duplicate the runtime discards,
            // and scanning only what is on screen would quietly report the table as clean.
            var allKeyProps = new List<SerializedProperty>();
            // The array size these rows were last drawn from - NOT the rendered count. See the
            // AttachExternalArraySizeSync call at the end of this method for why the difference matters.
            int lastBuiltSize = -1;

            // Element paths this table has ALREADY nudged open. buildRows re-runs in full on every Add,
            // Delete, Duplicate and Insert, so an unconditional isExpanded = true down in the row loop was
            // not a first-build nudge at all — it re-opened every compound value the user had collapsed,
            // every time the table changed shape, and the comment beside it claimed the opposite. Remembered
            // per TABLE INSTANCE (not persisted): the nudge is a first-sight courtesy, and after that the
            // foldout is the user's.
            var nudgedOpen = new HashSet<string>();

            // ================================ chrome ========================================================
            var frame = CreateFrame(spec.Style);
            root.Add(frame);

            // Header: [chevron  title  (count)  ⚠]  ...  [+ Add]
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 4;

            var titleRegion = new VisualElement();
            titleRegion.style.flexDirection = FlexDirection.Row;
            titleRegion.style.alignItems = Align.Center;
            titleRegion.style.flexGrow = 1;

            var chevron = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronRight, 12, SasquatchIcons.FoldoutChevronTint);
            chevron.style.marginRight = 4;
            chevron.style.flexShrink = 0;
            titleRegion.Add(chevron);

            // NULL means "not supplied — use the property's own name"; EMPTY means "no title at all".
            // Exactly the distinction CollectionNode.TitleOverride draws (see collection-title-override).
            // Collapsing the two is what made Hide Label a no-op here: BindingFactory passes "" for a hidden
            // label, `string.IsNullOrEmpty` folded that back to "unset", and the field name reappeared.
            bool titleHidden = spec.Title != null && spec.Title.Length == 0;
            var title = new Label(titleHidden ? string.Empty : (spec.Title ?? labelProp.displayName));
            // THE AFFIX ANCHOR. Without one, FieldAffixes.ResolveHosts finds no unity-base-field within its
            // depth-2 reach (the table root's shallow descendants are the frame's header and body), falls all
            // the way through to the root fallback, and mounts a typed Prefix/Suffix on the OUTSIDE of the
            // whole frame — a stray "kg" floating above or below the entire table, decorating nothing.
            // Declaring the anchor puts it on the title's own line, where it reads as part of the header,
            // exactly the way UnityEventWidget and the curve editor place theirs.
            //
            // The section is hidden going forward (FieldAffixes.CapabilitiesFor has a dictionary arm now), so
            // this is for layouts authored BEFORE it was hidden — the same contract every card widget keeps:
            // do not offer it, but render an existing one somewhere sane.
            title.AddToClassList(FieldAffixes.AffixAnchorClass);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 12;
            title.style.color = WidgetPalette.TextStrong;
            titleRegion.Add(title);

            var countLabel = new Label("(0)");
            countLabel.style.marginLeft = 6;
            countLabel.style.fontSize = 11;
            countLabel.style.color = WidgetPalette.TextGray;
            // The count belongs to the title, so hiding one hides both — the same rule a collection header
            // follows. A lone "(3)" floating beside the chevron names nothing.
            if (titleHidden) countLabel.style.display = DisplayStyle.None;
            titleRegion.Add(countLabel);

            // 16px: the triangle-alert's interior "!" fuses into a smudge below ~14px (1px strokes).
            var headerWarn = SasquatchIcons.Create(SasquatchIcons.Ids.TriangleAlert, 16, DuplicateInk());
            headerWarn.style.marginLeft = 6;
            headerWarn.style.flexShrink = 0;
            headerWarn.style.display = DisplayStyle.None;
            headerWarn.tooltip = "This dictionary has duplicate keys.";
            titleRegion.Add(headerWarn);

            header.Add(titleRegion);

            // Icon + label as CHILDREN with no Button.text: a Button is itself a TextElement, so setting text
            // AND inserting an icon child renders both on top of each other (the button's own text is not part
            // of the flex layout its children use).
            var addBtn = new Button();
            addBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Plus, 10));
            var addLabel = new Label("Add") { pickingMode = PickingMode.Ignore };
            addLabel.style.marginLeft = 3;
            addBtn.Add(addLabel);
            addBtn.style.flexDirection = FlexDirection.Row;
            addBtn.style.alignItems = Align.Center;
            addBtn.style.paddingLeft = 6;
            addBtn.style.paddingRight = 8;
            addBtn.style.fontSize = 11;
            addBtn.style.flexShrink = 0;
            // Row search. Sits between the title and Add so the eye reads [what this is] [find one] [add one],
            // and hidden until the dictionary is long enough to need it (see SearchRevealThreshold) so a
            // three-entry table looks exactly as it always did.
            var searchField = new TextField();
            searchField.AddToClassList("sasq-collection-search"); // the collection's own box, same look
            searchField.textEdition.placeholder = "Search keys and values...";
            searchField.style.flexShrink = 1;
            searchField.style.flexGrow = 0;
            searchField.style.width = 150;
            searchField.style.marginRight = 4;
            searchField.style.display = DisplayStyle.None;
            // Design select-only, exactly as MakeCollectionSearchField does it: on the canvas a click must
            // fall through and SELECT the node rather than put a caret in a preview's search box.
            if (spec.DesignMode) LayoutTreeBuilder.SuppressInteractionForDesign(searchField);
            header.Add(searchField);

            EditorSpringFX.ButtonSpring(addBtn);
            header.Add(addBtn);

            frame.Add(header);

            // Collapsible body: [duplicate banner] [column header] [rows]
            var body = new VisualElement();
            frame.Add(body);

            var banner = CreateDuplicateBanner(out var bannerLabel);
            body.Add(banner);

            // MULTI-EDIT SAFETY RAIL. Every structural edit here goes through the multi-target
            // SerializedObject, whose arraySize reads target[0] and whose insert/delete forces every OTHER
            // target to that length — so on a selection whose dictionaries differ in size, one press of Add
            // truncates the longer ones and destroys their tail entries with no warning. Measured, not
            // reasoned: see multi-edit-array-append-destroys-data. Reorder already refuses this case
            // (CollectionRowReorder.CommitMove), so refusing the SIZE-changing controls too is the
            // consistent answer rather than a new rule.
            var multiEditNotice = CreateMultiEditNotice();
            body.Add(multiEditNotice);

            // Ledger sinks the column header AND the rows into one recessed well so the zebra bands and the
            // column rule read as a single continuous surface; every other style hangs them on the body.
            // The banner stays OUTSIDE the well either way -- it is a warning about the data, not part of it.
            VisualElement rowsHost = body;
            if (spec.Style == DictionaryTableStyle.Ledger)
            {
                var well = CreateLedgerWell();
                body.Add(well);
                rowsHost = well;
            }

            var columnHeader = BuildColumnHeader(spec);
            if (columnHeader != null)
            {
                // A Card row takes a rail's width out of its own left edge and the tray's padding out of its
                // right. The header must take EXACTLY the same bite or every growing column absorbs a share
                // of the difference and the cells drift left of their names -- worse the further right you
                // go. Same single decision point the collection table uses, so the two cannot disagree.
                LayoutTreeBuilder.ApplyTableCardHeaderInset(columnHeader, spec.RowSeparator);
                rowsHost.Add(columnHeader);
            }

            var rows = new VisualElement();
            rowsHost.Add(rows);

            // ================================ behaviour =====================================================
            System.Action recomputeDuplicates = () =>
            {
                // PREFER THE PLATFORM'S OWN ANSWER. Unity 6.6 can name the exact rows its runtime will drop
                // (GetDictionaryIgnoredEntries), which is strictly better than re-deriving them from key text:
                // it covers key types KeyToken has no case for, and it agrees with the runtime by construction.
                // Without one — our own SerializedDictionary, and every editor before 6.6 — fall back to
                // counting KeyTokens, which is what this widget always did.
                var flagged = new HashSet<int>();
                var nullKeys = new HashSet<int>();
                bool authoritative = spec.DuplicateIndices != null;

                if (authoritative)
                {
                    foreach (int i in spec.DuplicateIndices() ?? System.Array.Empty<int>()) flagged.Add(i);
                    if (spec.NullKeyIndices != null)
                        foreach (int i in spec.NullKeyIndices() ?? System.Array.Empty<int>()) nullKeys.Add(i);
                }
                else
                {
                    // Tokenise ONCE per row into a scratch array. This runs on every keystroke in any key
                    // cell, and CompositeKeyToken can walk a struct key's whole subtree, so reading each key
                    // twice (as this did) was real work on a long dictionary.
                    var tokens = new string[allKeyProps.Count];
                    var counts = new Dictionary<string, int>();
                    for (int i = 0; i < allKeyProps.Count; i++)
                    {
                        // An unset REFERENCE key is dropped by the runtime (the cache skips null keys), so it
                        // is an empty key, not a duplicate. Two unset rows both tokenised to "o:0" and were
                        // reported as duplicates of each other — the wrong problem, pointing at the wrong
                        // fix, while a SINGLE unset key was reported as nothing at all even though the
                        // runtime discards it just the same. Counted separately, and kept out of the
                        // duplicate scan so the two marks never contradict each other.
                        if (IsUnsetReferenceKey(allKeyProps[i])) { nullKeys.Add(i); continue; }
                        tokens[i] = KeyToken(allKeyProps[i]);
                        if (tokens[i] == null) continue;
                        counts.TryGetValue(tokens[i], out int n);
                        counts[tokens[i]] = n + 1;
                    }
                    for (int i = 0; i < allKeyProps.Count; i++)
                        if (tokens[i] != null && counts.TryGetValue(tokens[i], out int n) && n > 1) flagged.Add(i);
                }

                // Read off the WHOLE dictionary, not off the painted rows: the banner and the header warning
                // are statements about the DATA, so a duplicate hidden behind a search query must still raise
                // them. (Before the search existed these were OR-ed over the rendered rows, which was the same
                // thing only because every row was rendered.)
                bool anyDup = flagged.Count > 0, anyNull = nullKeys.Count > 0;
                for (int i = 0; i < keyProps.Count; i++)
                {
                    int raw = i < rowIndices.Count ? rowIndices[i] : i;
                    bool dup = flagged.Contains(raw);
                    bool nul = nullKeys.Contains(raw);
                    keyWarns[i].style.display = (dup || nul) ? DisplayStyle.Flex : DisplayStyle.None;
                    keyWarns[i].tooltip = nul
                        ? "Empty key — this row is skipped at runtime."
                        : "Duplicate key — only the first occurrence is used at runtime.";
                    // NOT StyleKeyword.Null for the unflagged case. Mapping paints the key cell's chip fill
                    // as an INLINE backgroundColor (ApplyMappingChip), and this line runs at the end of every
                    // buildRows — so clearing to Null wiped the accent wash on every ordinary row, leaving
                    // the chip as a bare outline and showing its fill only on a row that happened to be
                    // flagged. The cell carries no USS class, so nothing restored it. Put back the style's
                    // own resting fill rather than clearing to nothing.
                    keyCells[i].style.backgroundColor = (dup || nul)
                        ? DuplicateCellBg()
                        : (spec.Style == DictionaryTableStyle.Mapping
                            ? new StyleColor(WidgetPalette.AccentSoft)
                            : new StyleColor(StyleKeyword.Null));
                    // NOTE: anyDup / anyNull are NOT accumulated here any more - they are read off the whole
                    // dictionary above, so a flagged row hidden by a search query still raises the banner.
                }

                bannerLabel.text = anyDup && anyNull
                    ? "Duplicate keys, and empty keys — those rows are skipped at runtime."
                    : anyNull && !anyDup
                        ? "Empty keys — those rows are skipped at runtime."
                        : "Duplicate keys — only the first occurrence of each is used at runtime.";
                banner.style.display = (anyDup || anyNull) ? DisplayStyle.Flex : DisplayStyle.None;
                headerWarn.style.display = (anyDup || anyNull) ? DisplayStyle.Flex : DisplayStyle.None;
            };

            System.Action rebuild = null;

            System.Action buildRows = () =>
            {
                rows.Clear();
                keyProps.Clear();
                keyCells.Clear();
                keyWarns.Clear();
                rowIndices.Clear();
                allKeyProps.Clear();

                // Re-asked on every rebuild rather than captured once: the selection can change under a live
                // inspector, and a stale answer would either leave the rail up on a safe single selection or
                // — far worse — drop it on a dangerous mixed one.
                bool structuralLocked = CollectionRowReorder.TargetsDisagreeOnLength(arrayProp, serializedObject);
                multiEditNotice.style.display = structuralLocked ? DisplayStyle.Flex : DisplayStyle.None;
                addBtn.SetEnabled(!structuralLocked);
                addBtn.tooltip = structuralLocked
                    ? "The selected objects have different numbers of entries — adding would overwrite the longer ones."
                    : null;

                int count = arrayProp.arraySize;
                lastBuiltSize = count;

                // Every key, raw order - the duplicate scan's input, and it must not narrow with the filter.
                for (int i = 0; i < count; i++)
                    allKeyProps.Add(arrayProp.GetArrayElementAtIndex(i).FindPropertyRelative(spec.KeyRelative));

                // The search box earns its place only on a table long enough to need it. Re-evaluated on
                // every build, so a dictionary that grows past the threshold gains the box without a
                // reselect - and one that shrinks below it loses the box AND its query, which is why the
                // value is cleared here rather than left to filter rows nobody can see a box for.
                bool searchable = count >= SearchRevealThreshold;
                searchField.style.display = searchable ? DisplayStyle.Flex : DisplayStyle.None;
                if (!searchable && !string.IsNullOrEmpty(searchField.value))
                    searchField.SetValueWithoutNotify(string.Empty);

                string query = searchable ? searchField.value?.Trim() : null;
                bool filtering = !string.IsNullOrEmpty(query);

                // RAW array indices, in array order. Reuses the collection's own row matcher, so "what counts
                // as a match" is one decision for a list row and a dictionary row - it walks the element and
                // its children, which for a dictionary entry is exactly the key AND the value.
                var visible = new List<int>(count);
                for (int i = 0; i < count; i++)
                    if (!filtering || CollectionViewModel.RowMatchesQuery(arrayProp.GetArrayElementAtIndex(i), query))
                        visible.Add(i);

                countLabel.text = filtering ? "(" + visible.Count + " of " + count + ")" : "(" + count + ")";

                if (count > 0 && visible.Count == 0)
                {
                    var none = new Label("No entries match “" + query + "”.");
                    none.style.color = WidgetPalette.TextGray;
                    none.style.unityFontStyleAndWeight = FontStyle.Italic;
                    none.style.fontSize = 11;
                    none.style.paddingTop = 4;
                    none.style.paddingBottom = 2;
                    if (spec.Style == DictionaryTableStyle.Ledger)
                    {
                        none.style.paddingLeft = 6;
                        none.style.paddingRight = 6;
                        none.style.paddingTop = 6;
                        none.style.paddingBottom = 6;
                    }
                    rows.Add(none);
                    recomputeDuplicates();
                    return;
                }

                if (count == 0)
                {
                    var empty = new Label("Empty — click Add to create the first entry.");
                    empty.style.color = WidgetPalette.TextGray;
                    empty.style.unityFontStyleAndWeight = FontStyle.Italic;
                    empty.style.fontSize = 11;
                    empty.style.paddingTop = 4;
                    empty.style.paddingBottom = 2;
                    // Ledger's empty state sits INSIDE the well, which has no padding of its own (the rows
                    // are full-bleed by design), so the text would otherwise start hard against the border.
                    if (spec.Style == DictionaryTableStyle.Ledger)
                    {
                        empty.style.paddingLeft = 6;
                        empty.style.paddingRight = 6;
                        empty.style.paddingTop = 6;
                        empty.style.paddingBottom = 6;
                    }
                    rows.Add(empty);
                    recomputeDuplicates();
                    return;
                }

                for (int slot = 0; slot < visible.Count; slot++)
                {
                    int index = visible[slot];
                    var elem = arrayProp.GetArrayElementAtIndex(index);
                    var keyProp = elem.FindPropertyRelative(spec.KeyRelative);
                    var valProp = elem.FindPropertyRelative(spec.ValueRelative);

                    var row = new VisualElement();
                    row.userData = index; // reorder target — RAW array index (see CollectionRowReorder)
                    row.style.flexDirection = FlexDirection.Row;
                    // TOP, like the collection table row: a tall VALUE widget (curve, gradient, object preview)
                    // would otherwise sink the key field into the middle of the row. The cells keep their own
                    // Align.Center (see MakeCell) — that centers the warning icon against the field beside it,
                    // inside a cell that is now only as tall as its own content.
                    row.style.alignItems = Align.FlexStart;
                    row.AddToClassList(LayoutTreeBuilder.TableRowClass);

                    // The row FRAME (bare / hairline / tray) is painted by the collection table's own
                    // ApplyRowSeparator -- the single decision point for that chrome, so a dictionary row
                    // frame and a list row frame are the same code and cannot drift. isTable:true buys the
                    // tighter vertical rhythm a many-row widget needs, and it also sets the 2px marginBottom
                    // this row used to hardcode.
                    //
                    // Zebra is Ledger's alone: it is what makes a full-bleed, boxless row scannable, and it
                    // is exactly the wrong thing under a value that paints its own background (a colour
                    // swatch, a gradient strip). Passing it here rather than at the call site matters --
                    // Card mode has to COMPOSITE it over the tray fill instead of replacing it.
                    // The zebra follows the VISIBLE position, not the raw index. Striping by raw index under
                    // a filter drops bands wherever a row was hidden, so the surface reads as ragged rather
                    // than alternating - the same page-relative rule the collection's own zebra follows.
                    Color? zebraTint = (spec.Style == DictionaryTableStyle.Ledger && (slot % 2) == 1)
                        ? WidgetPalette.ZebraStripe
                        : (Color?)null;
                    LayoutTreeBuilder.ApplyRowSeparator(
                        row, spec.RowSeparator, spec.SeparatorColor,
                        isLast: slot == visible.Count - 1, isTable: true, zebraTint: zebraTint,
                        // Design mode opts out of the rail's hover, exactly as a List and a Table row do:
                        // on the canvas a hover already means "this is the node you'd select", and two
                        // hover languages fight over the same pixels.
                        hoverable: !spec.DesignMode);

                    // ---- STYLE-OWNED ROW RHYTHM, applied AFTER the frame -----------------------------------
                    // The enum's own contract (and manual.html) is "every style works with every frame": the
                    // STYLE owns the density, the FRAME owns the edge. Both corrections below used to be gated
                    // on RowSeparator == None, which meant the style silently lost its identity the moment a
                    // frame was chosen -- Ledger + Line was Ledger with its continuous rule cut into segments,
                    // and Minimal + Line was exactly as dense as Table + Line.

                    // Ledger's rows are FLUSH: that is what makes the zebra bands and the column rule read as
                    // one continuous surface, which is the whole point of the style. ApplyRowSeparator leaves a
                    // 2px margin under a row for None AND for Line, and that margin sits OUTSIDE the row's
                    // background box -- so the well showed through between every band and cut the rule.
                    // Card is deliberately excluded: a tray IS a separated row, and gluing trays together
                    // would destroy the frame rather than compose with it.
                    if (spec.Style == DictionaryTableStyle.Ledger && spec.RowSeparator != CollectionRowSeparator.Card)
                    {
                        row.style.marginBottom = 0;
                        row.style.paddingTop = 2;
                        row.style.paddingBottom = 2;
                    }
                    // Minimal buys its air the way the LIST's Minimal does -- unconditionally, after the
                    // separator pass, as PADDING rather than margin. Padding lives inside a Card tray's fill
                    // and inside a Line row's border box, so it survives every frame; a margin does not.
                    // Mirrors LayoutTreeBuilder.ApplyListRowStyleGeometry, whose 5px is the number a Minimal
                    // list sitting beside this dictionary uses -- the pairing is the stated point of the enum.
                    else if (spec.Style == DictionaryTableStyle.Minimal)
                    {
                        row.style.paddingTop = 5;
                        row.style.paddingBottom = 5;
                    }
                    // NESTED-BUILD BOUNDARY. The two CreateBindingField calls below build whole Sasquatch
                    // fields inside this row, and FieldAffixes.FindAnchor - which searches DOWNWARD from the
                    // affixed field, unbounded - stops only at "sasq-build-root". Without this stamp, a unit
                    // typed on the DICTIONARY field itself migrates into row 0 the moment any row renders a
                    // widget that plants an affix anchor (proved by planting one: the outer "kg" landed
                    // inside sasq-table-row). No buyer can reach it today only because dictionary keys and
                    // values render Unity's stock drawers, which stamp no anchor - luck, not design, and
                    // AnimationCurve:187 and UnityEventWidget:70 stamp UNCONDITIONALLY one routing change
                    // away. Bounding FindAnchor instead was tried and rejected: a card widget legitimately
                    // puts its anchor several levels down, which is why that search is unbounded at all.
                    row.AddToClassList("sasq-build-root");

                    var grip = LayoutTreeBuilder.MakeReorderGrip();
                    grip.style.width = GripWidth;
                    grip.style.flexShrink = 0;
                    // REORDER IS OFF WHILE A QUERY IS ACTIVE, exactly as it is on a filtered collection. A
                    // drag moves a RAW array index, and with rows hidden the two rows either side of the drop
                    // are not the array neighbours the user is looking at - so the row would land somewhere
                    // they did not aim. Hidden rather than removed: the lane keeps its width, so the columns
                    // do not shift the moment somebody types.
                    if (filtering)
                    {
                        grip.style.visibility = Visibility.Hidden;
                        grip.pickingMode = PickingMode.Ignore;
                        grip.tooltip = null;
                    }
                    row.Add(grip);

                    // Key cell (warning icon + field). Mapping pins the lane instead of sharing the row by
                    // fraction: the chip is a fixed affordance, and a growing one would make every row a
                    // different shape as keys were typed.
                    var keyCell = MakeCell(spec.KeyColumnFraction, spec.Style);
                    if (spec.Style == DictionaryTableStyle.Mapping)
                    {
                        keyCell.style.flexGrow = 0;
                        keyCell.style.flexShrink = 0;
                        keyCell.style.flexBasis = MappingChipLaneWidth;
                    }
                    // Where the warning icon and the key field actually go. For every style but Ledger that
                    // is the cell itself; Ledger needs one more level, see below.
                    var keyContent = keyCell;
                    if (spec.Style == DictionaryTableStyle.Ledger)
                    {
                        // Half of the continuous column rule (the header cell draws the other half). The rule
                        // has to span the row's FULL height, so the cell stretches -- but a stretched cell
                        // whose own alignItems is Center (MakeCell) then floats its content to the vertical
                        // MIDDLE of a tall row, which is precisely what the row's Align.FlexStart exists to
                        // prevent: an expanded struct value or a curve sank the key field to the middle of
                        // the row while every other style, and every list/table row, kept it on the first
                        // eye-line. Split the two jobs -- the CELL is the box the rule is drawn on, and a
                        // top-hugging inner row carries the content with the icon still centred against the
                        // field beside it.
                        keyCell.style.borderRightWidth = 1;
                        keyCell.style.borderRightColor = WidgetPalette.BorderSofter;
                        keyCell.style.alignSelf = Align.Stretch;
                        keyCell.style.flexDirection = FlexDirection.Column;
                        keyCell.style.justifyContent = Justify.FlexStart;
                        keyCell.style.alignItems = Align.Stretch;

                        keyContent = new VisualElement();
                        keyContent.style.flexDirection = FlexDirection.Row;
                        keyContent.style.alignItems = Align.Center;
                        keyCell.Add(keyContent);
                    }
                    var warn = SasquatchIcons.Create(SasquatchIcons.Ids.TriangleAlert, 16, DuplicateInk());
                    warn.style.display = DisplayStyle.None;
                    warn.style.marginRight = 2;
                    warn.style.flexShrink = 0;
                    warn.tooltip = "Duplicate key — only the first occurrence is used at runtime.";
                    keyContent.Add(warn);
                    var keyField = BuildCellField(spec.KeyTemplate, spec.KeyRelative, keyProp, serializedObject);
                    keyField.style.flexGrow = 1;
                    keyContent.Add(keyField);
                    if (spec.Style == DictionaryTableStyle.Mapping) ApplyMappingChip(keyCell, keyField);
                    row.Add(keyCell);

                    // The lane between the two columns. Mapping spends it on the arrow that replaces the
                    // column header; Ledger spends it on nothing, because its two lanes meet at the rule.
                    if (spec.Style == DictionaryTableStyle.Mapping)
                    {
                        var arrowLane = new VisualElement { pickingMode = PickingMode.Ignore };
                        arrowLane.style.width = MappingArrowLaneWidth;
                        arrowLane.style.flexGrow = 0;
                        arrowLane.style.flexShrink = 0;
                        arrowLane.style.alignItems = Align.Center;
                        arrowLane.style.justifyContent = Justify.Center;
                        arrowLane.style.alignSelf = Align.Center;
                        arrowLane.Add(SasquatchIcons.Create(SasquatchIcons.Ids.ArrowRight, 12, WidgetPalette.TextGray));
                        row.Add(arrowLane);
                    }
                    else if (spec.Style != DictionaryTableStyle.Ledger)
                    {
                        row.Add(Spacer(DividerWidth));
                    }

                    // Value cell — full Sasquatch field treatment (colour swatch, object picker, Spring widget, …).
                    var valCell = MakeCell(1f - spec.KeyColumnFraction, spec.Style);
                    // Mapping gave the key a fixed lane, so the value takes everything that is left rather
                    // than its old fraction -- otherwise the two would not add up to the row.
                    if (spec.Style == DictionaryTableStyle.Mapping) valCell.style.flexGrow = 1;
                    // A COMPOUND value (a [Serializable] struct, a List<>) has no widget of its own, so
                    // PropertyField renders it as a foldout — collapsed, and labelled "Value", which the column
                    // header already says. That reads as an empty row and costs a click per entry to reach the
                    // data. Unity's own DictionaryView shows those children inline, so starting expanded is
                    // parity, not a flourish. Only compound values are touched: a plain string/int/Color has
                    // no children and is unaffected, and the user's own collapse still sticks for the rest of
                    // the session (isExpanded is editor-only UI state, never serialized game data).
                    //
                    // ONCE PER PATH, not once per build. buildRows re-runs in full on every Add, Delete,
                    // Duplicate and Insert, so writing isExpanded unconditionally re-opened every value the
                    // user had collapsed each time the table changed shape -- the exact opposite of what the
                    // paragraph above claims. nudgedOpen remembers the paths this table instance has already
                    // nudged, so the courtesy happens on first sight and the foldout is the user's after that.
                    if (valProp.hasVisibleChildren && valProp.propertyType == SerializedPropertyType.Generic
                        && nudgedOpen.Add(valProp.propertyPath))
                        valProp.isExpanded = true;
                    var valField = BuildCellField(spec.ValueTemplate, spec.ValueRelative, valProp, serializedObject);
                    valField.style.flexGrow = 1;
                    valCell.Add(valField);
                    row.Add(valCell);

                    // Delete.
                    var del = new Button(() => CollectionRowMenu.Delete(arrayProp, serializedObject, index, rebuild));
                    del.tooltip = structuralLocked
                        ? "The selected objects have different numbers of entries — deleting would overwrite the longer ones."
                        : "Remove this entry";
                    del.SetEnabled(!structuralLocked);
                    del.Add(SasquatchIcons.Create(SasquatchIcons.Ids.X, 9));
                    StyleRowButton(del);
                    // Hover-revealed, like every List and Table row action. The row already carries
                    // TableRowClass, so the shipped ".sasq-table-row:hover .sasq-table-row-action" rule picks
                    // this up with no new USS -- and a 20-entry dictionary stops showing a column of 20 delete
                    // buttons competing with the data. Opacity, not display: the lane keeps its width, so
                    // nothing reflows as the pointer travels down the table.
                    del.AddToClassList(LayoutTreeBuilder.TableRowActionClass);
                    row.Add(del);

                    rows.Add(row);

                    // Reuse the collection table's live-row machinery verbatim (both operate on any array property).
                    if (!filtering)
                        CollectionRowReorder.Attach(grip, row, rows, arrayProp, serializedObject, recomputeDuplicates);
                    // Duplicate / Insert / Delete all resize the array, so the row menu goes away with the
                    // buttons on a mixed-length multi-selection rather than offering a command that would
                    // silently destroy the other targets' entries.
                    if (!structuralLocked)
                        CollectionRowMenu.Attach(row, arrayProp, serializedObject, index, rebuild,
                            // Insert creates a BLANK element, which matches no query - so behind a live
                            // filter it rendered nothing and read as a dead menu item. Deliberately NOT wired
                            // to Duplicate: a clone keeps the original's values, so it still matches the same
                            // query and appears right beside its twin. Same split the collection makes.
                            onRevealInserted: () =>
                            {
                                if (!string.IsNullOrEmpty(searchField.value))
                                    searchField.SetValueWithoutNotify(string.Empty);
                            },
                            standalone: spec.StandaloneRowMenu,
                            // Duplicate clones the element VERBATIM, which on a dictionary means a key the
                            // runtime is guaranteed to discard -- the new row landed already red, every time,
                            // for a command the user reached for to copy a VALUE. Same decision the Add button
                            // makes, carried into the menu so the two cannot disagree. (Insert needs nothing:
                            // CollectionRowMenu.ResetToDefault already blanks the whole element.)
                            onNewElement: spec.ClearKeyOnNewRow
                                ? (System.Action<SerializedProperty>)(fresh => ClearKey(fresh?.FindPropertyRelative(spec.KeyRelative)))
                                : null);
                    // Re-check duplicates whenever this row's key value changes (value edits never rebuild).
                    keyCell.TrackPropertyValueWithRefresh(keyProp, _ => recomputeDuplicates());

                    keyProps.Add(keyProp);
                    keyCells.Add(keyCell);
                    keyWarns.Add(warn);
                    rowIndices.Add(index);
                }

                rows.Bind(serializedObject);
                recomputeDuplicates();
            };

            rebuild = () =>
            {
                serializedObject.Update();
                buildRows();
            };

            // Typing filters. Registered AFTER `rebuild` is assigned, and it re-runs the whole row build
            // rather than toggling display on existing rows: the rows are Sasquatch fields bound to specific
            // array elements, and hiding one leaves its bindings and its row menu live on a row nobody can
            // see. Same call the Add button makes.
            searchField.RegisterValueChangedCallback(_ => rebuild());

            addBtn.clicked += () =>
            {
                // A NEW ROW IS BLANK, AND A BLANK ROW MATCHES NOTHING. Adding behind a live query appended a
                // row that filtered straight back out, so the button read as dead - the invisible-append trap
                // the collection hit with Insert. Drop the query first: the user asked for a new row, and a
                // new row they cannot see is not one.
                if (!string.IsNullOrEmpty(searchField.value)) searchField.SetValueWithoutNotify(string.Empty);
                arrayProp.arraySize++;
                // Unity grows an array by CLONING the previous last element, which on a dictionary means the
                // brand-new row arrives already carrying an existing key — instantly red, for something the
                // user has not typed yet. Unity's own dictionary "+" adds a BLANK row, and so does this, for
                // BOTH shapes: the bonus SerializedDictionary used to hand out the guaranteed-duplicate row
                // from the same button in the same table, which is two answers to one question.
                if (spec.ClearKeyOnNewRow && arrayProp.arraySize > 0)
                {
                    var fresh = arrayProp.GetArrayElementAtIndex(arrayProp.arraySize - 1)
                                         .FindPropertyRelative(spec.KeyRelative);
                    ClearKey(fresh);
                }
                serializedObject.ApplyModifiedProperties();
                rebuild();
            };

            // `persist` splits APPLYING the state from SAVING it. The initial call is not a user decision --
            // it is this build echoing back what EditorPrefs just said -- and writing it anyway meant every
            // inspector build and every layout rebuild performed one EditorPrefs write per dictionary on
            // screen, i.e. a registry write per dictionary per rebuild on Windows. The equality guard covers
            // the rest (a click that lands on the value already stored, e.g. after an undo).
            void SetExpanded(bool value, bool animate, bool persist = true)
            {
                expanded = value;
                if (persist && EditorPrefs.GetBool(expandKey, true) != value) EditorPrefs.SetBool(expandKey, value);
                body.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                EditorSpringFX.SpringCaret(chevron, value, animate);
            }

            titleRegion.RegisterCallback<ClickEvent>(_ => SetExpanded(!expanded, true));
            SetExpanded(expanded, false, persist: false);

            // The table maintains its rows through its OWN Add / ✕ / row menu. Anything that resizes the
            // array from OUTSIDE — Ctrl+Z after an Add, a ButtonNode method calling dict.Add(), a running
            // script, a second inspector, a prefab revert — ran nothing in here, so the rows sat at the stale
            // count: the header lied, a removed entry kept a ghost row bound to a dead element, and that
            // row's ✕ was dead because CollectionRowMenu.Delete bounds-checks and returns.
            //
            // UndoRebuildGate does NOT cover the common case. Its baseline is captured only on a full body
            // build, so undoing this widget's own Add lands the fingerprint back exactly on that baseline and
            // the gate correctly answers "nothing structural moved" — which is why the Add-then-Ctrl+Z case
            // needs the helper's own Undo.undoRedoPerformed hook rather than the gate.
            //
            // Must come after `rebuild` is assigned, and the count probe is keyProps.Count (cleared and
            // refilled by buildRows, 0 in the empty state) so the widget's own edits never rebuild twice.
            // renderedCount is the array size these rows were LAST BUILT FROM, not the number of rows on
            // screen. The two were the same number until the search box existed; they are not any more, and
            // handing over the rendered count would make `renderedCount() == now` permanently false the
            // moment a query hides a row - a rebuild on every binding tick, forever.
            LayoutTreeBuilder.AttachExternalArraySizeSync(
                root, arrayProp, designMode: false, renderedCount: () => lastBuiltSize, rebuildRowsInPlace: rebuild);

            buildRows();
            return root;
        }

        // ------------------------------------------------------------------------------------------------------
        // Duplicate-key token: a stable per-key string so equal keys collide. Covers the key types anyone
        // actually uses (string / int / enum / bool / float / char / object ref); unhandled types skip detection.
        // ------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Blanks a freshly-added row's key, so Unity's clone-the-last-element array growth does not hand the
        /// user a pre-filled duplicate. Covers the key types <see cref="KeyToken"/> covers; anything else is
        /// left alone rather than guessed at (a wrong reset would silently destroy data).
        /// </summary>
        private static void ClearKey(SerializedProperty p)
        {
            if (p == null) return;
            switch (p.propertyType)
            {
                case SerializedPropertyType.String: p.stringValue = string.Empty; break;
                case SerializedPropertyType.Integer: p.longValue = 0; break;
                case SerializedPropertyType.Character: p.intValue = 0; break;
                case SerializedPropertyType.Float: p.doubleValue = 0; break;
                case SerializedPropertyType.Boolean: p.boolValue = false; break;
                case SerializedPropertyType.ObjectReference: p.objectReferenceValue = null; break;
                // Enum deliberately NOT reset: every enum value is a legitimate key and 0 is as likely to
                // collide as the cloned one, so there is nothing to gain.
            }
        }

        /// <summary>
        /// True for a REFERENCE key left unset — the one shape this package's own dictionary silently drops
        /// at runtime (<c>SerializedDictionary</c>'s cache skips null keys). Deliberately NOT an empty
        /// string: Unity serializes a string field as <c>""</c>, never null, and <c>""</c> is a perfectly
        /// legitimate key that the runtime keeps — marking it would cry wolf on ordinary data.
        /// </summary>
        private static bool IsUnsetReferenceKey(SerializedProperty p) =>
            p != null
            && p.propertyType == SerializedPropertyType.ObjectReference
            && p.objectReferenceValue == null;

        /// <summary>
        /// Upper bound on the leaves <see cref="CompositeKeyToken"/> will walk before giving up. A key is a
        /// small value by nature; anything past this is a struct nobody is using as a dictionary key, and the
        /// walk is re-run for every row on every keystroke.
        /// </summary>
        private const int MaxCompositeKeyFields = 64;

        /// <summary>
        /// The scalar half of <see cref="KeyToken"/> — one stable string per directly-readable value type,
        /// or null for anything with no scalar reading of its own. Split out so the composite walk below can
        /// format a struct's leaves with exactly the same rules the top level uses.
        /// </summary>
        private static string LeafKeyToken(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.String: return "s:" + p.stringValue;
                case SerializedPropertyType.Integer: return "i:" + p.longValue;
                // enumValueFlag, NOT enumValueIndex. The index is a position in the type's NAME list, so a
                // [Flags] key holding a combined value that has no single name reads back as -1 — and every
                // such row tokenised identically, painting perfectly good rows as duplicates of each other.
                // enumValueFlag is the underlying value, which is what "the same key" actually means.
                case SerializedPropertyType.Enum: return "e:" + p.enumValueFlag;
                case SerializedPropertyType.Boolean: return "b:" + (p.boolValue ? 1 : 0);
                case SerializedPropertyType.Float: return "f:" + p.doubleValue;
                case SerializedPropertyType.Character: return "c:" + p.intValue;
                case SerializedPropertyType.ObjectReference:
                    // Via the shim, not p.objectReferenceInstanceIDValue: that property is an ERROR-level
                    // obsolete on Unity 6000.5 (CS0619 — it fails the build, it does not warn). Same string
                    // either way; the null case still yields "o:0". See UnityObjectId.
                    return "o:" + UnityObjectId.KeyOf(p.objectReferenceValue);
                default: return null;
            }
        }

        internal static string KeyToken(SerializedProperty p)
        {
            if (p == null) return null;
            return LeafKeyToken(p) ?? CompositeKeyToken(p);
        }

        /// <summary>
        /// Token for a key that has no scalar reading of its own: every <see cref="SerializedPropertyType"/>
        /// outside <see cref="LeafKeyToken"/>'s seven — <c>Vector2</c>/<c>3</c>/<c>4</c>, <c>Vector2Int</c>/
        /// <c>3Int</c>, <c>Color</c>, <c>Rect</c>/<c>RectInt</c>, <c>Bounds</c>/<c>BoundsInt</c>,
        /// <c>Quaternion</c>, <c>LayerMask</c> — plus any <c>[Serializable]</c> user struct.
        /// <para>
        /// This used to be <c>default: return null</c>, and a null token is SKIPPED by the duplicate scan.
        /// So a <c>SerializedDictionary&lt;Vector2Int, T&gt;</c> — a tile/grid map, one of the most ordinary
        /// reasons to reach for a serializable dictionary at all — got no duplicate mark, no red cell and no
        /// banner, while Unity's clone-the-last-element array growth handed the user a byte-identical key on
        /// every press of Add. The second entry was then dropped at runtime, silently, first-occurrence-wins.
        /// </para>
        /// <para>
        /// Every one of those types exposes serialized children, so ONE walk covers all of them and needs no
        /// per-type case analysis to keep in lockstep with Unity. Compound nodes contribute nothing
        /// themselves — the walk descends into them and their leaves land in declaration order, which is what
        /// makes the token stable between two structs of the same type. A leaf the scalar half cannot read
        /// abandons the whole token (null, i.e. no detection) rather than emitting a partial one that would
        /// collide two keys differing only in the part that was dropped.
        /// </para>
        /// </summary>
        private static string CompositeKeyToken(SerializedProperty p)
        {
            if (p == null || !p.hasVisibleChildren) return null;

            var sb = new System.Text.StringBuilder("g:");
            var it = p.Copy();
            var end = p.GetEndProperty();
            int fields = 0;

            while (it.NextVisible(true) && !SerializedProperty.EqualContents(it, end))
            {
                if (++fields > MaxCompositeKeyFields) return null;
                // A container: the walk is already descending into it, so it contributes only its shape.
                if (it.propertyType == SerializedPropertyType.Generic) { sb.Append('{'); continue; }

                string leaf = LeafKeyToken(it);
                if (leaf == null) return null;
                sb.Append(leaf).Append('|');
            }

            // Nothing readable came out of it (an empty struct): no token rather than a bare "g:" that every
            // such key would share.
            return fields == 0 ? null : sb.ToString();
        }

        // ------------------------------------------------------------------------------------------------------
        // Chrome helpers
        // ------------------------------------------------------------------------------------------------------
        /// <summary>
        /// The outer shell. Only <see cref="DictionaryTableStyle.Table"/> draws one: Minimal and Mapping are
        /// deliberately chromeless (spacing carries the rhythm, which is the whole point of them), and
        /// Ledger's shell is the recessed WELL around the rows -- bordering here as well would double it and
        /// leave the header inside a box it does not belong to.
        /// </summary>
        private static VisualElement CreateFrame(DictionaryTableStyle style)
        {
            var frame = new VisualElement();
            frame.style.flexDirection = FlexDirection.Column;

            if (style != DictionaryTableStyle.Table)
            {
                // A shell-less style still needs a hair of breathing room, or the header sits flush against
                // whatever field precedes it in the inspector.
                frame.style.paddingTop = 1;
                frame.style.paddingBottom = 2;
                return frame;
            }

            frame.style.borderTopWidth = 1;
            frame.style.borderBottomWidth = 1;
            frame.style.borderLeftWidth = 1;
            frame.style.borderRightWidth = 1;
            var border = WidgetPalette.BorderSofter;
            frame.style.borderTopColor = border;
            frame.style.borderBottomColor = border;
            frame.style.borderLeftColor = border;
            frame.style.borderRightColor = border;
            frame.style.borderTopLeftRadius = 4;
            frame.style.borderTopRightRadius = 4;
            frame.style.borderBottomLeftRadius = 4;
            frame.style.borderBottomRightRadius = 4;
            frame.style.paddingTop = 6;
            frame.style.paddingBottom = 6;
            frame.style.paddingLeft = 8;
            frame.style.paddingRight = 8;
            return frame;
        }

        /// <summary>
        /// Ledger's recessed well: the rows run edge to edge inside it, so the zebra bands read as one
        /// continuous surface. <c>overflow: hidden</c> is load-bearing -- without it a full-bleed zebra row
        /// paints straight over the well's rounded corners and squares them off.
        /// </summary>
        private static VisualElement CreateLedgerWell()
        {
            var well = new VisualElement();
            well.style.flexDirection = FlexDirection.Column;
            well.style.backgroundColor = WidgetPalette.WellBg;
            well.style.borderTopWidth = 1;
            well.style.borderBottomWidth = 1;
            well.style.borderLeftWidth = 1;
            well.style.borderRightWidth = 1;
            var edge = WidgetPalette.BorderSofter;
            well.style.borderTopColor = edge;
            well.style.borderBottomColor = edge;
            well.style.borderLeftColor = edge;
            well.style.borderRightColor = edge;
            well.style.borderTopLeftRadius = 4;
            well.style.borderTopRightRadius = 4;
            well.style.borderBottomLeftRadius = 4;
            well.style.borderBottomRightRadius = 4;
            well.style.overflow = Overflow.Hidden;
            return well;
        }

        /// <summary>
        /// The Key / Value column header, or <c>null</c> for <see cref="DictionaryTableStyle.Mapping"/>, which
        /// has no columns to name -- its arrow glyph carries the relationship the header used to.
        /// A null return is a real branch, not a failure: the caller skips the row entirely rather than adding
        /// an empty element, so Mapping does not pay a phantom row's margin.
        /// </summary>
        private static VisualElement BuildColumnHeader(TableSpec spec)
        {
            if (spec.Style == DictionaryTableStyle.Mapping) return null;

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;

            switch (spec.Style)
            {
                case DictionaryTableStyle.Minimal:
                    // No rule at all -- Minimal's whole premise is that spacing separates, not borders. The
                    // labels drop to small-caps hints so they read as annotation rather than as chrome.
                    head.style.marginBottom = 5;
                    break;

                case DictionaryTableStyle.Ledger:
                    // Inside the well: a faint tinted strip carrying the same rule the rows carry between
                    // their columns, so the column split runs unbroken from the header to the last row.
                    head.style.backgroundColor = WidgetPalette.ZebraStripe;
                    head.style.borderBottomWidth = 1;
                    head.style.borderBottomColor = WidgetPalette.BorderSofter;
                    head.style.paddingTop = 2;
                    head.style.paddingBottom = 2;
                    break;

                default: // Table -- unchanged from what this widget always drew.
                    head.style.marginBottom = 3;
                    head.style.paddingBottom = 2;
                    head.style.borderBottomWidth = 1;
                    head.style.borderBottomColor = WidgetPalette.BorderSofter;
                    break;
            }

            head.Add(Spacer(GripLaneWidth));
            head.Add(HeaderLabelCell(spec.KeyLabel, spec.KeyColumnFraction, spec.Style, isKey: true));
            // Ledger has no divider gutter: its two lanes meet at the column rule, so a 7px spacer between
            // them would push the value lane off the line the rule draws.
            if (spec.Style != DictionaryTableStyle.Ledger) head.Add(Spacer(DividerWidth));
            head.Add(HeaderLabelCell(spec.ValueLabel, 1f - spec.KeyColumnFraction, spec.Style, isKey: false));
            head.Add(Spacer(DeleteLaneWidth));
            return head;
        }

        private static VisualElement HeaderLabelCell(string text, float share, DictionaryTableStyle style, bool isKey)
        {
            var cell = new VisualElement();
            cell.style.flexGrow = share;
            cell.style.flexBasis = 0;
            cell.style.minWidth = CellMinWidth;
            ApplyCellLanePadding(cell, style);

            // Ledger's column rule is drawn by the KEY lane of the header AND of every row -- one border per
            // element, stacked, is what makes it read as a single unbroken line down the body.
            if (style == DictionaryTableStyle.Ledger && isKey)
            {
                cell.style.borderRightWidth = 1;
                cell.style.borderRightColor = WidgetPalette.BorderSofter;
            }

            var lbl = new Label(text);
            if (style == DictionaryTableStyle.Minimal || style == DictionaryTableStyle.Ledger)
            {
                // Small-caps hint. UI Toolkit has no text-transform, so uppercase the string itself.
                lbl.text = text.ToUpperInvariant();
                lbl.style.fontSize = 9;
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                lbl.style.letterSpacing = 1.2f;
                lbl.style.color = WidgetPalette.TextGray;
            }
            else
            {
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                lbl.style.fontSize = 11;
                lbl.style.color = WidgetPalette.LabelMuted;
            }
            cell.Add(lbl);
            return cell;
        }

        /// <summary>
        /// Horizontal padding for one lane, applied from ONE place to the header cell and the row cell alike.
        /// They must agree exactly: take padding out of the rows and not the header and every growing column
        /// absorbs a share of the difference, so the values drift out from under their own names -- the same
        /// register problem <c>LayoutTreeBuilder.ApplyTableCardHeaderInset</c> exists for.
        /// </summary>
        private static void ApplyCellLanePadding(VisualElement cell, DictionaryTableStyle style)
        {
            // Ledger's lanes butt up against a rule, so they need real gutters; every other style keeps the
            // original 2px, which is what this widget always shipped.
            float pad = style == DictionaryTableStyle.Ledger ? 5f : 2f;
            cell.style.paddingLeft = pad;
            cell.style.paddingRight = pad;
        }

        /// <summary>
        /// One row cell — the key half or the value half — built from the column's authored template when the
        /// layout supplied one, and from a bare binding when it did not.
        /// <para>
        /// <b>The template is handed to <c>CreateBindingField</c> AS-IS, against the cell's own resolved
        /// path.</b> That method takes the resolved path as its own argument and reads the node's
        /// <c>PropertyPath</c> for nothing but an error message, so no clone-and-rewrite is needed — which is
        /// what lets one authored template (whose path is empty by convention) drive both dictionary shapes,
        /// even though ours names its halves <c>Key</c>/<c>Value</c> and Unity's names them
        /// <c>key</c>/<c>value</c>.
        /// </para>
        /// <para>
        /// The two post-build passes are the ones a table CELL learned it needed the hard way (see
        /// <c>table-cells-bypassed-buildnode</c>): a cell built through the widget constructor alone silently
        /// drops the Appearance box and the Field Styling box, and the author sees them work on the designer
        /// canvas and do nothing in the real Inspector. The theme is deliberately null — this drawer is
        /// reached from four routes and two of them (the stock inspector, an exported editor) have no theme
        /// to resolve a style TOKEN against; a token is the only thing that needs one, and every direct
        /// override still lands.
        /// </para>
        /// <para>
        /// The registry stamp is what makes a styled cell right-clickable back to the node that styled it,
        /// the same way a table cell resolves through <c>LayoutNodeRegistry</c>.
        /// </para>
        /// </summary>
        private static VisualElement BuildCellField(BindingNode template, string relative,
                                                    SerializedProperty cellProp, SerializedObject so)
        {
            var node = template ?? new BindingNode { PropertyPath = relative };
            var field = BindingFactory.CreateBindingField(node, cellProp.propertyPath, so, forceHideLabel: true);
            if (template == null) return field;

            StyleApplier.Apply(field, template, null);
            LayoutTreeBuilder.ApplyNodeTextStyles(field, template);
            LayoutNodeRegistry.SetNode(field, template);
            return field;
        }

        private static VisualElement MakeCell(float share, DictionaryTableStyle style)
        {
            var cell = new VisualElement();
            cell.style.flexDirection = FlexDirection.Row;
            cell.style.alignItems = Align.Center;
            cell.style.flexGrow = share;
            cell.style.flexBasis = 0;
            cell.style.minWidth = CellMinWidth;
            // A lane is a narrow TRACK, exactly like a Columns/Grid cell: any stock row that ends up inside
            // it (a compound value's foldout children) must fit this cell's own gutter, never the
            // inspector-wide column. Without the scope those rows measure against the InspectorElement,
            // whose formula subtracts how far right the cell starts -- ~300px in on a two-column table --
            // and hands every label `width: 0`, which paints as a single truncation dot beside a full-width
            // field. See FieldLabelColumn.ScopeClass and label-column-in-track-scopes.
            cell.AddToClassList(FieldLabelColumn.ScopeClass);
            ApplyCellLanePadding(cell, style);
            cell.style.borderTopLeftRadius = 3;
            cell.style.borderTopRightRadius = 3;
            cell.style.borderBottomLeftRadius = 3;
            cell.style.borderBottomRightRadius = 3;
            return cell;
        }

        /// <summary>
        /// Turns the KEY cell into Mapping's chip: the cell takes the pill (fill, edge, full rounding) and the
        /// field inside it goes flat, so the pill is the only chrome and the key still edits normally.
        /// <para>
        /// The flatten is best-effort by design. <c>unity-base-field__input</c> is the field's own value box
        /// for every scalar key type; a key whose editor has no such box (a compound struct key) simply keeps
        /// its own chrome inside the pill -- visibly plainer, never broken, and never a null-ref.
        /// </para>
        /// </summary>
        private static void ApplyMappingChip(VisualElement keyCell, VisualElement keyField)
        {
            keyCell.style.backgroundColor = WidgetPalette.AccentSoft;
            keyCell.style.borderTopWidth = 1;
            keyCell.style.borderBottomWidth = 1;
            keyCell.style.borderLeftWidth = 1;
            keyCell.style.borderRightWidth = 1;
            var edge = WidgetPalette.AccentEdge;
            keyCell.style.borderTopColor = edge;
            keyCell.style.borderBottomColor = edge;
            keyCell.style.borderLeftColor = edge;
            keyCell.style.borderRightColor = edge;
            // A pill, not a rounded rect: half the 18px field height, so the ends are true semicircles.
            keyCell.style.borderTopLeftRadius = 9;
            keyCell.style.borderTopRightRadius = 9;
            keyCell.style.borderBottomLeftRadius = 9;
            keyCell.style.borderBottomRightRadius = 9;
            keyCell.style.paddingLeft = 7;
            keyCell.style.paddingRight = 7;
            keyCell.style.overflow = Overflow.Hidden;

            var input = keyField?.Q<VisualElement>(className: FieldAffixes.BaseFieldInputClass);
            if (input == null) return;
            input.style.backgroundColor = Color.clear;
            input.style.borderTopWidth = 0;
            input.style.borderBottomWidth = 0;
            input.style.borderLeftWidth = 0;
            input.style.borderRightWidth = 0;
            input.style.paddingLeft = 0;
            input.style.paddingRight = 0;
        }

        private static VisualElement Spacer(float width)
        {
            var s = new VisualElement { pickingMode = PickingMode.Ignore };
            s.style.width = width;
            s.style.flexGrow = 0;
            s.style.flexShrink = 0;
            return s;
        }

        private static void StyleRowButton(Button button)
        {
            button.style.fontSize = 10;
            button.style.width = DeleteWidth;
            button.style.flexShrink = 0;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            // Row chrome centres on the row; only CELLS top-align (the row is Align.FlexStart above, and the
            // reorder grip at the other end pins `align-self: center` in USS). Without this the ✕ hugged the
            // row's top edge the moment an entry's key or value grew past one line, leaving the row's two
            // ends half a row apart — the same defect the list and table rows carried.
            button.style.alignSelf = Align.Center;
            button.style.paddingLeft = 2;
            button.style.paddingRight = 2;
            button.style.marginLeft = 2;
            EditorSpringFX.ButtonSpring(button);
        }

        /// <summary>
        /// The rail shown when a multi-object selection's dictionaries hold different numbers of entries.
        /// Deliberately NEUTRAL rather than red: nothing is wrong with the user's data — some controls are
        /// simply unavailable for this selection — and reusing the duplicate-key red would say the opposite.
        /// Hidden by default; <c>buildRows</c> is the only thing that shows it.
        /// </summary>
        private static VisualElement CreateMultiEditNotice()
        {
            var note = new VisualElement();
            note.style.flexDirection = FlexDirection.Row;
            note.style.alignItems = Align.Center;
            note.style.display = DisplayStyle.None;
            note.style.backgroundColor = WidgetPalette.WellBg;
            var edge = WidgetPalette.BorderSofter;
            note.style.borderTopWidth = 1;
            note.style.borderBottomWidth = 1;
            note.style.borderLeftWidth = 1;
            note.style.borderRightWidth = 1;
            note.style.borderTopColor = edge;
            note.style.borderBottomColor = edge;
            note.style.borderLeftColor = edge;
            note.style.borderRightColor = edge;
            note.style.borderTopLeftRadius = 3;
            note.style.borderTopRightRadius = 3;
            note.style.borderBottomLeftRadius = 3;
            note.style.borderBottomRightRadius = 3;
            note.style.paddingLeft = 6;
            note.style.paddingRight = 6;
            note.style.paddingTop = 3;
            note.style.paddingBottom = 3;
            note.style.marginBottom = 5;

            var icon = SasquatchIcons.Create(SasquatchIcons.Ids.Info, 14, WidgetPalette.TextGray);
            icon.style.marginRight = 5;
            icon.style.flexShrink = 0;
            note.Add(icon);

            var msg = new Label(
                "These objects have different numbers of entries. Values are still editable; adding, " +
                "deleting and reordering are off until the counts match.");
            msg.style.fontSize = 11;
            msg.style.color = WidgetPalette.TextGray;
            msg.style.whiteSpace = WhiteSpace.Normal;
            msg.style.flexShrink = 1;
            note.Add(msg);
            return note;
        }

        private static VisualElement CreateDuplicateBanner(out Label message)
        {
            var banner = new VisualElement();
            banner.style.flexDirection = FlexDirection.Row;
            banner.style.alignItems = Align.Center;
            banner.style.display = DisplayStyle.None;
            banner.style.backgroundColor = DuplicateBannerBg();
            var edge = DuplicateInk();
            edge.a = 0.5f;
            banner.style.borderTopWidth = 1;
            banner.style.borderBottomWidth = 1;
            banner.style.borderLeftWidth = 1;
            banner.style.borderRightWidth = 1;
            banner.style.borderTopColor = edge;
            banner.style.borderBottomColor = edge;
            banner.style.borderLeftColor = edge;
            banner.style.borderRightColor = edge;
            banner.style.borderTopLeftRadius = 3;
            banner.style.borderTopRightRadius = 3;
            banner.style.borderBottomLeftRadius = 3;
            banner.style.borderBottomRightRadius = 3;
            banner.style.paddingLeft = 6;
            banner.style.paddingRight = 6;
            banner.style.paddingTop = 3;
            banner.style.paddingBottom = 3;
            banner.style.marginBottom = 5;

            var icon = SasquatchIcons.Create(SasquatchIcons.Ids.TriangleAlert, 16, DuplicateInk());
            icon.style.marginRight = 5;
            icon.style.flexShrink = 0;
            banner.Add(icon);

            message = new Label("Duplicate keys — only the first occurrence of each is used at runtime.");
            message.style.fontSize = 11;
            message.style.color = DuplicateInk();
            message.style.whiteSpace = WhiteSpace.Normal;
            banner.Add(message);
            return banner;
        }

        // ------------------------------------------------------------------------------------------------------
        // Skin-aware duplicate colours (a warm red that reads on both editor skins).
        // ------------------------------------------------------------------------------------------------------
        private static Color DuplicateInk() => WidgetPalette.IsLight
            ? new Color(0.72f, 0.16f, 0.16f, 1f)
            : new Color(0.98f, 0.52f, 0.52f, 1f);

        private static Color DuplicateCellBg() => WidgetPalette.IsLight
            ? new Color(0.85f, 0.20f, 0.20f, 0.12f)
            : new Color(0.95f, 0.35f, 0.35f, 0.14f);

        private static Color DuplicateBannerBg() => WidgetPalette.IsLight
            ? new Color(0.85f, 0.20f, 0.20f, 0.10f)
            : new Color(0.95f, 0.35f, 0.35f, 0.10f);
    }
}
