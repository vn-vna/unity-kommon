using System;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    public class TimedOutAction : PulseTimer
    {
        public TimedOutAction(Action callback = null, float interval = 0.0f)
            : base((c) => callback?.Invoke(), interval, 1)
        { }
    }
}
