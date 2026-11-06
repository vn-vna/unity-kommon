using System;

namespace Com.Scheherazade.Common.Chrono
{
    public interface ISettableTimeProvider
    {
        void AdvanceTime(TimeSpan timeSpan);

        void SetTime(DateTime dateTime);

        void Reset();
    }
}
