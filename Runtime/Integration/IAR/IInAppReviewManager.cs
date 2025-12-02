using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.Integration.IAR
{
    public interface IInAppReviewManager
    {
        void Initialize();
        IEnumerator InitializeCoroutine();
        void PerformInAppReviewRequest();
    }
}