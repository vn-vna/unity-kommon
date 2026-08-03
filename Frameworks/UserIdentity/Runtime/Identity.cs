using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    /// <summary>
    /// Static facade over <see cref="UserIdentityManager"/>. Mirrors the
    /// <c>Leaderboard</c> facade pattern: safe property access, async API
    /// with readiness guards, coroutine versions and fire-and-forget
    /// helpers for callbacks. Providers are identified by their class type
    /// (e.g. <c>typeof(GoogleServiceIdentityProvider)</c>); the parameterless
    /// sign-in triggers every login-required provider.
    /// </summary>
    public static class Identity
    {
        #region Properties

        public static UserIdentityStatus Status =>
            Manager?.Status ?? UserIdentityStatus.Uninitialized;

        public static bool IsInitialized
        {
            get
            {
                UserIdentityManager manager = Manager;
                return manager != null
                    && manager.Status != UserIdentityStatus.Uninitialized
                    && manager.Status != UserIdentityStatus.Error;
            }
        }

        public static bool IsSignedIn => Manager?.IsSignedIn ?? false;

        public static string CanonicalId => Manager?.CanonicalId ?? string.Empty;

        public static string DisplayName => Manager?.ResolveDisplayName() ?? "Player";

        public static UserProfile CurrentUser => Manager?.CurrentUser;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the identity module. Safe to call multiple times;
        /// returns false when no configuration exists.
        /// </summary>
        public static async Task<bool> InitializeAsync(
            CancellationToken ct = default)
        {
            UserIdentityManager manager = Manager;
            if (manager == null)
            {
                QuickLog.SError(
                    "User identity is not available. Ensure a "
                    + "UserIdentityConfiguration exists.");
                return false;
            }

            return await manager.InitializeAsync(ct);
        }

        #endregion

        #region Async API

        /// <summary>Signs in to every login-required provider (list order).</summary>
        public static async Task<IReadOnlyList<SignInResult>> SignInAsync(
            bool forceUi = true,
            CancellationToken ct = default)
        {
            UserIdentityManager manager = EnsureReady();
            return await manager.SignInAllAsync(forceUi, ct);
        }

        /// <summary>
        /// Explicitly signs in to the provider of the given class type
        /// (e.g. <c>typeof(GoogleServiceIdentityProvider)</c>).
        /// </summary>
        public static async Task<SignInResult> SignInAsync(
            Type providerType,
            bool forceUi = true,
            CancellationToken ct = default)
        {
            UserIdentityManager manager = EnsureReady();
            return await manager.SignInAsync(providerType, forceUi, ct);
        }

        public static async Task<bool> SignOutAsync(
            Type providerType,
            CancellationToken ct = default)
        {
            UserIdentityManager manager = EnsureReady();
            return await manager.SignOutAsync(providerType, ct);
        }

        /// <summary>True if the profile carries a link with the given identity.</summary>
        public static bool IsLinkedTo(string providerId, string providerUserId)
        {
            return Manager?.IsLinkedTo(providerId, providerUserId) ?? false;
        }

        /// <summary>Type-based convenience; resolves the provider's id first.</summary>
        public static bool IsLinkedTo(Type providerType, string providerUserId)
        {
            return Manager?.IsLinkedTo(providerType, providerUserId) ?? false;
        }

        public static IdentityLink FindLink(string providerId)
        {
            return Manager?.FindLink(providerId);
        }

        /// <summary>All linked identities (for leaderboard deduplication).</summary>
        public static IReadOnlyList<IdentityLink> GetLinkedIdentities()
        {
            return Manager?.GetLinkedIdentities()
                ?? (IReadOnlyList<IdentityLink>)Array.Empty<IdentityLink>();
        }

        /// <summary>Configured providers for the current platform, in list order.</summary>
        public static IReadOnlyList<IIdentityProvider> GetProviders()
        {
            return Manager?.GetProviders()
                ?? (IReadOnlyList<IIdentityProvider>)Array.Empty<IIdentityProvider>();
        }

        #endregion

        #region Coroutine API

        /// <summary>
        /// Coroutine version of <see cref="InitializeAsync"/>. Dispatch via
        /// <c>coroutine.DispatchOnDispatcher()</c> or any MonoBehaviour.
        /// </summary>
        public static IEnumerator InitializeCoroutine(CancellationToken ct = default)
        {
            yield return WaitForTask(InitializeAsync(ct));
        }

        /// <summary>
        /// Coroutine version of <see cref="SignInAsync(bool, CancellationToken)"/>
        /// (all providers); invokes <paramref name="onResult"/> with the
        /// per-provider outcomes.
        /// </summary>
        public static IEnumerator SignInCoroutine(
            bool forceUi = true,
            Action<IReadOnlyList<SignInResult>> onResult = null,
            CancellationToken ct = default)
        {
            Task<IReadOnlyList<SignInResult>> task;
            try
            {
                task = SignInAsync(forceUi, ct);
            }
            catch (Exception ex)
            {
                QuickLog.SCritical(
                    "Sign-in failed due to exception: {0}", ex);
                onResult?.Invoke(Array.Empty<SignInResult>());
                yield break;
            }

            yield return WaitForTask(task);

            try
            {
                onResult?.Invoke(task.GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                QuickLog.SCritical(
                    "Sign-in failed due to exception: {0}", ex);
                onResult?.Invoke(Array.Empty<SignInResult>());
            }
        }

        /// <summary>
        /// Coroutine version of <see cref="SignInAsync(Type, bool, CancellationToken)"/>
        /// (explicit provider class); invokes <paramref name="onResult"/>.
        /// </summary>
        public static IEnumerator SignInCoroutine(
            Type providerType,
            bool forceUi = true,
            Action<SignInResult> onResult = null,
            CancellationToken ct = default)
        {
            Task<SignInResult> task;
            try
            {
                task = SignInAsync(providerType, forceUi, ct);
            }
            catch (Exception ex)
            {
                QuickLog.SCritical(
                    "Sign-in to {0} failed due to exception: {1}",
                    providerType, ex
                );
                onResult?.Invoke(new SignInResult(
                    false, providerType, ex.Message));
                yield break;
            }

            yield return WaitForTask(task);

            try
            {
                onResult?.Invoke(task.GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                QuickLog.SCritical(
                    "Sign-in to {0} failed due to exception: {1}",
                    providerType, ex
                );
                onResult?.Invoke(new SignInResult(
                    false, providerType, ex.Message));
            }
        }

        /// <summary>
        /// Coroutine version of <see cref="SignOutAsync"/>; invokes
        /// <paramref name="onResult"/> with the outcome.
        /// </summary>
        public static IEnumerator SignOutCoroutine(
            Type providerType,
            Action<bool> onResult = null,
            CancellationToken ct = default)
        {
            Task<bool> task;
            try
            {
                task = SignOutAsync(providerType, ct);
            }
            catch (Exception ex)
            {
                QuickLog.SCritical(
                    "Sign-out from {0} failed due to exception: {1}",
                    providerType, ex
                );
                onResult?.Invoke(false);
                yield break;
            }

            yield return WaitForTask(task);

            try
            {
                onResult?.Invoke(task.GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                QuickLog.SCritical(
                    "Sign-out from {0} failed due to exception: {1}",
                    providerType, ex
                );
                onResult?.Invoke(false);
            }
        }

        #endregion

        #region Fire-and-Forget

        public static async void SignIn(
            bool forceUi = true,
            Action<IReadOnlyList<SignInResult>> onResult = null)
        {
            try
            {
                IReadOnlyList<SignInResult> results = await SignInAsync(forceUi);
                onResult?.Invoke(results);
            }
            catch (Exception ex)
            {
                QuickLog.SCritical(
                    "Sign-in failed due to exception: {0}", ex);
                onResult?.Invoke(Array.Empty<SignInResult>());
            }
        }

        public static async void SignIn(
            Type providerType,
            bool forceUi = true,
            Action<SignInResult> onResult = null)
        {
            try
            {
                SignInResult result = await SignInAsync(providerType, forceUi);
                onResult?.Invoke(result);
            }
            catch (Exception ex)
            {
                QuickLog.SCritical(
                    "Sign-in to {0} failed due to exception: {1}",
                    providerType, ex
                );
                onResult?.Invoke(new SignInResult(
                    false, providerType, ex.Message));
            }
        }

        public static async void SignOut(
            Type providerType,
            Action<bool> onResult = null)
        {
            try
            {
                bool success = await SignOutAsync(providerType);
                onResult?.Invoke(success);
            }
            catch (Exception ex)
            {
                QuickLog.SCritical(
                    "Sign-out from {0} failed due to exception: {1}",
                    providerType, ex
                );
                onResult?.Invoke(false);
            }
        }

        #endregion

        #region Private Methods

        private static UserIdentityManager Manager => UserIdentityManager.Instance;

        private static IEnumerator WaitForTask(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted || task.IsCanceled)
            {
                task.GetAwaiter().GetResult();
            }
        }

        private static UserIdentityManager EnsureReady()
        {
            UserIdentityManager manager = Manager;
            if (manager == null)
            {
                throw new UserIdentityNotInitializedException(
                    "UserIdentityManager is not available. Ensure a "
                    + "UserIdentityConfiguration exists.");
            }

            if (manager.Status == UserIdentityStatus.Uninitialized
                || manager.Status == UserIdentityStatus.Error)
            {
                throw new UserIdentityNotInitializedException();
            }

            return manager;
        }

        #endregion
    }
}
