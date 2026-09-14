using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The wire format for the shipped preset defaults in <see cref="LayoutPresetBakedDefaults"/>.
    ///
    /// A recommended default is a whole configured node, not a handful of enum values, so it can't be expressed
    /// as a readable C# literal without a per-field copy that drifts the moment a widget option is added. It is
    /// instead stored the same way a compressed layout asset stores its graph — GZip'd
    /// <c>EditorJsonUtility</c> JSON, base64 — which round-trips EVERY serialized field (including
    /// <c>displayStyleData</c> and the text-style block) with zero per-field maintenance.
    ///
    /// <see cref="Decode"/> is what reads the baked defaults; <see cref="Encode"/> is its counterpart, so a
    /// payload can be regenerated from a real, visually-tuned preset asset rather than hand-authored.
    /// </summary>
    public static class LayoutPresetBakeCodec
    {
        /// <summary>Packs a template node into the payload format (null in ⇒ null out).</summary>
        public static string Encode(LayoutNode template) => LayoutGraphStorage.Compress(template);

        /// <summary>Unpacks a payload back into a fresh, independent node. Null on an empty or corrupt payload.</summary>
        public static LayoutNode Decode(string payload) => LayoutGraphStorage.Decompress(payload);

        /// <summary>Unpacks a payload known to hold a field-type template. Null if it isn't a binding.</summary>
        public static BindingNode DecodeBinding(string payload) => Decode(payload) as BindingNode;
    }

    /// <summary>One baked per-field-type default: the template for a <see cref="PresetCategory"/>.</summary>
    internal readonly struct BakedTypeRow
    {
        public readonly PresetCategory Category;
        public readonly bool Enabled;
        public readonly string Payload;

        public BakedTypeRow(PresetCategory category, bool enabled, string payload)
        {
            Category = category;
            Enabled = enabled;
            Payload = payload;
        }
    }

    /// <summary>One baked collection-context override: how a <see cref="PresetCategory"/> looks inside a
    /// List / Table element, when that should differ from the base template.</summary>
    internal readonly struct BakedContextRow
    {
        public readonly PresetCategory Category;
        public readonly CollectionFieldContext Context;
        public readonly bool Enabled;
        public readonly string Payload;

        public BakedContextRow(PresetCategory category, CollectionFieldContext context, bool enabled, string payload)
        {
            Category = category;
            Context = context;
            Enabled = enabled;
            Payload = payload;
        }
    }

    /// <summary>One baked palette-widget styling default, stamped onto the widget when it is dropped on a canvas.</summary>
    internal readonly struct BakedWidgetRow
    {
        public readonly WidgetPresetKind Kind;
        public readonly bool Enabled;
        public readonly string Payload;

        public BakedWidgetRow(WidgetPresetKind kind, bool enabled, string payload)
        {
            Kind = kind;
            Enabled = enabled;
            Payload = payload;
        }
    }
}
