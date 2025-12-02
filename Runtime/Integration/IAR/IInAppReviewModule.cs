using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.Integration.IAR
{
    public interface IInAppReviewModule
    {
        IInAppReviewManager Manager { get; set; }
        bool IsInitialized { get; }

        void Initialize();
        void CleanUp();
        IEnumerator PerformInAppReviewRequest();
    }
}