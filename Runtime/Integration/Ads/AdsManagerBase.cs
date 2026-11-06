using System;
using System.Collections;
using Com.Scheherazade.Common.Chrono;
using Com.Scheherazade.Common.Logging;
using Com.Scheherazade.Common.Singleton;
using Com.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Scheherazade.Common.Integration.Ads
{
    public enum AdsManagerStatus
    {
        Uninitialized,
        Initializing,
        Ready
    }

    public enum IntervalTrackingMode
    {
        DeltaTime,
        TimePoint
    }

    public abstract class AdsManagerBase<T> :
        SingletonScriptableObject<T>,
        IAdsManager,
        ITickableModule,
        IIntegrationModule
        where T : ScriptableObject
    {
        #region Constants
        private const float DefaultInitializationTimeout = 30f;
        #endregion

        #region Properties
        public string DeviceAdvertisingId => _provider != null ? _provider.DeviceAdvertisingId : string.Empty;
        public bool IsBannerAvailable => _provider != null && _provider.IsBannerAvailable;
        public AdsBannerState BannerState => _provider?.BannerState ??
            AdsBannerState.Unavailable("No Ads provider is registered.");
        public bool IsInterstitialAdsAvailable => _provider != null && _provider.IsInterstitialAvailable;
        public bool IsRewardAdsAvailable => _provider != null && _provider.IsRewardedAvailable;
        public bool IsAppOpenAdsAvailable => _provider != null && _provider.IsOpenAppAdAvailable;
        public AdsManagerStatus Status { get; private set; } = AdsManagerStatus.Uninitialized;
        public int InterstitialAdCount { get; private set; }
        public int RewardAdCount { get; private set; }
        public int AppOpenAdCount { get; private set; }
        public float ShowInterstitialAdsInterval { get; set; } = 120f;
        public bool IsIntersitialAdsWillShow => _intervalTrackingMode switch
        {
            IntervalTrackingMode.DeltaTime => _interstitialTimer >= ShowInterstitialAdsInterval,
            IntervalTrackingMode.TimePoint => _isInterstitialReadyCached,
            _ => throw new ArgumentException()
        };
        #endregion

        #region Serialized Fields
        [SerializeField]
        private AdsConfiguration overrideConfig;

        [SerializeField]
        [HideInInspector]
        private ScriptableObject adServiceProvider;

        [SerializeField]
        private IntervalTrackingMode _intervalTrackingMode = IntervalTrackingMode.DeltaTime;

        [SerializeField]
        private float _timePointCheckInterval = 1f;

        [SerializeField]
        private bool _verboseDebugging;
        #endregion

        #region Private Fields
        private IAdsServiceProvider _provider;
        private float _interstitialTimer;
        private DateTime _lastAdShowTimePoint;
        private float _checkIntervalAccumulator;
        private bool _isInterstitialReadyCached;
        private bool _initializationRequested;
        private bool _shuttingDown;
        private int _lifecycleGeneration;
        #endregion

        #region Unity Callbacks
        protected override void OnEnable()
        {
            base.OnEnable();
            Integration.RegisterManager(this);
        }

        protected override void OnDisable()
        {
            Shutdown();
            base.OnDisable();
        }
        #endregion

        #region Public Methods
        public virtual void Reset()
        {
            Shutdown();
            InterstitialAdCount = 0;
            RewardAdCount = 0;
            AppOpenAdCount = 0;
            _interstitialTimer = 0f;
            _lastAdShowTimePoint = ChronoDirector.UtcNow;
            _checkIntervalAccumulator = 0f;
            _isInterstitialReadyCached = false;
            OverrideConfiguration();
            ResetProvider();
        }

        public virtual void Tick(float deltaTime)
        {
            if (_provider == null) return;
            try { _provider.LoadAds(); }
            catch (Exception exception)
            {
                QuickLog.Error<AdsManagerBase<T>>("Ads provider LoadAds failed: {0}", exception.Message);
            }
            ReconcileLateInitialization();
            ResolveInterstitialAdInterval(SanitizeDelta(deltaTime));
        }

        public virtual void Initialize(float timeOut = float.MaxValue)
        {
            if (_shuttingDown || Status == AdsManagerStatus.Ready) return;
            Dispatcher.DispatchCoroutine(InitializeCoroutine(timeOut));
        }

        public virtual IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            if (_shuttingDown || Status == AdsManagerStatus.Ready) yield break;
            if (_provider == null)
            {
                QuickLog.Error<AdsManagerBase<T>>("No provider registered. Cannot initialize AdsManager.");
                Status = AdsManagerStatus.Uninitialized;
                yield break;
            }

            int generation = _lifecycleGeneration;
            Status = AdsManagerStatus.Initializing;
            if (!_initializationRequested)
            {
                _initializationRequested = true;
                try { _provider.Initialize(); }
                catch (Exception exception)
                {
                    QuickLog.Error<AdsManagerBase<T>>("Ads provider initialization failed: {0}", exception.Message);
                    _initializationRequested = false;
                    Status = AdsManagerStatus.Uninitialized;
                    yield break;
                }
            }

            float timeout = NormalizeTimeout(timeOut);
            float elapsed = 0f;
            while (generation == _lifecycleGeneration && _provider != null &&
                   !_provider.IsInitialized && elapsed < timeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (generation != _lifecycleGeneration || _shuttingDown) yield break;
            if (_provider != null && _provider.IsInitialized)
            {
                Status = AdsManagerStatus.Ready;
                LogDebug("Initialize: ready");
            }
            else
            {
                Status = AdsManagerStatus.Uninitialized;
                LogDebug("Initialize: timed out; late provider readiness remains observed by Tick");
            }
        }

        public virtual void Shutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            ++_lifecycleGeneration;
            _initializationRequested = false;
            try
            {
                LogDebug("Shutdown");
                if (_provider != null) _provider.CleanUp();
            }
            catch (Exception exception)
            {
                QuickLog.Error<AdsManagerBase<T>>("Ads provider cleanup failed: {0}", exception.Message);
            }
            finally
            {
                Status = AdsManagerStatus.Uninitialized;
                _shuttingDown = false;
            }
        }

        public void RegisterProvider(IAdsServiceProvider provider)
        {
            if (ReferenceEquals(_provider, provider))
            {
                if (provider != null) provider.AdsManager = this;
                return;
            }

            IAdsServiceProvider previous = _provider;
            _provider = null;
            if (previous != null)
            {
                try { previous.CleanUp(); }
                catch (Exception exception)
                {
                    QuickLog.Error<AdsManagerBase<T>>("Previous Ads provider cleanup failed: {0}", exception.Message);
                }
                finally { previous.AdsManager = null; }
            }

            _provider = provider;
            _initializationRequested = false;
            Status = AdsManagerStatus.Uninitialized;
            if (_provider != null) _provider.AdsManager = this;
            LogDebug("RegisterProvider: {0}", provider?.GetType().Name ?? "null");
        }

        public virtual AdsInvocationHandler ShowBanner()
        {
            LogDebug("ShowBanner");
            return InvokeProvider(AdsType.Banner, string.Empty, provider => provider.ShowBanner());
        }

        public virtual AdsInvocationHandler HideBanner()
        {
            LogDebug("HideBanner");
            return InvokeProvider(AdsType.Banner, string.Empty, provider => provider.HideBanner());
        }

        public virtual AdsInvocationHandler ShowInterstitialAds(string placement, bool force = false)
        {
            LogDebug(
                "ShowInterstitialAds: placement={0}, force={1}, ready={2}",
                placement,
                force,
                IsIntersitialAdsWillShow
            );
            if (_provider == null) return MissingProvider(AdsType.Interstitial, placement);
            if (!IsIntersitialAdsWillShow && !force)
            {
                QuickLog.Debug<AdsManagerBase<T>>(
                    "Interstitial ad skipped. Interval not met. {0:F1} / {1}s",
                    GetInterstitialProgressFormatted(),
                    ShowInterstitialAdsInterval
                );
                return AdsInvocationHandler.Skipped(
                    AdsType.Interstitial,
                    placement,
                    "Interstitial interval has not elapsed."
                );
            }

            AdsInvocationHandler handler = InvokeProvider(
                AdsType.Interstitial,
                placement,
                provider => provider.ShowInterstitialAds(placement)
            );
            ObserveShowing(handler, AdsType.Interstitial);
            return handler;
        }

        public virtual AdsInvocationHandler ShowRewardAds(string placement)
        {
            LogDebug("ShowRewardAds: placement={0}", placement);
            AdsInvocationHandler handler = InvokeProvider(
                AdsType.Rewarded,
                placement,
                provider => provider.ShowRewardAds(placement)
            );
            ObserveShowing(handler, AdsType.Rewarded);
            return handler;
        }

        public virtual AdsInvocationHandler ShowAppOpenAds(string placement)
        {
            LogDebug("ShowAppOpenAds: placement={0}", placement);
            AdsInvocationHandler handler = InvokeProvider(
                AdsType.OpenApp,
                placement,
                provider => provider.ShowAppOpenAds(placement)
            );
            ObserveShowing(handler, AdsType.OpenApp);
            return handler;
        }
        #endregion

        #region Private Methods
        private AdsInvocationHandler InvokeProvider(
            AdsType type,
            string placement,
            Func<IAdsServiceProvider, AdsInvocationHandler> invoke
        )
        {
            if (_shuttingDown)
                return AdsInvocationHandler.Failed(type, placement, "Ads manager is shutting down.");
            if (_provider == null) return MissingProvider(type, placement);
            try
            {
                AdsInvocationHandler handler = invoke(_provider);
                if (handler == null)
                    return AdsInvocationHandler.Failed(type, placement, "Ads provider returned no invocation handler.");
                if (handler.AdType != type)
                    return AdsInvocationHandler.Failed(type, placement, "Ads provider returned a handler for the wrong ad type.");
                ObserveDebug(handler);
                return handler;
            }
            catch (Exception exception)
            {
                QuickLog.Error<AdsManagerBase<T>>(
                    "Ads provider invocation failed for {0} placement '{1}': {2}",
                    type,
                    placement,
                    exception.Message
                );
                return AdsInvocationHandler.Failed(type, placement, exception.Message);
            }
        }

        private AdsInvocationHandler MissingProvider(AdsType type, string placement)
        {
            QuickLog.Warning<AdsManagerBase<T>>("No provider registered.");
            return AdsInvocationHandler.Failed(type, placement, "No Ads provider is registered.");
        }

        private void ObserveShowing(AdsInvocationHandler handler, AdsType type)
        {
            if (handler == null) return;
            bool counted = false;
            IDisposable subscription = null;
            subscription = handler.Observe(current =>
            {
                if (!counted && (current.Status == AdsInvocationStatus.Showing || current.WasDisplayed))
                {
                    counted = true;
                    switch (type)
                    {
                        case AdsType.Interstitial:
                            ++InterstitialAdCount;
                            ResetIntersitialInterval();
                            break;
                        case AdsType.Rewarded:
                            ++RewardAdCount;
                            ResetIntersitialInterval();
                            break;
                        case AdsType.OpenApp:
                            ++AppOpenAdCount;
                            break;
                    }
                }
                if (current.IsTerminal) subscription?.Dispose();
            });
            if (handler.IsTerminal) subscription.Dispose();
        }

        private void ObserveDebug(AdsInvocationHandler handler)
        {
            if (!_verboseDebugging || handler == null) return;
            IDisposable subscription = null;
            subscription = handler.Observe(current =>
            {
                QuickLog.Debug<AdsManagerBase<T>>(
                    "{0} status={1}, placement={2}, displayed={3}, closed={4}, reward={5}, reason={6}",
                    current.AdType,
                    current.Status,
                    current.Placement,
                    current.WasDisplayed,
                    current.WasClosed,
                    current.RewardEarned,
                    current.Reason
                );
                if (current.IsTerminal) subscription?.Dispose();
            });
            if (handler.IsTerminal) subscription.Dispose();
        }

        private void ResetProvider()
        {
            if (adServiceProvider == null) return;
            if (adServiceProvider is not IAdsServiceProvider provider)
            {
                QuickLog.Error<AdsManagerBase<T>>(
                    "Assigned ad service provider does not implement IAdsServiceProvider."
                );
                return;
            }
            RegisterProvider(provider);
        }

        private void OverrideConfiguration()
        {
            if (!overrideConfig) return;
            ShowInterstitialAdsInterval = overrideConfig.ShowInterstitialAdsInterval;
        }

        private void ReconcileLateInitialization()
        {
            if (!_initializationRequested || _provider == null || !_provider.IsInitialized) return;
            if (Status != AdsManagerStatus.Ready) LogDebug("Initialize: provider became ready after wait ended");
            Status = AdsManagerStatus.Ready;
        }

        private float GetInterstitialProgressFormatted()
        {
            if (_intervalTrackingMode == IntervalTrackingMode.DeltaTime) return _interstitialTimer;
            return (float)(ChronoDirector.UtcNow - _lastAdShowTimePoint).TotalSeconds;
        }

        protected void ResetIntersitialInterval()
        {
            if (_intervalTrackingMode == IntervalTrackingMode.DeltaTime)
            {
                _interstitialTimer = 0f;
                return;
            }
            _lastAdShowTimePoint = ChronoDirector.UtcNow;
            _checkIntervalAccumulator = 0f;
            _isInterstitialReadyCached = false;
        }

        private void ResolveInterstitialAdInterval(float deltaTime)
        {
            switch (_intervalTrackingMode)
            {
                case IntervalTrackingMode.DeltaTime:
                    _interstitialTimer = Mathf.Min(_interstitialTimer + deltaTime, ShowInterstitialAdsInterval);
                    LogDebug(
                        "Tick(DeltaTime): dt={0:F3}s, timer={1:F1}s/{2}s",
                        deltaTime,
                        _interstitialTimer,
                        ShowInterstitialAdsInterval
                    );
                    break;
                case IntervalTrackingMode.TimePoint:
                    _checkIntervalAccumulator += deltaTime;
                    if (_checkIntervalAccumulator < _timePointCheckInterval) return;
                    _checkIntervalAccumulator -= _timePointCheckInterval;
                    _isInterstitialReadyCached =
                        (ChronoDirector.UtcNow - _lastAdShowTimePoint).TotalSeconds >= ShowInterstitialAdsInterval;
                    LogDebug(
                        "Tick(TimePoint): elapsed={0:F1}s/{1}s, ready={2}",
                        GetInterstitialProgressFormatted(),
                        ShowInterstitialAdsInterval,
                        _isInterstitialReadyCached
                    );
                    break;
            }
        }

        private void LogDebug(string message, params object[] parameters)
        {
            if (_verboseDebugging) QuickLog.Debug<AdsManagerBase<T>>(message, parameters);
        }

        private static float NormalizeTimeout(float timeout) =>
            float.IsNaN(timeout) || float.IsInfinity(timeout) || timeout <= 0f || timeout == float.MaxValue
                ? DefaultInitializationTimeout
                : timeout;

        private static float SanitizeDelta(float deltaTime) =>
            float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ? 0f : Mathf.Max(0f, deltaTime);
        #endregion
    }
}
