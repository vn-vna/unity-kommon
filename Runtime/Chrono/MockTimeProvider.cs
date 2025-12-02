using System;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{

    public class MockTimeProvider :
        ITimeProvider,
        IArtificialTimeProvider
    {
        private DateTime _mockDateTime;

        public MockTimeProvider()
        {
            _mockDateTime = DateTime.Now;
        }

        public MockTimeProvider(DateTime mockDateTime)
        {
            _mockDateTime = mockDateTime;
        }

        public DateTime Now => _mockDateTime;

        public DateTime Today => _mockDateTime.Date;

        public DateTime UtcNow => _mockDateTime.ToUniversalTime();

        public DateTime Epoch => DateTime.UnixEpoch;

        public void SetMockDateTime(DateTime mockDateTime)
        {
            _mockDateTime = mockDateTime;
        }

        public void AdvanceTime(TimeSpan timeSpan)
        {
            _mockDateTime = _mockDateTime.Add(timeSpan);
        }

        public void AdvanceTime(int days, int hours, int minutes, int seconds)
        {
            _mockDateTime = _mockDateTime
                .AddDays(days)
                .AddHours(hours)
                .AddMinutes(minutes)
                .AddSeconds(seconds);
        }

        public void Reset()
        {
            _mockDateTime = DateTime.Now;
        }

        public void SetMockDateTime(int year, int month, int day, int hour, int minute, int second)
        {
            _mockDateTime = new DateTime(year, month, day, hour, minute, second);
        }

        public void SetMockDateTime(int year = 2000, int month = 1, int day = 1)
        {
            _mockDateTime = new DateTime(year, month, day);
        }
    }
}
