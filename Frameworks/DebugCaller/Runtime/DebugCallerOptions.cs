using System.ComponentModel;
using Com.Scheherazade.Common.Integration;
using Com.Scheherazade.Common.Logging;
using UnityEngine.Scripting;

namespace Com.Scheherazade.Common.DebugCaller
{
    [Preserve]
    public sealed class DebugCallerOptions
    {
        [Preserve]
        [Category("Debug Caller")]
        [DisplayName("Debug enabled")]
        public bool DebugEnabled => DebugCallerState.IsEnabled;

        [Category("Integration Status")]
        [DisplayName("Show full status panel")]
        [Preserve]
        public void ShowIntegrationStatus()
        {
            IntegrationStatusView.ShowOrCreate(minimized: false);
        }

        [Category("Integration Status")]
        [DisplayName("Show compact status panel")]
        [Preserve]
        public void ShowCompactIntegrationStatus()
        {
            IntegrationStatusView.ShowOrCreate(minimized: true);
        }

        [Category("Integration Status")]
        [DisplayName("Refresh status panel")]
        [Preserve]
        public void RefreshIntegrationStatus()
        {
            IntegrationStatusView.Instance?.RefreshNow();
        }

        [Category("Integration Status")]
        [DisplayName("Hide status panel")]
        [Preserve]
        public void HideIntegrationStatus()
        {
            IntegrationStatusView.Instance?.HidePanel();
        }

        [Preserve]
        [Category("Debug Caller")]
        [DisplayName("Clear debug enabled state")]
        public void ClearDebugEnabledState()
        {
            DebugCallerState.Clear();
            QuickLog.Info<DebugCallerOptions>(
                "Persistent debug-enabled state cleared."
            );
        }
    }
}
