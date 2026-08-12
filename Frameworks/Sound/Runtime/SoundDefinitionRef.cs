using System;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Sound
{
    /// <summary>
    /// Bindable sound cue. Drop one on any MonoBehaviour via [SerializeField],
    /// assign a SoundDefinition sub-asset in the inspector, then call Play().
    /// The returned handle supports Stop / Pause / Resume directly, and the
    /// cue keeps a reference to the last handle for convenience wrappers.
    /// </summary>
    [Serializable]
    public sealed class SoundDefinitionRef
    {
        [SerializeField]
        private SoundDefinition _definition;

        private SoundHandle _lastHandle;

        public SoundDefinition Definition
        {
            get => _definition;
            set => _definition = value;
        }

        public bool IsValid => _definition != null;

        public bool HasLastHandle => _lastHandle.IsValid;

        public SoundHandle LastHandle => _lastHandle;

        /// <summary>
        /// Routes through the SoundManager. BGM definitions play on the BGM
        /// provider, SFX on the pooled SFX provider.
        /// </summary>
        public SoundHandle Play(float volumeScale = 1f)
        {
            if (_definition == null)
            {
                QuickLog.Warning<SoundDefinitionRef>(
                    "Play ignored: no SoundDefinition bound.");
                return SoundHandle.Invalid;
            }
            if (SoundManager.Instance == null)
            {
                QuickLog.Warning<SoundDefinitionRef>(
                    "Play({0}) ignored: SoundManager not ready.", _definition.Id);
                return SoundHandle.Invalid;
            }

            _lastHandle = _definition.IsBgm
                ? SoundManager.Instance.PlayBgm(_definition)
                : SoundManager.Instance.PlaySfx(_definition, volumeScale);

            return _lastHandle;
        }

        /// <summary>Convenience: stops the last played handle, if any.</summary>
        public void Stop()
        {
            if (_lastHandle.IsValid)
            {
                _lastHandle.Stop();
            }
        }

        /// <summary>Convenience: pauses the last played handle, if any.</summary>
        public void Pause()
        {
            if (_lastHandle.IsValid)
            {
                _lastHandle.Pause();
            }
        }

        /// <summary>Convenience: resumes the last played handle, if any.</summary>
        public void Resume()
        {
            if (_lastHandle.IsValid)
            {
                _lastHandle.Resume();
            }
        }
    }
}
