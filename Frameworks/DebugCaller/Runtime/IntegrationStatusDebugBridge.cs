using Com.Scheherazade.Common.Integration;
using UnityEngine;

namespace Com.Scheherazade.Common.DebugCaller
{
    internal static class IntegrationStatusDebugBridge
    {
        private static bool _isSubscribed;
        private static bool _hasShownForCurrentRun;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            if (_isSubscribed)
            {
                DebugCallerState.StateChanged -= HandleDebugStateChanged;
                DebugCaller.DebuggerRequested -= HandleDebuggerRequested;
            }

            _isSubscribed = false;
            _hasShownForCurrentRun = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (!_isSubscribed)
            {
                DebugCallerState.StateChanged += HandleDebugStateChanged;
                DebugCaller.DebuggerRequested += HandleDebuggerRequested;
                _isSubscribed = true;
            }

            if (DebugCallerState.IsEnabled)
            {
                ShowOnce();
            }
        }

        private static void HandleDebugStateChanged()
        {
            if (DebugCallerState.IsEnabled)
            {
                ShowOnce();
            }
        }

        private static void HandleDebuggerRequested()
        {
            ShowOnce();
        }

        private static void ShowOnce()
        {
            if (_hasShownForCurrentRun) return;

            _hasShownForCurrentRun = true;
            IntegrationStatusView.ShowOrCreate(minimized: false);
        }
    }
}
