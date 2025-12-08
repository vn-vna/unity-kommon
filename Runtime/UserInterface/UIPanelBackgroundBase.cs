using System;
using Com.Hapiga.Scheherazade.Common.Extensions;
using DG.Tweening;
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
        Action IUIAnimatedElement.PreShowCallback => null;

        Tween IUIAnimatedElement.ShowAnimation =>
            DOTween.Sequence()
                .AppendCallback(() => image.color = backgroundColor.WithAlpha(0f))
                .Append(image.DOFade(backgroundAlpha, 0.2f).SetEase(Ease.InOutBounce));

        Action IUIAnimatedElement.PreHideCallback => null;

        Tween IUIAnimatedElement.HideAnimation =>
            DOTween.Sequence()
                .AppendCallback(() => image.color = backgroundColor.WithAlpha(backgroundAlpha))
                .Append(image.DOFade(0f, 0.2f).SetEase(Ease.InOutBounce));

        public RectTransform RectTransform => rectTransform;
        public Image Image => image;
        #endregion

        #region Serialized Fields
        [SerializeField]
        private RectTransform rectTransform;

        [SerializeField]
        private Image image;

        [SerializeField]
        private Color backgroundColor = Color.black;

        [SerializeField]
        private float backgroundAlpha = 0.5f;
        #endregion

        #region Unity Methods
        private void OnDestroy()
        {
            DOTween.Kill(this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            rectTransform = GetComponent<RectTransform>();
            image = GetComponent<Image>();
        }
#endif
        #endregion
    }
}