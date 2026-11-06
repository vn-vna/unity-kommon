using System;

namespace Com.Scheherazade.Common.Integration.Tracking
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TrackingParamDefaultFactoryAttribute : Attribute
    {
        public string ParameterName { get; }

        public string DisplayName { get; }

        public TrackingParamDefaultFactoryAttribute(string parameterName)
        {
            ParameterName = parameterName;
            DisplayName = parameterName;
        }

        public TrackingParamDefaultFactoryAttribute(string parameterId, string displayName)
        {
            ParameterName = parameterId;
            DisplayName = displayName;
        }
    }
}
