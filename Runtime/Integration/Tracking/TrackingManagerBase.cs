using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public abstract class TrackingManagerBase<T> :
        SingletonScriptableObject<T>,
        ITrackingManager,
        ITickableModule,
        IIntegrationModule
        where T : ScriptableObject
    {
        #region Interfaces & Properties
        public string DeviceTrackingIdentifier { get; set; }
        public bool AllowTracking { get; set; }
        public TrackingManagerStatus Status { get; private set; }
        public List<ITrackingProvider> Providers => _providers;
        public HashSet<string> FilteredTrackingIds => _filteredTrackingIds;
        public bool? IsTrackingFiltered
        {
            get
            {
                if (Status != TrackingManagerStatus.Ready
                    && Status != TrackingManagerStatus.PartiallyReady) return null;
                if (_filteredTrackingIds == null) return false;
                if (string.IsNullOrWhiteSpace(DeviceTrackingIdentifier)) return false;
                return _filteredTrackingIds.Contains(DeviceTrackingIdentifier);
            }
        }
        #endregion

        #region Serialized Fields
        [SerializeField]
        private ActionSeverity minimumActionSeverity = ActionSeverity.Debug;

        [SerializeField]
        [TrackingFeatureFilter]
        private TrackingProviderFeatures enabledFeatures = TrackingProviderFeatures.AllFeatures;

        [SerializeField]
        private ScriptableObject[] initialProviders;

        [SerializeField]
        private InitialAllowTracking initialAllowTracking = InitialAllowTracking.Undefined;

        [SerializeField]
        private string[] filteredTrackingIds;

        [SerializeField]
        [Tooltip("Determines when the tracking manager reports as Ready.")]
        private ReadyThreshold readyThreshold = ReadyThreshold.AllProviders;
        #endregion

        #region Private Fields
        private const int MaxPendingActionsPerProvider = 500;
        private List<ITrackingProvider> _providers;
        private HashSet<string> _filteredTrackingIds;
        private Queue<Action> _pendingActions;
        private Dictionary<ITrackingProvider, Queue<Action>> _providerPendingBuffers;
        private float _timer;
        #endregion

        #region Lifecycle & Unity Callbacks
        protected override void OnEnable()
        {
            base.OnEnable();
            _providers ??= new List<ITrackingProvider>();
            _pendingActions ??= new Queue<Action>();
            _providerPendingBuffers ??= new Dictionary<ITrackingProvider, Queue<Action>>();
            Integration.RegisterManager(this);
        }

        public virtual void Reset()
        {
            Queue<Action> preservedActions = _pendingActions;
            _pendingActions = new Queue<Action>();

            if (preservedActions != null && preservedActions.Count > 0)
            {
                while (preservedActions.Count > 0)
                {
                    _pendingActions.Enqueue(preservedActions.Dequeue());
                }

                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Preserved {0} pending tracking event(s) across Reset().",
                    _pendingActions.Count
                );
            }

            _providerPendingBuffers = new Dictionary<ITrackingProvider, Queue<Action>>();
            _providers = new List<ITrackingProvider>();
            _filteredTrackingIds = new HashSet<string>(filteredTrackingIds);

            Status = TrackingManagerStatus.Uninitialized;

            switch (initialAllowTracking)
            {
                case InitialAllowTracking.Yes:
                    AllowTracking = true;
                    break;
                case InitialAllowTracking.No:
                    AllowTracking = false;
                    break;
            }

            _providers ??= new List<ITrackingProvider>();
            _providers.Clear();

            if (initialProviders == null || initialProviders.Length == 0)
            {
                QuickLog.Info<TrackingManagerBase<T>>(
                    "No initial tracking providers configured."
                );
            }
            else
            {
                RegisterAllInitialProviders();
            }
        }

        public void AssignFilteredTrackingDevices(params string[] ids)
        {
            foreach (string deviceId in ids) _filteredTrackingIds.Add(deviceId);
        }

        public void UnassignFilteredTrackingDevices(params string[] ids)
        {
            foreach (string deviceId in ids) _filteredTrackingIds.Remove(deviceId);
        }

        public void Tick(float deltaTime)
        {
            DrainProviderPendingBuffers();

            if (Status != TrackingManagerStatus.Ready
                && Status != TrackingManagerStatus.PartiallyReady) return;

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
            Dispatcher.DispatchCoroutine(InitializeCoroutine(timeOut));
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

            QuickLog.Info<TrackingManagerBase<T>>(
                "Initializing tracking manager with {0} providers.",
                _providers.Count
            );

            Status = TrackingManagerStatus.Initializing;

            _providers.Sort((x, y) => x.Priority.CompareTo(y.Priority));
            foreach (ITrackingProvider provider in _providers)
            {
                if (provider == null)
                {
                    QuickLog.Critical<TrackingManagerBase<T>>(
                        "Found a NULL provider, skipping"
                    );
                }

                try
                {
                    provider.Initialize();
                }
                catch (Exception piex)
                {
                    QuickLog.Critical<TrackingManagerBase<T>>(
                        "One provider initialization failure happened: {0}",
                        piex.ToString()
                    );
                }
            }

            _timer = 0.0f;

            while (true)
            {
                bool anyReady = _providers.Any(p => p.IsInitialized);
                bool allReady = _providers.Count > 0 && _providers.All(p => p.IsInitialized);

                if (allReady)
                {
                    Status = TrackingManagerStatus.Ready;
                    yield break;
                }

                if (anyReady && readyThreshold == ReadyThreshold.AnyProvider)
                {
                    Status = TrackingManagerStatus.Ready;
                    yield break;
                }

                if (anyReady && readyThreshold == ReadyThreshold.AllProviders)
                {
                    Status = TrackingManagerStatus.PartiallyReady;
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

            QuickLog.Info<TrackingManagerBase<T>>(
                "Registering tracking provider: {0} (Priority: {1})",
                provider.GetType().Name, provider.Priority
            );
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

            if (_filteredTrackingIds.Contains(DeviceTrackingIdentifier))
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking is filtered by device identifier"
                );
                return;
            }

            if ((enabledFeatures & TrackingProviderFeatures.ScreenView) == 0)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Screen view tracking is disabled in manager features. Skipping."
                );
                return;
            }

            if (Status != TrackingManagerStatus.Ready
                && Status != TrackingManagerStatus.PartiallyReady)
            {
                _pendingActions.Enqueue(() => TrackScreen(screenId));
                return;
            }

            foreach (ITrackingProvider provider in _providers)
            {
                if (!provider.IsInitialized)
                {
                    EnqueueForProvider(provider, () => DispatchScreenToProvider(provider, screenId));
                    continue;
                }

                if (!provider.IsTrackingEnabled) continue;
                if ((provider.EnabledFeatures & provider.Features & TrackingProviderFeatures.ScreenView) == 0) continue;
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

            if (_filteredTrackingIds.Contains(DeviceTrackingIdentifier))
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking is filtered by device identifier"
                );
                return;
            }

            if (Status != TrackingManagerStatus.Ready
                && Status != TrackingManagerStatus.PartiallyReady)
            {
                _pendingActions.Enqueue(() => TrackAction(info));
                return;
            }

            if (info.Severity < minimumActionSeverity)
            {
                QuickLog.Info<TrackingManagerBase<T>>(
                    $"Action severity {info.Severity} is below minimum {minimumActionSeverity}. Skipping."
                );
                return;
            }

            if ((enabledFeatures & TrackingProviderFeatures.IngameAction) == 0)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Ingame action tracking is disabled in manager features. Skipping."
                );
                return;
            }

            QuickLog.Debug<TrackingManagerBase<T>>(
                "Tracking action: [ActionId = {0}, Severity = {1}], Parameters = {2}",
                info.ActionId, info.Severity,
                (Func<object>)(() =>
                {
                    if (info.Parameters == null) return "null";
                    return "{" + string.Join(", ",
                        info.Parameters.Select(kv => $"{kv.Key} = {kv.Value}")) + "}";
                })
            );

            foreach (ITrackingProvider provider in _providers)
            {
                if (!provider.IsInitialized)
                {
                    EnqueueForProvider(provider, () => DispatchActionToProvider(provider, info));
                    continue;
                }

                if (!provider.IsTrackingEnabled) continue;
                if ((provider.EnabledFeatures & provider.Features & TrackingProviderFeatures.IngameAction) == 0) continue;
                if (info.Severity < provider.MinimumActionSeverity) continue;
                if (info.ProviderMask != ProviderIdentity.None
                    && (info.ProviderMask & provider.ProviderIdentity) == 0) continue;

                provider.TrackAction(info);
            }
        }

        public void TrackAdRevenue(AdTrackingInfo info)
        {
#if TRACKING_AD_REVENUE_FILTERED
            QuickLog.Warning<TrackingManagerBase<T>>(
                "Ad revenue tracking is disabled via compile-time flag. Skipping."
            );
            QuickLog.Debug<TrackingManagerBase<T>>(
                "AdTrackingInfo: [Network = {0}, Placement = {1}, Revenue = {2}, Currency = {3}]",
                info.NetworkName, info.Placement,
                info.Revenue, info.RevenueUnit
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

            if (_filteredTrackingIds.Contains(DeviceTrackingIdentifier))
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking is filtered by device identifier"
                );
                return;
            }

            if (Status != TrackingManagerStatus.Ready
                && Status != TrackingManagerStatus.PartiallyReady)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking manager is not ready. Queuing ad revenue tracking."
                );

                _pendingActions.Enqueue(() => TrackAdRevenue(info));
                return;
            }

            if ((enabledFeatures & TrackingProviderFeatures.AdRevenue) == 0)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Ad revenue tracking is disabled in manager features. Skipping."
                );
                return;
            }

            foreach (ITrackingProvider provider in _providers)
            {
                if (!provider.IsInitialized)
                {
                    EnqueueForProvider(provider, () => DispatchAdRevenueToProvider(provider, info));
                    continue;
                }

                if (!provider.IsTrackingEnabled) continue;
                if ((provider.EnabledFeatures & provider.Features & TrackingProviderFeatures.AdRevenue) == 0) continue;

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
            QuickLog.Debug<TrackingManagerBase<T>>(
                "PurchaseTrackingInfo: [Id = {0}, ProductId = {1}, Price = {2}, Currency = {3}]",
                info.TransactionId, info.ProductId,
                info.Price, info.Currency
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

            if (_filteredTrackingIds.Contains(DeviceTrackingIdentifier))
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking is filtered by device identifier"
                );
                return;
            }

            if (Status != TrackingManagerStatus.Ready
                && Status != TrackingManagerStatus.PartiallyReady)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Tracking manager is not ready. Queuing purchase revenue tracking."
                );

                _pendingActions.Enqueue(() => TrackPurchaseRevenue(info));
                return;
            }

            if ((enabledFeatures & TrackingProviderFeatures.PurchaseRevenue) == 0)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Purchase revenue tracking is disabled in manager features. Skipping."
                );
                return;
            }

            foreach (ITrackingProvider provider in _providers)
            {
                if (!provider.IsInitialized)
                {
                    EnqueueForProvider(
                        provider,
                        () => DispatchPurchaseRevenueToProvider(provider, info)
                    );
                    continue;
                }

                if (!provider.IsTrackingEnabled) continue;
                if ((provider.EnabledFeatures & provider.Features & TrackingProviderFeatures.PurchaseRevenue) == 0) continue;
                provider.TrackPurchaseRevenue(info);
            }
