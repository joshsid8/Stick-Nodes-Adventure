using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 6 — an inspector that reacts. Half these fields are irrelevant most of the time, which is the
    /// normal state of a real settings component; the designed layout uses condition rules so each group only
    /// appears when it applies. Also the home of the fiddlier value widgets: an XY pad, a min/max range, a
    /// gradient and Euler angles.
    /// </summary>
    public class DemoLevelSettings : MonoBehaviour
    {
        public enum TimeOfDay { Dawn, Noon, Dusk, Night }
        public enum WeatherKind { Clear, Rain, Snow, Storm }
        public enum Difficulty { Story, Normal, Hard, Nightmare }

        // ---- Identity ------------------------------------------------------------------------------------
        public string levelName = "Hollowreach";
        public Difficulty difficulty = Difficulty.Normal;

        // ---- Atmosphere ----------------------------------------------------------------------------------
        public TimeOfDay timeOfDay = TimeOfDay.Dusk;
        public Gradient skyTint = new Gradient();
        public Vector3 sunRotation = new Vector3(38f, 145f, 0f);
        [Range(0f, 8f)] public float sunIntensity = 1.4f;

        // ---- Weather (conditional) -----------------------------------------------------------------------
        [Tooltip("Drives the conditional weather group in the designed layout.")]
        public bool weatherEnabled = true;

        public WeatherKind weather = WeatherKind.Rain;
        [Range(0f, 1f)] public float precipitation = 0.55f;
        [Range(0f, 40f)] public float windSpeed = 7f;

        [Tooltip("Wind direction as an XY pad — two floats that are far easier to aim than to type.")]
        public Vector2 windDirection = new Vector2(0.6f, -0.35f);

        // ---- Fog (conditional on weather) ----------------------------------------------------------------
        public bool fogEnabled = true;
        public Color fogColour = new Color(0.55f, 0.6f, 0.68f);
        [Tooltip("Near/far fog distance as a single min-max range.")]
        public Vector2 fogDistance = new Vector2(12f, 90f);
    }
}
