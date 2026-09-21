using System;
using System.Reflection;
using System.Threading;
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
        internal IUIManager UIManager { get; set; }
        public bool IsBusy => _animation != null;
        public UIPanelBackgroundBase Background => background;
        public UIPanelContentBase Content => content;
        public bool AutoDisposeOnHide => autoDisposeOnHide;
        public float AutoDisposeDelay => autoDisposeDelay;
        public bool IsPanelReady => _isReady;
        internal bool HasBeenShown { get; private set; }
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

        [SerializeField]
        protected float showAnimationDelay = 0.0f;

        [SerializeField]
        protected float hideAnimationDelay = 0.0f;
        #endregion

        #region Private Fields
        private PanelAnimation _animation;
        private bool _isReady = false;
        private int _animationRequestVersion;
        #endregion

        #region CTor
        // Unity owns construction, but application assemblies may derive panels.
        protected UIPanelBase() { }
        #endregion

        #region Unity Methods
        protected virtual void Awake()
        {
            _isReady = true;
            Canvas.enabled = false;
            ResetTransform();
        }

        protected virtual void Start()
        {
            _isReady = true;
        }

        private void OnDestroy()
        {
            _animationRequestVersion++;
            CancelCurrentAnimation();
        }
        #endregion

        #region Public Methods
        public virtual void Show(bool immediate = false, Action callback = null)
        {
            int requestVersion = ++_animationRequestVersion;
            if (!Canvas.enabled) Canvas.enabled = true;
            if (!TryContinueAnimationRequest(requestVersion, callback)) return;

            CompleteCurrentAnimation();
            if (!TryContinueAnimationRequest(requestVersion, callback)) return;

            gameObject.SetActive(true);
            if (!TryContinueAnimationRequest(requestVersion, callback)) return;

            PreShowPanel?.Invoke();
            if (!TryContinueAnimationRequest(requestVersion, callback)) return;

            ((IUIAnimatedElement)background).PreShowCallback?.Invoke();
            if (!TryContinueAnimationRequest(requestVersion, callback)) return;

            ((IUIAnimatedElement)content).PreShowCallback?.Invoke();
            if (!TryContinueAnimationRequest(requestVersion, callback)) return;

            if (immediate)
            {
                ResetAnimatedElementTransforms();
                HandleShowStarted();
                if (!TryContinueAnimationRequest(requestVersion, callback)) return;

                HandleShowEnded(callback);
                return;
            }

            StartAnimation(
                ((IUIAnimatedElement)background).ShowAnimation,
                ((IUIAnimatedElement)content).ShowAnimation,
                showAnimationDelay,
                HandleShowStarted,
                () => HandleShowEnded(callback),
                requestVersion
            );
        }

        public virtual void Hide(bool immediate = false, Action callback = null)
        {
            int requestVersion = ++_animationRequestVersion;
            CompleteCurrentAnimation();
            if (!TryContinueAnimationRequest(requestVersion, callback)) return;

            gameObject.SetActive(true);
            if (!TryContinueAnimationRequest(requestVersion, callback)) return;

            PreHidePanel?.Invoke();
            if (!TryContinueAnimationRequest(requestVersion, callback)) return;

            ((IUIAnimatedElement)background).PreHideCallback?.Invoke();
            if (!TryContinueAnimationRequest(requestVersion, callback)) return;

            ((IUIAnimatedElement)content).PreHideCallback?.Invoke();
            if (!TryContinueAnimationRequest(requestVersion, callback)) return;

            if (immediate)
            {
                ResetAnimatedElementTransforms();
                HandleHideStarted();
                if (!TryContinueAnimationRequest(requestVersion, callback)) return;

                HandleHideEnded(callback);
                return;
            }

            StartAnimation(
                ((IUIAnimatedElement)background).HideAnimation,
                ((IUIAnimatedElement)content).HideAnimation,
                hideAnimationDelay,
                HandleHideStarted,
                () => HandleHideEnded(callback),
                requestVersion
            );
        }
        #endregion

        #region Private Methods
        private void StartAnimation(
            AnimationHandle backgroundAnimation,
            AnimationHandle contentAnimation,
            float delay,
            Action started,
            Action completed,
            int requestVersion
        )
        {
            backgroundAnimation ??= AnimationHandle.CreateCompleted();
            contentAnimation ??= AnimationHandle.CreateCompleted();

            var animation = new PanelAnimation(
                backgroundAnimation,
                contentAnimation,
                completed
            );
            _animation = animation;

            try
            {
                started?.Invoke();
                if (
                    !IsCurrentAnimationRequest(requestVersion)
                    || !ReferenceEquals(_animation, animation)
                )
                {
                    if (ReferenceEquals(_animation, animation))
                    {
                        CancelAnimation(animation);
                    }
                    return;
                }

                _ = RunAnimationAsync(animation, delay);
            }
            catch
            {
                if (ReferenceEquals(_animation, animation))
                {
                    CancelAnimation(animation);
                }
                throw;
            }
        }

        private async Awaitable RunAnimationAsync(
            PanelAnimation animation,
            float delay
        )
        {
            try
            {
                if (delay > 0f)
                {
                    await Awaitable.WaitForSecondsAsync(
                        delay,
                        animation.CancellationToken
                    );
                }

                animation.Play();
                await animation.Background;
                await animation.Content;
                CompleteAnimation(animation);
            }
            catch (OperationCanceledException)
            {
                if (!animation.IsCompleting)
                {
                    CancelAnimation(animation);
                }
            }
            catch (Exception exception)
            {
                CancelAnimation(animation);
                Debug.LogException(exception, this);
            }
        }

        private bool TryContinueAnimationRequest(
            int requestVersion,
            Action callback
        )
        {
            if (IsCurrentAnimationRequest(requestVersion))
            {
                return true;
            }

            callback?.Invoke();
            return false;
        }

        private bool IsCurrentAnimationRequest(int requestVersion)
        {
            return _animationRequestVersion == requestVersion;
        }

        private void CompleteCurrentAnimation()
        {
            PanelAnimation animation = _animation;
            if (animation == null)
            {
                return;
            }

            animation.Complete();
            CompleteAnimation(animation);
        }

        private void CancelCurrentAnimation()
        {
            PanelAnimation animation = _animation;
            if (animation == null)
            {
                return;
            }

            _animation = null;
            animation.Cancel();
            animation.Dispose();
        }

        private void CompleteAnimation(PanelAnimation animation)
        {
            if (!ReferenceEquals(_animation, animation))
            {
                return;
            }

            _animation = null;
            animation.Dispose();
            animation.InvokeCompletion();
        }

        private void CancelAnimation(PanelAnimation animation)
        {
            if (!ReferenceEquals(_animation, animation))
            {
                return;
            }

            _animation = null;
            animation.Cancel();
            animation.Dispose();
        }

        private void ResetAnimatedElementTransforms()
        {
            ResetAnimatedElementTransform(background.RectTransform);
            ResetAnimatedElementTransform(content.RectTransform);
        }

        private static void ResetAnimatedElementTransform(RectTransform target)
        {
            target.anchoredPosition = Vector2.zero;
            target.localScale = Vector3.one;
            target.localRotation = Quaternion.identity;
            target.localPosition = Vector3.zero;
        }

        protected virtual void HandleShowStarted()
        {
            Canvas.ForceUpdateCanvases();
            IsVisible = true;
            HasBeenShown = true;
            ShowStarted?.Invoke();
        }

        protected virtual void HandleShowEnded(Action callback)
        {
            ShowCompleted?.Invoke();
            callback?.Invoke();
        }

        protected virtual void HandleHideStarted()
        {
            HideStarted?.Invoke();
        }

        protected virtual void HandleHideEnded(Action callback)
        {
            IsVisible = false;
            gameObject.SetActive(false);
            HideCompleted?.Invoke();
            callback?.Invoke();
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

        #region Nested Types

        private sealed class PanelAnimation : IDisposable
        {
            private readonly CancellationTokenSource _cancellationTokenSource = new();
            private readonly Action _completion;
            private bool _isStopping;
            private bool _isDisposed;

            public AnimationHandle Background { get; }
            public AnimationHandle Content { get; }
            public CancellationToken CancellationToken => _cancellationTokenSource.Token;
            public bool IsCompleting { get; private set; }

            public PanelAnimation(
                AnimationHandle background,
                AnimationHandle content,
                Action completion
            )
            {
                Background = background;
                Content = content;
                _completion = completion;
            }

            public void Play()
            {
                Background.Play();
                Content.Play();
            }

            public void Complete()
            {
                if (_isStopping || _isDisposed)
                {
                    return;
                }

                _isStopping = true;
                IsCompleting = true;
                _cancellationTokenSource.Cancel();
                Background.Complete();
                Content.Complete();
            }

            public void Cancel()
            {
                if (_isStopping || _isDisposed)
                {
                    return;
                }

                _isStopping = true;
                _cancellationTokenSource.Cancel();
                Background.Cancel();
                Content.Cancel();
            }

            public void InvokeCompletion()
            {
                _completion?.Invoke();
            }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _cancellationTokenSource.Dispose();
            }
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
