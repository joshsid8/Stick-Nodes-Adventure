#if UNITY_EDITOR_LINUX
using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace SasquatchInspector.Editor.Audio
{
    /// <summary>
    /// Linux native audio backend via ALSA (<c>libasound</c>).
    ///
    /// ALSA is the one audio interface present on effectively every Linux desktop: PipeWire and PulseAudio both
    /// ship ALSA compatibility layers that own the "default" PCM, and bare ALSA falls back to dmix — so this one
    /// backend covers all three sound stacks. Unlike winmm/AudioQueue there is no OS-driven buffer callback;
    /// instead a single managed FEEDER THREAD pushes ~20 ms chunks through the blocking <c>snd_pcm_writei</c>,
    /// which self-paces against the device ring buffer. Every P/Invoke is FORWARD-only (managed → native, never
    /// native → managed), so like the Windows backend this is CoreCLR-tolerant and needs no Mono gate.
    ///
    /// Two playback modes share one open device:
    /// • <see cref="Play"/> — the feeder streams the clip once off its own thread (so playback survives a busy
    ///   main thread, like the other backends' device clocks); the playhead is published as frames-written minus
    ///   the device delay (<c>snd_pcm_delay</c>) through a volatile field the main thread polls.
    /// • <see cref="BeginScrub"/>/<see cref="ScrubSeek"/> — the same CONTINUOUS crossfaded scrub stream as
    ///   Windows/macOS: a seek just publishes a new read position, each seam is equal-power-crossfaded, the
    ///   stream fades in from / out to silence at the edges, and past the play ceiling it feeds silence so
    ///   seeking back is instant. Seam mixing runs on the feeder thread; ScrubSeek is an Interlocked publish.
    ///
    /// THREADING RULE: the PCM handle is touched by at most one thread at a time (libasound handles are not
    /// thread-safe). The feeder owns the handle while it runs (writei/delay/recover only); the main thread opens,
    /// re-arms, drops and closes it only after the feeder has been joined (<see cref="Stop"/> joins first).
    ///
    /// NOT YET VERIFIED ON A REAL LINUX DESKTOP (WSL2 has no ALSA, so it cannot be exercised here). Three things
    /// to check when it can be: the <c>"default"</c> PCM opens under PipeWire / PulseAudio / dmix; the
    /// FLOAT_LE → S16_LE format fallback engages cleanly; and the scrub latency feels right (~60 ms ring is
    /// requested, but <c>snd_pcm_get_params</c> reports what the device actually granted).
    /// </summary>
    internal sealed class AlsaAudioOutput : INativeAudioOutput
    {
        // The runtime soname — present on every ALSA userspace install; the unversioned .so needs dev packages.
        private const string Lib = "libasound.so.2";

        [DllImport(Lib)] private static extern IntPtr snd_asoundlib_version();
        [DllImport(Lib)] private static extern int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);
        [DllImport(Lib)] private static extern int snd_pcm_close(IntPtr pcm);
        [DllImport(Lib)] private static extern int snd_pcm_set_params(IntPtr pcm, int format, int access, uint channels, uint rate, int softResample, uint latencyUs);
        [DllImport(Lib)] private static extern int snd_pcm_get_params(IntPtr pcm, out ulong bufferFrames, out ulong periodFrames);
        [DllImport(Lib)] private static extern int snd_pcm_prepare(IntPtr pcm);
        [DllImport(Lib)] private static extern int snd_pcm_drop(IntPtr pcm);
        [DllImport(Lib)] private static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);
        [DllImport(Lib)] private static extern int snd_pcm_delay(IntPtr pcm, out long delayFrames);
        // Same entry point, one overload per staging format (the marshaller pins the array for the call only).
        [DllImport(Lib, EntryPoint = "snd_pcm_writei")] private static extern long snd_pcm_writei_float(IntPtr pcm, float[] buffer, ulong frames);
        [DllImport(Lib, EntryPoint = "snd_pcm_writei")] private static extern long snd_pcm_writei_short(IntPtr pcm, short[] buffer, ulong frames);

        private const int SND_PCM_STREAM_PLAYBACK = 0;
        private const int SND_PCM_ACCESS_RW_INTERLEAVED = 3;
        private const int SND_PCM_FORMAT_S16_LE = 2;
        private const int SND_PCM_FORMAT_FLOAT_LE = 14;

        // ~60 ms device ring — matches the Windows queue depth, so scrub audio trails the cursor by about the same.
        private const uint RequestedLatencyUs = 60_000;

        // "default" routes through PipeWire/PulseAudio when present, dmix otherwise; plughw is the bare-metal
        // fallback for setups whose default route points at a sound server that is configured but not running
        // (plug, unlike raw hw, converts rate/format so any clip still plays).
        private static readonly string[] DeviceCandidates = { "default", "plughw:0,0" };

        // Device state (owned by the main thread; mutated only while no feeder thread is alive)
        private IntPtr _handle = IntPtr.Zero;
        private int _rate;
        private int _channels;
        private bool _deviceFloat;          // opened FLOAT_LE (else staging converts to S16_LE)
        private bool _deviceRejectsFloat;   // sticky across reopens, like the Windows PCM16 fallback
        private int _ringFrames;            // the ring the device actually granted (snd_pcm_get_params)
        private int _chunkFrames;           // ~20 ms per feeder write

        // Availability probe, cached per backend instance (i.e. per domain load — Shutdown recreates the backend)
        private bool _probed;
        private bool _probeOk;

        // Source PCM (managed; the feeder reads it directly — a managed thread needs no pinning)
        private float[] _source;
        private int _totalFrames;
        private int _startFrame;

        // Staging buffers, reused every write (allocated on device open; zero allocation per chunk)
        private float[] _mix;
        private short[] _mix16;

        // Feeder thread + cross-thread signals
        private Thread _feeder;
        private int _stopFlag;        // 1 → feeder exits at the next chunk boundary (Interlocked)
        private int _seekTo = -1;     // scrub seek target, published main → feeder (Interlocked)
        private int _positionFrames;  // one-shot playhead, published feeder → main (Volatile)
        private int _voiceDone;       // 1 → one-shot voice fully drained or failed (Volatile)

        // Main-thread mode flags (same pattern as the Windows backend)
        private bool _playing;
        private bool _scrubMode;

        public AlsaAudioOutput()
        {
            // Touch the library now: a system without libasound throws DllNotFoundException here, which
            // NativeAudioBackend.Create catches → NullAudioOutput. (Every desktop distro ships libasound.so.2.)
            snd_asoundlib_version();
        }

        public bool IsAvailable
        {
            get
            {
                if (_handle != IntPtr.Zero) return true;
                if (!_probed)
                {
                    _probed = true;
                    _probeOk = ProbeDevice();
                }
                return _probeOk;
            }
        }

        public bool SupportsScrubbing => true;

        // A genuine open is the only reliable device test on Linux (headless/CI boxes have libasound but no
        // sink). Open + configure + close; the playback device then opens lazily on the first Play.
        private bool ProbeDevice()
        {
            try
            {
                if (!EnsureDevice(48000, 2)) return false;
                CloseDevice();
                return true;
            }
            catch { return false; }
        }

        // ---------------- one-shot playback ----------------

        public void Play(float[] pcm, int channels, int sampleRate, int startFrame, int totalFrames)
        {
            Stop();
            if (pcm == null || channels <= 0 || sampleRate <= 0 || totalFrames <= 0) return;
            if (startFrame < 0) startFrame = 0;
            if (startFrame >= totalFrames) return;
            if (!EnsureDevice(sampleRate, channels)) return;

            _source = pcm;
            _totalFrames = totalFrames;
            _startFrame = startFrame;

            StartFeeder(PlayLoop, scrub: false);
        }

        public bool IsPlaying
        {
            get
            {
                if (_scrubMode) return _playing; // continuous while scrubbing
                if (!_playing) return false;
                if (Volatile.Read(ref _voiceDone) != 0) { _playing = false; return false; }
                return true;
            }
        }

        public int PositionFrames => _scrubMode ? 0 : Volatile.Read(ref _positionFrames); // scrub drives its playhead from the cursor

        private void PlayLoop()
        {
            IntPtr h = _handle;
            try
            {
                float[] src = _source;
                int channels = _channels;
                int total = _totalFrames;
                int pos = _startFrame;
                int written = 0;

                while (Volatile.Read(ref _stopFlag) == 0 && pos < total)
                {
                    int n = Math.Min(_chunkFrames, total - pos);
                    Array.Copy(src, pos * channels, _mix, 0, n * channels);
                    if (!WriteChunk(h, n)) { Volatile.Write(ref _voiceDone, 1); return; }
                    pos += n;
                    written += n;
                    PublishPosition(h, written);
                }

                // Natural end: keep the playhead moving while the last ~ring of queued audio drains.
                while (Volatile.Read(ref _stopFlag) == 0)
                {
                    if (snd_pcm_delay(h, out long d) < 0 || d <= 0) break;
                    PublishPosition(h, written);
                    Thread.Sleep(5);
                }
                Volatile.Write(ref _positionFrames, written);
                Volatile.Write(ref _voiceDone, 1);
            }
            catch
            {
                // The feeder must die silently — a background-thread error may never surface to the user.
                Volatile.Write(ref _voiceDone, 1);
            }
        }

        // Playhead = frames handed to the device minus what still sits in its ring (clamped on transient errors).
        private void PublishPosition(IntPtr h, int written)
        {
            if (snd_pcm_delay(h, out long d) < 0) return; // keep the last known value
            long p = written - (d > 0 ? d : 0);
            if (p < 0) p = 0;
            Volatile.Write(ref _positionFrames, (int)Math.Min(p, int.MaxValue));
        }

        // ---------------- continuous scrub stream ----------------

        public void BeginScrub(float[] pcm, int channels, int sampleRate, int startFrame, int totalFrames)
        {
            Stop();
            if (pcm == null || channels <= 0 || sampleRate <= 0 || totalFrames <= 0) return;
            if (!EnsureDevice(sampleRate, channels)) return;

            _source = pcm;
            _totalFrames = totalFrames;
            _startFrame = Mathf.Clamp(startFrame, 0, totalFrames);

            StartFeeder(ScrubLoop, scrub: true);
        }

        public void ScrubSeek(int frame)
        {
            if (!_scrubMode || !_playing) return;
            Interlocked.Exchange(ref _seekTo, Mathf.Clamp(frame, 0, _totalFrames));
        }

        private void ScrubLoop()
        {
            IntPtr h = _handle;
            try
            {
                int channels = _channels;
                int total = _totalFrames;
                int readFrame = _startFrame;
                int lastFillEnd = readFrame;   // frame the previous fill ended on — detects a seek jump vs. a contiguous advance
                bool fadeInNext = true;        // ease the first buffer in from silence so starting mid-waveform never pops
                int playCeil = readFrame + ScrubRunFrames();

                while (Volatile.Read(ref _stopFlag) == 0)
                {
                    int seek = Interlocked.Exchange(ref _seekTo, -1);
                    bool jumped = false;
                    if (seek >= 0)
                    {
                        // A jump from the contiguous play position is what clicks at the seam; flag it so the head
                        // below crossfades. A seek landing where we already are, or right after silence, isn't a jump.
                        jumped = !fadeInNext && seek != lastFillEnd;
                        readFrame = seek;
                        playCeil = seek + ScrubRunFrames(); // a move re-opens the play window from the new position
                    }

                    // Never play past the ceiling: while the mouse moves, each seek pushes it forward so audio flows
                    // continuously; once the mouse stops the cursor reaches it and we go silent (this is what makes
                    // scrubbing stop when you stop, instead of running on through the rest of the clip).
                    int limit = Math.Min(total, playCeil);
                    if (readFrame >= limit)
                    {
                        Array.Clear(_mix, 0, _chunkFrames * channels); // silence keeps the stream alive (instant seek-back)
                        fadeInNext = true;                             // a later move into audio fades in from this silence
                        if (!WriteChunk(h, _chunkFrames)) return;
                        continue;
                    }

                    int n = Math.Min(_chunkFrames, limit - readFrame);

                    // Build this chunk's HEAD: fade in from silence after a (re)start, equal-power crossfade across a
                    // seek seam, straight copy on a contiguous advance.
                    int head = fadeInNext ? Math.Min(n, FadeInFrames())
                             : jumped ? Math.Min(n, CrossfadeFrames())
                             : 0;
                    for (int k = 0; k < head; k++)
                    {
                        float t = (k + 0.5f) / head;
                        for (int c = 0; c < channels; c++)
                        {
                            float v;
                            if (fadeInNext)
                            {
                                v = SrcSample(readFrame + k, c) * t; // ramp up from zero
                            }
                            else // equal-power: old fades out (cos), new fades in (sin) → constant perceived loudness
                            {
                                float aGain = Mathf.Cos(t * 0.5f * Mathf.PI);
                                float bGain = Mathf.Sin(t * 0.5f * Mathf.PI);
                                v = SrcSample(lastFillEnd + k, c) * aGain + SrcSample(readFrame + k, c) * bGain;
                            }
                            _mix[k * channels + c] = v;
                        }
                    }

                    if (n > head) // body: straight copy of the new position
                        Array.Copy(_source, (readFrame + head) * channels, _mix, head * channels, (n - head) * channels);

                    // Reached the ceiling (mouse stopped) or the clip end → fade the tail to silence so going quiet
                    // doesn't click; arm a fade-in so the next move eases back in.
                    bool reachedLimit = readFrame + n >= limit;
                    if (reachedLimit)
                    {
                        int fo = Math.Min(FadeOutFrames(), n - head);
                        for (int k = 0; k < fo; k++)
                        {
                            float g = 1f - (k + 0.5f) / fo; // 1 → 0
                            for (int c = 0; c < channels; c++)
                                _mix[(n - fo + k) * channels + c] = SrcSample(readFrame + n - fo + k, c) * g;
                        }
                    }

                    if (!WriteChunk(h, n)) return;
                    lastFillEnd = readFrame + n;
                    readFrame += n;
                    fadeInNext = reachedLimit;
                }
            }
            catch
            {
                // The feeder must die silently — a background-thread error may never surface to the user.
            }
        }

        // ~6 ms crossfade across a seek seam; ~2.5 ms fade-in / ~5 ms fade-out at the silence edges.
        private int CrossfadeFrames() => Mathf.Clamp(Mathf.RoundToInt(_rate * 0.006f), 64, _chunkFrames / 2);
        private int FadeInFrames() => Mathf.Clamp(Mathf.RoundToInt(_rate * 0.0025f), 32, _chunkFrames / 2);
        private int FadeOutFrames() => Mathf.Clamp(Mathf.RoundToInt(_rate * 0.005f), 32, _chunkFrames / 2);

        // How far a scrub may play past the last seek before going silent — above the device ring (so continuous
        // dragging never gaps) but bounded (~90 ms floor) so the sound stops promptly once the mouse stops.
        private int ScrubRunFrames() => Mathf.Max(Mathf.RoundToInt(_rate * 0.09f), _ringFrames + 2 * _chunkFrames);

        // One interleaved sample from the managed source PCM (range [-1,1]), clamped to the clip's bounds.
        private float SrcSample(int frame, int ch)
        {
            if (frame < 0) frame = 0;
            else if (frame >= _totalFrames) frame = _totalFrames - 1;
            return _source[frame * _channels + ch];
        }

        // ---------------- shared ----------------

        public void Stop()
        {
            Interlocked.Exchange(ref _stopFlag, 1);
            JoinFeeder();
            if (_handle != IntPtr.Zero)
            {
                snd_pcm_drop(_handle);    // discard whatever is still queued in the ring (instant silence)
                snd_pcm_prepare(_handle); // re-arm so the device stays open and ready for the next voice
            }
            _playing = false;
            _scrubMode = false;
        }

        public void Dispose()
        {
            Stop();
            CloseDevice();
        }

        private void StartFeeder(ThreadStart loop, bool scrub)
        {
            _scrubMode = scrub;
            snd_pcm_prepare(_handle); // recover from a previous natural-end underrun (harmless when already prepared)
            Volatile.Write(ref _positionFrames, 0);
            Volatile.Write(ref _voiceDone, 0);
            Interlocked.Exchange(ref _stopFlag, 0);
            Interlocked.Exchange(ref _seekTo, -1);

            _feeder = new Thread(loop)
            {
                IsBackground = true, // never blocks editor shutdown
                Name = "SasqAlsaAudioFeeder",
                Priority = System.Threading.ThreadPriority.AboveNormal, // 20 ms cadence must survive editor load spikes
            };
            _feeder.Start();
            _playing = true;
        }

        private void JoinFeeder()
        {
            var t = _feeder;
            _feeder = null;
            if (t == null || !t.IsAlive) return;
            // writei blocks at most ~one ring (~60 ms) and the drain poll wakes every 5 ms, so this join is quick.
            // If a dying device somehow wedges the thread, LEAK the handle rather than close it under the feeder
            // (the feeder captured its own copy; an orphaned open device is harmless, a use-after-close is not).
            if (!t.Join(1000)) _handle = IntPtr.Zero;
        }

        // Writes the first <paramref name="frames"/> frames of _mix to the device (converting to S16 when the
        // device rejected float), handling partial writes and underrun/suspend recovery. False = voice-fatal.
        private bool WriteChunk(IntPtr h, int frames)
        {
            int channels = _channels;
            if (!_deviceFloat)
            {
                int count = frames * channels;
                for (int i = 0; i < count; i++)
                    _mix16[i] = (short)(int)(Mathf.Clamp(_mix[i], -1f, 1f) * 32767f);
            }

            int remaining = frames;
            while (remaining > 0)
            {
                if (Volatile.Read(ref _stopFlag) != 0) return true; // Stop wins over finishing the chunk
                long w = _deviceFloat
                    ? snd_pcm_writei_float(h, _mix, (ulong)remaining)
                    : snd_pcm_writei_short(h, _mix16, (ulong)remaining);
                if (w < 0)
                {
                    // -EPIPE (underrun) / -ESTRPIPE (suspend) are recoverable; anything else ends the voice.
                    if (snd_pcm_recover(h, (int)w, 1) < 0) return false;
                    continue;
                }
                remaining -= (int)w;
                if (remaining > 0 && w > 0)
                {
                    // Rare partial write (interrupted syscall): shift the remainder to the front and continue.
                    if (_deviceFloat) Array.Copy(_mix, (int)w * channels, _mix, 0, remaining * channels);
                    else Array.Copy(_mix16, (int)w * channels, _mix16, 0, remaining * channels);
                }
            }
            return true;
        }

        private bool EnsureDevice(int rate, int channels)
        {
            if (_handle != IntPtr.Zero && _rate == rate && _channels == channels) return true;
            CloseDevice();

            foreach (var name in DeviceCandidates)
            {
                if (snd_pcm_open(out var h, name, SND_PCM_STREAM_PLAYBACK, 0) < 0) continue;

                int fmt = _deviceRejectsFloat ? SND_PCM_FORMAT_S16_LE : SND_PCM_FORMAT_FLOAT_LE;
                // set_params negotiates hw+sw params in one call (soft_resample on) and leaves the device PREPARED.
                if (snd_pcm_set_params(h, fmt, SND_PCM_ACCESS_RW_INTERLEAVED, (uint)channels, (uint)rate, 1, RequestedLatencyUs) < 0)
                {
                    if (fmt == SND_PCM_FORMAT_FLOAT_LE
                        && snd_pcm_set_params(h, SND_PCM_FORMAT_S16_LE, SND_PCM_ACCESS_RW_INTERLEAVED, (uint)channels, (uint)rate, 1, RequestedLatencyUs) >= 0)
                    {
                        _deviceRejectsFloat = true;
                        fmt = SND_PCM_FORMAT_S16_LE;
                    }
                    else
                    {
                        snd_pcm_close(h);
                        continue;
                    }
                }

                _handle = h;
                _rate = rate;
                _channels = channels;
                _deviceFloat = fmt == SND_PCM_FORMAT_FLOAT_LE;
                _chunkFrames = Mathf.Max(256, rate / 50); // ~20 ms per feeder write
                _ringFrames = snd_pcm_get_params(h, out var granted, out _) >= 0 && granted > 0
                    ? (int)Math.Min(granted, int.MaxValue)
                    : Mathf.RoundToInt(rate * (RequestedLatencyUs / 1_000_000f));
                _mix = new float[_chunkFrames * channels];
                _mix16 = _deviceFloat ? null : new short[_chunkFrames * channels];
                return true;
            }
            return false;
        }

        private void CloseDevice()
        {
            if (_handle != IntPtr.Zero)
            {
                snd_pcm_close(_handle);
                _handle = IntPtr.Zero;
            }
            _playing = false;
        }
    }
}
#endif
