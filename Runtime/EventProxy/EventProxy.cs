using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.EventProxy
{

    [AddComponentMenu("Scheherazade/Event Proxy")]
    public class EventProxy : MonoBehaviour
    {
        #region Interfaces

        public List<MonoBehaviour> Publishers => publishers;
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

        public void AddPublisher<T>(T publisher)
            where T : MonoBehaviour
        {
            publishers.Add(publisher);
            CollectEventProxyData();
            _requireScan = true;
        }

        public void RemovePublisher<T>(T publisher)
            where T : MonoBehaviour
        {
            publishers.Remove(publisher);
            CollectEventProxyData();
            _requireScan = true;
        }

        public void AddSubscriber<T>(T subscriber)
            where T : MonoBehaviour
        {
            subscribers.Add(subscriber);
            CollectEventProxyData();
            _requireScan = true;
        }

        public void RemoveSubscriber<T>(T subscriber)
            where T : MonoBehaviour
        {
            subscribers.Remove(subscriber);
            CollectEventProxyData();
            _requireScan = true;
        }

        public void ClearAllPublishers()
        {
            publishers.Clear();
            _requireScan = true;
        }

        public void ClearAllSubscribers()
        {
            subscribers.Clear();
            _requireScan = true;
        }

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

        public struct EventInformation
        {
            public string EventName { get; set; }
            public MethodInfo AdderMethod { get; set; }
            public MethodInfo RemoverMethod { get; set; }
            public MonoBehaviour Publisher { get; set; }
        }

        private struct SubscriberInformation
        {
            public string EventName { get; set; }
            public Delegate Delegate { get; set; }
            public MonoBehaviour Publisher { get; set; }
        }

        #endregion
    }
}