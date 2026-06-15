using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    public abstract class UserSegmentationBase<T> :
        SingletonScriptableObject<T>,
        IUserSegmentation,
        ITickableModule,
        IIntegrationModule
        where T : ScriptableObject
    {
        #region Interfaces & Properties
        public UserSegmentationStatus Status { get; private set; } = UserSegmentationStatus.Uninitialized;
        public bool IsFirstSegmentDetermined => _firstSegmentDetermined;
        public DateTime? LastSegmentationUpdateTime { get; private set; }
        public SegmentationInformation SegmentInformation => _userSegmentation;
        public SegmentationDeclaration CurrentSegmentDeclaration => _currentSegmentDeclaration;
        #endregion

        #region Serialized Fields
        [SerializeField]
        private SegmentationDeclaration[] declarations;

        [SerializeField]
        private ScriptableObject[] initialProviders;

        [SerializeField]
        private ScriptableObject[] initialTrackers;
        #endregion

        #region Private Fields
        private bool _firstSegmentDetermined = false;
        private SegmentationInformation _userSegmentation;
        private SegmentationDeclaration _currentSegmentDeclaration;
        private List<IUserSegmentationProvider> _providers;
        private List<IUserSegmentationTracker> _segmentationTrackers;
        #endregion

        #region Unity Methods
        protected override void OnEnable()
        {
            base.OnEnable();

            _providers ??= new List<IUserSegmentationProvider>();
            _segmentationTrackers ??= new List<IUserSegmentationTracker>();
            Integration.RegisterManager(this);
        }

        public virtual void Reset()
        {
            Status = UserSegmentationStatus.Uninitialized;
            _firstSegmentDetermined = false;
            _userSegmentation = null;
            _currentSegmentDeclaration = null;
            LastSegmentationUpdateTime = null;

            _providers ??= new List<IUserSegmentationProvider>();
            _providers.Clear();

            _segmentationTrackers ??= new List<IUserSegmentationTracker>();
            _segmentationTrackers.Clear();

            if (initialProviders != null && initialProviders.Length > 0)
            {
                RegisterInitialProviders();
            }

            if (initialTrackers != null && initialTrackers.Length > 0)
            {
                RegisterInitialTrackers();
            }
        }
        #endregion

        #region Public Methods — Lifecycle
        public void Initialize()
        {
            Dispatcher.DispatchCoroutine(InitializeCoroutine());
        }

        public IEnumerator InitializeCoroutine()
        {
            if (Status == UserSegmentationStatus.Initialized)
            {
                yield break;
            }

            Status = UserSegmentationStatus.Initializing;

            QuickLog.Info<UserSegmentationBase<T>>(
                "Initializing {0} segmentation providers.",
                _providers.Count
            );

            foreach (IUserSegmentationProvider provider in _providers)
            {
                provider.SegmentationDataAcquired += OnProviderDataAcquired;
                provider.Initialize();
            }

            while (_providers.Any(p => !p.IsInitialized))
            {
                yield return null;
            }

            Status = UserSegmentationStatus.Initialized;

            QuickLog.Info<UserSegmentationBase<T>>(
                "All segmentation providers initialized."
            );
        }
        #endregion

        #region Public Methods — Tick
        public void Tick(float deltaTime)
        {
            foreach (IUserSegmentationProvider provider in _providers)
            {
                if (provider is ITickableModule tickable)
                {
                    tickable.Tick(deltaTime);
                }
            }
        }
        #endregion

        #region Public Methods — Providers
        public void RegisterProvider(IUserSegmentationProvider provider)
        {
            if (provider == null)
            {
                QuickLog.Error<UserSegmentationBase<T>>(
                    "Attempted to register a null segmentation provider."
                );
                return;
            }

            if (_providers.Any(existing => existing.GetType() == provider.GetType()))
            {
                QuickLog.Warning<UserSegmentationBase<T>>(
                    "Segmentation provider of type {0} is already registered. Skipping.",
                    provider.GetType().Name
                );
                return;
            }

            provider.Manager = this;
            _providers.Add(provider);

            QuickLog.Info<UserSegmentationBase<T>>(
                "Registered segmentation provider: {0}",
                provider.GetType().Name
            );
        }
        #endregion

        #region Public Methods — Trackers
        public void RegisterSegmentationTracker(IUserSegmentationTracker tracker)
        {
            if (tracker == null)
            {
                QuickLog.Error<UserSegmentationBase<T>>(
                    "Attempted to register a null segmentation tracker."
                );
                return;
            }

            if (_segmentationTrackers.Any(existing => existing.GetType() == tracker.GetType()))
            {
                QuickLog.Warning<UserSegmentationBase<T>>(
                    "Segmentation tracker of type {0} is already registered. Skipping.",
                    tracker.GetType().Name
                );
                return;
            }

            tracker.Manager = this;
            _segmentationTrackers.Add(tracker);

            QuickLog.Info<UserSegmentationBase<T>>(
                "Registered segmentation tracker: {0}",
                tracker.GetType().Name
            );
        }

        public void NotifySegmentationTrackers()
        {
            if (!_firstSegmentDetermined)
            {
                QuickLog.Warning<UserSegmentationBase<T>>(
                    "Cannot notify trackers: no segment determined yet."
                );
                return;
            }

            QuickLog.Info<UserSegmentationBase<T>>(
                "Notifying {0} segmentation trackers.",
                _segmentationTrackers.Count
            );

            foreach (IUserSegmentationTracker tracker in _segmentationTrackers)
            {
                tracker.SegmentationDataUpdated(_userSegmentation, _currentSegmentDeclaration);
            }
        }
        #endregion

        #region Private Methods — Provider Event Handling
        private void OnProviderDataAcquired(SegmentationInformation info)
        {
            if (info == null) return;

            QuickLog.Info<UserSegmentationBase<T>>(
                "Segmentation data received from provider."
            );

            _userSegmentation = info;
            DetermineUserSegmention(info);
            SegmentationDataUpdated();

            _firstSegmentDetermined = true;
            LastSegmentationUpdateTime = DateTime.UtcNow;

            NotifySegmentationTrackers();
        }
        #endregion

        #region Private Methods — Initial Registration
        private void RegisterInitialProviders()
        {
            foreach (ScriptableObject providerAsset in initialProviders)
            {
                if (providerAsset is IUserSegmentationProvider provider)
                {
                    RegisterProvider(provider);
                }
                else
                {
                    QuickLog.Warning<UserSegmentationBase<T>>(
                        "Initial provider asset {0} does not implement IUserSegmentationProvider. Skipping.",
                        providerAsset != null ? providerAsset.name : "null"
                    );
                }
            }
        }

        private void RegisterInitialTrackers()
        {
            foreach (ScriptableObject trackerAsset in initialTrackers)
            {
                if (trackerAsset is IUserSegmentationTracker tracker)
                {
                    RegisterSegmentationTracker(tracker);
                }
                else
                {
                    QuickLog.Warning<UserSegmentationBase<T>>(
                        "Initial tracker asset {0} does not implement IUserSegmentationTracker. Skipping.",
                        trackerAsset != null ? trackerAsset.name : "null"
                    );
                }
            }
        }
        #endregion

        #region Private Methods — Segmentation Processing
        private void DetermineUserSegmention(SegmentationInformation info)
        {
            SegmentationDeclaration matched = null;
            if (declarations != null)
            {
                foreach (SegmentationDeclaration declaration in declarations)
                {
                    if (!declaration.Matches(info)) continue;
                    matched = declaration;
                    break;
                }
            }

            if (matched == null)
            {
                QuickLog.Warning<UserSegmentationBase<T>>(
                    "User did not match any segmentation declaration."
                );
                return;
            }

            _currentSegmentDeclaration = matched;
            QuickLog.Info<UserSegmentationBase<T>>(
                "User matched segmentation: {0}",
                matched.SegmentName
            );
        }

        protected virtual void SegmentationDataUpdated()
        {
            QuickLog.Info<UserSegmentationBase<T>>(
                "Segmentation data updated. Segment: {0}",
                _currentSegmentDeclaration != null
                    ? _currentSegmentDeclaration.SegmentName
                    : "Undetermined"
            );
        }
        #endregion
    }
}
