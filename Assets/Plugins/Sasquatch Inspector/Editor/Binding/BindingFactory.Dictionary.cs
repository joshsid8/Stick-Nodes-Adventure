using System;
using System.Collections.Generic;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// <see cref="DisplayType.Dictionary"/> — the Sasquatch Key/Value table drawn over a <b>native</b> Unity
    /// <c>Dictionary&lt;K, V&gt;</c> field.
    ///
    /// <para><b>Why this exists.</b> Unity 6.6 finally serializes dictionaries and ships a perfectly decent
    /// <c>DictionaryView</c> for them — which Sasquatch renders faithfully under <see cref="DisplayType.Default"/>.
    /// This display type is the opt-in upgrade: the same table the bonus <c>SerializedDictionary</c> gets, so a
    /// native dictionary picks up the three things Unity's own UI has no answer for —
    /// <b>drag-to-reorder</b> (Unity sorts for display only and never writes the order back),
    /// a <b>per-row right-click menu</b> (duplicate / insert above / below / delete), and
    /// <b>full Sasquatch value editors</b> (a value column of Colors shows real swatches; a column of
    /// <c>SpringConfig</c> shows the Spring widget).</para>
    ///
    /// <para><b>What it does NOT do:</b> it does not fight Unity. The column labels and key-column width come
    /// from Unity's own <c>[DictionaryDisplay]</c> attribute when the field carries one, and the duplicate /
    /// empty-key marks come from <c>SerializedProperty.GetDictionaryIgnoredEntries()</c> — literally the rows
    /// Unity's runtime will drop — rather than from a re-derivation that could disagree with it.</para>
    ///
    /// <para><b>Version story.</b> The whole file is inert before Unity 6.6: no editor older than that can
    /// produce a SerializedProperty for a <c>Dictionary</c> field at all, so <see cref="CreateDictionaryTable"/>
    /// falls through to a plain <c>PropertyField</c> there and the node survives untouched. Only the two 6.6-only
    /// APIs are <c>#if</c>-guarded; everything else compiles on the 6000.0 floor.</para>
    /// </summary>
    public static partial class BindingFactory
    {
        /// <summary>
        /// Builds the Sasquatch dictionary table for <paramref name="prop"/>, or a stock
        /// <c>PropertyField</c> when the property is not a native serialized dictionary (an older editor, or
        /// the display type parked on a field whose type changed in code). Never returns null, and never
        /// renders blank — the same degradation contract every other widget creator here follows.
        /// </summary>
        internal static VisualElement CreateDictionaryTable(BindingNode node, SerializedProperty prop, string label, bool designMode = false)
        {
            if (prop == null) return new PropertyField(prop);

            // EVERY degradation below is a labelled field, not a bare one. `label` carries the caller's
            // decision -- "" for a hidden label (a Table column or a Columns cell, where the header already
            // names the value) and the node's own override otherwise -- and dropping it printed Unity's stock
            // display name in both cases. The Default arm in BindingFactory.cs does exactly this; a fallback
            // that ignored it was the one creator in the file that did not.
            PropertyField Degrade()
            {
                var pf = new PropertyField(prop);
                if (label != null) pf.label = label;
                return pf;
            }

            // OUR OWN SerializedDictionary takes the same table. It is a Generic property whose `entries`
            // child is the array of pairs, and its halves are `Key`/`Value` (CAPITALISED - they are real C#
            // fields on Entry, unlike Unity's lowercase `key`/`value`). Routing it here rather than leaving
            // it to SerializedDictionaryDrawer.CreatePropertyGUI is what lets a SerializedDictionary field
            // pick up the node's Style and Row Frame: the drawer alone has no LayoutNode to read them from.
            if (!prop.isArray || prop.propertyType == SerializedPropertyType.String)
            {
                var entriesProp = prop.FindPropertyRelative("entries");
                if (entriesProp == null || !entriesProp.isArray) return Degrade();

                var ownSpec = new SerializedDictionaryDrawer.TableSpec
                {
                    // Key/Value and the SerializedDictionary expand prefix are both TableSpec defaults, so
                    // only the title differs from a bare `new TableSpec()`.
                    // PASSED THROUGH AS-IS: null means "no label supplied, use the property name" and EMPTY
                    // means "the label is hidden". Folding "" to null here is what made Hide Label a no-op.
                    Title = label,
                    DesignMode = designMode,
                };
                ApplyNodeDictionaryStyle(node, ownSpec);
                return SerializedDictionaryDrawer.BuildTable(prop, entriesProp, ownSpec);
            }

            // SHAPE IS NOT TYPE. Everything below builds the NATIVE table, which addresses each element's
            // halves as `key`/`value` — and `isArray` is true for every List<T> and array too. A node left on
            // DisplayType.Dictionary over a field whose code type changed to a List therefore reached
            // BuildTable, where FindPropertyRelative("key") returns null and the row build dereferenced it.
            // The guard for exactly this was written below and never called; this is its call site, and it
            // restores the contract this method's own doc comment promises.
            if (!IsNativeDictionaryProperty(prop)) return Degrade();

            // MULTI-OBJECT EDITING. Unity's own dictionary view refuses this case and shows a help box, and
            // this package's manual documents that refusal as the native shape's behaviour. Two concrete
            // reasons not to draw an editable table instead: GetDictionaryIgnoredEntries() throws on a
            // multi-target SerializedObject (so the table would not merely misbehave, it would take the whole
            // layout down through the fallback path's catch), and every row is bound BY INDEX, which for a
            // dictionary means writing one object's key into the slot another object uses for a different
            // key entirely. Deferring to the stock field keeps Sasquatch and Unity saying the same thing.
            if (prop.serializedObject != null && prop.serializedObject.isEditingMultipleObjects)
                return Degrade();

            var spec = new SerializedDictionaryDrawer.TableSpec
            {
                // LOWERCASE. Unity's pair element exposes `key`/`value`; `Key`/`Value` resolve to null there
                // (that is the bonus type's shape, whose Entry has real C# fields of those names).
                KeyRelative = "key",
                ValueRelative = "value",
                // Its own EditorPrefs namespace: a native dictionary and a SerializedDictionary can sit at the
                // same property path on two different types, and they must not share one expand bit.
                ExpandPrefix = "SasquatchInspector.NativeDictionary.exp:",
                // The native table always supplies its own title, so pass the label through unfolded: null
                // = use the property name, "" = the label is hidden. See the own-type branch above.
                Title = label,
                DesignMode = designMode,
                // ClearKeyOnNewRow is a TableSpec default (true) and covers both shapes now -- Unity grows an
                // array by cloning its last element, and InsertArrayElementAtIndex clones in place, so without
                // it every Add and every Duplicate hands the user a pre-filled duplicate key.
            };

            ApplyUnityDictionaryDisplayAttribute(prop, spec);
            AttachIgnoredEntrySource(prop, spec);
            ApplyNodeDictionaryStyle(node, spec);

            return SerializedDictionaryDrawer.BuildTable(prop, prop, spec);
        }

        /// <summary>
        /// Copies the node's Dictionary Style and Row Frame onto the spec. Both dictionary shapes go through
        /// here, which is the point: one pair of model fields, one renderer, so the native table and the
        /// SerializedDictionary table cannot drift apart the way two hand-rolled copies would.
        /// A null node (a drawer-only render, with no layout above it) keeps the spec defaults, which are the
        /// look this widget has always had.
        /// </summary>
        private static void ApplyNodeDictionaryStyle(BindingNode node, SerializedDictionaryDrawer.TableSpec spec)
        {
            if (node == null) return;
            spec.Style = node.DictionaryStyle;
            spec.RowSeparator = node.DictionaryRowSeparator;
            spec.SeparatorColor = node.DictionarySeparatorColor;
            // The CELL templates travel the same road as the container-level look, for the same reason: one
            // pair of model fields, one renderer, so a native Dictionary and a SerializedDictionary cannot end
            // up styling their values differently. Null stays null -- the drawer then builds the bare cell it
            // always built, which is what every dictionary with no authored template must keep looking like.
            spec.KeyTemplate = node.DictionaryKeyTemplate;
            spec.ValueTemplate = node.DictionaryValueTemplate;
        }

        /// <summary>
        /// The KEY (index 0) or VALUE (index 1) type of either dictionary shape, or null when
        /// <paramref name="fieldType"/> is neither. The package's own dictionary is a generic CLASS whose
        /// arguments may sit on a BASE (<c>class Loot : SerializedDictionary&lt;string,int&gt; {}</c> is a
        /// legal declaration), so this walks the base chain rather than reading the type itself.
        /// </summary>
        internal static Type DictionaryHalfType(Type fieldType, int half)
        {
            for (Type t = fieldType; t != null && t != typeof(object); t = t.BaseType)
            {
                if (!t.IsGenericType) continue;
                var def = t.GetGenericTypeDefinition();
                if (def != typeof(Dictionary<,>) && !typeof(SerializedDictionaryBase).IsAssignableFrom(t)) continue;
                var a = t.GetGenericArguments();
                if (a.Length == 2) return a[half];
            }
            return null;
        }

        /// <summary>
        /// True when <paramref name="prop"/> is one of this package's own <c>SerializedDictionary</c> fields.
        /// Cheap shape test first (Generic with an `entries` array child) so the reflection only runs for the
        /// handful of properties that could possibly match; the TYPE test is what actually decides, because a
        /// plain struct that happens to serialize a list called `entries` is not a dictionary.
        /// </summary>
        internal static bool IsSerializedDictionaryProperty(SerializedProperty prop)
        {
            if (prop == null || prop.propertyType != SerializedPropertyType.Generic) return false;
            var entries = prop.FindPropertyRelative("entries");
            if (entries == null || !entries.isArray) return false;

            var target = prop.serializedObject != null ? prop.serializedObject.targetObject : null;
            if (target == null) return false;
            // THE RESOLVED TYPE, NOT THE FIELD'S. For an element path like `tables.Array.data[0]` the
            // resolved FieldInfo is the LIST field, whose FieldType is List<SerializedDictionary<K,V>> and is
            // not assignable to SerializedDictionaryBase — so a dictionary living inside a List never routed
            // here, and its node's Dictionary Style / Row Frame / Frame Color were dead controls (the table
            // still drew, via the CustomPropertyDrawer, just never with the styling the designer offered).
            // The resolver already computes the element type; it was simply being discarded with `out _`.
            var field = GetFieldInfoFromPropertyPath(target.GetType(), prop.propertyPath, out var resolvedType);
            var candidate = resolvedType ?? field?.FieldType;
            return field != null && candidate != null && typeof(SerializedDictionaryBase).IsAssignableFrom(candidate);
        }

        /// <summary>
        /// Honours Unity's own <c>[DictionaryDisplay(keyLabel, valueLabel, keyColumnFraction)]</c> on the bound
        /// field, so choosing the Sasquatch table never silently discards authoring the user already did in
        /// Unity's vocabulary. <c>layout</c> is deliberately ignored: its two one-column modes are shapes this
        /// table does not have, and quietly rendering two columns for a field that asked for one would be worse
        /// than plainly always being the two-column table this display type advertises.
        /// </summary>
        private static void ApplyUnityDictionaryDisplayAttribute(SerializedProperty prop, SerializedDictionaryDrawer.TableSpec spec)
        {
#if UNITY_6000_6_OR_NEWER
            var target = prop.serializedObject != null ? prop.serializedObject.targetObject : null;
            if (target == null) return;

            var field = GetFieldInfoFromPropertyPath(target.GetType(), prop.propertyPath, out var resolvedType);
            if (field == null) return;

            // TWO ATTRIBUTES, ONE JOB, and only the field-scoped one was ever read. Unity 6.6 ships a second,
            // ASSEMBLY-scoped registration for exactly the same four settings:
            //
            //   [assembly: DictionaryDisplayForType(typeof(Dictionary<string, LootEntry>), keyLabel = "Item")]
            //
            // It is how a project sets one look for every field of a given dictionary shape without
            // annotating each one. Unity's own view honours it under Default; ignoring it here meant a user's
            // authoring vanished the moment they switched that field to Dictionary Table -- the exact
            // opposite of the "does not fight Unity" promise in this file's header.
            //
            // MEASURED against a real 6000.6.0f1 editor, not read off a page: the attribute derives from
            // DictionaryDisplayAttribute, is AttributeTargets.Assembly with AllowMultiple, and its targetType
            // is keyed on the EXACT CLOSED Dictionary<K,V> (Unity's own registry is a Dictionary<Type, ...>
            // with that key, and analyzer UAC1021 rejects anything else at compile time). The FIELD attribute
            // is the more specific of the two and wins, which is also the order Unity resolves them in.
            var attr = Attribute.GetCustomAttribute(field, typeof(UnityEngine.DictionaryDisplayAttribute))
                       as UnityEngine.DictionaryDisplayAttribute
                       ?? FindAssemblyDictionaryDisplay(resolvedType ?? field.FieldType);
            if (attr == null) return;

            if (!string.IsNullOrEmpty(attr.keyLabel)) spec.KeyLabel = attr.keyLabel;
            if (!string.IsNullOrEmpty(attr.valueLabel)) spec.ValueLabel = attr.valueLabel;
            // UNITY'S OWN RANGE, 0.01–0.99. Clamping to a range 15x narrower at the low end made the two
            // views of the SAME field disagree on width for any legal value outside it: a
            // [DictionaryDisplay(keyColumnFraction = 0.1f)] gave a 10% key column under Default (Unity's own
            // view) and a 15% one under Dictionary Table, with nothing saying why. Nothing was bought by the
            // narrower clamp either -- the 56px CellMinWidth already stops either column collapsing, and the
            // `> 0f` guard already rejects the unset case.
            if (attr.keyColumnFraction > 0f)
                spec.KeyColumnFraction = UnityEngine.Mathf.Clamp(attr.keyColumnFraction, 0.01f, 0.99f);
#endif
        }

#if UNITY_6000_6_OR_NEWER
        /// <summary>
        /// The assembly-scoped <c>[DictionaryDisplayForType(typeof(Dictionary&lt;K,V&gt;), …)]</c> registrations,
        /// keyed by the closed dictionary type they name. Built once per domain reload: an assembly-level
        /// attribute cannot appear without a recompile, and a domain reload clears this static, so there is
        /// nothing to invalidate. Scanning ~200 assemblies per rendered field would not be affordable; doing
        /// it once, lazily, on the first native dictionary drawn, is.
        /// </summary>
        private static Dictionary<Type, UnityEngine.DictionaryDisplayForTypeAttribute> s_AssemblyDictionaryDisplay;

        /// <summary>
        /// The registration for <paramref name="dictionaryType"/>, or null. Matches on the EXACT closed type,
        /// which is what <c>targetType</c> means (analyzer UAC1021 refuses anything that is not a constructed
        /// <c>Dictionary&lt;TKey, TValue&gt;</c>).
        /// <para>
        /// Deliberately does NOT re-check the ownership rule Unity's analyzer enforces (UAC1022: the attribute
        /// must live in the assembly declaring the key or value type). That rule is a COMPILE ERROR, so a
        /// violating registration cannot exist in a project built from source at all; re-deriving it here
        /// would only risk rejecting a VALID one over a subtlety in "or any type nested within either", and
        /// silently dropping a user's authoring is the failure this whole method exists to stop.
        /// </para>
        /// </summary>
        private static UnityEngine.DictionaryDisplayForTypeAttribute FindAssemblyDictionaryDisplay(Type dictionaryType)
        {
            if (dictionaryType == null) return null;

            if (s_AssemblyDictionaryDisplay == null)
            {
                s_AssemblyDictionaryDisplay = new Dictionary<Type, UnityEngine.DictionaryDisplayForTypeAttribute>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    object[] found;
                    // A dynamic or unloadable assembly throws rather than answering; "no registrations" is
                    // the right reading for one we cannot read at all.
                    try { found = asm.GetCustomAttributes(typeof(UnityEngine.DictionaryDisplayForTypeAttribute), false); }
                    catch { continue; }
                    foreach (var o in found)
                    {
                        var a = o as UnityEngine.DictionaryDisplayForTypeAttribute;
                        if (a == null || a.targetType == null) continue;
                        // FIRST wins. AllowMultiple is true and two assemblies could name the same closed
                        // type; Unity's own registry is a Dictionary keyed the same way, so exactly one of
                        // them takes effect there too, and quietly picking a different one than Unity did
                        // would be a new disagreement rather than a fix.
                        if (!s_AssemblyDictionaryDisplay.ContainsKey(a.targetType))
                            s_AssemblyDictionaryDisplay[a.targetType] = a;
                    }
                }
            }

            return s_AssemblyDictionaryDisplay.TryGetValue(dictionaryType, out var hit) ? hit : null;
        }
