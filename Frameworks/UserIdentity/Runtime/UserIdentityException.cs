using System;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    public class UserIdentityException : Exception
    {
        public UserIdentityException(string message) : base(message) { }
        public UserIdentityException(string message, Exception inner) : base(message, inner) { }
    }

    public class UserIdentityNotInitializedException : UserIdentityException
    {
        public UserIdentityNotInitializedException()
            : base("User identity is not initialized. Call UserIdentityManager.Instance.InitializeAsync() first.") { }

        public UserIdentityNotInitializedException(string message)
            : base(message) { }
    }
}
