using System;
using UnityEngine;
using UnityEngine.Events;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// A throwaway ScriptableObject with exactly one serialized field per <see cref="PresetCategory"/>. The Preset
    /// Editor uses an instance of this as the synthetic layout's target so the designer's existing field-type
    /// resolution and live preview work unchanged: a preset template binding points at the matching field
    /// (<see cref="FieldFor"/>), and the field's REAL declared type drives the widget options + the rendered preview.
    /// Editor-only (never shipped in player builds).
    /// </summary>
    public class PresetSampleObject : ScriptableObject
    {
        // Sample enum types so the Enum / FlagsEnum categories resolve to real enum fields.
        public enum SampleEnum { Alpha, Beta, Gamma, Delta }
        [Flags] public enum SampleFlags { None = 0, Read = 1, Write = 2, Execute = 4 }

        // Managed-reference sample so the ManagedReference category resolves to a [SerializeReference] interface field.
        public interface ISampleRef { }
        [Serializable] public class SampleRefImpl : ISampleRef { public int amount = 1; public string label = "Sample"; }

        [SerializeField] private string sampleString;
        [SerializeField] private int sampleInt;
        [SerializeField] private float sampleFloat;
        [SerializeField] private double sampleDouble;
        [SerializeField] private bool sampleBool;
        [SerializeField] private SampleEnum sampleEnum;
        [SerializeField] private SampleFlags sampleFlags;
        [SerializeField] private Color sampleColor = Color.white;
        [SerializeField] private Vector2 sampleVector2;
        [SerializeField] private Vector3 sampleVector3;
        [SerializeField] private Vector4 sampleVector4;
        [SerializeField] private LayerMask sampleLayerMask;
        [SerializeField] private AnimationCurve sampleCurve;
        [SerializeField] private Gradient sampleGradient;
        // The BONUS type, not a native Dictionary<K,V>: it works on every supported editor and reaches the
        // very same table, so the Preset Editor's preview renders identically on the 6000.0 floor and on 6.6.
        // A native Dictionary here would draw the "Unity can't serialize this" card on anything before 6.6.
        [SerializeField] private SerializedDictionary<string, int> sampleDictionary =
            new SerializedDictionary<string, int>();
#if SASQ_AUDIO
        [SerializeField] private AudioClip sampleAudio;
#endif
        [SerializeField] private Texture2D sampleTexture;
        [SerializeField] private Sprite sampleSprite;
        [SerializeField] private Mesh sampleMesh;
        [SerializeField] private Material sampleMaterial;
        [SerializeField] private GameObject sampleGameObject;
#if SASQ_PARTICLES
        [SerializeField] private ParticleSystem sampleParticles;
#endif
        [SerializeField] private UnityEvent sampleEvent;
        [SerializeReference] private ISampleRef sampleManagedRef;
        [SerializeField] private UnityEngine.Object sampleObject;

        /// <summary>The serialized field name (= property path) representing <paramref name="category"/>.</summary>
        public static string FieldFor(PresetCategory category)
        {
            switch (category)
            {
                case PresetCategory.Dictionary: return nameof(sampleDictionary);
                case PresetCategory.String: return nameof(sampleString);
                case PresetCategory.Int: return nameof(sampleInt);
                case PresetCategory.Float: return nameof(sampleFloat);
                case PresetCategory.Double: return nameof(sampleDouble);
                case PresetCategory.Bool: return nameof(sampleBool);
                case PresetCategory.Enum: return nameof(sampleEnum);
                case PresetCategory.FlagsEnum: return nameof(sampleFlags);
                case PresetCategory.Color: return nameof(sampleColor);
                case PresetCategory.Vector2: return nameof(sampleVector2);
                case PresetCategory.Vector3: return nameof(sampleVector3);
                case PresetCategory.Vector4: return nameof(sampleVector4);
                case PresetCategory.LayerMask: return nameof(sampleLayerMask);
                case PresetCategory.AnimationCurve: return nameof(sampleCurve);
                case PresetCategory.Gradient: return nameof(sampleGradient);
#if SASQ_AUDIO
                case PresetCategory.AudioClip: return nameof(sampleAudio);
#endif
                case PresetCategory.Texture: return nameof(sampleTexture);
                case PresetCategory.Sprite: return nameof(sampleSprite);
                case PresetCategory.Mesh: return nameof(sampleMesh);
                case PresetCategory.Material: return nameof(sampleMaterial);
                case PresetCategory.GameObject: return nameof(sampleGameObject);
#if SASQ_PARTICLES
                case PresetCategory.ParticleSystem: return nameof(sampleParticles);
#endif
                case PresetCategory.UnityEvent: return nameof(sampleEvent);
                case PresetCategory.ManagedReference: return nameof(sampleManagedRef);
                case PresetCategory.ObjectReference: return nameof(sampleObject);
                default: return null;
            }
        }

        /// <summary>Fills the value fields with representative defaults so the live preview looks alive (asset
        /// references stay null — their widgets render an empty state, which is fine for configuring knobs).</summary>
        public static void SeedSampleValues(PresetSampleObject o)
        {
            if (o == null) return;
            o.sampleString = "Sample text";
            o.sampleInt = 42;
            o.sampleFloat = 0.5f;
            o.sampleDouble = 0.5;
            o.sampleBool = true;
            o.sampleEnum = SampleEnum.Beta;
            o.sampleFlags = SampleFlags.Read | SampleFlags.Write;
            o.sampleColor = new Color(1f, 0.70f, 0.28f, 1f);
            o.sampleVector2 = new Vector2(0.25f, 0.75f);
            o.sampleVector3 = new Vector3(1f, 2f, 3f);
            o.sampleVector4 = new Vector4(1f, 2f, 3f, 4f);
            o.sampleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

            // Three rows, all distinct keys: the preview is for judging the TABLE's look, and a duplicate-key
            // banner sitting in the Preset Editor would read as an error in the user's own configuration.
            o.sampleDictionary = new SerializedDictionary<string, int>
            {
                { "Common", 60 },
                { "Rare", 25 },
                { "Epic", 15 },
            };

            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.45f, 0.78f, 1f), 0f), new GradientColorKey(new Color(1f, 0.70f, 0.28f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            o.sampleGradient = grad;

            o.sampleEvent = new UnityEvent();
            o.sampleManagedRef = new SampleRefImpl();
        }
    }
}
