using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public struct GameEndTrackingInfo
    {
        public string PlayId;
        public int StageNumber;
        public int CurrencyBalance;
        public string GameResult; // "win", "lose", "quit", "other"
        public int IngameTime; // seconds
        public int Progress; // %
        public int LoopNumber;
        public int? CurrentStage;
    }
}
