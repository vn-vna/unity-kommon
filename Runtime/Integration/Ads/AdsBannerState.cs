using UnityEngine;

namespace Com.Scheherazade.Common.Integration.Ads
{
    public enum AdsBannerStatus
    {
        Unavailable,
        Loading,
        Available,
        Showing,
        Hidden,
        Failed
    }

    public readonly struct AdsBannerState
    {
        public AdsBannerStatus Status { get; }
        /// <summary>Current MAX banner layout size in screen coordinates; zero until measurable.</summary>
        public Vector2 Size { get; }
        public string Reason { get; }
        public bool IsAvailable => Status == AdsBannerStatus.Available ||
            Status == AdsBannerStatus.Showing || Status == AdsBannerStatus.Hidden;
        public bool IsVisible => Status == AdsBannerStatus.Showing;

        public AdsBannerState(AdsBannerStatus status, Vector2 size, string reason = "")
        {
            Status = status;
            Size = size;
            Reason = reason ?? string.Empty;
        }

        public static AdsBannerState Unavailable(string reason = "") =>
            new AdsBannerState(AdsBannerStatus.Unavailable, Vector2.zero, reason);
    }
}
