using System;
using System.Collections.Generic;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor.Export
{
    /// <summary>
    /// Emits a node's <see cref="ConditionRule"/>s as C#: a private <c>ApplyConditions_*</c> method that toggles the
    /// element's display (Show/Hide) or enabled state (Enable/Disable), wired to run once immediately plus on a
    /// 150 ms poll (matching Sasquatch's own member-driver cadence, and correct for serialized drivers too). The
    /// comparison semantics are provided by two self-contained baked helpers (<see cref="BakedHelpers"/>) ported
    /// from <c>ConditionEvaluator</c> so the generated file needs nothing from the package.
    /// </summary>
    internal static class ConditionEmitter
    {
        /// <summary>Emits the per-element condition method + poll wiring. No-op when the node carries no usable rules.</summary>
        public static void Emit(CodeWriter w, EmitContext ctx, LayoutNode node, string elementVar, string basePath)
        {
            var rules = CollectRules(node);
            if (rules.Count == 0) return;

            bool hasShowHide = false, hasEnableDisable = false, hasHighlight = false;
            foreach (var r in rules)
            {
                switch (r.Outcome)
                {
                    case ConditionOutcome.Show:
                    case ConditionOutcome.Hide: hasShowHide = true; break;
                    case ConditionOutcome.Enable:
                    case ConditionOutcome.Disable: hasEnableDisable = true; break;
                    case ConditionOutcome.WarningHighlight: hasHighlight = true; break;
                }
            }
            if (!hasShowHide && !hasEnableDisable && !hasHighlight) return;

            string method = ctx.NewMethod("ApplyConditions_" + elementVar);

            // Inside a LIST ROW the drivers are sibling fields of the SAME element, so their paths only
            // exist per row. The rules are hoisted into their own method, which has none of the row's
            // locals in scope — so the element base travels as a PARAMETER, and the body reads it back as
            // `__base`. Without this the hoisted method referenced a variable that isn't there and the
            // exported file did not compile at all.
            string rowArg = ctx.RowBaseExpr;
            string signature = rowArg == null
                ? $"private void {method}(SerializedObject so, VisualElement v)"
                : $"private void {method}(SerializedObject so, VisualElement v, string __base)";
            string callArgs = rowArg == null ? $"so, {elementVar}" : $"so, {elementVar}, {rowArg}";

            // Wiring in the current (caller's) method body. The polling closure captures the row's base
            // expression, which is still in scope there — a container method's own `__base` parameter, or
            // the element-factory lambda's `__element`.
            w.Line($"{method}({callArgs});");
            w.Line($"{elementVar}.schedule.Execute(() => {method}({callArgs})).Every(150);");

            // The condition method itself.
            var mw = new CodeWriter();
            mw.Line(signature);
            mw.OpenBrace();
            if (hasShowHide) mw.Line("bool visible = true;");
            if (hasEnableDisable) mw.Line("bool enabled = true;");
            if (hasHighlight) mw.Line("bool highlight = false;");

            // The rule EXPRESSIONS are emitted into the hoisted method, where the base is its own `__base`
            // parameter — not whatever expression the call site used. Restored in the finally, since the
            // caller carries on emitting against its own scope afterwards.
            string prevRowBase = ctx.RowBaseExpr;
            if (rowArg != null) ctx.RowBaseExpr = "__base";
            try
            {
                int idx = 0;
                foreach (var rule in rules)
                {
                    string expr = EmitRuleExpr(ctx, rule, basePath);
                    string rv = "rule" + idx;
                    mw.Line($"bool {rv} = {expr};");
                    switch (rule.Outcome)
                    {
                        case ConditionOutcome.Show: mw.Line($"visible = visible && {rv};"); break;
                        case ConditionOutcome.Hide: mw.Line($"visible = visible && !{rv};"); break;
                        case ConditionOutcome.Enable: mw.Line($"enabled = enabled && {rv};"); break;
                        case ConditionOutcome.Disable: mw.Line($"enabled = enabled && !{rv};"); break;
                        case ConditionOutcome.WarningHighlight: mw.Line($"highlight = highlight || {rv};"); break;
                    }
                    idx++;
                }
            }
            finally { ctx.RowBaseExpr = prevRowBase; }

            if (hasShowHide)
            {
                // Through the display ARBITER, not a raw style write. The field search hides non-matching
                // elements on the same property, and this method re-runs on a 150 ms poll — a direct write here
                // would re-reveal a filtered-out field a sixth of a second after it collapsed, and clearing the
                // search would un-hide a condition-hidden one. SasqKit.SetHidden folds both reasons into one
                // mask (the twin of the package's ElementVisibility) and derives the display from the whole of
                // it, so the last word belongs to neither feature alone.
                mw.Line("SasqKit.SetHidden(v, SasqKit.HideCondition, !visible);");
                ctx.UseKit(null); // Core (SetHidden)
            }
            if (hasEnableDisable)
            {
                mw.Line("v.SetEnabled(enabled);");
                // SetEnabled's dim is subtree opacity, and UI Toolkit composites that PER ELEMENT — a slider
                // under a Disable outcome would go see-through (the track reads through the knob). Re-home it
                // in colour. Explicit because an enabled-state change fires no event of any kind, so this is
                // the only moment anything can react to the flip. A no-op with no slider underneath.
                mw.Line("SasqKit.RefreshDisabledKnobsUnder(v);");
                ctx.UseKit(null); // Core (RefreshDisabledKnobsUnder)
            }
            if (hasHighlight)
            {
                // The live '.sasq-warning-highlight' rule: a 1px #ef4444 border on the field. Cleared to the
                // stylesheet default when the highlight condition isn't met (poll re-applies each cycle).
                mw.Line("var __hl = new Color(0.937f, 0.267f, 0.267f, 1f);");
                mw.Line("if (highlight)");
                mw.OpenBrace();
                mw.Line("v.style.borderTopWidth = 1f; v.style.borderRightWidth = 1f; v.style.borderBottomWidth = 1f; v.style.borderLeftWidth = 1f;");
                mw.Line("v.style.borderTopColor = __hl; v.style.borderRightColor = __hl; v.style.borderBottomColor = __hl; v.style.borderLeftColor = __hl;");
                mw.CloseBrace();
                mw.Line("else");
                mw.OpenBrace();
                mw.Line("v.style.borderTopWidth = StyleKeyword.Null; v.style.borderRightWidth = StyleKeyword.Null; v.style.borderBottomWidth = StyleKeyword.Null; v.style.borderLeftWidth = StyleKeyword.Null;");
                mw.Line("v.style.borderTopColor = StyleKeyword.Null; v.style.borderRightColor = StyleKeyword.Null; v.style.borderBottomColor = StyleKeyword.Null; v.style.borderLeftColor = StyleKeyword.Null;");
                mw.CloseBrace();
            }
            mw.CloseBrace();
            ctx.Methods.Add(mw.ToString());
        }

        private static List<ConditionRule> CollectRules(LayoutNode node)
        {
            var result = new List<ConditionRule>();
            if (node.ConditionRules == null) return result;
            foreach (var r in node.ConditionRules)
            {
                if (r == null || r.Items == null || r.Items.Count == 0) continue;
                // A rule with at least one item that has at least one condition with a path.
                bool any = false;
                foreach (var item in r.Items)
                {
                    if (item?.Conditions == null) continue;
                    foreach (var c in item.Conditions)
                        if (!string.IsNullOrEmpty(c.PropertyPath)) { any = true; break; }
                    if (any) break;
                }
                if (any) result.Add(r);
            }
            return result;
        }

        private static string EmitRuleExpr(EmitContext ctx, ConditionRule rule, string basePath)
        {
            var parts = new List<string>();
            foreach (var item in rule.Items)
            {
                string p = EmitItemExpr(ctx, item, basePath);
                if (p != null) parts.Add(p);
            }
            if (parts.Count == 0) return "true";
            string joiner = rule.Match == ConditionMatch.All ? " && " : " || ";
            return string.Join(joiner, parts);
        }

        private static string EmitItemExpr(EmitContext ctx, ConditionGroup item, string basePath)
        {
            if (item?.Conditions == null || item.Conditions.Count == 0) return null;

            if (item.IsGroup)
            {
                var inner = new List<string>();
                foreach (var c in item.Conditions)
                {
                    string e = EmitCondExpr(ctx, c, basePath);
                    if (e != null) inner.Add(e);
                }
                if (inner.Count == 0) return null;
                string joiner = item.Match == ConditionMatch.All ? " && " : " || ";
                return "(" + string.Join(joiner, inner) + ")";
            }

            // Bare item: exactly one condition.
            return EmitCondExpr(ctx, item.Conditions[0], basePath);
        }

        private static string EmitCondExpr(EmitContext ctx, VisualCondition cond, string basePath)
        {
            if (string.IsNullOrEmpty(cond.PropertyPath)) return null;

            string full = Combine(basePath, cond.PropertyPath);
            int op = (int)cond.Op;
            string valLit = Lit.Str(cond.Value ?? "");
            string extra = CondExtraArgs(ctx, cond, full);
            // Inside a list row a condition's driver is a SIBLING FIELD OF THE SAME ELEMENT, whose path only
            // exists per row — so the path travels as the runtime expression, exactly as the widget's own
            // does. PathArg is inert everywhere else, where it is the baked literal this always was.
            string pathArg = ctx.PathArg(full);

            if (ctx.ResolveField(full) != null)
            {
                ctx.NeedsEvalCond = true;
                return $"(EvalCond(so.FindProperty({pathArg}), {op}, {valLit}{extra}))";
            }

            if (IsBoolMember(ctx, cond.PropertyPath))
            {
                ctx.NeedsBoolMember = true;
                return $"(EvalBoolMember(target, {Lit.Str(cond.PropertyPath)}, {op}, {valLit}))";
            }

            // A BUILT-IN driver (IsInPlayMode) is EDITOR state, not a member of the inspected object. Tried in the
            // same LAST place live tries it — after a real field and a real bool member — so the exported editor
            // shadows the name exactly as the live one does. Both sides of its comparison are known at export
            // (one bool, one authored operator/value), so it bakes straight down to a literal expression: no baked
            // helper, no reflection, nothing extra for the buyer's file to carry. The generated method already
            // re-runs on the 150 ms poll, which is what makes entering and leaving Play Mode take effect live.
            if (ConditionDrivers.IsPlayMode(cond.PropertyPath))
                return "(" + PlayModeExpr(cond) + ")";

            // Ambiguous at export (e.g. a member not resolvable here, or a static driver): decide at runtime.
            ctx.NeedsEvalCond = true;
            ctx.NeedsBoolMember = true;
            return $"(so.FindProperty({pathArg}) != null " +
                   $"? EvalCond(so.FindProperty({pathArg}), {op}, {valLit}{extra}) " +
                   $": EvalBoolMember(target, {Lit.Str(cond.PropertyPath)}, {op}, {valLit}))";
        }

        /// <summary>
        /// Bakes the play-mode built-in down to a literal <c>EditorApplication.isPlaying</c> test. Mirrors
        /// <c>ConditionEvaluator.ApplyBoolOperator</c> term for term — including its two fallbacks, where an
        /// unparseable value means "is true" for Equals and "is false" for NotEquals — so the exported editor and
        /// the live one can never disagree about what "IsInPlayMode = maybe" meant.
        /// </summary>
        private static string PlayModeExpr(VisualCondition cond)
        {
            bool want;
            switch (cond.Op)
            {
                case ConditionOperator.IsFalse:
                    want = false;
                    break;
                case ConditionOperator.NotEquals:
                    want = bool.TryParse(cond.Value, out bool nb) ? !nb : false;
                    break;
                default:    // IsTrue, Equals, and anything else — live folds them all the same way
                    want = bool.TryParse(cond.Value, out bool b) ? b : true;
                    break;
            }
            return want ? "EditorApplication.isPlaying" : "!EditorApplication.isPlaying";
        }

        /// <summary>
        /// The two things the generic string comparison cannot work out for itself, as NAMED arguments so only a
        /// deviation travels:
        ///
        ///  • <c>policy</c> — <see cref="MixedValuePolicy"/>. The exported class carries
        ///    <c>[CanEditMultipleObjects]</c>, so without it a two-object selection whose driver DISAGREES gets
        ///    the first object's answer and the buyer sees a field the live inspector deliberately withheld.
        ///    HideDependent is both the model default and the helper's, so the common case emits nothing.
        ///
        ///  • <c>flagsMask</c> / <c>isFlags</c> — a <c>[Flags]</c> driver. Live resolves the enum Type at runtime
        ///    and asks <c>(current &amp; target) == target</c>; without a mask the baked helper can only compare
        ///    the value NAME at <c>enumValueIndex</c>, which does not exist for a multi-bit value, so "Fire"
        ///    would never match <c>Fire | Ice</c>. The mask is resolved HERE, at export, so the buyer's file needs no reflection —
        ///    the same trade the rest of the emitter makes. Non-flags enums are left alone deliberately: their
        ///    name compare already agrees with live's numeric one.
        /// </summary>
        private static string CondExtraArgs(EmitContext ctx, VisualCondition cond, string full)
        {
            var args = new List<string>();

            if (cond.Policy != MixedValuePolicy.HideDependent)
                args.Add($"policy: {(int)cond.Policy}");

            if ((cond.Op == ConditionOperator.Equals || cond.Op == ConditionOperator.NotEquals)
                && TryResolveFlagsMask(ctx, cond, full, out long mask))
            {
                args.Add($"flagsMask: {mask}L");
                args.Add("isFlags: true");
            }

            return args.Count == 0 ? "" : ", " + string.Join(", ", args);
        }

        // Mirrors ConditionEvaluator's enum-type resolution, including the array / List<T> element unwrap a
        // condition inside a collection item needs.
        private static bool TryResolveFlagsMask(EmitContext ctx, VisualCondition cond, string full, out long mask)
        {
            mask = 0;
            var field = ctx.ResolveField(full);
            var t = field?.FieldType;
            if (t == null) return false;

            if (t.IsArray && t.GetElementType() != null && t.GetElementType().IsEnum) t = t.GetElementType();
            else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)
                     && t.GetGenericArguments()[0].IsEnum) t = t.GetGenericArguments()[0];
            if (!t.IsEnum || !t.IsDefined(typeof(FlagsAttribute), true)) return false;

            string value = cond.Value;
            if (string.IsNullOrEmpty(value)) return false;
            try
            {
                // Same call live makes: accepts a name, a comma-separated set, or a numeric string.
                if (!Enum.TryParse(t, value, true, out object parsed) || parsed == null) return false;
                mask = Convert.ToInt64(parsed);
                return true;
            }
            catch { return false; }   // an unrepresentable underlying type falls back to the name compare
        }

        private static bool IsBoolMember(EmitContext ctx, string name)
        {
            var pi = ctx.ResolveReadableProperty(name);
            if (pi != null && pi.PropertyType == typeof(bool)) return true;
            var mi = ctx.ResolveParameterlessMethod(name);
            return mi != null && mi.ReturnType == typeof(bool);
        }

        private static string Combine(string basePath, string path)
        {
            if (string.IsNullOrEmpty(basePath)) return path;
            if (string.IsNullOrEmpty(path)) return basePath;
            return basePath + "." + path;
        }

        /// <summary>
        /// The self-contained condition helpers to append to the generated class, gated by which the file needs.
        /// A faithful port of <c>ConditionEvaluator</c>'s comparison logic — no Sasquatch types referenced.
        /// </summary>
        public static string BakedHelpers(EmitContext ctx)
        {
            if (!ctx.NeedsEvalCond && !ctx.NeedsBoolMember) return "";

            var w = new CodeWriter();
            w.Line();
            w.Comment("--- Baked condition helpers (self-contained; no Sasquatch dependency) ---");

            if (ctx.NeedsEvalCond)
            {
                w.Line("private static bool EvalCond(SerializedProperty prop, int op, string val, int policy = 0, long flagsMask = 0, bool isFlags = false)");
                w.OpenBrace();
                w.Line("if (prop == null) return true;");
                w.Comment("A driver that DISAGREES across a multi-object selection is not a value to test.");
                w.Comment("0 = Hide Dependent (the default), 1 = Evaluate Prime Target, 2 = Show Dependent.");
                w.Line("if (prop.serializedObject != null && prop.serializedObject.isEditingMultipleObjects && prop.hasMultipleDifferentValues)");
                w.OpenBrace();
                w.Line("if (policy == 0) return false;");
                w.Line("if (policy == 2) return true;");
                w.CloseBrace();
                w.Line("switch (op)");
                w.OpenBrace();
                w.Line("case 4: return prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue == null;");
                w.Line("case 5: return prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue != null;");
                w.Line("case 6: return prop.propertyType == SerializedPropertyType.Boolean && prop.boolValue;");
                w.Line("case 7: return prop.propertyType == SerializedPropertyType.Boolean && !prop.boolValue;");
                w.Line("case 0: return EvalCondEquals(prop, val, flagsMask, isFlags);");
                w.Line("case 1: return !EvalCondEquals(prop, val, flagsMask, isFlags);");
                w.Line("case 2: return EvalCondNumeric(prop, val, out int c2) && c2 > 0;");
                w.Line("case 8: return EvalCondNumeric(prop, val, out int c8) && c8 >= 0;");
                w.Line("case 3: return EvalCondNumeric(prop, val, out int c3) && c3 < 0;");
                w.Line("case 9: return EvalCondNumeric(prop, val, out int c9) && c9 <= 0;");
                w.Line("default: return true;");
                w.CloseBrace();
                w.CloseBrace();
                w.Line();

                w.Line("private static bool EvalCondEquals(SerializedProperty prop, string val, long flagsMask = 0, bool isFlags = false)");
                w.OpenBrace();
                w.Line("switch (prop.propertyType)");
                w.OpenBrace();
                w.Line("case SerializedPropertyType.Boolean: return bool.TryParse(val, out var b) && prop.boolValue == b;");
                w.Line("case SerializedPropertyType.Integer: return long.TryParse(val, out var i) && prop.longValue == i;");
                w.Line("case SerializedPropertyType.Float:");
                w.Line("    return double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)");
                w.Line("        && Mathf.Approximately((float)prop.doubleValue, (float)f);");
                w.Line("case SerializedPropertyType.String:");
                w.Line("    return string.Equals(prop.stringValue, val, System.StringComparison.OrdinalIgnoreCase);");
                w.Line("case SerializedPropertyType.Enum:");
                w.Comment("    A [Flags] driver is a MASK, not a single pick: the target's bits must all be set.");
                w.Comment("    Zero means None, which only matches a completely clear mask.");
                w.Line("    if (isFlags) return flagsMask == 0 ? prop.enumValueFlag == 0 : (prop.enumValueFlag & flagsMask) == flagsMask;");
                w.Line("    if (int.TryParse(val, out var ev)) return prop.enumValueFlag == ev;");
                w.Line("    if (prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumNames.Length)");
                w.Line("        return string.Equals(prop.enumNames[prop.enumValueIndex], val, System.StringComparison.OrdinalIgnoreCase)");
                w.Line("            || string.Equals(prop.enumDisplayNames[prop.enumValueIndex], val, System.StringComparison.OrdinalIgnoreCase);");
                w.Line("    return false;");
                w.Line("case SerializedPropertyType.ObjectReference:");
                w.Line("    if (string.IsNullOrEmpty(val) || val == \"null\") return prop.objectReferenceValue == null;");
                w.Line("    return prop.objectReferenceValue != null && string.Equals(prop.objectReferenceValue.name, val, System.StringComparison.OrdinalIgnoreCase);");
                w.Line("default: return false;");
                w.CloseBrace();
                w.CloseBrace();
                w.Line();

                w.Line("private static bool EvalCondNumeric(SerializedProperty prop, string val, out int cmp)");
                w.OpenBrace();
                w.Line("cmp = 0;");
                w.Line("if (prop.propertyType == SerializedPropertyType.Integer && long.TryParse(val, out var i)) { cmp = prop.longValue.CompareTo(i); return true; }");
                w.Line("if (prop.propertyType == SerializedPropertyType.Float && double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) { cmp = prop.doubleValue.CompareTo(f); return true; }");
                w.Line("return false;");
                w.CloseBrace();
            }

            if (ctx.NeedsBoolMember)
            {
                if (ctx.NeedsEvalCond) w.Line();
                w.Line("private static bool EvalBoolMember(UnityEngine.Object target, string member, int op, string val)");
                w.OpenBrace();
                w.Line("bool value = false; bool found = false;");
                w.Line("if (target != null && !string.IsNullOrEmpty(member))");
                w.OpenBrace();
                w.Line("var type = target.GetType();");
                w.Line("const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;");
                w.Line("try { var pi = type.GetProperty(member, flags); if (pi != null && pi.PropertyType == typeof(bool) && pi.CanRead) { value = (bool)pi.GetValue(target); found = true; } } catch { }");
                w.Line("if (!found) { try { var mi = type.GetMethod(member, flags, null, System.Type.EmptyTypes, null); if (mi != null && mi.ReturnType == typeof(bool)) { value = (bool)mi.Invoke(target, null); found = true; } } catch { } }");
                w.CloseBrace();
                w.Line("if (!found) return true;");
                w.Line("switch (op)");
                w.OpenBrace();
                w.Line("case 7: return !value;");
                w.Line("case 1: return bool.TryParse(val, out var nb) ? value != nb : !value;");
                w.Line("default: return bool.TryParse(val, out var b) ? value == b : value;");
                w.CloseBrace();
                w.CloseBrace();
            }

            return w.ToString();
        }
    }
}
