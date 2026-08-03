using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    /// <summary>
    /// Static facade over the registered <see cref="IUserSegmentation"/>.
    /// Exposes query, fire-and-forget, coroutine and async APIs.
    /// </summary>
    public class Segmentation
    {
        #region Queries

        public static IUserSegmentation Manager => Integration.UserSegmentation;

        public static bool IsAvailable => Manager != null;

        public static UserSegmentationStatus Status =>
            Manager != null ? Manager.Status : UserSegmentationStatus.Uninitialized;

        public static bool IsReady => Status == UserSegmentationStatus.Initialized;

        public static SegmentationInformation Information => Manager?.SegmentInformation;

        public static SegmentationDeclaration CurrentDeclaration => Manager?.CurrentSegmentDeclaration;

        public static DateTime? LastUpdateTime => Manager?.LastSegmentationUpdateTime;

        #endregion

        #region Initialization

        public static void Initialize()
        {
            if (!TryGetManager(out IUserSegmentation manager))
            {
                return;
            }

            manager.Initialize();
        }

        public static IEnumerator InitializeCoroutine()
        {
            if (!TryGetManager(out IUserSegmentation manager))
            {
                yield break;
            }

            IEnumerator steps = manager.InitializeCoroutine();
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }

        public static Task InitializeAsync(CancellationToken ct = default)
        {
            RequireManager();
            return CoroutineTaskBridge.RunAsync(InitializeCoroutine());
        }

        #endregion

        #region Wait For Initialized

        public static void WaitForInitialized(Action onInitialized)
        {
            if (!TryGetManager(out IUserSegmentation manager))
            {
                onInitialized?.Invoke();
                return;
            }

            Dispatcher.DispatchCoroutine(WaitForInitializedCoroutineImpl(manager, onInitialized));
        }

        public static IEnumerator WaitForInitializedCoroutine(Action onInitialized)
        {
            if (!TryGetManager(out IUserSegmentation manager))
            {
                onInitialized?.Invoke();
                yield break;
            }

            IEnumerator steps = WaitForInitializedCoroutineImpl(manager, onInitialized);
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }

        public static Task WaitForInitializedAsync(
            float timeoutSeconds = 30f,
            CancellationToken ct = default
        )
        {
            IUserSegmentation manager = RequireManager();
            return CoroutineTaskBridge.RunWithCallbackAsync<bool>(
                onDone => WaitForInitializedCoroutineImpl(manager, () => onDone(true)),
                timeoutSeconds,
                ct
            );
        }

        #endregion

        #region Trackers

        public static void NotifyTrackers()
        {
            if (TryGetManager(out IUserSegmentation manager))
            {
                manager.NotifySegmentationTrackers();
            }
        }

        #endregion

        #region Private Methods

        private static IEnumerator WaitForInitializedCoroutineImpl(
            IUserSegmentation manager,
            Action onInitialized
        )
        {
            while (manager.Status != UserSegmentationStatus.Initialized)
            {
                yield return null;
            }

            onInitialized?.Invoke();
        }

        private static bool TryGetManager(out IUserSegmentation manager)
        {
            manager = Integration.UserSegmentation;
            if (manager == null)
            {
                QuickLog.Warning<Segmentation>(
                    "Segmentation manager is not registered. Ensure the module is enabled in the IntegrationCentre."
                );
            }

            return manager != null;
        }

        private static IUserSegmentation RequireManager()
        {
            IUserSegmentation manager = Integration.RequireManager<IUserSegmentation>();
            if (manager.Status != UserSegmentationStatus.Initialized)
            {
                throw new IntegrationNotInitializedException(nameof(Segmentation));
            }

            return manager;
        }

        #endregion
    }
}
