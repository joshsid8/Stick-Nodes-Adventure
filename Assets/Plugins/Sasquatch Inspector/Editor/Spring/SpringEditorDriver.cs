using SasquatchSpring;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Ticks the shared SpringEngine from the editor update loop so spring animations
    /// run in editor windows and inspectors without entering play mode.
    /// </summary>
    [InitializeOnLoad]
    internal static class SpringEditorDriver
    {
        // Clamp stalls (asset imports, modal dialogs, debugger breaks) to one sane step.
        private const float MaxDeltaTime = 0.1f;

        private static double _lastTime;

        static SpringEditorDriver()
        {
            _lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;

            // The engine's NativeArrays would otherwise leak across domain reloads.
            AssemblyReloadEvents.beforeAssemblyReload += SpringEngine.ShutdownInstance;
            EditorApplication.quitting += SpringEngine.ShutdownInstance;
        }

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min((float)(now - _lastTime), MaxDeltaTime);
            _lastTime = now;

            // The runtime driver owns the tick during play mode. While play mode is PAUSED neither driver
            // ticks — and that stays true on purpose: the engine is shared with the user's own runtime
            // springs, so ticking here would carry scene objects through the pause, which is the one thing
            // a paused editor must not do. Editor chrome does not lose its interactions to that freeze,
            // because EditorSpringFX.SpringClockFrozen makes MotionEnabled read false for exactly this
            // window and every chrome call site falls back to its instant, reduced-motion branch.
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // Created lazily by the first spring; nothing to tick before that.
            if (!SpringEngine.HasInstance) return;
            if (dt <= 0f) return;

            // No game time-scale at edit time, so scaled and unscaled steps are identical here.
            SpringEngine.Instance.UpdateEngine(dt, dt);
        }
    }
}
