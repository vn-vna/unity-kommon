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
        Action IUIAnimatedElement.PreShowCallback => animationType switch
        {
            UIPanelContentAnimationType.Scale => null,
            UIPanelContentAnimationType.Fade => null,
            UIPanelContentAnimationType.None => null,
            UIPanelContentAnimationType.Slide => PreShowAction_Slide,
            _ => null
        };

        Tween IUIAnimatedElement.ShowAnimation => animationType switch
        {
            UIPanelContentAnimationType.Scale => ShowAnimation_Scale(),
            UIPanelContentAnimationType.Fade => ShowAnimation_Fade(),
            UIPanelContentAnimationType.None => ShowAnimation_None(),
            UIPanelContentAnimationType.Slide => ShowAnimation_Slide(),
            _ => null
        };

        Action IUIAnimatedElement.PreHideCallback => animationType switch
        {
            UIPanelContentAnimationType.Scale => null,
            UIPanelContentAnimationType.Fade => null,
            UIPanelContentAnimationType.None => null,
            UIPanelContentAnimationType.Slide => PreHideAction_Slide,
            _ => null
        };

        Tween IUIAnimatedElement.HideAnimation => animationType switch
        {
            UIPanelContentAnimationType.Scale => HideAnimation_Scale(),
            UIPanelContentAnimationType.Fade => HideAnimation_Fade(),
            UIPanelContentAnimationType.None => HideAnimation_None(),
            UIPanelContentAnimationType.Slide => HideAnimation_Slide(),
            _ => null
        };

        public RectTransform RectTransform => rectTransform;
        public CanvasGroup CanvasGroup => canvasGroup;
        #endregion

        #region Serialized Fields
        [SerializeField]
        protected RectTransform rectTransform;

        [SerializeField]
        protected UIPanelContentAnimationType animationType;

        [SerializeField]
        protected CanvasGroup canvasGroup;

        [SerializeField]
        protected float showAnimationDuration = 0.2f;

        [SerializeField]
        protected float hideAnimationDuration = 0.2f;
        #endregion

        #region Unity Methods
        private void OnDestroy()
        {
            DOTween.Kill(this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (rectTransform == null)
            {
                rectTransform = GetComponent<RectTransform>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }
        }
#endif
        #endregion

        #region Private Methods
        private Tween ShowAnimation_None() =>
            DOTween.Sequence()
                .AppendCallback(() => rectTransform.localScale = Vector3.one)
                .AppendCallback(() => canvasGroup.alpha = 1f);

        private Tween HideAnimation_None() =>
            DOTween.Sequence()
                .AppendCallback(() => rectTransform.localScale = Vector3.one)
                .AppendCallback(() => canvasGroup.alpha = 1f);

        private Tween ShowAnimation_Scale() =>
            DOTween.Sequence()
                .AppendCallback(() => rectTransform.localScale = Vector3.zero)
                .Append(rectTransform.DOScale(Vector3.one, 0.2f).SetEase(Ease.OutBack));

        private Tween HideAnimation_Scale() =>
            DOTween.Sequence()
                .AppendCallback(() => rectTransform.localScale = Vector3.one)
                .Append(rectTransform.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack));

        private Tween ShowAnimation_Fade() =>
            DOTween.Sequence()
                .AppendCallback(() => canvasGroup.alpha = 0f)
                .Append(canvasGroup.DOFade(1f, 0.2f).SetEase(Ease.InOutBounce));

        private Tween HideAnimation_Fade() =>
            DOTween.Sequence()
                .AppendCallback(() => canvasGroup.alpha = 1f)
                .Append(canvasGroup.DOFade(0f, 0.2f).SetEase(Ease.InOutBounce));

        private void PreShowAction_Slide()
        {
            rectTransform.anchoredPosition = new Vector2(-rectTransform.rect.width, 0);
        }

        private Tween ShowAnimation_Slide() =>
            DOTween.Sequence()
                .Append(rectTransform.DOAnchorPosX(0, showAnimationDuration));

        private void PreHideAction_Slide()
        {
            rectTransform.anchoredPosition = Vector2.zero;
        }

        private Tween HideAnimation_Slide() =>
            DOTween.Sequence()
                .Append(rectTransform.DOAnchorPosY(-rectTransform.rect.height, hideAnimationDuration));
        #endregion

        #region Nested Types
        public enum UIPanelContentAnimationType
        {
            None, Scale, Fade, Slide
        }
        #endregion
    }
}