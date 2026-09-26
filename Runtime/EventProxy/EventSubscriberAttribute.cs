using System;

namespace Com.Scheherazade.Common.EventProxy
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class EventSubscriberAttribute : Attribute
    {
        public EventSubscriberAttribute(string eventName)
        {
            EventName = eventName;
        }

        public string EventName { get; }
    }
}