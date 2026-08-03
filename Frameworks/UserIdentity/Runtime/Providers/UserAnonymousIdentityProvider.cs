using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    /// <summary>
    /// Always-available anonymous identity. The user id is not owned by this
    /// provider: the manager seeds the anonymous link with the canonical id,
    /// so the anonymous identity and the canonical user are one and the same.
    /// </summary>
    [CreateAssetMenu(
        fileName = "UserAnonymousIdentityProvider",
        menuName = "Scheherazade/User Identity/Anonymous Provider")]
    public class UserAnonymousIdentityProvider :
        ScriptableObject, IIdentityProvider
    {
        #region Constants

        public const string ProviderIdValue = "anonymous";

        #endregion

        #region Serialized Fields

        [SerializeField]
        [HideInInspector]
        private string _providerId = ProviderIdValue;

        #endregion

        #region IIdentityProvider

        public string ProviderId => _providerId;
        public bool RequiresLogin => false;
        public bool IsAuthenticated => true;

        public Task<bool> InitializeAsync(CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> SignInAsync(bool forceUi, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> SignOutAsync(CancellationToken ct = default)
        {
            // The anonymous identity is the bottom layer of the profile and
            // is never removed.
            return Task.FromResult(true);
        }

        public Task<IdentityLink> GetIdentityAsync(CancellationToken ct = default)
        {
            // The manager owns the anonymous user id (it equals the canonical
            // profile id) and never queries this provider for it.
            return Task.FromResult<IdentityLink>(null);
        }

        #endregion
    }
}
