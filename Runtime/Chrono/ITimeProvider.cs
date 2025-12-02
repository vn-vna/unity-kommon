using System;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    public interface ITimeProvider
    {
        DateTime Epoch { get; }
        DateTime Now { get; }
        DateTime UtcNow { get; }
        DateTime Today { get; }
    }
}
