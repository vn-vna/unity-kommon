#if APPLOVIN_MAX

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Com.Scheherazade.Common.Integration.Ads;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.Editor
{
    /// <summary>Deterministic callback-order checks that never initialize or call the MAX SDK.</summary>
    public static class ApplovinMaxAtomicOperationSelfTests
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Scheherazade/Ads/Run MAX Atomic Operation Self Tests")]
        public static void RunAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run MAX atomic operation tests outside Play Mode.");

            var checks = new Action[]
            {
                DuplicateShowIsRejectedUntilTerminalCallback,
                ConcurrentBeginAllowsExactlyOneOperation,
                RewardAndHiddenCallbacksCompleteExactlyOnce,
                HiddenWithoutRewardCancelsAndReleases,
                MismatchedCallbacksCannotResolveCurrentOperation,
                DisplayFailureReleasesCurrentOperation
            };
            var failures = new List<string>();
            foreach (Action check in checks)
            {
                try { check(); }
                catch (Exception exception)
                {
                    failures.Add(check.Method.Name + ": " + exception.GetBaseException().Message);
                }
            }

            if (failures.Count > 0)
                throw new InvalidOperationException(
                    "MAX atomic operation self tests failed:\n" + string.Join("\n", failures)
                );

            Debug.Log($"[Sand Ads] MAX atomic operation self tests passed: {checks.Length}/{checks.Length}.");
        }

        private static void DuplicateShowIsRejectedUntilTerminalCallback() => WithProvider(provider =>
        {
            AdsInvocationHandler first = Begin(provider, AdsType.Rewarded, "reward", "reward-unit");
            AdsInvocationHandler duplicate = Begin(provider, AdsType.Rewarded, "duplicate", "reward-unit");

            Require(first.Status == AdsInvocationStatus.Pending,
                "The first operation must remain pending until MAX sends callbacks.");
            Require(duplicate.Status == AdsInvocationStatus.Failed,
                "A duplicate fullscreen call must fail without replacing the active operation.");

            Invoke(provider, "MarkFullscreenHidden", AdsType.Rewarded, "reward-unit", "reward");
            AdsInvocationHandler next = Begin(provider, AdsType.Rewarded, "next", "reward-unit");
            Require(next.Status == AdsInvocationStatus.Pending,
                "A new operation must be accepted after the active operation reaches a terminal callback.");
        });

        private static void ConcurrentBeginAllowsExactlyOneOperation() => WithProvider(provider =>
        {
            var results = new AdsInvocationHandler[16];
            Parallel.For(0, results.Length, index =>
            {
                results[index] = Begin(provider, AdsType.Rewarded, "reward", "reward-unit");
            });

            int pending = 0;
            int failed = 0;
            foreach (AdsInvocationHandler result in results)
            {
                if (result.Status == AdsInvocationStatus.Pending) pending++;
                if (result.Status == AdsInvocationStatus.Failed) failed++;
            }

            Require(pending == 1 && failed == results.Length - 1,
                "Concurrent calls must atomically create one operation and reject every duplicate.");
        });

        private static void RewardAndHiddenCallbacksCompleteExactlyOnce() => WithProvider(provider =>
        {
            AdsInvocationHandler handler = Begin(provider, AdsType.Rewarded, "reward", "reward-unit");
            Invoke(provider, "MarkFullscreenDisplayed", AdsType.Rewarded, "reward-unit", "reward");
            Invoke(provider, "MarkRewardEarned", "reward-unit", "reward");
            Invoke(provider, "MarkRewardEarned", "reward-unit", "reward");
            Invoke(provider, "MarkFullscreenHidden", AdsType.Rewarded, "reward-unit", "reward");
            Invoke(provider, "MarkFullscreenHidden", AdsType.Rewarded, "reward-unit", "reward");

            Require(handler.Status == AdsInvocationStatus.Succeeded && handler.WasDisplayed &&
                    handler.WasClosed && handler.RewardEarned,
                "MAX displayed, reward, and hidden callbacks must produce one successful operation.");
        });

        private static void HiddenWithoutRewardCancelsAndReleases() => WithProvider(provider =>
        {
            AdsInvocationHandler handler = Begin(provider, AdsType.Rewarded, "reward", "reward-unit");
            Invoke(provider, "MarkFullscreenDisplayed", AdsType.Rewarded, "reward-unit", "reward");
            Invoke(provider, "MarkFullscreenHidden", AdsType.Rewarded, "reward-unit", "reward");
            Invoke(provider, "MarkRewardEarned", "reward-unit", "reward");

            Require(handler.Status == AdsInvocationStatus.Cancelled && handler.WasClosed &&
                    !handler.RewardEarned,
                "A hidden rewarded ad without MAX reward confirmation must cancel and ignore late callbacks.");
            Require(Begin(provider, AdsType.Rewarded, "next", "reward-unit").Status ==
                    AdsInvocationStatus.Pending,
                "Cancellation must release the operation for the next ad call.");
        });

        private static void MismatchedCallbacksCannotResolveCurrentOperation() => WithProvider(provider =>
        {
            AdsInvocationHandler handler = Begin(provider, AdsType.Interstitial, "inter", "inter-unit");
            Invoke(provider, "MarkFullscreenDisplayed", AdsType.Interstitial, "wrong-unit", "inter");
            Invoke(provider, "MarkFullscreenDisplayed", AdsType.Interstitial, "inter-unit", "wrong-placement");
            Require(handler.Status == AdsInvocationStatus.Pending,
                "Callbacks for another unit or placement must not mutate the active operation.");

            Invoke(provider, "MarkFullscreenDisplayed", AdsType.Interstitial, "inter-unit", "inter");
            Invoke(provider, "MarkFullscreenHidden", AdsType.Interstitial, "inter-unit", "inter");
            Require(handler.Status == AdsInvocationStatus.Succeeded && handler.WasClosed,
                "Matching MAX callbacks must complete the active operation.");
        });

        private static void DisplayFailureReleasesCurrentOperation() => WithProvider(provider =>
        {
            AdsInvocationHandler handler = Begin(provider, AdsType.OpenApp, "open", "open-unit");
            Invoke(
                provider,
                "FailFullscreenOperation",
                AdsType.OpenApp,
                "open-unit",
                "open",
                "display failed"
            );

            Require(handler.Status == AdsInvocationStatus.Failed,
                "A matching MAX display-failed callback must fail the operation.");
            Require(Begin(provider, AdsType.OpenApp, "next", "open-unit").Status ==
                    AdsInvocationStatus.Pending,
                "Display failure must release the operation for the next ad call.");
        });

        private static AdsInvocationHandler Begin(
            ApplovinMaxAdsServiceProvider provider,
            AdsType type,
            string placement,
            string unitId)
        {
            return (AdsInvocationHandler)Invoke(
                provider,
                "BeginAtomicFullscreenOperation",
                type,
                placement,
                unitId
            );
        }

        private static object Invoke(
            ApplovinMaxAdsServiceProvider provider,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = typeof(ApplovinMaxAdsServiceProvider).GetMethod(
                methodName,
                InstanceMembers
            );
            if (method == null)
                throw new MissingMethodException(typeof(ApplovinMaxAdsServiceProvider).FullName, methodName);
            return method.Invoke(provider, arguments);
        }

        private static void WithProvider(Action<ApplovinMaxAdsServiceProvider> check)
        {
            var provider = ScriptableObject.CreateInstance<ApplovinMaxAdsServiceProvider>();
            provider.hideFlags = HideFlags.HideAndDontSave;
            try { check(provider); }
            finally { UnityEngine.Object.DestroyImmediate(provider); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

#endif
