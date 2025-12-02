using System;
using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    [AddComponentMenu("Scheherazade/Chrono Director")]
    public class ChronoDirector : SingletonBehavior<ChronoDirector>
    {
        #region Constants
        public static string DK_LastOnlineKey => "last_online";
        #endregion

        #region Interfaces
        public DateTime? LastOnlineTime
        {
            get
            {
                string storedData = PlayerPrefs.GetString(DK_LastOnlineKey, null);
                if (DateTime.TryParse(storedData, out var lastOnline))
                {
                    return lastOnline;
                }
                return null;
            }
            set
            {
                if (value.HasValue)
                {
                    PlayerPrefs.SetString(DK_LastOnlineKey, value.Value.ToString("o"));
                }
                else
                {
                    PlayerPrefs.DeleteKey(DK_LastOnlineKey);
                }
            }
        }

        public DateTime? LastSessionEndTime { get; private set; }

        public ITimeProvider TimeProvider => _timeProvider;
        public long CurrentUnixTimepoint => ((DateTimeOffset)TimeProvider.UtcNow).ToUnixTimeMilliseconds();
        #endregion

        #region Serialized Fields
        [SerializeField]
        private float onlineMarkerTickDuration = 3000.0f; // 3 seconds
        #endregion

        #region Private Fields
        private ITimeProvider _timeProvider;
        private HashSet<IChronoManagedAction> _actions;
        private Queue<IChronoManagedAction> _removalAction;
        private float _onlineMarkerTimer;
        #endregion

        #region Unity Events
        protected override void Awake()
        {
            base.Awake();
            _actions = new HashSet<IChronoManagedAction>();
            _removalAction = new Queue<IChronoManagedAction>();
            LastSessionEndTime = LastOnlineTime;
        }

        void Update()
        {
            UpdateOnlineTimeMarker();

            if (_timeProvider is IArtificialTimeProvider atp)
            {
                atp.AdvanceTime(TimeSpan.FromSeconds(Time.deltaTime));
            }

            foreach (var action in _actions)
            {
                action.Tick();
            }

            while (_removalAction.Count > 0)
            {
                var action = _removalAction.Dequeue();
                if (action == null) continue;

                if (!_actions.Contains(action)) continue;

                _actions.Remove(action);
            }
        }
        #endregion

        #region Methods
        public void UseTimeProvider(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public void ManageCoroutine(IEnumerator coroutine)
        {
            if (coroutine == null)
            {
                Debug.LogWarning("Coroutine is null, cannot manage.");
                return;
            }

            StartCoroutine(coroutine);
        }

        public void RemoveAction(IChronoManagedAction action)
        {
            if (action == null)
            {
                Debug.LogWarning("Action is null, cannot remove.");
                return;
            }

            if (!_actions.Contains(action))
            {
                return;
            }

            _removalAction.Enqueue(action);
        }

        public void ManageAction(IChronoManagedAction action)
        {
            if (action == null)
            {
                Debug.LogWarning("Action is null, cannot manage.");
                return;
            }

            if (_actions.Contains(action))
            {
                return;
            }

            _actions.Add(action);
        }

        public void RemoveActions(object idFilters)
        {
            if (idFilters == null)
            {
                Debug.LogWarning("Id filters are null, cannot remove actions.");
                return;
            }

            var actionsToRemove = new List<IChronoManagedAction>();
            foreach (var action in _actions)
            {
                if (action.Id != null && action.Id.Equals(idFilters))
                {
                    actionsToRemove.Add(action);
                }
            }

            foreach (var action in actionsToRemove)
            {
                RemoveAction(action);
            }
        }
        #endregion

        #region Private Fields
        private void UpdateOnlineTimeMarker()
        {
            if (_onlineMarkerTimer <= 0)
            {
                LastOnlineTime = TimeProvider.UtcNow;
                _onlineMarkerTimer = onlineMarkerTickDuration / 1000.0f;
            }

            if (_onlineMarkerTimer > 0)
            {
                _onlineMarkerTimer -= Time.deltaTime;
                return;
            }
        }
        #endregion
    }
}
