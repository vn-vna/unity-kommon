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
    /// services account, not a Google SSO login. Observes the authentication
    /// state already driven by the game bootstrap; it never triggers a
    /// sign-in prompt during <see cref="InitializeAsync"/>.
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

        public Task<bool> InitializeAsync(CancellationToken ct = default)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            if (PlayGamesPlatform.Instance == null)
            {
                QuickLog.Warning<GoogleServiceIdentityProvider>(
                    "[{0}] PlayGamesPlatform not found. Not available.",
                    _providerId);
                return Task.FromResult(false);
            }

            bool authed = PlayGamesPlatform.Instance.IsAuthenticated();
            QuickLog.Debug<GoogleServiceIdentityProvider>(
                "[{0}] Availability check: authenticated={1}",
                _providerId, authed);
            return Task.FromResult(authed);
#else
            QuickLog.Info<GoogleServiceIdentityProvider>(
                "[{0}] Not available on this platform.",
                _providerId);
            return Task.FromResult(false);
#endif
        }

        public Task<bool> SignInAsync(bool forceUi, CancellationToken ct = default)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            if (PlayGamesPlatform.Instance == null)
            {
                return Task.FromResult(false);
            }

            if (PlayGamesPlatform.Instance.IsAuthenticated())
            {
                return Task.FromResult(true);
            }

            if (!forceUi)
            {
                QuickLog.Debug<GoogleServiceIdentityProvider>(
                    "[{0}] No silent sign-in: not authenticated.",
                    _providerId);
                return Task.FromResult(false);
            }

            QuickLog.Info<GoogleServiceIdentityProvider>(
                "[{0}] Opening Google Play Services sign-in flow...",
                _providerId);

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            ct.Register(() => tcs.TrySetCanceled(ct));

            try
            {
                PlayGamesPlatform.Instance.ManuallyAuthenticate(
                    (status) =>
                    {
                        bool success = status == SignInStatus.Success;
                        QuickLog.Debug<GoogleServiceIdentityProvider>(
                            "[{0}] Sign-in callback: status={1}",
                            _providerId, status);
                        tcs.TrySetResult(success);
                    });
            }
            catch (Exception ex)
            {
                QuickLog.Error<GoogleServiceIdentityProvider>(
                    "[{0}] Sign-in threw synchronously: {1}",
                    _providerId, ex.Message);
                tcs.TrySetResult(false);
            }

            return tcs.Task;
#else
            QuickLog.Warning<GoogleServiceIdentityProvider>(
                "[{0}] Sign-in unavailable on this platform.",
                _providerId);
            return Task.FromResult(false);
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
    }
}
