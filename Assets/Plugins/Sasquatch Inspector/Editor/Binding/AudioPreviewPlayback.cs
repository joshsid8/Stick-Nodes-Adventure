#if SASQ_AUDIO // AudioClip lives in com.unity.modules.audio, a REMOVABLE built-in module.
using System;
using UnityEngine;
using SasquatchInspector.Editor.Audio;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Plays / stops / scrubs an <see cref="AudioClip"/> entirely in the editor for the AudioPreview widget.
    /// </summary>
    /// <remarks>
    /// This is a self-contained implementation: PCM is read with the public
    /// <c>AudioClip.GetData</c> and pushed to the OS audio device through a native backend
    /// (<see cref="SasquatchInspector.Editor.Audio.NativeAudioBackend"/>, P/Invoke to system audio libraries — winmm on
    /// Windows, AudioToolbox on macOS, ALSA on Linux). Preview is one-shot (no looping). Scrubbing is a CONTINUOUS, seekable stream
    /// (<see cref="BeginScrub"/> / <see cref="ScrubSeek"/>): moving the cursor just shifts the backend's read
    /// position, and the backend equal-power-crossfades each seek seam so dragging is smooth and click-free. When
    /// no native device can be opened (headless box, disabled in settings) the device is a silent no-op and the
    /// widget shows the waveform only.
    /// </remarks>
    internal static class AudioPreviewPlayback
    {
        private static INativeAudioOutput Device => NativeAudioBackend.Device;

        // The clip we last asked to play, so callers can derive normalized progress and detect natural end.
        private static AudioClip _activeClip;

        // Identifies WHICH widget instance started the current playback. There is only one editor preview voice,
        // so the owner disambiguates two widgets bound to the same clip (e.g. Inspector + Designer preview).
        private static object _owner;

        // Snapshot of the active Play, used for absolute position math (PositionFrames is relative to the Play).
        private static int _activeStartFrame; // per-channel frame the current Play started at
        private static int _activeFrames;      // clip.samples (per-channel)
        private static int _activeRate;        // clip.frequency

        // Pause snapshot. There is no native pause, so "pause" = remember where we were + Stop the voice. The
        // snapshot survives Stop() so a later Resume can restart from a chosen sample (e.g. a scrub position).
        private static AudioClip _pausedClip;
        private static int _pausedSample;

        /// <summary>True when an audible native backend is available (false only when no device/sound service could be opened, or native audio is disabled in settings).</summary>
        public static bool IsSupported => NativeAudioBackend.IsAvailable;

        /// <summary>Alias of <see cref="IsSupported"/>; read by the widget to gate its play/scrub controls.</summary>
        public static bool IsAvailable => NativeAudioBackend.IsAvailable;

        /// <summary>True when the active backend supports waveform scrubbing (all native backends do).</summary>
        public static bool SupportsScrubbing => NativeAudioBackend.SupportsScrubbing;

        /// <summary>True when the clip's samples can actually be read for playback (false for streaming/compressed clips).</summary>
        public static bool IsClipPlayable(AudioClip clip) => AudioClipPcm.IsClipPlayable(clip);

        /// <summary>True while a preview is paused (a snapshot exists awaiting <see cref="Resume"/>).</summary>
        public static bool IsPaused { get; private set; }

        /// <summary>Sample position captured at the last <see cref="Pause"/> call.</summary>
        public static int PausedSample => _pausedSample;

        public static AudioClip ActiveClip => _activeClip;

        public static object ActiveOwner => _owner;

        /// <summary>True only if <paramref name="owner"/> started the currently-playing preview.</summary>
        public static bool IsOwnedBy(object owner)
        {
            return owner != null && ReferenceEquals(_owner, owner) && IsPlaying();
        }

        /// <summary>
        /// Starts editor playback of <paramref name="clip"/> from <paramref name="startSample"/>. The
        /// <paramref name="loop"/> argument is accepted for API compatibility but ignored — preview is one-shot.
        /// A streaming/compressed clip (no readable PCM) is a clean silent no-op.
        /// </summary>
        public static void Play(AudioClip clip, int startSample = 0, bool loop = false, object owner = null)
        {
            IsPaused = false;
            if (clip == null) return;

            if (!AudioClipPcm.TryGet(clip, out var pcm, out var channels, out var rate, out var frames))
            {
                // Unplayable (streaming/compressed) — stay silent, exactly like today's waveform-null path.
                _activeClip = null;
                _owner = null;
                return;
            }

            _activeClip = clip;
            _owner = owner;
            _activeRate = rate;
            _activeFrames = frames;
            _activeStartFrame = Mathf.Clamp(startSample, 0, Mathf.Max(0, frames - 1));

            try
            {
                Device.Play(pcm, channels, rate, _activeStartFrame, frames);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Sasquatch Inspector] Failed to start native audio playback: {e.Message}");
                _activeClip = null;
                _owner = null;
            }
        }

        /// <summary>
        /// Begins a CONTINUOUS scrub stream from <paramref name="startSample"/> — smooth, seekable playback for the
        /// waveform drag. Move the position with <see cref="ScrubSeek"/>; end it with <see cref="Stop"/>. A
        /// streaming/compressed clip is a clean silent no-op (same as <see cref="Play"/>).
        /// </summary>
        public static void BeginScrub(AudioClip clip, int startSample, object owner = null)
        {
            IsPaused = false;
            if (clip == null) return;

            if (!AudioClipPcm.TryGet(clip, out var pcm, out var channels, out var rate, out var frames))
            {
                _activeClip = null;
                _owner = null;
                return;
            }

            _activeClip = clip;
            _owner = owner;
            _activeRate = rate;
            _activeFrames = frames;
            _activeStartFrame = Mathf.Clamp(startSample, 0, Mathf.Max(0, frames - 1));

            try
            {
                Device.BeginScrub(pcm, channels, rate, _activeStartFrame, frames);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Sasquatch Inspector] Failed to start native audio scrub: {e.Message}");
                _activeClip = null;
                _owner = null;
            }
        }

        /// <summary>Moves the active scrub stream's read position to <paramref name="sample"/> (smooth, no restart).</summary>
        public static void ScrubSeek(AudioClip clip, int sample)
        {
            if (clip == null || !ReferenceEquals(clip, _activeClip)) return;
            Device.ScrubSeek(Mathf.Clamp(sample, 0, Mathf.Max(0, _activeFrames - 1)));
        }

        /// <summary>Stops the editor preview voice and clears ownership, but leaves any pause snapshot intact.</summary>
        private static void StopVoice()
        {
            try { Device.Stop(); }
            catch { /* stopping should never surface an error to the user */ }
            _activeClip = null;
            _owner = null;
        }

        /// <summary>Stops all editor preview playback and discards any pause snapshot.</summary>
        public static void Stop()
        {
            IsPaused = false;
            _pausedClip = null;
            StopVoice();
        }

        /// <summary>
        /// Sample position of the play head for <paramref name="clip"/> (0 when unavailable), clamped to a valid index.
        /// </summary>
        public static int GetCurrentSample(AudioClip clip)
        {
            if (clip == null || clip.samples <= 0) return 0;
            if (!ReferenceEquals(clip, _activeClip)) return 0;
            int sample = _activeStartFrame + Device.PositionFrames;
            return Mathf.Clamp(sample, 0, Mathf.Max(0, clip.samples - 1));
        }

        /// <summary>
        /// "Pauses" the preview owned by <paramref name="owner"/> by snapshotting the current sample then stopping
        /// the voice. The snapshot survives so a later <see cref="Resume"/> can restart from a chosen position.
        /// No-op if <paramref name="owner"/> does not currently own playback.
        /// </summary>
        public static void Pause(object owner)
        {
            if (!IsOwnedBy(owner)) return;
            _pausedClip = _activeClip;
            _pausedSample = GetCurrentSample(_activeClip);
            IsPaused = true;
            StopVoice(); // keeps the snapshot (does not clear _paused* state)
        }

        /// <summary>
        /// Resumes a paused preview from <paramref name="startSample"/> (the caller supplies it so scrub-resume lands
        /// on the scrubbed position). No-op if not paused.
        /// </summary>
        public static void Resume(int startSample, object owner)
        {
            if (!IsPaused || _pausedClip == null) return;
            var clip = _pausedClip;
            Play(clip, startSample, false, owner); // Play() clears IsPaused / snapshot
        }

        /// <summary>Current playback head position in seconds (0 when unavailable).</summary>
        public static float GetPositionSeconds()
        {
            if (_activeClip == null || _activeRate <= 0) return 0f;
            return (_activeStartFrame + Device.PositionFrames) / (float)_activeRate;
        }

        /// <summary>True while an editor preview clip is actively playing (flips false at the clip's natural end).</summary>
        public static bool IsPlaying()
        {
            return Device.IsPlaying;
        }

        /// <summary>
        /// Optional: move the play head to a sample position (scrub). Implemented as Stop + Play-from-sample on the
        /// active clip (the only backends that report SupportsScrubbing can do this cleanly). No-op otherwise.
        /// </summary>
        public static void SetSamplePosition(AudioClip clip, int samplePosition)
        {
            if (clip == null) return;
            if (!NativeAudioBackend.SupportsScrubbing) return;
            if (!ReferenceEquals(clip, _activeClip)) return;
            Play(clip, samplePosition, false, _owner);
        }

        /// <summary>
        /// Normalized [0..1] playback progress for <paramref name="clip"/>, derived from the editor play head.
        /// Returns 0 when the clip is not the active one or has no length.
        /// </summary>
        public static float GetNormalizedPosition(AudioClip clip)
        {
            if (clip == null || clip.length <= 0f) return 0f;
            if (!ReferenceEquals(clip, _activeClip)) return 0f;
            return Mathf.Clamp01(GetPositionSeconds() / clip.length);
        }
    }
}
#endif // SASQ_AUDIO
