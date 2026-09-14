using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// Carries recorded traces across a domain reload.
    ///
    /// <para><b>Why this exists.</b> A recompile wipes every static in the editor, and it can land at exactly
    /// the moment the recording becomes worth reading. A plain static buffer therefore cannot be the only copy
    /// of a trace, and a <c>ScriptableSingleton</c> is the one storage that outlives a domain reload.</para>
    ///
    /// <para><b>"Leaving Play Mode IS a domain reload" is NOT true here, and this file used to say it was.</b>
    /// It holds only with Unity's DEFAULT Enter Play Mode settings. Switch the domain reload off — this
    /// project has, since its first commit, and it is the first thing anyone reaches for to make iteration
    /// fast — and <c>beforeAssemblyReload</c> never fires on either play-mode edge. Hooking the capture there
    /// alone meant nothing was ever written, so the inspector that rebuilt after a run restored an EMPTY trace
    /// and reported "no data recorded" about a recording that had just finished. <see cref="PersistAll"/> is
    /// therefore driven from <c>ExitingPlayMode</c> as well; see the hook in <c>DebugRecorder</c>'s static
    /// constructor.</para>
    ///
    /// <para>Samples persist as a <c>byte[]</c> per channel rather than a float list. Unity serializes a byte
    /// array as one compact blob, while a list of 1,200 floats per channel becomes 1,200 YAML lines that would
    /// be re-parsed on every recompile — a cost paid constantly for a feature used occasionally.</para>
    ///
    /// <para>The window persisted is capped below the live ring: a trace is for reading back the last few
    /// seconds, and a 30-script accident must not write megabytes on every reload.</para>
    /// </summary>
    [FilePath("Library/SasquatchInspector/LiveDebugTrace.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class DebugTraceStore : ScriptableSingleton<DebugTraceStore>
    {
        /// <summary>Samples carried across a reload. Below the live ring on purpose — see the class note.</summary>
        internal const int PersistedSamples = 600;

        [Serializable]
        private sealed class ChannelBlob
        {
            public string Path;
            public byte[] Values;      // float[] as bytes, series-interleaved
            public int SeriesCount;
            public int[] ChangeSamples;
            public string[] ChangeText;
            public bool EverChanged;
            public float SeenMin;
            public float SeenMax;
        }

        [Serializable]
        private sealed class TraceBlob
        {
            public string TargetKey;   // scene path + type, stable across a reload (instance ids are NOT)
            public int Count;
            public int Absolute;
            public double[] Times;
            public int[] Frames;
            public List<ChannelBlob> Channels = new List<ChannelBlob>();
        }

        [SerializeField] private List<TraceBlob> _traces = new List<TraceBlob>();

        /// <summary>
        /// A reload-stable identity for a target. Instance ids are explicitly per-session
        /// (see <c>UnityObjectId</c>), so they cannot be the key on either side of a domain reload.
        /// </summary>
        private static string KeyFor(UnityEngine.Object target)
        {
            if (target == null) return null;
            var comp = target as Component;
            if (comp != null)
            {
                // NAME + SIBLING INDEX at every level. A name-only path cannot separate two children called
                // "Hand" under one parent, which is an ordinary rig rather than a corner case, and Restore
                // returns on the first key match — so the second one silently received the first one's trace.
                var t = comp.transform;
                string path = comp.gameObject.name + "[" + t.GetSiblingIndex() + "]";
                while (t.parent != null)
                {
                    t = t.parent;
                    path = t.name + "[" + t.GetSiblingIndex() + "]/" + path;
                }

                // …and the ORDINAL among same-type components on this GameObject. Two of one script on one
                // object share everything above, so without this they shared a key: A recorded 11, B recorded
                // 22, and after a reload BOTH came back holding 11 while B's trace became unreachable forever.
                // The user cannot even see it happen — same type means the same channel paths and the same
                // labels, and nothing on screen names the object a trace came from.
                //
                // Both halves are as reload-stable as the name was: a sibling index and a component order only
                // change when the user reorders them, which is the same event that would invalidate a name.
                int ordinal = 0;
                var siblings = comp.GetComponents(comp.GetType());
                for (int i = 0; i < siblings.Length; i++)
                {
                    if (ReferenceEquals(siblings[i], comp)) { ordinal = i; break; }
                }

                return comp.gameObject.scene.path + "|" + path + "#" + ordinal + "|" + target.GetType().FullName;
            }
            string assetPath = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(assetPath)) return assetPath + "|" + target.GetType().FullName;

            // NO ASSET PATH: an in-memory object. Its NAME is not an identity — two ScriptableObjects created
            // in one session are routinely named alike (they default to the type name), and sharing a key made
            // one of them silently restore the other's trace with nothing on screen to say so. There is no
            // cross-domain identity to offer instead, because an unsaved object does not survive a domain
            // reload either; the instance id is the honest answer, and a key that stops matching after a
            // reload is CORRECT for a target that no longer exists. Within a session it keeps them apart,
            // which is the whole of what can be promised.
            return target.name + "#" + UnityObjectId.KeyOf(target) + "|" + target.GetType().FullName;
        }

        /// <summary>Writes every live recorder into the singleton and flushes it. Hooked to beforeAssemblyReload.</summary>
        internal static void PersistAll()
        {
            try
            {
                var store = instance;
                store._traces.Clear();
                foreach (var kv in DebugRecorder.Live)
                {
                    // Per RECORDER, not per sweep. The outer catch below is the reload's seatbelt, but it was
                    // also the only one — so a single recorder holding a freed SerializedObject threw, the
                    // loop was abandoned with `_traces` already cleared and `Save` never reached, and EVERY
                    // healthy trace in the session was lost along with it. Worse, the file on disk kept the
                    // PREVIOUS domain's traces, so the next restore handed back stale data that looked real.
                    try
                    {
                        var blob = kv.Value.Capture();
                        if (blob != null) store._traces.Add((TraceBlob)blob);
                    }
                    catch
                    {
                        // This recorder's trace is gone. Its neighbours' are not, which is the whole point.
                    }
                }
                store.Save(true);
            }
            catch
            {
                // A trace is a convenience. Losing one across a reload must never take the reload with it.
            }
        }

        /// <summary>Repopulates a freshly created recorder from whatever the previous domain left for this target.</summary>
        internal static void Restore(UnityEngine.Object target, DebugRecorder recorder)
        {
            try
            {
                string key = KeyFor(target);
                if (string.IsNullOrEmpty(key)) return;
                var store = instance;
                for (int i = 0; i < store._traces.Count; i++)
                {
                    if (!string.Equals(store._traces[i].TargetKey, key, StringComparison.Ordinal)) continue;
                    recorder.Adopt(store._traces[i]);
                    return;
                }
            }
            catch { /* as above — a restore failure costs the trace, nothing else */ }
        }

        internal static void ClearAll()
        {
            try
            {
                instance._traces.Clear();
                instance.Save(true);
            }
            catch { }
        }

        // ---- the recorder-facing shapes, kept here so TraceBlob stays private ----------------------------

        internal static object NewTrace(string key, int count, int absolute, double[] times, int[] frames)
            => new TraceBlob { TargetKey = key, Count = count, Absolute = absolute, Times = times, Frames = frames };

        internal static void AddChannel(object trace, string path, byte[] values, int series,
            int[] changeSamples, string[] changeText, bool everChanged, float seenMin, float seenMax)
        {
            var t = trace as TraceBlob;
            if (t == null) return;
            t.Channels.Add(new ChannelBlob
            {
                Path = path, Values = values, SeriesCount = series,
                ChangeSamples = changeSamples, ChangeText = changeText,
                EverChanged = everChanged, SeenMin = seenMin, SeenMax = seenMax,
            });
        }

        internal static string TraceKey(UnityEngine.Object target) => KeyFor(target);

        internal static void ReadTrace(object trace, out int count, out int absolute,
            out double[] times, out int[] frames, out int channelCount)
        {
            var t = (TraceBlob)trace;
            count = t.Count; absolute = t.Absolute; times = t.Times; frames = t.Frames;
            channelCount = t.Channels.Count;
        }

        internal static void ReadChannel(object trace, int i, out string path, out byte[] values, out int series,
            out int[] changeSamples, out string[] changeText, out bool everChanged, out float seenMin, out float seenMax)
        {
            var c = ((TraceBlob)trace).Channels[i];
            path = c.Path; values = c.Values; series = c.SeriesCount;
            changeSamples = c.ChangeSamples; changeText = c.ChangeText;
            everChanged = c.EverChanged; seenMin = c.SeenMin; seenMax = c.SeenMax;
        }
    }
}
