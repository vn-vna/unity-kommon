using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Integration;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public static class GameEndTracker
    {
        public static void TrackGameEnd(GameEndTrackingInfo info)
        {
            if (Integration.TrackingManager == null)
                return;

            var parameters = new Dictionary<string, object>
            {
                { "play_id", info.PlayId },
                { "stage_number", info.StageNumber },
                { "currency_balance", info.CurrencyBalance },
                { "game_result", info.GameResult },
                { "ingame_time", info.IngameTime },
                { "progress", info.Progress },
            };

            Integration.TrackingManager.TrackAction(new TrackingActionInfo
            {
                ActionId = "game_end",
                Parameters = parameters
            });
        }
    }
}