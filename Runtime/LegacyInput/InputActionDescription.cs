using System;

namespace Com.Hapiga.Scheherazade.Common.LegacyInput
{
    /// <summary>
    /// Describes an input action with its mapping, name, and scan callback.
    /// </summary>
    public class InputActionDescription
    {
        public InputActionDescription(string mapping, string name,
            Action<InputActionInfo, InputActionDescription> scanCallback)
        {
            Mapping = mapping;
            Name = name;
            ScanCallback = scanCallback;
        }

        public string Mapping { get; }
        public string Name { get; }
        public Action<InputActionInfo, InputActionDescription> ScanCallback { get; }
        public event Action<InputActionInfo> StartListeners;
        public event Action<InputActionInfo> PerformListeners;
        public event Action<InputActionInfo> CancelListeners;

        public void InvokeListeners(InputActionInfo info)
        {
            if (!info.Enabled) return;

            switch (info.ActionState)
            {
                case ActionState.Started:
                    StartListeners?.Invoke(info);
                    break;
                case ActionState.Performed:
                    PerformListeners?.Invoke(info);
                    break;
                case ActionState.Canceled:
                    CancelListeners?.Invoke(info);
                    break;
            }
        }
    }
}