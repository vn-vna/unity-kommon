using System;

namespace Com.Scheherazade.Common.Threading
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
