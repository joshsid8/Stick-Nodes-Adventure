#if SASQ_AUDIO // AudioClip lives in com.unity.modules.audio, a REMOVABLE built-in module.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Provides the Audio Preview demo's clip. The shipped clip is a real, CC0 (public-domain) explosion —
    /// see the Audio section of <c>Third-Party Notices.txt</c> at the package root for its source and licence —
    /// imported as a normal <c>.wav</c> asset so it plays through the editor preview reliably (a runtime
    /// <c>AudioClip.Create</c> clip does not). If the asset cannot be found, <see cref="BuildExplosionWav"/>
    /// synthesizes a fallback explosion (a noise burst + low boom under an exponential decay) so the demo still
    /// works.
    /// </summary>
    internal static class ShowcaseAudio
    {
        private const string ClipName = "ShowcaseExplosion";

        /// <summary>The explosion AudioClip asset, synthesizing + importing one only if it cannot be found.</summary>
        public static AudioClip GetExplosionClip()
        {
            // Resolve by ASSET NAME first, never by a hardcoded path. SasquatchPaths' whole contract is that the
            // package keeps working when its folder is moved, renamed, or installed read-only — and a buyer
            // relocating a vendor folder under Assets/Plugins/ is routine. Looking the shipped clip up by path
            // would miss it on any relocated install and fall through to the synthesize branch below, leaving a
            // duplicate clip behind.
            foreach (var guid in AssetDatabase.FindAssets($"{ClipName} t:AudioClip"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(p) || !p.EndsWith("/" + ClipName + ".wav")) continue;
                var found = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                if (found != null) return found;
            }

            // Not found (the buyer deleted it, or the Showcase folder was trimmed): synthesize one INSIDE
            // wherever the package actually lives, so we never create files outside it.
            string folder = SasquatchPaths.PackageRoot + "/Editor/Showcase/Assets";
            if (!AssetDatabase.IsValidFolder(folder)) folder = SasquatchPaths.GeneratedDirectory;
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return null;

            string clipPath = folder + "/" + ClipName + ".wav";
            File.WriteAllBytes(Path.GetFullPath(clipPath), BuildExplosionWav());
            AssetDatabase.ImportAsset(clipPath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
        }

        // 16-bit PCM mono WAV: a sharp initial crack, a body of low-passed (rumbling) decaying noise, and a
        // descending low "boom", all under an exponential decay envelope — a recognizable explosion.
        private static byte[] BuildExplosionWav()
        {
            const int rate = 44100;
            const float dur = 1.6f;
            int n = (int)(rate * dur);
            var pcm = new short[n];

            // Deterministic noise via a tiny LCG so the asset is byte-reproducible (no Random.value).
            uint seed = 0x1234567u;
            float Noise()
            {
                seed = seed * 1664525u + 1013904223u;
                return (seed >> 8) / (float)(1 << 24) * 2f - 1f; // [-1, 1)
            }

            float lp = 0f; // one-pole low-pass state → softens the noise into a rumble
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Exp(-3.2f * t);                       // overall decay
                float crack = Mathf.Exp(-45f * t);                      // sharp initial transient
                float boomFreq = 90f - 45f * Mathf.Clamp01(t);          // descending low boom
                float boom = Mathf.Sin(2f * Mathf.PI * boomFreq * t);

                float white = Noise();
                lp += 0.08f * (white - lp);
                float body = 0.7f * lp + 0.3f * white;

                float s = env * (0.85f * body + 0.45f * boom) + 0.9f * crack * white;
                s = Mathf.Clamp(s, -1f, 1f) * 0.85f;
                pcm[i] = (short)Mathf.RoundToInt(s * short.MaxValue);
            }

            return WrapWav(pcm, rate, channels: 1);
        }

        // Wraps 16-bit PCM samples in a canonical 44-byte WAV header (BinaryWriter is little-endian, as WAV requires).
        private static byte[] WrapWav(short[] samples, int sampleRate, int channels)
        {
            const int bytesPerSample = 2;
            int dataBytes = samples.Length * bytesPerSample;

            using var ms = new MemoryStream(44 + dataBytes);
            using var w = new BinaryWriter(ms);

            void Tag(string s) { foreach (char c in s) w.Write((byte)c); }

            Tag("RIFF"); w.Write(36 + dataBytes); Tag("WAVE");
            Tag("fmt "); w.Write(16); w.Write((short)1); w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * bytesPerSample); // byte rate
            w.Write((short)(channels * bytesPerSample));     // block align
            w.Write((short)(bytesPerSample * 8));            // bits per sample
            Tag("data"); w.Write(dataBytes);
            foreach (var s in samples) w.Write(s);

            w.Flush();
            return ms.ToArray();
        }
    }
}
#endif // SASQ_AUDIO
