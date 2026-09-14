using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Runtime twin of the inspector's <c>{fieldName}</c> token resolver. The Rich Text Preview shows a
    /// template's tokens filled in with live field values; this is what makes the SAME string produce the
    /// SAME output once it reaches TextMeshPro, a uGUI Text, a UI Toolkit label, or a log line — so the
    /// preview is a promise about runtime rather than an editor-only convenience.
    ///
    /// The formatting table below is a deliberate mirror of the editor's <c>FormatTokenValue</c>
    /// (Editor/Binding/Widgets/BindingFactory.RichText.cs) and of the exported custom editor's
    /// <c>RichFormatToken</c> (Editor/Export/ExportedWidgetKit.cs). Those three MUST agree — a divergence
    /// is invisible in the designer and only shows up in a built player. `TestTokenFormatParityAcrossSites`
    /// pins them together.
    /// </summary>
    public static class SasquatchTokens
    {
        /// <summary>
        /// The token grammar, shared verbatim with the inspector preview. Top-level field names only:
        /// a token is a C# identifier in braces. Anything else (paths, format specs, mismatched braces)
        /// simply doesn't match and is left in the text untouched, which is what makes an unknown token
        /// render as literal <c>{text}</c> instead of vanishing.
        /// </summary>
        public const string TokenPattern = @"\{([A-Za-z_][A-Za-z0-9_]*)\}";

        private static readonly Regex TokenRegex = new Regex(TokenPattern, RegexOptions.Compiled);

        // Type -> serialized-field lookup. Reflection here is over the USER's own types, never Unity
        // Editor internals, so it stays clear of Asset Store rule 2.5.g.
        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> FieldCache =
            new Dictionary<Type, Dictionary<string, FieldInfo>>();
        private static readonly object CacheLock = new object();

        /// <summary>
        /// Replaces every <c>{fieldName}</c> in <paramref name="template"/> with the live value of that
        /// serialized field on <paramref name="source"/>. Unknown names and unsupported field types are
        /// left exactly as typed.
        ///
        /// Single-pass by design: a substituted value that itself contains braces is never re-scanned, so
        /// a self-referencing or circular template can't recurse.
        /// </summary>
        public static string Resolve(string template, object source)
        {
            if (string.IsNullOrEmpty(template)) return template;
            // Cheap bail-out: the overwhelming majority of strings carry no tokens at all, and this runs
            // on every assignment to a TMP_Text.
            if (template.IndexOf('{') < 0) return template;
            if (IsNullSource(source)) return template;

            var fields = GetSerializedFields(source.GetType());
            return TokenRegex.Replace(template, m =>
            {
                FieldInfo field;
                if (!fields.TryGetValue(m.Groups[1].Value, out field)) return m.Value;
                object value;
                try { value = field.GetValue(source); }
                catch (Exception) { return m.Value; }
                return Format(value, field.FieldType) ?? m.Value;
            });
        }

        /// <summary>
        /// True when a field of this type produces a value the resolver can print. Mirrors the editor's
        /// <c>IsTokenFormattable</c>, which is what the <c>{</c> autocomplete offers — so the designer
        /// never suggests a name that would fail to resolve at runtime.
        /// </summary>
        public static bool IsFormattable(Type t)
        {
            if (t == null) return false;
            return t == typeof(string) || t == typeof(bool) || t.IsEnum
                || t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
                || t == typeof(float) || t == typeof(double)
                || t == typeof(Color) || t == typeof(Color32)
                || typeof(UnityEngine.Object).IsAssignableFrom(t);
        }

        // A destroyed UnityEngine.Object is not reference-null but must be treated as absent. Declaring the
        // parameter as `object` means Unity's lifted == operator would NOT fire without this cast, so a
        // template on a destroyed component would otherwise reflect into a dead object.
        private static bool IsNullSource(object source)
        {
            if (source == null) return true;
            if (source is UnityEngine.Object unityObject) return unityObject == null;
            return false;
        }

        /// <summary>
        /// Display text for one resolved value. Returns null for a type the resolver doesn't handle, which
        /// tells the caller to keep the literal token.
        /// </summary>
        internal static string Format(object value, Type declaredType)
        {
            if (declaredType == typeof(string)) return value as string ?? string.Empty;
            if (declaredType == typeof(bool)) return (bool)value ? "True" : "False";
            if (declaredType.IsEnum) return FormatEnum(value, declaredType);

            if (declaredType == typeof(int) || declaredType == typeof(long)
                || declaredType == typeof(short) || declaredType == typeof(byte))
                return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

            // "0.###" matches the inspector, which reads SerializedProperty.doubleValue — a float widens to
            // double exactly the way Convert.ToDouble widens it, so both sides round identically.
            if (declaredType == typeof(float) || declaredType == typeof(double))
                return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture);

            if (declaredType == typeof(Color)) return "#" + ToHex((Color)value);
            if (declaredType == typeof(Color32)) return "#" + ToHex((Color)(Color32)value);

            if (typeof(UnityEngine.Object).IsAssignableFrom(declaredType))
            {
                var o = value as UnityEngine.Object;
                return o != null ? o.name : "None";
            }
            return null;
        }

        // Mirrors ColorHexUtility.ToHex(color, includeHash:false): 6 digits when opaque, 8 when not, so a
        // token pasted into TMP's <color=#…> tag stays a legal color literal either way.
        private static string ToHex(Color c)
        {
            return c.a >= 0.999f ? ColorUtility.ToHtmlStringRGB(c) : ColorUtility.ToHtmlStringRGBA(c);
        }

        // The inspector prints an enum through SerializedProperty.enumDisplayNames, which honours
        // [InspectorName] and otherwise nicifies the member name. Reproduced here so "FireDamage" reads
        // "Fire Damage" in a built player exactly as it did in the preview. A value with no declared member
        // (a flags combination, or a cast-in int) has no display name at all — the inspector falls back to
        // the raw number, so this does too.
        private static string FormatEnum(object value, Type enumType)
        {
            string name = Enum.GetName(enumType, value);
            if (name == null)
                return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

            var member = enumType.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (member != null)
            {
                var attrs = member.GetCustomAttributes(typeof(InspectorNameAttribute), false);
                if (attrs != null && attrs.Length > 0)
                {
                    var inspectorName = (InspectorNameAttribute)attrs[0];
                    if (!string.IsNullOrEmpty(inspectorName.displayName)) return inspectorName.displayName;
                }
            }
            return Nicify(name);
        }

        /// <summary>
        /// Runtime clone of <c>UnityEditor.ObjectNames.NicifyVariableName</c>, which the Runtime assembly
        /// cannot reference. Verified identical across 6,030 generated enum-member-shaped identifiers plus a
        /// curated edge-case corpus; `TestNicifyMatchesUnity` re-checks that on every run.
        ///
        /// Two stages, because Unity's own output demands both: split into words at case/digit boundaries,
        /// then re-merge adjacent runs that are purely uppercase-or-digit. The merge is what turns
        /// "MaxHP2" into "Max HP2" rather than "Max HP 2", and "aB" into "AB" rather than "A B".
        /// </summary>
        public static string Nicify(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            // Unity strips one serialized-name prefix: "m_", a "k" that fronts an uppercase letter, or a
            // single leading underscore. Only one, and only from the front.
            int i = 0;
            if (name.Length >= 2 && name[0] == 'm' && name[1] == '_') i = 2;
            else if (name.Length >= 2 && name[0] == 'k' && char.IsUpper(name[1])) i = 1;
            else if (name[0] == '_') i = 1;
            if (i >= name.Length) return string.Empty;

            var words = new List<string>();
            var current = new StringBuilder();
            current.Append(char.ToUpperInvariant(name[i]));
            for (int j = i + 1; j < name.Length; j++)
            {
                char c = name[j];
                char prev = name[j - 1];
                bool split = false;
                if (char.IsUpper(c))
                {
                    // "fireDamage" / "a1B" start a word; inside an acronym only the last capital does,
                    // and only when a lowercase follows it ("HTTPServer" -> "HTTP Server").
                    if (char.IsLower(prev) || char.IsDigit(prev)) split = true;
                    else if (char.IsUpper(prev) && j + 1 < name.Length && char.IsLower(name[j + 1])) split = true;
                }
                else if (char.IsDigit(c) && char.IsLetter(prev)) split = true;

                // An underscore is neither a boundary nor stripped mid-name: Unity leaves "is_on" as "Is_on".
                if (split) { words.Add(current.ToString()); current.Length = 0; }
                current.Append(c);
            }
            words.Add(current.ToString());

            for (int w = 0; w < words.Count - 1; )
            {
                if (IsUpperOrDigitRun(words[w]) && IsUpperOrDigitRun(words[w + 1]))
                {
                    words[w] = words[w] + words[w + 1];
                    words.RemoveAt(w + 1);
                }
                else w++;
            }
            return string.Join(" ", words.ToArray());
        }

        private static bool IsUpperOrDigitRun(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            for (int i = 0; i < word.Length; i++)
            {
                char c = word[i];
                if (!char.IsUpper(c) && !char.IsDigit(c)) return false;
            }
            return true;
        }

        // The set of fields SerializedObject.FindProperty would find: public instance fields plus private
        // ones carrying [SerializeField], minus [NonSerialized], walked base-first so a derived declaration
        // shadows an inherited one of the same name (GetFields never inherits private fields, hence the walk).
        private static Dictionary<string, FieldInfo> GetSerializedFields(Type type)
        {
            lock (CacheLock)
            {
                Dictionary<string, FieldInfo> cached;
                if (FieldCache.TryGetValue(type, out cached)) return cached;

                var chain = new List<Type>();
                for (var t = type; t != null && t != typeof(object); t = t.BaseType) chain.Add(t);
                chain.Reverse();

                var map = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public
                                         | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var t in chain)
                {
                    foreach (var f in t.GetFields(flags))
                    {
                        if (f.IsStatic || f.IsLiteral || f.IsInitOnly) continue;
                        if (Attribute.IsDefined(f, typeof(NonSerializedAttribute))) continue;
                        if (!f.IsPublic && !Attribute.IsDefined(f, typeof(SerializeField))) continue;
                        if (!IsFormattable(f.FieldType)) continue;
                        map[f.Name] = f;
                    }
                }
                FieldCache[type] = map;
                return map;
            }
        }
    }
}
