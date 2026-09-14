#if SASQ_AUDIO // AudioClip lives in com.unity.modules.audio, a REMOVABLE built-in module.
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Editor.Audio
{
    /// <summary>
    /// Extracts interleaved float PCM from an <see cref="AudioClip"/> via the public <c>AudioClip.GetData</c>
    /// (mirrors <see cref="AudioWaveformRenderer"/>), caching it by instance id with a small soft cap so that
    /// the playback path — which the widget hits ~60×/s while scrubbing — never re-reads the same clip.
    /// <c>GetData</c> returns false for streaming / compressed-in-memory clips; those cache as "not playable"
    /// and the widget surfaces a "Decompress On Load" hint instead of failing silently.
    /// </summary>
    internal static class AudioClipPcm
    {
        private struct Entry
        {
            public float[] data;
            public int channels;
            public int frequency;
            public int frames;   // per-channel
            public bool ok;
            // The load type this entry was decided under. A clip's instance id survives a REIMPORT, so
            // without this a "not playable" verdict is permanent: the widget tells the user to switch the
            // clip to Decompress On Load, they do exactly that, and the cache keeps serving the old No.
            // Comparing it makes the cache self-heal on the very next read — no invalidation call to
            // remember, no menu item to run.
            public AudioClipLoadType loadType;
        }

        private const int MaxEntries = 8;
        private static readonly Dictionary<int, Entry> Cache = new Dictionary<int, Entry>();

        /// <summary>
        /// Cache ids, least-recently-used first. <see cref="AudioWaveformRenderer"/> now shares this cache
        /// and probes it once per frame of a live resize, so the old "at the cap, drop the lot" behaviour
        /// meant a ninth clip in the inspector forced the other eight to re-decode on the very next frame.
        /// Evicting ONE entry keeps a working set intact instead.
        /// <para>
        /// Unlike the waveform textures, these arrays need no evict-notify protocol: they are plain managed
        /// <c>float[]</c> read within a single call, never retained by a widget the way <c>Image.image</c>
        /// retains a texture, so dropping one can never blank anything on screen.
        /// </para></summary>
        private static readonly List<int> Lru = new List<int>();

        /// <summary>
        /// Returns the cached/loaded PCM for <paramref name="clip"/>. False (with null/zero outs) when the clip
        /// is null or <c>GetData</c> refuses it (streaming/compressed) — callers then stay silent.
        /// </summary>
        public static bool TryGet(AudioClip clip, out float[] pcm, out int channels, out int rate, out int frames)
        {
            pcm = null; channels = 0; rate = 0; frames = 0;
            if (clip == null) return false;

            int id = UnityObjectId.Of(clip);
            AudioClipLoadType loadType;
            try { loadType = clip.loadType; }
            catch { return false; } // destroyed under us — nothing to cache a verdict about

            // A cached verdict is only valid for the load type it was taken under (see Entry.loadType).
            if (!Cache.TryGetValue(id, out var e) || e.loadType != loadType)
            {
                e = Load(clip, loadType);
                // Soft cap (these arrays are large), evicting oldest-first rather than wholesale.
                while (Cache.Count >= MaxEntries && Lru.Count > 0)
                {
                    Cache.Remove(Lru[0]);
                    Lru.RemoveAt(0);
                }
                Cache[id] = e;
            }
            Touch(id);
            if (!e.ok) return false;

            pcm = e.data; channels = e.channels; rate = e.frequency; frames = e.frames;
            return true;
        }

        /// <summary>True when the clip's samples can be read for playback (drives the widget's preview-availability badge).</summary>
        public static bool IsClipPlayable(AudioClip clip) => TryGet(clip, out _, out _, out _, out _);

        private static Entry Load(AudioClip clip, AudioClipLoadType loadType)
        {
            try
            {
                // GetData logs a Unity console error for non-DecompressOnLoad clips (streaming / compressed-in-
                // memory). Pre-check loadType so probing playability never spams the console — only DecompressOnLoad
                // clips can be read, which is exactly what the "Decompress On Load" widget hint tells the user.
                if (loadType != AudioClipLoadType.DecompressOnLoad) return new Entry { ok = false, loadType = loadType };

                int ch = Mathf.Max(1, clip.channels);
                int frames = clip.samples;
                if (frames <= 0) return new Entry { ok = false, loadType = loadType };
                var data = new float[frames * ch];
                bool ok = clip.GetData(data, 0);
                return ok
                    ? new Entry { data = data, channels = ch, frequency = clip.frequency, frames = frames, ok = true, loadType = loadType }
                    : new Entry { ok = false, loadType = loadType };
            }
            catch
            {
                return new Entry { ok = false, loadType = loadType };
            }
        }

        /// <summary>
        /// Moves <paramref name="id"/> to the most-recently-used end. N is <see cref="MaxEntries"/>, so a
        /// list scan is cheaper than the bookkeeping an ordered map would need.
        /// </summary>
        private static void Touch(int id)
        {
            int at = Lru.IndexOf(id);
            if (at >= 0) Lru.RemoveAt(at);
            Lru.Add(id);
        }

        /// <summary>Drop a single clip's cached PCM (e.g. when its import settings change).</summary>
        public static void Invalidate(AudioClip clip)
        {
            if (clip == null) return;
            int id = UnityObjectId.Of(clip);
            Cache.Remove(id);
            Lru.Remove(id); // must stay in lockstep, or the cap loop runs out of victims and the cache grows
        }

        /// <summary>Free all cached PCM (called on domain reload / quit so large float[]s don't leak across reloads).</summary>
        public static void Clear()
        {
            Cache.Clear();
            Lru.Clear();
        }
    }
}
#endif // SASQ_AUDIO
