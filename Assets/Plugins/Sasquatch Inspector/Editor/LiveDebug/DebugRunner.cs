using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// Drives one built inspector's Live Debug: a single scheduled tick that samples the recorder and refreshes
    /// every graph on it.
    ///
    /// <para><b>One tick per inspector, not one per field.</b> Every channel has to be written on the same
    /// pass or the shared sample index stops meaning a shared instant, and the whole co-change readout
    /// collapses. It is also the cheap arrangement: N fields cost N property reads inside one callback rather
    /// than N scheduled items.</para>
    ///
    /// <para><b>Cost at rest.</b> The tick's first statement is a captured bool read. That matters more here
    /// than it looks: an <c>Every(16)</c> item fires ~10x/second with the editor idle and up to ~57x/second
    /// while it is busy — it rides the editor update loop, not panel dirtiness — so anything above that early
    /// return would run forever in every inspector that ever enabled the toggle. The arm flag is refreshed by
    /// the <see cref="DebugModeState.Changed"/> event rather than re-read per tick, so switching debug on is
    /// what costs, never leaving it off.</para>
    ///
    /// <para><b>No Pause() on detach.</b> UI Toolkit already pauses a scheduled item on detach and resumes it
    /// on re-attach; an explicit <c>Pause()</c> is never resumed and would silently kill recording for the rest
    /// of the session after a window tab drag. Event unsubscription goes through
    /// <c>PanelLifetime.OnRealDetach</c>, which re-tests one tick later so a re-parent frees nothing.</para>
    /// </summary>
    internal static class DebugRunner
    {
        private const long TickMs = 16;

        /// <summary>
        /// How many inspectors are recording right now. Surfaced in the transport bar so a "turned it on for
        /// thirty scripts by mistake" is something you can SEE rather than something you only feel as a slow
        /// editor. Counted from armed runners rather than from the toggle, so it tracks what is actually costing.
        /// </summary>
        internal static int RecordingCount { get; private set; }

        private static readonly System.Collections.Generic.HashSet<object> s_armedRunners =
            new System.Collections.Generic.HashSet<object>();

        private static void SetArmed(object token, bool armed)
        {
            if (armed) s_armedRunners.Add(token);
            else s_armedRunners.Remove(token);
            RecordingCount = s_armedRunners.Count;
        }

        /// <summary>
        /// Installs the tick on a freshly built inspector root. No-ops when the build produced no graphs, so a
        /// layout without the toggle pays nothing at all.
        /// </summary>
        internal static void Install(VisualElement root, Type targetType, DebugRecorder recorder,
            List<DebugGraphElement> graphs, Action<DebugGraphElement, int> onSampleClicked,
            DebugTransportBar transport = null)
        {
            if (root == null || recorder == null) return;

            // A build that produced NO graphs still counted as a LOOK at this recorder, and saying so is the
            // whole of PruneDead's ability to tell "minted a moment ago, about to be used" from "residue".
            // Returning before this left a channel-less recorder — the shape you get from a layout whose
            // binding paths stopped resolving after a script refactor — un-evictable for the rest of the
            // domain, picking up a fresh un-evictable twin on every scene reload as the object id changed.
            if (graphs == null || graphs.Count == 0) { recorder.MarkBuilt(); return; }

            // Copied: the caller's list is a build-scope buffer that the next build clears.
            var owned = new List<DebugGraphElement>(graphs);

            // ONE readout width for the whole inspector, sized to the widest channel on this recorder. Here
            // rather than in the graph's own constructor because that runs per ROW, mid-walk, when only the
            // channels registered so far exist — the first row would size itself against a single channel and
            // the rows would stagger. This is the first moment the whole set is known.
            int widestSeries = 1;
            var channels = recorder.Channels;
            for (int i = 0; i < channels.Count; i++)
                if (channels[i].SeriesCount > widestSeries) widestSeries = channels[i].SeriesCount;
            float readoutWidth = DebugGraphElement.ReadoutWidthFor(widestSeries);
            for (int i = 0; i < owned.Count; i++) owned[i].SetReadoutWidth(readoutWidth);
            // Observe, not IsOn: this is the cache DebugModeState.Resync exists to correct. Reading it any
            // other way leaves the announced value unset, and the first out-of-band write by an exported
            // editor's baked twin then costs the user a press. See s_announced in DebugModeState.
            bool armed = DebugModeState.Observe(targetType);
            // ClockRunningFor, not isPlaying: the redraw tick has to run wherever samples are actually being
            // written, and in the Showcase Sandbox that is Edit Mode (see DebugSampleClock.SandboxType).
            bool wasClockRunning = DebugSampleClock.ClockRunningFor(targetType);
            var token = new object();

            // COUNTED ON ATTACH, NOT ON BUILD. A tree can be built and thrown away without ever reaching a
            // panel (a speculative rebuild, a preview that is replaced before it is shown), and
            // PanelLifetime.OnRealDetach cannot fire for something that never attached — so a token registered
            // here at build time stayed in s_armedRunners for the rest of the domain, permanently inflating
            // the "N inspectors recording" figure in the transport bar. That figure exists precisely so a
            // "turned it on for thirty scripts by mistake" is visible, which makes a drifting count worse than
            // no count. The recorder's viewer count rides along for the same reason (see DebugRecorder.PruneDead).
            bool live = false;

            void GoLive()
            {
                if (live) return;
                live = true;
                SetArmed(token, armed);
                recorder.AddViewer();
                // A body can be built, parked and re-homed a second later (the takeover's reuse slot) — and the
                // shared key can have been written by an exported editor's twin in between, with no event of ours.
                // Landing on a panel is the moment to find out; the header's hover is the other one.
                DebugModeState.Resync(targetType);
            }

            void SyncAll()
            {
                for (int i = 0; i < owned.Count; i++) owned[i].Sync();
                transport?.Sync();
            }

            void ApplyArmed()
            {
                var display = armed ? DisplayStyle.Flex : DisplayStyle.None;
                for (int i = 0; i < owned.Count; i++) owned[i].style.display = display;
                // Not style.display: the bar floats, and the gap it occupies in the flow belongs to a separate
                // anchor element that has to disappear with it.
                transport?.SetVisible(armed);
                // The tooltip mute travels with the graphs. Left armed after debug mode is switched off it
                // would silence an ordinary inspector for no visible reason, which reads as a broken package
                // rather than as a setting.
                DebugTooltipMute.SetArmed(root, armed);

                // SO DO THE CARDS. They are parented to the build root rather than to the rows, which is what
                // lets them float over the inspector — and it also means hiding the rows leaves them behind,
                // still open, still describing a graph that is no longer on screen. Switching the feature off
                // has to take everything it drew with it, or the leftovers read as a window that will not
                // close. Unconditional rather than only-when-disarming: re-arming should never inherit a card
                // from the last time the toggle was on.
                if (!armed)
                {
                    DebugMomentCard.Hide(root);
                    DebugSettingsCard.Hide(root);
                    DebugPeekCard.Hide(root);
                }
            }

            void OnDebugStateChanged()
            {
                bool next = DebugModeState.IsOn(targetType);
                if (next == armed) return;
                armed = next;
                if (live) SetArmed(token, armed);
                // Leaving debug releases the playhead, so re-arming never starts parked on a sample from a
                // recording the user has stopped looking at.
                if (!armed) recorder.SetScrub(-1);
                // Console capture is global (one hook for the editor), so it is armed by ANY recording
                // inspector. Deliberately not unhooked here: a second inspector may still be recording, and
                // the play-mode/reload handlers in DebugLogCapture are what actually tear it down.
                if (armed) { DebugLogCapture.Hook(); DebugSampleClock.Install(); }
                ApplyArmed();
                SyncAll();
            }

            for (int i = 0; i < owned.Count; i++)
            {
                var graph = owned[i];
                graph.SampleClicked += abs => onSampleClicked?.Invoke(graph, abs);
            }

            DebugModeState.Changed += OnDebugStateChanged;
            recorder.ScrubChanged += SyncAll;
            // Focusing a row re-draws every row, not just the two involved: the focus tell is exclusive, so
            // the row LOSING focus has to hear about it as well as the row gaining it.
            recorder.FocusChanged += SyncAll;
            // These three are released even when the tree never reaches a panel — PanelLifetime queues a watch
            // registered off-panel and settles it a drain later, precisely because this release used to be
            // unreachable for a discarded build. A leaked SyncAll is not merely a delegate: it pins the whole
            // discarded graph list alive and re-runs on every future scrub.
            PanelLifetime.OnRealDetach(root, () =>
            {
                DebugModeState.Changed -= OnDebugStateChanged;
                recorder.ScrubChanged -= SyncAll;
                recorder.FocusChanged -= SyncAll;
                // A torn-down inspector is no longer costing anything, so it must stop counting toward the
                // recording total — otherwise the warning creeps upward across a session of normal selection.
                SetArmed(token, false);
                if (live) { live = false; recorder.RemoveViewer(); }
            }, releaseIfNeverAttached: true);

            // Already on a panel (an inspector rebuilt in place) counts immediately; otherwise wait for the
            // real attach. A re-parent does not re-fire this, because the callback unregisters itself.
            if (root.panel != null) GoLive();
            else
            {
                void OnFirstAttach(AttachToPanelEvent _)
                {
                    root.UnregisterCallback<AttachToPanelEvent>(OnFirstAttach);
                    GoLive();
                }
                root.RegisterCallback<AttachToPanelEvent>(OnFirstAttach);
            }

            root.schedule.Execute(() =>
            {
                if (!armed) return;

                bool clockRunning = DebugSampleClock.ClockRunningFor(targetType);
                if (clockRunning != wasClockRunning)
                {
                    wasClockRunning = clockRunning;
                    // Leaving play mode keeps the buffer: the recording you just made is the thing you want to
                    // scrub through, and it is the moment people actually look at it.
                    SyncAll();
                }
                if (!clockRunning) return;

                // Pausing freezes the trace and leaves it fully scrubbable — deliberately not a stop, so the
                // readouts stay live against the parked playhead. (The player loop stops on pause anyway, so
                // this is now belt-and-braces: it also stops the redraw from spinning on a frozen buffer.)
                if (EditorApplication.isPaused) return;

                // NOTE: this tick no longer RECORDS — DebugSampleClock does, from inside the game's own player
                // loop. Recording on the editor update loop is what made consecutive samples one to five game
                // frames apart, so "step one frame" could not step one frame. Redraw rate and sample rate are
                // now independent, which is the whole point: this may run at 10Hz on a busy editor without
                // costing the trace a single frame.
                SyncAll();
            }).Every(TickMs);

            if (armed) { DebugLogCapture.Hook(); DebugSampleClock.Install(); }
            ApplyArmed();
            SyncAll();
        }
    }
}
