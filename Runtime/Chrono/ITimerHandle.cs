namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    public interface ITimerHandle
    {
        long Counter { get; }

        bool IsActive { get; }

        void Cancel();
    }
}
