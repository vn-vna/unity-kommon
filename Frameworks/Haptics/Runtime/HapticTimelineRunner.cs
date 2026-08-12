using System;
using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// Coroutine-driven keyframe clock. Walks a rhythm's sorted keyframes in
    /// time order and fires <see cref="IHapticProvider.Cue"/> at the right
    /// moments. Supports pause / resume / stop / loop.
    ///
    /// Zero-alloc hot path: slots are pooled, keyframe working arrays are
    /// reused across plays, and the coroutine accumulates delta time (yielding
    /// null does not allocate). No per-frame Update sweep.
    /// </summary>
    [DisallowMultipleComponent]
    public class HapticTimelineRunner : MonoBehaviour
    {
        #region Constants

        private const int MaxConcurrent = 8;

        #endregion

        #region Private Fields

        private readonly TimelineSlot[] _slots = new TimelineSlot[MaxConcurrent];
        private int _nextGeneration = 1;

        #endregion

        #region Unity Callbacks

        private void Awake()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = new TimelineSlot(i);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Sorts keyframes, starts the timeline, returns a generation-stamped handle.
        /// Returns <see cref="HapticHandle.Invalid"/> when no slot is free.
        /// </summary>
        public HapticHandle Play(
            HapticRhythm rhythm,
            IHapticProvider provider,
            float intensityScale = 1f)
        {
            if (rhythm == null)
            {
                QuickLog.Error<HapticTimelineRunner>("Play ignored: rhythm is null.");
                return HapticHandle.Invalid;
            }
            if (provider == null)
            {
                QuickLog.Error<HapticTimelineRunner>(
                    "Play('{0}') ignored: provider is null.", rhythm.Id);
                return HapticHandle.Invalid;
            }

            TimelineSlot slot = AcquireSlot();
            if (slot == null)
            {
                QuickLog.Warning<HapticTimelineRunner>(
                    "Play('{0}') ignored: all {1} timeline slots are busy.",
                    rhythm.Id, MaxConcurrent);
                return HapticHandle.Invalid;
            }

            slot.Generation = _nextGeneration++;
            slot.Rhythm = rhythm;
            slot.Provider = provider;
            slot.IntensityScale = intensityScale;
            slot.Paused = false;
            slot.HasContinuous = false;
            slot.PrepareKeyframes(rhythm.Keyframes);

            HapticHandle handle = new HapticHandle(slot.Index, slot.Generation);
            slot.Coroutine = StartCoroutine(RunTimeline(slot));
            return handle;
        }

        public void Stop(HapticHandle handle)
        {
            TimelineSlot slot = ResolveSlot(handle);
            if (slot == null) return;

            slot.HasContinuous = false;
            if (slot.Coroutine != null)
            {
                StopCoroutine(slot.Coroutine);
                slot.Coroutine = null;
            }
            ReleaseSlot(slot);
        }

        public void Pause(HapticHandle handle)
        {
            TimelineSlot slot = ResolveSlot(handle);
            if (slot != null)
            {
                slot.Paused = true;
            }
        }

        public void Resume(HapticHandle handle)
        {
            TimelineSlot slot = ResolveSlot(handle);
            if (slot != null)
            {
                slot.Paused = false;
            }
        }

        public void CancelAll()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                TimelineSlot slot = _slots[i];
                if (!slot.InUse) continue;

                if (slot.HasContinuous)
                {
                    slot.Provider?.EndContinuous(slot.ContinuousToken);
                    slot.HasContinuous = false;
                }

                if (slot.Coroutine != null)
                {
                    StopCoroutine(slot.Coroutine);
                    slot.Coroutine = null;
                }
                ReleaseSlot(slot);
            }
        }

        #endregion

        #region Private Methods

        private IEnumerator RunTimeline(TimelineSlot slot)
        {
            float elapsed = 0f;
            int index = 0;
            int count = slot.KeyframeCount;
            HapticKeyframe[] keyframes = slot.WorkingKeyframes;

            while (index < count)
            {
                if (!slot.Paused)
                {
                    elapsed += Time.deltaTime;

                    if (slot.HasContinuous && elapsed >= slot.ContinuousEndTime)
                    {
                        slot.Provider?.EndContinuous(slot.ContinuousToken);
                        slot.HasContinuous = false;
                    }

                    while (index < count && elapsed >= keyframes[index].TimeSeconds)
                    {
                        FireKeyframe(slot, keyframes[index]);
                        index++;
                    }

                    if (index >= count && slot.Rhythm.Loop)
                    {
                        index = 0;
                        elapsed = 0f;
                    }
                }

                yield return null;
            }

            if (slot.HasContinuous)
            {
                slot.Provider?.EndContinuous(slot.ContinuousToken);
                slot.HasContinuous = false;
            }

            ReleaseSlot(slot);
        }

        private static void FireKeyframe(TimelineSlot slot, HapticKeyframe keyframe)
        {
            if (keyframe.Waveform == HapticWaveformType.VibrationBurst)
            {
                if (slot.HasContinuous)
                {
                    slot.Provider?.EndContinuous(slot.ContinuousToken);
                }

                if (slot.Provider != null)
                {
                    slot.Provider.BeginContinuous(keyframe, out int token);
                    slot.ContinuousToken = token;
                    slot.ContinuousEndTime = keyframe.DurationEnd;
                    slot.HasContinuous = true;
                }
            }
            else
            {
                slot.Provider?.Cue(keyframe, slot.IntensityScale);
            }
        }

        private TimelineSlot AcquireSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].InUse)
                {
                    _slots[i].InUse = true;
                    return _slots[i];
                }
            }
            return null;
        }

        private TimelineSlot ResolveSlot(HapticHandle handle)
        {
            if (!handle.IsValid || handle.Id < 0 || handle.Id >= _slots.Length)
            {
                return null;
            }

            TimelineSlot slot = _slots[handle.Id];
            if (!slot.InUse || slot.Generation != handle.Generation)
            {
                return null;
            }
            return slot;
        }

        private void ReleaseSlot(TimelineSlot slot)
        {
            slot.InUse = false;
            slot.Rhythm = null;
            slot.Provider = null;
            slot.Coroutine = null;
            slot.Paused = false;
            slot.HasContinuous = false;
            slot.ResetWorkingArray();
        }

        #endregion

        #region Nested Types

        private sealed class TimelineSlot
        {
            public readonly int Index;
            public int Generation;
            public bool InUse;
            public HapticRhythm Rhythm;
            public IHapticProvider Provider;
            public float IntensityScale = 1f;
            public bool Paused;
            public Coroutine Coroutine;

            public bool HasContinuous;
            public int ContinuousToken = -1;
            public float ContinuousEndTime;

            public HapticKeyframe[] WorkingKeyframes;
            public int KeyframeCount;

            public TimelineSlot(int index)
            {
                Index = index;
            }

            public void PrepareKeyframes(HapticKeyframe[] source)
            {
                KeyframeCount = source == null ? 0 : source.Length;

                if (KeyframeCount == 0)
                {
                    ResetWorkingArray();
                    return;
                }

                if (WorkingKeyframes == null || WorkingKeyframes.Length < KeyframeCount)
                {
                    WorkingKeyframes = new HapticKeyframe[KeyframeCount];
                }

                Array.Copy(source, WorkingKeyframes, KeyframeCount);

                // Stable ascending sort by time (mirrors editor ordering).
                for (int i = 1; i < KeyframeCount; i++)
                {
                    HapticKeyframe current = WorkingKeyframes[i];
                    int j = i - 1;
                    while (j >= 0 && WorkingKeyframes[j].TimeSeconds > current.TimeSeconds)
                    {
                        WorkingKeyframes[j + 1] = WorkingKeyframes[j];
                        j--;
                    }
                    WorkingKeyframes[j + 1] = current;
                }
            }

            public void ResetWorkingArray()
            {
                KeyframeCount = 0;
            }
        }

        #endregion
    }
}
