using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    /// <summary>
    /// Static facade over the registered <see cref="ITrackingManager"/>.
    /// Tracking APIs are fire-and-forget by nature; the facade adds queries,
    /// initialization flavors and null-safe guards.
    /// </summary>
    public class Tracking
    {
        #region Queries

        public static ITrackingManager Manager => Integration.TrackingManager;

        public static bool IsAvailable => Manager != null;

        public static TrackingManagerStatus Status =>
            Manager != null ? Manager.Status : TrackingManagerStatus.Uninitialized;

        public static bool IsReady =>
            Status == TrackingManagerStatus.Ready
            || Status == TrackingManagerStatus.PartiallyReady;

        public static bool AllowTracking
        {
            get => Manager != null && Manager.AllowTracking;
            set
            {
                if (TryGetManager(out ITrackingManager manager))
                {
                    manager.AllowTracking = value;
                }
            }
        }

        public static string DeviceTrackingIdentifier
        {
            get => Manager != null ? Manager.DeviceTrackingIdentifier : string.Empty;
            set
            {
                if (TryGetManager(out ITrackingManager manager))
                {
                    manager.DeviceTrackingIdentifier = value;
                }
            }
        }

        public static bool? IsTrackingFiltered => Manager?.IsTrackingFiltered;

        public static void AssignFilteredTrackingDevices(params string[] ids)
        {
            if (TryGetManager(out ITrackingManager manager))
            {
                manager.AssignFilteredTrackingDevices(ids);
            }
        }

        #endregion

        #region Initialization

        public static void Initialize(float timeOut = float.MaxValue)
        {
            if (!TryGetManager(out ITrackingManager manager))
            {
                return;
            }

            manager.Initialize(timeOut);
        }

        public static IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            if (!TryGetManager(out ITrackingManager manager))
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

        #region Screen Tracking

        public static void TrackScreen(string screenId)
        {
            if (TryGetManager(out ITrackingManager manager))
            {
                manager.TrackScreen(screenId);
            }
        }

        #endregion

        #region Action Tracking

        public static void TrackAction(TrackingActionInfo info)
        {
            if (TryGetManager(out ITrackingManager manager))
            {
                manager.TrackAction(info);
            }
        }

        public static void TrackAction(string action, params (string key, object value)[] parameters)
        {
            if (TryGetManager(out ITrackingManager manager))
            {
                manager.TrackAction(action, parameters);
            }
        }

        public static void TrackAction(
            string action,
            ProviderIdentity mask,
            params (string key, object value)[] parameters
        )
        {
            if (TryGetManager(out ITrackingManager manager))
            {
                manager.TrackAction(action, mask, parameters);
            }
        }

        #endregion

        #region Templated Events

        public static void TrackTemplatedEvent(string eventName)
        {
            if (TryGetManager(out ITrackingManager manager))
            {
                manager.TrackTemplatedEvent(eventName);
            }
        }

        public static void TrackTemplatedEvent(string eventName, ProviderIdentity mask)
        {
            if (TryGetManager(out ITrackingManager manager))
            {
                manager.TrackTemplatedEvent(eventName, mask);
            }
        }

        #endregion

        #region Revenue Tracking

        public static void TrackAdRevenue(AdTrackingInfo info)
        {
            if (TryGetManager(out ITrackingManager manager))
            {
                manager.TrackAdRevenue(info);
            }
        }

        public static void TrackPurchaseRevenue(PurchaseTrackingInfo info)
        {
            if (TryGetManager(out ITrackingManager manager))
            {
                manager.TrackPurchaseRevenue(info);
            }
        }

        #endregion

        #region Private Methods

        private static bool TryGetManager(out ITrackingManager manager)
        {
            manager = Integration.TrackingManager;
            if (manager == null)
            {
                QuickLog.Warning<Tracking>(
                    "Tracking manager is not registered. Ensure the module is enabled in the IntegrationCentre."
                );
            }

            return manager != null;
        }

        private static ITrackingManager RequireManager()
        {
            ITrackingManager manager = Integration.RequireManager<ITrackingManager>();
            if (manager.Status != TrackingManagerStatus.Ready
                && manager.Status != TrackingManagerStatus.PartiallyReady)
            {
                throw new IntegrationNotInitializedException(nameof(Tracking));
            }

            return manager;
        }

        #endregion
    }
}
