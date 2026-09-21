using System;
using Com.Hapiga.Scheherazade.Common.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
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
        public Image Image => image;
        #endregion

        #region Serialized Fields
        [SerializeField]
        [HideInInspector]
        private RectTransform rectTransform;

        [SerializeField]
        [HideInInspector]
        private Image image;
        #endregion

        #region Unity Methods
        private void Awake()
        {
            Image.color = Color.clear;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            rectTransform = GetComponent<RectTransform>();
            image = GetComponent<Image>();
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