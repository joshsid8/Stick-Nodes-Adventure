using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>What a met visual condition does to the node (Show / Hide / Enable / Disable / WarningHighlight).</summary>
    public enum ConditionOutcome
    {
        Show = 0,
        Hide = 1,
        Enable = 2,
        Disable = 3,
        WarningHighlight = 4
    }

    /// <summary>
    /// A single conditional-display rule: compare another property (by path) with an operator and value, then
    /// apply an <see cref="ConditionOutcome"/>. Drives show/hide/enable/highlight behavior on a node.
    /// </summary>
    [Serializable]
    public struct VisualCondition
    {
        [SerializeField] private string propertyPath;
        [SerializeField] private ConditionOperator op;
        [SerializeField] private string value;
        [SerializeField] private MixedValuePolicy policy;
        [SerializeField] private ConditionOutcome outcome;

        public string PropertyPath
        {
            get => propertyPath;
            set => propertyPath = value;
        }

        public ConditionOperator Op
        {
            get => op;
            set => op = value;
        }

        public string Value
        {
            get => value;
            set => this.value = value;
        }

        public MixedValuePolicy Policy
        {
            get => policy;
            set => policy = value;
        }

        public ConditionOutcome Outcome
        {
            get => outcome;
            set => outcome = value;
        }

        /// <summary>Creates a condition comparing <paramref name="propertyPath"/> via <paramref name="op"/>/<paramref name="value"/>, applying <paramref name="outcome"/> when it holds.</summary>
        public VisualCondition(string propertyPath, ConditionOperator op, string value = "", MixedValuePolicy policy = MixedValuePolicy.HideDependent, ConditionOutcome outcome = ConditionOutcome.Show)
        {
            this.propertyPath = propertyPath;
            this.op = op;
            this.value = value;
            this.policy = policy;
            this.outcome = outcome;
        }
    }
}

