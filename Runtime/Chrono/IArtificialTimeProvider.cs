using System;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    public interface IArtificialTimeProvider
    {
        void AdvanceTime(TimeSpan timeSpan);
    }
}
