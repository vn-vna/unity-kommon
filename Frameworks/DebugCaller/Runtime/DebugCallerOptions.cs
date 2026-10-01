using System.ComponentModel;
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
