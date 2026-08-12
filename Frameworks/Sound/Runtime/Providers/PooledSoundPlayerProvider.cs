using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;
using UnityEngine.Audio;

namespace Com.Hapiga.Scheherazade.Common.Sound
{
    /// <summary>
    /// Default / primary provider: plays through <see cref="AudioSourcePlayerPool"/>.
    /// Each Play() dequeues a pooled player (disable = stop), and the returned
    /// SoundHandle can Stop / Pause / Resume the exact pooled object.
    /// </summary>
    [CreateAssetMenu(
        menuName = "Scheherazade/Sound/Pooled Sound Player Provider")]
    public class PooledSoundPlayerProvider : ScriptableObject, ISoundPlayerProvider
    {
        #region Serialized Fields

        [Header("Pool config")]
        [SerializeField]
        private int _prewarmCount = 8;

        [SerializeField]
        private int _maxCapacity = 32;

        [SerializeField]
        private bool _growable = true;

        [Header("Audio Mixer routing")]
        [SerializeField]
        private AudioMixerGroup _sfxGroup;

        [SerializeField]
        private AudioMixerGroup _bgmGroup;

        [SerializeField]
        private string _sfxVolumeParam = "SfxVolume";

        [SerializeField]
        private string _bgmVolumeParam = "BgmVolume";

        #endregion

        #region Private Fields

        private AudioSourcePlayerPool _pool;

        #endregion

        #region ISoundPlayerProvider

        public string ProviderId => nameof(PooledSoundPlayerProvider);
        public bool IsAvailable => true;

        public void Initialize()
        {
            if (_pool != null) return;

            GameObject host = new GameObject("[Sound Pool]");
            Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideInHierarchy;

            _pool = host.AddComponent<AudioSourcePlayerPool>();
            _pool.Configure(_prewarmCount, _maxCapacity, _growable);
            _pool.Initialize();

            QuickLog.Debug<PooledSoundPlayerProvider>(
                "Initialized pool ({0} prewarm / {1} max / growable={2}).",
                _prewarmCount, _maxCapacity, _growable);
        }

        public SoundHandle Play(SoundDefinition def, float volumeScale = 1f)
        {
            EnsureInitialized();
            return _pool.Play(def, volumeScale);
        }

        public void Stop(SoundHandle handle)
        {
            if (_pool != null) _pool.Stop(handle);
        }

        public void Pause(SoundHandle handle)
        {
            if (_pool != null) _pool.Pause(handle);
        }

        public void Resume(SoundHandle handle)
        {
            if (_pool != null) _pool.Resume(handle);
        }

        public void StopAll()
        {
            if (_pool != null) _pool.StopAll();
        }

        public void SetBusVolume(SoundBusType bus, float volume)
        {
            AudioMixerGroup group = bus == SoundBusType.Sfx ? _sfxGroup : _bgmGroup;
            string param = bus == SoundBusType.Sfx ? _sfxVolumeParam : _bgmVolumeParam;

            if (group != null && group.audioMixer != null && !string.IsNullOrEmpty(param))
            {
                float db = ToDecibels(Mathf.Clamp01(volume));
                group.audioMixer.SetFloat(param, db);
            }
        }

        #endregion

        #region Private Methods

        private void EnsureInitialized()
        {
            if (_pool == null)
            {
                Initialize();
            }
        }

        private static float ToDecibels(float linear)
        {
            return linear <= 0.0001f ? -80f : 20f * Mathf.Log10(linear);
        }

        #endregion
    }
}
