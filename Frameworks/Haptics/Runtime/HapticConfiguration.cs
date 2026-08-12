using System;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// Central configuration: platform-split provider slots + rhythm sub-assets.
    /// At runtime <see cref="Provider"/> resolves by platform
    /// (Android build uses the Android slot, iOS build uses the iOS slot,
    /// editor falls back to whichever exists).
    /// </summary>
    [SingletonScriptableConfig(
        ScriptableLoadSource.Resources,
        "Integration/Managers/HapticConfiguration")]
    public class HapticConfiguration : SingletonScriptableObject<HapticConfiguration>
    {
        #region Serialized Fields

        [SerializeField]
        private ScriptableObject _androidProvider;

        [SerializeField]
        private ScriptableObject _iosProvider;

        [SerializeField]
        private HapticRhythm[] _rhythms = Array.Empty<HapticRhythm>();

        #endregion

        #region Properties

        public IHapticProvider AndroidProvider => _androidProvider as IHapticProvider;
        public IHapticProvider IosProvider => _iosProvider as IHapticProvider;

        public ScriptableObject AndroidProviderAsset
        {
            get => _androidProvider;
            set => _androidProvider = value;
        }

        public ScriptableObject IosProviderAsset
        {
            get => _iosProvider;
            set => _iosProvider = value;
        }

        public HapticRhythm[] Rhythms
        {
            get => _rhythms ?? Array.Empty<HapticRhythm>();
            set => _rhythms = value ?? Array.Empty<HapticRhythm>();
        }

        public bool HasAnyProvider => _androidProvider != null || _iosProvider != null;

        /// <summary>
        /// Platform-resolved active provider (mirrors LeaderboardConfiguration).
        /// </summary>
        public IHapticProvider Provider
        {
            get
            {
#if UNITY_ANDROID
                return _androidProvider as IHapticProvider;
#elif UNITY_IOS
                return _iosProvider as IHapticProvider;
#else
                return (_androidProvider ?? _iosProvider) as IHapticProvider;
#endif
            }
        }

        #endregion

        #region Public Methods

        public HapticRhythm Find(string rhythmId)
        {
            if (string.IsNullOrEmpty(rhythmId) || _rhythms == null)
            {
                return null;
            }

            for (int i = 0; i < _rhythms.Length; i++)
            {
                if (_rhythms[i] != null && _rhythms[i].Id == rhythmId)
                {
                    return _rhythms[i];
                }
            }
            return null;
        }

        public void ValidateAll()
        {
            if (_rhythms == null) return;

            for (int i = 0; i < _rhythms.Length; i++)
            {
                if (_rhythms[i] != null)
                {
                    _rhythms[i].Validate();
                }
            }
        }

        #endregion
    }
}
