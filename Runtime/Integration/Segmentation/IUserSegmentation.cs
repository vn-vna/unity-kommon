using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    public interface IUserSegmentation
    {
        SegmentationInformation SegmentInformation { get; }
        SegmentationDeclaration CurrentSegmentDeclaration { get; }

        void RegisterSegmentation(SegmentationInformation userSegmentation);
        void Initialize();
        IEnumerator InitializeCoroutine();
        void NotifySegmentationTrackers();
    }
}