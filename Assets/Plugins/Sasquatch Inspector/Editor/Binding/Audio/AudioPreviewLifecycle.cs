#if SASQ_AUDIO // Tears down state that only exists when the audio widget compiles (incl. NativeAudioBackend.Shutdown).
using UnityEditor;

namespace SasquatchInspector.Editor.Audio
{
    /// <summary>
    /// Static teardown for the native audio preview — the analogue of the widgets' DetachFromPanel cleanup, for
    /// the process-wide native device. Stops playback and disposes the backend (closing the device, freeing
    /// unmanaged buffers / GCHandles, dropping native callbacks) before a domain reload, on editor quit, and
    /// when entering play mode. Guarantees no stuck audio and no leaked unmanaged handle/thread/buffer.
    /// </summary>
    [InitializeOnLoad]
    internal static class AudioPreviewLifecycle
    {
        static AudioPreviewLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Teardown;
            EditorApplication.quitting += Teardown;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) Teardown();
        }

        private static void Teardown()
        {
            try { AudioPreviewPlayback.Stop(); } catch { /* best effort */ }
            NativeAudioBackend.Shutdown();
            AudioClipPcm.Clear();
        }
    }
}
#endif // SASQ_AUDIO
