namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// The visual grouping a <see cref="ContainerNode"/> produces (Rows, Columns, Grid, Foldout, Tabs, Card, Frame).
    /// Names describe the visual result — the opposite of CSS flex-direction (see the note below).
    /// </summary>
    public enum ContainerType
    {
        // NOTE: the values are intentionally fixed — they are serialized by int, so renumbering one would
        // silently re-read an existing layout as a different container.
        // The names describe the *visual result* (a designer's mental model), which is the OPPOSITE of
        // CSS flex-direction: a "Rows" container stacks children into rows top-to-bottom
        // (flex-direction: column), and a "Columns" container places them side-by-side in columns
        // (flex-direction: row).
        Rows = 0,
        Columns = 1,
        Grid = 2,
        // Box = 3 was retired (visually near-identical to Card). The value is intentionally left as a
        // hole so Foldout/Tabs/Card keep their serialized ints; ContainerNode.OnAfterDeserialize clamps
        // any legacy int-3 layout to Card.
        Foldout = 4,
        Tabs = 5,
        Card = 6,
        // A shaped clipping container (see FrameSettings): rounded rect / circle / pill / diagonal split.
        // Appended as 7 (3 stays a retired hole) so existing layouts keep their serialized ints.
        Frame = 7
    }
}
