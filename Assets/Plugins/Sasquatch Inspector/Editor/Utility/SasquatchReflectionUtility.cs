using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Reflection helpers for discovering serializable/bindable members of a type and detecting custom
    /// property drawers — the data behind the designer's field list and the auto-sync engine.
    /// </summary>
    public static class SasquatchReflectionUtility
    {
        /// <summary>A discovered serializable member: its property path, declared type, whether it's a
        /// collection, and whether it (or its elements) is a <c>[SerializeReference]</c> managed reference.</summary>
        public struct ReflectedProperty
        {
            public string path;
            public Type type;
            public bool isCollection;
            /// <summary>The FIELD carries <c>[SerializeReference]</c> — a single polymorphic value, or a
            /// collection whose every element is a TYPE PICKER rather than a value. Recorded here because
            /// the discovery walk is the only place the FieldInfo is in hand; consumers that decide what a
            /// row is made of (element templates, column derivation) otherwise have to re-reflect for it.</summary>
            public bool isManagedReference;
        }

        /// <summary>All Unity-serializable members of <paramref name="type"/> (recursively, including nested struct fields).</summary>
        public static List<ReflectedProperty> GetSerializableProperties(Type type)
        {
            var results = new List<ReflectedProperty>();
            if (type == null) return results;

            var visited = new HashSet<Type>();
            GetPropertiesRecursive(type, "", results, visited);
            return results;
        }

        /// <summary>The top-level bindable members of <paramref name="type"/> in declaration order — leaves,
        /// nested-struct leaves (e.g. <c>baseStats.range</c>), and collections, but NOT a collection's element
        /// sub-fields (e.g. <c>modifications.modName</c>): those aren't bindable at the root, they belong in the
        /// collection's item-layout template. Shared by the designer's Bulk Add and the auto-sync engine.</summary>
        // Type -> the computed top-level list. A Type's fields cannot change within one domain, so the only
        // event that can invalidate this is the reload that retires the Type itself. Measured at 58.4 us per
        // REPEAT call on an identical Type — 5.8-6.6 ms for the 71 types every header sweep walks.
        private static readonly Dictionary<Type, List<ReflectedProperty>> _topLevelBindableMemo = new();

        [UnityEditor.InitializeOnLoadMethod]
        private static void HookBindableMemoInvalidation()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () => _topLevelBindableMemo.Clear();
        }

        public static List<ReflectedProperty> GetTopLevelBindableProperties(Type type)
        {
            // A DEFENSIVE COPY, always. Callers have received a freshly built, freely mutable List here
            // since this method existed, and at least one designer path sorts the result in place — handing
            // out the memo itself would let that sort reorder every future caller's field list, including
            // the auto-sync ledger's, where order is meaning rather than presentation. ReflectedProperty is
            // a struct, so copying the list copies the entries too.
            if (type != null && _topLevelBindableMemo.TryGetValue(type, out var memo))
                return new List<ReflectedProperty>(memo);

            var computed = ComputeTopLevelBindableProperties(type);
            if (type != null) _topLevelBindableMemo[type] = computed;
            return new List<ReflectedProperty>(computed);
        }

        private static List<ReflectedProperty> ComputeTopLevelBindableProperties(Type type)
        {
            var all = GetSerializableProperties(type);
            var collectionPrefixes = new List<string>();
            foreach (var p in all)
            {
                if (p.isCollection) collectionPrefixes.Add(p.path + ".");
            }

            var result = new List<ReflectedProperty>();
            foreach (var p in all)
            {
                if (p.isCollection) { result.Add(p); continue; }
                bool insideCollection = false;
                foreach (var prefix in collectionPrefixes)
                {
                    if (p.path.StartsWith(prefix)) { insideCollection = true; break; }
                }
                if (!insideCollection) result.Add(p);
            }
            return result;
        }

        // Hard depth backstop. The `visited` set below skips Unity built-in types, so a (pathological, not-known-
        // to-exist) cyclic type routed THROUGH a UnityEngine-namespaced non-leaf could evade it. This cap
        // guarantees termination regardless. Real inspected graphs are shallow; 12 is far beyond any legitimate
        // field nesting.
        private const int MaxDiscoveryDepth = 12;

        private static void GetPropertiesRecursive(Type type, string prefix, List<ReflectedProperty> results, HashSet<Type> visited, int depth = 0)
        {
            if (type == null || visited.Contains(type) || depth > MaxDiscoveryDepth) return;

            // Prevent infinite recursion on custom serializable structs/classes
            if (!IsUnityBuiltInType(type) && !type.IsPrimitive && type != typeof(string))
            {
                visited.Add(type);
            }

            // Enumerate fields on this type and its base types (excluding UnityEngine.Object base fields).
            // BASE-FIRST, DECLARED-ONLY: GetFields without DeclaredOnly also returns inherited PUBLIC fields,
            // which (combined with the base walk) emitted every inherited public field TWICE — and derived-
            // before-base put inherited fields at the BOTTOM. Unity's inspector lists base-class fields first
            // and once; walking the reversed chain with DeclaredOnly matches that exactly (the walk itself
            // exists to reach inherited PRIVATE [SerializeField] fields, which GetFields never inherits).
            var chain = new List<Type>();
            var currentType = type;
            while (currentType != null && currentType != typeof(object) && currentType != typeof(MonoBehaviour) && currentType != typeof(ScriptableObject) && currentType != typeof(Component))
            {
                chain.Add(currentType);
                currentType = currentType.BaseType;
            }
            chain.Reverse();

            foreach (var declaringType in chain)
            {
                var fields = declaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var field in fields)
                {
                    if (!IsSerializableField(field)) continue;

                    string path = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";
                    Type fieldType = field.FieldType;

                    bool isCollection = IsCollectionType(fieldType, out Type elementType);

                    // A [SerializeReference] field is a MANAGED reference — a polymorphic value whose concrete
                    // type is chosen at edit time. It (or each list element) is a SINGLE picker binding; we must
                    // never drill into the declared interface/abstract base's fields (an interface has none, and
                    // an abstract base's fields aren't the real instance's). Without this, interface-typed fields
                    // (Type.IsClass is false for interfaces) fell through every branch and were dropped entirely.
                    bool isManagedRef = field.GetCustomAttribute<SerializeReference>() != null;

                    if (isCollection)
                    {
                        results.Add(new ReflectedProperty
                        {
                            path = path,
                            type = fieldType,
                            isCollection = true,
                            isManagedReference = isManagedRef
                        });

                        // Don't drill into element types that render as a single field via their own
                        // PropertyDrawer (e.g. SceneReference) — they're one binding, not a bag of sub-fields.
                        // Managed-reference (polymorphic) elements are likewise single bindings, not drilled.
                        // UnityEvents render via the custom UnityEvent widget — also one binding, never drilled.
                        if (!isManagedRef && elementType != null && !IsLeafType(elementType) && !TypeHasCustomDrawer(elementType) && !IsUnityEventType(elementType) && (elementType.IsClass || (elementType.IsValueType && !elementType.IsEnum && !elementType.IsPrimitive)))
                        {
                            GetPropertiesRecursive(elementType, path, results, new HashSet<Type>(visited), depth + 1);
                        }
                    }
                    else if (isManagedRef)
                    {
                        // Single polymorphic field → one binding rendered by the type picker (the widget
                        // auto-detects the ManagedReference at build time, even under DisplayType.Default).
                        results.Add(new ReflectedProperty
                        {
                            path = path,
                            type = fieldType,
                            isCollection = false,
                            isManagedReference = true
                        });
                    }
                    else if (IsLeafType(fieldType))
                    {
                        results.Add(new ReflectedProperty
                        {
                            path = path,
                            type = fieldType,
                            isCollection = false
                        });
                    }
                    else if (TypeHasCustomDrawer(fieldType))
                    {
                        // A type with its own PropertyDrawer (e.g. SceneReference) renders as ONE field — the
                        // drawer owns its internals — so expose it as a single binding and don't drill into its
                        // serialized sub-fields (scenePath / sceneGuid / sceneAsset).
                        results.Add(new ReflectedProperty
                        {
                            path = path,
                            type = fieldType,
                            isCollection = false
                        });
                    }
                    else if (IsPlainUnserializableCollection(fieldType))
                    {
                        // Unity can't serialize a plain Dictionary/SortedDictionary/HashSet/SortedSet/…, so no
                        // SerializedProperty will ever exist. Without this branch the field only survives via the
                        // future-proof net below (recursion finds zero serializable members). Surface it
                        // DELIBERATELY as a single Default binding so the designer / Bulk Add / auto-sync keep
                        // offering it — at render time BuildNode swaps in the conversion guidance card.
                        results.Add(new ReflectedProperty
                        {
                            path = path,
                            type = fieldType,
                            isCollection = false
                        });
                    }
                    else if (IsUnityEventType(fieldType))
                    {
                        // A UnityEvent / UnityEvent<T> renders as ONE binding via the custom UnityEvent widget
                        // (UnityEventWidget owns its internals). Drilling in would bind raw m_PersistentCalls
                        // sub-fields (m_Target / m_MethodName / m_Mode / ...) and the widget would never run.
                        results.Add(new ReflectedProperty
                        {
                            path = path,
                            type = fieldType,
                            isCollection = false
                        });
                    }
                    else if (fieldType.IsClass || (fieldType.IsValueType && !fieldType.IsEnum && !fieldType.IsPrimitive))
                    {
                        // Recurse into a nested serializable object/struct. GENERIC FUTURE-PROOF NET:
                        // if the recursion surfaces NO bindable sub-fields, the type serializes via
                        // native private fields — a Unity built-in atomic type not yet in the IsLeafType
                        // allow-list, or one Unity adds in a FUTURE version. Rather than let the field
                        // VANISH from discovery, bind it as a single Default field; Unity's own
                        // PropertyField then draws it exactly like the stock inspector would. (Types WITH
                        // real serializable members — the user's own structs — still drill in as before,
                        // because their recursion adds sub-fields and this fallback never fires.)
                        int countBeforeRecurse = results.Count;
                        GetPropertiesRecursive(fieldType, path, results, new HashSet<Type>(visited), depth + 1);
                        if (results.Count == countBeforeRecurse)
                        {
                            results.Add(new ReflectedProperty
                            {
                                path = path,
                                type = fieldType,
                                isCollection = false
                            });
                        }
                    }
                }
            }
        }

        /// <summary>
        /// True for a plain BCL key/value collection Unity can never serialize (<c>Dictionary&lt;,&gt;</c> /
        /// <c>SortedDictionary&lt;,&gt;</c> / <c>SortedList&lt;,&gt;</c>) — no SerializedProperty will ever exist,
        /// so the field can't be drawn or saved. All have a &lt;K, V&gt; shape, so the render-time guidance card
        /// can offer a <c>SerializedDictionary&lt;K, V&gt;</c> swap. Sets are handled by
        /// <see cref="IsPlainUnserializableCollection"/> (which can't offer a K/V swap).
        /// </summary>
        public static bool IsPlainUnserializableDictionary(Type type)
            => ResolveUnserializableDictionaryBase(type) != null;

        /// <summary>
        /// The closed <c>Dictionary&lt;K,V&gt;</c> / <c>SortedDictionary&lt;K,V&gt;</c> /
        /// <c>SortedList&lt;K,V&gt;</c> in <paramref name="type"/>'s own hierarchy — <paramref name="type"/>
        /// itself when it IS one, its base when it derives from one, or null.
        /// <para>
        /// The base walk is what makes the common <c>[Serializable] class LootTable : Dictionary&lt;string,int&gt; {}</c>
        /// idiom visible. A subclass of a closed generic is itself NON-generic, so an opening
        /// <c>!type.IsGenericType</c> test rejected it instantly and the field got the bare red "Missing
        /// property" placeholder on every Unity version — no conversion card, no explanation — even though it
        /// is exactly the shape the card was written for.
        /// </para>
        /// <para>
        /// Returning the resolved BASE, not just a bool, is load-bearing: the card's copyable declaration
        /// reads <c>GetGenericArguments()</c> for K and V, and a subclass has none of its own.
        /// </para>
        /// <para>
        /// Deliberately NOT used by <see cref="IsNativelySerializableDictionaryType"/>, which stays an exact
        /// match: Unity 6.6 serializes <c>Dictionary&lt;K,V&gt;</c>, and a subclass of it is a different type
        /// the backend does not take — so a subclass keeps the "swap for SerializedDictionary" advice on 6.6
        /// too, which is the true answer for it.
        /// </para>
        /// </summary>
        internal static Type ResolveUnserializableDictionaryBase(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (!t.IsGenericType) continue;
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(Dictionary<,>)
                    || def == typeof(SortedDictionary<,>)
                    || def == typeof(SortedList<,>)) return t;
            }
            return null;
        }

        /// <summary>
        /// The set twin of <see cref="ResolveUnserializableDictionaryBase"/> — the closed
        /// <c>HashSet&lt;T&gt;</c> / <c>SortedSet&lt;T&gt;</c> in the hierarchy, or null. Same base walk, same
        /// reason: <c>class Tags : HashSet&lt;string&gt; {}</c> is the identical idiom.
        /// </summary>
        private static Type ResolveUnserializableSetBase(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (!t.IsGenericType) continue;
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(HashSet<>) || def == typeof(SortedSet<>)) return t;
            }
            return null;
        }

        /// <summary>
        /// True when the editor running us serializes <c>Dictionary&lt;K, V&gt;</c> natively — Unity 6.6 and
        /// newer. Verified 2026-09-02 against a real 6000.6.0f1 editor: the property comes through as
        /// <c>SerializedPropertyType.Generic</c> with <c>isArray == true</c> and
        /// <c>&lt;path&gt;.Array.data[i].key</c> / <c>.value</c> children, and Unity's own
        /// <c>DictionaryView</c> draws it.
        /// <para>
        /// This is deliberately about the TYPE only. Whether a given FIELD actually serializes is a separate
        /// question: Unity's dictionary backend requires an explicit <c>[SerializeField]</c> even on a PUBLIC
        /// field (analyzer UAC1015) — the one place Unity breaks its own "public fields serialize" rule. That
        /// is why <see cref="IsPlainUnserializableDictionary"/> stays true for <c>Dictionary&lt;,&gt;</c>:
        /// a field with no <c>[SerializeField]</c> still has no SerializedProperty and still needs the
        /// guidance card — only the card's ADVICE changes.
        /// </para>
        /// <para>
        /// <c>SortedDictionary&lt;,&gt;</c>, <c>SortedList&lt;,&gt;</c>, <c>HashSet&lt;&gt;</c> and
        /// <c>SortedSet&lt;&gt;</c> are NOT covered by Unity's support on any version.
        /// </para>
        /// </summary>
        public static bool IsNativelySerializableDictionaryType(Type type)
        {
#if UNITY_6000_6_OR_NEWER
            return type != null
                && type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
#else
            return false;
#endif
        }

        /// <summary>
        /// True for ANY plain BCL collection Unity can never serialize — the key/value types in
        /// <see cref="IsPlainUnserializableDictionary"/> PLUS sets (<c>HashSet&lt;&gt;</c> / <c>SortedSet&lt;&gt;</c>).
        /// Used to keep the field discoverable and, at render time, swap in a guidance card instead of a red
        /// "missing property" placeholder.
        /// </summary>
        public static bool IsPlainUnserializableCollection(Type type)
        {
            if (type == null) return false;
            return ResolveUnserializableDictionaryBase(type) != null
                || ResolveUnserializableSetBase(type) != null;
        }

        // The sync engine's liveness check shares this exact gate — a bound field that stops passing it
        // (public → private, [SerializeField] removed, [NonSerialized] added) has lost its
        // SerializedProperty and must be treated like a deleted field.
        internal static bool IsSerializableField(FieldInfo field)
        {
            if (field.IsInitOnly) return false; // readonly
            if (field.GetCustomAttribute<System.NonSerializedAttribute>() != null) return false;

            bool hasSerializeField = field.GetCustomAttribute<SerializeField>() != null;
            // [SerializeReference] alone is sufficient for Unity to serialize a private field — it implies
            // serialization, storing the value as a managed reference. Without this, the common polymorphic
            // idiom `[SerializeReference] private IFoo foo;` (no [SerializeField]) was filtered out here, so
            // no binding was ever created for it (import / Bulk Add / auto-sync all start from this gate).
            bool hasSerializeReference = field.GetCustomAttribute<SerializeReference>() != null;
            bool isPublic = field.IsPublic;

            return isPublic || hasSerializeField || hasSerializeReference;
        }

        internal static bool IsCollectionType(Type type, out Type elementType)
        {
            elementType = null;
            if (type == typeof(string)) return false;

            if (type.IsArray)
            {
                elementType = type.GetElementType();
                return true;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }

            return false;
        }

        internal static bool IsLeafType(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(char))
                return true;

            // Common Unity leaf types — every value type Unity draws as ONE atomic field.
            // The flagged built-ins below MUST be listed explicitly or reflection mishandles them:
            //  • Color32 / Quaternion expose PUBLIC component fields (r,g,b,a / x,y,z,w) → the
            //    nested-struct branch would EXPLODE each into 4 separate sub-bindings, not one field.
            //  • RectInt / BoundsInt / Hash128 / PropertyName / RenderingLayerMask serialize via
            //    NATIVE private fields (no [SerializeField]) → reflection finds ZERO members, so the
            //    field would vanish from discovery entirely even though Unity draws it fine (a
            //    REGRESSION vs stock Unity — the designer couldn't bind a field the user can see).
            //  • Quaternion is only accidentally rescued when an SRP package is installed (its
            //    QuaternionPropertyDrawer ships in Unity.RenderPipelines.Core.Editor); listing it
            //    here makes discovery render-pipeline-independent.
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) ||
                type == typeof(Vector2Int) || type == typeof(Vector3Int) ||
                type == typeof(Color) || type == typeof(Color32) ||
                type == typeof(Rect) || type == typeof(RectInt) ||
                type == typeof(Bounds) || type == typeof(BoundsInt) ||
                type == typeof(Quaternion) ||
                type == typeof(AnimationCurve) || type == typeof(Gradient) ||
                type == typeof(LayerMask) || type == typeof(RenderingLayerMask) ||
                type == typeof(Hash128) || type == typeof(PropertyName) ||
                typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                return true;
            }

            // ExposedReference<T> renders as one field via its own drawer — treat it as a leaf so
            // discovery never drills into its defaultValue / exposedName members.
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ExposedReference<>))
                return true;

            return false;
        }

        /// <summary>
        /// True when a collection's ELEMENT draws as ONE atomic field and therefore has no sub-field a
        /// binding could point at — a <c>Vector2</c>, a float, an enum, a Color, an object reference, a
        /// custom-drawer type, a UnityEvent, or a struct/class whose only members are collections.
        /// <para>
        /// This is the gate for the ELEMENT TEMPLATE: such a list can only be styled by binding the element
        /// itself (a <see cref="Runtime.BindingNode"/> with an empty <c>PropertyPath</c> in the collection's
        /// item layout), because there is nothing else to bind. An element WITH serializable sub-fields is
        /// deliberately excluded — the designer already lists those as bindable tokens, and offering a
        /// whole-element binding beside them would be two ways to say the same thing.
        /// </para>
        /// Deliberately type-only: <c>[SerializeReference]</c> is a property of the FIELD, and a managed
        /// reference is a type picker rather than a value, so call sites exclude it separately.
        /// </summary>
        public static bool IsAtomicElementType(Type elementType)
        {
            if (elementType == null) return false;
            // Primitives, enums, strings, Vector*/Color/Rect/Bounds/Curve/Gradient, object references.
            if (IsLeafType(elementType)) return true;
            // One binding drawn by the UnityEvent widget — drilling in would bind raw m_PersistentCalls.
            if (IsUnityEventType(elementType)) return true;
            // The drawer owns its internals, so its serialized members are not bindable fields.
            if (TypeHasCustomDrawer(elementType)) return true;
            // Neither a class nor a plain struct: nothing to recurse into.
            if (!(elementType.IsClass || (elementType.IsValueType && !elementType.IsEnum && !elementType.IsPrimitive)))
                return true;
            // A struct/class IS drillable — but only counts as non-atomic if the drill actually yields a
            // bindable DIRECT leaf. Mirrors the column derivation's filter exactly (sub-collections and
            // nested-struct sub-fields are not v1 columns), so "no columns derivable" and "atomic" can
            // never disagree and leave a list with neither columns nor an element template.
            foreach (var p in GetSerializableProperties(elementType))
            {
                if (p.isCollection || p.path.Contains('.')) continue;
                return false;
            }
            return true;
        }

        private static bool IsUnityBuiltInType(Type type)
        {
            return type.Namespace != null && (type.Namespace.StartsWith("UnityEngine") || type.Namespace.StartsWith("UnityEditor"));
        }

        // A UnityEvent / UnityEvent<T> field: rendered as ONE binding by the custom UnityEvent widget, so
        // field-discovery must treat it as a single bindable property and never drill into m_PersistentCalls.
        internal static bool IsUnityEventType(Type type)
        {
            return type != null && typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(type);
        }

        private static HashSet<Type> _drawerTargetTypes;            // exact-match: every type-targeted drawer's target
        private static HashSet<Type> _drawerTargetTypesForChildren; // subset declared useForChildren = true
        private static HashSet<Type> _drawerTargetAttributes;       // attribute-targeted drawers' attribute types
        private static bool _drawersCached = false;

        /// <summary>
        /// Test seam: the three drawer tables, so the TypeCache-based scan can be compared against a verbatim
        /// full-assembly sweep. Returns the live sets — read only.
        /// </summary>
        internal static void GetDrawerTablesForTests(out HashSet<Type> targets,
                                                     out HashSet<Type> forChildren,
                                                     out HashSet<Type> attributes)
        {
            CacheCustomPropertyDrawers();
            targets = _drawerTargetTypes;
            forChildren = _drawerTargetTypesForChildren;
            attributes = _drawerTargetAttributes;
        }

        /// <summary>Test seam: re-runs the drawer scan from scratch, so its cost can be measured.</summary>
        internal static void RescanCustomPropertyDrawersForTests()
        {
            _drawersCached = false;
            CacheCustomPropertyDrawers();
        }

        private static void CacheCustomPropertyDrawers()
        {
            if (_drawersCached) return;
            _drawersCached = true;

            _drawerTargetTypes = new HashSet<Type>();
            _drawerTargetTypesForChildren = new HashSet<Type>();
            _drawerTargetAttributes = new HashSet<Type>();

            // TypeCache, not AppDomain.GetAssemblies() -> assembly.GetTypes(). The old sweep loaded and walked
            // the type table of EVERY assembly the filter below did not exclude — and that filter excludes
            // none of the ~100 Unity.* package assemblies, netstandard, Newtonsoft, or any third-party plugin.
            // It ran on every domain reload, which in this editor means every script compile and every
            // play-mode enter: dozens of times an hour, to find a handful of drawer types Unity has already
            // indexed for us. (The same file already uses TypeCache below, for exactly this reason.)
            //
            // The ASSEMBLY FILTER IS KEPT, and keeping it is not caution — it is correctness. TypeCache
            // returns Unity's own built-in PropertyDrawers too, and admitting those would add their target
            // types to _drawerTargetTypes, changing which fields TypeHasCustomDrawer reports as
            // custom-drawn. Same predicate, same set, no per-reload walk. TestCustomDrawerScanMatchesAnAssemblySweep
            // pins the two against each other.
            foreach (var type in UnityEditor.TypeCache.GetTypesDerivedFrom<UnityEditor.PropertyDrawer>())
            {
                if (type == null || type.Assembly == null) continue;
                string asmName = type.Assembly.GetName().Name;
                if (asmName.StartsWith("System") || asmName.StartsWith("Microsoft") ||
                    asmName.StartsWith("UnityEditor") || asmName.StartsWith("UnityEngine") ||
                    asmName.StartsWith("mscorlib"))
                {
                    continue;
                }

                try
                {
                    // Read the drawer's target type AND useForChildren flag from the attribute's
                    // constructor arguments via BCL metadata (CustomAttributeData) — this reads the metadata
                    // without ever instantiating the attribute. Both [CustomPropertyDrawer(typeof(X))] and
                    // [CustomPropertyDrawer(typeof(X), useForChildren)] record typeof(X) as ctor arg 0 and the
                    // flag as ctor arg 1. Matches how Unity resolves drawers: the exact target type always owns
                    // its rendering; a base/interface target additionally owns its subtypes only when
                    // useForChildren is true.
                    foreach (var data in type.GetCustomAttributesData())
                    {
                        if (data.AttributeType != typeof(UnityEditor.CustomPropertyDrawer)) continue;
                        var ctorArgs = data.ConstructorArguments;
                        if (ctorArgs.Count == 0) continue;

                        Type targetType = ctorArgs[0].Value as Type;
                        if (targetType == null) continue;

                        bool useForChildren = ctorArgs.Count > 1 && ctorArgs[1].Value is bool b && b;

                        if (targetType.IsSubclassOf(typeof(PropertyAttribute)))
                        {
                            // Attribute-targeted: applies to any field carrying the attribute (useForChildren N/A).
                            _drawerTargetAttributes.Add(targetType);
                        }
                        else
                        {
                            _drawerTargetTypes.Add(targetType);
                            if (useForChildren) _drawerTargetTypesForChildren.Add(targetType);
                        }
                    }
                }
                catch
                {
                    // Ignore reflection errors on a single drawer type (a broken attribute reference), exactly
                    // as the per-assembly try did — one bad type must not empty the whole drawer table.
                }
            }
        }

        /// <summary>True if <paramref name="fieldType"/> has its own type-targeted CustomPropertyDrawer, so it
        /// should render as a single field (the drawer owns its internals) rather than being drilled into for
        /// sub-field bindings.</summary>
        /// <summary>
        /// A Sasquatch-owned type whose <c>PropertyDrawer</c> exists ONLY as the plain-inspector fallback,
        /// because the package's own widgets bind the type natively.
        ///
        /// <para>Anything reasoning "a drawer owns this field, so choosing our widget would REPLACE it" must
        /// exclude these — for them nothing is replaced and nothing is lost.
        /// <see cref="SasquatchString"/> is the case: <c>DisplayType.Text</c> binds its inner template
        /// (<c>BindingFactory.CreateTextField</c>), and its <c>{token}</c> resolution lives in the type's
        /// implicit string conversion, which no inspector decision can affect.</para>
        ///
        /// <para>Deliberately NOT filtered inside <see cref="TryGetCustomPropertyDrawer"/> itself: that result
        /// also drives the Default-field routing guard, which still needs the drawer to run.</para>
        /// </summary>
        public static bool IsNativelyBoundType(Type t) => t == typeof(SasquatchString);

        public static bool TypeHasCustomDrawer(Type fieldType)
        {
            if (fieldType == null) return false;
            CacheCustomPropertyDrawers();

            // Exact target: the type has its own type-targeted drawer (registered regardless of useForChildren).
            if (_drawerTargetTypes.Contains(fieldType)) return true;

            // Inherited: a base/interface drawer declared useForChildren = true also owns its subtypes, exactly as
            // Unity expands via TypeCache.GetTypesDerivedFrom(target) when that flag is set.
            foreach (var baseTarget in _drawerTargetTypesForChildren)
            {
                if (baseTarget.IsAssignableFrom(fieldType)) return true;
            }
            return false;
        }

        /// <summary>
        /// What owns a field's rendering when something other than Sasquatch does: either an attribute the
        /// developer put on the field, or a drawer registered against the field's own type.
        /// </summary>
        public struct CustomDrawerInfo
        {
            /// <summary>True when a <c>[SomeAttribute]</c> on the field matched; false when the field's TYPE has its own drawer.</summary>
            public bool isAttributeTargeted;
            /// <summary>The matched <see cref="PropertyAttribute"/> type, or the drawn field type.</summary>
            public Type source;

            /// <summary>How to name it to the user: <c>[ShowIf]</c> for an attribute, <c>SceneReference</c> for a type.</summary>
            public string DisplayName
            {
                get
                {
                    if (source == null) return string.Empty;
                    if (!isAttributeTargeted) return source.Name;
                    string name = source.Name;
                    // "ShowIfAttribute" reads as "[ShowIf]" — the way it is written in code.
                    if (name.EndsWith("Attribute", StringComparison.Ordinal) && name.Length > "Attribute".Length)
                        name = name.Substring(0, name.Length - "Attribute".Length);
                    return "[" + name + "]";
                }
            }
        }

        /// <summary>
        /// Resolves WHICH custom drawer owns <paramref name="field"/>, for surfacing to the user. Same matching
        /// as <see cref="HasCustomPropertyDrawer"/> — including its deliberate exclusion of drawers that ship
        /// inside Unity's own assemblies, because superseding THOSE is the entire product and warning about
        /// them would be pure noise. Only a developer's own (or a third party's) drawer is reported.
        /// </summary>
        public static bool TryGetCustomPropertyDrawer(FieldInfo field, out CustomDrawerInfo info)
        {
            info = default;
            if (field == null) return false;

            CacheCustomPropertyDrawers();

            // An attribute the developer wrote on the field wins the report: it is the thing they will
            // recognise, and it is the thing a widget choice silently discards.
            foreach (var attribute in field.GetCustomAttributes(true))
            {
                Type attributeType = attribute.GetType();
                if (_drawerTargetAttributes.Contains(attributeType))
                {
                    info = new CustomDrawerInfo { isAttributeTargeted = true, source = attributeType };
                    return true;
                }
            }

            Type fieldType = field.FieldType;
            if (IsCollectionType(fieldType, out Type elementType) && elementType != null)
            {
                fieldType = elementType;
            }

            if (TypeHasCustomDrawer(fieldType))
            {
                info = new CustomDrawerInfo { isAttributeTargeted = false, source = fieldType };
                return true;
            }

            return false;
        }

        /// <summary>True when the field at <paramref name="propertyPath"/> on <paramref name="targetObjectType"/> has a custom property drawer (type- or attribute-targeted).</summary>
        public static bool HasCustomPropertyDrawer(Type targetObjectType, string propertyPath)
        {
            if (targetObjectType == null || string.IsNullOrEmpty(propertyPath)) return false;

            FieldInfo field = GetFieldInfoRecursive(targetObjectType, propertyPath, out Type fieldOwnerType);
            return TryGetCustomPropertyDrawer(field, out _);
        }

        private static FieldInfo GetFieldInfoRecursive(Type type, string propertyPath, out Type ownerType)
        {
            ownerType = type;
            FieldInfo field = null;
            string[] parts = propertyPath.Split('.');

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];

                if (part == "Array")
                {
                    if (i + 1 < parts.Length && parts[i + 1].StartsWith("data["))
                    {
                        i++;
                        if (field != null)
                        {
                            if (IsCollectionType(field.FieldType, out Type elementType))
                            {
                                ownerType = elementType;
                            }
                        }
                        continue;
                    }
                }

                field = GetFieldRecursive(ownerType, part);
                if (field == null)
                {
                    return null;
                }

                ownerType = field.FieldType;
            }

            return field;
        }

        private static FieldInfo GetFieldRecursive(Type type, string fieldName)
        {
            var currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                var field = currentType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }
                currentType = currentType.BaseType;
            }
            return null;
        }

        /// <summary>A short, human-readable name for <paramref name="type"/> (generic arguments spelled out).</summary>
        public static string GetFriendlyTypeName(Type type)
        {
            if (type == null) return string.Empty;
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(long)) return "long";
            if (type == typeof(double)) return "double";
            if (type == typeof(short)) return "short";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(char)) return "char";
            if (type == typeof(decimal)) return "decimal";
            
            if (type.IsGenericType)
            {
                var genericType = type.GetGenericTypeDefinition();
                string cleanName = genericType.Name;
                int backtick = cleanName.IndexOf('`');
                if (backtick > 0)
                {
                    cleanName = cleanName.Substring(0, backtick);
                }
                var args = type.GetGenericArguments();
                var argNames = new string[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    argNames[i] = GetFriendlyTypeName(args[i]);
                }
                return $"{cleanName}<{string.Join(", ", argNames)}>";
            }

            if (type.IsArray)
            {
                return $"{GetFriendlyTypeName(type.GetElementType())}[]";
            }

            return type.Name;
        }

        /// <summary>True when the field at <paramref name="propertyPath"/> is a list or array.</summary>
        public static bool IsGenericCollection(Type targetObjectType, string propertyPath)
        {
            if (targetObjectType == null || string.IsNullOrEmpty(propertyPath)) return false;

            FieldInfo field = GetFieldInfoRecursive(targetObjectType, propertyPath, out Type fieldOwnerType);
            if (field == null) return false;

            Type fieldType = field.FieldType;
            if (IsCollectionType(fieldType, out Type elementType) && elementType != null)
            {
                return !IsLeafType(elementType);
            }
            return false;
        }

        /// <summary>
        /// Returns the concrete, instantiable types assignable to <paramref name="baseType"/> (an interface
        /// or abstract class), suitable as [SerializeReference] managed-reference values. Drives the
        /// polymorphic type picker. Filters out abstracts/interfaces, open generics, UnityEngine.Objects
        /// (managed references must be plain serializable classes), and types without an accessible
        /// parameterless constructor (also excludes structs, which can't be managed references). Sorted by
        /// friendly name for a stable, readable list.
        /// </summary>
        public static List<Type> GetAssignableConcreteTypes(Type baseType)
        {
            var result = new List<Type>();
            if (baseType == null) return result;

            // TypeCache covers interfaces AND abstract base classes (returns implementors / subclasses).
            foreach (var t in UnityEditor.TypeCache.GetTypesDerivedFrom(baseType))
            {
                if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (typeof(UnityEngine.Object).IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue; // also rules out structs
                result.Add(t);
            }

            result.Sort((a, b) => string.Compare(GetFriendlyTypeName(a), GetFriendlyTypeName(b),
                StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }
}
