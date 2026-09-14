using System;
using SasquatchInspector.Editor.LiveDebug;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The Showcase Sandbox's own Live Debug demo clock: it wiggles a few of the shown object's values and
    /// writes one sample per tick, so the Live Debug page draws a real, moving trace the instant the bug glyph
    /// is clicked — without leaving the editor.
    ///
    /// <para><b>Why this exists.</b> Live Debug graphs a value CHANGING over time, and nothing changes a
    /// throwaway sandbox ScriptableObject. Armed with no driver every strip is a flat line, which is the exact
    /// thing the feature is supposed to make impossible to miss.</para>
    ///
    /// <para><b>Why it does NOT just turn the real clock on.</b> <see cref="DebugSampleClock"/> refuses to
    /// splice into the player loop outside a play session, and <c>DebugRecorder.SampleAllArmed</c> restates that
    /// refusal at the write. That gate is load-bearing: the editor ticks a player loop too, so widening it puts
    /// EVERY recorder in the editor back to recording its own unchanging serialized values — the "all my graphs
    /// are flat blue lines" bug. So this drives ONE recorder, directly, through the per-recorder
    /// <c>DebugRecorder.Sample</c>, which has no such gate because it is not a sweep. The global gate is
    /// untouched; <see cref="DebugSampleClock.SandboxType"/> only tells the CHROME that a clock is running, so
    /// the transport shows a live Record button instead of "press play to record".
    /// </para>
    ///
    /// <para><b>Play Mode still wins.</b> While the editor is playing this stops sampling entirely and hands the
    /// recorder back to the real clock — two clocks writing different tick sources into one ring is a trace that
    /// clears itself every frame. It keeps ANIMATING though, so pressing Play from the sandbox demos the real
    /// path rather than freezing the values it was just moving.</para>
    /// </summary>
    internal sealed class ShowcaseLiveDebugDriver
    {
        /// <summary>Matches DebugRunner's own redraw tick, and lands near the recorder's default 10/sec Interval rate.</summary>
        private const long TickMs = 16;

        private readonly Func<SerializedObject> _current;
        private IVisualElementScheduledItem _tick;
        private double _startedAt;

        /// <summary>The type we last drove into Recording, so the start is an EDGE rather than a per-tick reset.</summary>
        private Type _armedType;

        internal ShowcaseLiveDebugDriver(Func<SerializedObject> current) => _current = current;

        /// <summary>
        /// Scheduled on the WINDOW root, not the preview container: switching page clears the preview, and a
        /// driver that died with it would have to be re-installed on every rebuild. Reading the live
        /// SerializedObject through a callback keeps this correct across page changes for free.
        /// </summary>
        internal void Install(VisualElement windowRoot)
        {
            if (windowRoot == null || _tick != null) return;
            _startedAt = EditorApplication.timeSinceStartup;
            _tick = windowRoot.schedule.Execute(Tick).Every(TickMs);
        }

        internal void Dispose()
        {
            _tick?.Pause();
            _tick = null;
            Release();
        }

        /// <summary>Hands the chrome back to Play Mode's answer. Called on teardown and whenever we stop driving.</summary>
        private void Release()
        {
            _armedType = null;
            if (DebugSampleClock.SandboxType != null) DebugSampleClock.SandboxType = null;
        }

        private void Tick()
        {
            var so = _current?.Invoke();
            var target = so != null ? so.targetObject : null;
            if (target == null) { Release(); return; }

            var type = target.GetType();
            // The bug glyph is the switch. Nothing moves until the user asks for it, so an ordinary page is
            // never a sandbox whose values shift under the cursor while they are reading it.
            if (!DebugModeState.IsOn(type)) { Release(); return; }

            if (EditorApplication.isPlaying)
            {
                // The player loop owns the ring now. Keep the values moving so the real clock has something
                // worth recording, but write nothing — see the class note on two clocks in one ring.
                if (DebugSampleClock.SandboxType != null) DebugSampleClock.SandboxType = null;
                _armedType = null;
                Animate(so);
                return;
            }

            DebugSampleClock.SandboxType = type;

            // On the OFF-to-ON edge only. Record() CLEARS the buffer by design, so calling it per tick would
            // hold the trace permanently one sample long — and it would also stomp the transport's Stop button,
            // which is a control this page exists to demonstrate.
            if (_armedType != type)
            {
                _armedType = type;
                DebugCaptureController.Record(type);
            }

            Animate(so);

            // Stop on the transport freezes the buffer (phase Held) and scrubbing keeps working — so honour the
            // phase here rather than sampling whenever the glyph is lit.
            if (DebugCaptureController.PhaseOf(type) != DebugCapturePhase.Recording) return;

            // Interval, not Update: Sample() de-dupes Update/LateUpdate on Time.frameCount, which is a GAME
            // frame counter and does not advance usefully in Edit Mode — every sample after the first would be
            // thrown away. Interval is gated on wall-clock instead, and it reads the user's own samples/sec
            // setting from the transport's gear, so the sandbox demonstrates that control too.
            DebugRecorder.For(so)?.Sample(DebugTickSource.Interval);
        }

        /// <summary>
        /// Moves a few well-known fields, if the object on screen happens to have them. Deliberately by NAME
        /// with a null check rather than by type: every Showcase page hands us a different target, and the ones
        /// without these fields simply sit still instead of needing a registry of which page is which.
        ///
        /// <para>Four shapes on purpose, because the point of the page is that a graph tells you something a
        /// number cannot: a smooth curve, a busier curve, a staircase, and a square wave.</para>
        /// </summary>
        private void Animate(SerializedObject so)
        {
            // A pending edit means the user is mid-drag on a field. Update() would snatch it back, so skip the
            // whole tick — the same guard, for the same reason, as DebugRecorder.Sample.
            if (so.hasModifiedProperties) return;
            so.UpdateIfRequiredOrScript();

            float t = (float)(EditorApplication.timeSinceStartup - _startedAt);

            var health = so.FindProperty("health");
            if (health != null && health.propertyType == SerializedPropertyType.Float)
                health.floatValue = Mathf.Clamp(50f + 42f * Mathf.Sin(t * 0.55f) + 4f * Mathf.Sin(t * 5.3f), 0f, 100f);

            var throttle = so.FindProperty("throttle");
            if (throttle != null && throttle.propertyType == SerializedPropertyType.Float)
                throttle.floatValue = 30f + 28f * Mathf.Sin(t * 1.3f + 1.1f);

            var waveSize = so.FindProperty("waveSize");
            if (waveSize != null && waveSize.propertyType == SerializedPropertyType.Integer)
                waveSize.intValue = 1 + Mathf.FloorToInt(t * 0.5f) % 8;

            var invincible = so.FindProperty("invincible");
            if (invincible != null && invincible.propertyType == SerializedPropertyType.Boolean)
                invincible.boolValue = Mathf.Sin(t * 0.45f) > 0.55f;

            // WithoutUndo: sixty ticks a second of demo wiggle must never reach the undo stack, where it would
            // bury whatever the user actually did on the page under a wall of identical entries.
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
