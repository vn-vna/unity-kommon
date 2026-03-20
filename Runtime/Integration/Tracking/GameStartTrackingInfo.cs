using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public struct GameStartTrackingInfo
    {
        public string PlayId;
        public int StageNumber;
        public int CurrencyBalance;
        public int LoopNumber;
        public int? CurrentStage; // optional, if useful in non-ingame contexts
    }
}
