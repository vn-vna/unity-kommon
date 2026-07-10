using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    [AddComponentMenu("Scheherazade/Chrono Director")]
    public class ChronoDirector : SingletonBehavior<ChronoDirector>
    {
        #region Constants
        private const string DK_LastOnlineKey = "last_online";
        #endregion

        #region Serialized Fields
        [SerializeField] private ChronoConfiguration _config;
        #endregion

        #region Private Fields
        private ITimeProvider _timeProvider;

        private IChronoPersister _persister;

        private List<TimerEntry> _timers;

        private float _onlineMarkerTimer;
        #endregion

        #region Unity Callbacks
        protected override void Awake()
        {
            base.Awake();
            _timers = new List<TimerEntry>();
            InitializeProviders();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            if (_timeProvider is ISettableTimeProvider settable)
            {
                settable.AdvanceTime(TimeSpan.FromSeconds(deltaTime));
            }

            if (_timeProvider is ITickableTimeProvider tickable)
            {
                tickable.Tick(deltaTime);
            }

            UpdateOnlineTimeMarker(deltaTime);
            TickTimers(deltaTime);
        }
        #endregion

        #region Public Methods (instance)
        public ITimeProvider TimeProvider => _timeProvider ?? new SystemTimeProvider();

        public void SetTimeProvider(ITimeProvider provider)
        {
            _timeProvider = provider ?? new SystemTimeProvider();
        }
        #endregion

        #region Public Methods (static)
        public static DateTime Now =>
            Instance != null && Instance._timeProvider != null
                ? Instance._timeProvider.Now
                : DateTime.Now;

        public static DateTime UtcNow =>
            Instance != null && Instance._timeProvider != null
                ? Instance._timeProvider.UtcNow
                : DateTime.UtcNow;

        public static long UnixMilliseconds =>
            Instance != null && Instance._timeProvider != null
                ? Instance._timeProvider.UnixTimeMilliseconds
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static long UnixSeconds =>
            Instance != null && Instance._timeProvider != null
                ? Instance._timeProvider.UnixTimeSeconds
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static DateTime? LastOnlineTime => Instance?.GetLastOnlineTimeFromPersister();

        public static ITimerHandle StartTimer(
            Action<long> callback,
            float intervalSeconds,
            int limit = -1
        )
        {
            return Instance?.CreateTimer(callback, intervalSeconds, limit);
        }

        public static ITimerHandle StartTimer(
            Action callback,
            float intervalSeconds,
            int limit = -1
        )
        {
            return StartTimer(_ => callback?.Invoke(), intervalSeconds, limit);
        }

        public static ITimerHandle StartTimeout(Action callback, float delaySeconds)
        {
            return StartTimer(_ => callback?.Invoke(), delaySeconds, 1);
        }
        #endregion

        #region Private Methods
        private void InitializeProviders()
        {
            if (!_config)
            {
                _config = Resources.Load<ChronoConfiguration>("ChronoConfiguration");
            }

            if (_config != null)
            {
                _timeProvider = _config.TimeProvider ?? new SystemTimeProvider();
                _persister = _config.Persister;
            }
            else
            {
                _timeProvider = new SystemTimeProvider();
                _persister = null;
            }
        }

        private ITimerHandle CreateTimer(
            Action<long> callback,
            float intervalSeconds,
            int limit
        )
        {
            if (callback == null)
            {
                return null;
            }

            if (intervalSeconds <= 0f)
            {
                intervalSeconds = 1f;
            }

            var entry = new TimerEntry
            {
                Callback = callback,
                Interval = intervalSeconds,
                Limit = limit,
                IsActive = true
            };

            _timers.Add(entry);
            return entry;
        }

        private void TickTimers(float deltaTime)
        {
            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                _timers[i].Tick(deltaTime);
            }
        }

        private void UpdateOnlineTimeMarker(float deltaTime)
        {
            _onlineMarkerTimer -= deltaTime;
            if (_onlineMarkerTimer > 0f)
            {
                return;
            }

            _onlineMarkerTimer = (_config != null
                ? _config.OnlineMarkerIntervalMs
                : 3000f) / 1000f;
            DateTime utcNow = _timeProvider.UtcNow;

            if (_persister != null)
            {
                _persister.Save(DK_LastOnlineKey, utcNow);
            }
        }

        private DateTime? GetLastOnlineTimeFromPersister()
        {
            if (_persister == null)
            {
                return null;
            }

            return _persister.Load(DK_LastOnlineKey);
        }
        #endregion

        #region Bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("[Scheherazade Chrono Director]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<ChronoDirector>();
        }
        #endregion

        #region Nested Types
        internal sealed class TimerEntry : ITimerHandle
        {
            public long Counter { get; internal set; }

            public bool IsActive { get; internal set; }

            public float Interval { get; internal set; }

            public float Accumulator { get; internal set; }

            public int Limit { get; internal set; }

            public Action<long> Callback { get; internal set; }

            long ITimerHandle.Counter => Counter;

            bool ITimerHandle.IsActive => IsActive;

            void ITimerHandle.Cancel()
            {
                IsActive = false;
            }

            public void Tick(float deltaTime)
            {
                if (!IsActive)
                {
                    return;
                }

                Accumulator += deltaTime;

                while (Accumulator >= Interval && (Limit < 0 || Counter < Limit))
                {
                    Accumulator -= Interval;
                    Counter++;
                    Callback?.Invoke(Counter);
                }
            }
        }
        #endregion
    }
}
