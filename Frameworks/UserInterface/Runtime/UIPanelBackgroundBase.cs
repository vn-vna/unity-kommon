using System;
using Com.Scheherazade.Common.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace Com.Scheherazade.Common.UserInterface
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Image))]
    public abstract class UIPanelBackgroundBase :
        MonoBehaviour,
        IUIAnimatedElement
    {
        #region Interfaces & Properties
        Action IUIAnimatedElement.PreShowCallback => PreShowBackgroundAnimation;
        AnimationHandle IUIAnimatedElement.ShowAnimation => PerformShowAnimation();
        Action IUIAnimatedElement.PreHideCallback => PreHideBackgroundAnimation;
        AnimationHandle IUIAnimatedElement.HideAnimation => PerformHideAnimation();

        public RectTransform RectTransform => rectTransform;
        #endregion

        #region Serialized Fields
        [SerializeField]
        [HideInInspector]
        private RectTransform rectTransform;
        #endregion

        #region Unity Methods
        private void Awake()
        { }

#if UNITY_EDITOR
        private void OnValidate()
        {
            rectTransform = GetComponent<RectTransform>();
        }
#endif
        #endregion

        #region Protected Methods
        protected abstract void PreShowBackgroundAnimation();
        protected abstract AnimationHandle PerformShowAnimation();
        protected abstract void PreHideBackgroundAnimation();
        protected abstract AnimationHandle PerformHideAnimation();
        #endregion
    }
}