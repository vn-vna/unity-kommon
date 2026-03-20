using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    // Manages play sessions (game_start) so all events share the same play_id
    public static class GameStartTracker
    {
        private static string _currentPlayId;
        private static int _currentStage = 0;
        private static int _currentLoop = 0;

        public static string CurrentPlayId => _currentPlayId;

        public static void BeginPlay(int stageNumber, int currencyBalance)
        {
            if (_currentPlayId == null)
            {
                _currentPlayId = Guid.NewGuid().ToString("N");
                _currentLoop = 0;
            }
            else if (stageNumber != _currentStage)
            {
                _currentLoop = 0;
            }
            else
            {
                _currentLoop++;
            }

            _currentStage = stageNumber;

            var info = new GameStartTrackingInfo
            {
                PlayId = _currentPlayId,
                StageNumber = stageNumber,
                CurrencyBalance = currencyBalance,
                LoopNumber = _currentLoop,
                CurrentStage = stageNumber
            };

            TrackGameStart(info);
        }

        public static void Reset()
        {
            _currentPlayId = null;
            _currentStage = 0;
            _currentLoop = 0;
        }

        public static void TrackGameStart(GameStartTrackingInfo info)
        {
            var parameters = new Dictionary<string, object>
            {
                { "play_id", info.PlayId },
                { "stage_number", info.StageNumber },
                { "currency_balance", info.CurrencyBalance },
                { "loop_number", info.LoopNumber }
            };
            // Log via the central tracking system
            Integration.TrackingManager?.TrackAction(new TrackingActionInfo
            {
                ActionId = "game_start",
                Parameters = parameters
            });
        }
    }
}
