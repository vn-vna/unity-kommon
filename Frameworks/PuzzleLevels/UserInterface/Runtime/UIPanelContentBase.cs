using System;
using DG.Tweening;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIPanelContentBase :
        MonoBehaviour,
        IUIAnimatedElement
    {

        #region Interfaces & Properties

        Action IUIAnimatedElement.PreShowCallback => PreShowCallbackAnimation;
        Action IUIAnimatedElement.PreHideCallback => PreHideCallbackAnimation;
        Tween IUIAnimatedElement.ShowAnimation => PerformShowAnimation();
        Tween IUIAnimatedElement.HideAnimation => PerformHideAnimation();
        public RectTransform RectTransform => rectTransform;
        public CanvasGroup CanvasGroup => canvasGroup;
        #endregion

        #region Serialized Fields
        [SerializeField]
        [HideInInspector]
        protected RectTransform rectTransform;

        [SerializeField]
        [HideInInspector]
        protected CanvasGroup canvasGroup;
        #endregion

        #region Unity Methods
        private void OnDestroy()
        {
            DOTween.Kill(this);
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (!rectTransform) rectTransform = GetComponent<RectTransform>();
            if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        }
#endif
        #endregion

        #region Protected Methods
        protected abstract void PreShowCallbackAnimation();
        protected abstract void PreHideCallbackAnimation();
        protected abstract Tween PerformShowAnimation();
        protected abstract Tween PerformHideAnimation();
        #endregion

    }
}