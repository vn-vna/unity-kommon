using System;

namespace Com.Hapiga.Scheherazade.Common.EventProxy
{
    [AttributeUsage(AttributeTargets.Event)]
    public class EventPublisherAttribute : Attribute
    {
        public EventPublisherAttribute(Type type, string methodName)
        {
            EventName = type.Name + "/" + methodName;
        }

        public EventPublisherAttribute(string eventName)
        {
            EventName = eventName;
        }

        public string EventName { get; }
    }
}