namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// How the left label column of Sasquatch's styled field rows sizes itself as the inspector widens
    /// (Project Settings ▸ Sasquatch Inspector ▸ Label Column).
    /// </summary>
    public enum LabelColumnMode
    {
        /// <summary>Follow Unity's proportional column until it reaches Max Label Width, then stop —
        /// any further inspector width goes to the field itself instead of the label gutter.</summary>
        Capped = 0,

        /// <summary>Keep growing proportionally at any width, exactly like stock inspector rows.</summary>
        UnityStandard = 1
    }
}