#endif
        }
        #endregion

        #region Private Methods
        private void DrainProviderPendingBuffers()
        {
            foreach (ITrackingProvider provider in _providers)
            {
                if (!provider.IsInitialized) continue;

                if (_providerPendingBuffers.TryGetValue(provider, out Queue<Action> buffer))
                {
                    while (buffer.Count > 0)
                    {
                        Action action = buffer.Dequeue();
                        action?.Invoke();
                    }
                }
            }
        }

        private void EnqueueForProvider(ITrackingProvider provider, Action action)
        {
            if (!_providerPendingBuffers.TryGetValue(provider, out Queue<Action> buffer))
            {
                buffer = new Queue<Action>();
                _providerPendingBuffers[provider] = buffer;
            }

            if (buffer.Count >= MaxPendingActionsPerProvider)
            {
                QuickLog.Warning<TrackingManagerBase<T>>(
                    "Provider {0} pending buffer full ({1} events). Dropping oldest.",
                    provider.GetType().Name,
                    MaxPendingActionsPerProvider
                );
                buffer.Dequeue();
            }

            buffer.Enqueue(action);
        }

        private static void DispatchScreenToProvider(ITrackingProvider provider, string screenId)
        {
            if (!provider.IsTrackingEnabled) return;
            if ((provider.EnabledFeatures & provider.Features & TrackingProviderFeatures.ScreenView) == 0) return;
            provider.TrackScreen(screenId);
        }

        private static void DispatchActionToProvider(
            ITrackingProvider provider,
            TrackingActionInfo info
        )
        {
            if (!provider.IsTrackingEnabled) return;
            if ((provider.EnabledFeatures & provider.Features
                & TrackingProviderFeatures.IngameAction) == 0) return;
            if (info.Severity < provider.MinimumActionSeverity) return;
            if (info.ProviderMask != ProviderIdentity.None
                && (info.ProviderMask & provider.ProviderIdentity) == 0) return;

            provider.TrackAction(info);
        }

        private static void DispatchAdRevenueToProvider(
            ITrackingProvider provider,
            AdTrackingInfo info
        )
        {
            if (!provider.IsTrackingEnabled) return;
            if ((provider.EnabledFeatures & provider.Features
                & TrackingProviderFeatures.AdRevenue) == 0) return;

            provider.TrackAdRevenue(info);
        }

        private static void DispatchPurchaseRevenueToProvider(
            ITrackingProvider provider,
            PurchaseTrackingInfo info
        )
        {
            if (!provider.IsTrackingEnabled) return;
            if ((provider.EnabledFeatures & provider.Features
                & TrackingProviderFeatures.PurchaseRevenue) == 0) return;

            provider.TrackPurchaseRevenue(info);
        }

        private void RegisterAllInitialProviders()
        {
            foreach (ScriptableObject providerAsset in initialProviders)
            {
                if (providerAsset is not ITrackingProvider provider)
                {
                    QuickLog.Warning<TrackingManagerBase<T>>(
                        "Initial provider asset {0} does not implement ITrackingProvider. Skipping.",
                        providerAsset.name
                    );
                    continue;
                }

                RegisterProvider(provider);
            }
        }
        #endregion

        #region Nested Types
        public enum ReadyThreshold
        {
            AnyProvider,
            AllProviders
        }

        private enum InitialAllowTracking
        {
            Undefined, Yes, No,
        }
        #endregion
    }
}