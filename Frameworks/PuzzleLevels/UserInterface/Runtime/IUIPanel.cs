using System;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
{
    public interface IUIPanel
    {
        event Action PreShowPanel;
        event Action ShowStarted;
        event Action ShowCompleted;
        event Action PreHidePanel;
        event Action HideStarted;
        event Action HideCompleted;

        void Show(bool immedidate = false, Action callback = null);
        void Hide(bool immedidate = false, Action callback = null);
    }
}