using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public interface IUserIdentityProvider
    {
        void SetUserId(string userId);
    }
}
