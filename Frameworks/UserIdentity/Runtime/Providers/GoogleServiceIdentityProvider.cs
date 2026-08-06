#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

using System;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    /// <summary>
    /// Google Play Services identity provider (GPGS). This is the game
    /// services account, not a Google SSO login. During
    /// <see cref="InitializeAsync"/> it performs a silent Google Services
    /// auto-login attempt and, when that fails and
    /// <see cref="AutoSignInOnInitialize"/> is enabled, forces the manual
    /// sign-in UI as a fallback.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GoogleServiceIdentityProvider",
        menuName = "Scheherazade/User Identity/Google Service Provider")]
    public class GoogleServiceIdentityProvider :
        ScriptableObject, IIdentityProvider
    {
        #region Constants

        public const string ProviderIdValue = "google_play_service";

        #endregion

        #region Serialized Fields

        [SerializeField]
        [HideInInspector]
        private string _providerId = ProviderIdValue;

        [SerializeField]
        [Tooltip(
            "Attempts a silent Google Services auto-login during " +
            "InitializeAsync and forces the manual sign-in UI when the " +
            "silent attempt fails.")]
        private bool _autoSignInOnInitialize = true;

        #endregion

        #region Interfaces & Properties

        /// <summary>
        /// When enabled, initialization performs a silent Google Services
        /// auto-login and falls back to the forced manual sign-in UI if the
        /// silent attempt fails. When disabled, initialization only reports
        /// the current authentication state.
        /// </summary>
        public bool AutoSignInOnInitialize => _autoSignInOnInitialize;

        #endregion

        #region IIdentityProvider

        public string ProviderId => _providerId;
        public bool RequiresLogin => true;

        public bool IsAuthenticated
        {
            get
            {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
                return PlayGamesPlatform.Instance != null
                    && PlayGamesPlatform.Instance.IsAuthenticated();
#else
                return false;
#endif
            }
        }

        public async Task<bool> InitializeAsync(CancellationToken ct = default)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            if (PlayGamesPlatform.Instance == null)
            {
                QuickLog.Warning<GoogleServiceIdentityProvider>(
                    "[{0}] PlayGamesPlatform not found. Not available.",
                    _providerId);
                return false;
            }

            if (PlayGamesPlatform.Instance.IsAuthenticated())
            {
                QuickLog.Debug<GoogleServiceIdentityProvider>(
                    "[{0}] Availability check: already authenticated.",
                    _providerId);
                return true;
            }

            if (!_autoSignInOnInitialize)
            {
                QuickLog.Debug<GoogleServiceIdentityProvider>(
                    "[{0}] Auto sign-in disabled; not authenticated.",
                    _providerId);
                return false;
            }

            return await RunAuthFlowAsync(
                forceUiOnSilentFailure: true,
                ct);
#else
            QuickLog.Info<GoogleServiceIdentityProvider>(
                "[{0}] Not available on this platform.",
                _providerId);
            return false;
#endif
        }

        public async Task<bool> SignInAsync(bool forceUi, CancellationToken ct = default)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            if (PlayGamesPlatform.Instance == null)
            {
                return false;
            }

            if (PlayGamesPlatform.Instance.IsAuthenticated())
            {
                return true;
            }

            if (!forceUi)
            {
                QuickLog.Debug<GoogleServiceIdentityProvider>(
                    "[{0}] Start auto login",
                    _providerId
                );

                TaskCompletionSource<bool> autoLoginTsc = new TaskCompletionSource<bool>();
                PlayGamesPlatform.Instance.Authenticate((status) => autoLoginTsc.SetResult(status == SignInStatus.Success));
                await autoLoginTsc.Task;

                if (autoLoginTsc.Task.Result)
                {
                    return true;
                }
            }

            QuickLog.Info<GoogleServiceIdentityProvider>(
                "[{0}] Opening Google Play Services sign-in flow...",
                _providerId);

            return await AttemptSignInAsync(
                useManualFlow: true,
                ct);
#else
            QuickLog.Warning<GoogleServiceIdentityProvider>(
                "[{0}] Sign-in unavailable on this platform.",
                _providerId);
            return false;
#endif
        }

        public Task<bool> SignOutAsync(CancellationToken ct = default)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            // This GPGS plugin version exposes no SignOut API. The user can
            // switch accounts through the platform account picker instead.
            QuickLog.Warning<GoogleServiceIdentityProvider>(
                "[{0}] This GPGS version has no programmatic sign-out. "
                + "Use the platform account switcher.",
                _providerId);
            return Task.FromResult(false);
#else
            return Task.FromResult(false);
#endif
        }

        public Task<IdentityLink> GetIdentityAsync(CancellationToken ct = default)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            if (!IsAuthenticated)
            {
                return Task.FromResult<IdentityLink>(null);
            }

            IdentityLink link = new IdentityLink(
                ProviderIdValue,
                PlayGamesPlatform.Instance.GetUserId(),
                PlayGamesPlatform.Instance.GetUserDisplayName());

            QuickLog.Debug<GoogleServiceIdentityProvider>(
                "[{0}] Identity: id='{1}', name='{2}'",
                _providerId, link.ProviderUserId, link.DisplayName);

            return Task.FromResult(link);
#else
            return Task.FromResult<IdentityLink>(null);
#endif
        }

        #endregion

        #region Private Methods

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
        private async Task<bool> RunAuthFlowAsync(
            bool forceUiOnSilentFailure,
            CancellationToken ct)
        {
            if (PlayGamesPlatform.Instance.IsAuthenticated())
            {
                return true;
            }

            bool silentSuccess = await AttemptSignInAsync(
                useManualFlow: false,
                ct);
            if (silentSuccess)
            {
                return true;
            }

            if (!forceUiOnSilentFailure)
            {
                QuickLog.Debug<GoogleServiceIdentityProvider>(
                    "[{0}] Silent auto-login failed; no forced UI requested.",
                    _providerId);
                return false;
            }

            QuickLog.Warning<GoogleServiceIdentityProvider>(
                "[{0}] Silent auto-login failed; forcing manual sign-in UI.",
                _providerId);
            return await AttemptSignInAsync(
                useManualFlow: true,
                ct);
        }

        private Task<bool> AttemptSignInAsync(
            bool useManualFlow,
            CancellationToken ct)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            ct.Register(() => tcs.TrySetCanceled(ct));

            Action<SignInStatus> callback = (status) =>
            {
                bool success = status == SignInStatus.Success;
                QuickLog.Debug<GoogleServiceIdentityProvider>(
                    "[{0}] Sign-in callback ({1}): status={2}",
                    _providerId,
                    useManualFlow ? "manual" : "silent",
                    status);
                tcs.TrySetResult(success);
            };

            try
            {
                if (useManualFlow)
                {
                    PlayGamesPlatform.Instance.ManuallyAuthenticate(callback);
                }
                else
                {
                    PlayGamesPlatform.Instance.Authenticate(callback);
                }
            }
            catch (Exception ex)
            {
                QuickLog.Error<GoogleServiceIdentityProvider>(
                    "[{0}] Sign-in threw synchronously: {1}",
                    _providerId, ex.Message);
                tcs.TrySetResult(false);
            }

            return tcs.Task;
        }
#endif

        #endregion
    }
}
