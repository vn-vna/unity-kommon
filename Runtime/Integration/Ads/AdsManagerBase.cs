using System;
using System.Collections;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    public enum AdsManagerStatus
    {
        Uninitialized,
        Initializing,
        Ready
    }

    public abstract class AdsManagerBase<T> :
        SingletonBehavior<T>,
        IAdsManager
        where T : SingletonBehavior<T>
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
        public float InterstitialTimer {get { return _interstitialTimer; } set { _interstitialTimer = value; } }
        #endregion

        #region Serialized Fields
        [SerializeField]
        private AdsConfiguration overrideConfig;
        #endregion

        #region Private Fields
        private IAdsServiceProvider _provider;
        private float _interstitialTimer;
        #endregion

        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            Integration.RegisterManager(this);

            if (overrideConfig != null)
            {
                OverrideConfiguration();
            }
        }

        private void Update()
        {
            if (_provider == null)
            {
                return;
            }

            _provider.LoadAds();
            _interstitialTimer += Time.deltaTime;
        }
        #endregion

        #region Public Methods
        public void Initialize(float timeOut = float.MaxValue)
        {
            StartCoroutine(InitializeCoroutine(timeOut));
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
            }
            else
            {
                Status = AdsManagerStatus.Uninitialized;
            }
        }

        public void Shutdown()
        {
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
        }

        public virtual void ShowBanner()
        {
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

        public virtual void ShowInterstitialAds(Action<bool> callback, string placement)
        {
            if (_interstitialTimer < ShowInterstitialAdsInterval)
            {
                Debug.Log($"Interstitial ad request ignored. Interval not met. {_interstitialTimer}/{ShowInterstitialAdsInterval}");
                callback?.Invoke(false);
                return;
            }

            if (_provider != null)
            {
                _provider.ShowInterstitialAds(callback, placement);
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
            if (_provider != null)
            {
                switch (overrideConfig.InterAdsIntervalResetType)
                {
                    case InterResetType.OnRewardedAdsShow:
                        ResetIntersitialInterval();
                        break;
                    case InterResetType.OnRewardedAdsComplete:
                        callback += ResetIntersitialInterval;
                        break;
                }
                _provider.ShowRewardAds(callback, placement);
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
            if (_provider != null)
            {
                _provider.ShowAppOpenAds(callback, placement);
                ++AppOpenAdCount;
            }
            else
            {
                Debug.LogWarning("AdsManager: No provider registered.");
            }
        }
        #endregion

        #region Private Methods
        private void OverrideConfiguration()
        {
            ShowInterstitialAdsInterval = overrideConfig.ShowInterstitialAdsInterval;
        }

        protected void ResetIntersitialInterval()
        {
            QuickLog.Debug<AdsManagerBase<T>>("Resetting interstitial ads interval timer.");
            _interstitialTimer = 0.0f;
        }

        protected void ResetIntersitialInterval(bool adsAvailable)
        {
            ResetIntersitialInterval();
        }

        #endregion

    }
}