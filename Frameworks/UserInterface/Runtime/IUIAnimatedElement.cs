using System;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
{

    public interface IUIAnimatedElement
    {
        Action PreShowCallback { get; }
        AnimationHandle ShowAnimation { get; }
        Action PreHideCallback { get; }
        AnimationHandle HideAnimation { get; }
    }
}