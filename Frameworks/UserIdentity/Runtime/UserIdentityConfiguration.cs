using System;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    /// <summary>
    /// Per-platform list of identity providers. List order is the priority:
    /// index 0 ranks highest. Reorder with the up/down buttons in the
    /// settings editor.
    /// </summary>
    [SingletonScriptableConfig(
        ScriptableLoadSource.Resources,
        "Integration/Managers/UserIdentityConfiguration")]
    public class UserIdentityConfiguration :
        SingletonScriptableObject<UserIdentityConfiguration>
    {
        #region Serialized Fields

        [SerializeField]
        private List<ScriptableObject> _androidProviders;

        [SerializeField]
        private List<ScriptableObject> _iosProviders;

        [SerializeField]
        [Tooltip("Display name used for the anonymous profile.")]
        private string _deviceDisplayName = "Player";

        [SerializeField]
        [Tooltip("Link already-authenticated platform identities during initialization.")]
        private bool _autoLinkAuthenticatedOnInit = true;

        #endregion

        #region Properties

        /// <summary>
        /// Providers for the current platform, in list order (index 0 first).
        /// </summary>
        public IReadOnlyList<IIdentityProvider> Providers
        {
            get
            {
                List<ScriptableObject> source = ResolvePlatformList();
                if (source == null) return Array.Empty<IIdentityProvider>();

                return source
                    .Where(so => so != null && so is IIdentityProvider)
                    .Select(so => (IIdentityProvider)so)
                    .ToList();
            }
        }

        public List<ScriptableObject> AndroidProviders
        {
            get => _androidProviders;
            set => _androidProviders = value;
        }

        public List<ScriptableObject> IosProviders
        {
            get => _iosProviders;
            set => _iosProviders = value;
        }

        public string DeviceDisplayName
        {
            get => _deviceDisplayName;
            set => _deviceDisplayName = value;
        }

        public bool AutoLinkAuthenticatedOnInit
        {
            get => _autoLinkAuthenticatedOnInit;
            set => _autoLinkAuthenticatedOnInit = value;
        }

        public bool HasAnyProvider
        {
            get
            {
                List<ScriptableObject> source = ResolvePlatformList();
                return source != null && source.Count > 0;
            }
        }

        #endregion

        #region Private Methods

        private List<ScriptableObject> ResolvePlatformList()
        {
#if UNITY_ANDROID
            return _androidProviders;
#elif UNITY_IOS
            return _iosProviders;
#else
            if (_androidProviders != null && _androidProviders.Count > 0)
            {
                return _androidProviders;
            }

            return _iosProviders;
#endif
        }

        #endregion
    }
}
