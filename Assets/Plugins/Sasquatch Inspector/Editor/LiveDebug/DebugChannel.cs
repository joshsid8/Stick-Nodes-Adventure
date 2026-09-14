using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>How a channel's samples are read and drawn. Chosen once from the property's type at registration.</summary>
    internal enum DebugChannelKind
    {
        /// <summary>int / long / float / double — a single auto-framed line.</summary>
        Numeric,
        /// <summary>bool — a square wave. Reads as a rectangle, which is exactly what a 2-frame dropout looks like.</summary>
        Bool,
        /// <summary>enum — a step trace plus the member name in the readout.</summary>
        Enum,
        /// <summary>Vector2/3/4, Quaternion — up to four series in one well, X/Y/Z/W coloured.</summary>
        Vector,
        /// <summary>string, object reference, Color, anything else — a change ribbon: a tick per change, value as text.</summary>
        Opaque
    }

    /// <summary>
    /// One field's recorded history: a fixed-capacity ring of samples, its running min/max, and (for
    /// <see cref="DebugChannelKind.Opaque"/>) a compact log of the values it actually took.
    ///
    /// <para><b>The ring is index-aligned with every other channel on the same
    /// <see cref="DebugRecorder"/>.</b> Sample <c>i</c> means the same instant in all of them, which is what
    /// makes the "what else changed on this frame" readout possible at all. Nothing here may ever append
    /// independently — <see cref="DebugRecorder.Sample"/> is the only writer, and it writes every channel or
    /// none.</para>
    ///
    /// <para>Storage is series-interleaved (<c>values[i * SeriesCount + s]</c>) and allocated ONCE at
    /// construction: this is read by a <c>generateVisualContent</c> pass that must never allocate.</para>
    /// </summary>
    internal sealed class DebugChannel
    {
        /// <summary>How many distinct values an Opaque channel remembers before it stops recording new ones.</summary>
        private const int MaxOpaqueChanges = 256;

        internal string Path { get; }
        internal string Label { get; }
        /// <summary>
        /// Settable, because a channel rebuilt from the trace store arrives with a GUESSED kind — see
        /// <see cref="FromStore"/> — and only the live property can say what it really is.
        /// </summary>
        internal DebugChannelKind Kind { get; private set; }
        /// <summary>1 for a scalar; 2-4 for a vector. Fixed at construction.</summary>
        internal int SeriesCount { get; }

        /// <summary>Enum member display names, indexed by the stored value. Null for every other kind.</summary>
        internal string[] EnumNames { get; private set; }

        /// <summary>
        /// The DECLARED VALUE behind each entry of <see cref="EnumNames"/>, or null when the enum's shape could
        /// not be resolved.
        ///
        /// <para>Doubles as the flag for WHICH ENCODING the ring holds. Non-null means samples are the enum's
        /// underlying number — the thing the inspector shows. Null means the old positional
        /// <c>enumValueIndex</c>, kept as a fallback so a property whose type cannot be reached still records
        /// something readable.</para>
        ///
        /// <para><b>Why the number and not the index.</b> <c>enumValueIndex</c> is defined only for a value
        /// matching exactly ONE declared member, and returns <b>-1</b> for everything else — so on a
        /// <c>[Flags]</c> field every multi-bit mask recorded as the same -1. <c>Fire|Frost</c> and
        /// <c>Fire|Shock</c> were the same sample, which means no change marker, no jump target, and
        /// "never changed" printed on a field that had just moved. This package ships a dedicated multi-select
        /// widget for flags enums, so that is not an exotic case.</para>
        /// </summary>
        internal long[] EnumValues { get; private set; }

        /// <summary>True when the recorded enum carries <see cref="FlagsAttribute"/>, so a sample is a mask of members rather than one of them.</summary>
        internal bool IsFlagsEnum { get; private set; }

        /// <summary>Authored range from a <c>[Range]</c> attribute, when the node carries one — otherwise the graph auto-frames.</summary>
        internal bool HasAuthoredRange { get; }
        internal float AuthoredMin { get; }
        internal float AuthoredMax { get; }

        // NOT allocated in the constructor. A DebugChannel is built for every bindable field of every type
        // the user clicks on, whether or not Live Debug is ever armed for it — capacity x series floats
        // apiece, ~50 KB per component on first click and ~4 MB of resident rings for types nobody debugs.
        // Nothing can read a slot before a sample has been written into it (Read is only reached through the
        // recorder's sample count, which is zero until Write runs), so the array is minted on first write.
        private float[] _values;
        private readonly int _capacity;

        /// <summary>
        /// The bound property, resolved ONCE and held. Re-resolving per tick was measurably the wrong shape:
        /// <c>FindProperty</c> is a string-path walk, and at 30 scripts x 10 fields x ~57 ticks/second that is
        /// ~17,000 path lookups a second doing nothing but re-deriving an answer that had not changed.
        /// Rebound when the recorder is handed a new SerializedObject (every inspector rebuild produces one).
        /// </summary>
        private SerializedProperty _prop;

        /// <summary>Running extremes across everything still in the ring's lifetime (NOT just what's visible).</summary>
        internal float SeenMin { get; private set; } = float.PositiveInfinity;
        internal float SeenMax { get; private set; } = float.NegativeInfinity;

        /// <summary>
        /// False until the value differs from the very first sample. This is the "is my code even running?"
        /// answer — the single most common question behind a scattered-Debug.Log session — so it is tracked
        /// for every kind, including the ones that draw as a flat ribbon.
        /// </summary>
        internal bool EverChanged { get; private set; }

        /// <summary>Sample indices at which the value changed. Drives the ribbon ticks and the moment card.</summary>
        internal readonly List<int> ChangeSamples = new List<int>();

        /// <summary>Rendered value at each entry in <see cref="ChangeSamples"/>. Opaque kinds only; else null.</summary>
        private List<string> _changeText;

        /// <summary>Colour at each change, for an Opaque Color field's ribbon. Null for every other kind.</summary>
        private List<Color> _changeColor;

        private bool _hasPrevious;
        private float _previous;
        private string _previousText;

        /// <summary>
        /// The previous sample's value on EVERY series, for the Vector kinds.
        ///
        /// <para><see cref="WriteVector"/> returns only <c>v.x</c> — it has to return something, and the
        /// ribbon's single "primary" number is X by convention. Deriving <c>changed</c> from that alone made
        /// a vector that moves in Y or Z record ZERO changes: a jump, gravity, a lift, a one-axis rotation.
        /// The ring held the right numbers the whole time; only the question "did this move?" was answered
        /// from one third of them, and the row then said "never changed" — the single most load-bearing
        /// claim the readout makes, and the exact case someone opens a value graph to watch.</para>
        /// </summary>
        private float[] _previousSeries;

        /// <summary>
        /// The very first text an Opaque channel saw, and the colour with it.
        ///
        /// <para>Opaque values are stored as a CHANGE LOG, not as a per-slot array — a string does not fit in
        /// the float ring — and the first sample of any channel is a baseline rather than a change, so it logs
        /// nothing. That combination meant a string, object reference or Color that NEVER changed had no entry
        /// to read and rendered as an em dash for the whole run: the readout said "no value" about a field
        /// that plainly had one, in exactly the "is my code even running?" case the row exists to answer.
        /// Held separately rather than pushed into the change log so "3 changes" still means three changes.</para>
        /// </summary>
        private string _baselineText;
        private Color _baselineColor;
        private bool _hasBaseline;

        internal DebugChannel(string path, string label, SerializedProperty prop, int capacity,
            bool hasAuthoredRange, float authoredMin, float authoredMax)
        {
            Path = path;
            Label = label;
            _capacity = Mathf.Max(2, capacity);
            HasAuthoredRange = hasAuthoredRange;
            AuthoredMin = authoredMin;
            AuthoredMax = authoredMax;

            _prop = prop;
            Kind = Classify(prop, out int series);
            SeriesCount = series;
            // _values stays null until the first Write — see its declaration.

            if (Kind == DebugChannelKind.Enum)
            {
                // Captured once: enumDisplayNames allocates a fresh array per call, and this is read on every
                // hover.
                EnumNames = prop.enumDisplayNames != null ? (string[])prop.enumDisplayNames.Clone() : Array.Empty<string>();
                CaptureEnumShape(prop);
            }

            if (Kind == DebugChannelKind.Opaque)
            {
                _changeText = new List<string>();
                _changeColor = prop.propertyType == SerializedPropertyType.Color ? new List<Color>() : null;
            }
        }

        /// <summary>
        /// Learns the declared value behind every member name, and whether the enum is a <c>[Flags]</c> set.
        ///
        /// <para>The type comes from <c>BindingFactory.GetFieldInfoFromPropertyPath</c>, the same resolver the
        /// enum badge grid and the condition evaluator use — so a field this channel calls a flags enum is one
        /// the multi-select widget would also call a flags enum. <b>Not reflection into an editor internal</b>
        /// (Asset Store 2.5.g): the type being examined belongs to the USER's script. <c>boxedValue</c> is no
        /// use here — for an enum property it hands back a boxed <c>System.Int32</c>, not the enum.</para>
        ///
        /// <para><see cref="SerializedProperty.enumNames"/> is used rather than <c>Enum.GetValues</c> because it
        /// is in DECLARATION order, which is the order <see cref="SerializedProperty.enumDisplayNames"/> uses —
        /// so the two arrays stay parallel by construction. <c>Enum.GetValues</c> sorts by value, which silently
        /// mismatches the names on any enum whose members are not declared in ascending order.</para>
        ///
        /// <para>Failure is not an error: the channel keeps <see cref="EnumValues"/> null and records the old
        /// positional index, which is what it always did.</para>
        /// </summary>
        private void CaptureEnumShape(SerializedProperty prop)
        {
            try
            {
                var target = prop.serializedObject != null ? prop.serializedObject.targetObject : null;
                if (target == null) return;

                Type enumType;
                BindingFactory.GetFieldInfoFromPropertyPath(target.GetType(), prop.propertyPath, out enumType);
                if (enumType == null) return;
                if (!enumType.IsEnum && enumType.IsArray) enumType = enumType.GetElementType();
                if (enumType == null || !enumType.IsEnum) return;

                string[] raw = prop.enumNames;
                if (raw == null || raw.Length == 0) return;

                var values = new long[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    values[i] = Convert.ToInt64(Enum.Parse(enumType, raw[i]));

                EnumValues = values;
                IsFlagsEnum = enumType.IsDefined(typeof(FlagsAttribute), false);
            }
            catch
            {
                // Any enum this cannot read still records — see EnumValues.
                EnumValues = null;
                IsFlagsEnum = false;
            }
        }

        /// <summary>
        /// The member name (or the <c>A | B</c> composition) behind a recorded enum sample.
        ///
        /// <para>A flags mask names every member it contains, because "3" tells a reader nothing about a field
        /// this package renders as a row of named badges. Bits belonging to no declared member are appended as a
        /// number rather than dropped — an undeclared bit is exactly the kind of thing someone opens a value
        /// graph to find.</para>
        /// </summary>
        private string EnumText(long value)
        {
            // No shape: the ring holds a positional index, as it always did.
            if (EnumValues == null)
                return EnumNames != null && value >= 0 && value < EnumNames.Length
                    ? EnumNames[value]
                    : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (!IsFlagsEnum)
            {
                for (int i = 0; i < EnumValues.Length; i++)
                    if (EnumValues[i] == value && EnumNames != null && i < EnumNames.Length) return EnumNames[i];
                return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (value == 0)
            {
                for (int i = 0; i < EnumValues.Length; i++)
                    if (EnumValues[i] == 0 && EnumNames != null && i < EnumNames.Length) return EnumNames[i];
                return "None";
            }

            string text = null;
            long covered = 0;
            for (int i = 0; i < EnumValues.Length; i++)
            {
                long bit = EnumValues[i];
                // Zero is the "None" member and is contained in every mask; a member is only named when ALL of
                // its bits are present, which is what makes a composite member like `All` read correctly.
                if (bit == 0 || (value & bit) != bit) continue;
                covered |= bit;
                string name = EnumNames != null && i < EnumNames.Length ? EnumNames[i] : bit.ToString();
                text = text == null ? name : text + " | " + name;
            }

            long leftover = value & ~covered;
            if (leftover != 0)
            {
                string extra = leftover.ToString(System.Globalization.CultureInfo.InvariantCulture);
                text = text == null ? extra : text + " | " + extra;
            }
            return text ?? value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Picks the kind (and series count) a property records as. Anything unrecognised lands on Opaque, which always works.</summary>
        private static DebugChannelKind Classify(SerializedProperty prop, out int series)
        {
            series = 1;
            if (prop == null) return DebugChannelKind.Opaque;

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Float:
                    return DebugChannelKind.Numeric;
                case SerializedPropertyType.Boolean:
                    return DebugChannelKind.Bool;
                case SerializedPropertyType.Enum:
                    return DebugChannelKind.Enum;
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector2Int:
                    series = 2;
                    return DebugChannelKind.Vector;
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector3Int:
                    series = 3;
                    return DebugChannelKind.Vector;
                case SerializedPropertyType.Vector4:
                    series = 4;
                    return DebugChannelKind.Vector;
                // THREE, not four. A Quaternion is recorded as EULER DEGREES (see WriteVector), which is three
                // numbers — the fourth series was padding, written as a hardcoded 0. That 0 was then folded
                // into SeenMin like any real sample, so the auto-frame of every rotation was stretched down to
                // include zero: a heading oscillating between 170° and 190° drew as a flat line in the top
                // tenth of the well. It was also drawn as a fourth accent-coloured line and printed as a
                // fourth number in the readout and in the CSV, for an axis that does not exist.
                case SerializedPropertyType.Quaternion:
                    series = 3;
                    return DebugChannelKind.Vector;
                default:
                    return DebugChannelKind.Opaque;
            }
        }

        /// <summary>True for a property this channel can still read — a stale path (script edited mid-session) reads null and is skipped.</summary>
        internal static bool CanRecord(SerializedProperty prop) => prop != null;

        /// <summary>
        /// Re-resolves the held property against a new SerializedObject. Every inspector rebuild makes one, and a
        /// property from the previous SerializedObject reads stale (or throws once that object is disposed).
        /// </summary>
        internal void Rebind(SerializedObject so)
        {
            var prop = so != null ? so.FindProperty(Path) : null;
            _prop = prop;
            if (prop != null) Reclassify(prop);
        }

        /// <summary>
        /// Re-derives <see cref="Kind"/> from the live property, and unbinds when the field no longer has the
        /// shape the stored samples were recorded in.
        ///
        /// <para><b>Why this is not optional, and why it is HERE.</b> <see cref="FromStore"/> cannot know what
        /// a restored channel was — the blob holds values, not a kind — so it guesses from the shape: multi
        /// series is a Vector, carried text is Opaque, and <i>everything else is Numeric</i>. That guess is
        /// wrong for every <b>bool</b>, every <b>enum</b>, and every <b>string that happened not to change
        /// during the recording</b>, because none of them stores text and all of them are single-series.
        /// A restore happens on every recompile (and, with Unity's default Enter Play Mode settings, on every
        /// play-mode edge as well), so this ran constantly: the channel came
        /// back believing it was a number and <c>Write</c> read <c>doubleValue</c> off a Boolean property,
        /// which throws <i>"is not a supported float value"</i> once per field per sample — an error storm
        /// from inside the player loop, arriving at the exact moment the user stops the game to read the
        /// trace. <see cref="FromStore"/>'s own comment already promised this re-classification; it had never
        /// been written.</para>
        ///
        /// <para><b>The samples survive a kind change, and that is correct.</b> Every single-series kind
        /// stores the same thing it always did — Bool writes 1/0, Enum writes the member index, Opaque writes
        /// a rising change counter — so a mis-guessed Numeric ring already holds exactly the right floats.
        /// Only the interpretation was wrong. A change in SERIES count is different: the ring is physically a
        /// different shape, every stored sample describes a different quantity, and the honest move is to stop
        /// reading it rather than to reinterpret it.</para>
        ///
        /// <para>It also covers the live case that has nothing to do with the store: a cached channel outlives
        /// the inspector that built it, so editing the script to change a field's type would otherwise leave
        /// the old kind reading the new field.</para>
        /// </summary>
        private void Reclassify(SerializedProperty prop)
        {
            var kind = Classify(prop, out int series);

            if (series != SeriesCount)
            {
                _prop = null;
                return;
            }

            if (kind == Kind) return;
            Kind = kind;

            // The side tables the primary constructor would have built for this kind. Each is allocated only
            // if it is missing, so a re-classification never discards history it already holds.
            if (kind == DebugChannelKind.Enum && EnumNames == null)
            {
                EnumNames = prop.enumDisplayNames != null
                    ? (string[])prop.enumDisplayNames.Clone()
                    : Array.Empty<string>();
                CaptureEnumShape(prop);
            }
            else if (kind == DebugChannelKind.Opaque)
            {
                if (_changeText == null) _changeText = new List<string>();
                if (_changeColor == null && prop.propertyType == SerializedPropertyType.Color)
                    _changeColor = new List<Color>();
            }
        }

        /// <summary>True while this channel still has a property to read — false after the field left the script.</summary>
        internal bool IsBound => _prop != null;

        /// <summary>
        /// Writes one sample at ring slot <paramref name="slot"/>. Called only by <see cref="DebugRecorder.Sample"/>,
        /// once per channel per tick, so every channel stays index-aligned.
        /// </summary>
        internal void Write(int slot, int absoluteIndex)
        {
            var prop = _prop;
            if (prop == null) return;

            // First sample: mint the ring. Deliberately AFTER the null-property bail, so a channel whose
            // property vanished never allocates one either.
            if (_values == null) _values = new float[_capacity * SeriesCount];
            if (_previousSeries == null) _previousSeries = new float[SeriesCount];

            int b = slot * SeriesCount;
            float primary;
            string text = null;
            Color color = default;

            switch (Kind)
            {
                case DebugChannelKind.Numeric:
                    // Through ReadNumeric, never intValue/floatValue — those silently truncate long and double.
                    primary = (float)BindingFactory.ReadNumeric(prop);
                    _values[b] = primary;
                    break;

                case DebugChannelKind.Bool:
                    primary = prop.boolValue ? 1f : 0f;
                    _values[b] = primary;
                    break;

                case DebugChannelKind.Enum:
                    // The enum's own NUMBER when its shape is known, the positional index otherwise. See
                    // EnumValues: enumValueIndex is -1 for every value that is not exactly one declared member,
                    // which on a [Flags] field collapses every mask onto one sample.
                    primary = EnumValues != null ? prop.intValue : prop.enumValueIndex;
                    _values[b] = primary;
                    break;

                case DebugChannelKind.Vector:
                    primary = WriteVector(prop, b);
                    break;

                default:
                    text = Describe(prop, out color);
                    // The ring holds a monotonically rising change count, so an Opaque channel still draws a
                    // meaningful step even though its VALUE isn't a number. The text is what the readout shows.
                    primary = _hasPrevious && !string.Equals(text, _previousText, StringComparison.Ordinal)
                        ? _previous + 1f
                        : (_hasPrevious ? _previous : 0f);
                    _values[b] = primary;
                    break;
            }

            bool changed;
            if (!_hasPrevious)
            {
                changed = false;
                _hasPrevious = true;
                if (Kind == DebugChannelKind.Opaque && !_hasBaseline)
                {
                    _baselineText = text;
                    _baselineColor = color;
                    _hasBaseline = true;
                }
            }
            else if (Kind == DebugChannelKind.Opaque)
            {
                changed = !string.Equals(text, _previousText, StringComparison.Ordinal);
            }
            else if (Kind == DebugChannelKind.Vector)
            {
                // Every series, not just the one WriteVector returns. Same exact-inequality rule as below,
                // applied per axis: a Vector3 that only ever moves in Y is still a Vector3 that moved.
                changed = false;
                for (int s = 0; s < SeriesCount; s++)
                    if (SampleDiffers(_values[b + s], _previousSeries[s])) { changed = true; break; }
            }
            else
            {
                changed = SampleDiffers(primary, _previous);
            }

            if (changed)
            {
                EverChanged = true;
                if (ChangeSamples.Count < MaxOpaqueChanges || Kind != DebugChannelKind.Opaque)
                {
                    ChangeSamples.Add(absoluteIndex);
                    _changeText?.Add(text);
                    _changeColor?.Add(color);
                }
                // The change list is unbounded for non-Opaque kinds only in principle: TrimChanges below drops
                // entries that have scrolled out of the ring, so it stays proportional to the window, not to
                // session length.
            }

            _previous = primary;
            _previousText = text;
            for (int s = 0; s < SeriesCount; s++) _previousSeries[s] = _values[b + s];

            for (int s = 0; s < SeriesCount; s++)
            {
                float v = _values[b + s];
                if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                if (v < SeenMin) SeenMin = v;
                if (v > SeenMax) SeenMax = v;
            }
        }

        private float WriteVector(SerializedProperty prop, int b)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Vector2:
                {
                    var v = prop.vector2Value;
                    _values[b] = v.x; _values[b + 1] = v.y;
                    return v.x;
                }
                case SerializedPropertyType.Vector2Int:
                {
                    var v = prop.vector2IntValue;
                    _values[b] = v.x; _values[b + 1] = v.y;
                    return v.x;
                }
                case SerializedPropertyType.Vector3:
                {
                    var v = prop.vector3Value;
                    _values[b] = v.x; _values[b + 1] = v.y; _values[b + 2] = v.z;
                    return v.x;
                }
                case SerializedPropertyType.Vector3Int:
                {
                    var v = prop.vector3IntValue;
                    _values[b] = v.x; _values[b + 1] = v.y; _values[b + 2] = v.z;
                    return v.x;
                }
                case SerializedPropertyType.Vector4:
                {
                    var v = prop.vector4Value;
                    _values[b] = v.x; _values[b + 1] = v.y; _values[b + 2] = v.z; _values[b + 3] = v.w;
                    return v.x;
                }
                case SerializedPropertyType.Quaternion:
                {
                    // Recorded as EULER degrees, not raw xyzw. A quaternion's components are not readable as a
                    // graph — a 90° turn is a curve through 0.707 — while degrees are the thing you actually
                    // wanted to watch.
                    // These are the CANONICAL decomposition, so they can differ from the numbers the Euler
                    // Angles widget shows for the same field: that widget displays an euler HINT (the triple
                    // the user dialled), and Euler(91,0,0) decomposes back to (89,180,180). Same rotation,
                    // different numbers. A recorder has no hint to carry — it samples over time with no editing
                    // session behind it — so the decomposition is the only thing it can honestly graph.
                    var e = prop.quaternionValue.eulerAngles;
                    _values[b] = e.x; _values[b + 1] = e.y; _values[b + 2] = e.z;
                    return e.x;
                }
                default:
                    for (int s = 0; s < SeriesCount; s++) _values[b + s] = 0f;
                    return 0f;
            }
        }

        /// <summary>A short, human-readable rendering of a non-graphable value — what the ribbon's readout shows.</summary>
        private static string Describe(SerializedProperty prop, out Color color)
        {
            color = default;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.String:
                    return string.IsNullOrEmpty(prop.stringValue) ? "—" : prop.stringValue;
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "None";
                case SerializedPropertyType.Color:
                    color = prop.colorValue;
                    return ColorUtility.ToHtmlStringRGBA(prop.colorValue);
                case SerializedPropertyType.Character:
                    return ((char)prop.intValue).ToString();
                case SerializedPropertyType.ArraySize:
                    return prop.intValue.ToString();
                case SerializedPropertyType.Bounds:
                    return prop.boundsValue.ToString();
                case SerializedPropertyType.Rect:
                    return prop.rectValue.ToString();
                case SerializedPropertyType.AnimationCurve:
                    return prop.animationCurveValue != null ? $"{prop.animationCurveValue.length} keys" : "None";
                case SerializedPropertyType.LayerMask:
                    return prop.intValue.ToString();
                case SerializedPropertyType.ManagedReference:
                    return string.IsNullOrEmpty(prop.managedReferenceFullTypename) ? "null" : ShortTypeName(prop.managedReferenceFullTypename);
                default:
                    return prop.propertyType.ToString();
            }
        }

        private static string ShortTypeName(string managedFullTypename)
        {
            int dot = managedFullTypename.LastIndexOf('.');
            return dot >= 0 && dot < managedFullTypename.Length - 1
                ? managedFullTypename.Substring(dot + 1)
                : managedFullTypename;
        }

        /// <summary>Reads series <paramref name="series"/> back out of ring slot <paramref name="slot"/>.</summary>
        // The null guard is belt-and-braces: a read can only be reached through the recorder's sample count,
        // which stays zero until Write has run at least once and minted the ring. Returning 0 rather than
        // throwing keeps a hypothetical early reader on exactly the value an unwritten slot held before.
        internal float Read(int slot, int series)
            => _values == null ? 0f : _values[slot * SeriesCount + series];

        /// <summary>The Opaque text recorded at (or most recently before) absolute sample <paramref name="absoluteIndex"/>; null for other kinds.</summary>
        internal string TextAt(int absoluteIndex)
        {
            if (_changeText == null) return null;
            // The baseline covers two cases the change log cannot: a value that never changed at all, and any
            // sample taken BEFORE the first recorded change.
            if (_changeText.Count == 0) return _hasBaseline ? _baselineText : null;
            string best = _hasBaseline && ChangeSamples.Count > 0 && ChangeSamples[0] > absoluteIndex
                ? _baselineText
                : _changeText[0];
            for (int i = 0; i < ChangeSamples.Count && i < _changeText.Count; i++)
            {
                if (ChangeSamples[i] > absoluteIndex) break;
                best = _changeText[i];
            }
            return best;
        }

        /// <summary>The Opaque colour recorded at (or most recently before) <paramref name="absoluteIndex"/>, when the field is a Color.</summary>
        internal bool TryColorAt(int absoluteIndex, out Color color)
        {
            color = default;
            if (_changeColor == null) return false;
            // Same baseline rule as TextAt: a Color field that never changed still has a colour to draw.
            if (_changeColor.Count == 0)
            {
                if (!_hasBaseline) return false;
                color = _baselineColor;
                return true;
            }
            color = _hasBaseline && ChangeSamples.Count > 0 && ChangeSamples[0] > absoluteIndex
                ? _baselineColor
                : _changeColor[0];
            for (int i = 0; i < ChangeSamples.Count && i < _changeColor.Count; i++)
            {
                if (ChangeSamples[i] > absoluteIndex) break;
                color = _changeColor[i];
            }
            return true;
        }

        // ---- persistence across the domain reload ------------------------------------------------------

        /// <summary>Private ctor for a channel rebuilt from the store — its kind and shape come from the blob.</summary>
        private DebugChannel(string path, DebugChannelKind kind, int series, int capacity)
        {
            Path = path;
            Label = ObjectNames.NicifyVariableName(path.Substring(path.LastIndexOf('.') + 1));
            Kind = kind;
            SeriesCount = Mathf.Max(1, series);
            _capacity = Mathf.Max(2, capacity);
            _values = new float[_capacity * SeriesCount];
            if (kind == DebugChannelKind.Opaque) _changeText = new List<string>();
        }

        /// <summary>Writes this channel's tail into a store blob, unrolled oldest-first.</summary>
        internal void Capture(DebugRecorder recorder, object trace, int firstAbsolute, int keep)
        {
            var bytes = new byte[keep * SeriesCount * sizeof(float)];
            for (int i = 0; i < keep; i++)
            {
                int slot = recorder.SlotOf(firstAbsolute + i);
                for (int s = 0; s < SeriesCount; s++)
                {
                    float v = slot < 0 ? 0f : Read(slot, s);
                    Buffer.BlockCopy(BitConverter.GetBytes(v), 0, bytes, (i * SeriesCount + s) * sizeof(float), sizeof(float));
                }
            }
            DebugTraceStore.AddChannel(trace, Path, bytes, SeriesCount,
                ChangeSamples.ToArray(), _changeText != null ? _changeText.ToArray() : null,
                EverChanged, SeenMin, SeenMax);
        }

        /// <summary>Rebuilds a channel from a stored blob. Unbound until the recorder hands it a SerializedObject.</summary>
        internal static DebugChannel FromStore(string path, byte[] values, int series, int[] marks,
            string[] text, bool everChanged, float seenMin, float seenMax, int capacity, DebugRecorder recorder)
        {
            if (string.IsNullOrEmpty(path) || values == null || series <= 0) return null;

            // The kind is re-derived from the shape rather than stored: a multi-series channel is a Vector, a
            // channel that carried text is Opaque, and everything else re-classifies on rebind. Storing the enum
            // would let a stale blob outlive a field whose TYPE changed, which is exactly when it must not.
            var kind = series > 1 ? DebugChannelKind.Vector
                     : (text != null && text.Length > 0) ? DebugChannelKind.Opaque
                     : DebugChannelKind.Numeric;

            var ch = new DebugChannel(path, kind, series, capacity);
            int samples = values.Length / (series * sizeof(float));
            int firstAbs = recorder.Absolute - Mathf.Min(samples, capacity);
            for (int i = 0; i < samples; i++)
            {
                int slot = recorder.SlotOf(firstAbs + i);
                if (slot < 0) continue;
                for (int s = 0; s < series; s++)
                    ch._values[slot * series + s] = BitConverter.ToSingle(values, (i * series + s) * sizeof(float));
            }

            if (marks != null) ch.ChangeSamples.AddRange(marks);
            if (text != null && ch._changeText != null) ch._changeText.AddRange(text);
            ch.EverChanged = everChanged;
            ch.SeenMin = seenMin;
            ch.SeenMax = seenMax;
            return ch;
        }

        /// <summary>Wipes recorded history but keeps the binding — the Clear button, and the start of a new run.</summary>
        internal void ClearSamples()
        {
            ChangeSamples.Clear();
            _changeText?.Clear();
            _hasBaseline = false;
            _baselineText = null;
            _changeColor?.Clear();
            SeenMin = float.PositiveInfinity;
            SeenMax = float.NegativeInfinity;
            EverChanged = false;
            _hasPrevious = false;
            _previousText = null;
            _previous = 0f;
            if (_previousSeries != null) Array.Clear(_previousSeries, 0, _previousSeries.Length);
        }

        /// <summary>Drops change entries that have scrolled out of the ring, so the lists track the window rather than the session.</summary>
        internal void TrimChanges(int oldestAbsoluteIndex)
        {
            int drop = 0;
            while (drop < ChangeSamples.Count && ChangeSamples[drop] < oldestAbsoluteIndex) drop++;
            // Always keep the last entry even when it has aged out: it is the value currently in force, and an
            // Opaque readout with no entries would show blank for a field that simply hasn't changed lately.
            if (drop >= ChangeSamples.Count) drop = Mathf.Max(0, ChangeSamples.Count - 1);
            if (drop <= 0) return;

            // PROMOTE the last entry being dropped into the baseline. It is the value in force for every
            // sample between it and the next surviving change, so discarding it outright makes TextAt fall
            // all the way back to the value the RECORDING started with — a string/object/Color row then
            // reports, for the whole left of the window, something the field last held minutes ago. The
            // baseline is exactly the "value before the first retained change" slot, which is what this is.
            if (_changeText != null && _changeText.Count > 0)
            {
                _baselineText = _changeText[Mathf.Min(drop, _changeText.Count) - 1];
                _hasBaseline = true;
            }
            if (_changeColor != null && _changeColor.Count > 0)
                _baselineColor = _changeColor[Mathf.Min(drop, _changeColor.Count) - 1];

            ChangeSamples.RemoveRange(0, drop);
            _changeText?.RemoveRange(0, Mathf.Min(drop, _changeText.Count));
            _changeColor?.RemoveRange(0, Mathf.Min(drop, _changeColor.Count));
        }

        /// <summary>
        /// Did this sample move from the one before it?
        ///
        /// <para>Exact inequality on purpose: an epsilon would swallow the slow drift that a "why is this
        /// creeping?" investigation is looking for, and these are the same bits the inspector shows.</para>
        ///
        /// <para><b>Except for NaN.</b> IEEE says <c>NaN != NaN</c>, so the plain exact rule logged a change
        /// on EVERY sample for a field sitting at NaN — 1,199 marks on a full ring, a marker ribbon that is a
        /// solid bar, and jump-to-change collapsed into a second step-forward. A field that was NaN and still
        /// is NaN has not moved. Going INTO or OUT OF NaN is a real change and still counts, which is the
        /// transition someone chasing a divide-by-zero actually wants to find.</para>
        /// </summary>
        private static bool SampleDiffers(float now, float was)
        {
            if (float.IsNaN(now)) return !float.IsNaN(was);
            if (float.IsNaN(was)) return true;
            return !Mathf.Approximately(now, was) || now != was;
        }

        /// <summary>
        /// True when this channel logged a change AT this exact absolute sample.
        ///
        /// <para>This is the same list the graph's change markers are drawn from, so anything that asks the
        /// question this way cannot disagree with what the user can see. <see cref="ChangeSamples"/> is
        /// append-only and therefore sorted, so a binary search is safe.</para>
        /// </summary>
        internal bool ChangedAt(int absoluteIndex) => ChangeSamples.BinarySearch(absoluteIndex) >= 0;

        /// <summary>Formats one sample for the readout — enum member name, bool word, vector triple, or a trimmed number.</summary>
        /// <param name="exact">
        /// Render numbers at full precision instead of the readout's two decimals. For the case where two
        /// genuinely different samples both round to the same short string, which would otherwise print as
        /// "1 → 1" and read as a bug.
        /// </param>
        internal string Format(int slot, int absoluteIndex, bool exact = false)
        {
            switch (Kind)
            {
                case DebugChannelKind.Bool:
                    return Read(slot, 0) > 0.5f ? "true" : "false";
                case DebugChannelKind.Enum:
                    return EnumText((long)Mathf.Round(Read(slot, 0)));
                case DebugChannelKind.Vector:
                {
                    if (SeriesCount == 2) return $"{N(slot, 0, exact)}, {N(slot, 1, exact)}";
                    if (SeriesCount == 3) return $"{N(slot, 0, exact)}, {N(slot, 1, exact)}, {N(slot, 2, exact)}";
                    return $"{N(slot, 0, exact)}, {N(slot, 1, exact)}, {N(slot, 2, exact)}, {N(slot, 3, exact)}";
                }
                case DebugChannelKind.Opaque:
                    return TextAt(absoluteIndex) ?? "—";
                default:
                    return N(slot, 0, exact);
            }
        }

        private string N(int slot, int series, bool exact)
            => exact ? TrimExact(Read(slot, series)) : Trim(Read(slot, series));

        /// <summary>Two decimals, then trailing zeros stripped — so 62 reads "62" and 0.6 reads "0.6", not "62.00".</summary>
        internal static string Trim(float v)
        {
            if (float.IsNaN(v)) return "NaN";
            if (float.IsInfinity(v)) return v > 0 ? "∞" : "-∞";
            string s = v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        /// <summary>
        /// Full precision, for the co-change readout's fallback. <see cref="Trim"/>'s two decimals are right
        /// for a readout you glance at, but they make a real 1.001 → 1.002 move print as "1 → 1".
        /// </summary>
        internal static string TrimExact(float v)
        {
            if (float.IsNaN(v)) return "NaN";
            if (float.IsInfinity(v)) return v > 0 ? "∞" : "-∞";
            string s = v.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        /// <summary>Component labels for a vector channel — the axis letters, then plain indices past W.</summary>
        private static readonly string[] AxisLetters = { "X", "Y", "Z", "W" };

        internal static string AxisName(int series)
            => series >= 0 && series < AxisLetters.Length ? AxisLetters[series] : (series + 1).ToString();

        /// <summary>
        /// Renders a BEFORE/AFTER pair at the shortest precision that still tells the two apart — two decimals
        /// when that already works, one more decimal at a time up to seven when it does not.
        ///
        /// <para>The readout used to have exactly two settings: <see cref="Trim"/>'s two decimals, and
        /// <see cref="TrimExact"/>'s seven. A pair that collided at two decimals fell all the way to seven,
        /// which is how <c>-3.39 → -3.39</c> became <c>-3.388955 → -3.394456</c>: five digits nobody needed,
        /// printed to prove a difference that shows in the third. On a Vector3 that is three of those side by
        /// side, and the row stopped fitting in the card at all. Stepping the precision means the string grows
        /// only as far as the actual question requires.</para>
        /// </summary>
        internal static void Pair(float a, float b, out string before, out string now)
        {
            for (int d = 2; d < 7; d++)
            {
                string x = Round(a, d), y = Round(b, d);
                if (!string.Equals(x, y, StringComparison.Ordinal))
                {
                    before = x; now = y;
                    return;
                }
            }
            before = TrimExact(a);
            now = TrimExact(b);
        }

        /// <summary>
        /// <see cref="Trim"/> at an arbitrary decimal count.
        ///
        /// <para>Trailing zeros are stripped at the default two decimals — 62 must read "62", not "62.00" —
        /// but KEPT once the precision has been escalated past them. At that point the two numbers are being
        /// compared digit against digit, and "-2.048 → -2.05" makes the reader re-count the columns to see
        /// that the last place moved by two; "-2.048 → -2.050" simply lines up.</para>
        /// </summary>
        private static string Round(float v, int decimals)
        {
            if (float.IsNaN(v)) return "NaN";
            if (float.IsInfinity(v)) return v > 0 ? "∞" : "-∞";
            string pattern = decimals <= 2
                ? "0." + new string('#', decimals)
                : "0." + new string('0', decimals);
            string s = v.ToString(pattern, System.Globalization.CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        /// <summary>
        /// Formats one sample PAIR for the co-change readout. Two decimals whenever those already differ;
        /// otherwise the shortest precision that shows the move, applied PER COMPONENT — so one drifting axis
        /// cannot force seven decimals onto the two that never moved.
        /// </summary>
        internal void FormatPair(int prevSlot, int slot, int prevAbsolute, int absolute,
            out string before, out string now)
        {
            before = Format(prevSlot, prevAbsolute);
            now = Format(slot, absolute);
            if (!string.Equals(before, now, StringComparison.Ordinal)) return;

            switch (Kind)
            {
                case DebugChannelKind.Vector:
                {
                    var a = new string[SeriesCount];
                    var b = new string[SeriesCount];
                    for (int s = 0; s < SeriesCount; s++) Pair(Read(prevSlot, s), Read(slot, s), out a[s], out b[s]);
                    before = string.Join(", ", a);
                    now = string.Join(", ", b);
                    return;
                }
                case DebugChannelKind.Numeric:
                    Pair(Read(prevSlot, 0), Read(slot, 0), out before, out now);
                    return;
                default:
                    // Bool / Enum / Opaque have no precision to spend; the old exact pass is still the answer.
                    before = Format(prevSlot, prevAbsolute, exact: true);
                    now = Format(slot, absolute, exact: true);
                    return;
            }
        }

        /// <summary>
        /// The components of a VECTOR channel that actually moved between two samples, each at the shortest
        /// precision that shows the move. Empty for every other kind, and for a vector whose stored components
        /// all compare equal.
        /// </summary>
        internal List<(string Axis, string Before, string Now)> ChangedComponents(int prevSlot, int slot)
        {
            var parts = new List<(string, string, string)>();
            if (Kind != DebugChannelKind.Vector) return parts;

            for (int s = 0; s < SeriesCount; s++)
            {
                float a = Read(prevSlot, s), b = Read(slot, s);
                // Bit equality, not a tolerance: a component that moved by a millionth still moved, and the
                // precision stepping above is what keeps that honest without printing seven digits.
                if (a.Equals(b)) continue;
                Pair(a, b, out string x, out string y);
                parts.Add((AxisName(s), x, y));
            }
            return parts;
        }
    }
}
