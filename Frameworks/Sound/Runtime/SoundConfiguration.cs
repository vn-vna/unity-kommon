using System;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Sound
{
    [SingletonScriptableConfig(
        ScriptableLoadSource.Resources,
        "Integration/Managers/SoundConfiguration")]
    public class SoundConfiguration : SingletonScriptableObject<SoundConfiguration>
    {
        #region Serialized Fields

        [SerializeField]
        private ScriptableObject _sfxProvider;

        [SerializeField]
        private ScriptableObject _bgmProvider;

        [SerializeField]
        private SoundDefinition[] _sounds = Array.Empty<SoundDefinition>();

        #endregion

        #region Properties

        public ISoundPlayerProvider SfxProvider
            => _sfxProvider as ISoundPlayerProvider;

        public ISoundPlayerProvider BgmProvider
            => _bgmProvider as ISoundPlayerProvider;

        public ScriptableObject SfxProviderAsset
        {
            get => _sfxProvider;
            set => _sfxProvider = value;
        }

        public ScriptableObject BgmProviderAsset
        {
            get => _bgmProvider;
            set => _bgmProvider = value;
        }

        public SoundDefinition[] Sounds
        {
            get => _sounds ?? Array.Empty<SoundDefinition>();
            set => _sounds = value ?? Array.Empty<SoundDefinition>();
        }

        #endregion

        #region Public Methods

        public SoundDefinition Find(string soundId)
        {
            if (string.IsNullOrEmpty(soundId) || _sounds == null)
            {
                return null;
            }

            for (int i = 0; i < _sounds.Length; i++)
            {
                if (_sounds[i] != null && _sounds[i].Id == soundId)
                {
                    return _sounds[i];
                }
            }
            return null;
        }

        #endregion
    }
}
