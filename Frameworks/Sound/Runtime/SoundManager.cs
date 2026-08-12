using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Sound
{
    [AddComponentMenu("Scheherazade/Sound Manager")]
    [DontDestroyOnLoad]
    public class SoundManager : SingletonBehavior<SoundManager>
    {
        #region Constants

        private const string ConfigPath =
            "Integration/Managers/SoundConfiguration";

        private const string SfxVolumeKey = "scheherazade.sound.sfxVolume";
        private const string BgmVolumeKey = "scheherazade.sound.bgmVolume";
        private const string MutedKey = "scheherazade.sound.muted";

        private const float DefaultVolume = 1f;

        #endregion

        #region Events & Delegates

        public event Action<SoundDefinition, SoundHandle> SoundPlayed;
        public event Action<SoundHandle> SoundStopped;
        public event Action<SoundDefinition> BgmChanged;
        public event Action<SoundBusType, float> VolumeChanged;

        #endregion

        #region Private Fields

        private SoundConfiguration _config;
        private ISoundPlayerProvider _sfxProvider;
        private ISoundPlayerProvider _bgmProvider;

        private readonly Dictionary<string, SoundDefinition> _soundsById =
            new Dictionary<string, SoundDefinition>(32);

        private SoundHandle _currentBgm = SoundHandle.Invalid;

        private float _sfxVolume = DefaultVolume;
        private float _bgmVolume = DefaultVolume;
        private bool _muted;

        #endregion

        #region Properties

        public SoundConfiguration Configuration => _config;
        public bool IsReady => _config != null && _sfxProvider != null;

        public float SfxVolume => _sfxVolume;
        public float BgmVolume => _bgmVolume;
        public bool Muted => _muted;

        #endregion

        #region Unity Callbacks

        protected override void Awake()
        {
            base.Awake();

            try
            {
                _config = Resources.Load<SoundConfiguration>(ConfigPath);
                if (_config != null)
                {
                    SoundConfiguration.Instance = _config;
                    BuildLookup();
                    ResolveProviders();
                    LoadVolumes();
                }
                else
                {
                    QuickLog.Warning<SoundManager>(
                        "No SoundConfiguration found at '{0}'. "
                        + "Falling back to an in-memory pooled provider.",
                        ConfigPath);
                    CreateFallbackProvider();
                }
            }
            catch (Exception ex)
            {
                QuickLog.Error<SoundManager>(
                    "SoundManager initialization failed: {0}", ex);
            }
        }

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GameObject go = new GameObject("[Scheherazade Sound Manager]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<SoundManager>();
        }

        #endregion

        #region Public Methods

        public SoundHandle PlaySfx(string soundId, float volumeScale = DefaultVolume)
        {
            SoundDefinition def = Resolve(soundId);
            return def != null ? PlaySfx(def, volumeScale) : SoundHandle.Invalid;
        }

        public SoundHandle PlaySfx(
            SoundDefinition def,
            float volumeScale = DefaultVolume)
        {
            if (def == null)
            {
                QuickLog.Error<SoundManager>("PlaySfx ignored: definition is null.");
                return SoundHandle.Invalid;
            }
            if (_sfxProvider == null)
            {
                QuickLog.Warning<SoundManager>(
                    "PlaySfx('{0}') ignored: SFX provider not ready.", def.Id);
                return SoundHandle.Invalid;
            }

            SoundHandle handle = _sfxProvider.Play(def, volumeScale);
            if (handle.IsValid)
            {
                SoundPlayed?.Invoke(def, handle);
            }
            return handle;
        }

        public SoundHandle PlayBgm(string soundId)
        {
            SoundDefinition def = Resolve(soundId);
            return def != null ? PlayBgm(def) : SoundHandle.Invalid;
        }

        public SoundHandle PlayBgm(SoundDefinition def)
        {
            if (def == null)
            {
                QuickLog.Error<SoundManager>("PlayBgm ignored: definition is null.");
                return SoundHandle.Invalid;
            }
            if (_bgmProvider == null)
            {
                QuickLog.Warning<SoundManager>(
                    "PlayBgm('{0}') ignored: BGM provider not ready.", def.Id);
                return SoundHandle.Invalid;
            }

            StopCurrentBgm();

            _currentBgm = _bgmProvider.Play(def, DefaultVolume);
            if (_currentBgm.IsValid)
            {
                BgmChanged?.Invoke(def);
            }
            return _currentBgm;
        }

        public void Stop(SoundHandle handle)
        {
            if (!handle.IsValid) return;

            _sfxProvider?.Stop(handle);
            _bgmProvider?.Stop(handle);

            if (_currentBgm == handle)
            {
                _currentBgm = SoundHandle.Invalid;
            }

            SoundStopped?.Invoke(handle);
        }

        public void Pause(SoundHandle handle)
        {
            if (!handle.IsValid) return;
            _sfxProvider?.Pause(handle);
            _bgmProvider?.Pause(handle);
        }

        public void Resume(SoundHandle handle)
        {
            if (!handle.IsValid) return;
            _sfxProvider?.Resume(handle);
            _bgmProvider?.Resume(handle);
        }

        public void StopAll()
        {
            _sfxProvider?.StopAll();
            _bgmProvider?.StopAll();
            _currentBgm = SoundHandle.Invalid;
        }

        public void StopBgm()
        {
            StopCurrentBgm();
        }

        public void SetSfxVolume(float volume)
        {
            _sfxVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(SfxVolumeKey, _sfxVolume);
            ApplyVolumes();
            VolumeChanged?.Invoke(SoundBusType.Sfx, _sfxVolume);
        }

        public void SetBgmVolume(float volume)
        {
            _bgmVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(BgmVolumeKey, _bgmVolume);
            ApplyVolumes();
            VolumeChanged?.Invoke(SoundBusType.Bgm, _bgmVolume);
        }

        public void Mute(bool muted)
        {
            _muted = muted;
            PlayerPrefs.SetInt(MutedKey, _muted ? 1 : 0);
            ApplyVolumes();
        }

        public void ToggleMute()
        {
            Mute(!_muted);
        }

        #endregion

        #region Private Methods

        private void BuildLookup()
        {
            _soundsById.Clear();

            SoundDefinition[] sounds = _config.Sounds;
            for (int i = 0; i < sounds.Length; i++)
            {
                SoundDefinition def = sounds[i];
                if (def == null || string.IsNullOrEmpty(def.Id))
                {
                    continue;
                }

                if (_soundsById.ContainsKey(def.Id))
                {
                    QuickLog.Warning<SoundManager>(
                        "Duplicate sound id '{0}' in configuration.", def.Id);
                    continue;
                }

                _soundsById[def.Id] = def;
            }
        }

        private void ResolveProviders()
        {
            _sfxProvider = _config.SfxProvider;
            _bgmProvider = _config.BgmProvider;

            if (_sfxProvider == null)
            {
                QuickLog.Warning<SoundManager>(
                    "No SFX provider configured; falling back to default.");
                _sfxProvider = CreateDefaultProvider();
            }
            if (_bgmProvider == null)
            {
                QuickLog.Warning<SoundManager>(
                    "No BGM provider configured; falling back to default.");
                _bgmProvider = _config.BgmProviderAsset == null
                    ? CreateDefaultProvider()
                    : _config.BgmProvider;
            }

            _sfxProvider.Initialize();
            if (_bgmProvider != _sfxProvider)
            {
                _bgmProvider.Initialize();
            }
        }

        private ISoundPlayerProvider CreateDefaultProvider()
        {
            PooledSoundPlayerProvider provider =
                ScriptableObject.CreateInstance<PooledSoundPlayerProvider>();
            return provider;
        }

        private void CreateFallbackProvider()
        {
            _sfxProvider = CreateDefaultProvider();
            _bgmProvider = _sfxProvider;
            _sfxProvider.Initialize();
            _sfxVolume = DefaultVolume;
            _bgmVolume = DefaultVolume;
            _muted = false;
        }

        private void StopCurrentBgm()
        {
            if (_currentBgm.IsValid)
            {
                _bgmProvider?.Stop(_currentBgm);
                _currentBgm = SoundHandle.Invalid;
            }
        }

        private void LoadVolumes()
        {
            _sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, DefaultVolume);
            _bgmVolume = PlayerPrefs.GetFloat(BgmVolumeKey, DefaultVolume);
            _muted = PlayerPrefs.GetInt(MutedKey, 0) == 1;
            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            if (_sfxProvider == null || _bgmProvider == null) return;

            float effectiveSfx = _muted ? 0f : _sfxVolume;
            float effectiveBgm = _muted ? 0f : _bgmVolume;

            _sfxProvider.SetBusVolume(SoundBusType.Sfx, effectiveSfx);
            _bgmProvider.SetBusVolume(SoundBusType.Bgm, effectiveBgm);
        }

        private SoundDefinition Resolve(string soundId)
        {
            if (_soundsById.TryGetValue(soundId, out SoundDefinition def))
            {
                return def;
            }

            QuickLog.Error<SoundManager>(
                "Unknown sound id '{0}'.", soundId);
            return null;
        }

        #endregion
    }
}
