using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 7 — the preview widgets, which are the ones that read as magic in a screenshot: an audio clip
    /// drawn as a waveform you can scrub, and a particle prefab rendered live in the Inspector.
    ///
    /// Audio and ParticleSystem are REMOVABLE built-in modules, so both fields are gated. A project with
    /// either module stripped still compiles this component, and the designed layout's condition rules put a
    /// short explanation where the missing exhibit would have been.
    /// </summary>
    public class DemoAudioCue : MonoBehaviour
    {
        public enum CueTrigger { OnEnter, OnExit, OnDamage, OnPickup }

        public string cueName = "Cavern Ambience";
        public CueTrigger trigger = CueTrigger.OnEnter;

#if SASQ_AUDIO
        [Tooltip("Drawn as a scrubbable waveform in the designed layout, not a bare object slot.")]
        public AudioClip clip;

        [Range(0f, 1f)] public float volume = 0.8f;
        [Range(-3f, 3f)] public float pitch = 1f;
        public bool loop = true;
#endif

        [Range(0f, 60f)] public float fadeInSeconds = 1.5f;
        [Range(0f, 60f)] public float fadeOutSeconds = 2.5f;
        public Vector2 randomPitchRange = new Vector2(0.95f, 1.05f);

#if SASQ_PARTICLES
        [Range(0f, 4f)] public float effectScale = 1f;
        public Color effectTint = Color.white;
#endif

        [Tooltip("Shown as a read-only note when a module this exhibit needs has been removed.")]
        [TextArea(2, 4)]
        public string moduleNote =
            "Audio and Particle System are removable Unity modules. Remove either from Package Manager and " +
            "this exhibit degrades gracefully rather than failing to compile.";
    }
}
