namespace SasquatchInspector.Editor.Audio
{
    /// <summary>
    /// Chooses and owns the single native audio backend for this editor session. Selection is by compile guard
    /// so winmm / AudioToolbox / ALSA+PulseAudio P/Invoke only compiles on its platform; anything else (an unknown
    /// platform, or a failed init) falls back to <see cref="NullAudioOutput"/>. The backend is created lazily and cached, and its device is
    /// kept open across Play/Stop — a scrub tick resets+rewrites rather than reopening. Guarantees the long-standing
    /// "never throws, at worst silent" behaviour of the preview.
    /// </summary>
    internal static class NativeAudioBackend
    {
        private static INativeAudioOutput _device;
        private static bool _created;

        public static INativeAudioOutput Device
        {
            get
            {
                if (!_created)
                {
                    _created = true;
                    _device = Create();
                }
                return _device;
            }
        }

        public static bool IsAvailable => Device.IsAvailable;
        public static bool SupportsScrubbing => Device.SupportsScrubbing;

        private static INativeAudioOutput Create()
        {
#if SASQ_NO_NATIVE_AUDIO
            // Global compile-time kill switch: never open a native audio device (waveform still renders).
            return new NullAudioOutput();
#else
            // Per-project runtime kill switch (Project Settings → Sasquatch Inspector). Silent preview, waveform intact.
            if (SasquatchInspector.Editor.SasquatchSettingsManager.Current.DisableNativeAudioPreview)
                return new NullAudioOutput();

#if UNITY_EDITOR_WIN
            // winmm waveOut is forward-only P/Invoke + polling (no managed callback on a native thread), so it is
            // CoreCLR-tolerant and stays ungated.
            try { return new WaveOutAudioOutput(); } catch { return new NullAudioOutput(); }
#elif UNITY_EDITOR_OSX
            // The AudioToolbox backend invokes a managed delegate from a realtime native audio thread via
            // [AOT.MonoPInvokeCallback] — a Mono-specific mechanism. On a future non-Mono editor runtime (CoreCLR)
            // that reverse call could hard-crash BELOW any managed try/catch, so default to a silent preview there
            // (the waveform still renders). A user who wants to test native audio on such a runtime can opt back in
            // with the SASQ_FORCE_NATIVE_AUDIO scripting define.
#if !SASQ_FORCE_NATIVE_AUDIO
            if (System.Type.GetType("Mono.Runtime") == null) return new NullAudioOutput();
#endif
            try { return new AudioQueueAudioOutput(); } catch { return new NullAudioOutput(); }
#elif UNITY_EDITOR_LINUX
            // Two-stage chain, both legs forward-only P/Invoke driven by a managed feeder thread (no managed
            // callback on a native thread) → CoreCLR-tolerant and ungated, like winmm.
            //   1. ALSA — reaches bare hardware, and on desktops the PipeWire/PulseAudio ALSA compat layers own
            //      its "default" device, so it covers every normal Linux install.
            //   2. PulseAudio (pa_simple) — for systems where ALSA can't make sound. The motivating case is
            //      WSL2/WSLg: no sound-card driver (no /dev/snd, often no libasound at all) and audio reaches
            //      Windows EXCLUSIVELY through WSLg's PulseAudio server. Also covers pulse-only containers.
            // Neither usable → silent preview (waveform still renders) + ONE console note naming each leg's
            // failure, so "no play button on Linux" is diagnosable from a Console screenshot.
            string alsaWhy, pulseWhy;
            try
            {
                var alsa = new AlsaAudioOutput();
                if (alsa.IsAvailable) return alsa;
                alsa.Dispose();
                alsaWhy = "libasound loaded but no playback device would open";
            }
            catch (System.DllNotFoundException) { alsaWhy = "libasound.so.2 is not installed"; }
            catch (System.Exception e) { alsaWhy = e.GetType().Name; }

            try
            {
                var pulse = new PulseAudioOutput();
                if (pulse.IsAvailable) return pulse;
                pulse.Dispose();
                pulseWhy = "libpulse loaded but no PulseAudio/PipeWire server accepted a stream";
            }
            catch (System.DllNotFoundException) { pulseWhy = "libpulse-simple.so.0 is not installed (Debian/Ubuntu: sudo apt install libpulse0)"; }
            catch (System.Exception e) { pulseWhy = e.GetType().Name; }

            UnityEngine.Debug.Log(
                "[Sasquatch Inspector] Audio preview will be silent — no native audio backend could start. "
                + $"ALSA: {alsaWhy}. PulseAudio: {pulseWhy}. "
                + "Waveforms still render; playback returns in the next editor session once ALSA or PulseAudio is reachable.");
            return new NullAudioOutput();
#else
            // No native backend for this platform; silent preview (waveform still renders).
            return new NullAudioOutput();
#endif
#endif // SASQ_NO_NATIVE_AUDIO
        }

        /// <summary>Dispose the device and reset (called on domain reload / quit / entering play mode).</summary>
        public static void Shutdown()
        {
            if (_device != null)
            {
                try { _device.Dispose(); } catch { /* teardown must never surface an error */ }
            }
            _device = null;
            _created = false;
        }
    }
}
