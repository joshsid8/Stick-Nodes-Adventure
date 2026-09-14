#if UNITY_EDITOR_OSX
using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace SasquatchInspector.Editor.Audio
{
    /// <summary>
    /// macOS native audio backend via AudioToolbox <c>AudioQueue</c>.
    ///
    /// The output callback runs on a realtime audio thread (not a managed thread), so it does ZERO managed
    /// allocation: it only reads scalar fields and <see cref="Marshal.Copy"/>s the next chunk from the PINNED
    /// source PCM (or a pinned silence buffer) into the queue buffer. Because it is already a streaming model,
    /// smooth scrubbing is free: <see cref="ScrubSeek"/> just publishes a new read position the callback picks up
    /// (no queue teardown, no device restart — that restart is what made the old stop+replay scrub choppy). Past
    /// the end in scrub mode it feeds silence so seeking back is instant.
    ///
    /// VERIFY ON MAC: AudioQueueBuffer field offsets (mAudioData @ 8, mAudioDataByteSize @ 16 on 64-bit), ASBD
    /// float flags (IsFloat|IsPacked = 9), and that AudioQueueStop/Dispose(immediate:true) are synchronous.
    /// </summary>
    internal sealed class AudioQueueAudioOutput : INativeAudioOutput
    {
        private const string Lib = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AudioQueueOutputCallback(IntPtr userData, IntPtr aq, IntPtr buffer);

        [DllImport(Lib)] private static extern int AudioQueueNewOutput(ref AudioStreamBasicDescription format, AudioQueueOutputCallback callback, IntPtr userData, IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr outAQ);
        [DllImport(Lib)] private static extern int AudioQueueAllocateBuffer(IntPtr aq, uint bufferByteSize, out IntPtr outBuffer);
        [DllImport(Lib)] private static extern int AudioQueueEnqueueBuffer(IntPtr aq, IntPtr buffer, uint numPacketDescs, IntPtr packetDescs);
        [DllImport(Lib)] private static extern int AudioQueueStart(IntPtr aq, IntPtr startTime);
        [DllImport(Lib)] private static extern int AudioQueueStop(IntPtr aq, bool immediate);
        [DllImport(Lib)] private static extern int AudioQueueDispose(IntPtr aq, bool immediate);
        [DllImport(Lib)] private static extern int AudioQueueGetCurrentTime(IntPtr aq, IntPtr timeline, ref AudioTimeStamp outTimeStamp, IntPtr outDiscontinuity);

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioStreamBasicDescription
        {
            public double mSampleRate;
            public uint mFormatID;
            public uint mFormatFlags;
            public uint mBytesPerPacket;
            public uint mFramesPerPacket;
            public uint mBytesPerFrame;
            public uint mChannelsPerFrame;
            public uint mBitsPerChannel;
            public uint mReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioTimeStamp
        {
            public double mSampleTime;
            public ulong mHostTime;
            public double mRateScalar;
            public ulong mWordClockTime;
            public long pad0, pad1, pad2, pad3;
        }

        private const uint kAudioFormatLinearPCM = 0x6C70636D;
        private const uint kAudioFormatFlagIsFloat = 1;
        private const uint kAudioFormatFlagIsPacked = 8;

        private const int OFF_MAUDIODATA = 8;
        private const int OFF_BYTESIZE = 16;

        private const int BufferCount = 3;
        private const int ChunkFrames = 4096;

        private IntPtr _aq = IntPtr.Zero;
        private IntPtr[] _buffers;
        private GCHandle _selfHandle;
        private AudioQueueOutputCallback _callbackDelegate;

        private float[] _pcm;
        private GCHandle _pcmHandle;
        private float[] _silence;
        private GCHandle _silenceHandle;
        private float[] _scrubMix;       // pinned scratch for crossfade / fade-in heads (no managed alloc in the callback)
        private GCHandle _scrubMixHandle;

        private int _channels;
        private int _rate;

        // Voice (set on main thread while stopped; cursor advanced by the callback)
        private int _totalFrames;
        private int _startFrame;
        private int _cursorFrames;
        private int _segmentFrames;   // _totalFrames - startFrame, for one-shot end detection
        private int _seekTo = -1;     // scrub seek target published main→callback (Interlocked)
        private int _stopping;
        private bool _scrubMode;
        private int _lastFillEndFrame;   // frame the previous fill ended on — detects a seek jump vs. contiguous advance
        private bool _scrubFadeInNext;   // next audible fill fades in from silence (scrub start / seek-back past the end)
        private int _scrubPlayCeil;      // cursor may not play past this — caps how far a scrub coasts after the mouse stops

        private bool _playing;
        private bool _needBase;
        private double _sampleTimeBase;
        private int _lastPos;

        public AudioQueueAudioOutput()
        {
            _callbackDelegate = OutputCallback;
            _selfHandle = GCHandle.Alloc(this);
        }

        public bool IsAvailable => true;
        public bool SupportsScrubbing => true;

        public void Play(float[] pcm, int channels, int sampleRate, int startFrame, int totalFrames)
        {
            Stop();
            if (pcm == null || channels <= 0 || sampleRate <= 0 || totalFrames <= 0) return;
            if (startFrame < 0) startFrame = 0;
            if (startFrame >= totalFrames) return;
            if (!EnsureQueue(channels, sampleRate)) return;
            EnsurePin(pcm);

            _scrubMode = false;
            _scrubFadeInNext = false;
            _totalFrames = totalFrames;
            _startFrame = startFrame;
            _cursorFrames = 0;
            _segmentFrames = totalFrames - startFrame;
            _needBase = true;
            _lastPos = 0;
            Interlocked.Exchange(ref _seekTo, -1);
            Interlocked.Exchange(ref _stopping, 0);

            for (int i = 0; i < _buffers.Length; i++) Fill(_buffers[i]);
            if (AudioQueueStart(_aq, IntPtr.Zero) != 0) return;
            _playing = true;
        }

        public void BeginScrub(float[] pcm, int channels, int sampleRate, int startFrame, int totalFrames)
        {
            Stop();
            if (pcm == null || channels <= 0 || sampleRate <= 0 || totalFrames <= 0) return;
            if (!EnsureQueue(channels, sampleRate)) return;
            EnsurePin(pcm);

            _scrubMode = true;
            _totalFrames = totalFrames;
            _startFrame = Mathf.Clamp(startFrame, 0, totalFrames);
            _cursorFrames = 0;
            _segmentFrames = Mathf.Max(1, totalFrames - _startFrame);
            _lastFillEndFrame = _startFrame;
            _scrubFadeInNext = true; // ease the first scrub buffer in from silence so starting mid-waveform never pops
            _scrubPlayCeil = _startFrame + ScrubRunFrames();
            Interlocked.Exchange(ref _seekTo, -1);
            Interlocked.Exchange(ref _stopping, 0);

            for (int i = 0; i < _buffers.Length; i++) Fill(_buffers[i]);
            if (AudioQueueStart(_aq, IntPtr.Zero) != 0) return;
            _playing = true;
        }

        public void ScrubSeek(int frame)
        {
            if (!_scrubMode || !_playing) return;
            Interlocked.Exchange(ref _seekTo, Mathf.Clamp(frame, 0, _totalFrames));
        }

        public void Stop()
        {
            Interlocked.Exchange(ref _stopping, 1);
            if (_aq != IntPtr.Zero) AudioQueueStop(_aq, true);
            _playing = false;
            _scrubMode = false;
        }

        public bool IsPlaying
        {
            get
            {
                if (!_playing || _aq == IntPtr.Zero) return false;
                if (Volatile.Read(ref _stopping) != 0) return false;
                if (_scrubMode) return true; // continuous while scrubbing
                int endEps = Mathf.Clamp(_rate / 100, 1, Mathf.Max(1, _segmentFrames / 2));
                if (PositionFrames >= _segmentFrames - endEps) { _playing = false; return false; }
                return true;
            }
        }

        public int PositionFrames
        {
            get
            {
                if (_aq == IntPtr.Zero || _scrubMode) return 0; // scrub drives its playhead from the cursor
                var ts = default(AudioTimeStamp);
                if (AudioQueueGetCurrentTime(_aq, IntPtr.Zero, ref ts, IntPtr.Zero) != 0) return _lastPos;
                if (_needBase) { _sampleTimeBase = ts.mSampleTime; _needBase = false; }
                int pos = (int)(ts.mSampleTime - _sampleTimeBase);
                if (pos < 0) pos = 0;
                if (_segmentFrames > 0 && pos > _segmentFrames) pos = _segmentFrames;
                _lastPos = pos;
                return pos;
            }
        }

        public void Dispose()
        {
            Stop();
            DisposeQueue();
            FreePin();
            if (_selfHandle.IsAllocated) _selfHandle.Free();
            _callbackDelegate = null;
        }

        // ---- realtime callback (ZERO managed allocation) ----

        // CoreCLR MIGRATION RECIPE (when Unity's editor drops Mono for CoreCLR):
        //   [AOT.MonoPInvokeCallback] is Mono-specific. Under CoreCLR, a managed delegate invoked from a native
        //   realtime thread must instead be a static method marked [System.Runtime.InteropServices.UnmanagedCallersOnly]
        //   passed to native as a function pointer (delegate*). This callback is ALREADY shaped for that swap: it is
        //   static, takes only blittable args (three IntPtrs), allocates nothing, and never throws across the boundary.
        //   The blocker today is purely that [UnmanagedCallersOnly] does not exist in Unity's current Mono BCL, so a
        //   pre-emptive rewrite can't compile. Migration = replace the attribute with [UnmanagedCallersOnly], take
        //   &OutputCallback as a function pointer where AudioQueueNewOutput is P/Invoked, and drop the delegate pin
        //   (_callbackDelegate / FreePin). Until then, NativeAudioBackend.Create() defaults this whole backend OFF on
        //   any non-Mono runtime (System.Type.GetType("Mono.Runtime") == null) so it can never be reached there.
        [AOT.MonoPInvokeCallback(typeof(AudioQueueOutputCallback))]
        private static void OutputCallback(IntPtr userData, IntPtr aq, IntPtr buffer)
        {
            try
            {
                var handle = GCHandle.FromIntPtr(userData);
                if (!handle.IsAllocated) return;
                (handle.Target as AudioQueueAudioOutput)?.Fill(buffer);
            }
            catch
            {
                // An exception must never propagate into native code (instant crash). Swallow it (can't log here).
            }
        }

        private void Fill(IntPtr buffer)
        {
            if (Volatile.Read(ref _stopping) != 0) return;

            int seek = Interlocked.Exchange(ref _seekTo, -1);
            bool jumped = false;
            if (seek >= 0)
            {
                // A jump from the contiguous play position is what causes the seam click; flag it so the head below
                // crossfades. A seek that lands exactly where we already were, or one right after silence, isn't a jump.
                jumped = !_scrubFadeInNext && seek != _lastFillEndFrame;
                _startFrame = seek;
                _cursorFrames = 0;
                _scrubPlayCeil = seek + ScrubRunFrames(); // a move re-opens the play window from the new position
            }

            int pos = _startFrame + _cursorFrames;
            // Scrub never plays past the ceiling: dragging keeps pushing it forward, so audio flows; once the mouse
            // stops the cursor reaches it and we go silent (so scrubbing stops instead of running on through the clip).
            int limit = _scrubMode ? Mathf.Min(_totalFrames, _scrubPlayCeil) : _totalFrames;
            int remaining = limit - pos;
            IntPtr dst = Marshal.ReadIntPtr(buffer, OFF_MAUDIODATA);

            if (remaining <= 0)
            {
                if (_scrubMode)
                {
                    int floatsS = ChunkFrames * _channels;
                    Marshal.Copy(_silence, 0, dst, floatsS); // keep the stream alive (instant seek-back)
                    Marshal.WriteInt32(buffer, OFF_BYTESIZE, floatsS * 4);
                    _scrubFadeInNext = true; // a later seek-back into audio should fade in from this silence
                    AudioQueueEnqueueBuffer(_aq, buffer, 0, IntPtr.Zero);
                }
                else
                {
                    Marshal.WriteInt32(buffer, OFF_BYTESIZE, 0); // one-shot end: drain and stop
                }
                return;
            }

            int n = remaining < ChunkFrames ? remaining : ChunkFrames;

            // Build a short declick HEAD when the cursor jumped or the stream is (re)starting from silence; a
            // contiguous advance copies straight through. Device format here is always float32, so we mix in place
            // into the pinned _scrubMix scratch (no managed allocation on this realtime thread).
            bool fadeIn = _scrubMode && _scrubFadeInNext;
            int head = !_scrubMode ? 0
                     : fadeIn ? Mathf.Min(n, FadeInFrames())
                     : jumped ? Mathf.Min(n, CrossfadeFrames())
                     : 0;

            if (head > 0)
            {
                for (int k = 0; k < head; k++)
                {
                    float t = (k + 0.5f) / head;
                    for (int c = 0; c < _channels; c++)
                    {
                        float v;
                        if (fadeIn)
                        {
                            v = SrcSample(pos + k, c) * t; // ramp up from zero
                        }
                        else // equal-power crossfade: old (cos) out, new (sin) in → constant perceived loudness
                        {
                            float aGain = Mathf.Cos(t * 0.5f * Mathf.PI);
                            float bGain = Mathf.Sin(t * 0.5f * Mathf.PI);
                            v = SrcSample(_lastFillEndFrame + k, c) * aGain + SrcSample(pos + k, c) * bGain;
                        }
                        _scrubMix[k * _channels + c] = v;
                    }
                }
                Marshal.Copy(_scrubMix, 0, dst, head * _channels);
            }

            if (n > head) // body: straight copy of the new position
                Marshal.Copy(_pcm, (pos + head) * _channels,
                             new IntPtr(dst.ToInt64() + (long)head * _channels * 4),
                             (n - head) * _channels);

            // Reached the play ceiling (mouse stopped) or the clip end → fade the tail to silence so going quiet
            // doesn't click; arm a fade-in for the next move.
            bool reachedLimit = pos + n >= limit;
            if (_scrubMode && reachedLimit)
            {
                int fo = Mathf.Min(FadeOutFrames(), n - head);
                if (fo > 0)
                {
                    int baseFrame = pos + n - fo;
                    for (int k = 0; k < fo; k++)
                    {
                        float g = 1f - (k + 0.5f) / fo; // 1 → 0
                        for (int c = 0; c < _channels; c++)
                            _scrubMix[k * _channels + c] = SrcSample(baseFrame + k, c) * g;
                    }
                    Marshal.Copy(_scrubMix, 0, new IntPtr(dst.ToInt64() + (long)(n - fo) * _channels * 4), fo * _channels);
                }
            }

            Marshal.WriteInt32(buffer, OFF_BYTESIZE, n * _channels * 4);
            _cursorFrames += n;
            _lastFillEndFrame = pos + n;
            _scrubFadeInNext = _scrubMode && reachedLimit; // if we ran into silence, the next audible buffer fades in
            AudioQueueEnqueueBuffer(_aq, buffer, 0, IntPtr.Zero);
        }

        // ---- internals (main thread) ----

        private bool EnsureQueue(int channels, int rate)
        {
            if (_aq != IntPtr.Zero && _channels == channels && _rate == rate) return true;
            DisposeQueue();

            uint bytesPerFrame = (uint)(channels * 4);
            var fmt = new AudioStreamBasicDescription
            {
                mSampleRate = rate,
                mFormatID = kAudioFormatLinearPCM,
                mFormatFlags = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked, // = 9
                mBytesPerPacket = bytesPerFrame,
                mFramesPerPacket = 1,
                mBytesPerFrame = bytesPerFrame,
                mChannelsPerFrame = (uint)channels,
                mBitsPerChannel = 32,
                mReserved = 0,
            };

            if (AudioQueueNewOutput(ref fmt, _callbackDelegate, GCHandle.ToIntPtr(_selfHandle), IntPtr.Zero, IntPtr.Zero, 0, out _aq) != 0)
            {
                _aq = IntPtr.Zero;
                return false;
            }

            _buffers = new IntPtr[BufferCount];
            uint bufBytes = (uint)(ChunkFrames * channels * 4);
            for (int i = 0; i < BufferCount; i++)
            {
                if (AudioQueueAllocateBuffer(_aq, bufBytes, out _buffers[i]) != 0)
                {
                    DisposeQueue();
                    return false;
                }
            }

            // Pinned silence buffer for scrub-past-end (the callback may not allocate).
            FreeSilence();
            _silence = new float[ChunkFrames * channels];
            _silenceHandle = GCHandle.Alloc(_silence, GCHandleType.Pinned);

            // Pinned scratch for crossfade / fade-in heads, mixed in the realtime callback (no managed alloc there).
            FreeScrubMix();
            _scrubMix = new float[ChunkFrames * channels];
            _scrubMixHandle = GCHandle.Alloc(_scrubMix, GCHandleType.Pinned);

            _channels = channels;
            _rate = rate;
            return true;
        }

        private void DisposeQueue()
        {
            if (_aq != IntPtr.Zero)
            {
                AudioQueueStop(_aq, true);
                AudioQueueDispose(_aq, true);
                _aq = IntPtr.Zero;
            }
            _buffers = null;
            FreeSilence();
            FreeScrubMix();
            _playing = false;
        }

        private void EnsurePin(float[] pcm)
        {
            if (_pcm != null && ReferenceEquals(pcm, _pcm)) return;
            FreePin();
            _pcm = pcm;
            _pcmHandle = GCHandle.Alloc(pcm, GCHandleType.Pinned);
        }

        private void FreePin()
        {
            if (_pcmHandle.IsAllocated) _pcmHandle.Free();
            _pcm = null;
        }

        private void FreeSilence()
        {
            if (_silenceHandle.IsAllocated) _silenceHandle.Free();
            _silence = null;
        }

        private void FreeScrubMix()
        {
            if (_scrubMixHandle.IsAllocated) _scrubMixHandle.Free();
            _scrubMix = null;
        }

        // ~6 ms crossfade across a seek seam; ~2.5 ms fade-in / ~5 ms fade-out at the silence edges.
        private int CrossfadeFrames() => Mathf.Clamp(Mathf.RoundToInt(_rate * 0.006f), 64, ChunkFrames / 2);
        private int FadeInFrames() => Mathf.Clamp(Mathf.RoundToInt(_rate * 0.0025f), 32, ChunkFrames / 2);
        private int FadeOutFrames() => Mathf.Clamp(Mathf.RoundToInt(_rate * 0.005f), 32, ChunkFrames / 2);

        // How far a scrub may play past the last seek before going silent. Above the buffer queue so dragging never
        // gaps, but bounded so the sound stops once the mouse stops. (Mac's larger buffers → a longer coast than Win.)
        private int ScrubRunFrames() => Mathf.Max(Mathf.RoundToInt(_rate * 0.30f), (BufferCount + 1) * ChunkFrames);

        // One interleaved sample from the pinned source PCM (range [-1,1]), clamped to the clip's bounds.
        private float SrcSample(int frame, int ch)
        {
            if (frame < 0) frame = 0;
            else if (frame >= _totalFrames) frame = _totalFrames - 1;
            return _pcm[frame * _channels + ch];
        }
    }
}
#endif
