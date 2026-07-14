using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TrackingParamDescriptionAttribute : Attribute
    {
        public string Description { get; }

        public TrackingParamDescriptionAttribute(string description)
        {
            Description = description;
        }
    }
}
