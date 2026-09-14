using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Section 1 of the unified right-click menu: the "native essentials" the capture-phase takeover
    /// suppressed, rebuilt by us so the menu is the same everywhere. Three context-sensitive blocks:
    /// text edit ops when the click landed in a TextField, ping/select/properties when it landed in
    /// an ObjectField (incl. the AssetPreview widget, whose ObjectField is an ancestor of the clicked
    /// pixel), and property-value ops whenever a bound SerializedProperty can be resolved from the
    /// clicked element. Numeric Float/Integer fields intentionally skip the text block — Copy/Paste
    /// VALUE from the property block is the useful operation there.
    ///
    /// The property block also rebuilds the field's own <c>[ContextMenuItem]</c> entries. Those are
    /// the developer's DECLARED actions, not chrome of ours: suppressing the native menu without
    /// replacing them would silently delete a documented Unity feature from any type that gains a
    /// Sasquatch layout, so they are non-negotiable here.
    /// </summary>
    public static class QuickMenuNativeEssentials
    {
        /// <summary>Adds the rebuilt native-essential entries (text edit / object ping-select-properties / property value ops) for the clicked element.</summary>
        public static void Contribute(QuickMenuRequest request, QuickMenuModel menu)
        {
            var deep = request?.DeepTarget;
            if (deep == null || menu == null) return;

            var textField = deep as TextField ?? deep.GetFirstAncestorOfType<TextField>();
            if (textField != null) AppendTextEntries(textField, menu);

            var objectField = deep as ObjectField ?? deep.GetFirstAncestorOfType<ObjectField>();
            if (objectField != null)
            {
                menu.AddSeparator();
                AppendObjectEntries(objectField, menu);
            }

            var ctx = request.Ctx;
            if (ctx?.SerializedObject != null)
            {
                string path = ResolveBindingPath(deep, request.Root, ctx.SerializedObject.targetObject);
                if (!string.IsNullOrEmpty(path) && ctx.SerializedObject.FindProperty(path) != null)
                {
                    menu.AddSeparator();
                    AppendPropertyEntries(ctx, path, menu);
                }
            }
        }

        private static void AppendTextEntries(TextField tf, QuickMenuModel menu)
        {
            string text = tf.text ?? string.Empty;
            int selStart = Mathf.Clamp(Mathf.Min(tf.cursorIndex, tf.selectIndex), 0, text.Length);
            int selEnd = Mathf.Clamp(Mathf.Max(tf.cursorIndex, tf.selectIndex), 0, text.Length);
            bool hasSelection = selEnd > selStart;
            // No selection (typical for an unfocused field) → the ops act on the whole text.
            string selected = hasSelection ? text.Substring(selStart, selEnd - selStart) : text;
            bool readOnly = tf.isReadOnly;
            string clip = EditorGUIUtility.systemCopyBuffer;

            menu.Add("Cut", () =>
            {
                EditorGUIUtility.systemCopyBuffer = selected;
                tf.value = hasSelection ? text.Remove(selStart, selEnd - selStart) : string.Empty;
            }, enabled: !readOnly && selected.Length > 0);

            menu.Add("Copy", () => EditorGUIUtility.systemCopyBuffer = selected,
                enabled: selected.Length > 0);

            menu.Add("Paste", () =>
            {
                string paste = EditorGUIUtility.systemCopyBuffer ?? string.Empty;
                if (hasSelection)
                    tf.value = text.Substring(0, selStart) + paste + text.Substring(selEnd);
                else
                {
                    int caret = Mathf.Clamp(tf.cursorIndex, 0, text.Length);
                    tf.value = text.Insert(caret, paste);
                }
            }, enabled: !readOnly && !string.IsNullOrEmpty(clip));

            menu.Add("Select All", () =>
            {
                tf.Focus();
                tf.SelectAll();
            }, enabled: text.Length > 0);
        }

        private static void AppendObjectEntries(ObjectField of, QuickMenuModel menu)
        {
            var value = of.value;
            bool has = value != null;
            menu.Add("Ping", () => { if (of.value != null) EditorGUIUtility.PingObject(of.value); }, has);
            menu.Add("Select", () => { if (of.value != null) Selection.activeObject = of.value; }, has);
            menu.Add("Open Properties…", () => { if (of.value != null) EditorUtility.OpenPropertyEditor(of.value); }, has);
        }

        private static void AppendPropertyEntries(QuickEditContext ctx, string path, QuickMenuModel menu)
        {
            var so = ctx.SerializedObject;
            var prop = so.FindProperty(path);

            menu.Add("Copy Property Path", () => EditorGUIUtility.systemCopyBuffer = path);
            menu.Add("Copy Value", () =>
            {
                var p = so.FindProperty(path);
                if (p != null) PropertyClipboard.Copy(p);
            });
            menu.Add("Paste Value", () =>
            {
                var p = so.FindProperty(path);
                if (p == null) return;
                int sizeBefore = p.isArray ? p.arraySize : -1;
                if (!PropertyClipboard.Paste(p)) return;
                so.ApplyModifiedProperties(); // records undo
                if (p.isArray && p.arraySize != sizeBefore) ctx.RebuildInspector?.Invoke();
            }, enabled: PropertyClipboard.CanPaste(prop));

            if (ctx.Target != null && PrefabUtility.IsPartOfPrefabInstance(ctx.Target) && prop.prefabOverride)
            {
                // Apply to Prefab — the counterpart to Revert, matching Unity's native property menu. Only offered
                // when the nearest instance root maps to an EDITABLE prefab asset (an immutable model/imported/read-
                // only prefab can't take an override, so we omit the item rather than offer a broken action).
                if (TryGetApplicablePrefabPath(ctx.Target, out string prefabPath, out string prefabName))
                {
                    menu.Add($"Apply to Prefab '{prefabName}'", () =>
                    {
                        var p = so.FindProperty(path);
                        if (p == null) return;
                        try { PrefabUtility.ApplyPropertyOverride(p, prefabPath, InteractionMode.UserAction); }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Sasquatch Inspector: couldn't apply '{path}' to prefab '{prefabName}': {e.Message}");
                            return;
                        }
                        so.Update();
                        ctx.RebuildInspector?.Invoke();
                    });
                }

                menu.Add("Revert to Prefab Value", () =>
                {
                    var p = so.FindProperty(path);
                    if (p == null) return;
                    PrefabUtility.RevertPropertyOverride(p, InteractionMode.UserAction);
                    so.Update();
                    ctx.RebuildInspector?.Invoke();
                });
            }

            // The script's OWN per-field verbs, last: stock value ops read first, then whatever the
            // developer declared on the field.
            AppendContextMenuItemEntries(ctx, path, menu);
        }

        /// <summary>
        /// Rebuilds a field's <c>[ContextMenuItem("Label", "MethodName")]</c> entries — the ones Unity's
        /// native property menu would have shown before <see cref="QuickMenuController"/>'s capture-phase
        /// takeover suppressed it. Resolved from the same RESOLVED property path the value ops use, so a
        /// collection row's cell surfaces its ELEMENT field's items too. The attribute allows multiples on
        /// one field; every one of them gets an entry.
        /// </summary>
        private static void AppendContextMenuItemEntries(QuickEditContext ctx, string path, QuickMenuModel menu)
        {
            var target = ctx.SerializedObject.targetObject;
            if (target == null) return;

            var field = BindingFactory.GetFieldInfoFromPropertyPath(target.GetType(), path, out _);
            if (field == null) return;

            var attributes = field.GetCustomAttributes(typeof(ContextMenuItemAttribute), true);
            if (attributes == null || attributes.Length == 0) return;

            menu.AddSeparator();
            foreach (ContextMenuItemAttribute attribute in attributes)
            {
                if (attribute == null || string.IsNullOrEmpty(attribute.name)) continue;
                string label = attribute.name;
                string function = attribute.function;
                menu.Add(label, () => InvokeContextMenuItem(ctx, label, function));
            }
        }

        /// <summary>
        /// Invokes a <c>[ContextMenuItem]</c> method on EVERY edited target (multi-select included), the
        /// same way Unity's native menu does.
        /// </summary>
        /// <remarks>
        /// Undo is ours, and that is a DELIBERATE divergence from stock: the native path records nothing,
        /// so a method that simply assigns a field left the change un-undoable and — on a scene object —
        /// un-dirtied. <c>RegisterCompleteObjectUndo</c> rather than <c>RecordObject</c>, because a target
        /// carrying [SerializeReference] data hits a diffing pathology on the latter. A method that records
        /// its own undo still collapses into this single step (both records share one undo group), so the
        /// user never pays two Ctrl+Z for one click.
        /// </remarks>
        private static void InvokeContextMenuItem(QuickEditContext ctx, string label, string function)
        {
            var so = ctx.SerializedObject;
            if (so == null || string.IsNullOrEmpty(function)) return;

            var targets = so.targetObjects;
            if (targets == null || targets.Length == 0) return;

            Undo.RegisterCompleteObjectUndo(targets, label);

            bool invokedAny = false;
            foreach (var target in targets)
            {
                if (target == null) continue;
                var method = FindContextMenuMethod(target.GetType(), function);
                if (method == null) continue;
                try
                {
                    method.Invoke(method.IsStatic ? null : target, null);
                    invokedAny = true;
                }
                catch (Exception e)
                {
                    // The user's own code threw — surface THEIR exception (not the reflection wrapper)
                    // against their object, and keep going so a multi-select isn't halted by one bad target.
                    Debug.LogError($"Sasquatch Inspector: [ContextMenuItem] '{label}' → {function}() threw: " +
                                   $"{e.InnerException ?? e}", target);
                }
            }

            if (!invokedAny)
            {
                Debug.LogWarning($"Sasquatch Inspector: [ContextMenuItem(\"{label}\", \"{function}\")] found no " +
                                 $"parameterless method named '{function}'.", targets[0]);
                return;
            }

            // The method mutated the C# object behind the SerializedObject's back, so re-read before the
            // bound fields are asked for values, then rebuild — a structural change (a list resized, a
            // reference nulled) needs the tree rebuilt, exactly like the prefab ops above.
            so.Update();
            ctx.RebuildInspector?.Invoke();
        }

        /// <summary>
        /// The parameterless method named <paramref name="function"/> on <paramref name="type"/> or any base
        /// type — public or private, instance or static, matching what Unity's native callback accepts.
        /// DeclaredOnly + an explicit walk so a private method on a BASE class is still found (plain
        /// <c>GetMethod</c> never returns inherited privates).
        /// </summary>
        private static MethodInfo FindContextMenuMethod(Type type, string function)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.DeclaredOnly;
            for (var t = type; t != null; t = t.BaseType)
            {
                var method = t.GetMethod(function, flags, null, Type.EmptyTypes, null);
                if (method != null) return method;
            }
            return null;
        }

        /// <summary>
        /// The editable prefab-asset path (and its display name) to Apply an override to — the nearest instance
        /// root's source prefab. Returns false when the click isn't on a prefab instance, the root has no asset
        /// path, or that prefab is IMMUTABLE (a model/imported/read-only prefab can't receive an applied override),
        /// so the caller simply omits the "Apply to Prefab" entry in those cases.
        /// </summary>
        private static bool TryGetApplicablePrefabPath(UnityEngine.Object target, out string assetPath, out string prefabName)
        {
            assetPath = null;
            prefabName = null;
            if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target)) return false;

            assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(target);
            if (string.IsNullOrEmpty(assetPath)) return false;

            var source = PrefabUtility.GetCorrespondingObjectFromSource(target);
            if (source == null || PrefabUtility.IsPartOfImmutablePrefab(source)) return false;

            prefabName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            return true;
        }

        /// <summary>
        /// The RESOLVED property path bound at/around the clicked element (collection rows included,
        /// e.g. "modifications.Array.data[3].modName"). (a) First IBindable with a non-empty
        /// bindingPath walking up from the click — every widget binds via BindProperty, so this is
        /// authoritative. (b) Fallback for clicks on a row label whose bound control is a sibling:
        /// the deepest BindingNode chain element's subtree is queried for a bindable. (c) Null when
        /// nothing resolves (known edge: MinMaxSlider syncs two floats manually) — the property
        /// section is simply omitted.
        /// </summary>
        /// <param name="expectedOwner">
        /// The object the caller will resolve the path against. Optional, and only the builder's stamp
        /// consults it — a null owner keeps the historical behaviour of trusting whatever is found.
        /// </param>
        public static string ResolveBindingPath(VisualElement deepTarget, VisualElement stopAt,
            UnityEngine.Object expectedOwner = null)
        {
            for (var el = deepTarget; el != null; el = el.parent)
            {
                if (el is IBindable bindable && !string.IsNullOrEmpty(bindable.bindingPath))
                    return bindable.bindingPath;

                // The builder's stamp, for the widgets Unity never bound (Enum Badge Grid, Progress Bar,
                // Rating, Gradient, Tag Chips…). Checked per-element AFTER bindingPath so a natively bound
                // field resolves exactly as it always did; only the case that used to return null changes.
                string stamped = BindingFactory.ReadStampedPropertyPath(el, expectedOwner, out bool mismatch);
                if (!string.IsNullOrEmpty(stamped)) return stamped;

                // A stamp owned by ANOTHER object means the click landed inside an Inline Editor, which builds
                // against its own SerializedObject. Stop: the next stamp up describes the field that CONTAINS
                // the inline editor, and offering its menu (Apply to Prefab included) for a click on a nested
                // field would act on an object the user did not right-click.
                if (mismatch) return null;

                if (el == stopAt) break;
            }

            var chain = QuickEditMenu.ComputeAncestorChain(deepTarget, stopAt);
            foreach (var entry in chain)
            {
                if (!(entry.Node is BindingNode) || entry.Element == null) continue;
                string found = null;
                entry.Element.Query<VisualElement>().ForEach(ve =>
                {
                    if (found == null && ve is IBindable ib && !string.IsNullOrEmpty(ib.bindingPath))
                        found = ib.bindingPath;
                });
                if (found != null) return found;

                found = BindingFactory.ReadStampedPropertyPath(entry.Element, expectedOwner, out _);
                if (!string.IsNullOrEmpty(found)) return found;

                entry.Element.Query<VisualElement>().ForEach(ve =>
                {
                    if (found == null) found = BindingFactory.ReadStampedPropertyPath(ve, expectedOwner, out _);
                });
                if (!string.IsNullOrEmpty(found)) return found;
            }
            return null;
        }
    }
}
