using System;

namespace Com.Hapiga.Scheherazade.Common.EventProxy
{
    /// <summary>
    /// Attribute to mark a method as a subscriber to events in the event proxy system.
    /// </summary>
    /// <remarks>
    /// Use this attribute on methods to make them automatically subscribe to events published
    /// through the EventProxy component. The method signature must match the event delegate type.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class MySubscriber : MonoBehaviour
    /// {
    ///     [EventSubscriber("MyEvent")]
    ///     private void OnMyEventHandler()
    ///     {
    ///         Debug.Log("Event received!");
    ///     }
    ///     
    ///     // Can subscribe to multiple events
    ///     [EventSubscriber("Event1")]
    ///     [EventSubscriber("Event2")]
    ///     private void OnMultipleEvents()
    ///     {
    ///         Debug.Log("Multiple events handled");
    ///     }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class EventSubscriberAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance with the event name to subscribe to.
        /// </summary>
        /// <param name="eventName">The unique name of the event to subscribe to.</param>
        public EventSubscriberAttribute(string eventName)
        {
            EventName = eventName;
        }

        /// <summary>
        /// Gets the unique name of the event this method subscribes to.
        /// </summary>
        public string EventName { get; }
    }
}