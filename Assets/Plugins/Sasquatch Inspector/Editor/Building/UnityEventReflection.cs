using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Reflection helpers for the custom UnityEvent drawer: discover the event's dynamic argument types,
    /// enumerate target candidate objects, build the valid method/setter list (mirroring Unity's persistent-call
    /// rules), and invoke an event in edit mode for the Test button. Everything here is plain reflection; the
    /// persistent calls themselves are read/written via SerializedProperty in the widget.
    /// </summary>
    public static class UnityEventReflection
    {
        private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>
        /// Which section a candidate object belongs to in the function picker. The order of the values IS the
        /// section order: the user's own scripts come first (that's what people are reaching for), then the
        /// GameObject, then everything built-in.
        /// </summary>
        public enum GroupKind
        {
            ProjectScript = 0,
            GameObject = 1,
            Component = 2,
        }

        /// <summary>One selectable function: the object whose method is called, the method, and its arg mode.</summary>
        public struct MethodEntry
        {
            public UnityEngine.Object Target;
            public string MethodName;            // e.g. "AddForce" or "set_enabled"
            public PersistentListenerMode Mode;  // EventDefined (dynamic) / Void / Int / Float / String / Bool / Object
            public Type ArgType;                 // single static-arg type (null for Void / dynamic)
            public bool IsDynamic;
            public string Group;                 // component / type display name, disambiguated ("Audio Source #2")
            public string Display;               // "Rigidbody/Add Force (dynamic)" etc. (legacy flat-picker label)

            public Type ArgEnumType;             // the enum an int argument really means (null = a plain int)
            public Type DeclaringType;           // where the member is actually declared (may be a base type)
            public GroupKind Kind;               // which picker section this entry's object belongs to
            public int GroupIndex;               // index of the owning object in ResolveCandidateObjects order
            public bool IsProjectScript;         // the owning object is backed by a script under Assets/
            public bool IsInherited;             // a project script's member that it inherited from Unity
            public string MemberDisplay;         // "Set Active" — the member alone, no group prefix
            public string ArgLabel;              // "bool" / "float" / "dynamic" / "" for void
            public string Haystack;              // lowercased search corpus (raw + nicified + camel-split)
        }

        /// <summary>The dynamic argument types the event delivers (from its UnityEvent&lt;...&gt; base). Empty for plain UnityEvent.</summary>
        public static Type[] GetEventArgTypes(Type eventType)
        {
            var t = eventType;
            while (t != null && t != typeof(object))
            {
                if (t.IsGenericType)
                {
                    var def = t.GetGenericTypeDefinition();
                    if (def.FullName != null && def.FullName.StartsWith("UnityEngine.Events.UnityEvent`", StringComparison.Ordinal))
                        return t.GetGenericArguments();
                }
                t = t.BaseType;
            }
            return Type.EmptyTypes;
        }

        /// <summary>
        /// Whether a single arg type can be stored AND INVOKED as a UnityEvent persistent-call argument.
        ///
        /// ENUMS ARE DELIBERATELY EXCLUDED. They serialize fine — into m_IntArgument under mode Int — but they
        /// can never FIRE: mode Int is the only thing Unity's binder ever asks with, so it looks for
        /// <c>Method(int)</c> and an <c>Method(MyEnum)</c> overload never matches. Verified against Unity's own
        /// oracle: <c>UnityEventBase.GetValidMethodInfo(t, "SetTeam", [typeof(int)])</c> returns null while
        /// <c>[typeof(MyEnum)]</c> binds — and mode Int can only ever produce the former. Offering them yields
        /// listeners that look fully configured in the inspector and silently do nothing.
        /// Unity's supported persistent-argument set is int / float / string / bool / UnityEngine.Object.
        ///
        /// <para>Enum PICKING is still available, and fires — see <see cref="ResolveArgEnumType(Type,string)"/>.
        /// It works the only way it can: the method takes the <c>int</c> Unity will actually bind, and the card
        /// draws the enum above it.</para>
        /// </summary>
        public static bool IsPersistentArgType(Type t)
        {
            if (t == null) return false;
            if (t == typeof(int) || t == typeof(float) || t == typeof(string) || t == typeof(bool)) return true;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return true;
            return false;
        }

        /// <summary>The <c>PersistentListenerMode</c> for a static argument of type <paramref name="argType"/> (Void when null).</summary>
        public static PersistentListenerMode ModeForArg(Type argType)
        {
            if (argType == null) return PersistentListenerMode.Void;
            if (argType == typeof(int)) return PersistentListenerMode.Int;
            if (argType == typeof(float)) return PersistentListenerMode.Float;
            if (argType == typeof(string)) return PersistentListenerMode.String;
            if (argType == typeof(bool)) return PersistentListenerMode.Bool;
            if (typeof(UnityEngine.Object).IsAssignableFrom(argType)) return PersistentListenerMode.Object;
            return PersistentListenerMode.Void;
        }

        // ───────────────────────── enum-backed int arguments ─────────────────────────
        //
        // A UnityEvent can never carry an enum-TYPED argument (see IsPersistentArgType). What it can carry is
        // the int the enum is made of — which is exactly what the enum IS on disk, since mode Int stores it in
        // m_IntArgument either way. So the method takes the (int) Unity's binder will actually find, and the
        // card draws a dropdown over it. The bytes written are identical to Unity's own, which is why this
        // costs nothing: Unity's built-in drawer opens the same listener and shows the same number.
        //
        // Two ways to learn WHICH enum, in priority order:
        //   1. an [EnumArg(typeof(E))] declaration on the parameter, the method, or the property; then
        //   2. inference — a same-named overload taking a single enum, i.e. the one-line bridge
        //      `void SetTeam(int t) => SetTeam((Team)t);` that a developer writes anyway.
        // Two different enum overloads of one name are ambiguous and resolve to nothing (a plain int box),
        // because guessing between them would silently wire the wrong dropdown.

        private static readonly Dictionary<(Type, string), Type> ArgEnumCache = new Dictionary<(Type, string), Type>();

        /// <summary>The enum a saved call's int argument really means, or null for a plain int. See the block comment above.</summary>
        public static Type ResolveArgEnumType(UnityEngine.Object target, string methodName)
            => target == null ? null : ResolveArgEnumType(target.GetType(), methodName);

        /// <summary>
        /// The enum <paramref name="methodName"/>'s single int parameter really means on
        /// <paramref name="targetType"/>, or null when it is an honest int. Cached per domain — the answer is
        /// pure static reflection, and this runs on every card build.
        /// </summary>
        public static Type ResolveArgEnumType(Type targetType, string methodName)
        {
            if (targetType == null || string.IsNullOrEmpty(methodName)) return null;

            var key = (targetType, methodName);
            if (ArgEnumCache.TryGetValue(key, out var cached)) return cached;

            Type resolved = ResolveArgEnumTypeUncached(targetType, methodName);
            ArgEnumCache[key] = resolved;
            return resolved;
        }

        private static Type ResolveArgEnumTypeUncached(Type targetType, string methodName)
        {
            // The (int) overload is the ONLY signature mode Int can ever call, so it is also the only one whose
            // argument we are entitled to redraw. Asking for it by exact type means an enum overload of the
            // same name can never be picked up here by accident.
            var method = targetType.GetMethod(methodName, MemberFlags, null, new[] { typeof(int) }, null);
            if (method == null) return null;

            var declared = DeclaredEnumType(method);
            if (declared != null) return Storable(declared);

            Type inferred = null;
            foreach (var m in targetType.GetMethods(MemberFlags))
            {
                if (!string.Equals(m.Name, methodName, StringComparison.Ordinal)) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || !ps[0].ParameterType.IsEnum) continue;
                if (inferred != null && inferred != ps[0].ParameterType) return null; // ambiguous — stay an int
                inferred = ps[0].ParameterType;
            }
            return Storable(inferred);
        }

        /// <summary>
        /// An explicit <c>[EnumArg]</c> declaration on the parameter, the method, or — for a property setter,
        /// which serializes as <c>set_Foo</c> — the property, which is where anyone would naturally write it.
        /// </summary>
        private static Type DeclaredEnumType(MethodInfo method)
        {
            var ps = method.GetParameters();
            if (ps.Length == 1)
            {
                var fromParam = EnumTypeFromAttributes(ps[0].GetCustomAttributes(inherit: true));
                if (fromParam != null) return fromParam;
            }

            var fromMethod = EnumTypeFromAttributes(method.GetCustomAttributes(inherit: true));
            if (fromMethod != null) return fromMethod;

            if (method.IsSpecialName && method.Name.StartsWith("set_", StringComparison.Ordinal))
            {
                var prop = method.DeclaringType?.GetProperty(method.Name.Substring(4), MemberFlags);
                if (prop != null) return EnumTypeFromAttributes(prop.GetCustomAttributes(inherit: true));
            }
            return null;
        }

        /// <summary>
        /// The enum named by an <c>[EnumArg]</c> attribute among <paramref name="attributes"/>, matched by NAME
        /// and SHAPE rather than by type identity.
        ///
        /// That is what lets one rule serve three callers that cannot reference each other: the attribute this
        /// package ships, a hand-rolled one in a project that would rather not take the dependency, and the
        /// export kit's package-free twin, which by definition cannot see either. It is the same reasoning as
        /// the <c>[EnumButtons]</c> lookup in <c>UnboundFieldSync</c> — an attribute is a declaration of
        /// intent, and honoring the intent matters more than owning the type.
        /// </summary>
        private static Type EnumTypeFromAttributes(object[] attributes)
        {
            if (attributes == null) return null;
            foreach (var a in attributes)
            {
                if (a == null) continue;
                var at = a.GetType();
                if (at.Name != "EnumArgAttribute" && at.Name != "EnumArg") continue;

                foreach (var p in at.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (p.PropertyType == typeof(Type) && p.GetIndexParameters().Length == 0
                        && p.GetValue(a) is Type pt && pt.IsEnum)
                        return pt;

                foreach (var f in at.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (f.FieldType == typeof(Type) && f.GetValue(a) is Type ft && ft.IsEnum)
                        return ft;
            }
            return null;
        }

        /// <summary>
        /// The type back, or null when it cannot faithfully live in <c>m_IntArgument</c>. A long/ulong-backed
        /// enum is rejected on purpose: the argument field is a 32-bit int, so a big member would be silently
        /// truncated, and a dropdown that quietly stores the wrong value is worse than an honest number box.
        /// </summary>
        private static Type Storable(Type enumType)
        {
            if (enumType == null || !enumType.IsEnum) return null;
            var underlying = Enum.GetUnderlyingType(enumType);
            return underlying == typeof(long) || underlying == typeof(ulong) ? null : enumType;
        }

        /// <summary>Whether <paramref name="enumType"/> carries <c>[Flags]</c> — a mask field rather than a single pick.</summary>
        public static bool IsFlagsEnum(Type enumType) =>
            enumType != null && enumType.IsEnum && enumType.IsDefined(typeof(FlagsAttribute), inherit: false);

        /// <summary>
        /// A DEFINED member of <paramref name="enumType"/>, used to seed a freshly built EnumField. Constructing
        /// one straight from an undefined value is not a supported shape, so the real (possibly legacy) value is
        /// pushed in afterwards with SetValueWithoutNotify.
        /// </summary>
        public static Enum FirstEnumValue(Type enumType)
        {
            var values = Enum.GetValues(enumType);
            return values.Length > 0 ? (Enum)values.GetValue(0) : (Enum)Enum.ToObject(enumType, 0);
        }

        /// <summary>The stored int as a boxed enum value (undefined values pass through unchanged).</summary>
        public static Enum ToEnumValue(Type enumType, int stored) => (Enum)Enum.ToObject(enumType, stored);

        /// <summary>
        /// An enum value as the int <c>m_IntArgument</c> holds. uint-backed members above int.MaxValue would
        /// throw a checked convert, so those go across as their bit pattern — which round-trips exactly.
        /// </summary>
        public static int EnumToInt(Enum value)
        {
            if (value == null) return 0;
            return Enum.GetUnderlyingType(value.GetType()) == typeof(uint)
                ? unchecked((int)Convert.ToUInt32(value))
                : Convert.ToInt32(value);
        }

        /// <summary>
        /// Whether <paramref name="stored"/> names a real member. Any bit combination is legitimate for a
        /// <c>[Flags]</c> enum, so those are always true.
        /// </summary>
        public static bool IsDefinedEnumValue(Type enumType, int stored)
        {
            if (enumType == null || !enumType.IsEnum) return false;
            if (IsFlagsEnum(enumType)) return true;
            return Enum.IsDefined(enumType, Enum.ToObject(enumType, stored));
        }

        /// <summary>The objects whose methods can be targeted: a GameObject + its components, or the object itself.</summary>
        public static List<UnityEngine.Object> ResolveCandidateObjects(UnityEngine.Object target)
        {
            var list = new List<UnityEngine.Object>();
            if (target == null) return list;

            GameObject go = target as GameObject;
            if (go == null && target is Component comp) go = comp.gameObject;

            if (go != null)
            {
                list.Add(go);
                foreach (var c in go.GetComponents<Component>())
                    if (c != null) list.Add(c);
            }
            else
            {
                list.Add(target); // ScriptableObject or other Object
            }
            return list;
        }

        /// <summary>
        /// All valid functions across the target's candidate objects: dynamic (params match the event exactly),
        /// static methods (0 params, or 1 persistent-arg param), and property setters of persistent-arg types.
        /// Static (non-instance) methods are excluded — persistent calls need an instance.
        ///
        /// Entries are returned in PICKER order — project scripts, then the GameObject, then built-in
        /// components; within a group, declared members before inherited ones, alphabetically. Dynamic entries
        /// are a SECTION (flagged by <c>IsDynamic</c>) rather than a sort key, so the picker lists them
        /// separately while still showing each object's own group — they are NOT hoisted to the front.
        ///
        /// A method with one persistable parameter legitimately appears TWICE for an event that carries a
        /// matching argument — once as dynamic (passes the event's value through) and once as a static call
        /// (uses a value typed into the card). Those are genuinely different listeners; this is not a
        /// de-duplication bug, and collapsing them would remove a real capability.
        /// </summary>
        public static List<MethodEntry> GetMethodCandidates(UnityEngine.Object target, Type[] dynamicArgs)
        {
            var entries = new List<MethodEntry>();
            dynamicArgs ??= Type.EmptyTypes;

            var candidates = ResolveCandidateObjects(target);
            var groupNames = BuildGroupNames(candidates);

            for (int gi = 0; gi < candidates.Count; gi++)
            {
                var obj = candidates[gi];
                if (obj == null) continue;

                var objType = obj.GetType();
                string group = groupNames[gi];
                int groupIndex = gi;
                bool projectScript = IsProjectScript(obj);
                GroupKind kind = projectScript ? GroupKind.ProjectScript
                    : obj is GameObject ? GroupKind.GameObject
                    : GroupKind.Component;

                // Builds one entry with all the picker metadata filled in. `declaring` decides the
                // inherited-vs-declared split, which only ever folds inside a PROJECT SCRIPT's group — a
                // built-in component's inherited surface is the whole point of listing it.
                MethodEntry Make(string methodName, string memberDisplay, Type declaring,
                                 PersistentListenerMode mode, Type argType, bool dynamic)
                {
                    // An int that really means an enum is labelled with the ENUM ("Set Team  Event Picker
                    // Team"), because the field the row opens is a dropdown — calling it "int" would describe
                    // the storage rather than the choice, and the storage is not what anyone is picking.
                    Type argEnum = !dynamic && mode == PersistentListenerMode.Int && argType == typeof(int)
                        ? ResolveArgEnumType(objType, methodName)
                        : null;
                    string argLabel = dynamic ? "dynamic"
                        : argEnum != null ? ObjectNames.NicifyVariableName(argEnum.Name)
                        : argType == null ? "" : FriendlyArg(argType);
                    return new MethodEntry
                    {
                        Target = obj,
                        MethodName = methodName,
                        Mode = mode,
                        ArgType = argType,
                        ArgEnumType = argEnum,
                        IsDynamic = dynamic,
                        Group = group,
                        Display = $"{group}/{memberDisplay} ({argLabel})",
                        DeclaringType = declaring,
                        Kind = kind,
                        GroupIndex = groupIndex,
                        IsProjectScript = projectScript,
                        IsInherited = projectScript && IsUnityDeclaredType(declaring),
                        MemberDisplay = memberDisplay,
                        ArgLabel = argLabel,
                        Haystack = BuildEntryHaystack(group, objType, methodName, memberDisplay, argLabel, argEnum?.Name),
                    };
                }

                var methods = objType.GetMethods(MemberFlags);

                // Dynamic — exact signature match (void return).
                if (dynamicArgs.Length > 0)
                {
                    foreach (var m in methods)
                    {
                        if (m.IsSpecialName || m.IsGenericMethodDefinition || m.ReturnType != typeof(void) || IsObsolete(m)) continue;
                        var ps = m.GetParameters();
                        if (ps.Length != dynamicArgs.Length) continue;
                        bool match = true;
                        for (int i = 0; i < ps.Length; i++)
                            if (ps[i].ParameterType != dynamicArgs[i]) { match = false; break; }
                        if (!match) continue;
                        entries.Add(Make(m.Name, ObjectNames.NicifyVariableName(m.Name), m.DeclaringType,
                            PersistentListenerMode.EventDefined, null, dynamic: true));
                    }
                }

                // Static methods: 0 params, or 1 persistent-arg param.
                foreach (var m in methods)
                {
                    if (m.IsSpecialName || m.IsGenericMethodDefinition || m.ReturnType != typeof(void) || IsObsolete(m)) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 0)
                    {
                        entries.Add(Make(m.Name, ObjectNames.NicifyVariableName(m.Name), m.DeclaringType,
                            PersistentListenerMode.Void, null, dynamic: false));
                    }
                    else if (ps.Length == 1 && IsPersistentArgType(ps[0].ParameterType))
                    {
                        var at = ps[0].ParameterType;
                        entries.Add(Make(m.Name, ObjectNames.NicifyVariableName(m.Name), m.DeclaringType,
                            ModeForArg(at), at, dynamic: false));
                    }
                }

                // Property setters of persistent-arg types (e.g. "enabled", "position.x" not — only whole props).
                foreach (var p in objType.GetProperties(MemberFlags))
                {
                    var setter = p.GetSetMethod(false);
                    if (setter == null || IsObsolete(p) || IsObsolete(setter)) continue;
                    if (!IsPersistentArgType(p.PropertyType)) continue;
                    entries.Add(Make(setter.Name, ObjectNames.NicifyVariableName(p.Name), p.DeclaringType,
                        ModeForArg(p.PropertyType), p.PropertyType, dynamic: false));
                }
            }

            return entries
                .OrderBy(e => (int)e.Kind)
                .ThenBy(e => e.GroupIndex)
                .ThenBy(e => e.IsInherited ? 1 : 0)
                .ThenBy(e => e.MemberDisplay, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.ArgLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Display names for each candidate object, with same-typed objects disambiguated as "Audio Source #1",
        /// "Audio Source #2". Two components of one type otherwise produce byte-identical rows, leaving no way
        /// to tell which one a pick is aimed at (the write is correct either way — only the label is ambiguous).
        /// </summary>
        private static List<string> BuildGroupNames(List<UnityEngine.Object> candidates)
        {
            var names = new List<string>(candidates.Count);
            var totals = new Dictionary<string, int>();
            foreach (var o in candidates)
            {
                string n = o == null ? "" : ObjectNames.NicifyVariableName(o.GetType().Name);
                totals[n] = totals.TryGetValue(n, out int c) ? c + 1 : 1;
                names.Add(n);
            }

            var seen = new Dictionary<string, int>();
            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (totals[n] <= 1) continue;
                int k = seen.TryGetValue(n, out int c) ? c + 1 : 1;
                seen[n] = k;
                names[i] = $"{n} #{k}";
            }
            return names;
        }

        /// <summary>
        /// The search corpus for one entry. Carries BOTH the raw member name and its nicified/camel-split form,
        /// so typing what you actually wrote in C# ("SetActive", "AddForce") matches — the display name alone
        /// says "Set Active", against which "setactive" finds nothing.
        /// </summary>
        private static string BuildEntryHaystack(string group, Type objType, string methodName,
            string memberDisplay, string argLabel, string argEnumName = null)
        {
            var sb = new System.Text.StringBuilder();
            void Add(string s)
            {
                if (string.IsNullOrEmpty(s)) return;
                sb.Append(s);
                sb.Append(' ');
            }

            Add(memberDisplay);
            Add(methodName);
            // Property setters are stored as "set_Health" — index the bare property name too.
            if (!string.IsNullOrEmpty(methodName) && methodName.StartsWith("set_", StringComparison.Ordinal))
            {
                string bare = methodName.Substring(4);
                Add(bare);
                Add(InspectorSearchIndex.SplitWords(bare));
            }
            Add(InspectorSearchIndex.SplitWords(methodName));
            Add(group);
            Add(objType?.Name);
            Add(InspectorSearchIndex.SplitWords(objType?.Name));
            Add(argLabel);
            // argLabel already carries the enum NICIFIED ("Event Picker Team"); index the raw C# name too, so
            // typing the type as it is written in code finds the row. Same reasoning as the method name above.
            Add(argEnumName);
            return sb.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// True when <paramref name="obj"/> is backed by a script asset under <c>Assets/</c> — i.e. the user's
        /// own code (or a plugin they imported into the project), as opposed to a native component or a script
        /// shipped inside a package.
        ///
        /// Deliberately NOT a namespace check. The five other "is this Unity's own type" predicates in this
        /// codebase all test <c>Namespace.StartsWith("UnityEngine")</c>, which misfiles every
        /// third-party plugin and any user script that happens to live in a Unity-prefixed namespace. Going
        /// through the backing MonoScript's asset path is exact, rename-proof, and O(1) for a live instance.
        /// </summary>
        public static bool IsProjectScript(UnityEngine.Object obj)
        {
            if (obj == null) return false;
            if (!ScriptGuidUtility.TryGetGuid(obj, out string guid) || string.IsNullOrEmpty(guid)) return false;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return !string.IsNullOrEmpty(path)
                && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a member declared by <paramref name="t"/> counts as inherited Unity plumbing. Used ONLY for
        /// the cosmetic fold inside a project script's group (CancelInvoke / SendMessage / set_enabled / … are
        /// noise around the two methods you actually wrote). A namespace test is the right tool here precisely
        /// because it is about the Unity base classes, not about who owns the file — a user's own base class in
        /// their own namespace stays unfolded, which is what you want.
        /// </summary>
        internal static bool IsUnityDeclaredType(Type t)
        {
            string ns = t?.Namespace;
            return ns != null
                && (ns.StartsWith("UnityEngine", StringComparison.Ordinal)
                    || ns.StartsWith("UnityEditor", StringComparison.Ordinal));
        }

        /// <summary>
        /// Index of the entry matching a saved persistent call, or -1. The mode is part of the match because a
        /// single method can be present both as a dynamic entry and as a static one.
        /// </summary>
        public static int FindEntryIndex(IList<MethodEntry> entries, UnityEngine.Object target,
            string methodName, PersistentListenerMode mode)
        {
            if (entries == null || target == null || string.IsNullOrEmpty(methodName)) return -1;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (ReferenceEquals(e.Target, target)
                    && string.Equals(e.MethodName, methodName, StringComparison.Ordinal)
                    && e.Mode == mode)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Whether a saved persistent call still resolves against its target — false once the method is renamed
        /// or deleted, or its signature no longer fits the stored mode. An empty method name is "No Function",
        /// which is not a break. Drives the card's missing-method warning.
        /// </summary>
        public static bool IsMethodResolvable(UnityEngine.Object target, string methodName, PersistentListenerMode mode)
        {
            if (target == null || string.IsNullOrEmpty(methodName)) return true;
            var targetType = target.GetType();

            // Dynamic: the event supplies the arguments and its signature isn't available here, so any void
            // overload of that name still binds.
            if (mode == PersistentListenerMode.EventDefined)
            {
                foreach (var m in targetType.GetMethods(MemberFlags))
                    if (string.Equals(m.Name, methodName, StringComparison.Ordinal)
                        && m.ReturnType == typeof(void) && !m.IsGenericMethodDefinition)
                        return true;
                return false;
            }

            // Object mode: the concrete argument type lives on the call, not here, so match structurally.
            if (mode == PersistentListenerMode.Object)
            {
                foreach (var m in targetType.GetMethods(MemberFlags))
                {
                    if (!string.Equals(m.Name, methodName, StringComparison.Ordinal)) continue;
                    if (m.ReturnType != typeof(void) || m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && typeof(UnityEngine.Object).IsAssignableFrom(ps[0].ParameterType)) return true;
                }
                return false;
            }

            // Everything else: ask UNITY'S OWN binder with exactly the types PersistentCall will use at
            // runtime. That makes this the authority rather than an approximation of it — and it is why a
            // legacy enum-argument listener (mode Int, method takes MyEnum) correctly reports as BROKEN: it
            // is, since Unity will look for Method(int) and never find it.
            Type[] args =
                mode == PersistentListenerMode.Void ? Type.EmptyTypes :
                mode == PersistentListenerMode.Int ? new[] { typeof(int) } :
                mode == PersistentListenerMode.Float ? new[] { typeof(float) } :
                mode == PersistentListenerMode.String ? new[] { typeof(string) } :
                mode == PersistentListenerMode.Bool ? new[] { typeof(bool) } :
                null;
            if (args == null) return true; // unknown future mode — never cry wolf

            return UnityEventBase.GetValidMethodInfo(targetType, methodName, args) != null;
        }

        /// <summary>
        /// The display name for a persistent call's target — the nicified type name, suffixed "#n" when the
        /// GameObject carries MORE THAN ONE component of that exact type. Unity's ObjectField renders both as
        /// a bare "MyObject (Audio Source)", so without this the card cannot tell you which instance it is
        /// wired to. Numbering mirrors <see cref="BuildGroupNames"/> (inspector order), so the card and the
        /// picker always agree.
        /// </summary>
        public static string TargetDisplayName(UnityEngine.Object target)
        {
            if (target == null) return "";
            string name = ObjectNames.NicifyVariableName(target.GetType().Name);
            CountSameTypeSiblings(target, out int index, out int total);
            return total > 1 ? $"{name} #{index}" : name;
        }

        /// <summary>
        /// True when naming the target actually tells you something the card's ObjectField doesn't. Picking a
        /// function re-points m_Target at the component that owns it, so the field already shows the type —
        /// the breadcrumb prefix is pure repetition EXCEPT when several components share that type and only
        /// the "#n" says which one. Drives whether the card shows a prefix at all.
        /// </summary>
        public static bool TargetIsAmbiguous(UnityEngine.Object target)
        {
            CountSameTypeSiblings(target, out _, out int total);
            return total > 1;
        }

        private static void CountSameTypeSiblings(UnityEngine.Object target, out int index, out int total)
        {
            index = 0;
            total = 0;
            var comp = target as Component;
            if (comp == null || comp.gameObject == null) return;

            foreach (var sibling in comp.gameObject.GetComponents<Component>())
            {
                if (sibling == null || sibling.GetType() != comp.GetType()) continue;
                total++;
                if (ReferenceEquals(sibling, comp)) index = total;
            }
        }

        /// <summary>Unity-style assembly type name ("Namespace.Type, Assembly") for m_TargetAssemblyTypeName.</summary>
        public static string UnityStyleTypeName(Type t) => t == null ? "" : $"{t.FullName}, {t.Assembly.GetName().Name}";

        /// <summary>Invokes the event's persistent (Editor+Runtime) calls in edit mode (the Test button).</summary>
        public static void InvokeInEditor(UnityEngine.Object owner, string propertyPath)
        {
            try
            {
                var evt = ResolveEventInstance(owner, propertyPath);
                if (evt == null) return;
                var invoke = evt.GetType().GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
                if (invoke == null) return;
                var ps = invoke.GetParameters();
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++) args[i] = DefaultArg(ps[i].ParameterType);
                invoke.Invoke(evt, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Sasquatch Inspector] UnityEvent invoke failed: {ex.Message}");
            }
        }

        /// <summary>Walks a SerializedProperty-style path on an object to fetch the live UnityEventBase instance.</summary>
        public static UnityEventBase ResolveEventInstance(UnityEngine.Object owner, string propertyPath)
        {
            object current = owner;
            if (current == null || string.IsNullOrEmpty(propertyPath)) return current as UnityEventBase;

            var parts = propertyPath.Split('.');
            for (int i = 0; i < parts.Length && current != null; i++)
            {
                string part = parts[i];
                if (part == "Array" && i + 1 < parts.Length && parts[i + 1].StartsWith("data[", StringComparison.Ordinal))
                {
                    i++;
                    string idxStr = parts[i].Substring(5).TrimEnd(']');
                    if (!int.TryParse(idxStr, out int idx)) return null;
                    current = (current is IList list && idx >= 0 && idx < list.Count) ? list[idx] : null;
                }
                else
                {
                    current = GetFieldValue(current, part);
                }
            }
            return current as UnityEventBase;
        }

        private static object GetFieldValue(object obj, string name)
        {
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(obj);
            }
            return null;
        }

        private static object DefaultArg(Type t)
        {
            if (t == typeof(string)) return string.Empty;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return null;
            if (t.IsValueType) return Activator.CreateInstance(t);
            return null;
        }

        private static bool IsObsolete(MemberInfo m) => m.GetCustomAttribute<ObsoleteAttribute>() != null;

        private static string FriendlyArg(Type t) =>
            t == typeof(int) ? "int" :
            t == typeof(float) ? "float" :
            t == typeof(string) ? "string" :
            t == typeof(bool) ? "bool" : t.Name;
    }
}
