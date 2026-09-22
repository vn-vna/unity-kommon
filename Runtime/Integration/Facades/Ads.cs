using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    /// <summary>Static facade over the registered <see cref="IAdsManager"/>.</summary>
    public class Ads
    {
        #region Properties
        public static IAdsManager Manager => Integration.AdsManager;
        public static bool IsAvailable => Manager != null;
        public static AdsManagerStatus Status =>
            Manager != null ? Manager.Status : AdsManagerStatus.Uninitialized;
        public static bool IsReady => Status == AdsManagerStatus.Ready;
        public static bool IsBannerAvailable => Manager != null && Manager.IsBannerAvailable;
        public static AdsBannerState BannerState => Manager?.BannerState ??
            AdsBannerState.Unavailable("Ads manager is not registered.");
        public static Vector2 BannerSize => BannerState.Size;
        public static bool IsInterstitialAvailable => Manager != null && Manager.IsInterstitialAdsAvailable;
        public static bool IsRewardedAvailable => Manager != null && Manager.IsRewardAdsAvailable;
        public static bool IsAppOpenAvailable => Manager != null && Manager.IsAppOpenAdsAvailable;
        #endregion

        #region Initialization
        public static void Initialize(float timeOut = float.MaxValue)
        {
            if (TryGetManager(out IAdsManager manager)) manager.Initialize(timeOut);
        }

        public static IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            if (!TryGetManager(out IAdsManager manager)) yield break;
            IEnumerator steps = manager.InitializeCoroutine(timeOut);
            while (steps.MoveNext()) yield return steps.Current;
        }

        public static Task InitializeAsync(float timeOut = float.MaxValue, CancellationToken ct = default)
        {
            RequireManager();
            return CoroutineTaskBridge.RunAsync(InitializeCoroutine(timeOut));
        }
        #endregion

        #region Banner
        public static AdsInvocationHandler ShowBanner()
        {
            if (!TryGetManager(out IAdsManager manager))
                return MissingManager(AdsType.Banner, string.Empty);
            return InvokeSafely(AdsType.Banner, string.Empty, manager.ShowBanner);
        }

        public static AdsInvocationHandler HideBanner()
        {
            if (!TryGetManager(out IAdsManager manager))
                return MissingManager(AdsType.Banner, string.Empty);
            return InvokeSafely(AdsType.Banner, string.Empty, manager.HideBanner);
        }
        #endregion

        #region Interstitial
        public static AdsInvocationHandler ShowInterstitial(string placement, bool force = false)
        {
            if (!TryGetManager(out IAdsManager manager))
                return MissingManager(AdsType.Interstitial, placement);
            return InvokeSafely(
                AdsType.Interstitial,
                placement,
                () => manager.ShowInterstitialAds(placement, force)
            );
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
            IEnumerator steps = ObserveInvocationCoroutine(
                () => manager.ShowInterstitialAds(placement, force),
                IsSuccessful,
                AdsType.Interstitial,
                placement,
                onResult
            );
            while (steps.MoveNext()) yield return steps.Current;
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
                onResult => ObserveInvocationCoroutine(
                    () => manager.ShowInterstitialAds(placement, force),
                    IsSuccessful,
                    AdsType.Interstitial,
                    placement,
                    onResult,
                    timeoutSeconds,
                    () => ct.IsCancellationRequested
                ),
                0f, // The observing coroutine owns timeout so it always unsubscribes.
                ct
            );
        }
        #endregion

        #region Rewarded
        public static AdsInvocationHandler ShowRewarded(string placement)
        {
            if (!TryGetManager(out IAdsManager manager))
                return MissingManager(AdsType.Rewarded, placement);
            return InvokeSafely(
                AdsType.Rewarded,
                placement,
                () => manager.ShowRewardAds(placement)
            );
        }

        public static IEnumerator ShowRewardedCoroutine(Action<bool> onResult, string placement)
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                onResult?.Invoke(false);
                yield break;
            }
            IEnumerator steps = ObserveInvocationCoroutine(
                () => manager.ShowRewardAds(placement),
                IsRewardSuccessful,
                AdsType.Rewarded,
                placement,
                onResult
            );
            while (steps.MoveNext()) yield return steps.Current;
        }

        public static Task<bool> ShowRewardedAsync(
            string placement,
            float timeoutSeconds = 30f,
            CancellationToken ct = default
        )
        {
            IAdsManager manager = RequireManager();
            return CoroutineTaskBridge.RunWithCallbackAsync<bool>(
                onResult => ObserveInvocationCoroutine(
                    () => manager.ShowRewardAds(placement),
                    IsRewardSuccessful,
                    AdsType.Rewarded,
                    placement,
                    onResult,
                    timeoutSeconds,
                    () => ct.IsCancellationRequested
                ),
                0f, // The observing coroutine owns timeout so it always unsubscribes.
                ct
            );
        }
        #endregion

        #region App Open
        public static AdsInvocationHandler ShowAppOpen(string placement)
        {
            if (!TryGetManager(out IAdsManager manager))
                return MissingManager(AdsType.OpenApp, placement);
            return InvokeSafely(
                AdsType.OpenApp,
                placement,
                () => manager.ShowAppOpenAds(placement)
            );
        }

        public static IEnumerator ShowAppOpenCoroutine(Action<bool> onResult, string placement)
        {
            if (!TryGetManager(out IAdsManager manager))
            {
                onResult?.Invoke(false);
                yield break;
            }
            IEnumerator steps = ObserveInvocationCoroutine(
                () => manager.ShowAppOpenAds(placement),
                IsSuccessful,
                AdsType.OpenApp,
                placement,
                onResult
            );
            while (steps.MoveNext()) yield return steps.Current;
        }

        public static Task<bool> ShowAppOpenAsync(
            string placement,
            float timeoutSeconds = 30f,
            CancellationToken ct = default
        )
        {
            IAdsManager manager = RequireManager();
            return CoroutineTaskBridge.RunWithCallbackAsync<bool>(
                onResult => ObserveInvocationCoroutine(
                    () => manager.ShowAppOpenAds(placement),
                    IsSuccessful,
                    AdsType.OpenApp,
                    placement,
                    onResult,
                    timeoutSeconds,
                    () => ct.IsCancellationRequested
                ),
                0f, // The observing coroutine owns timeout so it always unsubscribes.
                ct
            );
        }
        #endregion

        #region Private Methods
        private static IEnumerator ObserveInvocationCoroutine(
            Func<AdsInvocationHandler> invoke,
            Func<AdsInvocationHandler, bool> successPredicate,
            AdsType type,
            string placement,
            Action<bool> onResult,
            float timeoutSeconds = 30f,
            Func<bool> isCancelled = null
        )
        {
            AdsInvocationHandler handler = InvokeSafely(type, placement, invoke);
            bool completed = false;
            bool success = false;
            IDisposable subscription = handler.Observe(current =>
            {
                if (!current.IsTerminal) return;
                success = successPredicate(current);
                completed = true;
            });

            float elapsed = 0f;
            try
            {
                while (!completed && elapsed < timeoutSeconds && !(isCancelled?.Invoke() ?? false))
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            finally { subscription.Dispose(); }

            if (!completed && !(isCancelled?.Invoke() ?? false))
            {
                QuickLog.Warning<Ads>(
                    "{0} ad timed out for placement '{1}'.",
                    type,
                    placement
                );
            }
            onResult?.Invoke(completed && success);
        }

        private static AdsInvocationHandler InvokeSafely(
            AdsType type,
            string placement,
            Func<AdsInvocationHandler> invoke
        )
        {
            try
            {
                AdsInvocationHandler handler = invoke();
                return handler ?? AdsInvocationHandler.Failed(
                    type,
                    placement,
                    "Ads manager returned no invocation handler."
                );
            }
            catch (Exception exception)
            {
                QuickLog.Error<Ads>(
                    "{0} ad invocation failed for placement '{1}': {2}",
                    type,
                    placement,
                    exception.Message
                );
                return AdsInvocationHandler.Failed(type, placement, exception.Message);
            }
        }

        private static bool IsSuccessful(AdsInvocationHandler handler) =>
            handler.Status == AdsInvocationStatus.Succeeded;

        private static bool IsRewardSuccessful(AdsInvocationHandler handler) =>
            handler.Status == AdsInvocationStatus.Succeeded && handler.RewardEarned;

        private static AdsInvocationHandler MissingManager(AdsType type, string placement) =>
            AdsInvocationHandler.Failed(type, placement, "Ads manager is not registered.");

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
                throw new IntegrationNotInitializedException(nameof(Ads));
            return manager;
        }
        #endregion
    }
}
