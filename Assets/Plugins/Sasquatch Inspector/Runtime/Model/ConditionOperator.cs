namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Comparison operator for a <see cref="VisualCondition"/> (Equals, GreaterThan, IsNull, IsTrue, …).
    /// APPEND-ONLY: the values are serialized as ints on saved layouts, so new operators must be added at the end
    /// with explicit values — never reorder or insert, or existing conditions would silently change meaning.
    /// </summary>
    public enum ConditionOperator
    {
        Equals = 0,
        NotEquals = 1,
        GreaterThan = 2,
        LessThan = 3,
        IsNull = 4,
        IsNotNull = 5,
        IsTrue = 6,
        IsFalse = 7,
        GreaterThanOrEqual = 8,
        LessThanOrEqual = 9
    }
}
