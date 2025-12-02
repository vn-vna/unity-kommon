using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public struct TrackingActionInfo
    {
        public string ActionId { get; set; }
        public Dictionary<string, object> Parameters { get; set; }

        public static Dictionary<string, object> CreateParametersDictionary(
            params (string key, object value)[] parameters
        )
        {
            var dict = new Dictionary<string, object>();
            foreach (var (key, value) in parameters)
            {
                dict[key] = value;
            }
            return dict;
        }
    }
}