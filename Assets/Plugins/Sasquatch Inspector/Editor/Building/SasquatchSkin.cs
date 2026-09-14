using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Applies the per-skin widget token stylesheet + root class. The build root gets this from
    /// LayoutTreeBuilder; surfaces that live OUTSIDE the build root (the unbound-field sync banner,
    /// the gradient color popover, the curve/gradient/palette editor windows) must apply it to
    /// their own roots or the --sasq-* variables never reach them (USS variables only inherit down
    /// the element tree).
    /// </summary>
    internal static class SasquatchSkin
    {
        internal const string DarkClass = "sasq-skin-dark";
        internal const string LightClass = "sasq-skin-light";

        /// <summary>Adds the skin class + loads the matching token sheet onto <paramref name="root"/>.</summary>
        internal static void Apply(VisualElement root, bool isLight)
        {
            if (root == null) return;
            root.RemoveFromClassList(isLight ? DarkClass : LightClass);
            root.AddToClassList(isLight ? LightClass : DarkClass);
            var sheet = SasquatchPaths.LoadStyleSheet(isLight ? "SasquatchWidgetThemeLight" : "SasquatchWidgetThemeDark");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
        }

        /// <summary>
        /// For standalone hosts (aux editor windows, popovers): loads the shared widget sheet AND
        /// the skin token sheet for the current ambient skin.
        /// </summary>
        internal static void ApplyWithWidgetSheet(VisualElement root, bool? isLight = null)
        {
            if (root == null) return;
            var widgets = SasquatchPaths.LoadStyleSheet("SasquatchWidgets");
            if (widgets != null && !root.styleSheets.Contains(widgets)) root.styleSheets.Add(widgets);
            Apply(root, isLight ?? WidgetPalette.IsLight);
        }
    }
}
