using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Sound
{
    /// <summary>
    /// Static facade over <see cref="SoundManager"/>. All methods are null-safe
    /// and log via QuickLog. This is the fire-and-forget surface game code calls.
    /// </summary>
    public class Sounds
    {
        private Sounds() { }
        public static bool IsReady => SoundManager.Instance != null;

        public static bool Initialize()
        {
            return SoundManager.TryCreateIfConfigured();
        }

        public static async Awaitable<bool> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool initialized = Initialize();
            await Awaitable.NextFrameAsync();

            cancellationToken.ThrowIfCancellationRequested();
            return initialized && IsReady;
        }

        public static Task<bool> InitializeTaskAsync(
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<bool>(cancellationToken);
            }

            return Task.FromResult(Initialize() && IsReady);
        }

        public static IEnumerator InitializeCoroutine(
            Action<bool> onInitialized = null)
        {
            bool initialized = Initialize();
            yield return null;
            onInitialized?.Invoke(initialized && IsReady);
        }

        public static SoundHandle PlaySfx(string soundId, float volumeScale = 1f)
        {
            if (!EnsureReady("PlaySfx")) return SoundHandle.Invalid;
            return SoundManager.Instance.PlaySfx(soundId, volumeScale);
        }

        public static SoundHandle PlaySfx(
            SoundDefinition def,
            float volumeScale = 1f)
        {
            if (!EnsureReady("PlaySfx")) return SoundHandle.Invalid;
            return SoundManager.Instance.PlaySfx(def, volumeScale);
        }

        public static SoundHandle PlayBgm(string soundId)
        {
            if (!EnsureReady("PlayBgm")) return SoundHandle.Invalid;
            return SoundManager.Instance.PlayBgm(soundId);
        }

        public static SoundHandle PlayBgm(SoundDefinition def)
        {
            if (!EnsureReady("PlayBgm")) return SoundHandle.Invalid;
            return SoundManager.Instance.PlayBgm(def);
        }

        public static void Stop(SoundHandle handle)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.Stop(handle);
            }
        }

        public static void Pause(SoundHandle handle)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.Pause(handle);
            }
        }

        public static void Resume(SoundHandle handle)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.Resume(handle);
            }
        }

        public static void StopAll()
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.StopAll();
            }
        }

        public static void StopBgm()
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.StopBgm();
            }
        }

        public static void SetSfxVolume(float volume)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.SetSfxVolume(volume);
            }
        }

        public static void SetBgmVolume(float volume)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.SetBgmVolume(volume);
            }
        }

        public static void Mute(bool muted)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.Mute(muted);
            }
        }

        public static void ToggleMute()
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.ToggleMute();
            }
        }

        #region Private Methods

        private static bool EnsureReady(string operation)
        {
            if (SoundManager.Instance != null)
            {
                return true;
            }

            if (Initialize())
            {
                return true;
            }

            if (SoundManager.Instance == null)
            {
                QuickLog.Warning<Sounds>(
                    "{0} ignored: no SoundConfiguration was found.", operation);
                return false;
            }

            return true;
        }

        #endregion
    }
}
