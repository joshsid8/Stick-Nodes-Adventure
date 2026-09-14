using System;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Maps a bound field's <see cref="Type"/> to a <see cref="PresetCategory"/> and back to a representative
    /// concrete type (used to filter the widget dropdown and resolve preset config). Pure — no Unity object state.
    /// The match order mirrors <c>DisplayTypeCatalog.AppliesTo</c>: exact value types first, reference hierarchies
    /// most-specific-first, managed references (non-UnityObject interface/abstract) before the generic Object
    /// fallback.
    /// </summary>
    public static class LayoutPresetCategories
    {
        // Sample types so the enum / managed-reference categories have a concrete System.Type to feed
        // DisplayTypeCatalog.OfferedForField (which keys off IsEnum / IsInterface / == checks).
        private enum SamplePlainEnum { A }
        [Flags] private enum SampleFlagsEnum { None = 0, A = 1 }
        private interface ISampleManagedRef { }

        /// <summary>True for a <c>[Flags]</c>-attributed enum type (its preset category is <see cref="PresetCategory.FlagsEnum"/>).</summary>
        public static bool IsFlags(Type t) =>
            t != null && t.IsEnum && t.IsDefined(typeof(FlagsAttribute), false);

        /// <summary>Convenience overload that detects <c>[Flags]</c> itself.</summary>
        public static PresetCategory? Match(Type t) => Match(t, IsFlags(t));

        /// <summary>
        /// The preset category for a bound field's declared type, or null when no category applies (e.g. a plain
        /// serializable struct/class).
        /// </summary>
        public static PresetCategory? Match(Type t, bool isFlags)
        {
            if (t == null) return null;

            // Value / primitive types — exact match.
            if (t == typeof(bool)) return PresetCategory.Bool;
            if (t == typeof(int)) return PresetCategory.Int;
            if (t == typeof(float)) return PresetCategory.Float;
            if (t == typeof(double)) return PresetCategory.Double;
            // A SasquatchString is a string that carries its {token} resolution to runtime, and it renders
            // through the very same Text widget — so it takes the String preset rather than a category of its
            // own. Deliberately NOT a new PresetCategory member: the baked preset asset stores one entry per
            // member and TestBakedPresetDefaults asserts that count, so a new member would mean re-baking the
            // shipped asset to buy nothing.
            if (t == typeof(string) || t == typeof(SasquatchString)) return PresetCategory.String;
            if (t == typeof(Color) || t == typeof(Color32)) return PresetCategory.Color;
            if (t == typeof(Vector2)) return PresetCategory.Vector2;
            if (t == typeof(Vector3)) return PresetCategory.Vector3;
            if (t == typeof(Vector4)) return PresetCategory.Vector4;
            if (t == typeof(LayerMask)) return PresetCategory.LayerMask;

            if (t.IsEnum) return isFlags ? PresetCategory.FlagsEnum : PresetCategory.Enum;

            if (t == typeof(AnimationCurve)) return PresetCategory.AnimationCurve;
            if (t == typeof(Gradient)) return PresetCategory.Gradient;

            // BOTH dictionary shapes, one category. This package's SerializedDictionary (every editor) is a
            // class hierarchy, so IsAssignableFrom; Unity 6.6's native Dictionary<K,V> is an open generic, so
            // the definition test. Deliberately BEFORE the managed-reference arm below: SerializedDictionaryBase
            // is an abstract non-UnityObject class, so that arm would otherwise swallow it and file every
            // dictionary under Managed Reference.
            if (typeof(SerializedDictionaryBase).IsAssignableFrom(t)) return PresetCategory.Dictionary;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.Dictionary<,>))
                return PresetCategory.Dictionary;

            // Reference hierarchies — specific before generic.
#if SASQ_AUDIO
            if (typeof(AudioClip).IsAssignableFrom(t)) return PresetCategory.AudioClip;
#endif
#if SASQ_PARTICLES
            if (typeof(ParticleSystem).IsAssignableFrom(t)) return PresetCategory.ParticleSystem; // before GameObject
