using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    [CreateAssetMenu(fileName = "SystemTimeProvider", menuName = "Scheherazade/Chrono/System Time Provider")]
    public class SystemTimeProvider : TimeProviderBase
    {
        public override DateTime Now => DateTime.Now;

        public override DateTime Today => DateTime.Today;

        public override DateTime UtcNow => DateTime.UtcNow;

        public override DateTime Epoch => DateTime.UnixEpoch;
    }
}
