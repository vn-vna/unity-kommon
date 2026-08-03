using System;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    public enum UserIdentityStatus
    {
        Uninitialized,
        Anonymous,
        SigningIn,
        SignedIn,
        Error
    }

    /// <summary>
    /// Outcome of a sign-in attempt for a single provider. The provider is
    /// identified by its class type (e.g. <c>typeof(GoogleServiceIdentityProvider)</c>).
    /// </summary>
    public readonly struct SignInResult
    {
        public static readonly SignInResult Failure =
            new SignInResult(false, null, string.Empty);

        public bool Success { get; }
        public Type ProviderType { get; }
        public string Message { get; }

        public SignInResult(
            bool success,
            Type providerType,
            string message)
        {
            Success = success;
            ProviderType = providerType;
            Message = message;
        }
    }
}
