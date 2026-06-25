using System;

namespace Com.Hapiga.Scheherazade.Common.Threading
{
    [Serializable]
    public enum RetryStrategy
    {
        Immediate,
        FixedInterval,
        ExponentialInterval,
        ExponentialIntervalWithJitter,
        Cancel
    }
}
