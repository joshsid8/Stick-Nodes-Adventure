using System.Collections.Generic;
using System.Text;
using UnityEditor;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>The net display state a node's conditions produce, for the condition debugger.</summary>
    public enum ConditionVerdict { Visible, Hidden, Disabled }

    /// <summary>One evaluated condition term: the rule, whether it passed, and the driver's current value rendered for display.</summary>
    public readonly struct ConditionTermResult
    {
        public readonly VisualCondition Condition;
        public readonly string ResolvedPath;
        public readonly bool Passed;
        public readonly string ActualValueText;

        public ConditionTermResult(VisualCondition condition, string resolvedPath, bool passed, string actualValueText)
        {
            Condition = condition;
            ResolvedPath = resolvedPath;
            Passed = passed;
            ActualValueText = actualValueText;
        }

        /// <summary>The condition's own (unscoped) property/member path — the readable driver name, e.g. "isActive".</summary>
        public string LeafPath => Condition.PropertyPath;

        /// <summary>Driver annotated with its live value, e.g. "isActive [TRUE]".</summary>
        public string ShortForm => $"{LeafPath} [{ActualValueText}]";

        /// <summary>The rule in human form, e.g. "isActive is TRUE", "mode == Ranged", "ammoCount &gt; 5".</summary>
        public string RuleText => $"{LeafPath} {ConditionEvaluationReport.DescribeOperator(Condition)}";
    }

    /// <summary>One evaluated top-level item under a rule: a bare condition (single term) or a one-level group.</summary>
    public sealed class EvaluatedItem
    {
        public bool IsGroup;
        public ConditionMatch Match;
        public bool Met;
        public bool Counted;
        public readonly List<ConditionTermResult> Terms = new List<ConditionTermResult>();
    }

    /// <summary>One evaluated rule: its outcome, all/any mode, whether its expression was met, and its items.</summary>
    public sealed class EvaluatedRule
    {
        public ConditionOutcome Outcome;
        public ConditionMatch Match;
        public bool Met;
        public bool Counted;
        public readonly List<EvaluatedItem> Items = new List<EvaluatedItem>();
    }

    /// <summary>
    /// Captures the full per-term evaluation of a node's condition rules plus the net verdict, so the condition
    /// debugger can reveal a hidden/disabled field and explain exactly which rule/term drove it. Built from the
    /// same <see cref="ConditionEvaluator"/> + <see cref="ConditionBinder.FoldMatch"/> + <see cref="ConditionBinder.ApplyOutcome"/>
    /// logic the live binder uses, so the displayed verdict can never disagree with the real show/hide.
    /// </summary>
    public sealed class ConditionEvaluationReport
    {
        public bool Visible = true;
        public bool Enabled = true;
        public bool Highlight = false;
        public readonly List<EvaluatedRule> Rules = new List<EvaluatedRule>();

        /// <summary>Every leaf term across all rules/groups, flattened in render order — a convenience for tests and simple readouts.</summary>
        public List<ConditionTermResult> Terms
        {
            get
            {
                var all = new List<ConditionTermResult>();
                foreach (var r in Rules)
                    foreach (var i in r.Items)
                        foreach (var t in i.Terms)
                            all.Add(t);
                return all;
            }
        }

        /// <summary>One-line summary, e.g. "isActive [TRUE] &amp;&amp; hasAmmo [FALSE] → HIDDEN".</summary>
        public string Headline { get; private set; } = "";

        /// <summary>Multi-line grouped breakdown — the native-tooltip fallback when no build root is found for the card.</summary>
        public string Detail { get; private set; } = "";

        public ConditionVerdict Verdict => !Visible ? ConditionVerdict.Hidden
                                         : !Enabled ? ConditionVerdict.Disabled
                                         : ConditionVerdict.Visible;

        /// <summary>True when the node would be hidden or disabled — i.e. the debugger should draw an overlay.</summary>
        public bool HasOverlay => !Visible || !Enabled;

        public static ConditionEvaluationReport Build(List<ConditionRule> rules, SerializedObject serializedObject, string basePath)
        {
            var report = new ConditionEvaluationReport();
            if (rules != null)
            {
                foreach (var rule in rules)
                {
                    var er = new EvaluatedRule { Outcome = rule.Outcome, Match = rule.Match };
                    var itemMets = new List<bool>();

                    if (rule.Items != null)
                    {
                        foreach (var item in rule.Items)
                        {
                            var ei = new EvaluatedItem { IsGroup = item.IsGroup, Match = item.Match };
                            var leafPasses = new List<bool>();

                            if (item.Conditions != null)
                            {
                                foreach (var c in item.Conditions)
                                {
                                    if (string.IsNullOrEmpty(c.PropertyPath)) continue;
                                    string resolved = ConditionBinder.ResolvePath(basePath, c.PropertyPath);
                                    bool passed = ConditionEvaluator.Evaluate(c, serializedObject, resolved);
                                    string actual = ConditionEvaluator.DescribeCurrentValue(serializedObject, resolved);
                                    ei.Terms.Add(new ConditionTermResult(c, resolved, passed, actual));
                                    leafPasses.Add(passed);
                                }
                            }

                            ei.Met = ConditionBinder.FoldMatch(item.Match, leafPasses, out bool itemCounted);
                            ei.Counted = itemCounted;
                            er.Items.Add(ei);
                            if (itemCounted) itemMets.Add(ei.Met);
                        }
                    }

                    er.Met = ConditionBinder.FoldMatch(rule.Match, itemMets, out bool ruleCounted);
                    er.Counted = ruleCounted;
                    report.Rules.Add(er);

                    if (ruleCounted)
                        ConditionBinder.ApplyOutcome(rule.Outcome, er.Met, ref report.Visible, ref report.Enabled, ref report.Highlight);
                }
            }
            report.BuildText();
            return report;
        }

        private void BuildText()
        {
            string verdictText = Verdict == ConditionVerdict.Hidden ? "HIDDEN"
                               : Verdict == ConditionVerdict.Disabled ? "DISABLED"
                               : "VISIBLE";

            Headline = BuildHeadline(verdictText);

            var sb = new StringBuilder();
            sb.Append(verdictText);
            foreach (var rule in Rules)
            {
                if (!rule.Counted) continue;
                sb.Append('\n').Append(OutcomeLabel(rule.Outcome)).Append(" when ").Append(MatchWord(rule.Match))
                  .Append(" of:").Append(rule.Met ? "  [met]" : "  [not met]");
                foreach (var item in rule.Items)
                {
                    if (!item.Counted) continue;
                    if (item.IsGroup)
                    {
                        sb.Append("\n  (").Append(MatchWord(item.Match)).Append(" of)");
                        foreach (var t in item.Terms)
                            sb.Append("\n    ").Append(t.Passed ? "[ok] " : "[x] ").Append(t.RuleText).Append("  (now ").Append(t.ActualValueText).Append(')');
                    }
                    else
                    {
                        foreach (var t in item.Terms)
                            sb.Append("\n  ").Append(t.Passed ? "[ok] " : "[x] ").Append(t.RuleText).Append("  (now ").Append(t.ActualValueText).Append(')');
                    }
                }
            }
            Detail = sb.ToString();
        }

        private string BuildHeadline(string verdictText)
        {
            var parts = new List<string>();
            foreach (var rule in Rules)
            {
                if (!rule.Counted) continue;
                bool drives =
                    (Verdict == ConditionVerdict.Hidden && ((rule.Outcome == ConditionOutcome.Show && !rule.Met) || (rule.Outcome == ConditionOutcome.Hide && rule.Met))) ||
                    (Verdict == ConditionVerdict.Disabled && ((rule.Outcome == ConditionOutcome.Enable && !rule.Met) || (rule.Outcome == ConditionOutcome.Disable && rule.Met)));
                if (!drives) continue;
                parts.Add(DescribeRuleShort(rule));
            }

            string lhs = parts.Count > 0 ? string.Join("  |  ", parts) : "(condition)";
            return $"{lhs} → {verdictText}";
        }

        private static string DescribeRuleShort(EvaluatedRule rule)
        {
            var termTexts = new List<string>();
            foreach (var item in rule.Items)
            {
                if (!item.Counted) continue;
                foreach (var t in item.Terms)
                    termTexts.Add(t.ShortForm);
            }
            string joiner = rule.Match == ConditionMatch.All ? " && " : " || ";
            return string.Join(joiner, termTexts);
        }

        /// <summary>Human label for an outcome, e.g. "Show", "Hide", "Highlight".</summary>
        public static string OutcomeLabel(ConditionOutcome outcome)
        {
            switch (outcome)
            {
                case ConditionOutcome.Show: return "Show";
                case ConditionOutcome.Hide: return "Hide";
                case ConditionOutcome.Enable: return "Enable";
                case ConditionOutcome.Disable: return "Disable";
                case ConditionOutcome.WarningHighlight: return "Highlight";
                default: return outcome.ToString();
            }
        }

        /// <summary>"all" / "any" for a match mode.</summary>
        public static string MatchWord(ConditionMatch match) => match == ConditionMatch.All ? "all" : "any";

        /// <summary>Renders a condition's operator + value in human form, e.g. "is TRUE", "== Ranged", "≥ 5".</summary>
        public static string DescribeOperator(VisualCondition c)
        {
            switch (c.Op)
            {
                case ConditionOperator.IsTrue: return "is TRUE";
                case ConditionOperator.IsFalse: return "is FALSE";
                case ConditionOperator.IsNull: return "is null";
                case ConditionOperator.IsNotNull: return "is not null";
                case ConditionOperator.Equals: return $"== {c.Value}";
                case ConditionOperator.NotEquals: return $"!= {c.Value}";
                case ConditionOperator.GreaterThan: return $"> {c.Value}";
                case ConditionOperator.GreaterThanOrEqual: return $"≥ {c.Value}";
                case ConditionOperator.LessThan: return $"< {c.Value}";
                case ConditionOperator.LessThanOrEqual: return $"≤ {c.Value}";
                default: return c.Op.ToString();
            }
        }
    }
}
