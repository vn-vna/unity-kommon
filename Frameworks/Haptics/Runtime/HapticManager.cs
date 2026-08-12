using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    [AddComponentMenu("Scheherazade/Haptic Manager")]
    [DontDestroyOnLoad]
    public class HapticManager : SingletonBehavior<HapticManager>
    {
        #region Constants

        private const string ConfigPath =
            "Integration/Managers/HapticConfiguration";

        private const float DefaultCueDuration = 0.05f;

        #endregion

        #region Events & Delegates

        public event Action<HapticRhythm, HapticHandle> RhythmStarted;
        public event Action<HapticRhythm, HapticHandle> RhythmStopped;
        public event Action<string> Error;

        #endregion

        #region Private Fields

        private HapticConfiguration _config;
        private IHapticProvider _provider;
        private HapticTimelineRunner _runner;

        private readonly Dictionary<string, HapticRhythm> _rhythmsById =
            new Dictionary<string, HapticRhythm>(32);

        private readonly Dictionary<int, HapticRhythm> _rhythmsByHandle =
            new Dictionary<int, HapticRhythm>(16);

        #endregion

        #region Properties

        public HapticConfiguration Configuration => _config;
        public IHapticProvider Provider => _provider;
        public bool IsAvailable => _provider != null && _provider.IsAvailable;

        #endregion

        #region Unity Callbacks

        protected override void Awake()
        {
            base.Awake();

            try
            {
                _config = Resources.Load<HapticConfiguration>(ConfigPath);
                if (_config != null)
                {
                    HapticConfiguration.Instance = _config;
                    BuildLookup();
                }
                else
                {
                    QuickLog.Warning<HapticManager>(
                        "No HapticConfiguration found at '{0}'. "
                        + "Haptics will no-op until a config is added.",
                        ConfigPath);
                }

                ResolveProvider();
                CreateRunner();
            }
            catch (Exception ex)
            {
                QuickLog.Error<HapticManager>(
                    "HapticManager initialization failed: {0}", ex);
            }
        }

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GameObject go = new GameObject("[Scheherazade Haptic Manager]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<HapticManager>();
        }

        #endregion

        #region Public Methods

        public HapticHandle PlayRhythm(string rhythmId, float intensityScale = 1f)
        {
            HapticRhythm rhythm = Resolve(rhythmId);
            return rhythm != null ? PlayRhythm(rhythm, intensityScale) : HapticHandle.Invalid;
        }

        public HapticHandle PlayRhythm(HapticRhythm rhythm, float intensityScale = 1f)
        {
            if (rhythm == null)
            {
                QuickLog.Error<HapticManager>("PlayRhythm ignored: rhythm is null.");
                return HapticHandle.Invalid;
            }

            if (_runner == null)
            {
                QuickLog.Warning<HapticManager>(
                    "PlayRhythm('{0}') ignored: timeline runner not ready.", rhythm.Id);
                return HapticHandle.Invalid;
            }

            IHapticProvider active = ActiveProvider();
            if (active == null)
            {
                QuickLog.Warning<HapticManager>(
                    "PlayRhythm('{0}') ignored: no haptic provider available.", rhythm.Id);
                return HapticHandle.Invalid;
            }

            HapticHandle handle = _runner.Play(rhythm, active, intensityScale);
            if (handle.IsValid)
            {
                _rhythmsByHandle[handle.Id] = rhythm;
                RhythmStarted?.Invoke(rhythm, handle);
            }
            return handle;
        }

        public void Stop(HapticHandle handle)
        {
            if (!handle.IsValid) return;

            _runner?.Stop(handle);

            if (_rhythmsByHandle.Remove(handle.Id, out HapticRhythm rhythm))
            {
                RhythmStopped?.Invoke(rhythm, handle);
            }
        }

        public void Pause(HapticHandle handle)
        {
            if (!handle.IsValid) return;
            _runner?.Pause(handle);
        }

        public void Resume(HapticHandle handle)
        {
            if (!handle.IsValid) return;
            _runner?.Resume(handle);
        }

        public void StopAll()
        {
            _runner?.CancelAll();
            _provider?.CancelAll();
            _rhythmsByHandle.Clear();
        }

        /// <summary>
        /// Fire a single one-shot (fire-and-forget convenience, e.g. button taps).
        /// </summary>
        public void Cue(
            HapticWaveformType type,
            float intensity = 1f,
            float durationSeconds = DefaultCueDuration)
        {
            IHapticProvider active = ActiveProvider();
            if (active == null) return;

            HapticKeyframe keyframe = new HapticKeyframe(
                0f, durationSeconds, Mathf.Clamp01(intensity), 0.5f, type);
            active.Cue(keyframe, 1f);
        }

        /// <summary>
        /// Platform capability query — delegates to the active provider.
        /// </summary>
        public bool Supports(HapticWaveformType type)
        {
            return _provider != null && _provider.SupportsWaveform(type);
        }

        #endregion

        #region Private Methods

        private void BuildLookup()
        {
            _rhythmsById.Clear();

            HapticRhythm[] rhythms = _config.Rhythms;
            for (int i = 0; i < rhythms.Length; i++)
            {
                HapticRhythm rhythm = rhythms[i];
                if (rhythm == null || string.IsNullOrEmpty(rhythm.Id))
                {
                    continue;
                }

                if (_rhythmsById.ContainsKey(rhythm.Id))
                {
                    QuickLog.Warning<HapticManager>(
                        "Duplicate rhythm id '{0}' in configuration.", rhythm.Id);
                    continue;
                }

                _rhythmsById[rhythm.Id] = rhythm;
            }
        }

        private void ResolveProvider()
        {
            _provider = _config != null ? _config.Provider : null;

            if (_provider == null || !_provider.IsAvailable)
            {
                if (_provider == null)
                {
                    QuickLog.Warning<HapticManager>(
                        "No haptic provider configured; falling back to no-op.");
                }
                else
                {
                    QuickLog.Warning<HapticManager>(
                        "Provider '{0}' is not available on this device; "
                        + "falling back to no-op.", _provider.ProviderId);
                }

                _provider = ScriptableObject.CreateInstance<NullHapticProvider>();
            }

            try
            {
                _provider.Initialize();
            }
            catch (Exception ex)
            {
                QuickLog.Error<HapticManager>(
                    "Provider '{0}' failed to initialize: {1}", _provider.ProviderId, ex);
                _provider = ScriptableObject.CreateInstance<NullHapticProvider>();
            }
        }

        private void CreateRunner()
        {
            GameObject runnerGo = new GameObject("[Haptic Timeline Runner]");
            runnerGo.hideFlags = HideFlags.HideInHierarchy;
            runnerGo.transform.SetParent(transform, false);
            _runner = runnerGo.AddComponent<HapticTimelineRunner>();
        }

        private IHapticProvider ActiveProvider()
        {
            if (_provider == null || !_provider.IsAvailable) return null;
            return _provider;
        }

        private HapticRhythm Resolve(string rhythmId)
        {
            if (_rhythmsById.TryGetValue(rhythmId, out HapticRhythm rhythm))
            {
                return rhythm;
            }

            QuickLog.Error<HapticManager>(
                "Unknown rhythm id '{0}'.", rhythmId);
            return null;
        }

        #endregion
    }
}
