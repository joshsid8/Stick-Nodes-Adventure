using UnityEngine;

namespace SasquatchInspector.Showcase
{
    // · charge and throttle are deliberately UNATTRIBUTED. The affix page mounts inside-the-box pills on
    //   them, and a [Range] float draws a stock slider whose read-out box is pinned at ~50px, where a pair
    //   of inside pills would eat most of the box. Re-pointing those rows at `health` reintroduces that.
    // · runeCount and heroName are the two fields questReward's {tokens} interpolate live.
    //
    // Kept off the fields themselves: a comment directly above a member is lifted verbatim into the
    // sandbox's "C# Source" pane (ShowcaseSource.ExtendBackward), which shows bare declarations.
    internal class ShowcaseCommonFieldsTarget : ScriptableObject
    {
        [Range(0f, 100f)] public float health = 75f;

        public bool invincible = true;

        [TextArea(2, 4)] public string notes = "Sasquatch fields render with UI Toolkit.";

        public int runeCount = 10;

        public string heroName = "Aria";

        [TextArea(4, 6)] public string questReward =
            "You found <color=#FF6600><b>{runeCount}x Fire Runes</b></color>!\nUse them <i>wisely</i>, <size=120%><color=#88C0D0>{heroName}</color></size>.";

        public string savePath = "";

        public Vector2 damageRange = new Vector2(15f, 45f);

        public Gradient gradient = MakeShowcaseGradient();

        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public int stars = 3;

        public Vector2 aim = new Vector2(0.35f, 0.6f);

        public Vector3 windForce = new Vector3(0.6f, 0.3f, -0.5f);

        public Vector3 spawnOffset = new Vector3(1.5f, 0f, -2f);

        public int waveSize = 3;

        public float price = 499f;

        public Vector3 velocity = new Vector3(5f, 2f, 9f);

        public float charge = 12.5f;

        public float throttle = 24.4f;

        [ColorUsage(true, true)] public Color glowTint = new Color(1.6f, 0.95f, 0.35f, 0.85f);

        public string PowerRating => $"{Mathf.RoundToInt(health * (invincible ? 2f : 1f))} PWR";

        // A pleasant blue → purple → orange default so the Gradient field reads well out of the box.
        private static Gradient MakeShowcaseGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.16f, 0.50f, 0.90f), 0f),
                    new GradientColorKey(new Color(0.55f, 0.30f, 0.85f), 0.5f),
                    new GradientColorKey(new Color(0.95f, 0.45f, 0.30f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                });
            return g;
        }
    }
}
