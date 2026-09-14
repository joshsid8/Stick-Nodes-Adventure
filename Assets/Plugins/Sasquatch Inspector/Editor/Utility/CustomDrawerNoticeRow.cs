using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The "this field already has a drawer" notice, shown under the Display Type picker in BOTH authoring
    /// surfaces (the Layout Designer's Properties panel and the inspector's right-click quick-edit popup).
    ///
    /// It exists because of one asymmetry that is otherwise completely invisible: a field left on
    /// <see cref="DisplayType.Default"/> renders through Unity's <c>PropertyField</c>, so a developer's own
    /// <c>[SomeAttribute]</c> + <c>PropertyDrawer</c> runs normally — but the moment ANY explicit Sasquatch
    /// widget is chosen we build the control ourselves off the SerializedProperty, and that drawer never runs
    /// again. Nothing said so. This says so, on the row where the choice is made.
    ///
    /// Deliberately silent in TWO cases, both for the same reason — a notice that cries wolf trains people to
    /// ignore the one that matters:
    /// <list type="number">
    /// <item>Drawers shipping inside Unity's own assemblies
    /// (<see cref="SasquatchReflectionUtility.TryGetCustomPropertyDrawer"/> filters them): superseding Unity's
    /// stock rendering is the whole product.</item>
    /// <item>Sasquatch's own types that its widgets bind natively — see <see cref="IsNativelyBound"/>. There
    /// the premise itself is false: nothing is replaced and nothing is lost.</item>
    /// </list>
    /// </summary>
    public sealed class CustomDrawerNoticeRow : VisualElement
    {
        private readonly Label _label;
        private readonly string _drawerName;

        private CustomDrawerNoticeRow(string drawerName)
        {
            _drawerName = drawerName;

            style.marginTop = 2;
            style.marginBottom = 4;
            style.paddingTop = 4;
            style.paddingBottom = 4;
            style.paddingLeft = 6;
            style.paddingRight = 6;
            style.borderTopLeftRadius = 3;
            style.borderTopRightRadius = 3;
            style.borderBottomLeftRadius = 3;
            style.borderBottomRightRadius = 3;
            style.borderLeftWidth = 2;

            _label = new Label { style = { whiteSpace = WhiteSpace.Normal, fontSize = 11 } };
            Add(_label);
        }

        /// <summary>
        /// The notice for <paramref name="field"/>, or null when nothing else draws it — which is the common
        /// case, so callers can add the result unconditionally with a null check and no branching noise.
        /// </summary>
        public static CustomDrawerNoticeRow TryCreate(FieldInfo field, DisplayType current)
        {
            if (!SasquatchReflectionUtility.TryGetCustomPropertyDrawer(field, out var info)) return null;
            if (IsNativelyBound(info)) return null;
            var row = new CustomDrawerNoticeRow(info.DisplayName);
            row.Sync(current);
            return row;
        }

        /// <summary>
        /// A type whose drawer exists ONLY as the plain-inspector fallback, because Sasquatch's own widgets
        /// understand the type directly. The notice's premise — "an explicit widget replaces the drawer, so
        /// you lose what it gave you" — is false for these, and the warning wording actively misleads.
        ///
        /// <para><see cref="SasquatchString"/> is the case that forced this. Choosing <c>Text</c> does not
        /// replace anything: <c>BindingFactory.CreateTextField</c> binds the struct's inner template and gives
        /// it the full rich-text toolbar and preview, which is the BETTER of the two choices. And the
        /// <c>{token}</c> resolution that makes the type worth having lives in its implicit <c>string</c>
        /// conversion — the runtime never sees a drawer at all — so "SasquatchString is ignored here" reads as
        /// "your tokens stop resolving", which is exactly wrong.</para>
        /// </summary>
        private static bool IsNativelyBound(SasquatchReflectionUtility.CustomDrawerInfo info)
            => !info.isAttributeTargeted && SasquatchReflectionUtility.IsNativelyBoundType(info.source);

        /// <summary>
        /// Repaints the notice for <paramref name="current"/>. Call from the Display Type change callback on
        /// surfaces that do NOT rebuild themselves — the whole point is that the message flips the instant the
        /// choice is made, not the next time the panel happens to be rebuilt.
        /// </summary>
        public void Sync(DisplayType current)
        {
            bool preserved = current == DisplayType.Default;
            if (preserved)
            {
                _label.text = $"{_drawerName} draws this field. \"Default\" keeps it.";
                _label.style.color = WidgetPalette.LabelMuted;
                style.backgroundColor = Color.clear;
                style.borderLeftColor = WidgetPalette.LabelDim;
            }
            else
            {
                // Named, not vague: "bypassed" alone leaves the user guessing what to do about it.
                _label.text =
                    $"{_drawerName} draws this field, but \"{DisplayTypeCatalog.Format(current)}\" replaces it — " +
                    $"so {_drawerName} is ignored here. Choose \"Default\" to let it draw.";
                _label.style.color = WidgetPalette.WarningCalloutText;
                style.backgroundColor = WidgetPalette.WarningCalloutBg;
                style.borderLeftColor = WidgetPalette.WarningCalloutBorder;
            }
        }
    }
}
