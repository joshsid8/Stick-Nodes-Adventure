using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Shared layout for the 4-way <see cref="ElementPlacement"/> used by widgets that arrange a secondary
    /// element (preview thumbnail, audio waveform, color bar, …) around their primary content. One placement
    /// behavior shared by AssetPreview, AudioPreview and the ColorPalette bar, so the same-row / stacked /
    /// margin logic isn't re-derived per widget.
    /// </summary>
    internal static class PlacementLayout
    {
        /// <summary>Left/Right share the row; Above/Below stack on separate rows.</summary>
        internal static bool IsSameRow(ElementPlacement placement) =>
            placement == ElementPlacement.Left || placement == ElementPlacement.Right;

        /// <summary>
        /// Arranges <paramref name="primary"/> (the field/surface, which grows) and <paramref name="secondary"/>
        /// (the preview/bar, fixed) inside <paramref name="container"/> per <paramref name="placement"/>.
        /// For Left/Right the secondary takes <paramref name="secondaryPercent"/> of the row width; for
        /// Above/Below it stacks full-width. Adds both children in the correct order — the caller only builds them.
        /// </summary>
        internal static void Arrange(VisualElement container, VisualElement primary, VisualElement secondary,
            ElementPlacement placement, float secondaryPercent = 34f, float sideGap = 6f, float stackGap = 4f)
        {
            if (IsSameRow(placement))
            {
                container.style.flexDirection = FlexDirection.Row;
                container.style.alignItems = Align.FlexStart;

                primary.style.flexGrow = 1;
                primary.style.flexShrink = 1;
                primary.style.minWidth = 0;

                secondary.style.flexShrink = 0;
                secondary.style.width = Length.Percent(secondaryPercent);

                if (placement == ElementPlacement.Left)
                {
                    secondary.style.marginRight = sideGap;
                    container.Add(secondary);
                    container.Add(primary);
                }
                else
                {
                    secondary.style.marginLeft = sideGap;
                    container.Add(primary);
                    container.Add(secondary);
                }
            }
            else
            {
                container.style.flexDirection = FlexDirection.Column;

                if (placement == ElementPlacement.Above)
                {
                    secondary.style.marginBottom = stackGap;
                    container.Add(secondary);
                    container.Add(primary);
                }
                else
                {
                    secondary.style.marginTop = stackGap;
                    container.Add(primary);
                    container.Add(secondary);
                }
            }
        }
    }
}
