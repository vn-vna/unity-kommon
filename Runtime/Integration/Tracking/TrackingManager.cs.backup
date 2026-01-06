using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public abstract class TrackingManagerBase<T> :
        SingletonBehavior<T>,
        ITrackingManager
        where T : TrackingManagerBase<T>
    {
        #region Interfaces & Properties
        public bool AllowTracking { get; set; }
        public TrackingManagerStatus Status { get; private set; }
        public IReadOnlyList<ITrackingProvider> Providers => _providers;
        #endregion

        #region Serialized Fields
        [SerializeField]
        private ActionSeverity minimumActionSeverity = ActionSeverity.Debug;
        #endregion

        #region Private Fields
        private readonly List<ITrackingProvider> _providers = new();
        private readonly Dictionary<ITrackingProvider, Queue<Action<ITrackingProvider>>> _queues = new();
        #endregion

        #region Unity
        protected override void Awake()
        {
            base.Awake();
            Integration.RegisterManager(this);
            Status = TrackingManagerStatus.Uninitialized;
        }

        private void Update()
        {
            foreach (var provider in _providers)
            {
                if (!provider.IsInitialized) continue;
                FlushQueue(provider, budget: 5);
            }
        }
        #endregion

        #region Initialization
        public void Initialize(float timeOut = float.MaxValue)
        {
            StartCoroutine(InitializeCoroutine(timeOut));
        }

        public IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            if (!AllowTracking)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking disabled. Initialization skipped."
                );
                yield break;
            }

            Status = TrackingManagerStatus.Initializing;

            foreach (var provider in _providers.OrderBy(p => p.Priority))
            {
                provider.Initialize();
            }

            float timer = 0f;
            while (timer < timeOut)
            {
                if (_providers.Any(p => p.IsInitialized))
                {
                    Status = TrackingManagerStatus.Ready;
                    yield break;
                }
                timer += Time.deltaTime;
                yield return null;
            }

            QuickLog.Error<TrackingManagerBase<T>>("Tracking init timeout.");
            Status = TrackingManagerStatus.Uninitialized;
        }

        public void Shutdown()
        {
            foreach (var provider in _providers)
            {
                if (provider.IsInitialized)
                    provider.CleanUp();
            }
            _queues.Clear();
            Status = TrackingManagerStatus.Uninitialized;
        }
        #endregion

        #region Provider Registration
        public void RegisterProvider(ITrackingProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (_providers.Contains(provider)) return;

            provider.TrackingManager = this;
            _providers.Add(provider);
            _queues[provider] = new Queue<Action<ITrackingProvider>>();
        }
        #endregion

        #region Tracking APIs
        public void TrackScreen(string screenId)
        {
            Dispatch(provider =>
            {
                if ((provider.Features & TrackingProviderFeatures.ScreenView) == 0) return;
                provider.TrackScreen(screenId);
            });
        }

        public void TrackAction(TrackingActionInfo info)
        {
            if (info.Severity < minimumActionSeverity) return;

            Dispatch(provider =>
            {
                if ((provider.Features & TrackingProviderFeatures.IngameAction) == 0) return;
                if (info.Severity < provider.MinimumActionSeverity) return;
                provider.TrackAction(info);
            });
        }

        public void TrackAdRevenue(AdTrackingInfo info)
        {
            Dispatch(provider =>
            {
                if ((provider.Features & TrackingProviderFeatures.AdRevenue) == 0) return;
                provider.TrackAdRevenue(info);
            });
        }

        public void TrackPurchaseRevenue(PurchaseTrackingInfo info)
        {
            Dispatch(provider =>
            {
                if ((provider.Features & TrackingProviderFeatures.PurchaseRevenue) == 0) return;
                provider.TrackPurchaseRevenue(info);
            });
        }
        #endregion

        #region Core Dispatch Logic
        private void Dispatch(Action<ITrackingProvider> action)
        {
            if (!AllowTracking) return;

            foreach (var provider in _providers)
            {
                if (!provider.IsTrackingEnabled) continue;

                if (provider.IsInitialized)
                {
                    action(provider);
                }
                else
                {
                    _queues[provider].Enqueue(action);
                }
            }
        }

        private void FlushQueue(ITrackingProvider provider, int budget)
        {
            var queue = _queues[provider];
            while (queue.Count > 0 && budget-- > 0)
            {
                var action = queue.Dequeue();
                action(provider);
            }
        }
        #endregion
    }
}
