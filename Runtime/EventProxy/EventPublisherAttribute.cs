using System;

namespace Com.Hapiga.Scheherazade.Common.EventProxy
{
    /// <summary>
    /// Attribute to mark an event as a publisher in the event proxy system.
    /// </summary>
    /// <remarks>
    /// Use this attribute on events to make them discoverable by the EventProxy component.
    /// The EventProxy can then automatically connect these events to subscribers.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class MyPublisher : MonoBehaviour
    /// {
    ///     [EventPublisher("MyEvent")]
    ///     public event Action OnMyEvent;
    ///     
    ///     [EventPublisher(typeof(MyPublisher), "CustomEvent")]
    ///     public event Action OnCustomEvent;
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Event)]
    public class EventPublisherAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance using a type and method name to generate the event name.
        /// </summary>
        /// <param name="type">The type containing the event.</param>
        /// <param name="methodName">The method name to append to the type name.</param>
        public EventPublisherAttribute(Type type, string methodName)
        {
            EventName = type.Name + "/" + methodName;
        }

        /// <summary>
        /// Initializes a new instance with a direct event name.
        /// </summary>
        /// <param name="eventName">The unique name for this event.</param>
        public EventPublisherAttribute(string eventName)
        {
            EventName = eventName;
        }

        /// <summary>
        /// Gets the unique name identifying this event in the proxy system.
        /// </summary>
        public string EventName { get; }
    }
}