using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Reflection + invocation helpers for <see cref="ButtonNode"/> action buttons: resolves the target
    /// method, decides which parameter types we can prompt for, and invokes the method (passing the collected
    /// arguments) while driving any async return (<see cref="Task"/> / <see cref="IEnumerator"/>) so the
    /// button can show a "running" state until the work finishes.
    /// </summary>
    public static class ButtonInvoker
    {
        internal const BindingFlags MethodFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Remembers the last-entered arguments per button within the editor session, so re-running a
        // parametered button doesn't reset the prompt to defaults every time.
        private static readonly Dictionary<ButtonNode, object[]> _argCache = new Dictionary<ButtonNode, object[]>();

        /// <summary>Parameter types the argument prompt knows how to draw a field for.</summary>
        public static bool IsSupportedParameterType(Type t)
        {
            if (t == null) return false;
            if (t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double) ||
                t == typeof(bool) || t == typeof(string) ||
                t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) ||
                t == typeof(Color) || t == typeof(Rect) || t == typeof(Bounds))
                return true;
            if (t.IsEnum) return true;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return true;
            return false;
        }

        /// <summary>A method is invokable from a button when every parameter is a type we can prompt for.</summary>
        public static bool IsInvokable(MethodInfo m)
        {
            if (m == null) return false;
            if (m.ContainsGenericParameters) return false; // can't late-bind an open generic method
            foreach (var p in m.GetParameters())
                if (!IsSupportedParameterType(p.ParameterType)) return false;
            return true;
        }

        /// <summary>
        /// A method the button UI should offer or heal against: user-declared (not object/UnityEngine
        /// bases), non-special, and invokable. The designer's autocomplete list and the rename self-heal
        /// (<see cref="ButtonMethodSync"/>) share this filter, so a heal can only ever land on a method the
        /// dropdown would have offered.
        /// </summary>
        public static bool IsUserActionMethod(MethodInfo m)
        {
            return m != null && !m.IsSpecialName && IsInvokable(m)
                   && m.DeclaringType != null && m.DeclaringType != typeof(object)
                   && (m.DeclaringType.Namespace == null || !m.DeclaringType.Namespace.StartsWith("UnityEngine"));
        }

        /// <summary>True when ANY instance method on <paramref name="type"/> has this name, invokable or not.
        /// A method that merely became un-bindable (e.g. gained an unsupported parameter) still exists — that
        /// state must never be mistaken for a rename.</summary>
        public static bool HasMethodNamed(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name)) return false;
            foreach (var m in MethodTable(type))
                if (m.Name == name && !m.IsSpecialName) return true;
            return false;
        }

        // One type's method table, materialised once. GetMethods allocates a fresh MethodInfo[] on EVERY call,
        // and the button paths sweep it repeatedly: the rename self-heal runs on every inspector rebuild and,
        // per button, can sweep up to three times (ResolveMethod, then HasMethodNamed and the rename search
        // inside TryFindRenameTarget). Memoising here fixes all of them at once rather than one call site at a
        // time.
        //
        // Safe because a type's method table cannot change without a domain reload, which is what clears this
        // dictionary — and because every caller only READS the array (foreach / LINQ). Nothing sorts or
        // rewrites it, so handing back the same instance is indistinguishable from a fresh one.
        private static readonly Dictionary<Type, MethodInfo[]> _methodTables = new Dictionary<Type, MethodInfo[]>();

        internal static MethodInfo[] MethodTable(Type type)
        {
            if (type == null) return Array.Empty<MethodInfo>();
            if (!_methodTables.TryGetValue(type, out var table))
            {
                table = type.GetMethods(MethodFlags);
                _methodTables[type] = table;
            }
            return table;
        }

        /// <summary>
        /// The autocomplete suggestion list for binding a button method on <paramref name="type"/> — the
        /// SINGLE provider behind the designer's Method Name field, the quick-edit popup, and the
        /// in-inspector relink row, so every picker offers exactly the heal-eligible set
        /// (<see cref="IsUserActionMethod"/>) and none can drift.
        /// </summary>
        public static IEnumerable<AutocompleteTextField.Suggestion> GetMethodSuggestions(Type type)
        {
            if (type == null) return Array.Empty<AutocompleteTextField.Suggestion>();
            return MethodTable(type)
                .Where(IsUserActionMethod)
                .GroupBy(m => m.Name)
                .Select(g => new AutocompleteTextField.Suggestion(g.Key, FormatParameterList(g.First())));
        }

        /// <summary>"(int amount, string label)"-style detail for a suggestion or hint; null when parameterless.</summary>
        public static string FormatParameterList(MethodInfo m)
        {
            var ps = m.GetParameters();
            if (ps.Length == 0) return null;
            return "(" + string.Join(", ",
                ps.Select(p => $"{SasquatchReflectionUtility.GetFriendlyTypeName(p.ParameterType)} {p.Name}")) + ")";
        }

        /// <summary>Finds the named method on <paramref name="type"/> (any accessibility), or null.</summary>
        public static MethodInfo ResolveMethod(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;

            // Memoised per (type, name), including the MISS: an unresolvable name is exactly the case the
            // rename self-heal hits on every rebuild, and it is the one that swept the whole table to find
            // nothing. Same domain-reload lifetime as MethodTable.
            var key = (type, name);
            if (_resolvedMethods.TryGetValue(key, out var cached)) return cached;

            // First name match whose parameters we can all prompt for (overloads pick the first such match).
            var resolved = MethodTable(type)
                .FirstOrDefault(m => m.Name == name && !m.IsSpecialName && IsInvokable(m));
            _resolvedMethods[key] = resolved;
            return resolved;
        }

        private static readonly Dictionary<(Type, string), MethodInfo> _resolvedMethods =
            new Dictionary<(Type, string), MethodInfo>();

        /// <summary>Finds the named method on the serialized object's target type, or null.</summary>
        public static MethodInfo ResolveMethod(SerializedObject serializedObject, string name)
        {
            if (serializedObject == null) return null;
            foreach (var t in serializedObject.targetObjects)
                if (t != null) return ResolveMethod(t.GetType(), name);
            return null;
        }

        /// <summary>A sensible value to pre-fill for parameter <paramref name="p"/> (its declared default, else its type's default).</summary>
        public static object DefaultValueForParameter(ParameterInfo p)
        {
            if (p == null) return null;
            if (p.HasDefaultValue && p.DefaultValue != null && !(p.DefaultValue is DBNull))
                return p.DefaultValue;
            return DefaultValueForType(p.ParameterType);
        }

        /// <summary>A sensible default value for type <paramref name="t"/> (zero/empty instance for prompt fields).</summary>
        public static object DefaultValueForType(Type t)
        {
            if (t == null) return null;
            if (t == typeof(string)) return string.Empty;
            if (t == typeof(Color)) return Color.white;
            if (t.IsEnum)
            {
                var values = Enum.GetValues(t);
                return values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(t);
            }
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return null;
            if (t.IsValueType) return Activator.CreateInstance(t);
            return null;
        }

        /// <summary>
        /// The initial argument values to seed the prompt with — the session-cached last-used values when they
        /// still fit the method's parameters, otherwise per-type defaults.
        /// </summary>
        public static object[] GetInitialArgs(ButtonNode node, ParameterInfo[] parameters)
        {
            if (parameters == null) return Array.Empty<object>();

            if (node != null && _argCache.TryGetValue(node, out var cached) &&
                cached != null && cached.Length == parameters.Length)
            {
                bool typesMatch = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    var v = cached[i];
                    if (v != null && !parameters[i].ParameterType.IsInstanceOfType(v)) { typesMatch = false; break; }
                }
                if (typesMatch) return (object[])cached.Clone();
            }

            var defaults = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                defaults[i] = DefaultValueForParameter(parameters[i]);
            return defaults;
        }

        /// <summary>Caches the last-entered <paramref name="args"/> for <paramref name="node"/> so the prompt re-opens pre-filled this session.</summary>
        public static void RememberArgs(ButtonNode node, object[] args)
        {
            if (node == null) return;
            _argCache[node] = args == null ? null : (object[])args.Clone();
        }

        // ---- Persisting what a button's method changed --------------------------------------------------
        // A button method mutates the C# object DIRECTLY: no SerializedProperty is written, so nothing marks
        // the target dirty and the bound SerializedObject keeps serving the pre-call data until something
        // else re-reads it. The visible symptom is a button that appends to a list leaving the inspector
        // showing the old rows. Both halves are handled here: detect a real change, then persist it and
        // re-read it. The rows themselves come back through the collection widget's own external-size sync,
        // so this never triggers a whole-inspector rebuild.

        /// <summary>Which persistence call a mutated target needs. Split out so the routing is testable
        /// without a real scene.</summary>
        internal enum PersistKind
        {
            /// <summary>Nothing to persist (no target).</summary>
            None,
            /// <summary>An asset or an in-memory object: <c>EditorUtility.SetDirty</c> is enough.</summary>
            Object,
            /// <summary>Lives in a loaded scene: <c>SetDirty</c> alone is a no-op there, the SCENE must be marked.</summary>
            SceneObject,
        }

        /// <summary>Classifies <paramref name="target"/> for <see cref="PersistKind"/>.</summary>
        internal static PersistKind ClassifyPersist(UnityEngine.Object target)
        {
            if (target == null) return PersistKind.None;
            if (EditorUtility.IsPersistent(target)) return PersistKind.Object;

            var go = target as GameObject ?? (target as Component)?.gameObject;
            if (go != null && go.scene.IsValid() && go.scene.isLoaded) return PersistKind.SceneObject;

            return PersistKind.Object;
        }

        /// <summary>
        /// A comparable snapshot of everything Unity serializes on <paramref name="target"/> — the change
        /// detector. Cheap enough for a click, and exact: it catches a list that grew AND a value that moved,
        /// so a button that only reads never dirties the scene.
        /// </summary>
        internal static string SnapshotState(UnityEngine.Object target)
        {
            if (target == null) return null;
            try { return EditorJsonUtility.ToJson(target); }
            catch { return null; } // never let a fingerprint failure block the invoke
        }

        /// <summary>Marks <paramref name="target"/> dirty the way its kind requires. Play mode changes are
        /// discarded on exit, so nothing is flagged there.</summary>
        private static void PersistMutation(UnityEngine.Object target)
        {
            if (target == null || EditorApplication.isPlayingOrWillChangePlaymode) return;

            switch (ClassifyPersist(target))
            {
                case PersistKind.SceneObject:
                    EditorUtility.SetDirty(target);
                    var go = target as GameObject ?? (target as Component)?.gameObject;
                    if (go != null)
                    {
                        if (PrefabUtility.IsPartOfPrefabInstance(go))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                        EditorSceneManager.MarkSceneDirty(go.scene);
                    }
                    break;
                case PersistKind.Object:
                    EditorUtility.SetDirty(target);
                    break;
            }
        }

        /// <summary>
        /// Invokes the node's method on every target, passing <paramref name="args"/>. <paramref name="onStarted"/>
        /// fires before the first invoke and <paramref name="onFinished"/> after all work completes — for a
        /// void/synchronous method that's the same frame; for a <see cref="Task"/>- or <see cref="IEnumerator"/>-
        /// returning method it's deferred until the async work finishes (driven off
        /// <see cref="EditorApplication.update"/>), letting the caller show a running state in between.
        /// Any target the method actually changed is marked dirty and re-read into
        /// <paramref name="serializedObject"/> once the work completes, then <paramref name="onMutated"/>
        /// fires so the caller can refresh what it rendered from the old data.
        /// </summary>
        public static void InvokeButtonMethod(ButtonNode node, SerializedObject serializedObject, object[] args,
            Action onStarted, Action onFinished, Action onMutated = null)
        {
            if (node == null || serializedObject == null || string.IsNullOrEmpty(node.MethodName))
            {
                onFinished?.Invoke();
                return;
            }

            onStarted?.Invoke();

            var pendingTasks = new List<Task>();
            var pendingCoroutines = new List<IEnumerator>();

            // Snapshot BEFORE the first invoke — an async method finishes long after this returns, so the
            // comparison has to run from the completion path against these values.
            var targets = serializedObject.targetObjects;
            var before = new string[targets.Length];
            // The dirty flag as it stood BEFORE the click, so a button that turns out to have changed
            // nothing can be left exactly as it found things — see the restore in PersistAndSync.
            var wasDirty = new bool[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                before[i] = SnapshotState(targets[i]);
                wasDirty[i] = targets[i] != null && EditorUtility.IsDirty(targets[i]);
            }

            // UNDO, also before the first invoke — the only moment the pre-call state still exists. Without
            // it a button that mutates its target commits through SetDirty / MarkSceneDirty and is then
            // PERMANENT: Ctrl+Z can't reach it, on either render path. The snapshot is registered
            // unconditionally (we can't know whether the method will write until it has), then collapsed
            // into one step below if it did. RegisterCompleteObjectUndo, not RecordObject: the full snapshot
            // costs ~3ms up front instead of a flush-time diff, and the diff is what the project measured as
            // catastrophic when a big mutation and a UI refresh land in the same tick.
            //
            // Covers the target objects' serialized state — the ordinary "button edits my fields" case. A
            // method that CREATES or DESTROYS objects still owes its own Undo.RegisterCreatedObjectUndo /
            // Undo.DestroyObjectImmediate; no editor-side wrapper can retrofit that.
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            string undoName = $"Button: {(string.IsNullOrEmpty(node.ButtonText) ? node.MethodName : node.ButtonText)}";
            var undoTargets = new List<UnityEngine.Object>(targets.Length);
            foreach (var t in targets) if (t != null) undoTargets.Add(t);
            if (undoTargets.Count > 0) Undo.RegisterCompleteObjectUndo(undoTargets.ToArray(), undoName);

            void PersistAndSync()
            {
                bool anyChanged = false;
                for (int i = 0; i < targets.Length; i++)
                {
                    var t = targets[i];
                    if (t == null || before[i] == null) continue;
                    if (SnapshotState(t) == before[i]) continue;
                    anyChanged = true;
                    PersistMutation(t);
                }
                if (!anyChanged)
                {
                    // A READ-ONLY button must leave no trace. Registering the undo snapshot above can set the
                    // dirty flag on its own, and "clicking Inspect marked my scene modified" is exactly the
                    // complaint the change detector exists to prevent — so put the flag back where it was.
                    // Safe precisely because the state comparison just proved nothing was written.
                    for (int i = 0; i < targets.Length; i++)
                        if (targets[i] != null && !wasDirty[i]) EditorUtility.ClearDirty(targets[i]);
                    return;
                }
                // One named step for the whole click, however many objects the method touched.
                Undo.SetCurrentGroupName(undoName);
                Undo.CollapseUndoOperations(undoGroup);
                try
                {
                    // Re-read first: every bound field re-syncs from the new data, and the explicit refresh
                    // below then re-fires the change-driven widgets against it. Unity's own trackers can't be
                    // relied on here — they fire on change-vs-baseline and silently swallow a change that
                    // lands outside their tick (see TrackedRefresh) — so the refresh is driven, not awaited.
                    serializedObject.SetIsDifferentCacheDirty();
                    serializedObject.Update();
                }
                catch { /* the inspector may already have been torn down under an async method */ }
                onMutated?.Invoke();
            }

            foreach (var targetObj in targets)
            {
                if (targetObj == null) continue;

                var method = ResolveMethod(targetObj.GetType(), node.MethodName);
                if (method == null)
                {
                    Debug.LogError($"[Sasquatch Inspector] Method '{node.MethodName}' not found (or has unsupported parameters) on '{targetObj.GetType().Name}'.");
                    continue;
                }

                try
                {
                    object result = method.Invoke(targetObj, args);
                    if (result is Task task)
                    {
                        if (task.IsCompleted) ObserveTaskFault(task);
                        else pendingTasks.Add(task);
                    }
                    else if (result is IEnumerator coroutine)
                    {
                        pendingCoroutines.Add(coroutine);
                    }
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    Debug.LogError($"[Sasquatch Inspector] Error executing '{node.MethodName}' on '{targetObj.name}': {inner.Message}");
                }
            }

            if (pendingTasks.Count == 0 && pendingCoroutines.Count == 0)
            {
                PersistAndSync();
                onFinished?.Invoke();
                return;
            }

            // Something is still running — poll the editor loop, stepping coroutines and reaping finished
            // tasks, and re-enable the button only once everything is done.
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                for (int i = pendingCoroutines.Count - 1; i >= 0; i--)
                {
                    bool more;
                    try { more = pendingCoroutines[i].MoveNext(); }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Sasquatch Inspector] Coroutine '{node.MethodName}' threw: {ex.Message}");
                        more = false;
                    }
                    if (!more) pendingCoroutines.RemoveAt(i);
                }

                for (int i = pendingTasks.Count - 1; i >= 0; i--)
                {
                    if (pendingTasks[i].IsCompleted)
                    {
                        ObserveTaskFault(pendingTasks[i]);
                        pendingTasks.RemoveAt(i);
                    }
                }

                if (pendingTasks.Count == 0 && pendingCoroutines.Count == 0)
                {
                    EditorApplication.update -= tick;
                    PersistAndSync();
                    onFinished?.Invoke();
                }
            };
            EditorApplication.update += tick;
        }

        private static void ObserveTaskFault(Task task)
        {
            if (task != null && task.IsFaulted && task.Exception != null)
            {
                var inner = task.Exception.InnerException ?? task.Exception;
                Debug.LogError($"[Sasquatch Inspector] Button task failed: {inner.Message}");
            }
        }
    }
}
