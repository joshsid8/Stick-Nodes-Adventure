using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Fans a properties-panel edit out to the rest of a multi-selection, by DELTA.
    ///
    /// <para>The designer's Properties panel is one very large single-node builder — every widget option in it
    /// closes over one <see cref="LayoutNode"/>. Re-authoring those rows a second time for multi-selection is
    /// what produced the old, tiny multi-edit panel: a hand-written subset that fell further behind the real
    /// panel with every widget added. So instead of re-authoring the rows, the real panel builds against the
    /// PRIMARY node and this class copies whatever it changed onto the followers.</para>
    ///
    /// <para><b>How:</b> snapshot the primary's serialized fields when the panel is built; on each
    /// <c>OnModification</c>, compare against the snapshot and write only the fields that actually changed. An
    /// edit that touches one row therefore mirrors one field — the untouched differences between the selected
    /// nodes survive, which is the whole contract ("the first edit unifies THAT row", not the whole node).</para>
    ///
    /// <para><b>Why field-level reflection over our own model</b> (not Editor internals — Asset Store §2.5.g is
    /// about reflecting into Unity, and this never leaves <c>SasquatchInspector.Runtime</c>): the fields ARE the
    /// serialized state, so the mirror is automatically complete. A widget option added to
    /// <see cref="BindingNode"/> tomorrow fans out with no edit here — the failure mode that a hand-maintained
    /// list guarantees, and that <see cref="StyleClipboard"/> pays for with ~200 hand-written transfer lambdas.
    /// Copying the FIELD, never the property, is also deliberate: property setters carry side effects (several
    /// force <c>isOverridden = true</c>) and the follower must land on the primary's state exactly.</para>
    ///
    /// <para>Three fields cannot be assigned raw and are routed through their migrations instead — see
    /// <see cref="ApplyStructuralDelta"/>. Everything a node IS rather than looks like — paths, labels, headers,
    /// method names, prose — is blocked by <see cref="IdentityFields"/> and never travels.</para>
    ///
    /// <para><b>SCOPED mode.</b> The sweep above is opt-OUT and so fails UNSAFE: a field nobody classified is
    /// written over every follower's value. That is the right trade only at the top tier, where the selection
    /// has already proved every row means the same thing on every node. The lower tiers (different Display
    /// Types, or different node kinds entirely) offer only the handful of sections that are provably
    /// type-independent — Conditionals, Animations, Prefix &amp; Suffix, Validation — so they arm the mirror
    /// with a <c>scope</c>: an opt-IN allow-list of field names, which fails SAFE. A field left out of the
    /// scope simply does not travel. See the four <c>…Fields</c> sets below.</para>
    /// </summary>
    internal sealed class MultiEditMirror
    {
        private readonly LayoutNode _primary;
        private readonly List<LayoutNode> _followers = new List<LayoutNode>();
        private readonly Func<BindingNode, Type> _resolveFieldType;
        // Non-null = scoped mode: only these field names travel, and the structural trio is skipped entirely.
        private readonly ICollection<string> _scope;

        private readonly FieldInfo[] _fields;
        private object[] _snapshot;

        // Structural fields: assigning the value straight across would leave the follower's CHILDREN read as a
        // shape they were never built as. Handled explicitly, so they are held out of the generic sweep.
        private const string FieldDisplayType = "displayType";      // BindingNode — gated on the follower's bound type
        private const string FieldContainerType = "type";           // ContainerNode — ContainerTypeMigration.Apply
        private const string FieldFrame = "frame";                  // ContainerNode — ContainerTypeMigration.ApplyFrameShape

        /// <summary>
        /// Arms a mirror over <paramref name="selection"/> with <paramref name="primary"/> as the node the panel
        /// is built against. <paramref name="resolveFieldType"/> resolves a binding's bound code type (used to
        /// gate a widget-kind change per follower); it may return null when the type can't be resolved.
        /// <para><paramref name="scope"/> — null for the full opt-OUT sweep (the top tier). Non-null restricts
        /// the mirror to exactly those field names and skips the structural trio: the lower tiers' safe mode,
        /// where the selection has NOT proved that every row means the same thing on every node.</para>
        /// </summary>
        internal MultiEditMirror(LayoutNode primary, IEnumerable<LayoutNode> selection,
                                 Func<BindingNode, Type> resolveFieldType,
                                 ICollection<string> scope = null)
        {
            _primary = primary;
            _resolveFieldType = resolveFieldType;
            _scope = scope;
            if (selection != null)
            {
                foreach (var n in selection)
                    if (n != null && !ReferenceEquals(n, primary)) _followers.Add(n);
            }
            _fields = ScopedFields(primary?.GetType(), scope);
            Resnapshot();
        }

        /// <summary>True when this mirror only fans an explicit allow-list of fields.</summary>
        internal bool IsScoped => _scope != null;

        /// <summary>The node the panel edits; every mirrored value is read from here.</summary>
        internal LayoutNode Primary => _primary;

        /// <summary>True when there is at least one other node to fan edits out to.</summary>
        internal bool HasFollowers => _followers.Count > 0;

        /// <summary>
        /// How the NEXT fan-out should treat a follower whose children already look like a pane pair when the
        /// primary's Frame shape enters DiagonalSplit. ONE-SHOT: <see cref="ApplyDelta"/> reads it and resets
        /// it to the non-destructive default, so a mode the designer set for one deliberate shape change can
        /// never leak into the unrelated edit after it. The mirror itself never prompts — a fan-out cannot
        /// sensibly raise one dialog per node — so it simply carries the answer the panel already got.
        /// </summary>
        internal EnterSplitMode NextEnterSplit { get; set; } = EnterSplitMode.AdoptExistingPanes;

        /// <summary>Re-reads the primary's state as the new baseline. Called after every fan-out.</summary>
        internal void Resnapshot()
        {
            SnapshotSpecial();
            if (_primary == null) { _snapshot = Array.Empty<object>(); return; }
            _snapshot = new object[_fields.Length];
            for (int i = 0; i < _fields.Length; i++)
                _snapshot[i] = SnapshotValue(_fields[i].GetValue(_primary));
        }

        /// <summary>
        /// Writes every field the primary changed since the last snapshot onto the followers, then re-snapshots.
        /// Returns the number of fields fanned out (0 = the modification never touched this node, which is the
        /// case for every structural op — add, delete, reorder — so this is a cheap no-op on those).
        /// </summary>
        internal int ApplyDelta()
        {
            // Consumed here, before any early-out, so the one-shot is spent whether or not it was used.
            var enterSplit = NextEnterSplit;
            NextEnterSplit = EnterSplitMode.AdoptExistingPanes;

            if (_primary == null || _followers.Count == 0) return 0;

            int changed = 0;
            for (int i = 0; i < _fields.Length; i++)
            {
                var field = _fields[i];
                var now = field.GetValue(_primary);
                if (DeepEquals(_snapshot[i], now)) continue;

                foreach (var follower in _followers)
                {
                    if (follower == null || !field.DeclaringType.IsInstanceOfType(follower)) continue;
                    if (TryDeepClone(now, out var copy)) field.SetValue(follower, copy);
                }
                changed++;
            }

            changed += ApplyStructuralDelta(enterSplit);
            if (changed > 0) Resnapshot();
            return changed;
        }

        // ---- The three fields that own children -------------------------------------------------------------

        /// <summary>
        /// The structural trio, each applied through the migration that owns the follower's children rather than
        /// assigned. A raw <c>c.Type = Tabs</c> makes the builder read child N as tab N, so a container of eight
        /// widgets becomes eight one-item tabs and seven of every eight widgets vanish — the same trap the
        /// hand-written fan-out documents, reached here by a different road. The widget kind has a softer version
        /// of the problem: a follower bound to a different code type may not ACCEPT the primary's new kind, so it
        /// is offered the change and keeps its own when the catalog says no.
        /// </summary>
        private int ApplyStructuralDelta(EnterSplitMode enterSplit)
        {
            int changed = 0;

            // A scoped mirror never restructures. The lower tiers reach a Display Type / Container Type change
            // through the hand-written "Display (All)" / "Container (All)" rows, which fan out themselves via
            // FanOutEdit — routing it here as well would apply the same change twice.
            if (_scope != null) return 0;

            if (_primary is BindingNode primaryBinding)
            {
                var before = (DisplayType?)_special.displayType;
                if (before.HasValue && before.Value != primaryBinding.DisplayType)
                {
                    var kind = primaryBinding.DisplayType;
                    foreach (var follower in _followers)
                    {
                        if (!(follower is BindingNode b)) continue;
                        var fieldType = _resolveFieldType?.Invoke(b);
                        // Null (mid-compile, or an unresolvable path) means UNKNOWN, not "no" — the whole
                        // selection reached this tier by already sharing a kind, so the honest move is to let
                        // the deliberate edit through rather than silently drop it on one node.
                        if (fieldType != null &&
                            !DisplayTypeCatalog.OfferedForField(b.DisplayType, fieldType).Contains(kind)) continue;
                        b.DisplayType = kind;
                        // Mirrors the single-edit side effect: a fresh progress bar starts interactive.
                        if (kind == DisplayType.ProgressBar) b.ProgressBarInteractive = true;
                    }
                    changed++;
                }
            }

            if (_primary is ContainerNode primaryContainer)
            {
                var beforeType = (ContainerType?)_special.containerType;
                if (beforeType.HasValue && beforeType.Value != primaryContainer.Type)
                {
                    var kind = primaryContainer.Type;
                    // KeepAsGroups: a fan-out cannot sensibly raise one dialog per node, so it takes the
                    // non-destructive default (the same call the hand-written Container (All) row makes).
                    foreach (var follower in _followers)
                        if (follower is ContainerNode c) ContainerTypeMigration.Apply(c, kind);
                    changed++;
                }

                // A Frame's whole look lives in one FrameSettings object, so the delta is the object — but two
                // parts of it can't be assigned across. The SHAPE re-reads the children (a DiagonalSplit frame's
                // direct children ARE its two panes), so it goes through its migration; and the four image
                // references are CONTENT — a bound fill path names a field on one target type, the GUIDs name
                // specific texture assets — so each follower keeps its own.
                if (!DeepEquals(_special.frame, primaryContainer.Frame))
                {
                    foreach (var follower in _followers)
                    {
                        if (!(follower is ContainerNode c)) continue;
                        if (!TryDeepClone(primaryContainer.Frame, out var copy) || !(copy is FrameSettings incoming)) continue;

                        incoming.FillBindingPath = c.Frame.FillBindingPath;
                        incoming.FillTextureGuid = c.Frame.FillTextureGuid;
                        incoming.PaneAFillGuid = c.Frame.PaneAFillGuid;
                        incoming.PaneBFillGuid = c.Frame.PaneBFillGuid;

                        var newShape = incoming.Shape;
                        incoming.Shape = c.Frame.Shape;   // rewound, then re-applied through the migration
                        c.Frame = incoming;
                        ContainerTypeMigration.ApplyFrameShape(c, newShape, enterSplit: enterSplit);
                    }
                    changed++;
                }
            }

            return changed;
        }

        // The structural trio's baseline, held beside the generic snapshot (they are excluded from _fields).
        private (object displayType, object containerType, object frame) _special;

        private void SnapshotSpecial()
        {
            _special = (
                _primary is BindingNode b ? (object)b.DisplayType : null,
                _primary is ContainerNode c ? (object)c.Type : null,
                _primary is ContainerNode cf ? SnapshotValue(cf.Frame) : null);
        }

        // ---- What differs across the selection (for the panel's header) --------------------------------------

        /// <summary>
        /// How many mirrorable settings currently disagree anywhere in <paramref name="nodes"/>. The panel shows
        /// the primary's values, so this is what the header owes the user: a count of the rows below that are
        /// NOT telling the whole truth. 0 means the selection is genuinely uniform.
        /// </summary>
        internal static int CountDifferingSettings(IReadOnlyList<LayoutNode> nodes) =>
            CountDifferingSettings(nodes, null);

        /// <summary>
        /// <see cref="CountDifferingSettings(IReadOnlyList{LayoutNode})"/> narrowed to one section's fields, for
        /// the lower tiers — where the panel shows only a few sections and a whole-node count would name
        /// differences the user cannot see or reach.
        /// <para>The reference is <paramref name="nodes"/>[0] and its type decides which fields are walked, so
        /// across MIXED kinds this counts the fields the reference declares. That is the honest answer for a
        /// scope like <see cref="ConditionFields"/>, which is declared on <see cref="LayoutNode"/> and therefore
        /// present on every node whatever its kind.</para>
        /// </summary>
        internal static int CountDifferingSettings(IReadOnlyList<LayoutNode> nodes, ICollection<string> scope)
        {
            if (nodes == null || nodes.Count < 2) return 0;
            var reference = nodes[0];
            if (reference == null) return 0;

            var fields = ScopedFields(reference.GetType(), scope);
            int differing = 0;
            foreach (var field in fields)
            {
                var baseline = field.GetValue(reference);
                for (int i = 1; i < nodes.Count; i++)
                {
                    var other = nodes[i];
                    if (other == null || !field.DeclaringType.IsInstanceOfType(other)) continue;
                    if (DeepEquals(baseline, field.GetValue(other))) continue;
                    differing++;
                    break;
                }
            }
            return differing;
        }

        // ---- Which fields travel ----------------------------------------------------------------------------

        /// <summary>
        /// What a node IS, rather than how it looks: never mirrored, however the panel is driven. These are the
        /// same rows the multi-selection panel hides — the block here is the belt to that braces, so a field can
        /// never travel by some route other than its row (a preset apply, a sync pass, a future control).
        /// </summary>
        private static readonly HashSet<string> IdentityFields = new HashSet<string>
        {
            // LayoutNode — per-node text, and the two geometry values a DIVIDER DRAG owns (their container-level
            // twins are excluded from StyleClipboard for the same reason: dragged results, not authored styling).
            "tooltip", "styleToken", "spaceImported", "columnWeight", "rowHeight",

            // BindingNode — the binding's identity, its prose, and the per-target-type code hooks.
            "propertyPath", "labelOverride", "header", "helpText",
            "valueSourceMethod",
            // NOTE: the four VALIDATION prose/hook fields — validationMethod, validationMessage,
            // requiredMessage, rangeValidationMessage — used to sit here and no longer do. A validation method
            // names a method on the layout's ONE target type, so it is shareable in a way propertyPath never
            // is; and the messages are prose the user is deliberately setting on the row in front of them.
            // The mirror is delta-based, so neither travels unasked. Their rows lost their Identity(...) wrap
            // in the same pass — the two authorities always move together.
            // Keyed to ONE enum's member names / to one target type's property paths: inert noise anywhere else.
            "enumBadgeOverrides", "customWidgetInputs",

            // ContainerNode — title/tab prose, the live tab index, and the two lists a divider drag writes.
            "title", "tabLabel", "activeTabIndex", "groupTogglePath",
            "sourceHeader", "hierarchyName", "gridColumnWeights", "gridRowHeights",

            // CollectionNode
            "titleOverride", "searchProperty",

            // ButtonNode — what it CALLS and what it says.
            "methodName", "methodSignature", "buttonText", "confirmTitle", "confirmMessage",

            // Leaf widgets — each one's content.
            "message", "collapsedSummary",   // HelpBoxNode
            "text",                          // TextBlockNode
            "textureGuid",                   // ImageBannerNode
            "textOverride", "customIcon",    // ScriptHeaderNode
            "ownerFieldPath",                // SpaceNode
            "propertyName",                  // PropertyNode
            "sliceMethodName",               // EmbeddedEditorNode
        };

        // Legacy fields folded into a successor on load. Harmless to copy, but they are dead weight that would
        // read as a real setting in a diff, so they sit out.
        private static readonly HashSet<string> LegacyFields = new HashSet<string>
        {
            "previewBelowField", "audioWaveformBelow",
            // BindingNode.unitSuffix — the pre-FieldAffix unit string. OnBeforeSerialize re-derives it from
            // suffixAffix.text on every write, so it is the SAME setting counted twice: a selection where one
            // node has "kg" and another has none reported "2 settings currently differ" for one real
            // difference. Fanning it out separately could also hand a follower a unitSuffix that disagrees
            // with its own suffixAffix.text until the next serialize re-mirrors it.
            "unitSuffix"
        };

        // ---- The scope sets: what the LOWER tiers are allowed to fan --------------------------------------
        //
        // These are opt-IN allow-lists, the exact inverse of IdentityFields above, and they exist because the
        // lower tiers show sections whose meaning does NOT depend on the node's Display Type or even its kind.
        // Each set is the complete serialized backing of one section — miss a field and that row silently
        // stops travelling; add one that is not in the section and it travels with no row to explain it.

        /// <summary>Conditionals. Every node kind has these — they are declared on <see cref="LayoutNode"/>.</summary>
        internal static readonly HashSet<string> ConditionFields = new HashSet<string>
        {
            "conditionRules",
            // The LEGACY flat list rides along so a selection can never end up with the two halves disagreeing.
            //
            // ⚠ Listing it here is NOT what stops a deleted rule resurrecting from it, and reasoning that it
            // does is the trap. This mirror is DELTA-based, and the PRIMARY's legacy list never changes — so it
            // never travels, however carefully it is named here. The real defence is upstream:
            // ArmMultiEditMirror calls ConditionModel.StripLegacyConditions over the whole selection BEFORE the
            // snapshot. Proven by TestConditionFanOutCarriesTheLegacyListToo, which failed with the name
            // already in this set.
            "conditions",
        };

        /// <summary>Animations — also <see cref="LayoutNode"/>-level, so also every kind.</summary>
        internal static readonly HashSet<string> AnimationFields = new HashSet<string>
        {
            "animations",
        };

        /// <summary>Prefix &amp; Suffix. <see cref="BindingNode"/> only. `unitSuffix` is deliberately absent —
        /// it is a legacy shim re-derived from `suffixAffix.text` on every serialize (see LegacyFields).</summary>
        internal static readonly HashSet<string> AffixFields = new HashSet<string>
        {
            "prefixAffix", "suffixAffix", "affixPerComponent", "affixInColumnHeader",
        };

        /// <summary>Validation Settings. <see cref="BindingNode"/> carries all of these;
        /// <see cref="CollectionNode"/> carries the required/method/message subset.</summary>
        internal static readonly HashSet<string> ValidationFields = new HashSet<string>
        {
            "required", "validateRange", "validationMin", "validationMax",
            "rangeHasMin", "rangeHasMax", "clampToRange",
            "requiredMessage", "rangeValidationMessage", "validationMethod", "validationMessage",
        };

        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();

        /// <summary>Every field on <paramref name="nodeType"/> that a multi-edit may fan out.</summary>
        internal static FieldInfo[] MirrorableFields(Type nodeType)
        {
            if (nodeType == null) return Array.Empty<FieldInfo>();
            if (FieldCache.TryGetValue(nodeType, out var cached)) return cached;

            var list = new List<FieldInfo>();
            foreach (var f in AllInstanceFields(nodeType))
                if (IsMirrorable(f)) list.Add(f);

            var result = list.ToArray();
            FieldCache[nodeType] = result;
            return result;
        }

        /// <summary>
        /// <see cref="MirrorableFields"/> narrowed to <paramref name="scope"/>, or the whole set when it is null.
        /// The scope is an intersection, never a bypass: a name in the scope that is also in
        /// <see cref="IdentityFields"/> still does not travel, so a section can never smuggle identity out
        /// through its allow-list. Uncached — the scoped sets are small and built once per panel.
        /// </summary>
        internal static FieldInfo[] ScopedFields(Type nodeType, ICollection<string> scope)
        {
            var all = MirrorableFields(nodeType);
            if (scope == null) return all;

            var list = new List<FieldInfo>();
            foreach (var f in all)
                if (scope.Contains(f.Name)) list.Add(f);
            return list.ToArray();
        }

        private static bool IsMirrorable(FieldInfo f)
        {
            if (f.IsStatic || f.IsInitOnly) return false;
            if (f.IsDefined(typeof(NonSerializedAttribute), false)) return false;

            var name = f.Name;
            if (IdentityFields.Contains(name) || LegacyFields.Contains(name)) return false;
            // One-shot deserialize/migration bookkeeping — state ABOUT the data, not the data. Per-node by
            // nature: it records whether THIS node's legacy value has already been folded into its successor.
            if (name.EndsWith("Migrated", StringComparison.Ordinal)) return false;

            // NOTE: a "…Seeded" flag looks like the same thing and is NOT. It is a live gate the GETTER reads
            // on every access (progressBarFillGradientSeeded → "is the stored ramp real, or do you get the
            // default red→yellow→green one?"), so it is half of a value and has to travel with its other half.
            // Excluding it fanned the gradient onto the follower's field while leaving the follower's getter
            // still answering with the default ramp — the value was there and completely invisible.
            // The structural trio, applied through their migrations instead.
            if (name == FieldDisplayType || name == FieldContainerType || name == FieldFrame) return false;

            return IsMirrorableType(f.FieldType);
        }

        private static bool IsMirrorableType(Type t)
        {
            // The tree itself never travels: a node's children belong to that node.
            if (typeof(LayoutNode).IsAssignableFrom(t)) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false; // asset refs are content

            if (t.IsArray) return IsMirrorableType(t.GetElementType());
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return IsMirrorableType(t.GetGenericArguments()[0]);
            return true;
        }

        private static readonly Dictionary<Type, FieldInfo[]> AllFieldCache = new Dictionary<Type, FieldInfo[]>();

        // GetFields does NOT return a base type's private fields, so the hierarchy is walked explicitly —
        // otherwise every LayoutNode-level field (Style, the condition rules) would silently sit out.
        private static FieldInfo[] AllInstanceFields(Type t)
        {
            if (AllFieldCache.TryGetValue(t, out var cached)) return cached;

            var list = new List<FieldInfo>();
            for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                list.AddRange(cur.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                            BindingFlags.NonPublic | BindingFlags.DeclaredOnly));

            var result = list.ToArray();
            AllFieldCache[t] = result;
            return result;
        }

        // ---- Value semantics --------------------------------------------------------------------------------
        //
        // Conditions and animations are LISTS, and the panel edits them IN PLACE — adding a rule mutates the
        // list the snapshot is holding, so a reference comparison would report "unchanged" and the edit would
        // never fan out. Both the snapshot and the copy therefore go by VALUE, recursively.

        private const int MaxDepth = 8;

        private static object SnapshotValue(object v) => TryDeepClone(v, out var copy) ? copy : v;

        /// <summary>
        /// An independent copy of <paramref name="v"/>. False when the value cannot be copied safely — the
        /// caller then leaves the follower's field alone, which is always better than aliasing it: two nodes
        /// sharing one condition-rule list means editing either silently rewrites both.
        /// </summary>
        internal static bool TryDeepClone(object v, out object clone) => TryDeepClone(v, 0, out clone);

        private static bool TryDeepClone(object v, int depth, out object clone)
        {
            clone = null;
            if (v == null) return true;
            if (depth > MaxDepth) return false;

            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)) { clone = v; return true; }
            // Asset references are shared deliberately — a copy of a Texture2D is not the point.
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) { clone = v; return true; }

            // Unity's two curve-ish classes: real classes, so a bare assignment aliases them, and neither
            // exposes its state as plain fields the recursion below could walk.
            if (t == typeof(Gradient)) { clone = CloneGradient((Gradient)v); return true; }
            if (t == typeof(AnimationCurve)) { clone = CloneCurve((AnimationCurve)v); return true; }

            if (t.IsArray)
            {
                var src = (Array)v;
                var dst = Array.CreateInstance(t.GetElementType(), src.Length);
                for (int i = 0; i < src.Length; i++)
                {
                    if (!TryDeepClone(src.GetValue(i), depth + 1, out var element)) return false;
                    dst.SetValue(element, i);
                }
                clone = dst;
                return true;
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var src = (IList)v;
                var dst = (IList)Activator.CreateInstance(t);
                foreach (var element in src)
                {
                    if (!TryDeepClone(element, depth + 1, out var copy)) return false;
                    dst.Add(copy);
                }
                clone = dst;
                return true;
            }

            // Plain [Serializable] struct or class — copy field by field. A class with no parameterless
            // constructor can't be built here, so it reports failure rather than aliasing.
            object instance;
            try { instance = Activator.CreateInstance(t); }
            catch { return false; }

            foreach (var f in AllInstanceFields(t))
            {
                if (f.IsStatic || f.IsInitOnly) continue;
                if (!TryDeepClone(f.GetValue(v), depth + 1, out var fieldCopy)) return false;
                f.SetValue(instance, fieldCopy);
            }
            clone = instance;
            return true;
        }

        /// <summary>Value equality over the same graph <see cref="TryDeepClone"/> walks.</summary>
        internal static bool DeepEquals(object a, object b) => DeepEquals(a, b, 0);

        private static bool DeepEquals(object a, object b, int depth)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (depth > MaxDepth) return true; // treat "too deep to tell" as unchanged rather than churning

            var t = a.GetType();
            if (t != b.GetType()) return false;

            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)) return a.Equals(b);
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return ReferenceEquals(a, b);
            if (t == typeof(Gradient)) return GradientsEqual((Gradient)a, (Gradient)b);
            if (t == typeof(AnimationCurve)) return CurvesEqual((AnimationCurve)a, (AnimationCurve)b);

            if (t.IsArray)
            {
                var x = (Array)a;
                var y = (Array)b;
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++)
                    if (!DeepEquals(x.GetValue(i), y.GetValue(i), depth + 1)) return false;
                return true;
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var x = (IList)a;
                var y = (IList)b;
                if (x.Count != y.Count) return false;
                for (int i = 0; i < x.Count; i++)
                    if (!DeepEquals(x[i], y[i], depth + 1)) return false;
                return true;
            }

            foreach (var f in AllInstanceFields(t))
            {
                if (f.IsStatic || f.IsInitOnly) continue;
                if (!DeepEquals(f.GetValue(a), f.GetValue(b), depth + 1)) return false;
            }
            return true;
        }

        private static Gradient CloneGradient(Gradient src)
        {
            var g = new Gradient();
            if (src != null) { g.mode = src.mode; g.SetKeys(src.colorKeys, src.alphaKeys); }
            return g;
        }

        private static bool GradientsEqual(Gradient a, Gradient b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            if (a.mode != b.mode) return false;

            var ca = a.colorKeys;
            var cb = b.colorKeys;
            if (ca.Length != cb.Length) return false;
            for (int i = 0; i < ca.Length; i++)
                if (ca[i].color != cb[i].color || !Mathf.Approximately(ca[i].time, cb[i].time)) return false;

            var aa = a.alphaKeys;
            var ab = b.alphaKeys;
            if (aa.Length != ab.Length) return false;
            for (int i = 0; i < aa.Length; i++)
                if (!Mathf.Approximately(aa[i].alpha, ab[i].alpha) ||
                    !Mathf.Approximately(aa[i].time, ab[i].time)) return false;

            return true;
        }

        private static AnimationCurve CloneCurve(AnimationCurve src)
        {
            if (src == null) return null;
            return new AnimationCurve(src.keys) { preWrapMode = src.preWrapMode, postWrapMode = src.postWrapMode };
        }

        private static bool CurvesEqual(AnimationCurve a, AnimationCurve b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            if (a.preWrapMode != b.preWrapMode || a.postWrapMode != b.postWrapMode) return false;
            var ka = a.keys;
            var kb = b.keys;
            if (ka.Length != kb.Length) return false;
            for (int i = 0; i < ka.Length; i++)
                if (!Mathf.Approximately(ka[i].time, kb[i].time) ||
                    !Mathf.Approximately(ka[i].value, kb[i].value) ||
                    !Mathf.Approximately(ka[i].inTangent, kb[i].inTangent) ||
                    !Mathf.Approximately(ka[i].outTangent, kb[i].outTangent)) return false;
            return true;
        }
    }
}
