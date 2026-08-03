using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;

namespace Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig
{
    /// <summary>
    /// Static facade over the registered <see cref="IRemoteConfigManager"/>.
    /// Exposes query, event, fire-and-forget, coroutine and async APIs.
    /// </summary>
    public class RemoteConfig
    {
        #region Queries

        public static IRemoteConfigManager Manager => Integration.RemoteConfigManager;

        public static bool IsAvailable => Manager != null;

        public static RemoteConfigStatus Status =>
            Manager != null ? Manager.Status : RemoteConfigStatus.Uninitialized;

        public static bool IsReady => Status == RemoteConfigStatus.Ready;

        /// <summary>
        /// Latest acquired config data, or null when not yet acquired.
        /// </summary>
        public static IRemoteConfigData Data => Manager?.Config as IRemoteConfigData;

        /// <summary>
        /// Latest acquired config data cast to the concrete config type, or null.
        /// </summary>
        public static T Get<T>() where T : class, IRemoteConfigData
        {
            return Manager?.Config as T;
        }

        #endregion

        #region Events

        public static event Action<IRemoteConfigData> ConfigAcquired
        {
            add
            {
                if (TrySubscribe(out IRemoteConfigManager manager)) manager.ConfigAcquired += value;
            }
            remove
            {
                if (Manager != null) Manager.ConfigAcquired -= value;
            }
        }

        #endregion

        #region Initialization

        public static void Initialize(float timeOut = float.MaxValue)
        {
            if (!TryGetManager(out IRemoteConfigManager manager))
            {
                return;
            }

            manager.Initialize(timeOut);
        }

        public static IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            if (!TryGetManager(out IRemoteConfigManager manager))
            {
                yield break;
            }

            IEnumerator steps = manager.InitializeCoroutine(timeOut);
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }

        public static Task InitializeAsync(float timeOut = float.MaxValue, CancellationToken ct = default)
        {
            RequireManager();
            return CoroutineTaskBridge.RunAsync(InitializeCoroutine(timeOut));
        }

        #endregion

        #region Wait For Config

        public static void WaitForConfig(Action<IRemoteConfigData> onAcquired)
        {
            if (!TryGetManager(out IRemoteConfigManager manager))
            {
                onAcquired?.Invoke(null);
                return;
            }

            Dispatcher.DispatchCoroutine(WaitForConfigCoroutineImpl(manager, onAcquired));
        }

        public static IEnumerator WaitForConfigCoroutine(Action<IRemoteConfigData> onAcquired)
        {
            if (!TryGetManager(out IRemoteConfigManager manager))
            {
                onAcquired?.Invoke(null);
                yield break;
            }

            IEnumerator steps = WaitForConfigCoroutineImpl(manager, onAcquired);
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }

        public static Task<IRemoteConfigData> WaitForConfigAsync(
            float timeoutSeconds = 30f,
            CancellationToken ct = default
        )
        {
            IRemoteConfigManager manager = RequireManager();
            return CoroutineTaskBridge.RunWithCallbackAsync<IRemoteConfigData>(
                onAcquired => WaitForConfigCoroutineImpl(manager, onAcquired),
                timeoutSeconds,
                ct
            );
        }

        #endregion

        #region Private Methods

        private static IEnumerator WaitForConfigCoroutineImpl(
            IRemoteConfigManager manager,
            Action<IRemoteConfigData> onAcquired
        )
        {
            if (manager.Config is IRemoteConfigData current && manager.Status == RemoteConfigStatus.Ready)
            {
                onAcquired?.Invoke(current);
                yield break;
            }

            IRemoteConfigData acquired = null;

            void Handler(IRemoteConfigData data) => acquired = data;

            manager.ConfigAcquired += Handler;
            while (acquired == null)
            {
                yield return null;
            }

            manager.ConfigAcquired -= Handler;
            onAcquired?.Invoke(acquired);
        }

        private static bool TryGetManager(out IRemoteConfigManager manager)
        {
            manager = Integration.RemoteConfigManager;
            if (manager == null)
            {
                QuickLog.Warning<RemoteConfig>(
                    "Remote config manager is not registered. Ensure the module is enabled in the IntegrationCentre."
                );
            }

            return manager != null;
        }

        private static bool TrySubscribe(out IRemoteConfigManager manager)
        {
            manager = Integration.RemoteConfigManager;
            if (manager == null)
            {
                QuickLog.Warning<RemoteConfig>(
                    "Remote config manager is not registered yet; subscription was dropped. " +
                    "Subscribe after initialization or use Integration.TryGetManager."
                );
            }

            return manager != null;
        }

        private static IRemoteConfigManager RequireManager()
        {
            IRemoteConfigManager manager = Integration.RequireManager<IRemoteConfigManager>();
            if (manager.Status != RemoteConfigStatus.Ready)
            {
                throw new IntegrationNotInitializedException(nameof(RemoteConfig));
            }

            return manager;
        }

        #endregion
    }
}
