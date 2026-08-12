using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Sound
{
    /// <summary>
    /// Owns the pooled players. Created by a provider on a hidden persistent GameObject.
    /// Plays by dequeueing a free PooledAudioPlayer, configuring, and enabling it;
    /// stops by disabling the object (disable = stop).
    /// </summary>
    public class AudioSourcePlayerPool : MonoBehaviour
    {
        #region Constants

        private const float SweepIntervalSeconds = 0.05f;
        private const int CapacityHint = 32;

        private static readonly WaitForSeconds SweepInterval =
            new WaitForSeconds(SweepIntervalSeconds);

        #endregion

        #region Serialized Fields

        [Header("Pool config")]
        [SerializeField]
        private int _prewarmCount = 8;

        [SerializeField]
        private int _maxCapacity = 32;

        [SerializeField]
        private bool _growable = true;

        #endregion

        #region Private Fields

        private readonly List<PooledAudioPlayer> _players =
            new List<PooledAudioPlayer>(CapacityHint);
        private readonly Stack<PooledAudioPlayer> _free =
            new Stack<PooledAudioPlayer>(CapacityHint);
        private int _nextInstanceId = 1;

        #endregion

        #region Properties

        public int Count => _players.Count;
        public int FreeCount => _free.Count;

        #endregion

        #region Public Methods

        public void Configure(int prewarmCount, int maxCapacity, bool growable)
        {
            _prewarmCount = Mathf.Max(0, prewarmCount);
            _maxCapacity = Mathf.Max(1, maxCapacity);
            _growable = growable;
        }

        public void Initialize()
        {
            for (int i = 0; i < _prewarmCount; i++)
            {
                CreatePlayer();
            }

            StartCoroutine(SweepFinishedPlayers());
        }

        public SoundHandle Play(SoundDefinition def, float volumeScale = 1f)
        {
            PooledAudioPlayer player = Acquire();
            if (player == null)
            {
                QuickLog.Warning<AudioSourcePlayerPool>(
                    "Pool exhausted ({0}); denying play of '{1}'",
                    _players.Count, def.Id);
                return SoundHandle.Invalid;
            }

            player.Configure(def, volumeScale);
            player.PlayRequest();
            return new SoundHandle(
                SoundManager.Instance,
                player.InstanceId,
                player.Generation);
        }

        public void Stop(SoundHandle handle)
        {
            PooledAudioPlayer player = FindById(handle.Id);
            if (player == null || player.Generation != handle.Generation) return; // stale handle
            if (player.IsFree) return;

            player.StopRequest();
            _free.Push(player);
        }

        public void Pause(SoundHandle handle)
        {
            PooledAudioPlayer player = FindById(handle.Id);
            if (player == null || player.Generation != handle.Generation) return; // stale handle
            player.PauseRequest();
        }

        public void Resume(SoundHandle handle)
        {
            PooledAudioPlayer player = FindById(handle.Id);
            if (player == null || player.Generation != handle.Generation) return; // stale handle
            player.ResumeRequest();
        }

        public void StopAll()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                PooledAudioPlayer player = _players[i];
                if (player.IsFree) continue;

                player.StopRequest();
                _free.Push(player);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Lightweight sweeper: recycles non-looping players whose clip finished
        /// naturally. Runs on a cached WaitForSeconds interval — no per-frame
        /// Update, no per-call allocations.
        /// </summary>
        private IEnumerator SweepFinishedPlayers()
        {
            while (true)
            {
                yield return SweepInterval;

                for (int i = 0; i < _players.Count; i++)
                {
                    PooledAudioPlayer player = _players[i];
                    if (player.IsFree || player.IsPaused) continue;
                    if (player.IsPlaying) continue;

                    player.StopRequest();
                    _free.Push(player);
                }
            }
        }

        private PooledAudioPlayer Acquire()
        {
            if (_free.Count > 0)
            {
                return _free.Pop();
            }

            if (_growable && _players.Count < _maxCapacity)
            {
                return CreatePlayer();
            }

            return null; // pool exhausted, not growable
        }

        private PooledAudioPlayer CreatePlayer()
        {
            GameObject go = new GameObject("PooledAudioPlayer_" + _nextInstanceId);
            go.transform.SetParent(transform, false);
            go.hideFlags = HideFlags.HideInHierarchy;

            PooledAudioPlayer player = go.AddComponent<PooledAudioPlayer>();
            player.Initialize(_nextInstanceId);
            _nextInstanceId++;

            _players.Add(player);
            _free.Push(player);
            return player;
        }

        private PooledAudioPlayer FindById(int id)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].InstanceId == id)
                {
                    return _players[i];
                }
            }
            return null;
        }

        #endregion
    }
}
