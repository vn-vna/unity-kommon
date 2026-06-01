using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    public enum UserSegmentationStatus
    {
        Uninitialized,
        Initializing,
        Initialized
    }

    public interface IUserSegmentation
    {
        UserSegmentationStatus Status { get; }
        SegmentationInformation SegmentInformation { get; }
        SegmentationDeclaration CurrentSegmentDeclaration { get; }

        void RegisterSegmentation(SegmentationInformation userSegmentation);
        void Initialize();
        IEnumerator InitializeCoroutine();
        void NotifySegmentationTrackers();
    }
}