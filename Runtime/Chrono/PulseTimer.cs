using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    public class PulseTimer : IChronoManagedAction
    {
        public long Counter => _counter;
        public object Id { get; set; } = null;
        public PulseTimerClockType ClockType { get; set; } = PulseTimerClockType.Default;

        private ChronoDirector _chronoDirector;
        private long _counter = 0;
        private float _interval;
        private Action<long> _callback;
        private float _timer;
        private int _limit;

        public PulseTimer(
            Action<long> callback = null,
            float interval = 0.0f,
            int limit = -1,
            PulseTimerClockType clockType = PulseTimerClockType.Default
        )
        {
            _chronoDirector = ChronoDirector.Instance;

            if (_chronoDirector == null)
            {
                Debug.LogError("ChronoDirector instance is null. Ensure it is initialized before using PulseTimer.");
                return;
            }

            _counter = 0;
            _interval = interval;
            _callback = callback;
            _limit = limit;
            _timer = 0.0f;
            ClockType = clockType;
        }

        public PulseTimer(
            Action callback = null,
            float interval = 0.0f,
            int limit = -1,
            PulseTimerClockType clockType = PulseTimerClockType.Default
        ) : this((c) => callback?.Invoke(), interval, limit, clockType)
        { }

        void IChronoManagedAction.Tick()
        {
            _timer += ClockType switch
            {
                PulseTimerClockType.Fixed => Time.fixedDeltaTime,
                PulseTimerClockType.UnscaledTime => Time.unscaledDeltaTime,
                _ => Time.deltaTime,
            };

            while (_timer >= _interval && (_limit < 0 || _counter < _limit))
            {
                _counter++;
                _timer -= _interval;
                _callback?.Invoke(_counter);
            }
        }

        public void Start()
        {
            _chronoDirector.ManageAction(this);
        }

        public void Stop()
        {
            _chronoDirector.RemoveAction(this);
        }

        public void Restart()
        {
            Stop();
            _counter = 0;
            _timer = 0.0f;
            Start();
        }
    }
}
