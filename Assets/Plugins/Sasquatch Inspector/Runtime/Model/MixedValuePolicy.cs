namespace SasquatchInspector.Runtime
{
    /// <summary>How a <see cref="VisualCondition"/> resolves under multi-object editing when the dependency differs across the selected targets.</summary>
    public enum MixedValuePolicy
    {
        HideDependent,
        EvaluatePrimeTarget,
        ShowDependent
    }
}
