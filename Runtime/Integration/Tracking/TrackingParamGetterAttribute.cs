using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TrackingParamGetterAttribute : Attribute
    {
        public string ParameterName { get; }

        public string DisplayName { get; }

        public TrackingParamGetterAttribute(string parameterName)
        {
            ParameterName = parameterName;
            DisplayName = parameterName;
        }

        public TrackingParamGetterAttribute(string parameterId, string displayName)
        {
            ParameterName = parameterId;
            DisplayName = displayName;
        }
    }
}
