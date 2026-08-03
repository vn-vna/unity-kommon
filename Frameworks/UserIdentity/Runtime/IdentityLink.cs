using System;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    /// <summary>
    /// A single identity a user has on one provider (e.g. a Google Play
    /// Services account, a Game Center account, or the local anonymous
    /// identity). <see cref="ProviderId"/> is the stable provider identifier
    /// (see <c>*Provider.ProviderIdValue</c> constants).
    /// </summary>
    [Serializable]
    public class IdentityLink
    {
        public string ProviderId;
        public string ProviderUserId;
        public string DisplayName;
        public DateTime LinkedAtUtc;

        public IdentityLink() { }

        public IdentityLink(
            string providerId,
            string providerUserId,
            string displayName)
        {
            ProviderId = providerId;
            ProviderUserId = providerUserId;
            DisplayName = displayName;
            LinkedAtUtc = DateTime.UtcNow;
        }
    }
}
