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
        public List<ITrackingProvider> Providers => _providers;
        #endregion

        #region Private Fields
        private List<ITrackingProvider> _providers;
        private Queue<Action> _pendingActions;
        private float _timer;
        #endregion

        #region Constructors
        public TrackingManagerBase()
        {
            _providers = new List<ITrackingProvider>();
            _pendingActions = new Queue<Action>();
            Status = TrackingManagerStatus.Uninitialized;
        }
        #endregion

        #region Unity Callbacks
        protected override void Awake()
        {
            base.Awake();
            Integration.RegisterManager(this);
            _providers.Clear();
        }

        private void Update()
        {
            if (Status != TrackingManagerStatus.Ready) return;

            while (_pendingActions.Count > 0)
            {
                Action action = _pendingActions.Dequeue();
                action?.Invoke();
            }
        }
        #endregion

        #region Public Methods
        public void Initialize(float timeOut)
        {
            StartCoroutine(InitializeCoroutine(timeOut));
        }

        public void Shutdown()
        {
            foreach (ITrackingProvider provider in _providers)
            {
                if (!provider.IsInitialized) continue;
                provider.CleanUp();
            }

            Status = TrackingManagerStatus.Uninitialized;
        }

        public IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            if (!AllowTracking)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking is disabled. Skipping initialization."
                );
                Status = TrackingManagerStatus.Uninitialized;
                yield break;
            }

            Status = TrackingManagerStatus.Initializing;

            _providers.Sort((x, y) => x.Priority.CompareTo(y.Priority));
            foreach (ITrackingProvider provider in _providers)
            {
                provider.Initialize();
            }

            _timer = 0.0f;

            while (true)
            {
                if (_providers.Any(p => p.IsInitialized))
                {
                    Status = TrackingManagerStatus.Ready;
                    yield break;
                }

                if (_timer > timeOut)
                {
                    QuickLog.Error<TrackingManagerBase<T>>(
                        "Tracking initialization timed out."
                    );
                    Status = TrackingManagerStatus.Uninitialized;
                    yield break;
                }

                _timer += Time.deltaTime;
                yield return null;
            }
        }

        public void RegisterProvider(ITrackingProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            foreach (ITrackingProvider existingProvider in _providers)
            {
                if (existingProvider.GetType() == provider.GetType())
                {
                    QuickLog.Warning<TrackingManagerBase<T>>(
                        "Tracking provider of type {0} is already registered. Skipping.",
                        provider.GetType().Name
                    );
                    return;
                }
            }

            provider.TrackingManager = this;
            _providers.Add(provider);
        }

        public void TrackScreen(string screenId)
        {
            if (!AllowTracking)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking is disabled. Skipping screen tracking."
                );
                return;
            }

            if (Status != TrackingManagerStatus.Ready)
            {
                _pendingActions.Enqueue(() => TrackScreen(screenId));
                return;
            }

            foreach (ITrackingProvider provider in _providers)
            {
                if (!provider.IsTrackingEnabled) continue; 
                if ((provider.Features & TrackingProviderFeatures.ScreenView) == 0) continue;
                provider.TrackScreen(screenId);
            }
        }

        public void TrackAction(TrackingActionInfo info)
        {
            if (!AllowTracking)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking is disabled. Skipping screen tracking."
                );
                return;
            }

            if (Status != TrackingManagerStatus.Ready)
            {
                _pendingActions.Enqueue(() => TrackAction(info));
                return;
            }

            foreach (ITrackingProvider provider in _providers)
            {
                if (!provider.IsTrackingEnabled) continue;
                if ((provider.Features & TrackingProviderFeatures.IngameAction) == 0) continue;

                provider.TrackAction(info);
            }
        }

        public void TrackAdRevenue(AdTrackingInfo info)
        {
#if TRACKING_AD_REVENUE_FILTERED
            QuickLog.Warning<TrackingManagerBase<T>>(
                "Ad revenue tracking is disabled via compile-time flag. Skipping."
            );
            return;
#else

            if (!AllowTracking)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking is disabled. Skipping screen tracking."
                );
                return;
            }

            if (Status != TrackingManagerStatus.Ready)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking manager is not ready. Queuing ad revenue tracking."
                );

                _pendingActions.Enqueue(() => TrackAdRevenue(info));
                return;
            }

            foreach (ITrackingProvider provider in _providers)
            {
                if (!provider.IsTrackingEnabled) continue;
                if ((provider.Features & TrackingProviderFeatures.AdRevenue) == 0) continue;

                provider.TrackAdRevenue(info);
            }
#endif
        }

        public void TrackPurchaseRevenue(PurchaseTrackingInfo info)
        {
#if TRACKING_PURCHASE_REVENUE_FILTERED
            QuickLog.Warning<TrackingManagerBase<T>>(
                "Purchase revenue tracking is disabled via compile-time flag. Skipping."
            );
            return;
#else
            if (!AllowTracking)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking is disabled. Skipping screen tracking."
                );
                return;
            }

            if (Status != TrackingManagerStatus.Ready)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking manager is not ready. Queuing purchase revenue tracking."
                );

                _pendingActions.Enqueue(() => TrackPurchaseRevenue(info));
                return;
            }

            foreach (ITrackingProvider provider in _providers)
            {
                if (!provider.IsTrackingEnabled) continue; 
                if ((provider.Features & TrackingProviderFeatures.PurchaseRevenue) == 0) continue;
                provider.TrackPurchaseRevenue(info);
            }
#endif
        }
        #endregion
    }
}