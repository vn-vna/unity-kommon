using System;
using DG.Tweening;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
{

    public interface IUIAnimatedElement
    {
        Action PreShowCallback { get; }
        Tween ShowAnimation { get; }
        Action PreHideCallback { get; }
        Tween HideAnimation { get; }
    }
}