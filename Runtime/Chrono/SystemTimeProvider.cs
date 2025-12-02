using System;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    public class SystemTimeProvider : ITimeProvider
    {
        public DateTime Now => DateTime.Now;
        public DateTime Today => DateTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime Epoch => DateTime.UnixEpoch;
    }
}
