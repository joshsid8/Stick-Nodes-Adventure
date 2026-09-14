#if UNITY_EDITOR_LINUX
using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace SasquatchInspector.Editor.Audio
{
    /// <summary>
    /// Linux fallback audio backend via the PulseAudio Simple API (<c>libpulse-simple</c>). Tried by
    /// <see cref="NativeAudioBackend"/> only when <see cref="AlsaAudioOutput"/> can't make
    /// sound. The motivating case is WSL2/WSLg: there is no sound-card kernel driver (no <c>/dev/snd</c>, usually
    /// no libasound either) and audio reaches Windows EXCLUSIVELY through WSLg's PulseAudio server, which libpulse
    /// finds on its own (WSL sessions export <c>PULSE_SERVER=unix:/mnt/wslg/PulseServer</c>). The same leg covers
    /// pulse-only containers and PipeWire desktops without the ALSA compat layer (pipewire-pulse is ubiquitous).
    ///
    /// STRUCTURE MIRRORS <see cref="AlsaAudioOutput"/> BY DESIGN — same feeder thread, same one-shot + continuous
    /// crossfaded scrub stream, same fades, same threading rule; edit the two files in step. Only the device layer
    /// differs:
    /// • <c>pa_simple_write</c> is blocking and all-or-nothing (no partial writes, no underrun recovery — the
    ///   server plays silence on starvation and the stream stays healthy), so WriteChunk is a single call.
    /// • The device "delay" is <c>pa_simple_get_latency</c> (microseconds → frames).
    /// • Stop discards queued audio with <c>pa_simple_flush</c>; there is no drop/prepare re-arm cycle.
    /// • The server accepts FLOAT32LE from every client and converts internally, so no S16 fallback path.
    /// • A failed write flags the connection dead so the next voice reconnects — a pulse stream is a socket to a
    ///   possibly-remote server (WSLg!) and can die while we sit idle, unlike a local ALSA device.
    ///
    /// THREADING RULE (same as ALSA): the pa_simple handle is touched by at most one thread at a time. The feeder
    /// owns it while it runs (write/get_latency only); the main thread opens, flushes and frees it only after the
    /// feeder has been joined (<see cref="Stop"/> joins first).
    /// </summary>
    internal sealed class PulseAudioOutput : INativeAudioOutput
    {
        // The runtime soname — shipped by the libpulse client package (Debian/Ubuntu: libpulse0), no dev packages.
        private const string Lib = "libpulse-simple.so.0";

        private const string AppName = "Sasquatch Inspector";

        [StructLayout(LayoutKind.Sequential)]
        private struct PaSampleSpec
        {
            public int Format;   // pa_sample_format_t
            public uint Rate;
            public byte Channels;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PaBufferAttr // uint.MaxValue in any field = "server default" to pulse
        {
            public uint MaxLength;
            public uint TLength;
            public uint PreBuf;
            public uint MinReq;
            public uint FragSize; // capture-only; unused for playback
        }

        [DllImport(Lib)] private static extern IntPtr pa_simple_new(string server, string name, int dir, string dev, string streamName, ref PaSampleSpec ss, IntPtr channelMap, ref PaBufferAttr attr, out int error);
        [DllImport(Lib)] private static extern void pa_simple_free(IntPtr s);
        [DllImport(Lib)] private static extern int pa_simple_write(IntPtr s, float[] data, UIntPtr bytes, out int error);
        [DllImport(Lib)] private static extern int pa_simple_flush(IntPtr s, out int error);
        [DllImport(Lib)] private static extern ulong pa_simple_get_latency(IntPtr s, out int error);

        private const int PA_STREAM_PLAYBACK = 1;
        private const int PA_SAMPLE_FLOAT32LE = 5;

        // ~60 ms server-side buffer target — matches the ALSA/Windows queue depth so scrub feel is consistent.
        private const uint RequestedLatencyUs = 60_000;

        // Device state (owned by the main thread; mutated only while no feeder thread is alive)
        private IntPtr _handle = IntPtr.Zero;
        private int _rate;
        private int _channels;
        private int _ringFrames;   // pa_simple can't report the granted buffer, so this is the requested ring
        private int _chunkFrames;  // ~20 ms per feeder write
        private int _connectionDead; // 1 → a write failed (server/socket gone); EnsureDevice reconnects (Interlocked)

        // Availability probe, cached per backend instance (i.e. per domain load — Shutdown recreates the backend)
        private bool _probed;
        private bool _probeOk;

        // Source PCM (managed; the feeder reads it directly — a managed thread needs no pinning)
        private float[] _source;
        private int _totalFrames;
        private int _startFrame;

        // Staging buffer, reused every write (allocated on device open; zero allocation per chunk)
        private float[] _mix;

        // Feeder thread + cross-thread signals
        private Thread _feeder;
        private int _stopFlag;        // 1 → feeder exits at the next chunk boundary (Interlocked)
        private int _seekTo = -1;     // scrub seek target, published main → feeder (Interlocked)
        private int _positionFrames;  // one-shot playhead, published feeder → main (Volatile)
        private int _voiceDone;       // 1 → one-shot voice fully drained or failed (Volatile)

        // Main-thread mode flags (same pattern as the other backends)
        private bool _playing;
        private bool _scrubMode;

        public PulseAudioOutput()
        {
            // Touch the library now: a system without libpulse throws DllNotFoundException here, which the backend
            // chooser catches → next candidate. The deliberately INVALID sample spec (rate 0) makes pa_simple_new
            // fail fast with PA_ERR_INVALID before it attempts any server connection.
            var invalid = new PaSampleSpec { Format = PA_SAMPLE_FLOAT32LE, Rate = 0, Channels = 0 };
            var attr = DefaultAttr(0);
            var p = pa_simple_new(null, AppName, PA_STREAM_PLAYBACK, null, "probe", ref invalid, IntPtr.Zero, ref attr, out _);
            if (p != IntPtr.Zero) pa_simple_free(p); // unreachable by design; belt-and-braces against a leak
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

        // A genuine connect is the only reliable test (libpulse can be installed with no server to talk to —
        // headless/CI boxes, plain ssh sessions). Connect + free; the playback stream then opens lazily on Play.
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

                // Natural end: keep the playhead moving while the last ~ring of queued audio drains server-side.
                while (Volatile.Read(ref _stopFlag) == 0)
                {
                    if (LatencyFrames(h) <= 0) break;
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

        // Frames still queued between us and the speaker (server buffer + sink latency); -1 on error.
        private long LatencyFrames(IntPtr h)
        {
            ulong usec = pa_simple_get_latency(h, out int err);
            if (err != 0 || usec == ulong.MaxValue) return -1;
            return (long)(usec * (ulong)_rate / 1_000_000UL);
        }

        // Playhead = frames handed to the server minus what still sits in its pipeline (clamped on transient errors).
        private void PublishPosition(IntPtr h, int written)
        {
            long d = LatencyFrames(h);
            if (d < 0) return; // keep the last known value
            long p = written - d;
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

        // How far a scrub may play past the last seek before going silent — above the server buffer (so continuous
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
                pa_simple_flush(_handle, out _); // discard whatever is still queued server-side (instant silence)
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
            Volatile.Write(ref _positionFrames, 0);
            Volatile.Write(ref _voiceDone, 0);
            Interlocked.Exchange(ref _stopFlag, 0);
            Interlocked.Exchange(ref _seekTo, -1);

            _feeder = new Thread(loop)
            {
                IsBackground = true, // never blocks editor shutdown
                Name = "SasqPulseAudioFeeder",
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
            // pa_simple_write blocks at most ~one server buffer (~60 ms) and the drain poll wakes every 5 ms, so
            // this join is quick. If a dead server wedges the thread, LEAK the handle rather than free it under
            // the feeder (the feeder captured its own copy; an orphaned connection is harmless, use-after-free is not).
            if (!t.Join(1000)) _handle = IntPtr.Zero;
        }

        // Writes the first <paramref name="frames"/> frames of _mix to the server. pa_simple_write blocks until the
        // whole chunk is accepted (self-pacing against the server buffer) — no partial writes, and playback underruns
        // don't error (the server plays silence and the stream stays healthy). False = voice-fatal: the connection is
        // gone, so flag it dead for the next EnsureDevice to reconnect.
        private bool WriteChunk(IntPtr h, int frames)
        {
            if (Volatile.Read(ref _stopFlag) != 0) return true; // Stop wins over finishing the chunk
            if (pa_simple_write(h, _mix, new UIntPtr((uint)(frames * _channels * sizeof(float))), out _) >= 0) return true;
            Interlocked.Exchange(ref _connectionDead, 1);
            return false;
        }

        private bool EnsureDevice(int rate, int channels)
        {
            if (_handle != IntPtr.Zero && _rate == rate && _channels == channels
                && Volatile.Read(ref _connectionDead) == 0) return true;
            CloseDevice();

            var ss = new PaSampleSpec { Format = PA_SAMPLE_FLOAT32LE, Rate = (uint)rate, Channels = (byte)channels };
            int ringFrames = Mathf.RoundToInt(rate * (RequestedLatencyUs / 1_000_000f));
            var attr = DefaultAttr((uint)(ringFrames * channels * sizeof(float)));
            // null server → libpulse's own lookup (PULSE_SERVER env, X11 prop, per-user socket); null dev → default sink.
            var h = pa_simple_new(null, AppName, PA_STREAM_PLAYBACK, null, "Audio Preview", ref ss, IntPtr.Zero, ref attr, out _);
            if (h == IntPtr.Zero) return false;

            _handle = h;
            _rate = rate;
            _channels = channels;
            _chunkFrames = Mathf.Max(256, rate / 50); // ~20 ms per feeder write
            _ringFrames = ringFrames;
            _mix = new float[_chunkFrames * channels];
            Interlocked.Exchange(ref _connectionDead, 0);
            return true;
        }

        // tlength = our requested ring (0 → default); everything else stays on the server default.
        private static PaBufferAttr DefaultAttr(uint tlengthBytes) => new PaBufferAttr
        {
            MaxLength = uint.MaxValue,
            TLength = tlengthBytes == 0 ? uint.MaxValue : tlengthBytes,
            PreBuf = uint.MaxValue,
            MinReq = uint.MaxValue,
            FragSize = uint.MaxValue,
        };

        private void CloseDevice()
        {
            if (_handle != IntPtr.Zero)
            {
                pa_simple_free(_handle); // frees the stream and drops the server connection; queued audio is discarded
                _handle = IntPtr.Zero;
            }
            _playing = false;
        }
    }
}
#endif
