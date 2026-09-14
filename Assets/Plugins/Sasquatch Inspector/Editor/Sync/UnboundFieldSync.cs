using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>A bound path whose code field no longer exists (the node will be deleted).</summary>
    public sealed class RemovedBinding
    {
        /// <summary>Root-relative, unindexed path (e.g. "modifications.modName").</summary>
        public string ResolvedPath;
        /// <summary>True when the node lives inside a CollectionNode's ItemLayout template.</summary>
        public bool InItemLayout;
    }

    /// <summary>A removed+added pair identified as the same field renamed (node is rewritten in place).</summary>
    public sealed class RenamedBinding
    {
        public string OldResolvedPath;
        public string NewResolvedPath;
        /// <summary>True = matched via [FormerlySerializedAs] (definitive); false = type+scope heuristic.</summary>
        public bool ViaFormerlySerializedAs;
    }

    /// <summary>A field added to a collection's ELEMENT type that belongs in that collection's item
    /// template. Kept apart from <see cref="LayoutSyncDiff.Added"/> because it inserts into a different
    /// place (one collection's ItemLayout, not the node tree) and binds a different scope (element-relative,
    /// not root-resolved).</summary>
    public sealed class ElementFieldAdd
    {
        /// <summary>Root-relative path of the owning collection (e.g. "loot").</summary>
        public string CollectionPath;
        /// <summary>The field's path RELATIVE to the element, which is what the binding stores (e.g. "weight").</summary>
        public string RelativePath;
        /// <summary>Declared type, for preset seeding and attribute import.</summary>
        public Type FieldType;
    }

    /// <summary>Everything a reconcile pass would change, computed without mutating anything.</summary>
    public sealed class LayoutSyncDiff
    {
        public List<SasquatchReflectionUtility.ReflectedProperty> Added = new();
        /// <summary>New ELEMENT fields, each destined for one existing collection's item template.</summary>
        public List<ElementFieldAdd> AddedElementFields = new();
        /// <summary>Element paths to record in the ledger WITHOUT inserting anything — the no-surprise
        /// migration for a layout authored before element paths were tracked. Invisible bookkeeping, so it
        /// never counts as a visible change (a curated table must not sprout columns on upgrade).</summary>
        public List<string> ElementLedgerBackfill = new();
        public List<RemovedBinding> Removed = new();
        public List<RenamedBinding> Renamed = new();
        /// <summary>Scoped-container PropertyPath prefixes renamed via [FormerlySerializedAs] (applied first).</summary>
        public List<RenamedBinding> ScopeRenames = new();
        /// <summary>Already-BOUND field paths whose code [Space] hasn't been imported yet (attribute added after
        /// the field was laid out) — a Space widget is inserted directly above each.</summary>
        public List<string> SpaceAdds = new();
        /// <summary>Bound field paths whose [Space] is already represented by an existing Space widget above them —
        /// the node's <see cref="LayoutNode.SpaceImported"/> flag is backfilled with no visual change (layouts
        /// imported before the flag existed).</summary>
        public List<string> SpaceMarks = new();
        /// <summary>Previously-imported paths whose [Space] attribute is gone from code — the flag clears so
        /// re-adding the attribute later imports again. The spacer widget itself is left alone.</summary>
        public List<string> SpaceClears = new();
        /// <summary>Ledger entries whose path is neither bound anywhere in the tree nor live in code. The
        /// field itself is gone, which supersedes whatever the entry recorded ("deliberately removed" or a
        /// stale accounting) — dropping it makes a LATER re-add of the same field name count as new again,
        /// the same contract removals keep. Ledger-only bookkeeping; no tree change.</summary>
        public List<string> LedgerScavenge = new();
        public bool NeedsLedgerInit;

        public bool IsEmpty => !NeedsLedgerInit && Added.Count == 0 && Removed.Count == 0
                               && Renamed.Count == 0 && ScopeRenames.Count == 0
                               && SpaceAdds.Count == 0 && SpaceMarks.Count == 0 && SpaceClears.Count == 0
                               && LedgerScavenge.Count == 0
                               && AddedElementFields.Count == 0 && ElementLedgerBackfill.Count == 0;

        /// <summary>True when applying the diff visibly changes the rendered layout — SpaceMarks/SpaceClears are
        /// invisible bookkeeping and don't count (the banner mode applies those silently instead of nagging).</summary>
        public bool HasVisibleChanges => Added.Count > 0 || Removed.Count > 0
                                         || Renamed.Count > 0 || ScopeRenames.Count > 0 || SpaceAdds.Count > 0
                                         || AddedElementFields.Count > 0;
    }

    /// <summary>
    /// Auto-sync engine for unbound fields: detects serialized fields that exist in code but are
    /// absent from a type's layout, and inserts them positioned to match their placement in code.
    ///
    /// Split (like ValidationBinder/ButtonInvoker) into pure, headless-testable decision logic
    /// (<see cref="ComputeNewFields"/>, <see cref="InsertFields"/>) and the persistence wiring
    /// (<see cref="SyncLayout"/>: BeginLayoutEditUndo → mutate → ledger update → PersistLayoutEdit).
    ///
    /// "New" means added to code after the layout was last authored/synced — NOT merely unbound. The
    /// per-layout <c>SyncedFieldPaths</c> ledger records every path the layout has accounted for, so a
    /// field the user deliberately removed in the designer is never force-re-added.
    /// </summary>
    public static class UnboundFieldSync
    {
        // Reflection results per type. Statics reset on domain reload — exactly when a type's fields can
        // change — so these can't go stale across a compile; InvalidateReflectionCaches exists only to
        // re-read a type whose fields were misread mid-compile.
        private static readonly Dictionary<Type, List<SasquatchReflectionUtility.ReflectedProperty>> _allPropsCache = new();
        private static readonly Dictionary<Type, List<SasquatchReflectionUtility.ReflectedProperty>> _bindablePropsCache = new();
        // Element leaves per (declaring type, collection path). Memoised for the same reason as the two
        // above and one more: this one does real reflection (a FieldInfo per element leaf) and it runs from
        // ComputeDiff, which every inspector rebuild calls.
        private static readonly Dictionary<(Type, string), List<ElementLeaf>> _elementLeafCache = new();

        private class NodeLocation
        {
            public ContainerNode Parent;
            public int Index;
            public string BasePath;
        }

        /// <summary>
        /// Precomputed [Header] context for one insertion pass ("Smart Header Routing", Project Settings →
        /// Sasquatch Inspector, default ON). Built from reflection over the user's own type. Null wherever
        /// a routing parameter is accepted ⇒ routing disabled (the historical predecessor-adjacent insert).
        /// </summary>
        public sealed class HeaderRouting
        {
            /// <summary>Field path → the nearest [Header] at-or-above it in declaration order (absent = none).</summary>
            public Dictionary<string, string> ContextByPath = new();
            /// <summary>Header text → the field paths carrying that [Header] attribute (usually one; duplicate
            /// header strings in one type all record here).</summary>
            public Dictionary<string, List<string>> OwnersByHeader = new();
            /// <summary>Container type for sections created for brand-new headers (the preset structural default).</summary>
            public ContainerType NewSectionType = ContainerType.Card;
            // Sections created during THIS pass, so sibling new fields sharing a brand-new header land together.
            internal readonly Dictionary<string, ContainerNode> CreatedThisPass = new();
        }

        /// <summary>
        /// Builds the routing context for <paramref name="type"/>: walks the top-level bindable fields in
        /// declaration order (the same enumeration the importer sections on — INCLUDING [HideInInspector]
        /// fields, whose [Header] still opens a section) tracking the most recent [Header] string.
        /// </summary>
        public static HeaderRouting BuildHeaderRouting(Type type)
        {
            var routing = new HeaderRouting
            {
                NewSectionType = SasquatchLayoutDesignerWindow.DefaultHeaderContainerTypeOrFallback()
            };
            if (type == null) return routing;

            string current = null;
            foreach (var p in GetBindableProps(type))
            {
                string header = GetHeaderOf(type, p.path);
                if (!string.IsNullOrEmpty(header))
                {
                    current = header;
                    if (!routing.OwnersByHeader.TryGetValue(header, out var owners))
                        routing.OwnersByHeader[header] = owners = new List<string>();
                    owners.Add(p.path);
                }
                if (!string.IsNullOrEmpty(current)) routing.ContextByPath[p.path] = current;
            }
            return routing;
        }

        /// <summary>Every distinct [Header] string currently on <paramref name="type"/>'s bindable fields,
        /// in declaration order. Shared with the orphaned-header cleanup (deleted-header detection).</summary>
        public static List<string> GetCurrentHeaders(Type type)
        {
            var result = new List<string>();
            if (type == null) return result;
            var seen = new HashSet<string>();
            foreach (var p in GetBindableProps(type))
            {
                string header = GetHeaderOf(type, p.path);
                if (!string.IsNullOrEmpty(header) && seen.Add(header)) result.Add(header);
            }
            return result;
        }

        private static string GetHeaderOf(Type type, string path)
        {
            var fi = BindingFactory.GetFieldInfoFromPropertyPath(type, path, out _);
            if (fi == null) return null;
            return (Attribute.GetCustomAttribute(fi, typeof(HeaderAttribute)) as HeaderAttribute)?.header;
        }

        // A header is BRAND-NEW (create its section) — as opposed to one whose section the user
        // deleted (respect that) — iff every field carrying it is itself among the fields being
        // inserted. Derivable with no persisted state: a header that already existed in code always
        // has at least one carrier that is not new.
        private static bool IsBrandNewHeader(HeaderRouting routing, string header, HashSet<string> newFieldPaths)
        {
            if (!routing.OwnersByHeader.TryGetValue(header, out var owners) || owners.Count == 0) return false;
            foreach (var owner in owners)
            {
                if (!newFieldPaths.Contains(owner)) return false;
            }
            return true;
        }

        // Finds the container that renders the given [Header]'s section: provenance (SourceHeader) match
        // first — it survives the user retitling the section — then a Title match on a container with no
        // recorded provenance (layouts imported before provenance existed; the caller backfills). The
        // out basePath is the scope the found container's CHILDREN resolve against. Root never matches.
        private static bool TryFindHeaderContainer(ContainerNode root, string header,
            out ContainerNode found, out string foundBasePath, out bool viaTitleFallback)
        {
            ContainerNode byProvenance = null, byTitle = null;
            string provenanceBase = null, titleBase = null;

            void Walk(ContainerNode container, string basePath, bool isRoot)
            {
                if (byProvenance != null || container.Children == null) return;
                string selfBase = basePath;
                if (!isRoot)
                {
                    selfBase = string.IsNullOrEmpty(container.PropertyPath)
                        ? basePath
                        : Resolve(basePath, container.PropertyPath);
                    if (container.SourceHeader == header)
                    {
                        byProvenance = container;
                        provenanceBase = selfBase;
                        return;
                    }
                    if (byTitle == null && string.IsNullOrEmpty(container.SourceHeader) && container.Title == header)
                    {
                        byTitle = container;
                        titleBase = selfBase;
                    }
                }
                foreach (var child in container.Children)
                {
                    if (child is ContainerNode c) Walk(c, selfBase, isRoot: false);
                }
            }
            Walk(root, "", isRoot: true);

            if (byProvenance != null)
            {
                found = byProvenance; foundBasePath = provenanceBase; viaTitleFallback = false;
                return true;
            }
            if (byTitle != null)
            {
                found = byTitle; foundBasePath = titleBase; viaTitleFallback = true;
                return true;
            }
            found = null; foundBasePath = null; viaTitleFallback = false;
            return false;
        }

        // Root-level position for a brand-new header's section: right after the direct-child-of-root
        // subtree holding the nearest declaration predecessor, else the top of root (below a pinned
        // Script Header) — keeping sections in code order.
        private static int RootInsertIndexAfterPredecessor(ContainerNode root,
            List<SasquatchReflectionUtility.ReflectedProperty> props, int declarationIndex,
            Dictionary<string, NodeLocation> index)
        {
            for (int j = declarationIndex - 1; j >= 0; j--)
            {
                if (!index.TryGetValue(props[j].path, out var loc)) continue;
                int anchorIndex = -1;
                if (loc.Parent == root)
                {
                    anchorIndex = loc.Index;
                }
                else
                {
                    var ancestor = RootAncestorOf(root, loc.Parent);
                    if (ancestor != null) anchorIndex = root.Children.IndexOf(ancestor);
                }
                if (anchorIndex >= 0) return anchorIndex + 1;
            }
            return (root.Children.Count > 0 && root.Children[0] is ScriptHeaderNode) ? 1 : 0;
        }

        // The direct child of root whose subtree contains `node` (nodes carry no parent pointers; a
        // scan is fine at insertion frequency).
        private static ContainerNode RootAncestorOf(ContainerNode root, ContainerNode node)
        {
            foreach (var child in root.Children)
            {
                if (child is ContainerNode c && SubtreeContains(c, node)) return c;
            }
            return null;
        }

        private static bool SubtreeContains(ContainerNode haystack, ContainerNode needle)
        {
            if (haystack == needle) return true;
            if (haystack.Children == null) return false;
            foreach (var child in haystack.Children)
            {
                if (child is ContainerNode c && SubtreeContains(c, needle)) return true;
            }
            return false;
        }

        // Position within a header section: right after the nearest declaration predecessor already in
        // it, else right before the nearest declaration successor in it, else appended at the end.
        private static int SectionInsertIndex(ContainerNode section,
            List<SasquatchReflectionUtility.ReflectedProperty> props, int declarationIndex,
            Dictionary<string, NodeLocation> index)
        {
            for (int j = declarationIndex - 1; j >= 0; j--)
            {
                if (index.TryGetValue(props[j].path, out var loc) && loc.Parent == section)
                    return loc.Index + 1;
            }
            for (int j = declarationIndex + 1; j < props.Count; j++)
            {
                if (index.TryGetValue(props[j].path, out var loc) && loc.Parent == section)
                    return loc.Index;
            }
            return section.Children.Count;
        }

        /// <summary>
        /// The preset to seed newly-synced fields with, honoring Project Settings → Sasquatch Inspector → "Sync New
        /// Fields As Presets". Off (the default) ⇒ null ⇒ new fields arrive as plain default bindings (historical
        /// behavior). Independent of the designer import tools' per-user "Use Layout Presets" toggle.
        /// </summary>
        private static SasquatchLayoutPreset ResolveSyncPreset()
        {
            var settings = SasquatchSettingsManager.Current;
            return settings != null && settings.ApplyPresetsOnSync ? LayoutPresetManager.Current : null;
        }

        /// <summary>
        /// The header-routing context for a sync pass, honoring Project Settings → Sasquatch Inspector → "Smart
        /// Header Routing" (default ON). Null when the setting is off or nothing is being added — null
        /// routing reproduces the historical predecessor-adjacent insertion exactly.
        /// </summary>
        private static HeaderRouting ResolveHeaderRouting(Type type, int addedCount)
        {
            if (addedCount == 0) return null;
            var settings = SasquatchSettingsManager.Current;
            if (settings == null || !settings.RouteSyncedFieldsByHeader) return null;
            return BuildHeaderRouting(type);
        }

        /// <summary>
        /// Collects every explicitly-bound property path into `paths`. When `derivedPaths` is supplied, a
        /// toggle-group Foldout's GroupTogglePath is recorded there instead: the bool isn't an explicit binding
        /// but IS in use (it drives the group's header checkbox), so callers can mark it "used" distinctly.
        /// (Moved from SasquatchLayoutDesignerWindow so the inspector-side sync can use it without a window.)
        /// </summary>
        public static void GatherBoundPaths(LayoutNode node, string basePath, HashSet<string> paths, HashSet<string> derivedPaths = null)
        {
            if (node == null) return;
            if (node is BindingNode bn && !string.IsNullOrEmpty(bn.PropertyPath))
            {
                string resolvedPath = string.IsNullOrEmpty(basePath) ? bn.PropertyPath : $"{basePath}.{bn.PropertyPath}";
                paths.Add(resolvedPath);
            }
            else if (node is CollectionNode col && !string.IsNullOrEmpty(col.PropertyPath))
            {
                string resolvedPath = string.IsNullOrEmpty(basePath) ? col.PropertyPath : $"{basePath}.{col.PropertyPath}";
                paths.Add(resolvedPath);
                if (col.ItemLayout != null)
                {
                    GatherBoundPaths(col.ItemLayout, resolvedPath, paths, derivedPaths);
                }
            }
            else if (node is ContainerNode container)
            {
                // A toggle-group Foldout's bool is a derived (implicit) use — resolved against the incoming
                // base path to match LayoutTreeBuilder.BuildToggleGroup.
                if (derivedPaths != null && container.Type == ContainerType.Foldout && !string.IsNullOrEmpty(container.GroupTogglePath))
                {
                    string togglePath = string.IsNullOrEmpty(basePath) ? container.GroupTogglePath : $"{basePath}.{container.GroupTogglePath}";
                    derivedPaths.Add(togglePath);
                }

                string nextBasePath = basePath;
                if (!string.IsNullOrEmpty(container.PropertyPath))
                {
                    nextBasePath = string.IsNullOrEmpty(basePath) ? container.PropertyPath : $"{basePath}.{container.PropertyPath}";
                }

                if (container.Children != null)
                {
                    foreach (var child in container.Children)
                    {
                        GatherBoundPaths(child, nextBasePath, paths, derivedPaths);
                    }
                }
            }
        }

        /// <summary>
        /// The resolved paths of every collection in the tree that already carries an ELEMENT TEMPLATE — a
        /// <see cref="BindingNode"/> with an empty <c>PropertyPath</c> in its item layout, which binds the
        /// element itself.
        /// <para>
        /// Separate from <see cref="GatherBoundPaths"/> on purpose: that method keys on a binding's own path,
        /// and an element template's path is empty BY DEFINITION, so it can never be represented there. The
        /// question "is this list's element already styled?" is structural, and this answers it by the
        /// collection's path — which is what the Bindings panel's element token is keyed on.
        /// </para>
        /// </summary>
        public static void GatherElementTemplatePaths(LayoutNode node, string basePath, HashSet<string> into)
        {
            if (node == null || into == null) return;
            if (node is CollectionNode col && !string.IsNullOrEmpty(col.PropertyPath))
            {
                string resolvedPath = string.IsNullOrEmpty(basePath) ? col.PropertyPath : $"{basePath}.{col.PropertyPath}";
                if (HasElementTemplate(col)) into.Add(resolvedPath);
                if (col.ItemLayout != null) GatherElementTemplatePaths(col.ItemLayout, resolvedPath, into);
            }
            else if (node is ContainerNode container)
            {
                string nextBasePath = basePath;
                if (!string.IsNullOrEmpty(container.PropertyPath))
                    nextBasePath = string.IsNullOrEmpty(basePath) ? container.PropertyPath : $"{basePath}.{container.PropertyPath}";
                if (container.Children != null)
                    foreach (var child in container.Children) GatherElementTemplatePaths(child, nextBasePath, into);
            }
        }

        /// <summary>True when <paramref name="col"/>'s item layout holds a binding on the element itself.</summary>
        public static bool HasElementTemplate(CollectionNode col) => FindElementTemplate(col) != null;

        /// <summary>
        /// The ELEMENT-TEMPLATE binding in <paramref name="col"/>'s item layout, or null. The empty
        /// <c>PropertyPath</c> is the entire marker — there is no flag and no new node kind, which is what
        /// lets a template flow through styling, presets, conditions and undo like any other binding.
        /// <para>
        /// Only the item layout ITSELF and its direct children are searched. A template binds the element,
        /// and the element is what the item layout is rooted at, so anything deeper is scoped to a sub-field
        /// and an empty path there means something else entirely.
        /// </para>
        /// </summary>
        public static BindingNode FindElementTemplate(CollectionNode col)
        {
            if (col?.ItemLayout == null) return null;
            if (col.ItemLayout is BindingNode direct)
                return string.IsNullOrEmpty(direct.PropertyPath) ? direct : null;
            if (col.ItemLayout is ContainerNode container && container.Children != null)
                foreach (var child in container.Children)
                    if (child is BindingNode b && string.IsNullOrEmpty(b.PropertyPath)) return b;
            return null;
        }

        /// <summary>One direct element leaf field of a collection, in both the forms the sync needs: the
        /// ROOT-resolved path the ledger records, and the ELEMENT-relative path the binding stores.</summary>
        private struct ElementLeaf
        {
            public string Full;      // "loot.weight" — ledger + bound-path key
            public string Relative;  // "weight"      — what the BindingNode holds
            public Type Type;
            public FieldInfo Field;
        }

        /// <summary>
        /// A collection's DIRECT element leaf fields, in declaration order — exactly the set
        /// <see cref="SasquatchLayoutDesignerWindow.BuildCollectionItemTemplate"/> builds a fresh template
        /// from, so "what a NEW collection gets" and "what an EXISTING one is topped up with" can never
        /// drift apart and leave a field reachable by neither route.
        /// <para>
        /// FieldInfos resolve against the ELEMENT type, not the root: <c>GetFieldInfoFromPropertyPath</c>
        /// walks declared field types and stops dead at <c>List&lt;T&gt;</c>, so an unindexed
        /// <c>"loot.weight"</c> answers null there — which would silently drop [HideInInspector] and every
        /// attribute import for element fields.
        /// </para>
        /// </summary>
        private static List<ElementLeaf> GetElementLeaves(Type type, string collectionPath)
        {
            if (_elementLeafCache.TryGetValue((type, collectionPath), out var cached)) return cached;

            var result = new List<ElementLeaf>();
            if (type == null || string.IsNullOrEmpty(collectionPath)) return result;
            BindingFactory.GetFieldInfoFromPropertyPath(type, collectionPath, out var collectionType);
            if (collectionType == null) return result;
            var elementType = UnwrapElementType(collectionType);
            if (elementType == null || elementType == collectionType) return result;

            string prefix = collectionPath + ".";
            foreach (var p in GetAllProps(type))
            {
                if (p.isCollection) continue;
                if (!p.path.StartsWith(prefix)) continue;
                string rel = p.path.Substring(prefix.Length);
                if (rel.Length == 0 || rel.Contains('.')) continue; // direct element leaves only
                result.Add(new ElementLeaf
                {
                    Full = p.path,
                    Relative = rel,
                    Type = p.type,
                    Field = GetFieldIncludingBase(elementType, rel)
                });
            }
            if (ReflectionIsSettled) _elementLeafCache[(type, collectionPath)] = result;
            return result;
        }

        /// <summary>Every collection's direct element leaf paths on <paramref name="type"/>, root-resolved.
        /// The ledger's element half — shared with the designer's save-ledger snapshot so both routes
        /// account for the same set (a path one records and the other doesn't reads as new forever).</summary>
        internal static List<string> GetAllElementLeafPaths(Type type)
        {
            var result = new List<string>();
            if (type == null) return result;
            foreach (var c in GetBindableProps(type))
            {
                if (!c.isCollection) continue;
                foreach (var leaf in GetElementLeaves(type, c.path)) result.Add(leaf.Full);
            }
            return result;
        }

        /// <summary>
        /// PURE: element fields to insert into the item templates of collections ALREADY in the layout, plus
        /// the ledger backfill for collections whose element paths were never tracked.
        /// <para>
        /// The three states this has to tell apart, all of which look identical in the tree alone — a live
        /// code field with no binding:
        /// <list type="bullet">
        /// <item>NEW — not bound, not ledgered ⇒ insert.</item>
        /// <item>DELETED BY THE USER — not bound, IS ledgered ⇒ leave alone, permanently. Curating a table
        /// down to the three columns that matter is a thing people do constantly, and it has to survive
        /// every later compile.</item>
        /// <item>PRE-EXISTING — the collection has NO element path in the ledger at all, so the layout
        /// predates element tracking ⇒ record what it has and insert nothing. Without this branch the first
        /// reconcile after this feature ships would pour every unbound element field into every curated
        /// template at once.</item>
        /// </list>
        /// </para>
        /// </summary>
        private static void CollectNewElementFields(Type type, List<BindingLocation> locations,
            HashSet<string> boundResolved, HashSet<string> ledgerSet, LayoutSyncDiff diff)
        {
            var seen = new HashSet<string>();
            foreach (var loc in locations)
            {
                if (!(loc.Node is CollectionNode col)) continue;
                // A collection nested INSIDE another collection's item layout has no unindexed path to its
                // own elements, so there is nothing here to resolve (and nested collections are not v1).
                if (loc.OwnerCollection != null) continue;
                if (!seen.Add(loc.ResolvedPath)) continue;

                // ONLY a per-field template is topped up. NO item layout is a deliberate state — default
                // rows, In-Line Editor Rows, a polymorphic type picker — and creating one would change how
                // the list renders. A BARE element template is one widget bound to the element itself;
                // appending element FIELDS beside it would wrap it in a container and reshape every row.
                if (!(col.ItemLayout is ContainerNode)) continue;
                if (HasElementTemplate(col)) continue;

                var leaves = GetElementLeaves(type, loc.ResolvedPath);
                if (leaves.Count == 0) continue;

                bool anyLedgered = false;
                foreach (var leaf in leaves)
                {
                    if (ledgerSet.Contains(leaf.Full)) { anyLedgered = true; break; }
                }
                if (!anyLedgered)
                {
                    foreach (var leaf in leaves) diff.ElementLedgerBackfill.Add(leaf.Full);
                    continue;
                }

                foreach (var leaf in leaves)
                {
                    if (boundResolved.Contains(leaf.Full) || ledgerSet.Contains(leaf.Full)) continue;
                    if (IsHiddenInInspector(leaf.Field)) continue;
                    diff.AddedElementFields.Add(new ElementFieldAdd
                    {
                        CollectionPath = loc.ResolvedPath,
                        RelativePath = leaf.Relative,
                        FieldType = leaf.Type
                    });
                }
            }
        }

        /// <summary>The CollectionNode whose resolved path is <paramref name="wanted"/>, or null. Walked
        /// fresh rather than read from a pre-mutation index: ApplyDiff has already renamed and removed nodes
        /// by the time this runs.</summary>
        private static CollectionNode FindCollectionByPath(LayoutNode node, string basePath, string wanted)
        {
            if (node == null) return null;
            if (node is CollectionNode col && !string.IsNullOrEmpty(col.PropertyPath))
            {
                string resolved = Resolve(basePath, col.PropertyPath);
                if (resolved == wanted) return col;
                return col.ItemLayout != null ? FindCollectionByPath(col.ItemLayout, resolved, wanted) : null;
            }
            if (node is ContainerNode container && container.Children != null)
            {
                string next = string.IsNullOrEmpty(container.PropertyPath)
                    ? basePath : Resolve(basePath, container.PropertyPath);
                foreach (var child in container.Children)
                {
                    var found = FindCollectionByPath(child, next, wanted);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Inserts each new element field into its collection's item template, positioned by DECLARATION
        /// order against whatever siblings are already there — including inside a nested container, so a
        /// table whose columns are grouped keeps its grouping. Returns the number inserted.
        /// <para>
        /// No [Space] handling, deliberately: an attribute-owned spacer is keyed on a ROOT-resolved path and
        /// a gap between two table columns means nothing. The preset context follows the collection's own
        /// display mode, so a new column arrives styled like the table's other columns.
        /// </para>
        /// </summary>
        private static int InsertElementFields(Type type, ContainerNode root, List<ElementFieldAdd> adds,
            bool applyAttributes, SasquatchLayoutPreset preset)
        {
            if (root == null || adds == null || adds.Count == 0) return 0;
            int changes = 0;

            // Grouped per collection so each template is indexed ONCE and later inserts see earlier ones —
            // which is what puts two fields added in the same compile into declaration order instead of
            // stacking both against the same anchor.
            var byCollection = new Dictionary<string, List<ElementFieldAdd>>();
            foreach (var a in adds)
            {
                if (a == null || string.IsNullOrEmpty(a.CollectionPath)) continue;
                if (!byCollection.TryGetValue(a.CollectionPath, out var list))
                    byCollection[a.CollectionPath] = list = new List<ElementFieldAdd>();
                list.Add(a);
            }

            foreach (var kv in byCollection)
            {
                var col = FindCollectionByPath(root, "", kv.Key);
                if (!(col?.ItemLayout is ContainerNode itemRoot)) continue;

                var order = GetElementLeaves(type, kv.Key);
                var context = LayoutPresetApplier.ContextFor(col.DisplayMode);

                // Element-RELATIVE locations inside the template: the item layout's scope IS the element,
                // so an empty base path is the correct starting scope here.
                var index = new Dictionary<string, NodeLocation>();
                IndexLocations(itemRoot, "", index);

                foreach (var add in kv.Value)
                {
                    int decl = order.FindIndex(e => e.Relative == add.RelativePath);
                    if (decl < 0) continue;

                    ContainerNode parent = null;
                    int at = 0;
                    for (int j = decl - 1; j >= 0 && parent == null; j--)
                    {
                        if (index.TryGetValue(order[j].Relative, out var prev))
                        {
                            parent = prev.Parent;
                            at = prev.Index + 1;
                        }
                    }
                    for (int j = decl + 1; j < order.Count && parent == null; j++)
                    {
                        if (index.TryGetValue(order[j].Relative, out var next))
                        {
                            parent = next.Parent;
                            at = next.Index;
                        }
                    }
                    if (parent == null)
                    {
                        parent = itemRoot;
                        at = itemRoot.Children != null ? itemRoot.Children.Count : 0;
                    }

                    if (parent.Children == null) parent.Children = new List<LayoutNode>();
                    if (at < 0) at = 0;
                    if (at > parent.Children.Count) at = parent.Children.Count;

                    var node = LayoutPresetApplier.MakeConfiguredBinding(
                        add.RelativePath, add.FieldType, preset, context);
                    if (applyAttributes && decl < order.Count) ApplyPerFieldAttributes(order[decl].Field, node);

                    parent.Children.Insert(at, node);
                    changes++;

                    foreach (var entry in index.Values)
                    {
                        if (entry.Parent == parent && entry.Index >= at) entry.Index++;
                    }
                    index[add.RelativePath] = new NodeLocation { Parent = parent, Index = at, BasePath = "" };
                }
            }

            return changes;
        }

        /// <summary>True when field <paramref name="fi"/> is marked [HideInInspector].</summary>
        public static bool IsHiddenInInspector(FieldInfo fi)
        {
            return fi != null && Attribute.IsDefined(fi, typeof(HideInInspector));
        }

        /// <summary>
        /// Maps a field's per-field attributes onto its freshly-created node: [Tooltip], [Range]→Slider+limits,
        /// [Min]→enforced lower-bound validation, [TextArea]/[Multiline]→Text widget, and (for collections)
        /// [NonReorderable]→no reorder affordance. [Space] is NOT handled here — a spacer is a sibling node, not
        /// a property of the field's node, so callers insert a Space widget BEFORE the field via
        /// <see cref="TryGetSpaceBefore"/> / <see cref="MakeSpaceNode"/>. [Header] is likewise a caller concern
        /// (section placement/creation) inside <see cref="InsertFields"/> or the bulk importers.
        /// </summary>
        public static void ApplyPerFieldAttributes(FieldInfo fi, LayoutNode node)
        {
            if (fi == null || node == null) return;

            var tooltipAttr = Attribute.GetCustomAttribute(fi, typeof(TooltipAttribute)) as TooltipAttribute;
            if (tooltipAttr != null)
            {
                node.Tooltip = tooltipAttr.tooltip;
            }

            if (node is BindingNode bindingNode)
            {
                // [Range] → a Slider, but ONLY for numeric fields. Unity's [Range] can't draw on a vector
                // (it errors "Use Range with float or int"), so we never import it onto a Vector2/3/4 — those
                // set their axis min/max in the designer instead, keeping the bounds out of the user's code so
                // a Revert to Default can't surface a broken [Range]-on-a-vector field.
                var rangeAttr = Attribute.GetCustomAttribute(fi, typeof(RangeAttribute)) as RangeAttribute;
                if (rangeAttr != null && IsNumericFieldType(fi.FieldType))
                {
                    bindingNode.DisplayType = DisplayType.Slider;
                    bindingNode.MinMaxLowLimit = rangeAttr.min;
                    bindingNode.MinMaxHighLimit = rangeAttr.max;
                }

                // [Min(n)] — a min-only range rule: enforce a lower bound (Validate + Clamp) with NO upper bound
                // (RangeHasMax off), so the designer shows just the Min field and there's no sentinel-max to look
                // wrong. Only wired for numeric fields ([Min] is a no-op on anything else).
                var minAttr = Attribute.GetCustomAttribute(fi, typeof(MinAttribute)) as MinAttribute;
                if (minAttr != null && IsNumericFieldType(fi.FieldType))
                {
                    bindingNode.ValidateRange = true;
                    bindingNode.ClampToRange = true;
                    bindingNode.RangeHasMin = true;
                    bindingNode.RangeHasMax = false;
                    bindingNode.ValidationMin = minAttr.min;
                    bindingNode.RangeValidationMessage = $"Value must be at least {minAttr.min}.";
                }

                bool hasTextArea = Attribute.IsDefined(fi, typeof(TextAreaAttribute));
                bool hasMultiline = Attribute.IsDefined(fi, typeof(MultilineAttribute));
                if (hasTextArea || hasMultiline)
                {
                    bindingNode.DisplayType = DisplayType.Text;
                }

                // [ColorUsage(showAlpha, hdr)] on a Color field → drive the plain Color widget's HDR / alpha
                // options (it stays a Default field; the flags make its picker HDR/alpha-aware). Guarded on the
                // real field type so a struct member reusing the attribute can't mis-fire.
                if (fi.FieldType == typeof(Color) &&
                    Attribute.GetCustomAttribute(fi, typeof(ColorUsageAttribute)) is ColorUsageAttribute colorUsage)
                {
                    bindingNode.ColorShowAlpha = colorUsage.showAlpha;
                    bindingNode.ColorFieldMode = colorUsage.hdr ? ColorFieldMode.HDR : ColorFieldMode.Default;
                }

                // [GradientUsage(hdr)] on a Gradient field → turn on the Gradient widget's HDR authoring (the pin
                // color picker gains an intensity control). The field already routes to the Gradient widget.
                if (fi.FieldType == typeof(Gradient) &&
                    Attribute.GetCustomAttribute(fi, typeof(GradientUsageAttribute)) is GradientUsageAttribute gradientUsage)
                {
                    bindingNode.GradientHdr = gradientUsage.hdr;
                }

                // [EnumButtons(includeObsolete)] on an enum field → the Enum Badges widget. That widget resolves
                // its own shape from the enum: a [Flags] enum becomes a multi-select chip grid, a plain enum a
                // single-select segmented control — the same two shapes Unity's own ToggleButtonGroup drawer
                // produces, so honoring the attribute means honoring its INTENT rather than rendering a stock
                // control we can't style. Like every imported attribute this is only the starting point; the
                // designer can switch the field to anything else afterwards.
                //
                // includeObsolete maps 1:1 onto the widget's own option, so an [Obsolete] member stays visible
                // exactly when the developer asked for it.
                if (fi.FieldType.IsEnum && TryGetEnumButtons(fi, out bool includeObsolete))
                {
                    bindingNode.DisplayType = DisplayType.EnumBadgeGrid;
                    bindingNode.EnumBadgeIncludeObsolete = includeObsolete;
                }
            }
            else if (node is CollectionNode collectionNode)
            {
                // [NonReorderable] arrays/lists import with the reorder affordances stripped (add/remove stay).
                if (Attribute.IsDefined(fi, typeof(NonReorderableAttribute)))
                {
                    collectionNode.ReorderStyle = CollectionReorderStyle.None;
                }
            }
        }

        // ─────────────────────────── Unity's [EnumButtons], resolved by NAME ───────────────────────────
        // EnumButtonsAttribute is a Unity 6 addition living in UnityEngine.CoreModule. It is looked up by name
        // rather than referenced with a plain typeof BY DESIGN: the package's floor is 6000.0 and we can only
        // certify against the editors we have installed, so a direct reference risks a hard COMPILE failure on
        // a customer's older 6000.0.x patch — for a cosmetic default. Reflection degrades to "attribute not
        // imported" instead, which nobody's project breaks over. It also survives Unity moving the type.
        // Both lookups are cached per domain and only run on the import/sync cold path.
        private static Type _enumButtonsType;
        private static FieldInfo _enumButtonsIncludeObsoleteField;
        private static bool _enumButtonsResolved;

        private static void ResolveEnumButtonsType()
        {
            if (_enumButtonsResolved) return;
            _enumButtonsResolved = true;

            // PropertyAttribute is the anchor: it lives in the same assembly (UnityEngine.CoreModule), so this
            // never guesses at an assembly-qualified name that could change.
            _enumButtonsType = typeof(PropertyAttribute).Assembly
                .GetType("UnityEngine.EnumButtonsAttribute", throwOnError: false);
            if (_enumButtonsType != null)
            {
                _enumButtonsIncludeObsoleteField = _enumButtonsType.GetField(
                    "includeObsolete", BindingFlags.Public | BindingFlags.Instance);
            }
        }

        /// <summary>
        /// True when field <paramref name="fi"/> carries Unity's <c>[EnumButtons]</c>, outputting its
        /// <c>includeObsolete</c> flag. Returns false — never throws — on an editor whose UnityEngine build
        /// predates the attribute.
        /// </summary>
        public static bool TryGetEnumButtons(FieldInfo fi, out bool includeObsolete)
        {
            includeObsolete = false;
            if (fi == null) return false;

            ResolveEnumButtonsType();
            if (_enumButtonsType == null) return false;

            var attribute = Attribute.GetCustomAttribute(fi, _enumButtonsType);
            if (attribute == null) return false;

            if (_enumButtonsIncludeObsoleteField != null &&
                _enumButtonsIncludeObsoleteField.GetValue(attribute) is bool flag)
            {
                includeObsolete = flag;
            }
            return true;
        }

        /// <summary>
        /// True when field <paramref name="fi"/> carries a <c>[Space]</c>; outputs the gap in pixels the
        /// importer/sync should place as a Space widget ABOVE the field. Unity's parameterless <c>[Space]</c>
        /// reports height 0, so we fall back to the 8px the default inspector draws; <c>[Space(n)]</c> passes
        /// <c>n</c> straight through. Callers turn this into a first-class Space widget (see
        /// <see cref="MakeSpaceNode"/>) inserted before the field, rather than folding it into the field's margin.
        /// </summary>
        public static bool TryGetSpaceBefore(FieldInfo fi, out float height)
        {
            height = 0f;
            var spaceAttr = fi == null ? null : Attribute.GetCustomAttribute(fi, typeof(SpaceAttribute)) as SpaceAttribute;
            if (spaceAttr == null) return false;
            height = spaceAttr.height > 0 ? spaceAttr.height : 8f;
            return true;
        }

        /// <summary>Builds the vertical Space widget a <c>[Space]</c>/<c>[Space(n)]</c> attribute imports to.
        /// Pass <paramref name="ownerFieldPath"/> (the resolved path of the field the attribute lives on) to
        /// stamp it as attribute-owned — locked in the designer and auto-managed by the sync engine. Omit it
        /// for a free, hand-authored designer space.</summary>
        public static SpaceNode MakeSpaceNode(float height, string ownerFieldPath = null) =>
            new SpaceNode { SpaceAmount = height, Axis = SpaceAxis.Vertical, OwnerFieldPath = ownerFieldPath ?? "" };

        /// <summary>True for the int/float family Unity serializes as a numeric field — the only types a
        /// <c>[Min]</c> range rule applies to.</summary>
        private static bool IsNumericFieldType(Type t) =>
            t == typeof(float) || t == typeof(double)
            || t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);

        /// <summary>
        /// PURE: the type's top-level bindable fields (declaration order) that are neither bound in the
        /// layout, nor in use as a toggle-group bool, nor recorded in the ledger, nor [HideInInspector].
        /// An empty/null ledger on a layout WITH bound fields means it predates the sync feature: returns
        /// empty and signals <paramref name="needsLedgerInit"/> so callers initialize the ledger instead of
        /// mass-inserting every historically-omitted field (no surprise mutation on upgrade). An empty
        /// ledger with NO bound fields has nothing deliberate to protect — it's the drained state after
        /// every field was deleted from code — so current fields count as new (else the first reconcile
        /// after a re-add would ledger them without ever inserting them, permanently blocking auto-append).
        /// </summary>
        public static List<SasquatchReflectionUtility.ReflectedProperty> ComputeNewFields(
            Type type, LayoutNode root, IList<string> syncedFieldPaths, out bool needsLedgerInit)
        {
            needsLedgerInit = false;
            var result = new List<SasquatchReflectionUtility.ReflectedProperty>();
            if (type == null || root == null)
            {
                needsLedgerInit = syncedFieldPaths == null || syncedFieldPaths.Count == 0;
                return result;
            }

            var bound = new HashSet<string>();
            GatherBoundPaths(root, "", bound, derivedPaths: bound);
            bool ledgerEmpty = syncedFieldPaths == null || syncedFieldPaths.Count == 0;
            needsLedgerInit = ledgerEmpty && bound.Count > 0;
            if (needsLedgerInit) return result;
            var ledger = ledgerEmpty ? new HashSet<string>() : new HashSet<string>(syncedFieldPaths);

            foreach (var p in GetBindableProps(type))
            {
                if (bound.Contains(p.path) || ledger.Contains(p.path)) continue;
                var fi = BindingFactory.GetFieldInfoFromPropertyPath(type, p.path, out _);
                if (IsHiddenInInspector(fi)) continue;
                result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// Mutates the node tree in place, inserting each new field adjacent to its nearest bound
        /// code-predecessor — wherever that predecessor lives, including inside Cards/Foldouts and
        /// property-scoped containers. With <paramref name="routing"/> supplied (Smart Header Routing),
        /// a field whose code sits under a [Header] is instead placed inside that header's section
        /// container (matched by provenance, then by title; a header that arrived WITH these new fields
        /// gets a new section created, like Smart Attribute Import). No persistence here; returns the
        /// inserted resolved paths.
        /// </summary>
        public static List<string> InsertFields(Type type, ContainerNode root,
            List<SasquatchReflectionUtility.ReflectedProperty> newFields, bool applyAttributes,
            SasquatchLayoutPreset preset = null, HeaderRouting routing = null)
        {
            var inserted = new List<string>();
            if (type == null || root == null || newFields == null || newFields.Count == 0) return inserted;

            var props = GetBindableProps(type);
            var all = GetAllProps(type);

            var index = new Dictionary<string, NodeLocation>();
            IndexLocations(root, "", index);

            var newFieldPaths = new HashSet<string>();
            foreach (var f in newFields) newFieldPaths.Add(f.path);

            foreach (var newField in newFields)
            {
                int declarationIndex = props.FindIndex(p => p.path == newField.path);
                if (declarationIndex < 0) continue;

                ContainerNode parent = null;
                int insertAt = 0;
                string parentBase = null;
                bool routedToHeaderSection = false;
                string header = null;

                // --- Smart Header Routing: place the field inside its [Header]'s section. ---
                if (routing != null) routing.ContextByPath.TryGetValue(newField.path, out header);
                if (!string.IsNullOrEmpty(header))
                {
                    ContainerNode section = null;
                    string sectionBase = null;

                    if (routing.CreatedThisPass.TryGetValue(header, out var created))
                    {
                        section = created;
                        sectionBase = "";
                    }
                    else if (TryFindHeaderContainer(root, header, out var existing, out var existingBase, out bool viaTitle))
                    {
                        // Scope compatibility — the section must be able to express the field's path.
                        if (existingBase.Length == 0 || newField.path.StartsWith(existingBase + "."))
                        {
                            section = existing;
                            sectionBase = existingBase;
                            // Backfill provenance on a pre-provenance (Title-matched) container: we're
                            // inside the caller's undo record and an insertion is happening regardless.
                            if (viaTitle) existing.SourceHeader = header;
                        }
                    }
                    else if (IsBrandNewHeader(routing, header, newFieldPaths))
                    {
                        // The header arrived WITH these new fields (every field carrying it is itself
                        // new) → create its section, positioned after the root-level ancestor of the
                        // declaration predecessor so sections keep code order.
                        section = new ContainerNode
                        {
                            Type = routing.NewSectionType,
                            Title = header,
                            SourceHeader = header,
                            DefaultToOpen = true
                        };
                        int sectionAt = RootInsertIndexAfterPredecessor(root, props, declarationIndex, index);
                        root.Children.Insert(sectionAt, section);
                        foreach (var entry in index.Values)
                        {
                            if (entry.Parent == root && entry.Index >= sectionAt) entry.Index++;
                        }
                        routing.CreatedThisPass[header] = section;
                        sectionBase = "";
                    }
                    // else: a pre-existing header whose section the user deleted/dissolved — respect
                    // that choice and fall through to the predecessor-adjacent insertion.

                    if (section != null)
                    {
                        parent = section;
                        parentBase = sectionBase;
                        insertAt = SectionInsertIndex(section, props, declarationIndex, index);
                        routedToHeaderSection = true;
                    }
                }

                if (parent == null)
                {
                    // Nearest declaration-order predecessor that's present in the layout and whose container
                    // scope is compatible with the new field's path.
                    NodeLocation loc = null;
                    for (int j = declarationIndex - 1; j >= 0; j--)
                    {
                        if (index.TryGetValue(props[j].path, out var candidate) &&
                            (candidate.BasePath.Length == 0 || newField.path.StartsWith(candidate.BasePath + ".")))
                        {
                            loc = candidate;
                            break;
                        }
                    }

                    if (loc != null)
                    {
                        parent = loc.Parent;
                        insertAt = loc.Index + 1;
                        parentBase = loc.BasePath;
                    }
                    else
                    {
                        // No usable predecessor: insert at the start of root, below a pinned Script Header.
                        parent = root;
                        insertAt = (root.Children.Count > 0 && root.Children[0] is ScriptHeaderNode) ? 1 : 0;
                        parentBase = "";
                    }
                }

                var node = SasquatchLayoutDesignerWindow.MakeFieldNode(newField, all, preset);

                // The section title IS the [Header] — suppress the header-owning field's own header
                // decorator so the name isn't rendered twice (mirrors ImportAttributesIntoLayout).
                if (routedToHeaderSection && node is BindingNode leadBinding &&
                    routing.OwnersByHeader.TryGetValue(header, out var owners) && owners.Contains(newField.path))
                {
                    leadBinding.Header = LayoutNode.HiddenSentinel;
                }

                // Inside a property-scoped container the node binds the scope-relative remainder
                // (e.g. predecessor "baseStats.range" bound as "range" → new "baseStats.newStat" → "newStat").
                if (parentBase.Length > 0)
                {
                    string rel = newField.path.Substring(parentBase.Length + 1);
                    if (node is BindingNode b) b.PropertyPath = rel;
                    else if (node is CollectionNode c) c.PropertyPath = rel;
                }

                if (applyAttributes)
                {
                    var fi = BindingFactory.GetFieldInfoFromPropertyPath(type, newField.path, out _);
                    ApplyPerFieldAttributes(fi, node);

                    // [Space] → a real, attribute-owned Space widget inserted just ABOVE the field (mirrors Smart
                    // Attribute Import). Insert it at the field's slot and shift the field down one so the gap
                    // leads. Flag the field's node as accounted-for AND stamp the spacer with the field's path so
                    // it's locked to the attribute (designer delete is redirected; sync auto-removes it when the
                    // attribute goes away).
                    if (TryGetSpaceBefore(fi, out float spaceHeight))
                    {
                        parent.Children.Insert(insertAt, MakeSpaceNode(spaceHeight, newField.path));
                        foreach (var entry in index.Values)
                        {
                            if (entry.Parent == parent && entry.Index >= insertAt) entry.Index++;
                        }
                        insertAt++;
                        node.SpaceImported = true;
                    }
                }

                parent.Children.Insert(insertAt, node);
                inserted.Add(newField.path);

                // Keep the index valid: shift indexed siblings at/after the insertion point, then register
                // the new node so later new fields (declared after this one) chain in declaration order.
                foreach (var entry in index.Values)
                {
                    if (entry.Parent == parent && entry.Index >= insertAt) entry.Index++;
                }
                index[newField.path] = new NodeLocation { Parent = parent, Index = insertAt, BasePath = parentBase };
            }

            return inserted;
        }

        /// <summary>
        /// WIRING: full sync against a registered layout. Early-outs with ZERO side effects when nothing
        /// is new (the inspector hot path runs this every rebuild). Otherwise: undo record → insert →
        /// ledger update → PersistLayoutEdit. Returns the number of fields added.
        /// </summary>
        public static int SyncLayout(Type type, InspectorLayoutAsset layout, bool applyAttributes)
        {
            if (type == null || layout == null || !(layout.Root is ContainerNode root)) return 0;

            var newFields = ComputeNewFields(type, layout.Root, layout.SyncedFieldPaths, out bool needsLedgerInit);
            if (needsLedgerInit)
            {
                InitializeLedger(type, layout);
                return 0;
            }
            if (newFields.Count == 0) return 0;

            LayoutRegistry.BeginLayoutEditUndo(layout, "Auto-Sync New Fields");
            InsertFields(type, root, newFields, applyAttributes, ResolveSyncPreset(),
                ResolveHeaderRouting(type, newFields.Count));
            AddAllCurrentFieldsToLedger(type, layout.SyncedFieldPaths, layout.SyncedFieldTypes);
            LayoutRegistry.PersistLayoutEdit(layout);
            return newFields.Count;
        }

        /// <summary>
        /// Marks every CURRENT code field as accounted-for on a FRESH, never-saved working clone —
        /// in-memory only (no undo, no persistence). Fresh authoring starts from an empty canvas by
        /// design; this seed pins that baseline so the drained-ledger rule (empty ledger + no bound
        /// fields ⇒ current fields are new) can't mass-insert into a from-scratch session, while a field
        /// added to code AFTER the session began still counts as new and auto-appends into the canvas.
        /// </summary>
        internal static void SeedCloneLedgerWithCurrentFields(Type type, InspectorLayoutAsset layout)
        {
            if (type == null || layout == null) return;
            AddAllCurrentFieldsToLedger(type, layout.SyncedFieldPaths, layout.SyncedFieldTypes);
        }

        /// <summary>
        /// First-encounter migration: record every current code field as "accounted for", so only fields
        /// added afterwards count as new. (Historically-omitted fields stay reachable via the designer's
        /// "Sync Missing" bulk tool.)
        /// </summary>
        public static void InitializeLedger(Type type, InspectorLayoutAsset layout)
        {
            if (type == null || layout == null || layout.SyncedFieldPaths == null) return;
            if (layout.SyncedFieldPaths.Count > 0) return;

            LayoutRegistry.BeginLayoutEditUndo(layout, "Initialize Sasquatch Sync Ledger");
            AddAllCurrentFieldsToLedger(type, layout.SyncedFieldPaths, layout.SyncedFieldTypes);
            LayoutRegistry.PersistLayoutEdit(layout);
        }

        // Ledgers ALL top-level bindable paths — including [HideInInspector] fields (never inserted, but
        // they must not keep counting as "new"). Also maintains the parallel type-name list (rename
        // heuristics) and backfills "" entries that now resolve. Mutates in place: on database-backed
        // layouts both lists alias the entry's lists by reference.
        private static void AddAllCurrentFieldsToLedger(Type type, IList<string> ledger, IList<string> types)
        {
            if (ledger == null) return;

            // Keep the type list aligned before appending (legacy ledgers predate it).
            if (types != null)
            {
                while (types.Count < ledger.Count) types.Add("");
            }

            var typeByPath = new Dictionary<string, string>();
            foreach (var p in GetBindableProps(type))
                typeByPath[p.path] = p.type != null ? p.type.FullName : "";

            var present = new HashSet<string>(ledger);
            foreach (var p in GetBindableProps(type))
            {
                if (present.Add(p.path))
                {
                    ledger.Add(p.path);
                    types?.Add(p.type != null ? p.type.FullName : "");
                }
            }

            // ELEMENT leaf paths too. Without an entry per element field there is no way to tell "the user
            // deleted this column" from "this column is new" — GatherBoundPaths reports both as unbound —
            // and the element sync would re-add a deleted column after every compile, forever.
            foreach (var c in GetBindableProps(type))
            {
                if (!c.isCollection) continue;
                foreach (var leaf in GetElementLeaves(type, c.path))
                {
                    typeByPath[leaf.Full] = leaf.Type != null ? leaf.Type.FullName : "";
                    if (present.Add(leaf.Full))
                    {
                        ledger.Add(leaf.Full);
                        types?.Add(leaf.Type != null ? leaf.Type.FullName : "");
                    }
                }
            }

            if (types != null)
            {
                for (int i = 0; i < ledger.Count && i < types.Count; i++)
                {
                    if (string.IsNullOrEmpty(types[i]) && typeByPath.TryGetValue(ledger[i], out var t))
                        types[i] = t;
                }
            }
        }

        private static void IndexLocations(ContainerNode container, string basePath, Dictionary<string, NodeLocation> index)
        {
            if (container == null || container.Children == null) return;
            for (int i = 0; i < container.Children.Count; i++)
            {
                var child = container.Children[i];
                if (child is BindingNode bn && !string.IsNullOrEmpty(bn.PropertyPath))
                {
                    string resolved = Resolve(basePath, bn.PropertyPath);
                    if (!index.ContainsKey(resolved))
                        index[resolved] = new NodeLocation { Parent = container, Index = i, BasePath = basePath };
                }
                else if (child is CollectionNode col && !string.IsNullOrEmpty(col.PropertyPath))
                {
                    // The collection node itself is a location; its ItemLayout holds element-relative
                    // paths where a top-level field can never legally insert — don't recurse into it.
                    string resolved = Resolve(basePath, col.PropertyPath);
                    if (!index.ContainsKey(resolved))
                        index[resolved] = new NodeLocation { Parent = container, Index = i, BasePath = basePath };
                }
                else if (child is ContainerNode c)
                {
                    string nextBase = string.IsNullOrEmpty(c.PropertyPath) ? basePath : Resolve(basePath, c.PropertyPath);
                    IndexLocations(c, nextBase, index);
                }
            }
        }

        private static string Resolve(string basePath, string propertyPath)
        {
            return string.IsNullOrEmpty(basePath) ? propertyPath : $"{basePath}.{propertyPath}";
        }

        // The per-domain reflection caches must never be POPULATED while the editor is compiling/importing:
        // a transiently under-reported field list cached in that window would stay wrong for the whole
        // domain session (missed appends, live paths reading as dead) with nothing left to invalidate it.
        private static bool ReflectionIsSettled => !EditorIsTransient;

        private static List<SasquatchReflectionUtility.ReflectedProperty> GetBindableProps(Type type)
        {
            if (!_bindablePropsCache.TryGetValue(type, out var props))
            {
                props = SasquatchReflectionUtility.GetTopLevelBindableProperties(type);
                if (ReflectionIsSettled) _bindablePropsCache[type] = props;
            }
            return props;
        }

        /// <summary>
        /// <see cref="SasquatchReflectionUtility.GetSerializableProperties"/>, memoised per type behind the
        /// <see cref="ReflectionIsSettled"/> guard above. Internal because the DESIGNER needs this memo too and
        /// must not grow a second one: its autocomplete providers and the Bindings-tab badge call the uncached
        /// version per keystroke and per committed edit, and the underlying call is a recursive walk of the
        /// type's whole serialized graph. Read-only — the returned list is SHARED, so callers must not mutate it.
        /// </summary>
        internal static List<SasquatchReflectionUtility.ReflectedProperty> GetAllProps(Type type)
        {
            if (!_allPropsCache.TryGetValue(type, out var props))
            {
                props = SasquatchReflectionUtility.GetSerializableProperties(type);
                if (ReflectionIsSettled) _allPropsCache[type] = props;
            }
            return props;
        }

        // ====================================================================================
        // Full reconcile (add + remove + rename) — supersedes SyncLayout as the inspector hook.
        // ====================================================================================

        // A bound node's exact spot in the tree, recorded in layout traversal order. Unlike
        // IndexLocations this ALSO descends into CollectionNode.ItemLayout (element sub-bindings
        // are removable/renameable; they are still never insertion anchors for top-level fields).
        private class BindingLocation
        {
            public LayoutNode Node;                 // BindingNode or CollectionNode
            public ContainerNode Parent;            // null when the node IS a collection's ItemLayout
            public CollectionNode OwnerCollection;  // non-null inside (or as) an ItemLayout
            public bool IsItemLayoutRoot;
            public string BasePath;
            public string ResolvedPath;
        }

        /// <summary>
        /// PURE one-stop diff: fields to add (declaration-ordered), dead bindings to remove, and
        /// removed+added pairs recognized as renames — via [FormerlySerializedAs] (definitive, any scope)
        /// or by ledgered-type + scope + order heuristics (top-level scope only). An empty ledger signals
        /// <see cref="LayoutSyncDiff.NeedsLedgerInit"/> and reports nothing else (no-surprise migration).
        /// </summary>
        public static LayoutSyncDiff ComputeDiff(Type type, LayoutNode root,
            IList<string> syncedFieldPaths, IList<string> syncedFieldTypes)
        {
            var diff = new LayoutSyncDiff();
            bool ledgerEmpty = syncedFieldPaths == null || syncedFieldPaths.Count == 0;
            if (type == null || !(root is ContainerNode rootContainer))
            {
                diff.NeedsLedgerInit = ledgerEmpty;
                return diff;
            }
            if (ledgerEmpty)
            {
                // An empty ledger on a layout WITH bound fields is a pre-sync-feature layout: init only,
                // never mass-insert (no-surprise migration). An empty ledger with NO bound fields has
                // nothing deliberate to protect — it's the drained state after every field was deleted
                // from code — and re-added fields MUST count as new here, or the first reconcile after a
                // re-add would ledger them without ever inserting them, permanently blocking auto-append.
                // (Fresh designer sessions never reach this: their clone's ledger is seeded at creation.)
                var anyBound = new HashSet<string>();
                GatherBoundPaths(root, "", anyBound, derivedPaths: anyBound);
                diff.NeedsLedgerInit = anyBound.Count > 0;
                if (diff.NeedsLedgerInit) return diff;
                syncedFieldPaths = new List<string>();
            }

            var currentPaths = GetCurrentPathSet(type);
            var former = GetFormerToCurrentMap(type);

            // 1. Scoped-container prefix renames (FSA only) — found first so bindings under a renamed
            // scope resolve against the NEW prefix and don't read as dead.
            CollectScopeRenames(rootContainer, "", type, currentPaths, former, diff.ScopeRenames);
            var scopeMap = new Dictionary<string, string>();
            foreach (var sr in diff.ScopeRenames) scopeMap[sr.OldResolvedPath] = sr.NewResolvedPath;

            // 2. Every bound node with its location (post-scope-rename resolution), plus derived
            // toggle-bool paths (in-use, but never deletable nodes).
            var locations = new List<BindingLocation>();
            var derived = new HashSet<string>();
            CollectBindingLocations(rootContainer, "", null, scopeMap, locations, derived);
            var boundResolved = new HashSet<string>();
            foreach (var l in locations) boundResolved.Add(l.ResolvedPath);

            // 3. Removed = bound paths failing the dual liveness check (path set + reflection fallback,
            // same rule as the designer's IsOrphanBinding).
            var removedLocs = new List<BindingLocation>();
            foreach (var loc in locations)
            {
                if (IsDeadPath(type, loc.ResolvedPath, currentPaths)) removedLocs.Add(loc);
            }

            // 4. Added = the existing new-field rule (top-level bindable, declaration order).
            var ledgerSet = new HashSet<string>(syncedFieldPaths);
            foreach (var p in GetBindableProps(type))
            {
                if (boundResolved.Contains(p.path) || derived.Contains(p.path) || ledgerSet.Contains(p.path)) continue;
                var fi = BindingFactory.GetFieldInfoFromPropertyPath(type, p.path, out _);
                if (IsHiddenInInspector(fi)) continue;
                diff.Added.Add(p);
            }

            // 4b. New ELEMENT fields. GetBindableProps above stops AT a collection on purpose — an element
            // sub-field can never bind at root — so step 4 structurally cannot see a field added to the
            // struct behind a List<T>. These belong in one collection's ItemLayout, at element-relative
            // scope, which is why they ride a separate list all the way through apply.
            CollectNewElementFields(type, locations, boundResolved, ledgerSet, diff);

            // 5a. FSA pairing (definitive, any scope incl. ItemLayout): a removed path that maps to a
            // live, unbound current path IS that path renamed.
            var pairedNewPaths = new HashSet<string>();
            for (int i = removedLocs.Count - 1; i >= 0; i--)
            {
                var loc = removedLocs[i];
                if (former.TryGetValue(loc.ResolvedPath, out var current) &&
                    !boundResolved.Contains(current) && pairedNewPaths.Add(current))
                {
                    diff.Renamed.Add(new RenamedBinding
                    {
                        OldResolvedPath = loc.ResolvedPath,
                        NewResolvedPath = current,
                        ViaFormerlySerializedAs = true
                    });
                    removedLocs.RemoveAt(i);
                }
            }

            // 5b. Heuristic pairing (non-ItemLayout only — element fields have no ledger, so "added vs
            // deliberately omitted" is indistinguishable there): same ledgered type + same scope prefix,
            // Nth removed (layout order) ↔ Nth added (declaration order).
            var ledgerTypeByPath = new Dictionary<string, string>();
            if (syncedFieldTypes != null)
            {
                for (int i = 0; i < syncedFieldPaths.Count && i < syncedFieldTypes.Count; i++)
                    ledgerTypeByPath[syncedFieldPaths[i]] = syncedFieldTypes[i] ?? "";
            }

            var addedByKey = new Dictionary<(string typeName, string scope), Queue<SasquatchReflectionUtility.ReflectedProperty>>();
            foreach (var a in diff.Added)
            {
                if (pairedNewPaths.Contains(a.path)) continue;
                string typeName = a.type != null ? a.type.FullName : "";
                if (typeName.Length == 0) continue;
                var key = (typeName, ScopePrefixOf(a.path));
                if (!addedByKey.TryGetValue(key, out var queue)) addedByKey[key] = queue = new Queue<SasquatchReflectionUtility.ReflectedProperty>();
                queue.Enqueue(a);
            }

            var stillRemoved = new List<BindingLocation>();
            foreach (var loc in removedLocs)
            {
                bool paired = false;
                if (loc.OwnerCollection == null)
                {
                    string typeName = ledgerTypeByPath.TryGetValue(loc.ResolvedPath, out var t) ? t : "";
                    if (typeName.Length > 0 &&
                        addedByKey.TryGetValue((typeName, ScopePrefixOf(loc.ResolvedPath)), out var queue) &&
                        queue.Count > 0)
                    {
                        // AMBIGUOUS when more than one same-typed field in this scope was renamed at once:
                        // the removed side is walked in LAYOUT order and the added side in DECLARATION order,
                        // and reordering rows in the designer is the product's whole point — so the two orders
                        // routinely disagree and the pairing is a guess. Nothing is lost either way (both nodes
                        // survive), but two fields can end up wearing each other's styling. There is no
                        // information here that could resolve it, so say so once instead of silently guessing.
                        // (Audit 2026-08-09, finding A6.)
                        bool ambiguous = queue.Count > 1;
                        var match = queue.Dequeue();
                        if (ambiguous)
                        {
                            Debug.LogWarning(
                                $"[Sasquatch Inspector] Renamed several '{typeName}' fields at once, so pairing " +
                                $"them with their new names is a guess — '{loc.ResolvedPath}' was matched to " +
                                $"'{match.path}'. If two of them swapped styling, undo (Ctrl+Z) and rename them " +
                                $"one at a time, or add [FormerlySerializedAs] so the match is exact.");
                        }
                        diff.Renamed.Add(new RenamedBinding
                        {
                            OldResolvedPath = loc.ResolvedPath,
                            NewResolvedPath = match.path,
                            ViaFormerlySerializedAs = false
                        });
                        pairedNewPaths.Add(match.path);
                        paired = true;
                    }
                }
                if (!paired) stillRemoved.Add(loc);
            }

            // 5c. Unknown-type fallback: within a scope, exactly one leftover removed + one leftover added
            // pair up (a legacy ledger without types still recognizes the single-rename case).
            var leftoverAddedByScope = new Dictionary<string, List<SasquatchReflectionUtility.ReflectedProperty>>();
            foreach (var a in diff.Added)
            {
                if (pairedNewPaths.Contains(a.path)) continue;
                string scope = ScopePrefixOf(a.path);
                if (!leftoverAddedByScope.TryGetValue(scope, out var list)) leftoverAddedByScope[scope] = list = new List<SasquatchReflectionUtility.ReflectedProperty>();
                list.Add(a);
            }
            var leftoverRemovedByScope = new Dictionary<string, List<BindingLocation>>();
            foreach (var loc in stillRemoved)
            {
                if (loc.OwnerCollection != null) continue;
                if (ledgerTypeByPath.TryGetValue(loc.ResolvedPath, out var t) && !string.IsNullOrEmpty(t)) continue; // typed but unmatched — a real delete
                string scope = ScopePrefixOf(loc.ResolvedPath);
                if (!leftoverRemovedByScope.TryGetValue(scope, out var list)) leftoverRemovedByScope[scope] = list = new List<BindingLocation>();
                list.Add(loc);
            }
            foreach (var kv in leftoverRemovedByScope)
            {
                if (kv.Value.Count != 1) continue;
                if (!leftoverAddedByScope.TryGetValue(kv.Key, out var adds) || adds.Count != 1) continue;
                var loc = kv.Value[0];
                diff.Renamed.Add(new RenamedBinding
                {
                    OldResolvedPath = loc.ResolvedPath,
                    NewResolvedPath = adds[0].path,
                    ViaFormerlySerializedAs = false
                });
                pairedNewPaths.Add(adds[0].path);
                stillRemoved.Remove(loc);
            }

            // 6. Finalize.
            diff.Added.RemoveAll(a => pairedNewPaths.Contains(a.path));
            foreach (var loc in stillRemoved)
            {
                diff.Removed.Add(new RemovedBinding
                {
                    ResolvedPath = loc.ResolvedPath,
                    InItemLayout = loc.OwnerCollection != null
                });
            }

            // 6b. Ledger scavenging: an entry that is neither bound in the tree nor live in code records
            // nothing real anymore — deleting the field is the ultimate removal, superseding any earlier
            // "user deliberately removed the node" state the entry encoded. Left in place it silently
            // blocks a future re-add of the same field name from ever auto-appending (a delete → re-add
            // cycle whose prune was deferred, or a ledgered-but-never-inserted path from an old bug). A
            // ledgered path that is live but unbound is a REAL deliberate removal and is kept. Renamed
            // old paths are bound (they came from bound locations), so they're never scavenged out from
            // under the ledger swap.
            foreach (var path in syncedFieldPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (boundResolved.Contains(path) || derived.Contains(path)) continue;
                if (!IsDeadPath(type, path, currentPaths)) continue;
                diff.LedgerScavenge.Add(path);
            }

            // 7. [Space] decorator sync on already-BOUND fields — insert-time import only covers brand-new
            // fields, so an attribute added to a field that's ALREADY in the layout needs its own pass. The
            // SpaceImported flag on the FIELD's node is the per-field ledger: attribute present + flag off ⇒
            // import — unless a Space widget already sits directly above (pre-flag layouts backfill the flag
            // silently, no duplicate); attribute gone + flag on ⇒ clear, re-arming a future re-add. A spacer
            // the user deleted stays deleted: its field keeps the flag while the attribute exists. Fields
            // being added/renamed this pass aren't bound yet and simply converge on the next reconcile.
            var firstLocByPath = new Dictionary<string, BindingLocation>();
            foreach (var l in locations)
            {
                if (!firstLocByPath.ContainsKey(l.ResolvedPath)) firstLocByPath[l.ResolvedPath] = l;
            }
            var ownedSpacerPaths = new HashSet<string>();
            CollectOwnedSpacerPaths(rootContainer, ownedSpacerPaths);
            foreach (var p in GetBindableProps(type))
            {
                if (!firstLocByPath.TryGetValue(p.path, out var loc) || loc.OwnerCollection != null) continue;
                var fi = BindingFactory.GetFieldInfoFromPropertyPath(type, p.path, out _);
                bool hasSpaceAttr = TryGetSpaceBefore(fi, out _);
                bool hasOwnedSpacer = ownedSpacerPaths.Contains(p.path);

                if (!hasSpaceAttr)
                {
                    // Attribute gone: re-arm the field (clear its flag) AND, in ApplyDiff, remove the owned spacer
                    // that mirrored it — the user removed the [Space] in code, so its space disappears with it.
                    if (loc.Node.SpaceImported || hasOwnedSpacer) diff.SpaceClears.Add(p.path);
                    continue;
                }

                // Attribute present.
                if (hasOwnedSpacer) continue;                    // already mirrored by an owned spacer — done
                if (FindSpaceWidgetAbove(rootContainer, loc.Node) != null)
                    diff.SpaceMarks.Add(p.path);                 // adopt the existing (unowned/legacy) spacer above
                else if (!loc.Node.SpaceImported)
                    diff.SpaceAdds.Add(p.path);                  // no spacer anywhere yet → import a fresh owned one
                // else: flag on, no spacer anywhere — a deliberately-removed gap; respect it, don't re-add.
            }
            return diff;
        }

        /// <summary>
        /// The existing Space widget that already renders the [Space] gap for <paramref name="node"/> (null if
        /// none): the sibling directly above it is a SpaceNode, or — when the node leads its container — the
        /// sibling directly above an ancestor container it leads (Smart Attribute Import parks a [Header]+[Space]
        /// field's gap at root, just before the section container).
        /// </summary>
        private static SpaceNode FindSpaceWidgetAbove(ContainerNode root, LayoutNode node)
        {
            LayoutNode current = node;
            while (true)
            {
                var parent = FindDirectParent(root, current);
                if (parent == null) return null;
                int i = parent.Children.IndexOf(current);
                if (i > 0) return parent.Children[i - 1] as SpaceNode;
                if (i != 0 || parent == root) return null;
                current = parent;
            }
        }

        /// <summary>Collects the resolved owner paths of every attribute-owned Space widget in the tree (walks
        /// containers and collection item-layouts). Used to detect "this field's [Space] is already represented"
        /// without an O(fields×tree) scan per field.</summary>
        private static void CollectOwnedSpacerPaths(LayoutNode node, HashSet<string> owned)
        {
            switch (node)
            {
                case SpaceNode sn when sn.IsAttributeOwned:
                    owned.Add(sn.OwnerFieldPath);
                    break;
                case ContainerNode c when c.Children != null:
                    foreach (var child in c.Children) CollectOwnedSpacerPaths(child, owned);
                    break;
                case CollectionNode col when col.ItemLayout != null:
                    CollectOwnedSpacerPaths(col.ItemLayout, owned);
                    break;
            }
        }

        /// <summary>True when the tree already holds an attribute-owned Space widget mirroring the field at
        /// <paramref name="ownerPath"/> — the dedupe guard the bulk importers share with the sync engine so a
        /// re-import (or a stale orphan spacer from old data) can't stack a second gap for the same [Space].</summary>
        public static bool HasOwnedSpacer(LayoutNode root, string ownerPath)
        {
            if (string.IsNullOrEmpty(ownerPath)) return false;
            var owned = new HashSet<string>();
            CollectOwnedSpacerPaths(root, owned);
            return owned.Contains(ownerPath);
        }

        /// <summary>Removes every attribute-owned Space widget whose <see cref="SpaceNode.OwnerFieldPath"/> equals
        /// <paramref name="ownerPath"/> (walks containers + item-layouts). Returns how many were removed.</summary>
        internal static int RemoveOwnedSpacers(LayoutNode node, string ownerPath)
        {
            if (string.IsNullOrEmpty(ownerPath)) return 0;
            int removed = 0;
            if (node is ContainerNode c && c.Children != null)
            {
                for (int i = c.Children.Count - 1; i >= 0; i--)
                {
                    var child = c.Children[i];
                    if (child is SpaceNode sn && sn.OwnerFieldPath == ownerPath)
                    {
                        c.Children.RemoveAt(i);
                        removed++;
                    }
                    else
                    {
                        removed += RemoveOwnedSpacers(child, ownerPath);
                    }
                }
            }
            else if (node is CollectionNode col && col.ItemLayout != null)
            {
                removed += RemoveOwnedSpacers(col.ItemLayout, ownerPath);
            }
            return removed;
        }

        /// <summary>Rewrites every attribute-owned Space widget's owner path so a field rename carries its
        /// locked spacer's ownership along (else the renamed field reads as "unspaced" and a duplicate spacer is
        /// imported). Exact match in <paramref name="renameMap"/> wins; failing that a scoped-container prefix
        /// rename (<paramref name="scopeRenames"/>) rewrites the leading segment.</summary>
        private static void RemapOwnedSpacerPaths(LayoutNode node, Dictionary<string, string> renameMap,
            List<RenamedBinding> scopeRenames)
        {
            switch (node)
            {
                case SpaceNode sn when sn.IsAttributeOwned:
                    if (renameMap.TryGetValue(sn.OwnerFieldPath, out var to))
                    {
                        sn.OwnerFieldPath = to;
                    }
                    else if (scopeRenames != null)
                    {
                        foreach (var sr in scopeRenames)
                        {
                            if (sn.OwnerFieldPath.StartsWith(sr.OldResolvedPath + "."))
                            {
                                sn.OwnerFieldPath = sr.NewResolvedPath + sn.OwnerFieldPath.Substring(sr.OldResolvedPath.Length);
                                break;
                            }
                        }
                    }
                    break;
                case ContainerNode c when c.Children != null:
                    foreach (var child in c.Children) RemapOwnedSpacerPaths(child, renameMap, scopeRenames);
                    break;
                case CollectionNode col when col.ItemLayout != null:
                    RemapOwnedSpacerPaths(col.ItemLayout, renameMap, scopeRenames);
                    break;
            }
        }

        // The container whose Children directly holds `needle`, by reference identity (ItemLayout templates are
        // not searched — element nodes never carry a top-level field's decorator).
        private static ContainerNode FindDirectParent(ContainerNode container, LayoutNode needle)
        {
            if (container?.Children == null) return null;
            foreach (var child in container.Children)
            {
                if (ReferenceEquals(child, needle)) return container;
                if (child is ContainerNode c)
                {
                    var found = FindDirectParent(c, needle);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>
        /// PURE tree mutation: scope renames → node renames in place (all styling survives) → reference
        /// rewrites (conditions / toggle paths) → removals (emptied containers stay) → insertions.
        /// No persistence here; returns the number of changes applied.
        /// </summary>
        public static int ApplyDiff(Type type, ContainerNode root, LayoutSyncDiff diff, bool applyAttributes,
            SasquatchLayoutPreset preset = null, HeaderRouting routing = null)
        {
            if (type == null || root == null || diff == null || diff.IsEmpty || diff.NeedsLedgerInit) return 0;
            int changes = 0;

            // 1. Scoped-container prefix renames first, so everything below resolves on the new names.
            var renameMap = new Dictionary<string, string>();
            foreach (var sr in diff.ScopeRenames) renameMap[sr.OldResolvedPath] = sr.NewResolvedPath;
            if (renameMap.Count > 0)
            {
                ApplyScopeRenames(root, "", renameMap);
                changes += diff.ScopeRenames.Count;
            }
            foreach (var rn in diff.Renamed) renameMap[rn.OldResolvedPath] = rn.NewResolvedPath;

            // 2. Locate nodes on the post-scope-rename tree (no scope map needed — paths are real now).
            var locations = new List<BindingLocation>();
            CollectBindingLocations(root, "", null, null, locations, null);
            var byResolved = new Dictionary<string, BindingLocation>();
            // ALL locations per path, not just the first: the same field can legitimately be bound by several
            // nodes (Ctrl+D on a binding, or duplicating a container that holds one), and every one of them
            // must follow a rename. Keyed lookups below still take the first.
            var allByResolved = new Dictionary<string, List<BindingLocation>>();
            foreach (var l in locations)
            {
                if (!byResolved.ContainsKey(l.ResolvedPath)) byResolved[l.ResolvedPath] = l;
                if (!allByResolved.TryGetValue(l.ResolvedPath, out var bucket))
                    allByResolved[l.ResolvedPath] = bucket = new List<BindingLocation>();
                bucket.Add(l);
            }

            // 3. Renames in place — the node object is untouched apart from PropertyPath, so widget,
            // styling, validation, conditions and position all survive.
            foreach (var rn in diff.Renamed)
            {
                if (!allByResolved.TryGetValue(rn.OldResolvedPath, out var locs)) continue;
                // EVERY node bound to the old path, not just the first. ComputeDiff can only ever emit ONE
                // Renamed entry for the one new path, so a field bound twice used to have its first node
                // renamed and its second left holding a dead path — and then step 5 deleted the renamed one.
                // (Audit 2026-08-09, finding UFS-1.)
                foreach (var loc in locs)
                {
                    // The node's base may itself be renamed in this same diff (e.g. its owning collection).
                    string effectiveBase = MapRenamedPath(loc.BasePath, renameMap) ?? loc.BasePath;
                    string rel;
                    if (effectiveBase.Length == 0) rel = rn.NewResolvedPath;
                    else if (rn.NewResolvedPath.StartsWith(effectiveBase + ".")) rel = rn.NewResolvedPath.Substring(effectiveBase.Length + 1);
                    else continue; // unexpressible from this scope — leave the node untouched (defensive)

                    if (loc.Node is BindingNode b) b.PropertyPath = rel;
                    else if (loc.Node is CollectionNode c) c.PropertyPath = rel;
                    changes++;
                }
            }

            // 3b. Carry each locked spacer's ownership across the rename it just followed.
            if (renameMap.Count > 0) RemapOwnedSpacerPaths(root, renameMap, diff.ScopeRenames);

            // 4. Rewrite other references that resolve to a renamed path (conditions are scope-relative;
            // only a resolved-path match is rewritten — same-named paths in other scopes are untouched).
            var removedSet = new HashSet<string>();
            foreach (var rm in diff.Removed) removedSet.Add(rm.ResolvedPath);
            RewriteRenamedReferences(root, "", renameMap, removedSet);

            // 5. Removals, by node reference (no index-shift hazards). Emptied containers are left. A pruned
            // field takes its attribute-owned spacer with it — the gap belonged to that field's [Space].
            foreach (var rm in diff.Removed)
            {
                // A path that was RENAMED is not a deletion — the field still exists, under a new name, and
                // step 3 has already repaired every node bound to it. Removing it here would delete the very
                // node the rename just fixed: ComputeDiff emits BOTH a Renamed and a leftover Removed carrying
                // the same old path when a field is bound more than once, and this loop used to resolve that
                // leftover through the same first-wins map and destroy the renamed node, styling and all.
                // (Audit 2026-08-09, finding UFS-1.)
                if (renameMap.ContainsKey(rm.ResolvedPath)) continue;

                if (!byResolved.TryGetValue(rm.ResolvedPath, out var loc)) continue;
                if (loc.IsItemLayoutRoot && loc.OwnerCollection != null) loc.OwnerCollection.ItemLayout = null;
                else loc.Parent?.Children.Remove(loc.Node);
                RemoveOwnedSpacers(root, rm.ResolvedPath);
                changes++;
            }

            // 6. Insertions (existing engine).
            changes += InsertFields(type, root, diff.Added, applyAttributes, preset, routing).Count;

            // 6b. New ELEMENT fields into the item templates they belong to. Separate from step 6 because
            // the destination is one collection's ItemLayout rather than the node tree, and the binding is
            // element-relative rather than root-resolved. Runs AFTER step 6 so a collection inserted in this
            // same pass is already in the tree — though a brand-new collection arrives with a full template
            // from MakeFieldNode and so contributes nothing here.
            changes += InsertElementFields(type, root, diff.AddedElementFields, applyAttributes, preset);

            // 7. [Space] import on fields that were ALREADY bound (late-added attributes) and the matching owner
            // bookkeeping: SpaceAdds insert a fresh owned spacer, SpaceMarks adopt a legacy spacer above (locking
            // it to the attribute), SpaceClears re-arm the flag AND auto-remove the owned spacer whose attribute
            // vanished. The locations were collected before the mutations above, but these node references stay
            // valid: space-op paths are live fields (never in Removed) and nothing above reparents a surviving
            // node — only the insertion index must be re-read.
            foreach (var path in diff.SpaceAdds)
            {
                if (!byResolved.TryGetValue(path, out var loc) || loc.Parent == null) continue;
                var fi = BindingFactory.GetFieldInfoFromPropertyPath(type, path, out _);
                if (!TryGetSpaceBefore(fi, out float spaceHeight)) continue;
                int at = loc.Parent.Children.IndexOf(loc.Node);
                if (at < 0) continue;
                loc.Parent.Children.Insert(at, MakeSpaceNode(spaceHeight, path));
                loc.Node.SpaceImported = true;
                changes++;
            }
            // Marks/clears are invisible bookkeeping — applied (and persisted by the caller) but not counted,
            // so "N script changes applied" toasts stay honest.
            foreach (var path in diff.SpaceMarks)
            {
                if (!byResolved.TryGetValue(path, out var loc)) continue;
                loc.Node.SpaceImported = true;
                // Adopt the pre-existing (unowned/legacy) spacer above so it's now locked to this attribute.
                var spacerAbove = FindSpaceWidgetAbove(root, loc.Node);
                if (spacerAbove != null && !spacerAbove.IsAttributeOwned) spacerAbove.OwnerFieldPath = path;
            }
            foreach (var path in diff.SpaceClears)
            {
                if (byResolved.TryGetValue(path, out var loc)) loc.Node.SpaceImported = false;
                // The attribute is gone — remove the spacer that mirrored it (does nothing for a legacy/unowned
                // spacer the user is free to keep). Runs behind the mid-compile safety window like every removal.
                RemoveOwnedSpacers(root, path);
            }
            return changes;
        }

        /// <summary>
        /// WIRING: full reconcile against a registered layout — the inspector hot-path entry. Strict
        /// zero-side-effect no-op when the layout already matches the code; otherwise one undo record
        /// covers renames + removals + insertions + ledger maintenance.
        /// </summary>
        public static int ReconcileLayout(Type type, InspectorLayoutAsset layout, bool applyAttributes)
            => ReconcileCore(type, layout, applyAttributes, persist: true);

        /// <summary>
        /// In-memory variant of <see cref="ReconcileLayout"/> for layout objects that are NOT the persisted
        /// registry asset — the designer's working CLONE (post-domain-reload catch-up, hand-opened-asset
        /// load). Identical diff/apply/ledger maintenance, but no undo record and no PersistLayoutEdit: the
        /// caller owns persistence (a designer clone only reaches disk through the user's Save). Without
        /// this, a clone taken (or kept alive across a compile) while the script gained fields would be
        /// saved WITHOUT those fields while the save's ledger accounts for them — permanently excluding
        /// them from auto-append. Strict no-op when the layout already matches the code.
        /// </summary>
        public static int ReconcileLayoutInMemory(Type type, InspectorLayoutAsset layout, bool applyAttributes)
            => ReconcileCore(type, layout, applyAttributes, persist: false);

        private static int ReconcileCore(Type type, InspectorLayoutAsset layout, bool applyAttributes, bool persist)
        {
            if (type == null || layout == null || !(layout.Root is ContainerNode root)) return 0;

            var diff = ComputeDiff(type, layout.Root, layout.SyncedFieldPaths, layout.SyncedFieldTypes);
            if (diff.NeedsLedgerInit)
            {
                if (persist)
                {
                    InitializeLedger(type, layout);
                }
                else if (layout.SyncedFieldPaths != null)
                {
                    // First-encounter migration for a non-persisted clone: same record-only semantics as
                    // InitializeLedger, minus the undo record + disk write the clone must never make.
                    AddAllCurrentFieldsToLedger(type, layout.SyncedFieldPaths, layout.SyncedFieldTypes);
                }
                return 0;
            }

            // [Space] decorator ops are attribute imports — the "Apply Script Attributes On Sync" setting gates
            // them exactly like insert-time attribute mapping does.
            if (!applyAttributes)
            {
                diff.SpaceAdds.Clear();
                diff.SpaceMarks.Clear();
                diff.SpaceClears.Clear();
            }

            // SAFETY: never AUTO-REMOVE bindings on the always-on inspector path while the editor may be in a
            // transient state where reflection under-reports the type's fields (mid-compile / mid-import). A
            // field momentarily misread as "dead" would be deleted AND persisted — silently losing the user's
            // node with no pattern. Drop only the destructive half in that window; Added/Renamed still apply. A
            // misread can also under-report ATTRIBUTES, so the [Space] re-arm (SpaceClears) is deferred the
            // same way, and ledger scavenging rides the same gate (a misread makes live entries look dead).
            // A type reporting ZERO serializable fields is also deferred — but it is NOT "never legitimate":
            // deleting a script's last field(s) is exactly how a type becomes fieldless, and permanently
            // blocking the prune there strands red orphan nodes forever. So the fieldless state is handled
            // two-phase: defer now, and let the re-arm CONFIRM it with a fresh reflection read in a stable
            // editor (_confirmedFieldlessTypes) — one glitchy read can never destroy a layout, while a real
            // delete-them-all prunes one update tick later. Every drop schedules the re-arm; there is no
            // guaranteed "next draw" after isUpdating clears, so an un-retried drop would strand the dead
            // node in the persisted asset (invisible in the inspector, a red orphan in the designer).
            if (diff.Removed.Count > 0 || diff.SpaceClears.Count > 0 || diff.LedgerScavenge.Count > 0)
            {
                bool transient = EditorIsTransient;
                bool unconfirmedFieldless = !transient && GetCurrentPathSet(type).Count == 0
                                            && !_confirmedFieldlessTypes.Contains(type);
                if (transient || unconfirmedFieldless)
                {
                    diff.Removed.Clear();
                    diff.SpaceClears.Clear();
                    diff.LedgerScavenge.Clear();
                    ScheduleDeferredRemovalRearm(type);
                }
            }

            if (diff.IsEmpty) return 0;

            if (persist) LayoutRegistry.BeginLayoutEditUndo(layout, "Sync Layout With Script");
            int changes = ApplyDiff(type, root, diff, applyAttributes, ResolveSyncPreset(),
                ResolveHeaderRouting(type, diff.Added.Count));
            UpdateLedgerForDiff(type, layout.SyncedFieldPaths, layout.SyncedFieldTypes, diff);
            if (persist) LayoutRegistry.PersistLayoutEdit(layout);
            return changes;
        }

        // Ledger maintenance for a reconcile: removed paths drop out (so re-adding the same field later
        // counts as new again), renamed paths swap old→new in place, then the standard add-all pass
        // appends inserted/hidden fields and refreshes type names. All in-place (aliasing invariant).
        private static void UpdateLedgerForDiff(Type type, IList<string> ledger, IList<string> types,
            LayoutSyncDiff diff)
        {
            if (ledger == null) return;
            if (types != null)
            {
                while (types.Count < ledger.Count) types.Add("");
            }

            foreach (var rm in diff.Removed)
            {
                int i = ledger.IndexOf(rm.ResolvedPath);
                if (i < 0) continue;
                ledger.RemoveAt(i);
                if (types != null && i < types.Count) types.RemoveAt(i);
            }

            foreach (var path in diff.LedgerScavenge)
            {
                int i = ledger.IndexOf(path);
                if (i < 0) continue;
                ledger.RemoveAt(i);
                if (types != null && i < types.Count) types.RemoveAt(i);
            }

            foreach (var rn in diff.Renamed)
            {
                int i = ledger.IndexOf(rn.OldResolvedPath);
                if (i < 0) continue;
                ledger[i] = rn.NewResolvedPath;
                if (types != null && i < types.Count) types[i] = ""; // refreshed by the backfill below
            }

            AddAllCurrentFieldsToLedger(type, ledger, types);
        }

        // ---- deferred-removal re-arm --------------------------------------------------------

        // Test seam: the safety window and the re-arm processor read editor transiency
        // (isCompiling/isUpdating), which background imports flip mid-test-suite — silently deferring the
        // very removal a test is asserting. Non-null forces the answer; production always leaves it null.
        internal static bool? ForceTransientForTests;

        private static bool EditorIsTransient =>
            ForceTransientForTests ?? (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating);

        // Types whose removals/SpaceClears/scavenges were dropped by the safety window, awaiting a stable
        // editor. Statics: a drop during isCompiling is wiped by the coming domain reload, but that reload
        // recreates every inspector, whose rebuild re-runs the reconcile anyway — the queue only needs to
        // survive the POST-reload isUpdating window, where no further reload comes and statics persist.
        private static readonly HashSet<Type> _pendingRemovalRearms = new();
        private static bool _rearmHooked;

        // Types CONFIRMED to genuinely have zero serializable fields — verified by a fresh reflection read
        // in a stable editor (the re-arm handler). The safety window lets removals through for these: a
        // user deleting a script's last field(s) is the legitimate way a type becomes fieldless, and the
        // orphaned nodes must still prune. Per-domain by design: fields can only change via a compile,
        // whose reload clears this set, so a confirmation can never go stale within a session.
        private static readonly HashSet<Type> _confirmedFieldlessTypes = new();

        private static void ScheduleDeferredRemovalRearm(Type type)
        {
            if (type == null) return;
            _pendingRemovalRearms.Add(type);
            if (_rearmHooked) return;
            _rearmHooked = true;
            UnityEditor.EditorApplication.update += ProcessDeferredRemovalRearms;
        }

        // Waits out the compile/import, then re-reconciles each deferred type through the channel that owns
        // it: a designer holding the type re-runs its catch-up (which syncs its clone AND the registered
        // layout while the inspectors' auto-sync is suppressed); otherwise the registered layout reconciles
        // directly, with a targeted refresh so the pruned asset is what the live inspectors render. AutoAppend
        // only — the other modes keep script/layout drift user-visible by design. Before routing, each type's
        // reflection caches are dropped and re-read in the now-stable editor: a genuinely fieldless type is
        // recorded in _confirmedFieldlessTypes (its deferred prune then passes the safety window), while a
        // type whose earlier read was a transient misread reconciles against the fresh truth. No re-queue
        // loop is possible: a stable pass either applies the drop or confirms the steady state.
        private static void ProcessDeferredRemovalRearms()
        {
            if (EditorIsTransient) return;
            UnityEditor.EditorApplication.update -= ProcessDeferredRemovalRearms;
            _rearmHooked = false;
            var types = new List<Type>(_pendingRemovalRearms);
            _pendingRemovalRearms.Clear();

            var settings = SasquatchSettingsManager.Current;
            if (settings == null || settings.UnboundFieldHandling != UnboundFieldSyncMode.AutoAppend) return;
            foreach (var type in types)
            {
                InvalidateReflectionCaches(type);
                if (GetCurrentPathSet(type).Count == 0) _confirmedFieldlessTypes.Add(type);

                if (SasquatchLayoutDesignerWindow.IsEditingType(type))
                {
                    SasquatchLayoutDesignerWindow.RearmCloneReconcile(type);
                }
                else if (LayoutRegistry.TryGet(type, out var layout))
                {
                    int changes = ReconcileLayout(type, layout, settings.ApplyScriptAttributesOnSync);
                    if (changes > 0) SasquatchEditorRegistrar.RefreshInspectorsForType(type);
                }
            }
        }

        private static void InvalidateReflectionCaches(Type type)
        {
            _allPropsCache.Remove(type);
            _bindablePropsCache.Remove(type);
            _currentPathSetCache.Remove(type);
            _formerToCurrentCache.Remove(type);
            // Keyed by (type, collectionPath), so drop every entry belonging to this type.
            var staleLeaves = new List<(Type, string)>();
            foreach (var key in _elementLeafCache.Keys) if (key.Item1 == type) staleLeaves.Add(key);
            foreach (var key in staleLeaves) _elementLeafCache.Remove(key);
        }

        /// <summary>Clears the deferred re-arm queue, its update hook, and the fieldless confirmations —
        /// the re-arm/confirm state is cross-test-contaminating (a confirmation persists for the domain),
        /// so tests touching the safety window call this on entry and exit.</summary>
        internal static void ResetDeferredRearmStateForTests()
        {
            ForceTransientForTests = null;
            _pendingRemovalRearms.Clear();
            _confirmedFieldlessTypes.Clear();
            if (_rearmHooked)
            {
                UnityEditor.EditorApplication.update -= ProcessDeferredRemovalRearms;
                _rearmHooked = false;
            }
        }

        // ---- diff internals -----------------------------------------------------------------

        /// <summary>
        /// Whether an (unindexed) resolved path still exists AS A SERIALIZED FIELD on the type: the
        /// serializable path set first (covers collection ELEMENT paths like "modifications.modName",
        /// which segment-walking reflection can't traverse unindexed), then a reflection fallback
        /// (covers base-class fields and drawer-collapsed types like SceneReference) — gated so a field
        /// that still exists in code but is no longer serialized (public → private, [SerializeField]
        /// removed) counts as dead. The single liveness rule shared by the diff engine, the designer's
        /// orphan tools, and the live builder's dead-column filtering.
        /// </summary>
        public static bool IsLivePath(Type type, string resolvedPath)
        {
            if (type == null) return false;
            return !IsDeadPath(type, resolvedPath, GetCurrentPathSet(type));
        }

        private static bool IsDeadPath(Type type, string resolvedPath, HashSet<string> currentPaths)
        {
            if (string.IsNullOrEmpty(resolvedPath)) return false;
            if (currentPaths.Contains(resolvedPath)) return false;
            if (BindingFactory.GetFieldInfoFromPropertyPath(type, resolvedPath, out _) == null) return true;

            // The field chain still exists in CODE, but a field Unity no longer SERIALIZES (public →
            // private, [SerializeField]/[SerializeReference] removed, [NonSerialized] added) has no
            // SerializedProperty behind it — its binding is exactly as dead as a deleted field's, so it
            // must not survive on the reflection fallback above (that fallback exists for base-class
            // fields and drawer-collapsed types, which ARE still serialized). Walk every segment: a
            // parent struct going private kills its whole subtree even though the leaf member itself may
            // still be public. A segment this walk can't resolve (e.g. an unindexed collection-element
            // path) stays live — the leaf resolver above vouched for the chain, and the bias here is
            // firmly toward NOT deleting the user's nodes.
            var currentType = type;
            foreach (var segment in resolvedPath.Split('.'))
            {
                var fi = currentType != null
                    ? GetFieldIncludingBase(UnwrapElementType(currentType), segment)
                    : null;
                if (fi == null) return false;
                if (!SasquatchReflectionUtility.IsSerializableField(fi)) return true;
                currentType = fi.FieldType;
            }
            return false;
        }

        // Every current serializable path (leaves + collections) plus every dotted prefix (struct scopes).
        // Cached per type; statics reset on domain reload, exactly when fields can change.
        private static readonly Dictionary<Type, HashSet<string>> _currentPathSetCache = new();

        private static HashSet<string> GetCurrentPathSet(Type type)
        {
            if (_currentPathSetCache.TryGetValue(type, out var cached)) return cached;

            var set = new HashSet<string>();
            foreach (var p in GetAllProps(type))
            {
                set.Add(p.path);
                int dot = p.path.IndexOf('.');
                while (dot > 0)
                {
                    set.Add(p.path.Substring(0, dot));
                    dot = p.path.IndexOf('.', dot + 1);
                }
            }
            if (ReflectionIsSettled) _currentPathSetCache[type] = set;
            return set;
        }

        private static string ScopePrefixOf(string path)
        {
            int i = path.LastIndexOf('.');
            return i < 0 ? "" : path.Substring(0, i);
        }

        // Exact or prefix mapping through the rename map; null when unmapped.
        private static string MapRenamedPath(string resolved, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(resolved)) return null;
            if (map.TryGetValue(resolved, out var direct)) return direct;
            foreach (var kv in map)
            {
                if (resolved.StartsWith(kv.Key + ".")) return kv.Value + resolved.Substring(kv.Key.Length);
            }
            return null;
        }

        // Maps every [FormerlySerializedAs]-derivable OLD path (segment-wise old names, incl. struct
        // prefixes) to its CURRENT path. Empty for types that use no FSA. Cached per type per domain.
        private static readonly Dictionary<Type, Dictionary<string, string>> _formerToCurrentCache = new();

        private static Dictionary<string, string> GetFormerToCurrentMap(Type type)
        {
            if (_formerToCurrentCache.TryGetValue(type, out var cached)) return cached;

            var map = new Dictionary<string, string>();
            var currentPaths = GetCurrentPathSet(type);
            foreach (var path in currentPaths)
            {
                foreach (var variant in BuildFormerVariants(type, path))
                {
                    if (!map.ContainsKey(variant)) map[variant] = path;
                }
            }
            if (ReflectionIsSettled) _formerToCurrentCache[type] = map;
            return map;
        }

        // All old-name spellings of a current path (cartesian product of each segment's current name +
        // its [FormerlySerializedAs] old names), excluding the current spelling. Empty when no segment
        // carries the attribute. Walks segments with collection-element awareness (an unindexed element
        // path like "items.itemName" must resolve THROUGH the List's element type — segment reflection
        // via GetFieldInfoFromPropertyPath can't, it only unwraps explicit "Array.data[N]" parts).
        private static List<string> BuildFormerVariants(Type type, string path)
        {
            var results = new List<string>();
            var segments = path.Split('.');
            var options = new List<List<string>>(segments.Length);
            bool anyFormer = false;

            Type currentType = type;
            foreach (var segment in segments)
            {
                var names = new List<string> { segment };
                FieldInfo fi = null;
                if (currentType != null)
                {
                    fi = GetFieldIncludingBase(UnwrapElementType(currentType), segment);
                    currentType = fi?.FieldType;
                }
                if (fi != null)
                {
                    foreach (var attr in fi.GetCustomAttributes(typeof(FormerlySerializedAsAttribute), false))
                    {
                        string oldName = ((FormerlySerializedAsAttribute)attr).oldName;
                        if (!string.IsNullOrEmpty(oldName) && oldName != segment)
                        {
                            names.Add(oldName);
                            anyFormer = true;
                        }
                    }
                }
                options.Add(names);
            }
            if (!anyFormer) return results;

            void Expand(int depth, string acc)
            {
                if (depth == options.Count)
                {
                    if (acc != path) results.Add(acc);
                    return;
                }
                foreach (var name in options[depth])
                    Expand(depth + 1, acc.Length == 0 ? name : acc + "." + name);
            }
            Expand(0, "");
            return results;
        }

        private static Type UnwrapElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return type.GetGenericArguments()[0];
            return type;
        }

        private static FieldInfo GetFieldIncludingBase(Type type, string fieldName)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var fi = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null) return fi;
            }
            return null;
        }

        // Scoped containers whose resolved PropertyPath is dead but FSA-maps to a live prefix. Recurses
        // under the NEW name so deeper detection stays consistent; dead scopes WITHOUT an FSA mapping are
        // left alone (a scoped container already degrades softly when its path doesn't resolve).
        private static void CollectScopeRenames(ContainerNode container, string basePath, Type type,
            HashSet<string> currentPaths, Dictionary<string, string> former, List<RenamedBinding> output)
        {
            if (container == null || container.Children == null) return;
            foreach (var child in container.Children)
            {
                if (child is ContainerNode c)
                {
                    string nextBase = basePath;
                    if (!string.IsNullOrEmpty(c.PropertyPath))
                    {
                        string resolved = Resolve(basePath, c.PropertyPath);
                        if (IsDeadPath(type, resolved, currentPaths) && former.TryGetValue(resolved, out var current))
                        {
                            output.Add(new RenamedBinding
                            {
                                OldResolvedPath = resolved,
                                NewResolvedPath = current,
                                ViaFormerlySerializedAs = true
                            });
                            resolved = current;
                        }
                        nextBase = resolved;
                    }
                    CollectScopeRenames(c, nextBase, type, currentPaths, former, output);
                }
                else if (child is CollectionNode col && col.ItemLayout is ContainerNode itemContainer)
                {
                    string resolved = string.IsNullOrEmpty(col.PropertyPath) ? basePath : Resolve(basePath, col.PropertyPath);
                    CollectScopeRenames(itemContainer, resolved, type, currentPaths, former, output);
                }
            }
        }

        private static void ApplyScopeRenames(ContainerNode container, string basePath, Dictionary<string, string> map)
        {
            if (container == null || container.Children == null) return;
            foreach (var child in container.Children)
            {
                if (child is ContainerNode c)
                {
                    string nextBase = basePath;
                    if (!string.IsNullOrEmpty(c.PropertyPath))
                    {
                        string resolved = Resolve(basePath, c.PropertyPath);
                        if (map.TryGetValue(resolved, out var renamed))
                        {
                            if (basePath.Length == 0) c.PropertyPath = renamed;
                            else if (renamed.StartsWith(basePath + ".")) c.PropertyPath = renamed.Substring(basePath.Length + 1);
                            resolved = renamed;
                        }
                        nextBase = resolved;
                    }
                    ApplyScopeRenames(c, nextBase, map);
                }
                else if (child is CollectionNode col && col.ItemLayout is ContainerNode itemContainer)
                {
                    string resolved = string.IsNullOrEmpty(col.PropertyPath) ? basePath : Resolve(basePath, col.PropertyPath);
                    ApplyScopeRenames(itemContainer, resolved, map);
                }
            }
        }

        // Like IndexLocations, but records full per-node locations and DOES descend into ItemLayout.
        // scopeMap (optional) substitutes pending scope renames while accumulating base paths, so the
        // diff resolves bindings under a renamed scope against the NEW prefix.
        private static void CollectBindingLocations(ContainerNode container, string basePath,
            CollectionNode ownerCollection, Dictionary<string, string> scopeMap,
            List<BindingLocation> output, HashSet<string> derived)
        {
            if (container == null || container.Children == null) return;

            if (derived != null && container.Type == ContainerType.Foldout && !string.IsNullOrEmpty(container.GroupTogglePath))
            {
                derived.Add(Resolve(basePath, container.GroupTogglePath));
            }

            foreach (var child in container.Children)
            {
                if (child is BindingNode bn && !string.IsNullOrEmpty(bn.PropertyPath))
                {
                    output.Add(new BindingLocation
                    {
                        Node = bn,
                        Parent = container,
                        OwnerCollection = ownerCollection,
                        IsItemLayoutRoot = false,
                        BasePath = basePath,
                        ResolvedPath = Resolve(basePath, bn.PropertyPath)
                    });
                }
                else if (child is CollectionNode col && !string.IsNullOrEmpty(col.PropertyPath))
                {
                    string resolved = Resolve(basePath, col.PropertyPath);
                    output.Add(new BindingLocation
                    {
                        Node = col,
                        Parent = container,
                        OwnerCollection = ownerCollection,
                        IsItemLayoutRoot = false,
                        BasePath = basePath,
                        ResolvedPath = resolved
                    });
                    if (col.ItemLayout is ContainerNode itemContainer)
                    {
                        CollectBindingLocations(itemContainer, resolved, col, scopeMap, output, derived);
                    }
                    else if (col.ItemLayout is BindingNode itemBinding && !string.IsNullOrEmpty(itemBinding.PropertyPath))
                    {
                        output.Add(new BindingLocation
                        {
                            Node = itemBinding,
                            Parent = null,
                            OwnerCollection = col,
                            IsItemLayoutRoot = true,
                            BasePath = resolved,
                            ResolvedPath = Resolve(resolved, itemBinding.PropertyPath)
                        });
                    }
                }
                else if (child is ContainerNode c)
                {
                    string nextBase = basePath;
                    if (!string.IsNullOrEmpty(c.PropertyPath))
                    {
                        nextBase = Resolve(basePath, c.PropertyPath);
                        if (scopeMap != null && scopeMap.TryGetValue(nextBase, out var renamedScope))
                        {
                            nextBase = renamedScope;
                        }
                    }
                    CollectBindingLocations(c, nextBase, ownerCollection, scopeMap, output, derived);
                }
            }
        }

        // One walk rewriting scope-relative references that RESOLVE to a renamed path: conditions
        // (struct list — write back by index) and toggle-group bool paths. A toggle path resolving to an
        // unpaired-REMOVED binding is cleared so the foldout reverts to a plain one. Method-name fields
        // (ValidationMethod / ValueSourceMethod / button methods) are names, not paths — untouched.
        private static void RewriteRenamedReferences(LayoutNode node, string basePath,
            Dictionary<string, string> map, HashSet<string> removed)
        {
            if (node == null) return;

            // Rewrite condition driver paths in BOTH the rule-based list and the legacy flat list (a node may be in
            // either state depending on whether it's been migrated). Member-driver names (not serialized paths) are
            // never in the rename map, so they pass through untouched.
            void RewriteConditionList(List<VisualCondition> list)
            {
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    var condition = list[i];
                    if (string.IsNullOrEmpty(condition.PropertyPath)) continue;
                    string resolved = Resolve(basePath, condition.PropertyPath);
                    string mapped = MapRenamedPath(resolved, map);
                    if (mapped == null) continue;
                    string rel = MakeRelative(mapped, basePath);
                    if (rel == null) continue;
                    condition.PropertyPath = rel;
                    list[i] = condition;
                }
            }

            RewriteConditionList(node.Conditions);
            if (node.ConditionRules != null)
            {
                foreach (var rule in node.ConditionRules)
                {
                    if (rule?.Items == null) continue;
                    foreach (var item in rule.Items)
                        RewriteConditionList(item?.Conditions);
                }
            }

            if (node is ContainerNode container)
            {
                if (!string.IsNullOrEmpty(container.GroupTogglePath))
                {
                    string resolved = Resolve(basePath, container.GroupTogglePath);
                    string mapped = MapRenamedPath(resolved, map);
                    if (mapped != null)
                    {
                        string rel = MakeRelative(mapped, basePath);
                        if (rel != null) container.GroupTogglePath = rel;
                    }
                    else if (removed.Contains(resolved))
                    {
                        container.GroupTogglePath = null;
                    }
                }

                // A Frame's per-object image fill is the THIRD path-bearing reference in the model, and it was
                // the one omission from this walk. It is filled from the same path namespace as everything else
                // (the designer picker feeds it GetSerializableProperties(...).path) and consumed with
                // FindProperty, so after a field rename it silently pointed at a name that no longer existed:
                // the frame lost its artwork with no warning, and the design-mode hint read "Fill: field is
                // empty", pointing the user at their data instead of at the dead path. Mirrors GroupTogglePath
                // exactly, including nulling on delete. (Audit 2026-08-09, finding UFS-2.)
                var frame = container.Frame;
                if (frame != null && !string.IsNullOrEmpty(frame.FillBindingPath))
                {
                    string resolvedFill = Resolve(basePath, frame.FillBindingPath);
                    string mappedFill = MapRenamedPath(resolvedFill, map);
                    if (mappedFill != null)
                    {
                        string rel = MakeRelative(mappedFill, basePath);
                        if (rel != null) frame.FillBindingPath = rel;
                    }
                    else if (removed.Contains(resolvedFill))
                    {
                        frame.FillBindingPath = "";
                    }
                }

                string nextBase = string.IsNullOrEmpty(container.PropertyPath)
                    ? basePath
                    : Resolve(basePath, container.PropertyPath);
                if (container.Children != null)
                {
                    foreach (var child in container.Children)
                        RewriteRenamedReferences(child, nextBase, map, removed);
                }
            }
            else if (node is CollectionNode col && col.ItemLayout != null)
            {
                string resolved = string.IsNullOrEmpty(col.PropertyPath) ? basePath : Resolve(basePath, col.PropertyPath);
                RewriteRenamedReferences(col.ItemLayout, resolved, map, removed);
            }
        }

        private static string MakeRelative(string resolved, string basePath)
        {
            if (string.IsNullOrEmpty(basePath)) return resolved;
            if (resolved.StartsWith(basePath + ".")) return resolved.Substring(basePath.Length + 1);
            return null; // unexpressible from this scope — caller leaves the reference untouched
        }
    }
}
