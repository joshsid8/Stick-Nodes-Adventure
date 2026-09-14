using UnityEngine;

namespace SasquatchInspector.Showcase
{
    // hoverValue is never written by anything — it exists so the Hover trigger has a row to animate as the
    // cursor passes over it. Trigger() is empty because the ANIMATION is the visible result.
    // Kept off the fields themselves: a comment directly above a member is lifted verbatim into the
    // sandbox's "C# Source" pane (ShowcaseSource.ExtendBackward), which shows bare declarations.
    internal class ShowcaseAnimationsTarget : ScriptableObject
    {
        [Range(0f, 100f)] public float appearValue = 50f;

        public float valueChanged = 25f;

        [Range(0f, 100f)] public float hoverValue = 70f;

        public void Trigger() { }
    }
}
