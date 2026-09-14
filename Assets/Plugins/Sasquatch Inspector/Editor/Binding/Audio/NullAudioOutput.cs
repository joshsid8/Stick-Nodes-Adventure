namespace SasquatchInspector.Editor.Audio
{
    /// <summary>
    /// The "no audible preview" backend: the graceful fallback whenever a platform backend fails to initialise
    /// (no audio device / no sound service), when the user disables native audio in Project Settings, or on a
    /// platform with no native backend at all. Every operation is a clean no-op, so the widget degrades to
    /// "waveform only" with no errors and no stuck audio.
    /// </summary>
    internal sealed class NullAudioOutput : INativeAudioOutput
    {
        public bool IsAvailable => false;
        public bool SupportsScrubbing => false;
        public bool IsPlaying => false;
        public int PositionFrames => 0;

        public void Play(float[] pcm, int channels, int sampleRate, int startFrame, int totalFrames) { }
        public void Stop() { }
        public void BeginScrub(float[] pcm, int channels, int sampleRate, int startFrame, int totalFrames) { }
        public void ScrubSeek(int frame) { }
        public void Dispose() { }
    }
}
