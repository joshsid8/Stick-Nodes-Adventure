namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// How a <see cref="ContainerType.Grid"/> decides its track count — mirrors Unity's
    /// <c>GridLayoutGroup.Constraint</c>. You pin ONE axis; the other flows from the item count.
    /// <para>
    /// <see cref="Flexible"/> (the serialization default = 0, so existing grids need no migration) auto-fits as
    /// many equal columns as the inspector width allows. <see cref="FixedColumnCount"/> uses
    /// <see cref="ContainerNode.GridColumns"/> as the column count (rows flow). <see cref="FixedRowCount"/>
    /// uses it as the row count (columns flow). A simultaneous fixed R×C matrix is intentionally NOT a mode —
    /// it over-constrains a dynamic field list (item-count mismatches cause overflow / empty cells).
    /// </para>
    /// </summary>
    public enum GridConstraint
    {
        Flexible = 0,
        FixedColumnCount = 1,
        FixedRowCount = 2
    }
}
