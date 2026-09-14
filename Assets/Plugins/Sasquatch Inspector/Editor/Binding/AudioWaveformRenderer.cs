#if SASQ_AUDIO // AudioClip lives in com.unity.modules.audio, a REMOVABLE built-in module.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Editor.Audio;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Builds (and caches) a <see cref="Texture2D"/> rendering of an <see cref="AudioClip"/>'s waveform for the
    /// AudioPreview widget. The waveform is a min/max amplitude envelope drawn one column per texture pixel.
    /// Textures are cached keyed by clip instance id + requested size + color so repeated layout passes are cheap;
    /// a different clip is a different key, so a reference swap can never render a stale waveform.
    /// </summary>
    internal static class AudioWaveformRenderer
    {
        private struct Key
        {
            public int clipId;
            public int width;
            public int height;
            public uint wave;
            public uint background;
            // A cheap fingerprint of the clip's AUDIO (sample count · rate · channels · load type). The
            // instance id survives an in-place reimport, so on its own it would keep serving the OLD
            // envelope after the clip's contents or import settings changed.
            public int content;

            public override bool Equals(object obj) =>
                obj is Key k && k.clipId == clipId && k.width == width && k.height == height
                && k.wave == wave && k.background == background && k.content == content;

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = clipId;
                    h = (h * 397) ^ width;
                    h = (h * 397) ^ height;
                    h = (h * 397) ^ (int)wave;
                    h = (h * 397) ^ (int)background;
                    h = (h * 397) ^ content;
                    return h;
                }
            }
        }

        /// <summary>
        /// Packs a colour the way <see cref="Build"/> actually paints it. Build writes <c>Color32</c>, so two
        /// <see cref="Color"/>s that pack identically produce a byte-identical texture — this is exactly as
        /// discriminating as the output, and unlike the <c>color.ToString().GetHashCode()</c> it replaces it
        /// allocates no string per cache probe (that probe runs once per frame of a live resize).
        /// </summary>
        private static uint Pack(Color c)
        {
            Color32 c32 = c;
            return ((uint)c32.r << 24) | ((uint)c32.g << 16) | ((uint)c32.b << 8) | c32.a;
        }

        private static readonly Dictionary<Key, Texture2D> Cache = new Dictionary<Key, Texture2D>();
        private const int MaxCacheEntries = 64;

        /// <summary>
        /// Raised right BEFORE evicted textures are destroyed, so every live widget can re-fetch first.
        /// <para>
        /// A cached texture is handed out by reference and lives on as <c>Image.image</c>. Destroying it out
        /// from under its holders — which the overflow eviction and <see cref="Invalidate"/> both do —
        /// blanks any other waveform showing the same clip at the same size, in this panel or another, with
        /// nothing to bring it back. Evict → notify → THEN destroy: by the time the destroy runs, every
        /// listener has already rebuilt against a fresh texture.
        /// </para></summary>
        internal static event System.Action CacheEvicted;

        // True while CacheEvicted listeners are re-fetching. Their GetWaveform calls repopulate the cache,
        // which could push it back over the cap and re-enter Clear → notify → re-fetch forever.
        private static bool s_evicting;

        // Shared pixel scratch for Build (see its use site). Static rather than per-clip: only one waveform is
        // ever being rasterised at a time, and the buffer holds nothing between builds.
        private static Color32[] s_pixelScratch;

        static AudioWaveformRenderer()
        {
            // The cached textures are HideAndDontSave, so they survive a domain reload as leaked native objects
            // unless destroyed first. Clear on reload/quit (mirrors TutorialImageScaler).
            AssemblyReloadEvents.beforeAssemblyReload += ClearForShutdown;
            EditorApplication.quitting += ClearForShutdown;
        }

        // Reload / quit: destroy everything and do NOT notify. A re-fetch here would mint fresh textures
        // moments before the domain goes away — exactly the leak this hook exists to prevent.
        private static void ClearForShutdown()
        {
            foreach (var kv in Cache)
                if (kv.Value != null) Object.DestroyImmediate(kv.Value);
            Cache.Clear();
            // Managed arrays rather than native textures, so this is about not carrying a stale envelope
            // across a reload — but it belongs on the same hook so there is only one teardown to keep wired.
            Envelopes.Clear();
        }

        /// <summary>The clip-content half of the cache key — see <see cref="Key.content"/>.</summary>
        private static int ContentStamp(AudioClip clip)
        {
            unchecked
            {
                int h = clip.samples;
                h = (h * 397) ^ clip.frequency;
                h = (h * 397) ^ clip.channels;
                h = (h * 397) ^ (int)clip.loadType;
                return h;
            }
        }

        // Destroys `victims` AFTER giving every live holder a chance to re-fetch. Anything re-entered into
        // the cache by that re-fetch is a NEW texture, so the destroy below can't take it back out.
        private static void EvictAndDestroy(List<Texture2D> victims)
        {
            if (victims.Count == 0) return;
            if (!s_evicting)
            {
                s_evicting = true;
                try { CacheEvicted?.Invoke(); }
                catch (System.Exception e) { Debug.LogException(e); } // one bad listener must not leak the rest
                finally { s_evicting = false; }
            }
            foreach (var t in victims)
                if (t != null) Object.DestroyImmediate(t);
        }

        public static Texture2D GetWaveform(AudioClip clip, int width, int height, Color color, Color background)
        {
            if (clip == null || width <= 0 || height <= 0) return null;

            width = Mathf.Clamp(width, 1, 4096);
            height = Mathf.Clamp(height, 1, 1024);

            var key = new Key
            {
                clipId = UnityObjectId.Of(clip),
                width = width,
                height = height,
                wave = Pack(color),
                // The background is painted into the texture too, so it has to discriminate. Keying only on
                // the wave colour served one background's texture to a widget asking for another.
                background = Pack(background),
                content = ContentStamp(clip)
            };

            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var tex = Build(clip, width, height, color, background);
            if (tex != null)
            {
                // Over the cap, drop the lot — but never destroy a texture a live widget is still painting
                // (see CacheEvicted). Done BEFORE inserting so the fresh texture isn't itself evicted, and
                // skipped while re-fetching (s_evicting) so a listener's own GetWaveform can't re-enter here.
                if (!s_evicting && Cache.Count >= MaxCacheEntries)
                    Clear();
                Cache[key] = tex;
            }
            return tex;
        }

        /// <summary>Drops cached textures referencing a specific clip (call when the clip reference changes).</summary>
        public static void Invalidate(AudioClip clip)
        {
            if (clip == null) return;
            int id = UnityObjectId.Of(clip);
            var toRemove = new List<Key>();
            var victims = new List<Texture2D>();
            foreach (var kv in Cache)
                if (kv.Key.clipId == id) { toRemove.Add(kv.Key); victims.Add(kv.Value); }
            foreach (var k in toRemove) Cache.Remove(k);

            // The envelope is derived from the same PCM, so a caller invalidating the clip means it too.
            // Under every content stamp: this is the reimport path, and the stamp is what just changed.
            var staleEnvelopes = new List<EnvelopeKey>();
            foreach (var kv in Envelopes)
                if (kv.Key.clipId == id) staleEnvelopes.Add(kv.Key);
            foreach (var k in staleEnvelopes) Envelopes.Remove(k);

            EvictAndDestroy(victims);
        }

        public static void Clear()
        {
            var victims = new List<Texture2D>(Cache.Values);
            Cache.Clear();
            EvictAndDestroy(victims);
        }

        // ---- the precomputed min/max envelope -----------------------------------------------------------

        /// <summary>
        /// Block size, in per-channel frames, for the precomputed min/max envelope.
        /// <para>
        /// One waveform column spans <c>frames / width</c> samples — for a 10-second clip in a 300px
        /// inspector, ~1,470 — so scanning every column's samples meant each rebuild walked the ENTIRE clip.
        /// That happens once per frame of a live resize. Reading whole blocks instead turns the per-column
        /// cost into <c>framesPerColumn / BlockFrames</c> pairs plus at most two partial blocks. 16 balances
        /// the two halves: smaller makes the partial scans cheap but the block reads many, larger the reverse.
        /// </para></summary>
        private const int BlockFrames = 16;

        private struct Envelope
        {
            public float[] min;
            public float[] max;
        }

        private struct EnvelopeKey
        {
            public int clipId;
            public int content; // same stamp the texture key uses, so a reimport misses here too

            public override bool Equals(object obj) =>
                obj is EnvelopeKey k && k.clipId == clipId && k.content == content;

            public override int GetHashCode() { unchecked { return (clipId * 397) ^ content; } }
        }

        private static readonly Dictionary<EnvelopeKey, Envelope> Envelopes =
            new Dictionary<EnvelopeKey, Envelope>();

        /// <summary>
        /// Soft cap on cached envelopes. Each is two <c>float[]</c> of <c>frames / BlockFrames</c> entries —
        /// ~220 KB for a 10-second clip, i.e. small managed memory beside the PCM it derives from. The cap is
        /// generous precisely so the drop-the-lot overflow below stays unreachable in practice: thirty-two
        /// distinct clips showing waveforms at once is not a real inspector.
        /// </summary>
        private const int MaxEnvelopeEntries = 32;

        /// <summary>
        /// The channel-averaged sample at per-channel frame <paramref name="frame"/> — the same expression,
        /// evaluated in the same order, as the loop this replaced, so results are bit-identical.
        /// </summary>
        private static float MonoAt(float[] samples, int frame, int channels)
        {
            if (channels == 1) return samples[frame];
            int baseIdx = frame * channels;
            float v = 0f;
            for (int c = 0; c < channels; c++) v += samples[baseIdx + c];
            return v / channels;
        }

        /// <summary>
        /// Returns the block min/max envelope for this clip, building it on first use.
        /// <para>
        /// Deliberately NOT dropped by <see cref="Clear"/>. The texture cache overflows and empties roughly
        /// once a second during a live drag, and rebuilding the envelope on that cadence would hand back the
        /// entire win — the envelope's whole purpose is to outlive the textures derived from it.
        /// </para></summary>
        private static Envelope GetEnvelope(AudioClip clip, float[] samples, int channels, int frames)
        {
            int blockCount = (frames + BlockFrames - 1) / BlockFrames;
            var key = new EnvelopeKey { clipId = UnityObjectId.Of(clip), content = ContentStamp(clip) };

            // The length check is belt-and-braces against a content stamp that collides across two sizes:
            // a short read here would be an IndexOutOfRange in the column loop rather than a wrong pixel.
            if (Envelopes.TryGetValue(key, out var cached) && cached.min != null && cached.min.Length == blockCount)
                return cached;

            var bMin = new float[blockCount];
            var bMax = new float[blockCount];
            for (int b = 0; b < blockCount; b++)
            {
                int s = b * BlockFrames;
                int e = Mathf.Min(s + BlockFrames, frames);
                float mn = float.MaxValue, mx = float.MinValue;
                for (int f = s; f < e; f++)
                {
                    float v = MonoAt(samples, f, channels);
                    if (v < mn) mn = v;
                    if (v > mx) mx = v;
                }
                bMin[b] = mn;
                bMax[b] = mx;
            }

            var env = new Envelope { min = bMin, max = bMax };
            if (Envelopes.Count >= MaxEnvelopeEntries) Envelopes.Clear();
            Envelopes[key] = env;
            return env;
        }

        /// <summary>Drops every cached envelope (reload/quit teardown, and the perf harness's cold-start reset).</summary>
        internal static void ClearEnvelopes() => Envelopes.Clear();

        private static Texture2D Build(AudioClip clip, int width, int height, Color waveColor, Color background)
        {
            // The PCM comes from the SHARED cache the playback path already fills, not a private GetData.
            // A waveform cache miss happens once per pixel WIDTH — i.e. every frame of a live resize — and
            // decoding a 10-second stereo clip is a 3.4 MB allocation plus a full decompress. Paying that
            // per frame was, measurably, the single most expensive thing in the package.
            //
            // Behaviour is unchanged, not merely similar: AudioClipPcm.Load applies the identical
            // DecompressOnLoad pre-check (so a streaming/compressed clip still renders nothing rather than
            // spamming the console), the identical Max(1, channels) / samples arithmetic, and the identical
            // GetData call — so `samples` here is the same float[] content this method used to decode for
            // itself. It is owned by that cache and must only ever be READ from here.
            if (!AudioClipPcm.TryGet(clip, out var samples, out int channels, out _, out int frames))
                return null;

            if (frames <= 0 || channels <= 0 || samples == null) return null;
            // AudioClipPcm always sizes the array frames*channels; clamping once here means the per-sample
            // loops below need no bounds guard of their own, which matters at ~440k samples per rebuild.
            frames = Mathf.Min(frames, samples.Length / channels);
            if (frames <= 0) return null;

            var env = GetEnvelope(clip, samples, channels, frames);

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Grown, never reallocated per build. The texture cache is keyed by pixel WIDTH, so a width drag
            // is a cache miss on every frame by construction (that is deliberate — re-rasterising at the new
            // width is what keeps the waveform sharp), and each miss was discarding a fresh Color32[] of up to
            // width*height. Measured over a 120-frame drag: 11.7 MB of managed garbage.
            //
            // The buffer is only ever read back through the width*height prefix that the loops below fully
            // write, so a surplus tail left over from a wider frame is never sampled.
            int pixelCount = width * height;
            if (s_pixelScratch == null || s_pixelScratch.Length < pixelCount)
                s_pixelScratch = new Color32[pixelCount];
            var pixels = s_pixelScratch;
            Color32 bg32 = background;
            for (int i = 0; i < pixelCount; i++) pixels[i] = bg32;

            Color32 wave32 = waveColor;
            int framesPerColumn = Mathf.Max(1, frames / width); // `frames` is the per-channel count from the PCM cache
            int half = height / 2;

            for (int x = 0; x < width; x++)
            {
                int startFrame = x * framesPerColumn;
                int endFrame = Mathf.Min(startFrame + framesPerColumn, frames);
                float min = 1f, max = -1f;

                // Whole blocks that fall entirely inside [startFrame, endFrame) are read straight off the
                // precomputed envelope; only the ragged ends are scanned sample by sample. min/max is
                // associative, so this yields BIT-IDENTICAL values to scanning every frame — the envelope is
                // a shortcut through the same arithmetic, never an approximation, and
                // TestAudioWaveformBlockEnvelopeMatchesADirectScan pins that pixel for pixel.
                // A column narrower than one block degrades naturally: the head scan covers all of it and
                // both other loops run zero times, which is exactly the old behaviour.
                int firstWholeBlock = (startFrame + BlockFrames - 1) / BlockFrames;
                int endWholeBlock = endFrame / BlockFrames;

                int headEnd = Mathf.Min(firstWholeBlock * BlockFrames, endFrame);
                for (int f = startFrame; f < headEnd; f++)
                {
                    float v = MonoAt(samples, f, channels);
                    if (v < min) min = v;
                    if (v > max) max = v;
                }

                for (int b = firstWholeBlock; b < endWholeBlock; b++)
                {
                    if (env.min[b] < min) min = env.min[b];
                    if (env.max[b] > max) max = env.max[b];
                }

                for (int f = Mathf.Max(endWholeBlock * BlockFrames, headEnd); f < endFrame; f++)
                {
                    float v = MonoAt(samples, f, channels);
                    if (v < min) min = v;
                    if (v > max) max = v;
                }

                if (max < min) { min = 0f; max = 0f; }

                int yMin = Mathf.Clamp(half + Mathf.RoundToInt(min * (half - 1)), 0, height - 1);
                int yMax = Mathf.Clamp(half + Mathf.RoundToInt(max * (half - 1)), 0, height - 1);
                if (yMin > yMax) (yMin, yMax) = (yMax, yMin);

                for (int y = yMin; y <= yMax; y++)
                    pixels[y * width + x] = wave32;
            }

            // The BLOCK overload: the whole-array one demands an array whose length exactly equals the pixel
            // count, which would force a fresh buffer per build and give back the reason the scratch exists.
            // Byte-identical otherwise — TestAudioWaveformBlockEnvelopeMatchesADirectScan compares the result
            // pixel-for-pixel against a verbatim copy of the original algorithm across 7 clip/size cases.
            tex.SetPixels32(0, 0, width, height, pixels, 0);
            tex.Apply(false);
            return tex;
        }
    }
}
#endif // SASQ_AUDIO
