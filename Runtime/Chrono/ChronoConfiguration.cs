using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    [CreateAssetMenu(
        fileName = "ChronoConfiguration",
        menuName = "Scheherazade/Chrono/Chrono Configuration"
    )]
    public class ChronoConfiguration : ScriptableObject
    {
        [SerializeField] private TimeProviderBase _timeProvider;

        [SerializeField] private ScriptableObject _persister;

        [SerializeField] private float _onlineMarkerIntervalMs = 3000f;

        public ITimeProvider TimeProvider => _timeProvider;

        public IChronoPersister Persister =>
            _persister as IChronoPersister;

        public float OnlineMarkerIntervalMs => _onlineMarkerIntervalMs;
    }
}
