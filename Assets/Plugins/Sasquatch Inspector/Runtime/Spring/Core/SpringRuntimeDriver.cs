using UnityEngine;

namespace SasquatchSpring
{
    /// <summary>
    /// Hidden scene object that ticks the SpringEngine during play mode and in builds.
    /// At edit time the engine is ticked by Sasquatch Inspector's SpringEditorDriver instead.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class SpringRuntimeDriver : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            // Clear any driver left over from an earlier play session BEFORE creating this one.
            //
            // In the editor, HideAndDontSave is exactly what stops Unity from destroying this object on
            // play-mode exit — so without this sweep every Enter Play adds another driver, each with its own
            // Update() calling UpdateEngine(). Ten playtests means the engine advances ten times per frame and
            // every spring in the project animates ten times too fast (the editor driver stands down during
            // play mode, so these are the only tickers). It also leaks one GameObject per session.
            //
            // A static bool cannot guard this: the domain reload that precedes play mode resets managed
            // statics while the native object survives, so the flag always reads "not booted".
            //
            // In a player this finds nothing — Boot() runs once per process — so it costs one scan at startup.
            foreach (var stale in Resources.FindObjectsOfTypeAll<SpringRuntimeDriver>())
            {
                if (stale != null && stale.gameObject != null) DestroyImmediate(stale.gameObject);
            }

            var go = new GameObject("[SasquatchSpring]")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(go);
            go.AddComponent<SpringRuntimeDriver>();
        }

        private void Update()
        {
            // Created lazily by the first spring; don't resurrect a disposed engine.
            if (!SpringEngine.HasInstance) return;
            // Pass both clocks so unscaled springs keep animating through a Time.timeScale == 0 pause.
            SpringEngine.Instance.UpdateEngine(Time.deltaTime, Time.unscaledDeltaTime);
        }

#if SASQ_PHYSICS || SASQ_PHYSICS2D
        private void FixedUpdate()
        {
            // The springs themselves advance on the render clock (above) — that is what keeps the motion
            // smooth above the fixed rate — but Rigidbody.MovePosition/MoveRotation are FixedUpdate calls:
            // they set the body's target for the NEXT physics step. Writing them from Update lands zero
            // calls in some steps and several in others, which reads as jitter and wrong collision response.
            // The tick queues them; this delivers them on the physics clock. See SpringPhysicsQueue.
            SpringPhysicsQueue.Flush();
        }
#endif

        private void OnApplicationQuit()
        {
#if SASQ_PHYSICS || SASQ_PHYSICS2D
            SpringPhysicsQueue.Clear();
#endif
            // In the editor the editor driver owns engine lifetime — editor-window
            // springs must keep working after leaving play mode.
            if (!Application.isEditor) SpringEngine.ShutdownInstance();
        }
    }
}
