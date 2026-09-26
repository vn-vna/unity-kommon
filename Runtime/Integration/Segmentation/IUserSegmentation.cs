using System;
using System.Collections;

namespace Com.Scheherazade.Common.Integration.Segmentation
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
        DateTime? LastSegmentationUpdateTime { get; }

        void Initialize();
        IEnumerator InitializeCoroutine();
        void NotifySegmentationTrackers();
    }
}