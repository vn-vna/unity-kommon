using System.Threading;
using System.Threading.Tasks;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    /// <summary>
    /// A source of identity for the current user. Implementations are
    /// ScriptableObjects configured per-platform in
    /// <see cref="UserIdentityConfiguration"/>. The provider class itself is
    /// the identity type (e.g. <c>typeof(GoogleServiceIdentityProvider)</c>).
    /// </summary>
    public interface IIdentityProvider
    {
        /// <summary>Stable identifier of this provider (e.g. "google_play_service").</summary>
        string ProviderId { get; }

        /// <summary>True for platform accounts (Google Play Services, Game Center).</summary>
        bool RequiresLogin { get; }

        /// <summary>Current authentication state of this provider.</summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// Checks availability. Implementations must never trigger a login
        /// prompt here (the game owns when the user is asked to sign in).
        /// </summary>
        Task<bool> InitializeAsync(CancellationToken ct = default);

        /// <summary>
        /// Signs in. <paramref name="forceUi"/> requests the platform consent
        /// UI when needed; when false, returns the current state without
        /// prompting.
        /// </summary>
        Task<bool> SignInAsync(bool forceUi, CancellationToken ct = default);

        Task<bool> SignOutAsync(CancellationToken ct = default);

        /// <summary>
        /// Returns the current identity link, or null when not authenticated.
        /// The anonymous provider is handled by the manager and never queried.
        /// </summary>
        Task<IdentityLink> GetIdentityAsync(CancellationToken ct = default);
    }
}
