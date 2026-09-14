using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Copies widget configuration between <see cref="BindingNode"/>s for the Layout Presets feature. Cloning
    /// round-trips a node through a throwaway <see cref="InspectorLayoutAsset"/> via <see cref="EditorJsonUtility"/>
    /// — the same polymorphic-graph-safe pattern <c>ShowcaseOverrides</c> uses — so EVERY serialized widget field
    /// (plus <c>displayStyleData</c> and the text-style block) is copied with zero drift as new options are added.
    /// </summary>
    public static class LayoutPresetApplier
    {
        // The small, stable allowlist of per-field identity / structural fields. These are NOT part of a type
        // generic preset: a template strips them (ClearIdentity); an upgrade carries the target's own across
        // (TransplantIdentity). Everything else on BindingNode is widget config and travels with the preset.

        /// <summary>A deep, independent copy of <paramref name="src"/> (all serialized fields, displayStyleData, text styles).</summary>
        public static BindingNode DeepClone(BindingNode src) => DeepCloneNode(src) as BindingNode;

        /// <summary>
        /// A deep, independent copy of ANY node — including a container and its whole subtree. The one node-cloning
        /// mechanism in the codebase: presets clone bindings through it, and the designer's Duplicate clones whatever
        /// the user selected. Keeping it single-sourced is what makes both paths pick up new serialized fields for
        /// free (a hand-rolled field-by-field copy drifts silently the moment a widget option is added).
        /// </summary>
        public static LayoutNode DeepCloneNode(LayoutNode src)
        {
            if (src == null) return null;

            // A throwaway asset is just a serialization vehicle. EditorJsonUtility (unlike JsonUtility) preserves
            // the polymorphic [SerializeReference] node graph — concrete node types and nested children included.
            // Serialize from one instance, deserialize into another so the clone is a fresh managed object with no
            // shared reference to the source.
            var writer = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            writer.hideFlags = HideFlags.DontSave;
            writer.Root = src;
            string json = EditorJsonUtility.ToJson(writer);
            writer.Root = null; // detach the aliased source before the vehicle is destroyed
            UnityEngine.Object.DestroyImmediate(writer);

            var reader = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            reader.hideFlags = HideFlags.DontSave;
            EditorJsonUtility.FromJsonOverwrite(json, reader);
            var clone = reader.Root;
            reader.Root = null; // detach the managed clone before the vehicle is destroyed
            UnityEngine.Object.DestroyImmediate(reader);
            return clone;
        }

        /// <summary>Resets a node's per-field identity / structural fields so it becomes a type-generic template.</summary>
        public static void ClearIdentity(BindingNode n)
        {
            if (n == null) return;
            n.PropertyPath = null;
            n.LabelOverride = null;
            n.HideLabel = false;
            n.Header = null;
            n.Tooltip = null;
            n.Conditions = new List<VisualCondition>();
            n.ConditionRules = new List<ConditionRule>();
            n.Animations = new List<NodeAnimation>();
            n.ColumnWeight = 0f;
            n.RowHeight = 0f;
            n.MakeWidthResizable = false;
            n.MakeHeightResizable = false;
            // A preset carries a LOOK. Capturing one from a hidden widget must not bake "and be invisible" into
            // it — every field the preset was later dropped on would silently disappear, with the cause three
            // steps back in the user's history.
            n.HiddenInInspector = false;
            // DICTIONARY CELL TEMPLATES ARE STRUCTURAL, NOT A LOOK — the one place this allowlist has to be
            // reasoned about rather than pattern-matched. They read as widget config (they ARE bindings with a
            // Display Type), so the default "everything else travels" would carry them; that is wrong twice
            // over, and both failures are silent.
            //
            // CARRYING them is wrong because a preset is TYPE-GENERIC and there is exactly ONE Dictionary
            // preset row for every dictionary in the project, whatever its K and V. Capturing a preset from a
            // Dictionary<Rarity, Color> whose values are a Color Palette would then hand that same template to
            // every Dictionary<string, int> the user imports afterwards — a palette widget over an int.
            //
            // And letting a preset OVERWRITE them is worse: applying one (or an "Override with Presets"
            // reset) would delete the cell styling the author built by hand, with no toast and no diff.
            // TransplantIdentity carries the target's own across, exactly as it does for HiddenInInspector.
            n.DictionaryKeyTemplate = null;
            n.DictionaryValueTemplate = null;
        }

        /// <summary>Copies the identity / structural allowlist from <paramref name="src"/> onto <paramref name="dst"/>.</summary>
        public static void TransplantIdentity(BindingNode dst, BindingNode src)
        {
            if (dst == null || src == null) return;
            dst.PropertyPath = src.PropertyPath;
            dst.LabelOverride = src.LabelOverride;
            dst.HideLabel = src.HideLabel;
            dst.Header = src.Header;
            dst.Tooltip = src.Tooltip;
            dst.Conditions = src.Conditions;
            dst.ConditionRules = src.ConditionRules;
            dst.Animations = src.Animations;
            dst.ColumnWeight = src.ColumnWeight;
            dst.RowHeight = src.RowHeight;
            dst.MakeWidthResizable = src.MakeWidthResizable;
            dst.MakeHeightResizable = src.MakeHeightResizable;
            // LOCKSTEP with the ClearIdentity line above: a preset must not CARRY "and be invisible", and
            // applying one must not DESTROY the author's own hide. Both preset-swap paths (TryUpgrade,
            // TryReskinForContext) build a replacement node and splice it into the tree, so without this the
            // field the author hid silently reappears in the shipped Inspector — no toast, no diff, and the
            // cause several steps back in their history. Structural per-node state, same class as the two
            // resizable flags directly above.
            dst.HiddenInInspector = src.HiddenInInspector;
            // LOCKSTEP with the ClearIdentity lines above, and for the sharper of the two reasons given
            // there: a preset swap must not silently destroy hand-authored dictionary cell styling.
            dst.DictionaryKeyTemplate = src.DictionaryKeyTemplate;
            dst.DictionaryValueTemplate = src.DictionaryValueTemplate;
            // NOT the affixes. An affix is widget CONFIG, so "Override with Presets" is meant to reset it to
            // the preset's — TestPresetsCarryAffixesAndPaintingDoesNot pins exactly that. The List<->Table
            // mode flip is a different question and is handled in TryReskinForContext, which is not a reset.
        }

        /// <summary>Captures <paramref name="source"/>'s full widget config as a type-generic preset template.</summary>
        public static BindingNode CaptureTemplateFrom(BindingNode source)
        {
            var clone = DeepClone(source);
            ClearIdentity(clone);
            return clone;
        }

        /// <summary>
        /// True when two templates carry identical serialized config (used to auto-prune a collection-context
        /// override that a user has edited back to match its base preset). Compares the same JSON the clone path uses,
        /// so every serialized widget field participates with zero per-field maintenance.
        /// <para>Both sides are round-tripped first so the comparison sees CANONICAL forms. BindingNode's
        /// <c>OnAfterDeserialize</c> carries one-shot migration bookkeeping (<c>rangeLimitsMigrated</c> and the
        /// placement folds), which is set on any template that has been through a load but NOT on one just built in
        /// code — so a freshly built base template (right after Reset to Defaults, or a brand-new preset asset) read
        /// as "different" from an override holding the very same values, and the override was never pruned. A
        /// round-trip runs the folds on both, leaving only real config differences.</para>
        /// </summary>
        public static bool TemplatesEqual(BindingNode a, BindingNode b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return SerializeForCompare(DeepClone(a)) == SerializeForCompare(DeepClone(b));
        }

        private static string SerializeForCompare(BindingNode n)
        {
            var vehicle = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            vehicle.hideFlags = HideFlags.DontSave;
            vehicle.Root = n;
            string json = EditorJsonUtility.ToJson(vehicle);
            vehicle.Root = null; // detach the aliased node before the vehicle is destroyed
            UnityEngine.Object.DestroyImmediate(vehicle);
            return json;
        }

        /// <summary>
        /// Builds the binding for an imported field: when <paramref name="preset"/> has an enabled template for the
        /// field's type category, a configured clone of it (with the field's path); otherwise a plain binding.
        /// </summary>
        public static BindingNode MakeConfiguredBinding(string propertyPath, Type fieldType, SasquatchLayoutPreset preset)
        {
            if (preset != null && fieldType != null)
            {
                var cat = LayoutPresetCategories.Match(fieldType);
                if (cat.HasValue && preset.TryGetTemplate(cat.Value, out var template))
                {
                    var clone = DeepClone(template);
                    ClearIdentity(clone);           // templates are already clean; defensive against stale assets
                    clone.PropertyPath = propertyPath;
                    return clone;
                }
            }
            return SeedTypeDefaults(new BindingNode { PropertyPath = propertyPath }, fieldType);
        }

        /// <summary>
        /// Display-type seeding for types whose Default rendering is strictly worse than a specific widget's.
        ///
        /// <para><see cref="SasquatchString"/> exists FOR the Text widget: on Default it draws through its
        /// fallback PropertyDrawer — a plain box with none of the <c>{token}</c> autocomplete, toolbar or
        /// preview — so landing there is a dead end the user has to know to escape. Text is what understands
        /// the type. Rich Text Preview is deliberately NOT turned on here: that stays opt-in per field, the
        /// same as for a plain string.</para>
        ///
        /// <para>Only reached when no preset template covers the field; a preset that DOES cover it already
        /// carries its own display type and must keep winning.</para>
        /// </summary>
        private static BindingNode SeedTypeDefaults(BindingNode node, Type fieldType)
        {
            if (fieldType == typeof(SasquatchString)) node.DisplayType = DisplayType.Text;
            return node;
        }

        /// <summary>
        /// Context-aware overload: for a field being placed inside a collection shown as <paramref name="context"/>
        /// (List / Table), prefer that context's override template; if there is none, fall back to the base preset
        /// (the parameterless-context behavior). A null <paramref name="context"/> means "standalone" → base only.
        /// </summary>
        public static BindingNode MakeConfiguredBinding(string propertyPath, Type fieldType, SasquatchLayoutPreset preset, CollectionFieldContext? context)
        {
            if (preset != null && fieldType != null && context.HasValue)
            {
                var cat = LayoutPresetCategories.Match(fieldType);
                if (cat.HasValue && preset.TryGetContextTemplate(cat.Value, context.Value, out var ctxTemplate))
                {
                    var clone = DeepClone(ctxTemplate);
                    ClearIdentity(clone);
                    clone.PropertyPath = propertyPath;
                    return clone;
                }
            }
            return MakeConfiguredBinding(propertyPath, fieldType, preset);
        }

        /// <summary>The element type of the collection field at <paramref name="collectionPath"/> on
        /// <paramref name="declaringType"/> (List&lt;T&gt;/T[] → T), or null. Used to resolve element-field categories
        /// when re-seeding a collection's context.</summary>
        public static Type ResolveElementType(Type declaringType, string collectionPath)
        {
            if (declaringType == null || string.IsNullOrEmpty(collectionPath)) return null;
            BindingFactory.GetFieldInfoFromPropertyPath(declaringType, collectionPath, out var colType);
            if (colType == null) return null;
            if (colType.IsArray) return colType.GetElementType();
            if (colType.IsGenericType && colType.GetGenericArguments().Length > 0) return colType.GetGenericArguments()[0];
            return null;
        }

        /// <summary>Maps a collection's display mode to the field context it renders per-element templates in, or null
        /// for modes with no per-field template to override (Default / CustomWidget / TagChips).</summary>
        public static CollectionFieldContext? ContextFor(CollectionDisplayMode mode)
        {
            switch (mode)
            {
                case CollectionDisplayMode.List: return CollectionFieldContext.InList;
                case CollectionDisplayMode.Table: return CollectionFieldContext.InTable;
                default: return null;
            }
        }

        /// <summary>
        /// Re-styles a single collection-element leaf when its collection is switched from <paramref name="oldCtx"/>
        /// to <paramref name="newCtx"/>. Returns a config-swapped clone (identity — path/label/header/conditions —
        /// transplanted from <paramref name="leaf"/>) via <paramref name="reskinned"/>, or false and leaves the leaf
        /// alone. The gate: if NEITHER context has an enabled override for the field's category, the type isn't
        /// context-sensitive, so a mode flip must not disturb it (manual edits stay). Otherwise the leaf adopts the
        /// NEW context's template (its override, else the base preset, else a plain Default binding) — so
        /// List→Table takes the Table override and Table→List reverts to base.
        /// </summary>
        public static bool TryReskinForContext(BindingNode leaf, Type fieldType, SasquatchLayoutPreset preset,
            CollectionFieldContext oldCtx, CollectionFieldContext newCtx, out BindingNode reskinned)
        {
            reskinned = null;
            if (leaf == null || preset == null || fieldType == null || oldCtx == newCtx) return false;

            var cat = LayoutPresetCategories.Match(fieldType);
            if (!cat.HasValue) return false;

            bool hasOld = preset.TryGetContextTemplate(cat.Value, oldCtx, out _);
            bool hasNew = preset.TryGetContextTemplate(cat.Value, newCtx, out var newOverride);
            if (!hasOld && !hasNew) return false; // not context-sensitive — never touch it on a mode flip

            // Target config for the new context: its override, else the base preset, else a plain Default binding.
            BindingNode template = hasNew ? newOverride
                : (preset.TryGetTemplate(cat.Value, out var baseTemplate) ? baseTemplate : null);

            reskinned = template != null ? DeepClone(template) : new BindingNode();
            ClearIdentity(reskinned);
            TransplantIdentity(reskinned, leaf);

            // A mode flip is NOT "reset to preset" — the user changed how the collection is laid out, and never
            // asked to lose the unit they typed. Unlike Apply Presets, this path reskins EVERY context-sensitive
            // leaf with no "only if still Default" guard, so without this the affixes on a List's element fields
            // were silently erased the moment it became a Table, with no confirmation and no undo affordance
            // beyond a plain Undo. Carried here and NOT in TransplantIdentity, so the destructive Override
            // action still resets affixes the way its own test requires.
            if (!leaf.PrefixAffix.IsEmpty) reskinned.PrefixAffix = leaf.PrefixAffix;
            if (!leaf.SuffixAffix.IsEmpty) reskinned.SuffixAffix = leaf.SuffixAffix;
            if (!leaf.PrefixAffix.IsEmpty || !leaf.SuffixAffix.IsEmpty)
            {
                reskinned.AffixPerComponent = leaf.AffixPerComponent;
                reskinned.AffixInColumnHeader = leaf.AffixInColumnHeader;
            }
            return true;
        }

        /// <summary>
        /// Re-seeds every element-field leaf of <paramref name="col"/>'s item layout for a List↔Table switch,
        /// swapping each context-sensitive leaf in place (identity preserved). <paramref name="elementType"/> is the
        /// collection's element type (used to resolve each leaf's field category). Returns the number of leaves
        /// changed. No-op when neither context has an override for a leaf's category, so it's silent unless a rule
        /// actually applies. Nested collections' own item layouts are a separate context and are left alone.
        /// </summary>
        public static int ReseedCollectionContext(CollectionNode col, Type elementType, SasquatchLayoutPreset preset,
            CollectionFieldContext oldCtx, CollectionFieldContext newCtx)
        {
            if (col == null || col.ItemLayout == null || preset == null || elementType == null || oldCtx == newCtx)
                return 0;

            // The item layout may itself be a single leaf binding (a one-field or value-column template).
            if (col.ItemLayout is BindingNode rootLeaf)
            {
                if (TryReskinLeafByPath(rootLeaf, elementType, preset, oldCtx, newCtx, out var reskinned))
                {
                    col.ItemLayout = reskinned;
                    return 1;
                }
                return 0;
            }
            return ReskinContainerChildren(col.ItemLayout, elementType, preset, oldCtx, newCtx);
        }

        private static int ReskinContainerChildren(LayoutNode node, Type elementType, SasquatchLayoutPreset preset,
            CollectionFieldContext oldCtx, CollectionFieldContext newCtx)
        {
            int changed = 0;
            if (node is ContainerNode container && container.Children != null)
            {
                for (int i = 0; i < container.Children.Count; i++)
                {
                    var child = container.Children[i];
                    if (child is BindingNode leaf)
                    {
                        if (TryReskinLeafByPath(leaf, elementType, preset, oldCtx, newCtx, out var reskinned))
                        {
                            container.Children[i] = reskinned;
                            changed++;
                        }
                    }
                    else if (!(child is CollectionNode)) // a nested collection owns its own context; don't descend
                    {
                        changed += ReskinContainerChildren(child, elementType, preset, oldCtx, newCtx);
                    }
                }
            }
            return changed;
        }

        private static bool TryReskinLeafByPath(BindingNode leaf, Type elementType, SasquatchLayoutPreset preset,
            CollectionFieldContext oldCtx, CollectionFieldContext newCtx, out BindingNode reskinned)
        {
            reskinned = null;
            // An empty path binds to the element value itself (e.g. a List<string> value widget).
            Type leafType = string.IsNullOrEmpty(leaf.PropertyPath) ? elementType : ResolveLeafFieldType(elementType, leaf.PropertyPath);
            if (leafType == null) return false;
            return TryReskinForContext(leaf, leafType, preset, oldCtx, newCtx, out reskinned);
        }

        private static Type ResolveLeafFieldType(Type elementType, string relativePath)
        {
            if (elementType == null || string.IsNullOrEmpty(relativePath)) return null;
            BindingFactory.GetFieldInfoFromPropertyPath(elementType, relativePath, out var fieldType);
            return fieldType;
        }

        /// <summary>
        /// Upgrades a binding to its preset widget + config, preserving the target's identity. By default only a
        /// still-<see cref="DisplayType.Default"/> binding is touched (so customizations are never lost); pass
        /// <paramref name="force"/> = true for the destructive "Override with Presets" action, which replaces ANY
        /// binding — including one already on a custom widget — with the preset (resetting a customized field to a
        /// Default-template type too). Returns false (and a null out) when the type has no enabled template, or
        /// when applying the template would be a no-op (a Default template onto a non-forced or already-Default
        /// binding).
        /// </summary>
        public static bool TryUpgrade(BindingNode target, Type fieldType, SasquatchLayoutPreset preset, out BindingNode upgraded, bool force = false)
            => TryUpgrade(target, fieldType, preset, null, out upgraded, force);

        /// <summary>
        /// Context-aware upgrade: when <paramref name="context"/> is set (the field is inside a List/Table collection)
        /// and its category has an enabled override for that context, upgrade to the override; otherwise fall back to
        /// the base preset (the parameterless-context behavior). Gating and the Default-template no-op guard are
        /// identical to the base overload.
        /// </summary>
        public static bool TryUpgrade(BindingNode target, Type fieldType, SasquatchLayoutPreset preset, CollectionFieldContext? context, out BindingNode upgraded, bool force = false)
        {
            upgraded = null;
            if (target == null || preset == null || fieldType == null) return false;
            if (!force && target.DisplayType != DisplayType.Default) return false;

            var cat = LayoutPresetCategories.Match(fieldType);
            if (!cat.HasValue) return false;

            BindingNode template;
            if (context.HasValue && preset.TryGetContextTemplate(cat.Value, context.Value, out var ctxTemplate))
                template = ctxTemplate;
            else if (!preset.TryGetTemplate(cat.Value, out template))
                return false;

            // A Default template carries no widget config. Applying it is only meaningful as a forced override of an
            // already-customized binding (resetting it to plain); for a plain Apply, or when the target is already
            // Default, it's a no-op — skip so the "upgraded N fields" count stays meaningful.
            if (template.DisplayType == DisplayType.Default && (!force || target.DisplayType == DisplayType.Default)) return false;

            upgraded = DeepClone(template);
            ClearIdentity(upgraded);
            TransplantIdentity(upgraded, target);
            return true;
        }
    }
}
