using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    /// <summary>
    /// Orchestrates identity providers, the canonical user profile, linking
    /// and persistence. Providers are identified by their class type
    /// (e.g. <c>typeof(GoogleServiceIdentityProvider)</c>). The anonymous
    /// identity is always the bottom layer: if no anonymous provider is
    /// configured, one is added implicitly.
    /// </summary>
    [DontDestroyOnLoad]
    public class UserIdentityManager : SingletonBehavior<UserIdentityManager>
    {
        #region Events & Delegates

        public event Action<UserProfile> IdentityChanged;
        public event Action<UserIdentityStatus> StatusChanged;

        #endregion

        #region Properties

        public UserProfile CurrentUser { get; private set; }

        public UserIdentityStatus Status { get; private set; } =
            UserIdentityStatus.Uninitialized;

        public string CanonicalId => CurrentUser?.CanonicalId ?? string.Empty;

        /// <summary>True when at least one login-required provider is authenticated.</summary>
        public bool IsSignedIn =>
            _providers.Any(provider => provider.RequiresLogin && provider.IsAuthenticated);

        #endregion

        #region Private Fields

        private IReadOnlyList<IIdentityProvider> _providers =
            Array.Empty<IIdentityProvider>();

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            UserIdentityConfiguration config = UserIdentityConfiguration.Instance;

            if (config == null)
            {
                QuickLog.Info<UserIdentityManager>(
                    "User identity disabled — no configuration found."
                );
                return;
            }

            var go = new GameObject("[Scheherazade User Identity]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<UserIdentityManager>();
            go.AddComponent<KeepAliveComponent>();
        }

        #endregion

        #region Unity Callbacks

        protected override void Awake()
        {
            base.Awake();
        }

        #endregion

        #region Public Methods

        public async Task<bool> InitializeAsync(CancellationToken ct = default)
        {
            UserIdentityConfiguration config = UserIdentityConfiguration.Instance;
            if (config == null)
            {
                QuickLog.Error<UserIdentityManager>(
                    "No UserIdentityConfiguration found. Cannot initialize.");
                SetStatus(UserIdentityStatus.Error);
                return false;
            }

            _providers = ResolveProviders(config);

            CurrentUser = UserIdentityStorage.Load();
            if (CurrentUser == null)
            {
                CurrentUser = CreateAnonymousProfile(config);
                SaveProfile();
                QuickLog.Info<UserIdentityManager>(
                    "Created anonymous profile: canonicalId='{0}'",
                    CurrentUser.CanonicalId);
            }

            foreach (IIdentityProvider provider in _providers)
            {
                bool available = false;
                try
                {
                    available = await provider.InitializeAsync(ct);
                }
                catch (Exception ex)
                {
                    QuickLog.Error<UserIdentityManager>(
                        "Provider '{0}' initialization threw: {1}",
                        provider.ProviderId, ex.Message);
                }

                QuickLog.Info<UserIdentityManager>(
                    "Provider '{0}' initialized: available={1}",
                    provider.ProviderId, available);
            }

            if (config.AutoLinkAuthenticatedOnInit)
            {
                await AutoLinkAuthenticatedAsync(ct);
            }

            RefreshStatus();
            return true;
        }

        /// <summary>
        /// Signs in to every login-required provider (list order), linking
        /// each successful identity. Returns one result per provider.
        /// </summary>
        public async Task<IReadOnlyList<SignInResult>> SignInAllAsync(
            bool forceUi = true,
            CancellationToken ct = default)
        {
            var results = new List<SignInResult>();

            foreach (IIdentityProvider provider in _providers)
            {
                if (!provider.RequiresLogin) continue;

                SignInResult result = await SignInProviderAsync(provider, forceUi, ct);
                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Explicitly signs in to the provider of the given class type
        /// (e.g. <c>typeof(GoogleServiceIdentityProvider)</c>).
        /// </summary>
        public async Task<SignInResult> SignInAsync(
            Type providerType,
            bool forceUi = true,
            CancellationToken ct = default)
        {
            if (providerType == null
                || !typeof(IIdentityProvider).IsAssignableFrom(providerType))
            {
                return new SignInResult(
                    false, providerType,
                    $"'{providerType?.Name}' is not an IIdentityProvider type.");
            }

            IIdentityProvider provider = FindProvider(providerType);
            if (provider == null)
            {
                return new SignInResult(
                    false, providerType,
                    $"No provider of type '{providerType.Name}' is configured.");
            }

            return await SignInProviderAsync(provider, forceUi, ct);
        }

        public async Task<bool> SignOutAsync(
            Type providerType,
            CancellationToken ct = default)
        {
            IIdentityProvider provider = FindProvider(providerType);
            if (provider == null)
            {
                return false;
            }

            bool success = await provider.SignOutAsync(ct);
            if (success)
            {
                // Links are kept for deduplication history; the profile
                // simply degrades to anonymous until the next sign-in.
                SaveProfile();
            }

            RefreshStatus();
            return success;
        }

        /// <summary>True if the profile carries a link with the given identity.</summary>
        public bool IsLinkedTo(string providerId, string providerUserId)
        {
            return CurrentUser != null
                && CurrentUser.IsLinkedTo(providerId, providerUserId);
        }

        /// <summary>Type-based convenience; resolves the provider's id first.</summary>
        public bool IsLinkedTo(Type providerType, string providerUserId)
        {
            IIdentityProvider provider = FindProvider(providerType);
            return provider != null
                && IsLinkedTo(provider.ProviderId, providerUserId);
        }

        public IdentityLink FindLink(string providerId)
        {
            return CurrentUser?.FindLink(providerId);
        }

        /// <summary>All linked identities (for leaderboard deduplication).</summary>
        public IReadOnlyList<IdentityLink> GetLinkedIdentities()
        {
            return CurrentUser?.LinkedIdentities
                ?? (IReadOnlyList<IdentityLink>)Array.Empty<IdentityLink>();
        }

        /// <summary>Configured providers for the current platform, in list order.</summary>
        public IReadOnlyList<IIdentityProvider> GetProviders()
        {
            return _providers;
        }

        /// <summary>
        /// Best display name by provider list order (index 0 first), falling
        /// back to the profile default.
        /// </summary>
        public string ResolveDisplayName()
        {
            foreach (IIdentityProvider provider in _providers)
            {
                if (provider.ProviderId == UserAnonymousIdentityProvider.ProviderIdValue)
                {
                    continue;
                }

                IdentityLink link = CurrentUser?.FindLink(provider.ProviderId);
                if (link != null && !string.IsNullOrEmpty(link.DisplayName))
                {
                    return link.DisplayName;
                }
            }

            string fallback = CurrentUser?.DisplayName ?? string.Empty;
            return string.IsNullOrEmpty(fallback) ? "Player" : fallback;
        }

        #endregion

        #region Private Methods

        private async Task<SignInResult> SignInProviderAsync(
            IIdentityProvider provider,
            bool forceUi,
            CancellationToken ct)
        {
            SetStatus(UserIdentityStatus.SigningIn);

            bool success;
            try
            {
                success = await provider.SignInAsync(forceUi, ct);
            }
            catch (Exception ex)
            {
                QuickLog.Error<UserIdentityManager>(
                    "Sign-in to '{0}' threw: {1}",
                    provider.ProviderId, ex.Message);
                success = false;
            }

            if (!success)
            {
                RefreshStatus();
                return new SignInResult(
                    false, provider.GetType(),
                    $"Sign-in to '{provider.ProviderId}' failed.");
            }

            IdentityLink link = await provider.GetIdentityAsync(ct);
            if (link == null || string.IsNullOrEmpty(link.ProviderUserId))
            {
                RefreshStatus();
                return new SignInResult(
                    false, provider.GetType(),
                    $"Sign-in to '{provider.ProviderId}' returned no identity.");
            }

            await ApplyLinkAsync(link);
            RefreshStatus();

            QuickLog.Info<UserIdentityManager>(
                "Signed in as '{0}' via '{1}' (canonical '{2}').",
                link.DisplayName, provider.ProviderId, CanonicalId);

            return new SignInResult(true, provider.GetType(), link.DisplayName);
        }

        private IReadOnlyList<IIdentityProvider> ResolveProviders(
            UserIdentityConfiguration config)
        {
            var providers = new List<IIdentityProvider>(config.Providers);

            if (!providers.Any(provider =>
                    provider.ProviderId == UserAnonymousIdentityProvider.ProviderIdValue))
            {
                providers.Add(
                    ScriptableObject.CreateInstance<UserAnonymousIdentityProvider>());

                QuickLog.Debug<UserIdentityManager>(
                    "No anonymous provider configured; added implicit "
                    + "fallback at the end of the list (lowest priority).");
            }

            return providers;
        }

        private UserProfile CreateAnonymousProfile(
            UserIdentityConfiguration config)
        {
            string canonicalId = Guid.NewGuid().ToString("N");

            var profile = new UserProfile(canonicalId, config.DeviceDisplayName);
            profile.AddLink(new IdentityLink(
                UserAnonymousIdentityProvider.ProviderIdValue,
                canonicalId,
                config.DeviceDisplayName));

            return profile;
        }

        private async Task AutoLinkAuthenticatedAsync(CancellationToken ct)
        {
            foreach (IIdentityProvider provider in _providers)
            {
                if (provider.ProviderId == UserAnonymousIdentityProvider.ProviderIdValue)
                {
                    continue;
                }

                if (!provider.IsAuthenticated)
                {
                    continue;
                }

                try
                {
                    IdentityLink link = await provider.GetIdentityAsync(ct);
                    if (link != null && !string.IsNullOrEmpty(link.ProviderUserId))
                    {
                        await ApplyLinkAsync(link);
                    }
                }
                catch (Exception ex)
                {
                    QuickLog.Warning<UserIdentityManager>(
                        "Auto-link for '{0}' failed: {1}",
                        provider.ProviderId, ex.Message);
                }
            }
        }

        private async Task ApplyLinkAsync(IdentityLink link)
        {
            if (CurrentUser == null) return;

            if (CurrentUser.IsLinkedTo(link.ProviderId, link.ProviderUserId))
            {
                QuickLog.Debug<UserIdentityManager>(
                    "Identity already linked: '{0}' ('{1}').",
                    link.ProviderId, link.ProviderUserId);
                return;
            }

            CurrentUser.AddLink(link);
            SaveProfile();
            IdentityChanged?.Invoke(CurrentUser);

            QuickLog.Info<UserIdentityManager>(
                "Linked '{0}' identity '{1}' to canonical user '{2}'.",
                link.ProviderId, link.ProviderUserId, CurrentUser.CanonicalId);
        }

        private IIdentityProvider FindProvider(Type providerType)
        {
            foreach (IIdentityProvider provider in _providers)
            {
                if (provider.GetType() == providerType)
                {
                    return provider;
                }
            }

            return null;
        }

        private void RefreshStatus()
        {
            if (_providers.Count == 0)
            {
                SetStatus(UserIdentityStatus.Error);
                return;
            }

            bool anyPlatformAuthenticated = _providers.Any(provider =>
                provider.RequiresLogin && provider.IsAuthenticated);

            SetStatus(anyPlatformAuthenticated
                ? UserIdentityStatus.SignedIn
                : UserIdentityStatus.Anonymous);
        }

        private void SetStatus(UserIdentityStatus newStatus)
        {
            if (Status == newStatus) return;
            Status = newStatus;
            StatusChanged?.Invoke(Status);
        }

        private void SaveProfile()
        {
            if (CurrentUser == null) return;
            CurrentUser.LastSeenUtc = DateTime.UtcNow;
            UserIdentityStorage.Save(CurrentUser);
        }

        #endregion
    }
}
