using System;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// One inspector-displayable C# type category that a <see cref="SasquatchLayoutPreset"/> can carry a default
    /// widget for. Value types match exactly; reference categories match most-specific-first (see
    /// LayoutPresetCategories.Match in the editor assembly). Append-only — serialized as int on
    /// <see cref="LayoutPresetEntry"/>, so never reorder or remove members.
    /// </summary>
    public enum PresetCategory
    {
        String,
        Int,
        Float,
        Double,
        Bool,
        Enum,
        FlagsEnum,
        Color,
        Vector2,
        Vector3,
        Vector4,
        LayerMask,
        AnimationCurve,
        Gradient,
        AudioClip,
        Texture,
        Sprite,
        Mesh,
        Material,
        GameObject,
        ParticleSystem,
        UnityEvent,
        ManagedReference,
        ObjectReference,
        // APPENDED 2026-09-02 — value 24, the 25th member. Append-only: this enum is serialized as an int on
        // every PresetEntry, so inserting anywhere but the END would silently repoint an existing tester's
        // saved preset rows at a different category. Covers BOTH dictionary shapes — this package's
        // SerializedDictionary on every editor, and Unity 6.6's native Dictionary<K,V> — because they render
        // through one table and a buyer setting a house style means it for "my dictionaries", not for one
        // of the two spellings.
        Dictionary
    }

    /// <summary>
    /// One palette-widget kind that a <see cref="SasquatchLayoutPreset"/> can carry a default STYLING template for,
    /// applied when the widget is dropped onto a designer canvas so it arrives pre-styled. Append-only — serialized
    /// as int on <see cref="WidgetPresetEntry"/>, so never reorder or remove members. Container kinds map 1:1 to the
    /// palette's container items; Embedded Editor and C# Property are intentionally absent (nothing to style).
    /// </summary>
    public enum WidgetPresetKind
    {
        Button,
        TextBlock,
        ImageBanner,
        HelpBox,
        LabeledDivider,
        Space,
        ScriptHeader,
        ContainerRows,
        ContainerColumns,
        ContainerGrid,
        ContainerCard,
        ContainerFoldout,
        ContainerTabs,
        ContainerFrame
    }

    /// <summary>
    /// The collection context a field can carry a distinct preset for, so "how a field shows up" can differ when it
    /// lives inside a collection element vs standalone (e.g. String Resizable everywhere but Fixed inside a Table).
    /// Maps 1:1 to the two Sasquatch-styled <see cref="CollectionDisplayMode"/>s that render per-field element
    /// templates (List / Table); the other display modes have no per-field template to override. Append-only —
    /// serialized as int on <see cref="ContextPresetEntry"/>, so never reorder or remove members.
    /// </summary>
    public enum CollectionFieldContext
    {
        InList,
        InTable
    }

    /// <summary>
    /// One preset row: the default widget + full configuration for a <see cref="PresetCategory"/>, stored as a
    /// type-generic template <see cref="BindingNode"/> (its identity fields — property path, label, header,
    /// conditions — are cleared so only widget config travels). <c>enabled == false</c> means "no preset for this
    /// category; import the plain default field".
    /// </summary>
    [Serializable]
    public class LayoutPresetEntry
    {
        public PresetCategory category;
        public bool enabled = true;
        [SerializeReference] public BindingNode template;
    }

    /// <summary>
    /// One collection-context override row: for a field of <see cref="category"/> rendered inside a collection shown
    /// as <see cref="context"/> (List / Table), use this template's widget + config instead of the base
    /// <see cref="LayoutPresetEntry"/>. Stored as a type-generic template <see cref="BindingNode"/> exactly like a
    /// base entry (identity fields cleared). <c>enabled == false</c> (or no entry) means "inherit the base preset in
    /// this context". Empty by default — collections behave exactly as before until an override is added.
    /// </summary>
    [Serializable]
    public class ContextPresetEntry
    {
        public PresetCategory category;
        public CollectionFieldContext context;
        public bool enabled = true;
        [SerializeReference] public BindingNode template;
    }

    /// <summary>
    /// One widget preset row: the default STYLING for a <see cref="WidgetPresetKind"/>, stored on a detached
    /// template <see cref="LayoutNode"/> that carries only styling (colors, sizes, box model) — content/identity
    /// (text, labels, icons, bound methods) is never captured. <c>enabled == false</c> (or no entry) means "drop
    /// this widget at its plain built-in default".
    /// </summary>
    [Serializable]
    public class WidgetPresetEntry
    {
        public WidgetPresetKind kind;
        public bool enabled = true;
        [SerializeReference] public LayoutNode template;
    }

    /// <summary>
    /// Project-wide map of "for a field of type X, default to widget Y configured like Z", applied when importing a
    /// script's fields into a layout so the powerful custom widgets (rich text, gradient, curve, dropdown, asset
    /// preview…) are used automatically instead of everything landing on the plain Unity field. Recommended
    /// defaults ship in code (LayoutPresetDefaults); a dev can override any category from a real field via the
    /// designer's "Set as default for all {type} fields" action. Assigned in Project Settings → Sasquatch Inspector.
    /// </summary>
    public class SasquatchLayoutPreset : ScriptableObject
    {
        [SerializeField] private List<LayoutPresetEntry> entries = new List<LayoutPresetEntry>();

        // Structural import defaults (appended — old assets deserialize to these initializers, no migration).
        // When a script's [Header] starts a section on import, the section container takes this type; the layout
        // ROOT is kept separate because it's usually wanted flat. Read independently of the per-user "Use Layout
        // Presets" widget toggle (these shape STRUCTURE, not per-field widgets).
        [SerializeField] private ContainerType defaultHeaderContainerType = ContainerType.Card;
        [SerializeField] private ContainerType defaultRootContainerType = ContainerType.Rows;

        // Per-palette-widget default STYLING, applied when a widget is dropped onto a designer canvas. Separate from
        // `entries` (which is per field-TYPE and applies on import). Empty until a user customizes a widget in the
        // Presets editor — so widget drops behave exactly as before until the feature is opted into.
        [SerializeField] private List<WidgetPresetEntry> widgetEntries = new List<WidgetPresetEntry>();

        // Per field-TYPE, per collection-CONTEXT (List / Table) overrides of the base `entries` template, so a field
        // can be styled differently inside a collection element than standalone. Applied when a collection's item
        // template is built and re-applied when its Display Type is switched (identity preserved, undoable). Appended
        // — old assets deserialize to an empty list, so collections behave exactly as before until an override exists.
        [SerializeField] private List<ContextPresetEntry> contextEntries = new List<ContextPresetEntry>();

        /// <summary>The per-category entries. One per <see cref="PresetCategory"/> when seeded with defaults.</summary>
        public List<LayoutPresetEntry> Entries
        {
            get => entries;
            set => entries = value;
        }

        /// <summary>The container type a script <c>[Header]</c> section is wrapped in on import (default: Card).</summary>
        public ContainerType DefaultHeaderContainerType
        {
            get => defaultHeaderContainerType;
            set => defaultHeaderContainerType = value;
        }

        /// <summary>The container type a freshly-created layout ROOT uses (kept separate from header sections;
        /// default: Rows — a flat top-level stack).</summary>
        public ContainerType DefaultRootContainerType
        {
            get => defaultRootContainerType;
            set => defaultRootContainerType = value;
        }

        /// <summary>The entry for <paramref name="category"/>, or null if none exists yet.</summary>
        public LayoutPresetEntry Find(PresetCategory category)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].category == category) return entries[i];
            return null;
        }

        /// <summary>The configured template for <paramref name="category"/> — only when the entry is enabled and has one.</summary>
        public bool TryGetTemplate(PresetCategory category, out BindingNode template)
        {
            var entry = Find(category);
            if (entry != null && entry.enabled && entry.template != null)
            {
                template = entry.template;
                return true;
            }
            template = null;
            return false;
        }

        /// <summary>The per-widget styling entries. Empty until a widget is customized in the Presets editor.</summary>
        public List<WidgetPresetEntry> WidgetEntries
        {
            get => widgetEntries;
            set => widgetEntries = value;
        }

        /// <summary>The widget entry for <paramref name="kind"/>, or null if none exists yet.</summary>
        public WidgetPresetEntry FindWidget(WidgetPresetKind kind)
        {
            for (int i = 0; i < widgetEntries.Count; i++)
                if (widgetEntries[i] != null && widgetEntries[i].kind == kind) return widgetEntries[i];
            return null;
        }

        /// <summary>The styling template for <paramref name="kind"/> — only when the entry is enabled and has one.</summary>
        public bool TryGetWidgetTemplate(WidgetPresetKind kind, out LayoutNode template)
        {
            var entry = FindWidget(kind);
            if (entry != null && entry.enabled && entry.template != null)
            {
                template = entry.template;
                return true;
            }
            template = null;
            return false;
        }

        /// <summary>The per-category, per-collection-context override entries. Empty until an override is added.</summary>
        public List<ContextPresetEntry> ContextEntries
        {
            get => contextEntries;
            set => contextEntries = value;
        }

        /// <summary>The override entry for (<paramref name="category"/>, <paramref name="context"/>), or null if none exists yet.</summary>
        public ContextPresetEntry FindContext(PresetCategory category, CollectionFieldContext context)
        {
            for (int i = 0; i < contextEntries.Count; i++)
                if (contextEntries[i] != null && contextEntries[i].category == category && contextEntries[i].context == context)
                    return contextEntries[i];
            return null;
        }

        /// <summary>The override template for (<paramref name="category"/>, <paramref name="context"/>) — only when the
        /// entry is enabled and has one. A disabled or missing override means "inherit the base preset in this context".</summary>
        public bool TryGetContextTemplate(PresetCategory category, CollectionFieldContext context, out BindingNode template)
        {
            var entry = FindContext(category, context);
            if (entry != null && entry.enabled && entry.template != null)
            {
                template = entry.template;
                return true;
            }
            template = null;
            return false;
        }
    }
}
