using System;

namespace SasquatchInspector.Editor.Audio
{
    /// <summary>
    /// One-shot native audio output for editor preview. Implementations push interleaved float PCM
    /// (range [-1,1], matching <c>AudioClip.GetData</c>) straight to the OS audio device via P/Invoke.
    /// There is no loop concept: every <see cref="Play"/> plays the clip once and stops.
    /// Scrubbing is a CONTINUOUS stream — <see cref="BeginScrub"/> primes a small queue of buffers
    /// fed from a movable read cursor that <see cref="ScrubSeek"/> repositions, and the backend crossfades each
    /// seek seam so dragging stays smooth and click-free (no per-move device restart).
    /// </summary>
    internal interface INativeAudioOutput : IDisposable
    {
        /// <summary>The native library loaded and a device can be opened — i.e. audible preview is possible.</summary>
        bool IsAvailable { get; }

        /// <summary>True when the backend supports the continuous crossfaded scrub stream (Windows/macOS/Linux true; Null false).</summary>
        bool SupportsScrubbing { get; }

        /// <summary>
        /// Play <paramref name="pcm"/> (interleaved float, <paramref name="channels"/>-interleaved) once, from
        /// <paramref name="startFrame"/> up to <paramref name="totalFrames"/> (per-channel frame counts).
        /// Stops any previous voice first. May reuse an already-open device. Never throws to the caller.
        /// </summary>
        void Play(float[] pcm, int channels, int sampleRate, int startFrame, int totalFrames);

        /// <summary>Stop the current voice (one-shot OR scrub). Idempotent. Keeps the device open for reuse.</summary>
        void Stop();

        /// <summary>
        /// Begin a CONTINUOUS scrub stream from <paramref name="startFrame"/>. Unlike <see cref="Play"/> (a
        /// single one-shot buffer), this streams small buffers from a movable read cursor so the position can be
        /// moved smoothly with <see cref="ScrubSeek"/> — no device restart per move, no clicks/gaps. Used only
        /// while the user is actively dragging the waveform. Ended by <see cref="Stop"/>.
        /// </summary>
        void BeginScrub(float[] pcm, int channels, int sampleRate, int startFrame, int totalFrames);

        /// <summary>Move the active scrub stream's read position to <paramref name="frame"/> (cheap; no restart).</summary>
        void ScrubSeek(int frame);

        /// <summary>True while a voice is sounding; flips false at the natural end of the clip (polled main-thread).</summary>
        bool IsPlaying { get; }

        /// <summary>
        /// Per-channel frames played since the most recent <see cref="Play"/> began (reset to 0 each Play).
        /// The facade adds the start frame to get an absolute clip position.
        /// </summary>
        int PositionFrames { get; }
    }
}
