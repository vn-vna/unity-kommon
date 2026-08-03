using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    /// <summary>
    /// The canonical user: a stable local GUID plus every identity this user
    /// is linked to across providers. Persisted locally via
    /// <see cref="UserIdentityStorage"/>.
    /// </summary>
    [Serializable]
    public class UserProfile
    {
        public string CanonicalId;
        public List<IdentityLink> LinkedIdentities;
        public string DisplayName;
        public DateTime CreatedAtUtc;
        public DateTime LastSeenUtc;

        public UserProfile()
        {
            LinkedIdentities = new List<IdentityLink>();
        }

        public UserProfile(string canonicalId, string displayName)
        {
            CanonicalId = canonicalId;
            DisplayName = displayName;
            LinkedIdentities = new List<IdentityLink>();
            CreatedAtUtc = DateTime.UtcNow;
            LastSeenUtc = DateTime.UtcNow;
        }

        public bool IsLinkedTo(string providerId, string providerUserId)
        {
            if (string.IsNullOrEmpty(providerUserId)) return false;

            foreach (IdentityLink link in LinkedIdentities)
            {
                if (link.ProviderId == providerId
                    && link.ProviderUserId == providerUserId)
                {
                    return true;
                }
            }

            return false;
        }

        public IdentityLink FindLink(string providerId)
        {
            foreach (IdentityLink link in LinkedIdentities)
            {
                if (link.ProviderId == providerId)
                {
                    return link;
                }
            }

            return null;
        }

        public bool HasPlatformIdentity
        {
            get
            {
                foreach (IdentityLink link in LinkedIdentities)
                {
                    if (link.ProviderId != UserAnonymousIdentityProvider.ProviderIdValue)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void AddLink(IdentityLink link)
        {
            if (link == null) return;
            if (IsLinkedTo(link.ProviderId, link.ProviderUserId)) return;

            LinkedIdentities.Add(link);
            LastSeenUtc = DateTime.UtcNow;
        }
    }
}
