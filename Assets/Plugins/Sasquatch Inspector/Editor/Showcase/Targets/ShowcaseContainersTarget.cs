using UnityEngine;

namespace SasquatchInspector.Showcase
{
    // enableModule drives the toggle-group Foldout's header checkbox. portrait is a Frame's BOUND image
    // fill — swap the texture and the frame repaints; the Showcase window seeds it with the shipped Brick
    // Wall texture.
    //
    // Kept off the fields themselves: a comment directly above a member is lifted verbatim into the
    // sandbox's "C# Source" pane (ShowcaseSource.ExtendBackward), which shows bare declarations.
    internal class ShowcaseContainersTarget : ScriptableObject
    {
        public string title = "Goblin Warren";
        public int capacity = 12;
        public float spawnRate = 1.5f;
        public bool active = true;

        public Color themeColor = new Color(0.40f, 0.70f, 1f);
        public Vector3 origin = Vector3.zero;

        public float width = 30f;
        public float height = 20f;

        public string description = "Containers group related fields together.";
        public int dangerLevel = 2;
        public bool bossRoom;

        public bool enableModule = true;

        public Texture2D portrait;
    }
}
