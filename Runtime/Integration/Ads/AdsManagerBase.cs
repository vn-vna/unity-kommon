using System;
using System.Collections;
using Com.Hapiga.Scheherazade.Common.Chrono;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
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
        #region Interfaces & Properties
        public string DeviceAdvertisingId => _provider != null ? _provider.DeviceAdvertisingId : string.Empty;
        public bool IsBannerAvailable => _provider != null && _provider.IsBannerAvailable;
        public bool IsInterstitialAdsAvailable => _provider != null && _provider.IsInterstitialAvailable;
        public bool IsRewardAdsAvailable => _provider != null && _provider.IsRewardedAvailable;
        public bool IsAppOpenAdsAvailable => _provider != null && _provider.IsOpenAppAdAvailable;
        public AdsManagerStatus Status { get; private set; } = AdsManagerStatus.Uninitialized;
        public int InterstitialAdCount { get; private set; }
        public int RewardAdCount { get; private set; }
        public int AppOpenAdCount { get; private set; }
        public float ShowInterstitialAdsInterval { get; set; } = 120.0f;
        public bool IsIntersitialAdsWillShow => 
            _intervalTrackingMode switch
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
        private float _timePointCheckInterval = 1.0f;

        [SerializeField]
        private bool _verboseDebugging;
        #endregion

        #region Private Fields
        private IAdsServiceProvider _provider;
        private float _interstitialTimer;
        private DateTime _lastAdShowTimePoint = DateTime.MinValue;
        private float _checkIntervalAccumulator;
        private bool _isInterstitialReadyCached;
        #endregion

        #region Lifecycle & Unity Methods
        protected override void OnEnable()
        {
            base.OnEnable();
            Integration.RegisterManager(this);
        }

        public virtual void Reset()
        {
            Status = AdsManagerStatus.Uninitialized;
            _lastAdShowTimePoint = DateTime.MinValue;
            _checkIntervalAccumulator = 0.0f;
            _isInterstitialReadyCached = false;

            OverrideConfiguration();
            ResetProvider();
        }

        public void Tick(float deltaTime)
        {
            if (_provider == null) return;
            _provider.LoadAds();
            ResolveInterstitialAdInterval(deltaTime);
        }
        #endregion

        #region Public Methods
        public void Initialize(float timeOut = float.MaxValue)
        {
            Dispatcher.DispatchCoroutine(InitializeCoroutine(timeOut));
        }

        public IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            Status = AdsManagerStatus.Initializing;

            if (_provider == null)
            {
                QuickLog.Error<AdsManagerBase<T>>(
                    "No provider registered. Cannot initialize AdsManager."
                );
                Status = AdsManagerStatus.Uninitialized;
                yield break;
            }

            _provider.Initialize();
            float timer = 0.0f;

            while (timer < timeOut && !(_provider != null && _provider.IsInitialized))
            {
                timer += Time.deltaTime;
                yield return null;
            }

            if (_provider != null && _provider.IsInitialized)
            {
                Status = AdsManagerStatus.Ready;
                if (_verboseDebugging)
                {
                    QuickLog.Debug<AdsManagerBase<T>>("Initialize: ready");
                }
            }
            else
            {
                Status = AdsManagerStatus.Uninitialized;
                if (_verboseDebugging)
                {
                    QuickLog.Debug<AdsManagerBase<T>>("Initialize: failed or timed out");
                }
            }
        }

        public void Shutdown()
        {
            if (_verboseDebugging)
            {
                QuickLog.Debug<AdsManagerBase<T>>("Shutdown");
            }

            if (_provider != null && _provider.IsInitialized)
            {
                _provider.CleanUp();
            }

            Status = AdsManagerStatus.Uninitialized;
        }

        public void RegisterProvider(IAdsServiceProvider provider)
        {
            if (_provider != null)
            {
                QuickLog.Warning<AdsManagerBase<T>>(
                    "AdsManager already has a provider registered. " +
                    "Overriding the existing one."
                );
            }
            _provider = provider;
            if (_verboseDebugging)
            {
                QuickLog.Debug<AdsManagerBase<T>>(
                    "RegisterProvider: {0}", provider?.GetType().Name);
            }
        }

        public virtual void ShowBanner()
        {
            if (_verboseDebugging)
            {
                QuickLog.Debug<AdsManagerBase<T>>("ShowBanner");
            }

            if (_provider != null)
            {
                _provider.ShowBanner();
            }
            else
            {
                QuickLog.Warning<AdsManagerBase<T>>(
                    "No provider registered."
                );
            }
        }

        public virtual void HideBanner()
        {
            if (_verboseDebugging)
            {
                QuickLog.Debug<AdsManagerBase<T>>("HideBanner");
            }

            if (_provider != null)
            {
                _provider.HideBanner();
            }
            else
            {
                QuickLog.Warning<AdsManagerBase<T>>(
                    "No provider registered."
                );
            }
        }

        public virtual void ShowInterstitialAds(Action<bool> callback, string placement, bool force = false)
        {
            if (_verboseDebugging)
            {
                QuickLog.Debug<AdsManagerBase<T>>(
                    "ShowInterstitialAds: placement={0}, force={1}, ready={2}", placement, force, IsIntersitialAdsWillShow);
            }

            if (!IsIntersitialAdsWillShow && !force)
            {
                QuickLog.Debug<AdsManagerBase<T>>(
                    "Interstitial ad request ignored. Interval not met. {0:F1} / {1}s",
                    GetInterstitialProgressFormatted(), ShowInterstitialAdsInterval);
                callback?.Invoke(false);
                return;
            }

            if (_provider != null)
            {
                _provider.ShowInterstitialAds(DebugCallback(callback, placement, "Interstitial"), placement);
                ResetIntersitialInterval();
                ++InterstitialAdCount;
            }
            else
            {
                QuickLog.Warning<AdsManagerBase<T>>(
                    "No provider registered."
                );
            }
        }

        public virtual void ShowRewardAds(Action<bool> callback, string placement)
        {
            if (_verboseDebugging)
            {
                QuickLog.Debug<AdsManagerBase<T>>(
                    "ShowRewardAds: placement={0}", placement);
            }

            if (_provider != null)
            {
                _provider.ShowRewardAds(DebugCallback(callback, placement, "Reward"), placement);
                ResetIntersitialInterval();
                ++RewardAdCount;
            }
            else
            {
                QuickLog.Warning<AdsManagerBase<T>>(
                    "No provider registered."
                );
            }
        }

        public virtual void ShowAppOpenAds(Action<bool> callback, string placement)
        {
            if (_verboseDebugging)
            {
                QuickLog.Debug<AdsManagerBase<T>>(
                    "ShowAppOpenAds: placement={0}", placement);
            }

            if (_provider != null)
            {
                _provider.ShowAppOpenAds(DebugCallback(callback, placement, "AppOpen"), placement);
                ++AppOpenAdCount;
            }
            else
            {
                QuickLog.Warning<AdsManagerBase<T>>("No provider registered.");
            }
        }
        #endregion

        #region Private Methods
        private void ResetProvider()
        {
            if (adServiceProvider == null) return;

            if (adServiceProvider is not IAdsServiceProvider provider)
            {
                QuickLog.Error<AdsManagerBase<T>>(
                    "Assigned ad service provider does not implement IAdsServiceProvider interface."
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

        private Action<bool> DebugCallback(Action<bool> callback, string placement, string adType)
        {
            if (!_verboseDebugging) return callback;
            return (success) =>
            {
                QuickLog.Debug<AdsManagerBase<T>>("{0} result: success={1}, placement={2}", adType, success, placement);
                callback?.Invoke(success);
            };
        }

        private float GetInterstitialProgressFormatted()
        {
            if (_intervalTrackingMode == IntervalTrackingMode.DeltaTime)
            {
                return _interstitialTimer;
            }

            return (float)(ChronoDirector.UtcNow - _lastAdShowTimePoint).TotalSeconds;
        }

        protected void ResetIntersitialInterval()
        {
            if (_intervalTrackingMode == IntervalTrackingMode.DeltaTime)
            {
                _interstitialTimer = 0.0f;
                return;
            }

            _lastAdShowTimePoint = ChronoDirector.UtcNow;
            _checkIntervalAccumulator = 0.0f;
            _isInterstitialReadyCached = false;
        }

        private void ResolveInterstitialAdInterval(float deltaTime)
        {
            switch (_intervalTrackingMode)
            {
                case IntervalTrackingMode.DeltaTime:
                    ResolveInterstitialAdIntervalDeltatime(deltaTime);
                    break;

                case IntervalTrackingMode.TimePoint:
                    ResolveInterstitialAdIntervalTimepoint(deltaTime);
                    break;
            }

        }

        private void ResolveInterstitialAdIntervalDeltatime(float deltaTime)
        {
            _interstitialTimer = Mathf.Min(_interstitialTimer + deltaTime, ShowInterstitialAdsInterval);
            if (_verboseDebugging)
            {
                QuickLog.Debug<AdsManagerBase<T>>(
                    "Tick(DeltaTime): dt={0:F3}s, timer={1:F1}s/{2}s",
                    deltaTime, _interstitialTimer, ShowInterstitialAdsInterval
                );
            }
        }

        private void ResolveInterstitialAdIntervalTimepoint(float deltaTime)
        {
            _checkIntervalAccumulator += deltaTime;
            if (_checkIntervalAccumulator < _timePointCheckInterval) return;

            _checkIntervalAccumulator -= _timePointCheckInterval;
            _isInterstitialReadyCached = (ChronoDirector.UtcNow - _lastAdShowTimePoint).TotalSeconds >= ShowInterstitialAdsInterval;

            if (_verboseDebugging)
            {
                QuickLog.Debug<AdsManagerBase<T>>(
                    "Tick(TimePoint): check, elapsed={0:F1}s/{1}s, ready={2}",
                    (Func<object>)(() => GetInterstitialProgressFormatted()),
                    ShowInterstitialAdsInterval, _isInterstitialReadyCached
                );
            }
        }
        #endregion

    }
}