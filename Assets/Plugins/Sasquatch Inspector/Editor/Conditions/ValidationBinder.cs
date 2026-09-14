using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Inline validation for binding/collection nodes — Required-field warnings, numeric min/max range alerts,
    /// and custom ValidateInput-style method messages — surfaced as a HelpBox under the field plus a red outline
    /// (<c>sasq-warning-highlight</c>) on the field itself.
    /// <para>
    /// <see cref="Evaluate"/> is the PURE decision function (no UI, no panel). <see cref="Attach"/> is the
    /// live-UI wiring: it wraps the field and re-runs Evaluate via TrackPropertyValue, which only polls once
    /// the element is attached to a panel.
    /// </para>
    /// </summary>
    public static class ValidationBinder
    {
        public const string WarningHighlightClass = "sasq-warning-highlight";

        // Dedups validation-method warnings. Evaluate() re-runs on every value change AND relayout
        // (TrackPropertyValue + GeometryChangedEvent), so a broken/ambiguous/missing method would otherwise
        // re-log endlessly. Keyed per type+method (+ error message for thrown exceptions) so each distinct
        // problem surfaces once per session (cleared on domain reload / recompile).
        private static readonly HashSet<string> _warnedValidationIssues = new HashSet<string>();

        /// <summary>Test-only: clears the one-warning-per-issue ledger, which is otherwise per-session.</summary>
        internal static void ResetWarningDedupForTests() => _warnedValidationIssues.Clear();

        private static void WarnValidationOnce(string key, string message, bool asError)
        {
            if (!_warnedValidationIssues.Add(key)) return;
            if (asError) Debug.LogError(message); else Debug.LogWarning(message);
        }

        /// <summary>The outcome of evaluating a node's validation rules: whether there's an error and the message to show.</summary>
        public struct ValidationResult
        {
            public bool HasError;
            public string Message;
            // true => Error severity (custom method failed); false => Warning severity (required / range).
            public bool IsError;

            public static ValidationResult None => new ValidationResult { HasError = false };
            public static ValidationResult Warning(string message) =>
                new ValidationResult { HasError = true, Message = message, IsError = false };
            public static ValidationResult Error(string message) =>
                new ValidationResult { HasError = true, Message = message, IsError = true };
        }

        // True when the node has any validation rule configured (so callers can skip the wiring entirely).
        /// <summary>True when <paramref name="node"/> has any validation configured.</summary>
        public static bool NeedsValidation(LayoutNode node)
        {
            if (node is BindingNode b)
                return b.Required || b.ValidateRange || !string.IsNullOrEmpty(b.ValidationMethod);
            if (node is CollectionNode c)
                return c.Required || !string.IsNullOrEmpty(c.ValidationMethod);
            return false;
        }

        // Pure evaluation: compute the validation state for a node against a serialized property.
        // No UI, no side effects — safe to call from tests. Checks run in order (Required → Range →
        // custom method) and the first failure wins.
        /// <summary>Runs <paramref name="node"/>'s validation against <paramref name="prop"/> and returns the result (pure; no UI).</summary>
        public static ValidationResult Evaluate(LayoutNode node, SerializedProperty prop)
        {
            if (node == null || prop == null) return ValidationResult.None;

            BindingNode bindingNode = node as BindingNode;
            CollectionNode collectionNode = node as CollectionNode;

            // 1. Required check.
            if (bindingNode != null && bindingNode.Required)
            {
                bool isEmpty =
                    (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue == null) ||
                    (prop.propertyType == SerializedPropertyType.String && string.IsNullOrWhiteSpace(prop.stringValue));

                if (isEmpty)
                {
                    return ValidationResult.Warning(string.IsNullOrEmpty(bindingNode.RequiredMessage)
                        ? "This field is required."
                        : bindingNode.RequiredMessage);
                }
            }
            else if (collectionNode != null && collectionNode.Required)
            {
                if (prop.isArray && prop.arraySize == 0)
                {
                    return ValidationResult.Warning(string.IsNullOrEmpty(collectionNode.RequiredMessage)
                        ? "This collection cannot be empty."
                        : collectionNode.RequiredMessage);
                }
            }

            // 2. Min/Max range check (numeric BindingNode only). Skipped entirely when Clamp to Range is on —
            // the value is coerced back in-range silently, so a persistent warning would only flash
            // for the frame before the clamp lands. Warn-only mode (Validate without Clamp) still warns. Each
            // bound is honored independently so a one-sided rule (min-only / max-only) only checks its side.
            if (bindingNode != null && bindingNode.ValidateRange && !bindingNode.ClampToRange)
            {
                bool hasMin = bindingNode.RangeHasMin;
                bool hasMax = bindingNode.RangeHasMax;
                bool outOfRange = false;
                if (prop.propertyType == SerializedPropertyType.Integer)
                {
                    long val = prop.longValue;
                    if ((hasMin && val < bindingNode.ValidationMin) || (hasMax && val > bindingNode.ValidationMax)) outOfRange = true;
                }
                else if (prop.propertyType == SerializedPropertyType.Float)
                {
                    double val = prop.doubleValue;
                    if ((hasMin && val < bindingNode.ValidationMin) || (hasMax && val > bindingNode.ValidationMax)) outOfRange = true;
                }

                if (outOfRange)
                {
                    return ValidationResult.Warning(string.IsNullOrEmpty(bindingNode.RangeValidationMessage)
                        ? DefaultRangeText(bindingNode.ValidationMin, bindingNode.ValidationMax, hasMin, hasMax)
                        : bindingNode.RangeValidationMessage);
                }
            }

            // 3. Custom ValidateInput-style method check.
            string valMethod = bindingNode != null ? bindingNode.ValidationMethod
                             : collectionNode != null ? collectionNode.ValidationMethod : null;
            string valMsg = bindingNode != null ? bindingNode.ValidationMessage
                          : collectionNode != null ? collectionNode.ValidationMessage : null;

            if (!string.IsNullOrEmpty(valMethod))
            {
                // On any reflection failure the field is treated as valid so it's never stuck.
                if (TryInvokeValidationMethod(valMethod, prop, out bool isValid) && !isValid)
                {
                    return ValidationResult.Error(string.IsNullOrEmpty(valMsg) ? "Validation failed." : valMsg);
                }
            }

            return ValidationResult.None;
        }

        // Default out-of-range warning text, worded for whichever bound(s) are active (min-only / max-only /
        // both). Used only in warn-only mode (Clamp off) when the author hasn't supplied a custom message.
        private static string DefaultRangeText(float min, float max, bool hasMin, bool hasMax)
        {
            if (hasMin && hasMax) return $"Value must be between {min} and {max}.";
            if (hasMin) return $"Value must be at least {min}.";
            if (hasMax) return $"Value must be at most {max}.";
            return "Value is unconstrained.";
        }

        // (target type, method name) → the user's validation method, or null for "resolved, and there isn't
        // one". Memoised because validation re-runs on every layout pass and on every value change, while the
        // reflection tables it searches cannot change without a domain reload — which is also what clears this
        // dictionary, so a recompiled script always re-resolves.
        //
        // Caching the MISS matters as much as caching the hit: a typo'd or removed method name is exactly the
        // case that re-ran the full GetMethod lookup forever. Nothing is lost by it — both failure modes warn
        // through WarnValidationOnce, which already collapsed every repeat after the first.
        private static readonly Dictionary<(Type, string), MethodInfo> _validationMethods =
            new Dictionary<(Type, string), MethodInfo>();

        /// <summary>Test seam: the memoised lookup, so its exactness twin can compare it to fresh reflection.</summary>
        internal static MethodInfo ResolveValidationMethodForTests(Type targetType, string methodName)
            => ResolveValidationMethod(targetType, methodName);

        private static MethodInfo ResolveValidationMethod(Type targetType, string methodName)
        {
            var key = (targetType, methodName);
            if (_validationMethods.TryGetValue(key, out var cached)) return cached;

            MethodInfo method = null;
            try
            {
                method = targetType.GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (AmbiguousMatchException)
            {
                WarnValidationOnce($"ambiguous|{targetType.Name}|{methodName}",
                    $"[Sasquatch Inspector] Validation method '{methodName}' is ambiguous (overloaded) on {targetType.Name}; skipping.", asError: false);
                _validationMethods[key] = null;
                return null;
            }

            if (method == null)
            {
                WarnValidationOnce($"missing|{targetType.Name}|{methodName}",
                    $"[Sasquatch Inspector] Validation method '{methodName}' not found on {targetType.Name}.", asError: false);
            }

            _validationMethods[key] = method;
            return method;
        }

        // Resolves + invokes the user's bool validation method (parameterless, or single-arg matching the
        // field type). Returns false on any reflection problem — a missing name, an AmbiguousMatchException
        // (overloaded method), an unsupported signature, a non-bool return, or a thrown exception — so a
        // typo can never escape into and break the inspector build. On success, isValid carries the result.
        private static bool TryInvokeValidationMethod(string methodName, SerializedProperty prop, out bool isValid)
        {
            isValid = true;
            var targetObj = prop.serializedObject.targetObject;
            if (targetObj == null) return false;

            var method = ResolveValidationMethod(targetObj.GetType(), methodName);
            if (method == null) return false; // resolution already warned once; see ResolveValidationMethod

            try
            {
                var parameters = method.GetParameters();
                object result;
                if (parameters.Length == 0)
                {
                    result = method.Invoke(targetObj, null);
                }
                else if (parameters.Length == 1)
                {
                    result = method.Invoke(targetObj, new[] { GetPropertyValue(prop) });
                }
                else
                {
                    return false; // unsupported signature
                }

                if (result is bool b)
                {
                    isValid = b;
                    return true;
                }
                return false; // non-bool return
            }
            catch (Exception ex)
            {
                // Keyed by message too, so a method that throws differently for different values still surfaces
                // each distinct error once (rather than collapsing to a single warning for the whole method).
                WarnValidationOnce($"invoke|{targetObj.GetType().Name}|{methodName}|{ex.Message}",
                    $"[Sasquatch Inspector] Error invoking validation method '{methodName}': {ex.Message}", asError: true);
                return false;
            }
        }

        private static object GetPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return (int)prop.longValue;
                case SerializedPropertyType.Float: return (float)prop.doubleValue;
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.ObjectReference: return prop.objectReferenceValue;
                case SerializedPropertyType.Boolean: return prop.boolValue;
                default: return null;
            }
        }

        // Wires inline validation into the live UI and returns the element the caller should insert in
        // place of fieldElement. No-ops (returns fieldElement unchanged) in design mode, when there's no
        // validation configured, or when the path can't be resolved. In a table cell only the red outline
        // is applied (no room for a HelpBox); otherwise the field is wrapped in a column with a HelpBox
        // beneath it. Re-evaluation is driven by TrackPropertyValue, so it never rebuilds the panel.
        /// <summary>Attaches a live validation message + field highlight under the field, re-evaluating as the value changes.</summary>
        public static VisualElement Attach(
            VisualElement fieldElement,
            LayoutNode node,
            string resolvedPath,
            SerializedObject serializedObject,
            bool designMode,
            bool isTableCell = false)
        {
            if (designMode || fieldElement == null || string.IsNullOrEmpty(resolvedPath) || serializedObject == null)
                return fieldElement;

            if (!NeedsValidation(node)) return fieldElement;

            var prop = serializedObject.FindProperty(resolvedPath);
            if (prop == null) return fieldElement;

            VisualElement targetForHighlight = fieldElement;
            VisualElement container = null;
            HelpBox validationHelpBox = null;
            VisualElement currentHighlightTarget = null;

            if (!isTableCell)
            {
                container = new VisualElement();
                container.AddToClassList("sasq-validation-wrapper");
                container.style.flexDirection = FlexDirection.Column;
                container.style.alignSelf = Align.Stretch;
                container.style.minWidth = 0;
                container.style.flexShrink = 1f;

                // Carry the field's layout onto the wrapper so it sits where the field used to, then let the
                // field fill the wrapper.
                container.style.marginTop = fieldElement.style.marginTop;
                container.style.marginBottom = fieldElement.style.marginBottom;
                container.style.marginLeft = fieldElement.style.marginLeft;
                container.style.marginRight = fieldElement.style.marginRight;
                container.style.flexGrow = fieldElement.style.flexGrow;
                container.style.flexShrink = fieldElement.style.flexShrink;
                container.style.width = fieldElement.style.width;
                container.style.height = fieldElement.style.height;
                container.style.alignSelf = fieldElement.style.alignSelf;

                fieldElement.style.marginTop = 0;
                fieldElement.style.marginBottom = 0;
                fieldElement.style.marginLeft = 0;
                fieldElement.style.marginRight = 0;
                fieldElement.style.flexGrow = 1f;
                fieldElement.style.flexShrink = 1f;
                fieldElement.style.minWidth = 0;
                fieldElement.style.alignSelf = Align.Stretch;

                container.Add(fieldElement);

                validationHelpBox = new HelpBox("", HelpBoxMessageType.Warning);
                validationHelpBox.style.marginTop = 4;
                validationHelpBox.style.marginBottom = 4;
                validationHelpBox.style.display = DisplayStyle.None;
                container.Add(validationHelpBox);
            }

            Action reevaluate = () =>
            {
                var result = Evaluate(node, prop);
                targetForHighlight = ResolveHighlightTarget(fieldElement);
                if (result.HasError)
                {
                    if (validationHelpBox != null)
                    {
                        validationHelpBox.text = result.Message;
                        validationHelpBox.messageType = result.IsError ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning;
                        validationHelpBox.style.display = DisplayStyle.Flex;
                    }
                    if (currentHighlightTarget != null && currentHighlightTarget != targetForHighlight)
                    {
                        currentHighlightTarget.RemoveFromClassList(WarningHighlightClass);
                    }
                    targetForHighlight.AddToClassList(WarningHighlightClass);
                    currentHighlightTarget = targetForHighlight;
                }
                else
                {
                    if (validationHelpBox != null)
                    {
                        validationHelpBox.style.display = DisplayStyle.None;
                    }
                    if (currentHighlightTarget != null)
                    {
                        currentHighlightTarget.RemoveFromClassList(WarningHighlightClass);
                        currentHighlightTarget = null;
                    }
                    targetForHighlight.RemoveFromClassList(WarningHighlightClass);
                }
            };

            // Enforced range ("Clamp to Range"): coerce the value back into the active bound(s). Clamps LIVE on
            // every value change (so a scrub / drag pins at the bound) — but only while the field isn't
            // being typed into, so partial keystrokes (typing "150" into a min-100 field passes through "1")
            // aren't fought mid-entry. Focus-out (typing commit) and pointer-up (scrub end) are safety nets that
            // catch the value even if the live path was skipped. Clamps are scheduled a tick out so ApplyModified
            // doesn't re-enter the tracker mid-callback.
            bool clampActive = node is BindingNode cn && cn.ClampToRange && cn.ValidateRange
                               && (cn.RangeHasMin || cn.RangeHasMax);
            BindingNode clampNode = clampActive ? (BindingNode)node : null;
            void DoClamp() => fieldElement.schedule.Execute(() =>
                ClampNumericProperty(prop, clampNode.ValidationMin, clampNode.ValidationMax,
                    clampNode.RangeHasMin, clampNode.RangeHasMax));

            fieldElement.TrackPropertyValueWithRefresh(prop, _ =>
            {
                reevaluate();
                if (clampActive && !IsTextInputFocused(fieldElement)) DoClamp();
            });
            fieldElement.RegisterCallback<GeometryChangedEvent>(_ => reevaluate());
            reevaluate();

            if (clampActive)
            {
                // Smooth live clamp: pin the value at the bound synchronously as it changes (so a label-scrub
                // drags right up to the edge and STAYS there instead of flailing past it while the deferred
                // DoClamp catches up a frame later). Focus-out / pointer-up / TrackPropertyValue keep DoClamp
                // as the safety net for typed commits and programmatic changes.
                WireSmoothClamp(fieldElement, prop,
                    clampNode.ValidationMin, clampNode.ValidationMax, clampNode.RangeHasMin, clampNode.RangeHasMax);
                fieldElement.RegisterCallback<FocusOutEvent>(_ => DoClamp());
                fieldElement.RegisterCallback<PointerUpEvent>(_ => DoClamp());
            }

            return isTableCell ? fieldElement : container;
        }

        // Pins a numeric field's value at the active bound(s) SYNCHRONOUSLY as it changes, so an out-of-range
        // value is never painted mid-scrub (the flail the deferred clamp alone can't prevent). Skips while the
        // text input is focused so typing isn't fought — the focus-out / pointer-up nets clamp that path on
        // commit. When the value is out of range: SetValueWithoutNotify pins the display in the same frame, the
        // property is written directly, and StopImmediatePropagation keeps the two-way binding from overwriting
        // it with the raw drag value (order-independent — if the binding already wrote it, our write wins).
        private static void WireSmoothClamp(VisualElement fieldElement, SerializedProperty prop,
            float min, float max, bool hasMin, bool hasMax)
        {
            if (prop.propertyType == SerializedPropertyType.Integer)
            {
                fieldElement.RegisterCallback<ChangeEvent<int>>(evt =>
                {
                    if (IsTextInputFocused(fieldElement)) return;
                    long clamped = ClampLong(evt.newValue, min, max, hasMin, hasMax);
                    if (clamped == evt.newValue) return;
                    evt.StopImmediatePropagation();
                    (evt.target as INotifyValueChanged<int>)?.SetValueWithoutNotify((int)clamped);
                    if (prop.longValue != clamped) { prop.longValue = clamped; prop.serializedObject.ApplyModifiedProperties(); }
                });
                fieldElement.RegisterCallback<ChangeEvent<long>>(evt =>
                {
                    if (IsTextInputFocused(fieldElement)) return;
                    long clamped = ClampLong(evt.newValue, min, max, hasMin, hasMax);
                    if (clamped == evt.newValue) return;
                    evt.StopImmediatePropagation();
                    (evt.target as INotifyValueChanged<long>)?.SetValueWithoutNotify(clamped);
                    if (prop.longValue != clamped) { prop.longValue = clamped; prop.serializedObject.ApplyModifiedProperties(); }
                });
            }
            else if (prop.propertyType == SerializedPropertyType.Float)
            {
                fieldElement.RegisterCallback<ChangeEvent<float>>(evt =>
                {
                    if (IsTextInputFocused(fieldElement)) return;
                    double clamped = ClampDouble(evt.newValue, min, max, hasMin, hasMax);
                    if (Mathf.Approximately((float)clamped, evt.newValue)) return;
                    evt.StopImmediatePropagation();
                    (evt.target as INotifyValueChanged<float>)?.SetValueWithoutNotify((float)clamped);
                    if (!Mathf.Approximately((float)prop.doubleValue, (float)clamped)) { prop.doubleValue = clamped; prop.serializedObject.ApplyModifiedProperties(); }
                });
                fieldElement.RegisterCallback<ChangeEvent<double>>(evt =>
                {
                    if (IsTextInputFocused(fieldElement)) return;
                    double clamped = ClampDouble(evt.newValue, min, max, hasMin, hasMax);
                    if (Mathf.Approximately((float)clamped, (float)evt.newValue)) return;
                    evt.StopImmediatePropagation();
                    (evt.target as INotifyValueChanged<double>)?.SetValueWithoutNotify(clamped);
                    if (!Mathf.Approximately((float)prop.doubleValue, (float)clamped)) { prop.doubleValue = clamped; prop.serializedObject.ApplyModifiedProperties(); }
                });
            }
        }

        // Coerces a numeric serialized property against whichever bound(s) are active. Each side is applied only
        // when its flag is set, so a min-only rule never touches the top (and vice-versa). Integer fields snap to
        // the inclusive integer sub-range. No-ops for non-numeric properties, when no bound is active, or when
        // already in range.
        private static void ClampNumericProperty(SerializedProperty prop, float min, float max, bool hasMin, bool hasMax)
        {
            if (prop == null || (!hasMin && !hasMax)) return;

            if (prop.propertyType == SerializedPropertyType.Integer)
            {
                long v = prop.longValue;
                long clamped = ClampLong(v, min, max, hasMin, hasMax);
                if (clamped != v)
                {
                    prop.longValue = clamped;
                    prop.serializedObject.ApplyModifiedProperties();
                }
            }
            else if (prop.propertyType == SerializedPropertyType.Float)
            {
                double v = prop.doubleValue;
                double clamped = ClampDouble(v, min, max, hasMin, hasMax);
                if (!Mathf.Approximately((float)clamped, (float)v))
                {
                    prop.doubleValue = clamped;
                    prop.serializedObject.ApplyModifiedProperties();
                }
            }
        }

        // Pure integer clamp against whichever bound(s) are active (each side only when its flag is set); snaps
        // to the inclusive integer sub-range. Normalizes a reversed two-sided window so min ≤ max.
        // Internal: the numeric stepper coerces its own presses through this same math (BindingFactory.StepNumeric)
        // rather than growing a second clamp that would drift.
        internal static long ClampLong(long v, float min, float max, bool hasMin, bool hasMax)
        {
            if (hasMin && hasMax && min > max) { float t = min; min = max; max = t; }
            long clamped = v;
            if (hasMin) { long lo = (long)System.Math.Ceiling(min); if (clamped < lo) clamped = lo; }
            if (hasMax) { long hi = (long)System.Math.Floor(max); if (clamped > hi) clamped = hi; }
            return clamped;
        }

        // Pure floating-point clamp against whichever bound(s) are active. Normalizes a reversed window so min ≤ max.
        internal static double ClampDouble(double v, float min, float max, bool hasMin, bool hasMax)
        {
            if (hasMin && hasMax && min > max) { float t = min; min = max; max = t; }
            double clamped = v;
            if (hasMin && clamped < min) clamped = min;
            if (hasMax && clamped > max) clamped = max;
            return clamped;
        }

        // True when the field's text editor currently holds keyboard focus (the user is typing) — as opposed to
        // a label scrub, which drives the value without focusing the text input. Lets the live clamp defer while
        // typing so partial keystrokes aren't fought. Defensive: any resolution failure returns false (clamp).
        private static bool IsTextInputFocused(VisualElement fieldElement)
        {
            var focused = fieldElement?.panel?.focusController?.focusedElement as VisualElement;
            if (focused == null) return false;
            // Numeric fields (Float/IntegerField) expose their text input as "unity-base-text-field__input";
            // plain TextFields use "unity-text-field__input". The editable TextElement is a descendant of it.
            var input = fieldElement.Q<VisualElement>(className: "unity-text-field__input")
                     ?? fieldElement.Q<VisualElement>(className: "unity-base-text-field__input");
            // A label-scrub focuses at most the field ROOT (an ancestor of the input), so it reads as "not
            // typing" and clamps live. Typing focuses the input (or its inner TextElement) → deferred.
            if (input != null) return input == focused || input.Contains(focused);
            // Unknown structure — fail safe: any focus inside the field counts as editing, so typing is never
            // fought (worst case a scrub clamps on release via the pointer-up / focus-out nets).
            return focused == fieldElement || fieldElement.Contains(focused);
        }

        private static VisualElement ResolveHighlightTarget(VisualElement fieldElement)
        {
            if (fieldElement == null) return null;

            return fieldElement.Q<VisualElement>(className: "unity-base-field__input")
                ?? fieldElement.Q<VisualElement>(className: "unity-text-field__input")
                ?? fieldElement.Q<VisualElement>(className: "unity-base-text-field__input")
                ?? fieldElement;
        }
    }
}
