using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Single source of truth for which DisplayTypes are OFFERED to users (the designer's Display Type
    /// popup and the inspector quick-edit popup both draw from here) and how each kind is displayed.
    /// A kind orphaned by a field-type change in code (e.g. ColorPalette on a now-Gradient field)
    /// drops out of the offered list so the Display Type popups' self-heal can migrate the node to
    /// Default. (Pre-release Tag/Layer/ValueDropdown/Scene kinds no longer exist — their retired
    /// serialized ints are migrated to DropDown/Default by BindingNode.OnAfterDeserialize.)
    /// </summary>
    public static class DisplayTypeCatalog
    {
        /// <summary>The full curated list, with the node's current kind retained.</summary>
        public static List<DisplayType> AllOffered(DisplayType current)
        {
            var offered = new List<DisplayType>
            {
                DisplayType.Default, DisplayType.DropDown, DisplayType.EnumBadgeGrid,
                DisplayType.AssetPreview,
                DisplayType.InlineEditor, DisplayType.SerializeReference,
#if SASQ_AUDIO
                DisplayType.AudioPreview,
#endif
                DisplayType.Slider, DisplayType.Toggle, DisplayType.Text, DisplayType.MinMaxSlider,
                DisplayType.ProgressBar, DisplayType.ColorPalette, DisplayType.UnityEvent,
#if SASQ_PARTICLES
                DisplayType.ParticleSystemPreview,
#endif
                DisplayType.AnimationCurveEditor,
                DisplayType.Gradient, DisplayType.Spring,
                // Wave 6 widgets appended at the END so existing per-type offered lists keep their interior
                // order (each new kind only appends to the types it applies to — no mid-list index shift).
                DisplayType.Rating, DisplayType.TagChips, DisplayType.XYPad,
                // DisplayType.EulerAngles retired (identical to Default for a Quaternion) — no longer offered;
                // BindingNode.OnAfterDeserialize migrates any stored node to Default.
                DisplayType.VectorSliders,
#if UNITY_6000_6_OR_NEWER
                // Dictionary: only Unity 6.6+ serializes a Dictionary<K, V> at all, so on anything older
                // there is no property to bind and the kind must not be offered. AppliesTo still accepts it
                // unconditionally — see the note there.
                DisplayType.Dictionary,
#endif
#if SASQ_CUSTOM_WIDGET
                // Custom Widget appended at the very END (same index-shift discipline as Wave 6).
                DisplayType.CustomWidget
#endif
            };
            if (!offered.Contains(current)) offered.Insert(0, current);
            return offered;
        }

        /// <summary>
        /// The curated list filtered to kinds that make sense for the bound field's type, for contexts
        /// (like the quick-edit popup) where the type is known. A null <paramref name="fieldType"/> means
        /// "unknown" and returns the unfiltered list WITH the current kind retained — never strip a user's
        /// selection on a bad guess (the type is routinely unresolvable mid-compile). When the type IS
        /// known, the current kind survives only while it still applies: a field whose code type changed
        /// (e.g. Color → Gradient) drops its now-stale kind so the Display Type popup's self-heal can
        /// migrate the node to Default.
        /// </summary>
        public static List<DisplayType> OfferedForField(DisplayType current, Type fieldType)
        {
            var all = AllOffered(current);
            if (fieldType == null) return all;

            // SpringConfig is exclusively the Spring widget — it has no meaningful Unity default, so Default/Text/
            // etc. are never offered (and Spring is never offered for anything else, via AppliesTo).
            if (fieldType == typeof(SasquatchSpring.SpringConfig))
                return new List<DisplayType> { DisplayType.Spring };

            return all.FindAll(k => AppliesTo(k, fieldType));
        }

        /// <summary>
        /// The kinds offered when EVERY selected binding must accept the choice (the multi-edit
        /// panel's shared Display Type popup): the intersection of each field's
        /// <see cref="OfferedForField"/> list, in the first field's order (so Default stays first).
        /// A single-entry result means "nothing meaningful to choose" — callers hide the row.
        /// </summary>
        public static List<DisplayType> OfferedIntersection(IEnumerable<(DisplayType current, Type fieldType)> fields)
        {
            List<DisplayType> result = null;
            foreach (var (current, fieldType) in fields)
            {
                var offered = OfferedForField(current, fieldType);
                if (result == null) result = new List<DisplayType>(offered);
                else result.RemoveAll(k => !offered.Contains(k));
            }
            return result ?? new List<DisplayType>();
        }

        private static bool AppliesTo(DisplayType kind, Type t)
        {
            switch (kind)
            {
                case DisplayType.Default:
                    return true;
                case DisplayType.Dictionary:
                    // Deliberately NOT version-gated, unlike the AllOffered entry. A layout authored on 6.6
                    // and opened on 6.0 still carries the node, and the designer's Display Type row PERMANENTLY
                    // self-heals any non-APPLICABLE kind to the first offered one — so gating this would
                    // silently rewrite the user's choice the moment they opened the layout on an older editor,
                    // and it would not come back when they returned to 6.6. Same trap ParticleSystemPreview's
                    // #else exists for. On an older editor the field simply has no SerializedProperty, so
                    // nothing renders either way.
                    return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>);
                case DisplayType.DropDown:
                    return t.IsEnum || t == typeof(string) || t == typeof(int) || t == typeof(LayerMask);
                case DisplayType.EnumBadgeGrid:
                    // Any enum: [Flags] enums render as a multi-select chip grid, plain enums as a single-select
                    // segmented control (the widget resolves multi/single at build time from the FlagsAttribute).
                    return t.IsEnum;
                case DisplayType.Slider:
                case DisplayType.ProgressBar:
                    return IsSliderNumeric(t);
                case DisplayType.MinMaxSlider:
                    return t == typeof(Vector2) || t == typeof(Vector2Int);
                case DisplayType.Toggle:
                    return t == typeof(bool);
                case DisplayType.Text:
                    // SasquatchString is a string that carries its own token resolution to runtime; the Text
                    // widget binds its inner template, so every Text option (including the Rich Text Preview)
                    // applies to it exactly as it does to a plain string.
                    return t == typeof(string) || t == typeof(SasquatchString);
#if SASQ_AUDIO
                case DisplayType.AudioPreview:
                    return typeof(AudioClip).IsAssignableFrom(t);
                // No #else twin here, unlike ParticleSystemPreview below. That guard exists because a particle
                // node can be bound to a GameObject field, which still resolves with the module gone — so the
                // kind must stay APPLICABLE or the Display Type row's self-heal permanently rewrites the node.
                // AudioPreview only ever applies to an AudioClip field, and AudioClip cannot resolve at all
                // without the module, so the field type comes back null, OfferedForField returns the unfiltered
                // list with the stored kind retained, and the self-heal never fires.
#endif
                case DisplayType.ParticleSystemPreview:
#if SASQ_PARTICLES
                    return typeof(ParticleSystem).IsAssignableFrom(t) || typeof(GameObject).IsAssignableFrom(t)
#if SASQ_VFX
                        || typeof(UnityEngine.VFX.VisualEffect).IsAssignableFrom(t)
#endif
                        ;
#else
                    // com.unity.modules.particlesystem is removed, so the widget is compiled out and AllOffered
                    // no longer offers this kind. It stays APPLICABLE on purpose: a node that already chose it
                    // keeps its choice, because the designer self-heals any non-offered kind to the first
                    // offered one and that write is PERMANENT (SasquatchLayoutDesignerWindow, Display Type row).
                    // The inspector renders the plain default field until the module comes back.
                    return typeof(UnityEngine.Object).IsAssignableFrom(t);
#endif
                case DisplayType.ColorPalette:
                    return t == typeof(Color) || t == typeof(Color32);
                case DisplayType.AnimationCurveEditor:
                    return t == typeof(AnimationCurve);
                case DisplayType.Gradient:
                    return t == typeof(Gradient);
                case DisplayType.Spring:
                    return t == typeof(SasquatchSpring.SpringConfig);
                case DisplayType.UnityEvent:
                    return typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(t);
                case DisplayType.AssetPreview:
                case DisplayType.InlineEditor:
                    return typeof(UnityEngine.Object).IsAssignableFrom(t);
                case DisplayType.SerializeReference:
                    // Managed-reference (polymorphic) fields: the declared field type is an interface or
                    // abstract base. UnityEngine.Object types are excluded — a UnityObject reference is always
                    // a plain object reference, never a [SerializeReference] managed reference, so offering the
                    // polymorphic picker on an abstract UnityObject subclass (a user's abstract ScriptableObject/
                    // MonoBehaviour base, or a built-in like PlayableAsset) would be meaningless. NOTE: the common
                    // built-in "base" object types (Texture, Collider, Renderer, Component, …) are NOT CLR-abstract
                    // despite reading as abstract — they never reached this branch anyway; the guard is for the
                    // genuinely-abstract UnityObject subclasses. (Concrete [SerializeReference] fields still
                    // auto-route at runtime via the SerializedProperty's ManagedReference type, independent of
                    // this offered list.)
                    return (t.IsInterface || t.IsAbstract)
                           && !typeof(UnityEngine.Object).IsAssignableFrom(t);
                case DisplayType.Rating:
                    // A small whole-number score. int only (the common case) to keep the offer list tight.
                    return t == typeof(int);
                case DisplayType.TagChips:
                    // The WHOLE string collection field — a string[] or List<string>.
                    return t == typeof(string[]) || t == typeof(System.Collections.Generic.List<string>);
                case DisplayType.XYPad:
                    return t == typeof(Vector2) || t == typeof(Vector2Int);
                // DisplayType.EulerAngles retired — never offered (migrated to Default on load).
                case DisplayType.VectorSliders:
                    return t == typeof(Vector3) || t == typeof(Vector4);
#if SASQ_CUSTOM_WIDGET
                case DisplayType.CustomWidget:
                    // Deliberately a STATIC primary-kind check (never "does a compatible asset exist") — a
                    // momentarily-empty asset index mid-import must not drop the kind and silently self-heal the
                    // node to Default, destroying the user's widget assignment.
                    return IsSupportedCustomWidgetPrimary(t);
#endif
                default:
                    return false;
            }
        }

#if SASQ_CUSTOM_WIDGET
        /// <summary>The field types a Custom Widget's PRIMARY input can be fed from: slider-numerics, int/enum,
        /// bool, Color/Color32, Vector2/Vector2Int, and float/bool collections. Kept a static type check so the
        /// applicability matrix stays deterministic and the dropdown entry is the composer's discoverability point.</summary>
        internal static bool IsSupportedCustomWidgetPrimary(Type t)
        {
            if (t == null) return false;
            if (IsSliderNumeric(t)) return true;             // float/int/double/long/… (int included)
            if (t.IsEnum) return true;                        // enums (incl. [Flags]) → Int input / InputBit
            if (t == typeof(bool)) return true;
            if (t == typeof(Color) || t == typeof(Color32)) return true;
            if (t == typeof(Vector2) || t == typeof(Vector2Int)) return true;
            if (t == typeof(float[]) || t == typeof(System.Collections.Generic.List<float>)) return true;
            if (t == typeof(bool[]) || t == typeof(System.Collections.Generic.List<bool>)) return true;
            return false;
        }
#endif

        // The numeric C# types the Slider/ProgressBar (and Stepper) widgets accept. int/float/double are the
        // historical set; the integral widths (long/uint/short/ushort/byte/sbyte/ulong) were added so every
        // whole-number field can drive a bar. Reads/writes route through BindingFactory.ReadNumeric/WriteNumeric
        // so 64-bit (long/ulong) and double edit at native precision. NOTE: char is intentionally excluded.
        private static readonly HashSet<Type> SliderNumericTypes = new HashSet<Type>
        {
            typeof(float), typeof(int), typeof(double), typeof(long), typeof(uint),
            typeof(short), typeof(ushort), typeof(byte), typeof(sbyte), typeof(ulong)
        };

        /// <summary>True for the numeric field types the Slider/ProgressBar (and Stepper) widgets accept.</summary>
        internal static bool IsSliderNumeric(Type t) => t != null && SliderNumericTypes.Contains(t);

        // Widgets that build no labelled base-field row of their own — a card, a graph well, an event list —
        // so there is nothing for "Label Above Field" to stack. MEASURED, not assumed: with the flag on, each
        // of these renders a byte-identical element tree, and TestLabelAboveCostsOneLineOnEveryDisplayType
        // re-derives this exact set from live geometry, so the list cannot quietly drift from the truth.
        //
        // In-Line Editor is here for a sharper reason than "nothing happens": its row DOES carry a stock
        // label, so the old flag took it out of the shared label column (BindingFactory skips
        // FieldLabelColumn.AttachToRow for a label-above row) — and then never stacked it, because the row
        // it needs to restyle is built by the object field underneath. Measured on a three-row layout, its
        // input started at x=138 while its neighbours' started at 161. The toggle was pure damage there.
        private static readonly HashSet<DisplayType> LabelAboveInert = new HashSet<DisplayType>
        {
            DisplayType.InlineEditor,
            DisplayType.UnityEvent,
            DisplayType.AnimationCurveEditor,
            DisplayType.Gradient,
            // Dictionary Table draws its own titled frame with the field name in the header, exactly like the
            // SerializedDictionary drawer does. "Label above" would stack a SECOND copy of that name above the
            // frame, which is the same reason InlineEditor and UnityEvent are here.
            DisplayType.Dictionary,
        };

        /// <summary>
        /// True when "Label Above Field" actually does something for this Display Type. Gate the RENDERER on
        /// it as well as the UI: a layout authored before the option was hidden still carries the flag, and a
        /// renderer that honours it would keep that row out of the label column for no benefit.
        /// </summary>
        public static bool SupportsLabelAbove(DisplayType kind) => !LabelAboveInert.Contains(kind);

        /// <summary>
        /// The field-type-aware overload, and the one every real call site should use. <c>LabelAboveInert</c>
        /// can only speak about the DISPLAY TYPE, and the dictionary table has TWO routes to it — the explicit
        /// <see cref="DisplayType.Dictionary"/>, and a <c>SerializedDictionary</c> field that keeps
        /// <see cref="DisplayType.Default"/> and is auto-routed by <c>BindingFactory</c>. Keying on the kind
        /// alone therefore hid the toggle for one and offered it — and honoured it — for the other, so two
        /// fields rendering byte-identical titled tables disagreed about which options exist, and the
        /// SerializedDictionary drew its name twice: once stacked above, once in the table's own header.
        /// <para>
        /// Same predicate the Dictionary Style / Row Frame controls are gated on
        /// (<see cref="ShowsDictionaryTable"/>), so the two cannot drift.
        /// </para>
        /// </summary>
        public static bool SupportsLabelAbove(DisplayType kind, Type fieldType)
            => !LabelAboveInert.Contains(kind) && !ShowsDictionaryTable(kind, fieldType);

        /// <summary>
        /// True when this binding renders the Sasquatch dictionary table, and therefore when the Dictionary
        /// Style / Row Frame controls have anything to act on. TWO routes reach that table: the explicit
        /// <see cref="DisplayType.Dictionary"/> over a native Unity 6.6 <c>Dictionary&lt;K,V&gt;</c>, and a
        /// field whose type is this package own <c>SerializedDictionary</c>, which BindingFactory auto-routes
        /// there whatever the display type says (the SpringConfig precedent).
        /// <para>
        /// ONE predicate on purpose. The designer Properties panel, the quick-edit popup and the live builder
        /// each decide independently whether to offer these controls, and every disagreement is a bug in one
        /// of two directions - a control that does nothing, or a value only one renderer honours. The export
        /// emitter is deliberately NOT a caller: it has no dictionary section at all and falls through to a
        /// PropertyField, which it already reports through DegradeNote.
        /// </para>
        /// </summary>
        public static bool ShowsDictionaryTable(DisplayType kind, Type fieldType)
        {
            if (kind == DisplayType.Dictionary) return true;
            return fieldType != null && typeof(SerializedDictionaryBase).IsAssignableFrom(fieldType);
        }

        /// <summary>
        /// True when this binding not only shows the dictionary table but has RESOLVABLE halves — i.e. the
        /// field's C# type really is one of the two dictionary shapes, so a Key / Value cell template has a
        /// type to be styled against.
        /// <para>
        /// The strict sibling of <see cref="ShowsDictionaryTable"/>, and the difference is deliberate.
        /// <c>ShowsDictionaryTable</c> answers "offer the Style / Row Frame rows", and takes
        /// <see cref="DisplayType.Dictionary"/> at its word even on a field whose type has since changed in
        /// code — an inert control on a broken binding is harmless. A cell template is NOT harmless there:
        /// with no key/value type there is nothing to filter the Display Type list against and nothing for
        /// the exporter to resolve, so this one insists on the type.
        /// </para>
        /// <para>
        /// ONE predicate, asked by the designer hierarchy's +KV button, <c>LayoutFieldTypeResolver</c> and
        /// the tests — see control-reach-is-one-predicate-every-surface-asks. Ungated by editor version on
        /// purpose: reflection sees a <c>Dictionary&lt;,&gt;</c> field on every Unity version (only the
        /// SERIALIZER is 6.6-only), so a layout authored on 6.6 keeps its templates visible and editable on
        /// the 6000.0 floor instead of having the designer quietly drop them.
        /// </para>
        /// </summary>
        public static bool SupportsDictionaryCellTemplates(DisplayType kind, Type fieldType)
        {
            if (fieldType == null) return false;
            if (typeof(SerializedDictionaryBase).IsAssignableFrom(fieldType)) return true;
            return kind == DisplayType.Dictionary
                   && fieldType.IsGenericType
                   && fieldType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.Dictionary<,>);
        }

        /// <summary>The kinds <see cref="SupportsLabelAbove"/> rejects — for the test that re-derives them.</summary>
        internal static IEnumerable<DisplayType> LabelAboveInertKinds => LabelAboveInert;

        /// <summary>Friendly display label for a kind, as shown in the Display Type popups.</summary>
        public static string Format(DisplayType k)
        {
            switch (k)
            {
                case DisplayType.Default: return "Default";
                case DisplayType.DropDown: return "Dropdown";
                case DisplayType.EnumBadgeGrid: return "Enum Badges";
                case DisplayType.AssetPreview: return "Asset Preview";
                case DisplayType.InlineEditor: return "In-Line Editor";
                case DisplayType.SerializeReference: return "Polymorphic";
                case DisplayType.AudioPreview: return "Audio Preview";
#if SASQ_VFX
                // No '/' — the widget-kind popup is path-based and would split "Particle / VFX" into a submenu.
                case DisplayType.ParticleSystemPreview: return "Particle & VFX Preview";
#else
                case DisplayType.ParticleSystemPreview: return "Particle System Preview";
#endif
                case DisplayType.Slider: return "Slider";
                case DisplayType.Toggle: return "Toggle";
                case DisplayType.Text: return "Text";
                case DisplayType.MinMaxSlider: return "Min-Max Slider";
                case DisplayType.ProgressBar: return "Progress Bar";
                case DisplayType.ColorPalette: return "Color Palette";
                case DisplayType.AnimationCurveEditor: return "Animation Curve";
                case DisplayType.Gradient: return "Gradient";
                case DisplayType.Spring: return "Spring";
                case DisplayType.UnityEvent: return "Unity Event";
                case DisplayType.Rating: return "Rating";
                case DisplayType.TagChips: return "Tag Chips";
                case DisplayType.XYPad: return "XY Pad";
                case DisplayType.EulerAngles: return "Euler Angles";
                case DisplayType.VectorSliders: return "Vector Sliders";
                // "Table", not just "Dictionary": the field is ALREADY a dictionary under Default, so the
                // choice being offered is the table, not the data structure.
                case DisplayType.Dictionary: return "Dictionary Table";
#if SASQ_CUSTOM_WIDGET
                case DisplayType.CustomWidget: return "Custom Widget";
#endif
                default: return ObjectNames.NicifyVariableName(k.ToString());
            }
        }
    }
}