#endif
            if (typeof(Texture).IsAssignableFrom(t)) return PresetCategory.Texture;
            if (typeof(Sprite).IsAssignableFrom(t)) return PresetCategory.Sprite;
            if (typeof(Mesh).IsAssignableFrom(t)) return PresetCategory.Mesh;
            if (typeof(Material).IsAssignableFrom(t)) return PresetCategory.Material;
            if (typeof(GameObject).IsAssignableFrom(t)) return PresetCategory.GameObject;
            if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(t)) return PresetCategory.UnityEvent;

            // Managed reference ([SerializeReference]): a non-UnityObject interface or abstract base.
            if ((t.IsInterface || t.IsAbstract) && !typeof(UnityEngine.Object).IsAssignableFrom(t))
                return PresetCategory.ManagedReference;

            // Any other UnityEngine.Object reference (concrete component, prefab, asset…).
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return PresetCategory.ObjectReference;

            return null;
        }

        /// <summary>A concrete type representative of the category — drives the offered-widget filter + config resolution.</summary>
        public static Type RepresentativeType(PresetCategory c)
        {
            switch (c)
            {
                case PresetCategory.String: return typeof(string);
                case PresetCategory.Int: return typeof(int);
                case PresetCategory.Float: return typeof(float);
                case PresetCategory.Double: return typeof(double);
                case PresetCategory.Bool: return typeof(bool);
                case PresetCategory.Enum: return typeof(SamplePlainEnum);
                case PresetCategory.FlagsEnum: return typeof(SampleFlagsEnum);
                case PresetCategory.Color: return typeof(Color);
                case PresetCategory.Vector2: return typeof(Vector2);
                case PresetCategory.Vector3: return typeof(Vector3);
                case PresetCategory.Vector4: return typeof(Vector4);
                case PresetCategory.LayerMask: return typeof(LayerMask);
                case PresetCategory.AnimationCurve: return typeof(AnimationCurve);
                case PresetCategory.Gradient: return typeof(Gradient);
#if SASQ_AUDIO
                case PresetCategory.AudioClip: return typeof(AudioClip);
#endif
                case PresetCategory.Texture: return typeof(Texture2D);
                case PresetCategory.Sprite: return typeof(Sprite);
                case PresetCategory.Mesh: return typeof(Mesh);
                case PresetCategory.Material: return typeof(Material);
                case PresetCategory.GameObject: return typeof(GameObject);
#if SASQ_PARTICLES
                case PresetCategory.ParticleSystem: return typeof(ParticleSystem);
#endif
                case PresetCategory.UnityEvent: return typeof(UnityEngine.Events.UnityEvent);
                case PresetCategory.ManagedReference: return typeof(ISampleManagedRef);
                case PresetCategory.ObjectReference: return typeof(UnityEngine.Object);
                // A REAL closed generic, not a private sample type. TestPresetCategoryTagsNeverShowSampleTypes
                // reflects over this class's private nested samples and fails any category whose [type] chip
                // names one — and "SerializedDictionary<string, int>" is also simply the truer label.
                case PresetCategory.Dictionary: return typeof(SerializedDictionary<string, int>);
                default: return null;
            }
        }

        /// <summary>
        /// The short type tag shown beside a category's name in the Preset Editor (the gold <c>[type]</c> chip).
        /// Mostly just the representative type's friendly name — but three categories are represented by PRIVATE
        /// sample types that exist only to feed the widget filter (<see cref="RepresentativeType"/>), and their
        /// names would read as noise next to two dozen genuine Unity types. Those three get spelled out instead:
        /// both enum flavours as the C# keyword (matching <c>[int]</c> / <c>[bool]</c> / <c>[string]</c>), and the
        /// managed-reference row as the attribute that actually creates such a field.
        /// </summary>
        public static string TypeTag(PresetCategory c)
        {
            switch (c)
            {
                case PresetCategory.Enum:
                case PresetCategory.FlagsEnum: return "enum"; // the two rows' NAMES already say which flavour
                case PresetCategory.ManagedReference: return "SerializeReference";
                // The representative type's friendly name is "SerializedDictionary<string, int>", which is
                // accurate but far too long for the gold chip beside the row name, and it names ONE closed
                // generic where the category covers every K and V (and Unity 6.6's native Dictionary too).
                case PresetCategory.Dictionary: return "Dictionary<K, V>";
                default:
                    var rep = RepresentativeType(c);
                    return rep != null ? SasquatchReflectionUtility.GetFriendlyTypeName(rep) : null;
            }
        }

        /// <summary>Friendly label for a category — used in the manager inspector and the menu items.</summary>
        public static string DisplayName(PresetCategory c)
        {
            switch (c)
            {
                case PresetCategory.FlagsEnum: return "Enum [Flags]";
                case PresetCategory.AnimationCurve: return "Animation Curve";
                case PresetCategory.AudioClip: return "Audio Clip";
                case PresetCategory.GameObject: return "Game Object";
                case PresetCategory.ParticleSystem: return "Particle System";
                case PresetCategory.UnityEvent: return "Unity Event";
                case PresetCategory.ManagedReference: return "Managed Reference";
                case PresetCategory.ObjectReference: return "Object Reference";
                case PresetCategory.LayerMask: return "Layer Mask";
                default: return c.ToString();
            }
        }
    }
}
