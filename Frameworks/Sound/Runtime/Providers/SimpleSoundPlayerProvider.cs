using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Sound
{
    /// <summary>
    /// Baseline fallback provider: a fresh AudioSource per play, no pooling.
    /// Useful for editor preview and as a reference implementation. Keeps the
    /// interface honest — the manager does not care which provider is behind it.
    /// </summary>
    [CreateAssetMenu(
        menuName = "Scheherazade/Sound/Simple Sound Player Provider")]
    public class SimpleSoundPlayerProvider : ScriptableObject, ISoundPlayerProvider
    {
        #region Constants

        private const int DefaultPriority = 128;

        #endregion

        #region Private Fields

        private readonly Dictionary<int, AudioSource> _sources =
            new Dictionary<int, AudioSource>();
        private GameObject _host;
        private int _nextId = 1;

        #endregion

        #region ISoundPlayerProvider

        public string ProviderId => nameof(SimpleSoundPlayerProvider);
        public bool IsAvailable => true;

        public void Initialize()
        {
            if (_host != null) return;

            _host = new GameObject("[Sound Simple Host]");
            Object.DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideInHierarchy;
        }

        public SoundHandle Play(SoundDefinition def, float volumeScale = 1f)
        {
            if (def == null || def.Clip == null)
            {
                return SoundHandle.Invalid;
            }

            EnsureInitialized();

            AudioSource source = _host.AddComponent<AudioSource>();
            source.clip = def.Clip;
            source.volume = Mathf.Clamp01(def.Volume * volumeScale);
            source.pitch = def.Pitch;
            source.loop = def.Loop;
            source.priority = def.Priority;
            source.spatialBlend = def.SpatialBlend;
            source.playOnAwake = false;
            source.Play();

            int id = _nextId;
            _nextId++;
            _sources[id] = source;

            if (!def.Loop)
            {
                float duration = def.Clip.length / Mathf.Max(0.01f, def.Pitch);
                Destroy(source, duration + 0.1f);
            }

            return new SoundHandle(SoundManager.Instance, id, 1);
        }

        public void Stop(SoundHandle handle)
        {
            if (!_sources.TryGetValue(handle.Id, out AudioSource source))
            {
                return;
            }

            _sources.Remove(handle.Id);
            if (source != null)
            {
                Destroy(source);
            }
        }

        public void Pause(SoundHandle handle)
        {
            if (_sources.TryGetValue(handle.Id, out AudioSource source)
                && source != null)
            {
                source.Pause();
            }
        }

        public void Resume(SoundHandle handle)
        {
            if (_sources.TryGetValue(handle.Id, out AudioSource source)
                && source != null)
            {
                source.UnPause();
            }
        }

        public void StopAll()
        {
            foreach (AudioSource source in _sources.Values)
            {
                if (source != null)
                {
                    Destroy(source);
                }
            }
            _sources.Clear();
        }

        public void SetBusVolume(SoundBusType bus, float volume)
        {
            // No pooled lifetime to tune; volume is applied per-source at Play.
        }

        #endregion

        #region Private Methods

        private void EnsureInitialized()
        {
            if (_host == null)
            {
                Initialize();
            }
        }

        #endregion
    }
}
