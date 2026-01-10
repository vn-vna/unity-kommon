using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.EventProxy
{
    /// <summary>
    /// Manages automatic event wiring between publisher and subscriber MonoBehaviours using reflection.
    /// </summary>
    /// <remarks>
    /// This component scans publishers and subscribers for EventPublisher and EventSubscriber attributes,
    /// then automatically connects matching events and methods. This provides a decoupled way to wire
    /// events without hard-coding references. Events can be attached/detached dynamically.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Setup in Inspector or code
    /// var proxy = gameObject.AddComponent&lt;EventProxy&gt;();
    /// proxy.AddPublisher(myPublisher);
    /// proxy.AddSubscriber(mySubscriber);
    /// proxy.AttachAllEvents();
    /// 
    /// // Later, detach when done
    /// proxy.DetatchAllEvents();
    /// </code>
    /// </example>
    [AddComponentMenu("Scheherazade/Event Proxy")]
    public class EventProxy : MonoBehaviour
    {
        #region Interfaces

        /// <summary>
        /// Gets the list of publisher MonoBehaviours.
        /// </summary>
        public List<MonoBehaviour> Publishers => publishers;
        
        /// <summary>
        /// Gets the list of subscriber MonoBehaviours.
        /// </summary>
        public List<MonoBehaviour> Subscribers => subscribers;

        #endregion

        #region Serialized Fields

        [SerializeField]
        private List<MonoBehaviour> publishers;

        [SerializeField]
        private List<MonoBehaviour> subscribers;

        [SerializeField]
        private bool attachOnStart;

        #endregion

        #region Private Fields

        private Dictionary<string, EventInformation> _eventInformations;
        private List<SubscriberInformation> _subscriberInformations;
        private bool _requireScan;

        #endregion

        #region Constructors

        public EventProxy()
        {
            publishers = new List<MonoBehaviour>();
            subscribers = new List<MonoBehaviour>();
            _requireScan = true;
        }

        #endregion


        #region Unity Events
        private void Start()
        {
            if (attachOnStart)
            {
                AttachAllEvents();
            }
        }
        #endregion

        #region Methods

        /// <summary>
        /// Adds a publisher to the event proxy.
        /// </summary>
        /// <typeparam name="T">The type of the publisher MonoBehaviour.</typeparam>
        /// <param name="publisher">The publisher to add.</param>
        public void AddPublisher<T>(T publisher)
            where T : MonoBehaviour
        {
            publishers.Add(publisher);
            CollectEventProxyData();
            _requireScan = true;
        }

        /// <summary>
        /// Removes a publisher from the event proxy.
        /// </summary>
        /// <typeparam name="T">The type of the publisher MonoBehaviour.</typeparam>
        /// <param name="publisher">The publisher to remove.</param>
        public void RemovePublisher<T>(T publisher)
            where T : MonoBehaviour
        {
            publishers.Remove(publisher);
            CollectEventProxyData();
            _requireScan = true;
        }

        /// <summary>
        /// Adds a subscriber to the event proxy.
        /// </summary>
        /// <typeparam name="T">The type of the subscriber MonoBehaviour.</typeparam>
        /// <param name="subscriber">The subscriber to add.</param>
        public void AddSubscriber<T>(T subscriber)
            where T : MonoBehaviour
        {
            subscribers.Add(subscriber);
            CollectEventProxyData();
            _requireScan = true;
        }

        /// <summary>
        /// Removes a subscriber from the event proxy.
        /// </summary>
        /// <typeparam name="T">The type of the subscriber MonoBehaviour.</typeparam>
        /// <param name="subscriber">The subscriber to remove.</param>
        public void RemoveSubscriber<T>(T subscriber)
            where T : MonoBehaviour
        {
            subscribers.Remove(subscriber);
            CollectEventProxyData();
            _requireScan = true;
        }

        /// <summary>
        /// Clears all publishers from the event proxy.
        /// </summary>
        public void ClearAllPublishers()
        {
            publishers.Clear();
            _requireScan = true;
        }

        /// <summary>
        /// Clears all subscribers from the event proxy.
        /// </summary>
        public void ClearAllSubscribers()
        {
            subscribers.Clear();
            _requireScan = true;
        }

        /// <summary>
        /// Scans all publishers and subscribers to collect event and subscription information.
        /// </summary>
        /// <remarks>
        /// This method uses reflection to find EventPublisher and EventSubscriber attributes,
        /// then builds internal data structures for event wiring.
        /// </remarks>
        public void CollectEventProxyData()
        {
            _eventInformations = new Dictionary<string, EventInformation>();

            foreach (var publisher in publishers)
                publisher
                    .GetType()
                    .GetEvents(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Where(eventInfo => eventInfo.GetCustomAttributes(typeof(EventPublisherAttribute), true).Length > 0)
                    .Select(eventInfo =>
                    {
                        var attributes = eventInfo.GetCustomAttributes(typeof(EventPublisherAttribute), true);
                        var eventName = ((EventPublisherAttribute)attributes[0]).EventName;
                        var adderMethod = eventInfo.GetAddMethod();
                        var removerMethod = eventInfo.GetRemoveMethod();

                        return new EventInformation
                        {
                            EventName = eventName,
                            AdderMethod = adderMethod,
                            RemoverMethod = removerMethod,
                            Publisher = publisher
                        };
                    })
                    .ToList()
                    .ForEach(eventInformation =>
                    {
                        if (_eventInformations.ContainsKey(eventInformation.EventName))
                            throw new Exception($"Duplicate Event Key of {eventInformation.EventName} found");
                        _eventInformations.Add(eventInformation.EventName, eventInformation);
                    });

            _subscriberInformations = new List<SubscriberInformation>();
            foreach (var subscriber in subscribers)
                subscriber
                    .GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Where(method => method.GetCustomAttributes(typeof(EventSubscriberAttribute), true).Length > 0)
                    .SelectMany(method =>
                    {
                        List<SubscriberInformation> subscriberInformations = new List<SubscriberInformation>();

                        foreach (var attribute in method.GetCustomAttributes(typeof(EventSubscriberAttribute), true))
                        {
                            var eventName = ((EventSubscriberAttribute)attribute).EventName;
                            if (!_eventInformations.ContainsKey(eventName)) continue;
                            var eventInformation = _eventInformations[eventName];
                            var del = Delegate.CreateDelegate(eventInformation.AdderMethod.GetParameters()[0].ParameterType, subscriber, method);
                            subscriberInformations.Add(new SubscriberInformation()
                            {
                                EventName = eventName,
                                Delegate = del,
                                Publisher = eventInformation.Publisher
                            });
                        }

                        return subscriberInformations;
                    })
                    .ToList()
                    .ForEach(subscriberInformation => { _subscriberInformations.Add(subscriberInformation); });
        }

        /// <summary>
        /// Attaches all subscriber methods to their corresponding publisher events.
        /// </summary>
        /// <remarks>
        /// Call this method after adding publishers and subscribers to activate event connections.
        /// If changes have been made since the last scan, it will automatically rescan.
        /// </remarks>
        public void AttachAllEvents()
        {
            if (_requireScan)
            {
                CollectEventProxyData();
                _requireScan = false;
            }

            foreach (var subscriberInformation in _subscriberInformations)
                if (_eventInformations.ContainsKey(subscriberInformation.EventName))
                {
                    var eventInformation = _eventInformations[subscriberInformation.EventName];
                    eventInformation.AdderMethod.Invoke(subscriberInformation.Publisher,
                        new object[] { subscriberInformation.Delegate });
                }
        }

        /// <summary>
        /// Detaches all subscriber methods from their corresponding publisher events.
        /// </summary>
        /// <remarks>
        /// Call this method to disconnect all event subscriptions, typically during cleanup.
        /// </remarks>
        public void DetatchAllEvents()
        {
            foreach (var subscriberInformation in _subscriberInformations)
                if (_eventInformations.ContainsKey(subscriberInformation.EventName))
                {
                    var eventInformation = _eventInformations[subscriberInformation.EventName];
                    eventInformation.RemoverMethod.Invoke(subscriberInformation.Publisher,
                        new object[] { subscriberInformation.Delegate });
                }
        }

        #endregion

        #region Nested Classes

        /// <summary>
        /// Contains information about a published event including its accessor methods.
        /// </summary>
        public struct EventInformation
        {
            /// <summary>
            /// Gets or sets the unique name of the event.
            /// </summary>
            public string EventName { get; set; }
            
            /// <summary>
            /// Gets or sets the method used to add subscribers to the event.
            /// </summary>
            public MethodInfo AdderMethod { get; set; }
            
            /// <summary>
            /// Gets or sets the method used to remove subscribers from the event.
            /// </summary>
            public MethodInfo RemoverMethod { get; set; }
            
            /// <summary>
            /// Gets or sets the MonoBehaviour that publishes this event.
            /// </summary>
            public MonoBehaviour Publisher { get; set; }
        }

        /// <summary>
        /// Contains information about a subscriber method and its target event.
        /// </summary>
        private struct SubscriberInformation
        {
            /// <summary>
            /// Gets or sets the unique name of the event being subscribed to.
            /// </summary>
            public string EventName { get; set; }
            
            /// <summary>
            /// Gets or sets the delegate representing the subscriber method.
            /// </summary>
            public Delegate Delegate { get; set; }
            
            /// <summary>
            /// Gets or sets the MonoBehaviour that publishes the event.
            /// </summary>
            public MonoBehaviour Publisher { get; set; }
        }

        #endregion
    }
}