using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.Integration.IAR
{
    /// <summary>
    /// Static facade over the registered <see cref="IInAppReviewManager"/>.
    /// The review request API is fire-and-forget by nature; the facade adds
    /// availability queries and initialization flavors.
    /// </summary>
    public class InAppReview
    {
        #region Queries

        public static IInAppReviewManager Manager => Integration.InAppReviewManager;

        public static bool IsAvailable => Manager != null;

        #endregion

        #region Initialization

        public static void Initialize()
        {
            if (!TryGetManager(out IInAppReviewManager manager))
            {
                return;
            }

            manager.Initialize();
        }

        public static IEnumerator InitializeCoroutine()
        {
            if (!TryGetManager(out IInAppReviewManager manager))
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

        #region Request (fire-and-forget)

        public static void Request()
        {
            if (!TryGetManager(out IInAppReviewManager manager))
            {
                return;
            }

            manager.PerformInAppReviewRequest();
        }

        #endregion

        #region Private Methods

        private static bool TryGetManager(out IInAppReviewManager manager)
        {
            manager = Integration.InAppReviewManager;
            if (manager == null)
            {
                QuickLog.Warning<InAppReview>(
                    "In-App Review manager is not registered. Ensure the module is enabled in the IntegrationCentre."
                );
            }

            return manager != null;
        }

        private static IInAppReviewManager RequireManager()
        {
            return Integration.RequireManager<IInAppReviewManager>();
        }

        #endregion
    }
}
