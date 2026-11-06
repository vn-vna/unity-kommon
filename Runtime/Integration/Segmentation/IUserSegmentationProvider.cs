using System;

namespace Com.Scheherazade.Common.Integration.Segmentation
{
    public interface IUserSegmentationProvider
    {
        IUserSegmentation Manager { get; set; }
        bool IsInitialized { get; }

        event Action<SegmentationInformation> SegmentationDataAcquired;

        void Initialize();
        void CleanUp();
    }
}
