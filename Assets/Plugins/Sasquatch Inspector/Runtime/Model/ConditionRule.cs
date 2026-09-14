using System;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>How the members of a rule / group are combined: every one must match (<see cref="All"/> = AND) or at least one (<see cref="Any"/> = OR).</summary>
    public enum ConditionMatch
    {
        All = 0,
        Any = 1
    }

    /// <summary>
    /// One item under a <see cref="ConditionRule"/>. Either a single bare condition (<see cref="IsGroup"/> false,
    /// exactly one entry in <see cref="Conditions"/>) or an explicit group of conditions combined by
    /// <see cref="Match"/> (<see cref="IsGroup"/> true). Groups never nest — this is the deliberate one-level cap
    /// that keeps the authored logic readable (a bare item OR a group; no groups-inside-groups).
    /// </summary>
    [Serializable]
    public class ConditionGroup
    {
        [SerializeField] private bool isGroup;
        [SerializeField] private ConditionMatch match = ConditionMatch.All;
        [SerializeField] private List<VisualCondition> conditions = new();

        /// <summary>True when this item is an explicit group (bordered box with its own all/any); false = a single bare condition row.</summary>
        public bool IsGroup
        {
            get => isGroup;
            set => isGroup = value;
        }

        /// <summary>How the group's <see cref="Conditions"/> combine (only meaningful when <see cref="IsGroup"/> is true).</summary>
        public ConditionMatch Match
        {
            get => match;
            set => match = value;
        }

        public List<VisualCondition> Conditions
        {
            get => conditions;
            set => conditions = value;
        }

        public ConditionGroup() { }

        /// <summary>Creates a bare single-condition item.</summary>
        public static ConditionGroup Leaf(VisualCondition condition) =>
            new ConditionGroup { isGroup = false, match = ConditionMatch.All, conditions = new List<VisualCondition> { condition } };

        /// <summary>Creates an explicit group seeded with one empty condition.</summary>
        public static ConditionGroup Group(ConditionMatch match = ConditionMatch.All) =>
            new ConditionGroup { isGroup = true, match = match, conditions = new List<VisualCondition> { new VisualCondition("", ConditionOperator.Equals, "") } };
    }

    /// <summary>
    /// A single conditional-display rule on a node: apply one <see cref="ConditionOutcome"/>
    /// (Show / Hide / Enable / Disable / WarningHighlight) when its expression evaluates true. The expression is
    /// a flat list of <see cref="ConditionGroup"/> items combined by <see cref="Match"/> (all = AND, any = OR),
    /// where each item is a bare condition or a one-level group. Reads as a sentence:
    /// "<b>Outcome</b> this field when <b>all/any</b> of: …". A node may carry several rules (e.g. hide on X,
    /// disable on Y), evaluated independently.
    /// </summary>
    [Serializable]
    public class ConditionRule
    {
        [SerializeField] private ConditionOutcome outcome = ConditionOutcome.Show;
        [SerializeField] private ConditionMatch match = ConditionMatch.All;
        [SerializeField] private List<ConditionGroup> items = new();

        public ConditionOutcome Outcome
        {
            get => outcome;
            set => outcome = value;
        }

        /// <summary>How the top-level <see cref="Items"/> combine (all = AND, any = OR).</summary>
        public ConditionMatch Match
        {
            get => match;
            set => match = value;
        }

        /// <summary>The rule's top-level items — each a bare condition or a one-level group.</summary>
        public List<ConditionGroup> Items
        {
            get => items;
            set => items = value;
        }

        public ConditionRule() { }

        public ConditionRule(ConditionOutcome outcome, ConditionMatch match)
        {
            this.outcome = outcome;
            this.match = match;
            this.items = new List<ConditionGroup>();
        }
    }

    /// <summary>
    /// Pure model helpers for the rule-based condition system, including the one-way migration from the legacy
    /// flat <see cref="LayoutNode.Conditions"/> list (each carrying its own outcome) to <see cref="LayoutNode.ConditionRules"/>.
    /// </summary>
    public static class ConditionModel
    {
        /// <summary>
        /// The all/any combine mode that reproduces the legacy per-condition fold for a given outcome:
        /// Show/Enable were "every term must pass" (ALL); Hide/Disable/Highlight were "any term triggers" (ANY).
        /// </summary>
        public static ConditionMatch DefaultMatchFor(ConditionOutcome outcome)
        {
            switch (outcome)
            {
                case ConditionOutcome.Show:
                case ConditionOutcome.Enable:
                    return ConditionMatch.All;
                default:
                    return ConditionMatch.Any;
            }
        }

        /// <summary>
        /// Ensures <paramref name="node"/> exposes its conditions as <see cref="ConditionRule"/>s. Idempotent and
        /// side-effect-free on already-migrated nodes: if <see cref="LayoutNode.ConditionRules"/> is already
        /// populated (or there's nothing legacy to convert) it does nothing. Otherwise it converts the legacy flat
        /// <see cref="LayoutNode.Conditions"/> list into rules — grouped by outcome in first-seen order, each old
        /// condition becoming a bare item, and each rule's match mode set by <see cref="DefaultMatchFor"/> so the
        /// migrated behavior is identical to the old fold. The legacy list is left intact (harmless dead data on old
        /// assets; ignored once rules exist), so this is safe to call on shared/cached live-inspector trees.
        /// </summary>
        public static void EnsureMigrated(LayoutNode node)
        {
            if (node == null) return;
            if (node.ConditionRules == null) node.ConditionRules = new List<ConditionRule>();

            var legacy = node.Conditions;
            if (node.ConditionRules.Count > 0 || legacy == null || legacy.Count == 0) return;

            var order = new List<ConditionOutcome>();
            var byOutcome = new Dictionary<ConditionOutcome, List<VisualCondition>>();
            foreach (var c in legacy)
            {
                if (!byOutcome.TryGetValue(c.Outcome, out var list))
                {
                    list = new List<VisualCondition>();
                    byOutcome[c.Outcome] = list;
                    order.Add(c.Outcome);
                }
                list.Add(c);
            }

            foreach (var outcome in order)
            {
                var rule = new ConditionRule(outcome, DefaultMatchFor(outcome));
                foreach (var c in byOutcome[outcome])
                    rule.Items.Add(ConditionGroup.Leaf(c));
                node.ConditionRules.Add(rule);
            }
        }

        /// <summary>Recursively runs <see cref="EnsureMigrated"/> over <paramref name="node"/> and its descendants (container children / collection item layout).</summary>
        public static void EnsureMigratedTree(LayoutNode node)
        {
            if (node == null) return;
            EnsureMigrated(node);

            if (node is ContainerNode container && container.Children != null)
            {
                foreach (var child in container.Children)
                    EnsureMigratedTree(child);
            }
            else if (node is CollectionNode collection)
            {
                EnsureMigratedTree(collection.ItemLayout);
            }
        }

        /// <summary>
        /// Migrates then DROPS the legacy <see cref="LayoutNode.Conditions"/> list across <paramref name="node"/>
        /// and its descendants, so a saved layout carries only <see cref="LayoutNode.ConditionRules"/> (no dead
        /// duplicate data). Called at the persist chokepoint. Idempotent — <see cref="EnsureMigrated"/> guarantees
        /// the rules exist before the source is cleared, and re-running on an already-clean tree is a no-op.
        /// </summary>
        /// <summary>
        /// <see cref="StripLegacyConditionsTree"/> for ONE node — migrate, then drop the legacy list, leaving
        /// <see cref="LayoutNode.ConditionRules"/> as the sole source of truth. Lossless, because the migration
        /// runs first, and idempotent.
        /// <para>Needed on its own because a node holding BOTH an empty rule list and a live legacy list is a
        /// live grenade: <see cref="EnsureMigrated"/> is a no-op once rules exist but RE-MIGRATES an empty one,
        /// so anything that legitimately empties the rules — a multi-edit fan-out of a deletion, say — is
        /// silently undone the next time this node is migrated. Strip before that can happen, not after.</para>
        /// </summary>
        public static void StripLegacyConditions(LayoutNode node)
        {
            if (node == null) return;
            EnsureMigrated(node);           // guarantee rules exist before dropping their source
            node.Conditions?.Clear();       // ConditionRules is now the sole source of truth
        }

        public static void StripLegacyConditionsTree(LayoutNode node)
        {
            if (node == null) return;
            StripLegacyConditions(node);

            if (node is ContainerNode container && container.Children != null)
            {
                foreach (var child in container.Children)
                    StripLegacyConditionsTree(child);
            }
            else if (node is CollectionNode collection)
            {
                StripLegacyConditionsTree(collection.ItemLayout);
            }
        }
    }
}
