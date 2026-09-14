#if UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor.Audio
{
    /// <summary>
    /// Windows native audio backend via <c>winmm</c> waveOut.
    ///
    /// Two playback modes share one open device:
    /// • <see cref="Play"/> — a single one-shot buffer holding the whole clip. Plays autonomously off the device
    ///   clock (survives the main thread being busy / the editor losing focus); polled position; PCM16 fallback.
    /// • <see cref="BeginScrub"/>/<see cref="ScrubSeek"/> — a CONTINUOUS stream of small buffers fed from a movable
    ///   read cursor, pumped on EditorApplication.update. Seeking just moves the cursor, so the audio flows
    ///   smoothly and follows the playhead with no per-move device restart — restarting the device on every
    ///   move (a stop+replay scrub) is what makes the sound choppy.
    ///
    /// Memory: the whole clip is converted ONCE into a cached unmanaged buffer (<see cref="Marshal.AllocHGlobal"/>)
    /// reused by both modes; a scrub copies ~20 ms chunks out of it. No managed array pinning, no per-move realloc.
    /// </summary>
    internal sealed class WaveOutAudioOutput : INativeAudioOutput
    {
        // ---- winmm / kernel32 P/Invoke ----
        [DllImport("winmm.dll")] private static extern uint waveOutGetNumDevs();
        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr hWaveOut, uint uDeviceID, ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
        [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr hWaveOut);
        [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpHdr, uint uSize);
        [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpHdr, uint uSize);
        [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr hWaveOut, IntPtr lpHdr, uint uSize);
        [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr hWaveOut);
        [DllImport("winmm.dll")] private static extern uint waveOutGetPosition(IntPtr hWaveOut, ref MMTIME lpInfo, uint uSize);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] private static extern void MoveMemory(IntPtr dest, IntPtr src, UIntPtr length);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MMTIME
        {
            public uint wType;
            public uint val;
            public uint pad;
        }

        private const uint WAVE_MAPPER = 0xFFFFFFFF;
        private const uint CALLBACK_NULL = 0;
        private const ushort WAVE_FORMAT_PCM = 1;
        private const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
        private const uint MMSYSERR_NOERROR = 0;
        private const uint WAVERR_BADFORMAT = 32;
        private const uint TIME_SAMPLES = 0x0002;
        private const uint TIME_BYTES = 0x0004;
        private const uint WHDR_DONE = 0x00000001;

        private static readonly uint HdrSize = (uint)Marshal.SizeOf(typeof(WAVEHDR));
        private static readonly uint MmTimeSize = (uint)Marshal.SizeOf(typeof(MMTIME));
        private static readonly int DwFlagsOffset = Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags").ToInt32();

        private const int ScrubBufferCount = 3; // ~3 × ~20 ms ≈ 60 ms queue (audio trails the cursor by ~that, smoothly)

        // Device state
        private IntPtr _hWaveOut = IntPtr.Zero;
        private int _devRate;
        private int _devChannels;
        private ushort _devFormatTag;
        private int _blockAlign;
        private bool _deviceRejectsFloat;

        // Cached whole-clip buffer (device format), shared by both modes
        private IntPtr _clipBuf = IntPtr.Zero;
        private float[] _clipBufSource;
        private int _clipBufChannels;
        private ushort _clipBufFormatTag;

        // One-shot voice
        private readonly IntPtr _hdrPtr;
        private bool _hdrPrepared;
        private bool _playing;
        private int _voiceFrames;
        private int _posBaseFrames;

        // Scrub stream
        private bool _scrubbing;
        private bool _pumpRegistered;
        private IntPtr[] _scrubHdrs;
        private IntPtr[] _scrubBufs;
        private bool[] _scrubPrepared;
        private IntPtr _silenceBuf = IntPtr.Zero;
        private int _scrubChunkFrames;
        private int _scrubReadFrame;
        private int _scrubTotalFrames;
        private int _lastFillEndFrame;   // frame the previous fill ended on — lets us detect a seek jump vs. a contiguous advance
        private bool _scrubFadeInNext;   // next audible fill should fade in from silence (scrub start / seek-back from past-end)
        private float[] _scrubMix;       // reusable scratch for crossfade / fade-in / fade-out edges (avoids per-fill GC)
        private int _scrubPlayCeil;      // the cursor may not play past this — caps how far a scrub coasts after the mouse stops

        public WaveOutAudioOutput()
        {
            _hdrPtr = Marshal.AllocHGlobal((int)HdrSize);
        }

        public bool IsAvailable => waveOutGetNumDevs() > 0;
        public bool SupportsScrubbing => true;

        // ---------------- one-shot playback ----------------

        public void Play(float[] pcm, int channels, int sampleRate, int startFrame, int totalFrames)
        {
            Stop();
            if (pcm == null || channels <= 0 || sampleRate <= 0 || totalFrames <= 0) return;
            if (startFrame < 0) startFrame = 0;
            if (startFrame >= totalFrames) return;

            if (!EnsureDevice(sampleRate, channels)) return;
            EnsureClipBuffer(pcm, channels, totalFrames);
            if (_clipBuf == IntPtr.Zero) return;

            int lengthBytes = (totalFrames - startFrame) * _blockAlign;
            if (lengthBytes <= 0) return;

            var hdr = new WAVEHDR
            {
                lpData = new IntPtr(_clipBuf.ToInt64() + (long)startFrame * _blockAlign),
                dwBufferLength = (uint)lengthBytes,
            };
            Marshal.StructureToPtr(hdr, _hdrPtr, false);

            if (waveOutPrepareHeader(_hWaveOut, _hdrPtr, HdrSize) != MMSYSERR_NOERROR) return;
            _hdrPrepared = true;

            _voiceFrames = totalFrames - startFrame;
            _posBaseFrames = ReadRawPositionFrames();

            if (waveOutWrite(_hWaveOut, _hdrPtr, HdrSize) != MMSYSERR_NOERROR)
            {
                waveOutUnprepareHeader(_hWaveOut, _hdrPtr, HdrSize);
                _hdrPrepared = false;
                return;
            }
            _playing = true;
        }

        public bool IsPlaying
        {
            get
            {
                if (_scrubbing) return true;
                if (!_playing || _hWaveOut == IntPtr.Zero) return false;
                if (PositionFrames >= _voiceFrames)
                {
                    _playing = false;
                    return false;
                }
                return true;
            }
        }

        public int PositionFrames
        {
            get
            {
                if (_scrubbing || _hWaveOut == IntPtr.Zero) return 0; // scrub drives its playhead from the cursor
                int raw = ReadRawPositionFrames() - _posBaseFrames;
                if (raw < 0) raw = 0;
                if (_voiceFrames > 0 && raw > _voiceFrames) raw = _voiceFrames;
                return raw;
            }
        }

        // ---------------- continuous scrub stream ----------------

        public void BeginScrub(float[] pcm, int channels, int sampleRate, int startFrame, int totalFrames)
        {
            Stop();
            if (pcm == null || channels <= 0 || sampleRate <= 0 || totalFrames <= 0) return;
            if (!EnsureDevice(sampleRate, channels)) return;
            EnsureClipBuffer(pcm, channels, totalFrames);
            if (_clipBuf == IntPtr.Zero) return;

            _scrubTotalFrames = totalFrames;
            _scrubReadFrame = Mathf.Clamp(startFrame, 0, totalFrames);
            _scrubChunkFrames = Mathf.Max(256, _devRate / 50); // ~20 ms
            _lastFillEndFrame = _scrubReadFrame;
            _scrubFadeInNext = true; // ease the very first buffer in from silence so starting mid-waveform never pops
            _scrubMix = new float[_scrubChunkFrames * Mathf.Max(1, _devChannels)];
            _scrubPlayCeil = _scrubReadFrame + ScrubRunFrames();

            int chunkBytes = _scrubChunkFrames * _blockAlign;
            _silenceBuf = Marshal.AllocHGlobal(chunkBytes);
            var zero = new byte[chunkBytes];
            Marshal.Copy(zero, 0, _silenceBuf, chunkBytes);

            _scrubHdrs = new IntPtr[ScrubBufferCount];
            _scrubBufs = new IntPtr[ScrubBufferCount];
            _scrubPrepared = new bool[ScrubBufferCount];
            for (int i = 0; i < ScrubBufferCount; i++)
            {
                _scrubHdrs[i] = Marshal.AllocHGlobal((int)HdrSize);
                _scrubBufs[i] = Marshal.AllocHGlobal(chunkBytes);
            }

            _scrubbing = true;
            for (int i = 0; i < ScrubBufferCount; i++) FillScrubBuffer(i); // primes + writes from _scrubReadFrame

            EditorApplication.update += Pump;
            _pumpRegistered = true;
        }

        public void ScrubSeek(int frame)
        {
            if (!_scrubbing) return;
            _scrubReadFrame = Mathf.Clamp(frame, 0, _scrubTotalFrames);
            _scrubPlayCeil = _scrubReadFrame + ScrubRunFrames(); // a move re-opens the play window from the new position
        }

        // Refills one scrub buffer from the current read cursor and re-queues it (main thread / pump only).
        private void FillScrubBuffer(int i)
        {
            if (_hWaveOut == IntPtr.Zero) return;
            IntPtr hdr = _scrubHdrs[i];
            IntPtr buf = _scrubBufs[i];
            int chunkBytes = _scrubChunkFrames * _blockAlign;

            if (_scrubPrepared[i])
            {
                waveOutUnprepareHeader(_hWaveOut, hdr, HdrSize);
                _scrubPrepared[i] = false;
            }

            int rf = _scrubReadFrame;
            // Never play past the play ceiling: while the mouse moves, each ScrubSeek pushes the ceiling forward, so
            // audio flows continuously; once it stops moving the cursor reaches the ceiling and we go silent (this is
            // what makes scrubbing stop when you stop, instead of running on through the rest of the clip).
            int limit = Mathf.Min(_scrubTotalFrames, _scrubPlayCeil);
            if (rf < limit)
            {
                int n = Mathf.Min(_scrubChunkFrames, limit - rf);

                // Decide how this chunk's HEAD is built. A contiguous advance (rf == last end) plays seamlessly with
                // a raw copy. A seek JUMP would leave a step discontinuity at the seam (old end value ≠ new start
                // value) → click; we mask it with a short equal-power crossfade from the old continuation into the
                // new position. After silence (scrub start / mouse stopped / seek-back) we instead fade in from zero.
                bool fadeIn = _scrubFadeInNext;
                bool jumped = !fadeIn && rf != _lastFillEndFrame;
                int head = fadeIn ? Mathf.Min(n, FadeInFrames())
                         : jumped ? CrossfadeFrames(n)
                         : 0;

                if (head > 0)
                {
                    for (int k = 0; k < head; k++)
                    {
                        float t = (k + 0.5f) / head;
                        for (int c = 0; c < _devChannels; c++)
                        {
                            float v;
                            if (jumped)
                            {
                                // equal-power: old fades out (cos), new fades in (sin) → constant perceived loudness
                                float aGain = Mathf.Cos(t * 0.5f * Mathf.PI);
                                float bGain = Mathf.Sin(t * 0.5f * Mathf.PI);
                                v = SrcSample(_lastFillEndFrame + k, c) * aGain + SrcSample(rf + k, c) * bGain;
                            }
                            else // fade in from silence
                            {
                                v = SrcSample(rf + k, c) * t;
                            }
                            _scrubMix[k * _devChannels + c] = v;
                        }
                    }
                    WriteFloatsToDevice(buf, 0, _scrubMix, head * _devChannels);
                }

                if (n > head) // body: straight copy of the new position
                    MoveMemory(new IntPtr(buf.ToInt64() + (long)head * _blockAlign),
                               new IntPtr(_clipBuf.ToInt64() + (long)(rf + head) * _blockAlign),
                               (UIntPtr)((uint)((n - head) * _blockAlign)));

                // If this fill reaches the limit (mouse stopped, or clip end), fade its tail to silence so going quiet
                // doesn't click — and arm a fade-in so the next move eases back in.
                bool reachedLimit = rf + n >= limit;
                if (reachedLimit)
                {
                    int fo = Mathf.Min(FadeOutFrames(), n - head);
                    if (fo > 0)
                    {
                        int baseFrame = rf + n - fo;
                        for (int k = 0; k < fo; k++)
                        {
                            float g = 1f - (k + 0.5f) / fo; // 1 → 0
                            for (int c = 0; c < _devChannels; c++)
                                _scrubMix[k * _devChannels + c] = SrcSample(baseFrame + k, c) * g;
                        }
                        WriteFloatsToDevice(buf, n - fo, _scrubMix, fo * _devChannels);
                    }
                }

                if (n < _scrubChunkFrames) // pad the tail with silence
                    MoveMemory(new IntPtr(buf.ToInt64() + (long)n * _blockAlign), _silenceBuf, (UIntPtr)((uint)((_scrubChunkFrames - n) * _blockAlign)));

                _scrubReadFrame = rf + n;
                _lastFillEndFrame = rf + n;
                _scrubFadeInNext = reachedLimit; // if we just ran into silence, the next audible buffer fades in
            }
            else
            {
                MoveMemory(buf, _silenceBuf, (UIntPtr)((uint)chunkBytes)); // idle / past end → silence (stream stays alive for the next move)
                _scrubFadeInNext = true; // a later move into audio should fade in from this silence, not crossfade
            }

            var h = new WAVEHDR { lpData = buf, dwBufferLength = (uint)chunkBytes };
            Marshal.StructureToPtr(h, hdr, false);
            if (waveOutPrepareHeader(_hWaveOut, hdr, HdrSize) != MMSYSERR_NOERROR) return;
            _scrubPrepared[i] = true;
            waveOutWrite(_hWaveOut, hdr, HdrSize);
        }

        // ~6 ms crossfade across a seek seam — short enough to feel instant, long enough to fully hide the step.
        private int CrossfadeFrames(int avail) => Mathf.Min(avail, Mathf.Clamp(Mathf.RoundToInt(_devRate * 0.006f), 64, _scrubChunkFrames / 2));

        // ~2.5 ms fade-in when the stream (re)starts from silence; ~5 ms fade-out when it runs into silence.
        private int FadeInFrames() => Mathf.Clamp(Mathf.RoundToInt(_devRate * 0.0025f), 32, _scrubChunkFrames / 2);
        private int FadeOutFrames() => Mathf.Clamp(Mathf.RoundToInt(_devRate * 0.005f), 32, _scrubChunkFrames / 2);

        // How far a scrub may play past the last seek before going silent. Kept above the buffer queue so continuous
        // dragging never gaps, but small enough (~90 ms) that the sound stops promptly once the mouse stops moving.
        private int ScrubRunFrames() => Mathf.Max(Mathf.RoundToInt(_devRate * 0.09f), (ScrubBufferCount + 1) * _scrubChunkFrames);

        // Reads one interleaved sample from the managed source PCM (range [-1,1]), clamped to the clip's bounds.
        private float SrcSample(int frame, int ch)
        {
            if (frame < 0) frame = 0;
            else if (frame >= _scrubTotalFrames) frame = _scrubTotalFrames - 1;
            return _clipBufSource[frame * _devChannels + ch];
        }

        // Writes <paramref name="count"/> interleaved float samples of <paramref name="mix"/> into the device buffer
        // starting at frame <paramref name="frameOffset"/>, in whatever format the device opened with (float32 / PCM16).
        private void WriteFloatsToDevice(IntPtr buf, int frameOffset, float[] mix, int count)
        {
            int byteBase = frameOffset * _blockAlign;
            if (_devFormatTag == WAVE_FORMAT_IEEE_FLOAT)
            {
                Marshal.Copy(mix, 0, new IntPtr(buf.ToInt64() + byteBase), count);
            }
            else // PCM16
            {
                for (int j = 0; j < count; j++)
                    Marshal.WriteInt16(buf, byteBase + j * 2, (short)(int)(Mathf.Clamp(mix[j], -1f, 1f) * 32767f));
            }
        }

        private void Pump()
        {
            if (!_scrubbing || _hWaveOut == IntPtr.Zero) return;
            for (int i = 0; i < ScrubBufferCount; i++)
            {
                if ((Marshal.ReadInt32(_scrubHdrs[i], DwFlagsOffset) & (int)WHDR_DONE) != 0)
                    FillScrubBuffer(i);
            }
        }

        private void EndScrub()
        {
            if (!_scrubbing) return;
            if (_pumpRegistered) { EditorApplication.update -= Pump; _pumpRegistered = false; }
            if (_hWaveOut != IntPtr.Zero) waveOutReset(_hWaveOut);

            if (_scrubHdrs != null)
            {
                for (int i = 0; i < _scrubHdrs.Length; i++)
                {
                    if (_scrubPrepared != null && _scrubPrepared[i] && _hWaveOut != IntPtr.Zero)
                        waveOutUnprepareHeader(_hWaveOut, _scrubHdrs[i], HdrSize);
                    if (_scrubHdrs[i] != IntPtr.Zero) Marshal.FreeHGlobal(_scrubHdrs[i]);
                    if (_scrubBufs != null && _scrubBufs[i] != IntPtr.Zero) Marshal.FreeHGlobal(_scrubBufs[i]);
                }
            }
            if (_silenceBuf != IntPtr.Zero) { Marshal.FreeHGlobal(_silenceBuf); _silenceBuf = IntPtr.Zero; }
            _scrubHdrs = null; _scrubBufs = null; _scrubPrepared = null;
            _scrubMix = null;
            _scrubbing = false;
        }

        // ---------------- shared ----------------

        public void Stop()
        {
            EndScrub();
            if (_hWaveOut != IntPtr.Zero)
            {
                waveOutReset(_hWaveOut);
                if (_hdrPrepared)
                {
                    waveOutUnprepareHeader(_hWaveOut, _hdrPtr, HdrSize);
                    _hdrPrepared = false;
                }
            }
            _playing = false;
        }

        public void Dispose()
        {
            Stop();
            if (_hWaveOut != IntPtr.Zero)
            {
                waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
            }
            FreeClipBuffer();
            if (_hdrPtr != IntPtr.Zero) Marshal.FreeHGlobal(_hdrPtr);
        }

        private int ReadRawPositionFrames()
        {
            var mt = new MMTIME { wType = TIME_BYTES };
            if (waveOutGetPosition(_hWaveOut, ref mt, MmTimeSize) != MMSYSERR_NOERROR) return 0;
            if (mt.wType == TIME_BYTES) return _blockAlign > 0 ? (int)(mt.val / (uint)_blockAlign) : 0;
            if (mt.wType == TIME_SAMPLES) return (int)mt.val;
            return 0;
        }

        private bool EnsureDevice(int rate, int channels)
        {
            if (_hWaveOut != IntPtr.Zero && _devRate == rate && _devChannels == channels) return true;
            CloseDevice();

            ushort tag = _deviceRejectsFloat ? WAVE_FORMAT_PCM : WAVE_FORMAT_IEEE_FLOAT;
            if (!OpenWith(rate, channels, tag))
            {
                if (tag == WAVE_FORMAT_IEEE_FLOAT)
                {
                    _deviceRejectsFloat = true;
                    if (!OpenWith(rate, channels, WAVE_FORMAT_PCM)) return false;
                }
                else return false;
            }
            return true;
        }

        private bool OpenWith(int rate, int channels, ushort formatTag)
        {
            ushort bits = formatTag == WAVE_FORMAT_IEEE_FLOAT ? (ushort)32 : (ushort)16;
            ushort blockAlign = (ushort)(channels * (bits / 8));
            var fmt = new WAVEFORMATEX
            {
                wFormatTag = formatTag,
                nChannels = (ushort)channels,
                nSamplesPerSec = (uint)rate,
                nAvgBytesPerSec = (uint)(rate * blockAlign),
                nBlockAlign = blockAlign,
                wBitsPerSample = bits,
                cbSize = 0,
            };
            if (waveOutOpen(out var h, WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL) != MMSYSERR_NOERROR)
                return false;
            _hWaveOut = h;
            _devRate = rate;
            _devChannels = channels;
            _devFormatTag = formatTag;
            _blockAlign = blockAlign;
            return true;
        }

        private void CloseDevice()
        {
            if (_hWaveOut != IntPtr.Zero)
            {
                waveOutReset(_hWaveOut);
                if (_hdrPrepared)
                {
                    waveOutUnprepareHeader(_hWaveOut, _hdrPtr, HdrSize);
                    _hdrPrepared = false;
                }
                waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
            }
            FreeClipBuffer();
            _playing = false;
        }

        private void EnsureClipBuffer(float[] pcm, int channels, int frames)
        {
            if (_clipBuf != IntPtr.Zero
                && ReferenceEquals(pcm, _clipBufSource)
                && _clipBufFormatTag == _devFormatTag
                && _clipBufChannels == channels)
            {
                return;
            }

            FreeClipBuffer();

            int count = frames * channels;
            int bytes = frames * _blockAlign;
            _clipBuf = Marshal.AllocHGlobal(bytes);

            if (_devFormatTag == WAVE_FORMAT_IEEE_FLOAT)
            {
                Marshal.Copy(pcm, 0, _clipBuf, count);
            }
            else // PCM16
            {
                for (int i = 0; i < count; i++)
                {
                    int v = (int)(Mathf.Clamp(pcm[i], -1f, 1f) * 32767f);
                    Marshal.WriteInt16(_clipBuf, i * 2, (short)v);
                }
            }

            _clipBufSource = pcm;
            _clipBufChannels = channels;
            _clipBufFormatTag = _devFormatTag;
        }

        private void FreeClipBuffer()
        {
            if (_clipBuf != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_clipBuf);
                _clipBuf = IntPtr.Zero;
            }
            _clipBufSource = null;
        }
    }
}
#endif
