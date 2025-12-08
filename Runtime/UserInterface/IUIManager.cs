using System;
using UnityEngine;
using UnityEngine.UI;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
{
    public interface IUIManager
    {
        public event Action<UIPanelBase> PanelInitialized;
        public event Action<UIPanelBase> PanelReleased;

        Canvas Canvas { get; }
        CanvasScaler CanvasScaler { get; }
        GraphicRaycaster GraphicRaycaster { get; }
        RectTransform RectTransform { get; }
        CanvasGroup CanvasGroup { get; }

        void ReleasePanel<T>() where T : UIPanelBase;
        T RequirePanel<T>() where T : UIPanelBase;
    }
}