#endif

        /// <summary>
        /// Points the table's duplicate/empty-key marks at Unity's own
        /// <c>SerializedProperty.GetDictionaryIgnoredEntries()</c> — the exact rows its runtime discards. This
        /// is strictly better than the token-counting fallback: it needs no per-key-type case analysis, so it
        /// covers struct and class keys the fallback cannot tokenise, and it cannot disagree with the runtime.
        /// </summary>
        private static void AttachIgnoredEntrySource(SerializedProperty prop, SerializedDictionaryDrawer.TableSpec spec)
        {
#if UNITY_6000_6_OR_NEWER
            // NOT INSTALLED for a multi-object selection: GetDictionaryIgnoredEntries() throws
            // InvalidOperationException on a multi-target SerializedObject. Bailing HERE rather than inside
            // the closures is load-bearing — leaving the delegates in place and having them return empty
            // would make the table think it HAS an authoritative source (DuplicateIndices != null) and skip
            // the KeyToken fallback entirely, so the dictionary would silently lose duplicate detection
            // rather than degrade to the pre-6.6 answer. The native path already refuses to build a table at
            // all under multi-edit; this keeps the drawer safe if it is ever reached another way.
            if (prop.serializedObject == null || prop.serializedObject.isEditingMultipleObjects) return;

            // Re-resolved on every recompute rather than captured once: the table recomputes after every key
            // edit, add, delete and reorder, and a stale snapshot would leave the marks a frame behind.
            // Wrapped because the property can die with its inspector between recomputes (a destroyed target,
            // a domain reload mid-edit) — a mark that goes missing for a frame is a far smaller failure than
            // an exception escaping into the inspector build.
            spec.DuplicateIndices = () =>
            {
                try
                {
                    var p = prop.serializedObject.FindProperty(prop.propertyPath);
                    return p == null ? Array.Empty<int>() : p.GetDictionaryIgnoredEntries().duplicateEntryIndices;
                }
                catch { return Array.Empty<int>(); }
            };
            spec.NullKeyIndices = () =>
            {
                try
                {
                    var p = prop.serializedObject.FindProperty(prop.propertyPath);
                    return p == null ? Array.Empty<int>() : p.GetDictionaryIgnoredEntries().nullKeyEntryIndices;
                }
                catch { return Array.Empty<int>(); }
            };
#endif
        }

        /// <summary>
        /// True when this node should render the native-dictionary table: the kind is explicitly chosen AND
        /// the property really is a serialized dictionary. Kept next to the creator so the guard and the
        /// builder cannot drift apart.
        /// </summary>
        internal static bool IsNativeDictionaryProperty(SerializedProperty prop)
        {
            return prop != null
                && prop.isArray
                && prop.propertyType != SerializedPropertyType.String
                && IsDictionaryField(prop);
        }

        private static bool IsDictionaryField(SerializedProperty prop)
        {
            // A DEAD SerializedObject is non-null but THROWS on .targetObject — see
            // serializedobject-outlives-its-inspector. This predicate is now on the collection build path
            // (LayoutTreeBuilder's two dead-collection gates call it), which the old bare `!prop.isArray`
            // test never touched, so it must not be the thing that turns a stale property into an exception.
            // "Not a dictionary" is the right answer for a property that can no longer be read at all: the
            // caller then treats it as a dead binding, which is exactly what it is.
            UnityEngine.Object target;
            try { target = prop.serializedObject != null ? prop.serializedObject.targetObject : null; }
            catch { return false; }
            if (target == null) return false;

            var field = GetFieldInfoFromPropertyPath(target.GetType(), prop.propertyPath, out _);
            var t = field != null ? field.FieldType : null;
            return t != null && t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>);
        }
    }
}
