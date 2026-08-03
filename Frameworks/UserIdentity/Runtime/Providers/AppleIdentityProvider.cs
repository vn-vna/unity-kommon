#if UNITY_IOS && APPLE_GAMEKIT
using Apple.Core.Runtime;
using Apple.GameKit;
#endif

using System;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    /// <summary>
    /// Apple Game Center identity provider. The GamePlayerId is used locally
    /// for linking and deduplication only; it must never be persisted to any
    /// external backend (Apple restriction).
    /// </summary>
    [CreateAssetMenu(
        fileName = "AppleIdentityProvider",
        menuName = "Scheherazade/User Identity/Apple Game Center Provider")]
    public class AppleIdentityProvider :
        ScriptableObject, IIdentityProvider
    {
        #region Constants

        public const string ProviderIdValue = "apple_game_center";

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
#if UNITY_IOS && APPLE_GAMEKIT
                return GKLocalPlayer.Local != null
                    && GKLocalPlayer.Local.IsAuthenticated;
#else
                return false;
#endif
            }
        }

        public Task<bool> InitializeAsync(CancellationToken ct = default)
        {
#if UNITY_IOS && APPLE_GAMEKIT
            bool authed = GKLocalPlayer.Local != null
                && GKLocalPlayer.Local.IsAuthenticated;
            QuickLog.Debug<AppleIdentityProvider>(
                "[{0}] Availability check: authenticated={1}",
                _providerId, authed);
            return Task.FromResult(authed);
#else
            QuickLog.Info<AppleIdentityProvider>(
                "[{0}] Not available on this platform.",
                _providerId);
            return Task.FromResult(false);
#endif
        }

        public async Task<bool> SignInAsync(bool forceUi, CancellationToken ct = default)
        {
#if UNITY_IOS && APPLE_GAMEKIT
            if (GKLocalPlayer.Local == null)
            {
                return false;
            }

            if (GKLocalPlayer.Local.IsAuthenticated)
            {
                return true;
            }

            if (!forceUi)
            {
                QuickLog.Debug<AppleIdentityProvider>(
                    "[{0}] No silent sign-in: not authenticated.",
                    _providerId);
                return false;
            }

            QuickLog.Info<AppleIdentityProvider>(
                "[{0}] Opening Game Center authentication...",
                _providerId);

            GKLocalPlayer.AuthenticateUpdate += HandleAuthUpdate;
            GKLocalPlayer.AuthenticateError += HandleAuthError;

            try
            {
                GKLocalPlayer player = await GKLocalPlayer.Authenticate();
                bool success = player != null && player.IsAuthenticated;
                QuickLog.Debug<AppleIdentityProvider>(
                    "[{0}] Authentication result: {1}",
                    _providerId, success);
                return success;
            }
            catch (Exception ex)
            {
                QuickLog.Warning<AppleIdentityProvider>(
                    "[{0}] Authentication threw: {1}",
                    _providerId, ex.Message);
                return false;
            }
            finally
            {
                GKLocalPlayer.AuthenticateUpdate -= HandleAuthUpdate;
                GKLocalPlayer.AuthenticateError -= HandleAuthError;
            }
#else
            QuickLog.Warning<AppleIdentityProvider>(
                "[{0}] Sign-in unavailable on this platform.",
                _providerId);
            return false;
#endif
        }

        public Task<bool> SignOutAsync(CancellationToken ct = default)
        {
            // Game Center has no programmatic sign-out; the user signs out
            // through iOS Settings.
            QuickLog.Warning<AppleIdentityProvider>(
                "[{0}] Game Center has no programmatic sign-out. "
                + "The user signs out in iOS Settings.",
                _providerId);
            return Task.FromResult(false);
        }

        public Task<IdentityLink> GetIdentityAsync(CancellationToken ct = default)
        {
#if UNITY_IOS && APPLE_GAMEKIT
            if (!IsAuthenticated || GKLocalPlayer.Local == null)
            {
                return Task.FromResult<IdentityLink>(null);
            }

            IdentityLink link = new IdentityLink(
                ProviderIdValue,
                GKLocalPlayer.Local.GamePlayerId
                    ?? GKLocalPlayer.Local.TeamPlayerId
                    ?? string.Empty,
                GKLocalPlayer.Local.DisplayName);

            QuickLog.Debug<AppleIdentityProvider>(
                "[{0}] Identity: id='{1}', name='{2}'",
                _providerId, link.ProviderUserId, link.DisplayName);

            return Task.FromResult(link);
#else
            return Task.FromResult<IdentityLink>(null);
#endif
        }

        #endregion

        #region Private Methods

#if UNITY_IOS && APPLE_GAMEKIT
        private void HandleAuthUpdate(GKLocalPlayer player)
        {
            QuickLog.Debug<AppleIdentityProvider>(
                "[{0}] Auth update: displayName='{1}'",
                _providerId, player?.DisplayName ?? "null");
        }

        private void HandleAuthError(NSError error)
        {
            QuickLog.Warning<AppleIdentityProvider>(
                "[{0}] Auth error: code={1}, description='{2}'",
                _providerId,
                error?.Code ?? -1,
                error?.LocalizedDescription ?? "null");
        }
#endif

        #endregion
    }
}
