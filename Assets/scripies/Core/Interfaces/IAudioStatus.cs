public interface IAudioStatus
{
    enum MusicType { Area, Combat, Boss }
    public MusicType musicType { get; set; }
}
