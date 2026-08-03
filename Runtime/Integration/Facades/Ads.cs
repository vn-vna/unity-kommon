using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    /// <summary>
    /// Static facade over the registered <see cref="IAdsManager"/>.
    /// Exposes query, fire-and-forget, coroutine and async APIs.
    /// </summary>
    public class Ads
    {
        #region Queries

        public static IAdsManager Manager => Integration.AdsManager;

        public static bool IsAvailable => Manager != null;

        public static AdsManagerStatus Status =>
            Manager != null ? Manager.Status : AdsManagerStatus.Uninitialized;

        public static bool IsReady => Status == AdsManagerStatus.Ready;

        public static bool IsBannerAvailable => Manager != null && Manager.IsBannerAvailable;

        public static bool IsInterstitialAvailable => Manager != null && Manager.IsInterstitialAdsAvailable;

        public static bool IsRewardedAvailable => Manager != null && Manager.IsRewardAdsAvailable;

        public static bool IsAppOpenAvailable => Manager != null && Manager.IsAppOpenAdsAvailable;

        #endregion

        #region Initialization

        public static void Initialize(float timeOut = float.MaxValue)
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                return;
            }

            manager.Initialize(timeOut);
        }

        public static IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                yield break;
            }

            IEnumerator steps = manager.InitializeCoroutine(timeOut);
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }

        public static Task InitializeAsync(float timeOut = float.MaxValue, CancellationToken ct = default)
        {
            RequireManager();
            return CoroutineTaskBridge.RunAsync(InitializeCoroutine(timeOut));
        }

        #endregion

        #region Banner (fire-and-forget)

        public static void ShowBanner()
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                return;
            }

            manager.ShowBanner();
        }

        public static void HideBanner()
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                return;
            }

            manager.HideBanner();
        }

        #endregion

        #region Interstitial

        public static void ShowInterstitial(Action<bool> onResult, string placement, bool force = false)
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                onResult?.Invoke(false);
                return;
            }

            Dispatcher.DispatchCoroutine(ShowInterstitialCoroutineImpl(manager, placement, force, onResult));
        }

        public static IEnumerator ShowInterstitialCoroutine(
            Action<bool> onResult,
            string placement,
            bool force = false
        )
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                onResult?.Invoke(false);
                yield break;
            }

            IEnumerator steps = ShowInterstitialCoroutineImpl(manager, placement, force, onResult);
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }

        public static Task<bool> ShowInterstitialAsync(
            string placement,
            bool force = false,
            float timeoutSeconds = 30f,
            CancellationToken ct = default
        )
        {
            IAdsManager manager = RequireManager();
            return CoroutineTaskBridge.RunWithCallbackAsync<bool>(
                onResult => ShowInterstitialCoroutineImpl(manager, placement, force, onResult, timeoutSeconds),
                timeoutSeconds,
                ct
            );
        }

        #endregion

        #region Rewarded

        public static void ShowRewarded(Action<bool> onResult, string placement)
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                onResult?.Invoke(false);
                return;
            }

            Dispatcher.DispatchCoroutine(ShowRewardedCoroutineImpl(manager, placement, onResult));
        }

        public static IEnumerator ShowRewardedCoroutine(Action<bool> onResult, string placement)
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                onResult?.Invoke(false);
                yield break;
            }

            IEnumerator steps = ShowRewardedCoroutineImpl(manager, placement, onResult);
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }

        public static Task<bool> ShowRewardedAsync(
            string placement,
            float timeoutSeconds = 30f,
            CancellationToken ct = default
        )
        {
            IAdsManager manager = RequireManager();
            return CoroutineTaskBridge.RunWithCallbackAsync<bool>(
                onResult => ShowRewardedCoroutineImpl(manager, placement, onResult, timeoutSeconds),
                timeoutSeconds,
                ct
            );
        }

        #endregion

        #region App Open

        public static void ShowAppOpen(Action<bool> onResult, string placement)
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                onResult?.Invoke(false);
                return;
            }

            Dispatcher.DispatchCoroutine(ShowAppOpenCoroutineImpl(manager, placement, onResult));
        }

        public static IEnumerator ShowAppOpenCoroutine(Action<bool> onResult, string placement)
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                onResult?.Invoke(false);
                yield break;
            }

            IEnumerator steps = ShowAppOpenCoroutineImpl(manager, placement, onResult);
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }

        public static Task<bool> ShowAppOpenAsync(
            string placement,
            float timeoutSeconds = 30f,
            CancellationToken ct = default
        )
        {
            IAdsManager manager = RequireManager();
            return CoroutineTaskBridge.RunWithCallbackAsync<bool>(
                onResult => ShowAppOpenCoroutineImpl(manager, placement, onResult, timeoutSeconds),
                timeoutSeconds,
                ct
            );
        }

        #endregion

        #region Private Methods

        private static IEnumerator ShowInterstitialCoroutineImpl(
            IAdsManager manager,
            string placement,
            bool force,
            Action<bool> onResult,
            float timeoutSeconds = 30f
        )
        {
            bool completed = false;
            bool success = false;
            float deadline = Time.time + timeoutSeconds;

            try
            {
                manager.ShowInterstitialAds(
                    result =>
                    {
                        success = result;
                        completed = true;
                    },
                    placement,
                    force
                );
            }
            catch (Exception ex)
            {
                QuickLog.Log("Show interstitial failed for placement '{0}': {1}", "Ads", LogLevel.Error, new object[] { placement, ex });
                onResult?.Invoke(false);
                yield break;
            }

            while (!completed && Time.time < deadline)
            {
                yield return null;
            }

            if (!completed)
            {
                QuickLog.Log("Show interstitial timed out for placement '{0}'.", "Ads", LogLevel.Warning, new object[] { placement });
            }

            onResult?.Invoke(success);
        }

        private static IEnumerator ShowRewardedCoroutineImpl(
            IAdsManager manager,
            string placement,
            Action<bool> onResult,
            float timeoutSeconds = 30f
        )
        {
            bool completed = false;
            bool success = false;
            float deadline = Time.time + timeoutSeconds;

            try
            {
                manager.ShowRewardAds(
                    result =>
                    {
                        success = result;
                        completed = true;
                    },
                    placement
                );
            }
            catch (Exception ex)
            {
                QuickLog.Log("Show rewarded failed for placement '{0}': {1}", "Ads", LogLevel.Error, new object[] { placement, ex });
                onResult?.Invoke(false);
                yield break;
            }

            while (!completed && Time.time < deadline)
            {
                yield return null;
            }

            if (!completed)
            {
                QuickLog.Log("Show rewarded timed out for placement '{0}'.", "Ads", LogLevel.Warning, new object[] { placement });
            }

            onResult?.Invoke(success);
        }

        private static IEnumerator ShowAppOpenCoroutineImpl(
            IAdsManager manager,
            string placement,
            Action<bool> onResult,
            float timeoutSeconds = 30f
        )
        {
            bool completed = false;
            bool success = false;
            float deadline = Time.time + timeoutSeconds;

            try
            {
                manager.ShowAppOpenAds(
                    result =>
                    {
                        success = result;
                        completed = true;
                    },
                    placement
                );
            }
            catch (Exception ex)
            {
                QuickLog.Log("Show app open failed for placement '{0}': {1}", "Ads", LogLevel.Error, new object[] { placement, ex });
                onResult?.Invoke(false);
                yield break;
            }

            while (!completed && Time.time < deadline)
            {
                yield return null;
            }

            if (!completed)
            {
                QuickLog.Log("Show app open timed out for placement '{0}'.", "Ads", LogLevel.Warning, new object[] { placement });
            }

            onResult?.Invoke(success);
        }

        private static bool TryGetManager(out IAdsManager manager)
        {
            manager = Integration.AdsManager;
            if (manager == null)
            {
                QuickLog.Warning<Ads>(
                    "Ads manager is not registered. Ensure the module is enabled in the IntegrationCentre."
                );
            }

            return manager != null;
        }

        private static IAdsManager RequireManager()
        {
            IAdsManager manager = Integration.RequireManager<IAdsManager>();
            if (manager.Status != AdsManagerStatus.Ready)
            {
                throw new IntegrationNotInitializedException(nameof(Ads));
            }

            return manager;
        }

        #endregion
    }
}
