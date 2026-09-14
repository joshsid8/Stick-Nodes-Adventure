using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>Evaluates a single <see cref="VisualCondition"/> against a serialized object (pure, UI-free).</summary>
    public static class ConditionEvaluator
    {
        public static bool SuppressWarnings = false;

        // Dedups the "path not found" warning. An unresolvable condition path (renamed/deleted/mistyped field)
        // is re-evaluated on a 200ms member-driver poll (see ConditionBinder), so without this it logs ~5×/sec
        // forever. Keyed by target type + path so each distinct broken condition surfaces exactly once per
        // session (cleared on domain reload). Mirrors SasquatchIcons' one-warning-per-id pattern.
        private static readonly HashSet<string> _warnedMissingPaths = new HashSet<string>();

        /// <summary>Test-only: clears the one-warning-per-path ledger, so "warns once per path" can be told apart from a
        /// global mute.</summary>
        internal static void ResetWarningDedupForTests() => _warnedMissingPaths.Clear();

        /// <summary>Evaluates <paramref name="condition"/> against <paramref name="serializedObject"/> using the condition's own property path.</summary>
        public static bool Evaluate(VisualCondition condition, SerializedObject serializedObject)
        {
            return Evaluate(condition, serializedObject, condition.PropertyPath);
        }

        // resolvedPath is condition.PropertyPath already scoped to the node's base path, so conditions
        // inside a collection ItemLayout resolve against the indexed element (e.g. list.Array.data[2].x).
        /// <summary>Evaluates <paramref name="condition"/> against <paramref name="resolvedPath"/> — the condition's path already scoped to the node's base path (so item-layout conditions hit the indexed element).</summary>
        public static bool Evaluate(VisualCondition condition, SerializedObject serializedObject, string resolvedPath)
        {
            if (string.IsNullOrEmpty(resolvedPath)) return true;

            var prop = serializedObject.FindProperty(resolvedPath);
            if (prop == null)
            {
                // Bool-member driver: the driver is a computed bool property / parameterless bool method rather than
                // a serialized field (e.g. ShowIf("IsValid")). Only attempted at top level — resolvedPath still
                // equals the raw driver name — so scoped collection-item paths never accidentally hit a root member.
                if (resolvedPath == condition.PropertyPath &&
                    TryEvaluateBoolMember(serializedObject, condition, out bool memberResult))
                {
                    return memberResult;
                }

                // BUILT-IN driver (IsInPlayMode): EDITOR state, not a member of the inspected object. Tried LAST,
                // so a class that already carries its own member of that name keeps it — and tried BEFORE the
                // warning below, because a built-in has no SerializedProperty and is therefore re-evaluated on the
                // 200ms poll: falling through would log "property path not found" five times a second, forever.
                // Keyed on the condition's own path, not the resolved one, so it holds even if a future caller
                // scopes it. Built-ins read as bool drivers, so they take the same operator fold.
                if (ConditionDrivers.IsPlayMode(condition.PropertyPath))
                    return ApplyBoolOperator(EditorApplication.isPlaying, condition.Op, condition.Value);

                if (!SuppressWarnings)
                {
                    // UnityEngine.Object '!= null' safely covers a destroyed target (poll↔detach race), so
                    // reading .name/.GetType() below can't throw MissingReferenceException.
                    var target = serializedObject.targetObject;
                    string targetName = target != null ? target.name : "<destroyed>";
                    string targetType = target != null ? target.GetType().FullName : "<unknown>";
                    if (_warnedMissingPaths.Add(targetType + "|" + resolvedPath))
                    {
                        Debug.LogWarning($"[Sasquatch Inspector] Condition: property path '{resolvedPath}' not found on " +
                                         $"target {targetName} — check for a renamed, deleted, or mistyped field/member. " +
                                         "(Shown once per type + path.)");
                    }
                }
                return true;
            }

            // Multi-object check
            if (serializedObject.isEditingMultipleObjects && prop.hasMultipleDifferentValues)
            {
                switch (condition.Policy)
                {
                    case MixedValuePolicy.HideDependent:
                        return false;
                    case MixedValuePolicy.ShowDependent:
                        return true;
                    case MixedValuePolicy.EvaluatePrimeTarget:
                        // Fall through to evaluate prime target
                        break;
                }
            }

            // Evaluate value
            return EvaluateProperty(prop, condition.Op, condition.Value);
        }

        /// <summary>
        /// Renders the property at <paramref name="resolvedPath"/> as short display text for the condition debugger
        /// (e.g. "TRUE", "42", "Ranged", "\"hello\""). Pure and warning-free — never logs (independent of
        /// <see cref="SuppressWarnings"/>); returns sentinel text for missing/mixed paths.
        /// </summary>
        public static string DescribeCurrentValue(SerializedObject serializedObject, string resolvedPath)
        {
            if (string.IsNullOrEmpty(resolvedPath)) return "(no path)";

            var prop = serializedObject.FindProperty(resolvedPath);
            if (prop == null)
            {
                // Bool-member driver (see Evaluate): render its live value for the debugger.
                if (TryGetBoolMemberValue(serializedObject.targetObject, resolvedPath, out bool memberValue))
                    return memberValue ? "TRUE" : "FALSE";
                // Built-in driver, in the same last-place order Evaluate resolves it.
                if (ConditionDrivers.IsPlayMode(resolvedPath))
                    return EditorApplication.isPlaying ? "TRUE" : "FALSE";
                return "<missing>";
            }

            if (serializedObject.isEditingMultipleObjects && prop.hasMultipleDifferentValues) return "(mixed)";

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return prop.boolValue ? "TRUE" : "FALSE";
                case SerializedPropertyType.Integer:
                    return prop.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Float:
                    return prop.doubleValue.ToString("0.###", CultureInfo.InvariantCulture);
                case SerializedPropertyType.String:
                    return string.IsNullOrEmpty(prop.stringValue) ? "\"\"" : $"\"{prop.stringValue}\"";
                case SerializedPropertyType.Enum:
                    if (prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length)
                        return prop.enumDisplayNames[prop.enumValueIndex];
                    return prop.enumValueFlag.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "null";
                default:
                    return prop.propertyType.ToString();
            }
        }

        private static bool EvaluateProperty(SerializedProperty prop, ConditionOperator op, string targetValue)
        {
            switch (op)
            {
                case ConditionOperator.IsNull:
                    return prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue == null;
                case ConditionOperator.IsNotNull:
                    return prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue != null;
                case ConditionOperator.IsTrue:
                    return prop.propertyType == SerializedPropertyType.Boolean && prop.boolValue;
                case ConditionOperator.IsFalse:
                    return prop.propertyType == SerializedPropertyType.Boolean && !prop.boolValue;

                case ConditionOperator.Equals:
                    return CompareValue(prop, targetValue, true);
                case ConditionOperator.NotEquals:
                    return !CompareValue(prop, targetValue, true);

                case ConditionOperator.GreaterThan:
                    return TryCompareNumeric(prop, targetValue, out int gt) && gt > 0;
                case ConditionOperator.GreaterThanOrEqual:
                    return TryCompareNumeric(prop, targetValue, out int ge) && ge >= 0;
                case ConditionOperator.LessThan:
                    return TryCompareNumeric(prop, targetValue, out int lt) && lt < 0;
                case ConditionOperator.LessThanOrEqual:
                    return TryCompareNumeric(prop, targetValue, out int le) && le <= 0;

                default:
                    return true;
            }
        }

        private static bool CompareValue(SerializedProperty prop, string targetValue, bool caseInsensitive = true)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    if (bool.TryParse(targetValue, out bool bVal))
                        return prop.boolValue == bVal;
                    return false;

                case SerializedPropertyType.Integer:
                    if (long.TryParse(targetValue, out long iVal))
                        return prop.longValue == iVal;
                    return false;

                case SerializedPropertyType.Float:
                    // Invariant: condition values are authored/serialized with a '.' decimal, so
                    // parsing under a comma-decimal locale would silently mis-evaluate the comparison.
                    if (double.TryParse(targetValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double fVal))
                        return Mathf.Approximately((float)prop.doubleValue, (float)fVal);
                    return false;

                case SerializedPropertyType.String:
                    return string.Equals(prop.stringValue, targetValue, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

                case SerializedPropertyType.Enum:
                    {
                        Type enumType = null;
                        var fieldInfo = BindingFactory.GetFieldInfoFromPropertyPath(prop.serializedObject.targetObject.GetType(), prop.propertyPath, out var resolvedType);
                        if (resolvedType != null && resolvedType.IsEnum)
                        {
                            enumType = resolvedType;
                        }
                        else if (resolvedType != null && resolvedType.IsArray && resolvedType.GetElementType().IsEnum)
                        {
                            enumType = resolvedType.GetElementType();
                        }
                        else if (resolvedType != null && resolvedType.IsGenericType && resolvedType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>) && resolvedType.GetGenericArguments()[0].IsEnum)
                        {
                            enumType = resolvedType.GetGenericArguments()[0];
                        }

                        if (enumType != null)
                        {
                            try
                            {
                                if (Enum.TryParse(enumType, targetValue, true, out var parsedValue))
                                {
                                    long targetMask = Convert.ToInt64(parsedValue);
                                    long currentMask = prop.enumValueFlag;

                                    if (enumType.IsDefined(typeof(FlagsAttribute), true))
                                    {
                                        if (targetMask == 0)
                                        {
                                            return currentMask == 0;
                                        }
                                        return (currentMask & targetMask) == targetMask;
                                    }
                                    else
                                    {
                                        return currentMask == targetMask;
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                // Fall through to simple index check on failure
                            }
                        }

                        if (int.TryParse(targetValue, out int enumFlagValue))
                        {
                            return prop.enumValueFlag == enumFlagValue;
                        }
                        if (prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length)
                        {
                            string enumDisplay = prop.enumDisplayNames[prop.enumValueIndex];
                            string enumName = prop.enumNames[prop.enumValueIndex];
                            return string.Equals(enumDisplay, targetValue, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(enumName, targetValue, StringComparison.OrdinalIgnoreCase);
                        }
                        return false;
                    }

                case SerializedPropertyType.ObjectReference:
                    if (targetValue == "null" || string.IsNullOrEmpty(targetValue))
                        return prop.objectReferenceValue == null;
                    return prop.objectReferenceValue != null &&
                           string.Equals(prop.objectReferenceValue.name, targetValue, StringComparison.OrdinalIgnoreCase);

                default:
                    return false;
            }
        }

        // True only when both sides are numerically comparable (numeric property + parseable target), with the
        // sign of the comparison in cmp. Returning false for non-numeric/unparseable is what keeps >= and <= from
        // spuriously matching (a plain "cmp == 0" default would make "≥"/"≤" true for strings, enums, etc.).
        private static bool TryCompareNumeric(SerializedProperty prop, string targetValue, out int cmp)
        {
            cmp = 0;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (long.TryParse(targetValue, out long iVal))
                    {
                        cmp = prop.longValue.CompareTo(iVal);
                        return true;
                    }
                    break;
                case SerializedPropertyType.Float:
                    if (double.TryParse(targetValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double fVal))
                    {
                        cmp = prop.doubleValue.CompareTo(fVal);
                        return true;
                    }
                    break;
            }
            return false;
        }

        // ---- Bool-member drivers: a computed bool property / parameterless bool method used as a condition
        // driver instead of a serialized field, e.g. ShowIf("IsValid"). Mirrors ValidationBinder's "reflection can
        // never break the inspector" contract — any resolution/invocation failure returns false (member unresolved),
        // and Evaluate then treats the term like a missing property.

        private static bool TryEvaluateBoolMember(SerializedObject serializedObject, VisualCondition condition, out bool result)
        {
            result = false;
            var target = serializedObject != null ? serializedObject.targetObject : null;
            if (target == null) return false;
            if (!TryGetBoolMemberValue(target, condition.PropertyPath, out bool value)) return false;
            result = ApplyBoolOperator(value, condition.Op, condition.Value);
            return true;
        }

        // Applies a condition operator to a bool driver value. The designer constrains member-driver operators to
        // is-true / is-false; Equals/NotEquals are handled too for robustness (empty/unparseable value ⇒ "is true").
        private static bool ApplyBoolOperator(bool value, ConditionOperator op, string targetValue)
        {
            switch (op)
            {
                case ConditionOperator.IsFalse:
                    return !value;
                case ConditionOperator.NotEquals:
                    return bool.TryParse(targetValue, out bool nb) ? value != nb : !value;
                case ConditionOperator.IsTrue:
                case ConditionOperator.Equals:
                default:
                    return bool.TryParse(targetValue, out bool b) ? value == b : value;
            }
        }

        // (type, member name) → the resolved bool accessor, or a cached "there isn't one". A condition whose
        // driver is a computed property/method has no SerializedProperty to track, so ConditionBinder polls it
        // every 200ms for as long as the field is on screen — and re-resolving the member BY NAME was the whole
        // body of that poll. The reflection tables cannot change without a domain reload, which is also what
        // clears this dictionary.
        //
        // Resolution and INVOCATION stay separate on purpose: a driver that throws must not be remembered as
        // "no such member", because the next poll may well succeed. Only the lookup is memoised.
        private readonly struct BoolMember
        {
            public readonly PropertyInfo Property;
            public readonly MethodInfo Method;
            public BoolMember(PropertyInfo p, MethodInfo m) { Property = p; Method = m; }
        }

        private static readonly Dictionary<(Type, string), BoolMember> _boolMembers =
            new Dictionary<(Type, string), BoolMember>();

        private static BoolMember ResolveBoolMember(Type type, string memberName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                var pi = type.GetProperty(memberName, flags);
                if (pi != null && pi.PropertyType == typeof(bool) && pi.CanRead) return new BoolMember(pi, null);
            }
            catch
            {
                // Mirrors the original control flow exactly: a throwing property lookup (an ambiguous name)
                // gave up entirely rather than falling through to the method lookup.
                return default;
            }

            try
            {
                // Bind to the parameterless overload explicitly so an overloaded name can't throw AmbiguousMatch.
                var mi = type.GetMethod(memberName, flags, null, Type.EmptyTypes, null);
                if (mi != null && mi.ReturnType == typeof(bool)) return new BoolMember(null, mi);
            }
            catch { return default; }

            return default;
        }

        /// <summary>Test seam: the memoised member-driver read, isolated from the path resolution around it.</summary>
        internal static bool TryGetBoolMemberValueForTests(object target, string memberName, out bool value)
            => TryGetBoolMemberValue(target, memberName, out value);

        private static bool TryGetBoolMemberValue(object target, string memberName, out bool value)
        {
            value = false;
            if (target == null || string.IsNullOrEmpty(memberName)) return false;

            var key = (target.GetType(), memberName);
            if (!_boolMembers.TryGetValue(key, out var member))
            {
                member = ResolveBoolMember(key.Item1, memberName);
                _boolMembers[key] = member;
            }

            if (member.Property != null)
            {
                try { value = (bool)member.Property.GetValue(target); return true; }
                catch { return false; }
            }

            if (member.Method != null)
            {
                try { value = (bool)member.Method.Invoke(target, null); return true; }
                catch { return false; }
            }

            return false;
        }
    }
}
