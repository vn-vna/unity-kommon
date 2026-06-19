using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [Flags]
    public enum ProviderIdentity
    {
        None = 0,
        PV00 = 1 << 0,
        PV01 = 1 << 1,
        PV02 = 1 << 2,
        PV03 = 1 << 3,
        PV04 = 1 << 4,
        PV05 = 1 << 5,
        PV06 = 1 << 6,
        PV07 = 1 << 7,
        PV08 = 1 << 8,
        PV09 = 1 << 9,
        PV10 = 1 << 10,
        PV11 = 1 << 11,
        PV12 = 1 << 12,
        PV13 = 1 << 13,
        PV14 = 1 << 14,
        PV15 = 1 << 15,
        PV16 = 1 << 16,
        PV17 = 1 << 17,
        PV18 = 1 << 18,
        PV19 = 1 << 19,
        PV20 = 1 << 20,
        PV21 = 1 << 21,
        PV22 = 1 << 22,
        PV23 = 1 << 23,
        PV24 = 1 << 24,
        PV25 = 1 << 25,
        PV26 = 1 << 26,
        PV27 = 1 << 27,
        PV28 = 1 << 28,
        PV29 = 1 << 29,
        PV30 = 1 << 30,
        PV31 = unchecked((int)0x80000000),
        All = ~0
    }
}
