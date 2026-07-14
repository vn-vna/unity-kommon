using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TrackingParamDefaultFactoryAttribute : Attribute
    {
        public string ParameterName { get; }

        public TrackingParamDefaultFactoryAttribute(string parameterName)
        {
            ParameterName = parameterName;
        }
    }
}
