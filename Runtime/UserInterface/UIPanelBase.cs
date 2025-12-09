using System;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(GraphicRaycaster))]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIPanelBase :
        MonoBehaviour,
        IUIPanel
    {
        #region Events & Delegates
        public event Action PreShowPanel;
        public event Action ShowStarted;
        public event Action ShowCompleted;
        public event Action PreHidePanel;
        public event Action HideStarted;
        public event Action HideCompleted;
        #endregion

        #region Interfaces & Properties
        public string PanelId => panelId;
        public Canvas Canvas => canvas;
        public GraphicRaycaster GraphicRaycaster => graphicRaycaster;
        public RectTransform RectTransform => rectTransform;
        public CanvasGroup CanvasGroup => canvasGroup;
        public UIPanelContentBase PanelContent => content;
        public bool IsVisible { get; private set; }
        public IUIManager UIManager { get; set; }
        public bool IsBusy => _animationTween != null;
        public UIPanelBackgroundBase Background => background;
        public UIPanelContentBase Content => content;
        public bool AutoDisposeOnHide => autoDisposeOnHide;
        public float AutoDisposeDelay => autoDisposeDelay;
        #endregion

        #region Serialized Fields
        [Header("UIPanel Settings")]

        [SerializeField]
        [HideInInspector]
        protected string panelId;

        [SerializeField]
        [HideInInspector]
        protected Canvas canvas;

        [SerializeField]
        [HideInInspector]
        protected GraphicRaycaster graphicRaycaster;

        [SerializeField]
        [HideInInspector]
        protected RectTransform rectTransform;

        [SerializeField]
        [HideInInspector]
        protected CanvasGroup canvasGroup;

        [SerializeField]
        [HideInInspector]
        protected UIPanelContentBase content;

        [SerializeField]
        [HideInInspector]
        protected UIPanelBackgroundBase background;

        [SerializeField]
        protected bool resetTransform = true;

        [SerializeField]
        protected bool autoDisposeOnHide = false;

        [SerializeField]
        protected float autoDisposeDelay = 30f;
        #endregion

        #region Private Fields
        private Sequence _animationTween;
        #endregion

        #region CTor
        //  Prevent direct instantiation or inheritance outside of this assembly
        internal UIPanelBase() { }
        #endregion

        #region Unity Methods
        private void Start()
        {
            content.RectTransform.anchoredPosition = Vector2.zero;
        }

        private void OnDestroy()
        {
            DOTween.Kill(this);
        }
        #endregion

        #region Public Methods
        public virtual void Show(bool immediate = false, Action callback = null)
        {
            _animationTween?.Kill(complete: true);

            PreShowPanel?.Invoke();

            if (callback != null)
            {
                ShowStarted += callback;
            }

            ((IUIAnimatedElement)background).PreShowCallback?.Invoke();
            ((IUIAnimatedElement)content).PreShowCallback?.Invoke();

            Tween backgroundShowAnimation = immediate
                ? DOTween.Sequence()
                    .AppendCallback(() =>
                    {
                        background.RectTransform.anchoredPosition = Vector2.zero;
                        background.RectTransform.localScale = Vector3.one;
                        background.RectTransform.localRotation = Quaternion.identity;
                        background.RectTransform.localPosition = Vector3.zero;
                    })
                : ((IUIAnimatedElement)background).ShowAnimation;

            Tween contentShowAnimation = immediate
                ? DOTween.Sequence()
                    .AppendCallback(() =>
                    {
                        content.RectTransform.anchoredPosition = Vector2.zero;
                        content.RectTransform.localScale = Vector3.one;
                        content.RectTransform.localRotation = Quaternion.identity;
                        content.RectTransform.localPosition = Vector3.zero;
                    })
                : ((IUIAnimatedElement)content).ShowAnimation;


            _animationTween = DOTween.Sequence()
                .SetId(this)
                .OnStart(HandleShowStarted)
                .Join(backgroundShowAnimation)
                .Join(contentShowAnimation)
                .OnComplete(() => HandleShowEnded(callback));

            if (immediate)
            {
                _animationTween.Complete(true);
            }
        }

        public virtual void Hide(bool immediate = false, Action callback = null)
        {
            _animationTween?.Kill(complete: true);

            gameObject.SetActive(true);

            PreHidePanel?.Invoke();

            if (callback != null)
            {
                HideStarted += callback;
            }

            ((IUIAnimatedElement)background).PreHideCallback?.Invoke();
            ((IUIAnimatedElement)content).PreHideCallback?.Invoke();

            Tween backgroundHideAnimation = immediate
                ? DOTween.Sequence()
                    .AppendCallback(() =>
                    {
                        background.RectTransform.anchoredPosition = Vector2.zero;
                        background.RectTransform.localScale = Vector3.one;
                        background.RectTransform.localRotation = Quaternion.identity;
                        background.RectTransform.localPosition = Vector3.zero;
                    })
                : ((IUIAnimatedElement)background).HideAnimation;

            Tween contentHideAnimation = immediate
                ? DOTween.Sequence()
                    .AppendCallback(() =>
                    {
                        content.RectTransform.anchoredPosition = Vector2.zero;
                        content.RectTransform.localScale = Vector3.one;
                        content.RectTransform.localRotation = Quaternion.identity;
                        content.RectTransform.localPosition = Vector3.zero;
                    })
                : ((IUIAnimatedElement)content).HideAnimation;

            _animationTween = DOTween.Sequence()
                .SetId(this)
                .OnStart(HandleHideStarted)
                .Join(backgroundHideAnimation)
                .Join(contentHideAnimation)
                .OnComplete(() => HandleHideEnded(callback));

            if (immediate)
            {
                _animationTween.Complete(true);
            }
        }
        #endregion

        #region Private Methods
        protected virtual void HandleShowStarted()
        {
            gameObject.SetActive(true);

            Canvas.ForceUpdateCanvases();
            IsVisible = true;
            ShowStarted?.Invoke();
        }

        protected virtual void HandleShowEnded(Action callback)
        {
            ShowCompleted?.Invoke();

            if (callback != null)
            {
                ShowStarted -= callback;
            }

            _animationTween = null;
        }

        protected virtual void HandleHideStarted()
        {
            HideStarted?.Invoke();
        }

        protected virtual void HandleHideEnded(Action callback)
        {
            HideCompleted?.Invoke();
            gameObject.SetActive(false);
            IsVisible = false;
            if (callback != null)
            {
                HideStarted -= callback;
            }
            _animationTween = null;
        }

        public void ResetTransform()
        {
            if (!resetTransform) return;
            RectTransform.anchorMin = Vector2.zero;
            RectTransform.anchorMax = Vector2.one;
            RectTransform.offsetMin = Vector2.zero;
            RectTransform.offsetMax = Vector2.zero;
            RectTransform.pivot = new Vector2(0.5f, 0.5f);
        }
        #endregion
    }

    public abstract class UIPanelBase<SelfT, ContentT, BackgroundT> :
        UIPanelBase
        where SelfT : UIPanelBase
        where ContentT : UIPanelContentBase
        where BackgroundT : UIPanelBackgroundBase
    {

        public UIPanelBase() : base() { }

#if UNITY_EDITOR
        private void OnValidate()
        {
            canvas = GetComponent<Canvas>();
            graphicRaycaster = GetComponent<GraphicRaycaster>();
            rectTransform = GetComponent<RectTransform>();
            canvasGroup = GetComponent<CanvasGroup>();

            content = GetComponentInChildren<ContentT>();
            background = GetComponentInChildren<BackgroundT>();

            if (content == null && background == null)
            {
                GameObject backgroundObject = new GameObject("Background");
                backgroundObject.transform.SetParent(transform, false);
                var bgRtf = backgroundObject.AddComponent<RectTransform>();
                // Set anchor to stretch full screen
                bgRtf.anchorMin = new Vector2(0, 0);
                bgRtf.anchorMax = new Vector2(1, 1);
                bgRtf.offsetMin = Vector2.zero;
                bgRtf.offsetMax = Vector2.zero;
                bgRtf.localScale = Vector3.one;
                bgRtf.localPosition = Vector3.zero;
                background = backgroundObject.AddComponent<BackgroundT>();

                GameObject contentObject = new GameObject("Content");
                contentObject.transform.SetParent(transform, false);
                var contentRtf = contentObject.AddComponent<RectTransform>();
                // Set anchor to stretch full screen
                contentRtf.anchorMin = new Vector2(0, 0);
                contentRtf.anchorMax = new Vector2(1, 1);
                contentRtf.offsetMin = Vector2.zero;
                contentRtf.offsetMax = Vector2.zero;
                contentRtf.localScale = Vector3.one;
                contentRtf.localPosition = Vector3.zero;
                content = contentObject.AddComponent<ContentT>();
            }

            panelId = GetType()
                .GetCustomAttribute<UIPanelInfoAttribute>()
                ?.PanelId ?? GetType().FullName;
        }
#endif
    }
}