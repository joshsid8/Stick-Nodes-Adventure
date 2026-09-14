using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 11 — Live Debug. The one exhibit in this scene that only makes its point in PLAY MODE.
    ///
    /// Every other demo component is static data: you look at the panel and the panel looks back. Live Debug
    /// is the opposite — it records every field on this script WHILE THE GAME RUNS, draws a trace under each
    /// row, and lets you scrub back to any frame and ask "what else changed here?". None of that is visible
    /// on a component that never moves, which is why this one has an <c>Update()</c> and the others do not.
    ///
    /// <para><b>How to try it:</b> select this object, click the bug glyph in the panel header (hover the
    /// header to reveal it), then press Play. Traces fill in under the rows. Stop, then click anywhere on a
    /// trace to open the moment card for that frame.</para>
    ///
    /// <para>The driver below is deliberately shaped for READING, not for realism. The state machine steps in
    /// blocks so the enum trace is a staircase; velocity ramps with a small tremor so the line has texture as
    /// well as shape; <c>isGrounded</c> carries one two-frame blip mid-sprint that you cannot see in the
    /// Inspector but can see instantly in the trace — that blip is the whole argument for a bool channel.</para>
    ///
    /// <para>Still no attributes, no custom editor, no partial class: the panel is a layout asset, same as
    /// every other exhibit. The only thing this script does that the others don't is CHANGE.</para>
    /// </summary>
    public class DemoLiveDebug : MonoBehaviour
    {
        public enum MoveState { Idle, Windup, Sprint, Airborne, Recover }

        // ---- Recorded channels ---------------------------------------------------------------------------
        // Nothing marks these as "recorded" — Live Debug watches every serialized field on the script. The
        // grouping here is for the reader, not for the package.

        [Tooltip("A float channel: a smooth ramp with a tremor on top, so the trace has shape AND texture.")]
        public float velocity;

        [Tooltip("A bool channel. Watch for the two-frame blip mid-sprint — invisible in the Inspector, obvious in the trace.")]
        public bool isGrounded = true;

        [Tooltip("An enum channel. Records the VALUE, so the trace reads as a labelled staircase.")]
        public MoveState state = MoveState.Idle;

        [Tooltip("A composite channel — three series (X/Y/Z) that separate visibly on one graph.")]
        public Vector3 position;

        [Tooltip("An int channel, stepping once per cycle beat.")]
        public int comboCount;

        [Range(0f, 100f)]
        [Tooltip("Drains while sprinting and recovers otherwise — the classic 'why did this run out?' trace.")]
        public float stamina = 100f;

        [Tooltip("A string channel. Quiet strings still record; the trace shows WHEN it changed, not a curve.")]
        public string currentAction = "idle";

        // ---- Driver --------------------------------------------------------------------------------------

        [Tooltip("Turn off to freeze the values and scrub a trace you already captured.")]
        public bool animate = true;

        [Tooltip("Writes a warning at 3.1s and an error at 5.4s of every cycle, so the moment card has real " +
                 "console rows to show beside the values.")]
        public bool emitDemoLogs = true;

        [Range(0.25f, 4f)]
        [Tooltip("Speeds the whole cycle up or down. Slow it right down to watch a single transition.")]
        public float speedScale = 1f;

        private float _lastCycle = -1f;
        private int _loggedThisCycle;

        private void Update()
        {
            if (!animate) return;

            float t = Time.time * speedScale;
            float cycle = Mathf.Repeat(t, 6f);

            // A new cycle resets the per-cycle log latch.
            if (cycle < _lastCycle) _loggedThisCycle = 0;
            _lastCycle = cycle;

            // ---- state machine: the enum channel's step trace ----
            if (cycle < 0.8f) state = MoveState.Idle;
            else if (cycle < 1.6f) state = MoveState.Windup;
            else if (cycle < 3.4f) state = MoveState.Sprint;
            else if (cycle < 4.6f) state = MoveState.Airborne;
            else state = MoveState.Recover;

            currentAction = state switch
            {
                MoveState.Idle => "idle",
                MoveState.Windup => "crouch",
                MoveState.Sprint => "sprint",
                MoveState.Airborne => "jump",
                _ => "land",
            };

            // ---- velocity: a smooth ramp with a small tremor, so the line has shape AND texture ----
            float target = state switch
            {
                MoveState.Idle => 0f,
                MoveState.Windup => 1.4f,
                MoveState.Sprint => 8.6f,
                MoveState.Airborne => 6.1f,
                _ => 1.8f,
            };
            velocity = Mathf.Lerp(velocity, target, 6f * Time.deltaTime) + Mathf.Sin(t * 21f) * 0.18f;

            // ---- grounded: false through the airborne window, plus ONE deliberate two-frame blip during the
            // sprint. The blip is the reason a bool channel earns its row: it is exactly the kind of thing you
            // cannot catch by staring at the Inspector, and cannot miss in a trace.
            bool blip = cycle > 2.55f && cycle < 2.62f;
            isGrounded = state != MoveState.Airborne && !blip;

            // ---- position: three series that separate visibly ----
            position = new Vector3(
                Mathf.Sin(t * 0.9f) * 4f,
                state == MoveState.Airborne ? Mathf.Sin((cycle - 3.4f) / 1.2f * Mathf.PI) * 2.6f : 0f,
                Mathf.Cos(t * 0.6f) * 2.2f);

            // ---- stamina: drains while sprinting, recovers otherwise ----
            stamina = Mathf.Clamp(
                stamina + (state == MoveState.Sprint ? -34f : 18f) * Time.deltaTime, 0f, 100f);

            comboCount = Mathf.FloorToInt(cycle * 1.5f);

            if (!emitDemoLogs) return;

            // Two fixed marks per cycle. Latched so one crossing logs once, not once per frame. Live Debug pins
            // console rows onto the timeline where they happened, so the moment card can answer "what was the
            // state when this warning fired?" without a single extra Debug.Log of your own.
            if (_loggedThisCycle < 1 && cycle >= 3.1f)
            {
                _loggedThisCycle = 1;
                Debug.LogWarning($"[Demo] Stamina low before jump ({stamina:0}%) — combo {comboCount} at risk.", this);
            }
            else if (_loggedThisCycle < 2 && cycle >= 5.4f)
            {
                _loggedThisCycle = 2;
                Debug.LogError($"[Demo] Landing recovery overran its budget (velocity {velocity:0.0}).", this);
            }
        }

        // ---- Buttons ---------------------------------------------------------------------------------------
        // Wired to Button nodes in the designed layout. The three below drive Live Debug FROM CODE, which is
        // the half of the feature the header's bug glyph doesn't show: SasquatchDebug is a plain public runtime
        // API, so a recording can start on a scene load, stop on a failed assertion, or drop a labelled marker
        // the instant your own code notices something. In a player build every call compiles to nothing.

        /// <summary>Starts recording this script, exactly as if you had clicked the header's bug glyph.</summary>
        public void StartRecording() => Runtime.SasquatchDebug.Record(this);

        /// <summary>Stops recording. The captured trace stays on screen to scrub.</summary>
        public void StopRecording() => Runtime.SasquatchDebug.Stop(this);

        /// <summary>Drops a labelled pin on the timeline at this instant — call it from anywhere in your code.</summary>
        public void MarkThisMoment() => Runtime.SasquatchDebug.Mark($"combo {comboCount} @ {state}");

        /// <summary>Snaps the run back to the top of a cycle, for a clean trace from a known start.</summary>
        public void ResetRun()
        {
            velocity = 0f;
            stamina = 100f;
            comboCount = 0;
            position = Vector3.zero;
            state = MoveState.Idle;
            currentAction = "idle";
            _lastCycle = -1f;
            _loggedThisCycle = 0;
        }
    }
}
