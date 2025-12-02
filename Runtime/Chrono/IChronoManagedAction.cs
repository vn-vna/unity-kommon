namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    public interface IChronoManagedAction
    {
        long Counter { get; }
        object Id { get; set; }
        void Tick();
        void Start();
        void Stop();
        void Restart();
    }
}
