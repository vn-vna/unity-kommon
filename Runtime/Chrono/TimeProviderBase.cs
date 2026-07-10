using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    public abstract class TimeProviderBase : ScriptableObject, ITimeProvider
    {
        public abstract DateTime Epoch { get; }

        public abstract DateTime Now { get; }

        public abstract DateTime UtcNow { get; }

        public abstract DateTime Today { get; }
    }
}
