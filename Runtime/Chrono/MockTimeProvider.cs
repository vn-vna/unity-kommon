using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    [CreateAssetMenu(fileName = "MockTimeProvider", menuName = "Scheherazade/Chrono/Mock Time Provider")]
    public class MockTimeProvider : TimeProviderBase, ISettableTimeProvider
    {
        [SerializeField] private SerializableDateTime _initialTime = new SerializableDateTime(DateTime.Now);

        [NonSerialized] private DateTime _mockTime;
        [NonSerialized] private DateTime _originalTime;

        private void OnEnable()
        {
            _mockTime = _initialTime.Value;
            _originalTime = _mockTime;
        }

        public override DateTime Now => _mockTime;

        public override DateTime Today => _mockTime.Date;

        public override DateTime UtcNow => _mockTime.ToUniversalTime();

        public override DateTime Epoch => DateTime.UnixEpoch;

        public void AdvanceTime(TimeSpan timeSpan)
        {
            _mockTime = _mockTime.Add(timeSpan);
        }

        public void SetTime(DateTime dateTime)
        {
            _mockTime = dateTime;
        }

        public void Reset()
        {
            _mockTime = _originalTime;
        }
    }
}
