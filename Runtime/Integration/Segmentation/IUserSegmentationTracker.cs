namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    public interface IUserSegmentationTracker
    {
        IUserSegmentation Manager { get; set; }

        void SegmentationDataUpdated(SegmentationInformation info, SegmentationDeclaration declaration);
    }
}
