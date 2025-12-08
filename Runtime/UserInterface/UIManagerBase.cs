using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.MappedList;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;
using UnityEngine.UI;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
{
    public abstract class UIManagerBase<SelfT> :
        SingletonBehavior<SelfT>,
        IUIManager
        where SelfT : UIManagerBase<SelfT>
    {
        #region Events & Delegates
        public event Action<UIPanelBase> PanelInitialized;
        public event Action<UIPanelBase> PanelReleased;
        #endregion

        #region Interfaces & Properties
        public Canvas Canvas => canvas;
        public CanvasScaler CanvasScaler => canvasScaler;
        public GraphicRaycaster GraphicRaycaster => graphicRaycaster;
        public RectTransform RectTransform => rectTransform;
        public CanvasGroup CanvasGroup => canvasGroup;
        #endregion

        #region Serialized Fields
        [SerializeField]
        [HideInInspector]
        private Canvas canvas;

        [SerializeField]
        [HideInInspector]
        private CanvasScaler canvasScaler;

        [SerializeField]
        [HideInInspector]
        private GraphicRaycaster graphicRaycaster;

        [SerializeField]
        [HideInInspector]
        private RectTransform rectTransform;

        [SerializeField]
        [HideInInspector]
        private CanvasGroup canvasGroup;

        [SerializeField]
        private UIPanelBase[] panelPrefabs;
        #endregion

        #region Private Fields
        private MappedList<Type, UIPanelBase> _panelMapping;
        private Dictionary<Type, UIPanelInstanceInfo> _panelInstances;
        private Queue<UIPanelBase> _autoDisposeQueue;
        private bool _isInitialized = false;
        #endregion

        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            _panelMapping = new MappedList<Type, UIPanelBase>(panelPrefabs, (p) => p.GetType());
            _panelInstances = new Dictionary<Type, UIPanelInstanceInfo>();
            _autoDisposeQueue = new Queue<UIPanelBase>();
            _isInitialized = true;

            this.RegisterManager();
        }

        private void Update()
        {
            if (!_isInitialized) return;

            foreach (UIPanelInstanceInfo panelInstanceInfo in _panelInstances.Values)
            {
                ScanUnunsedPanelInstance(panelInstanceInfo);
            }

            while (_autoDisposeQueue.Count > 0)
            {
                ResolvePanelDisposalRequest();
            }
        }

        protected override void OnDestroy()
        {
            this.UnregisterManager();
            base.OnDestroy();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (canvas == null) canvas = GetComponent<Canvas>();
            if (canvasScaler == null) canvasScaler = GetComponent<CanvasScaler>();
            if (graphicRaycaster == null) graphicRaycaster = GetComponent<GraphicRaycaster>();
            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        }
#endif
        #endregion

        #region Public Methods
        public T RequirePanel<T>()
            where T : UIPanelBase
        {
            Type panelType = typeof(T);
            RequirePanelInternal(panelType, out UIPanelInstanceInfo panelInstance);
            return (T)panelInstance.Panel;
        }

        public void ReleasePanel<T>()
            where T : UIPanelBase
        {
            Type panelType = typeof(T);
            DisposePanelInternal(panelType);
        }
        #endregion

        #region Private Methods
        private void ResolvePanelDisposalRequest()
        {
            UIPanelBase panelToDispose = _autoDisposeQueue.Dequeue();
            if (panelToDispose == null) return;
            if (panelToDispose.IsVisible) return;
            DisposePanelInternal(panelToDispose.GetType());
        }

        private void ScanUnunsedPanelInstance(UIPanelInstanceInfo panelInstanceInfo)
        {
            if (panelInstanceInfo.AutoDisposeTimer.HasValue)
            {
                CalculatePanelDisposalTimer(panelInstanceInfo);
                return;
            }

            if (panelInstanceInfo.Panel.AutoDisposeOnHide)
            {
                panelInstanceInfo.AutoDisposeTimer = panelInstanceInfo.Panel.AutoDisposeDelay;
            }
        }

        private void CalculatePanelDisposalTimer(UIPanelInstanceInfo panelInstanceInfo)
        {
            if (!panelInstanceInfo.Panel.IsVisible)
            {
                panelInstanceInfo.AutoDisposeTimer = null;
                return;
            }

            panelInstanceInfo.AutoDisposeTimer -= Time.unscaledDeltaTime;
            if (panelInstanceInfo.AutoDisposeTimer <= 0f)
            {
                _autoDisposeQueue.Enqueue(panelInstanceInfo.Panel);
            }
        }

        private void RequirePanelInternal(Type panelType, out UIPanelInstanceInfo panelInstanceInfo)
        {
            panelInstanceInfo = null;

            if (_panelInstances.TryGetValue(panelType, out UIPanelInstanceInfo panelInstance))
            {
                panelInstanceInfo = panelInstance;
                return;
            }

            UIPanelBase newPanelInstance = null;

            if (_panelMapping.TryGetValue(panelType, out UIPanelBase panelPrefab))
            {
                newPanelInstance = Instantiate(panelPrefab, rectTransform);
            }

            if (newPanelInstance == null)
            {
                QuickLog.SCritical(
                    "Cannot instantiate panel of type {0} because it is not registered in the panel mapping.",
                    panelType.Name
                );
                return;
            }

            newPanelInstance.UIManager = this;
            newPanelInstance.ResetTransform();
            newPanelInstance.RectTransform.localScale = Vector3.one;
            newPanelInstance.Canvas.overrideSorting = true;
            _panelInstances[panelType] = new UIPanelInstanceInfo
            {
                Panel = newPanelInstance,
                AutoDisposeTimer = null
            };
            PanelInitialized?.Invoke(newPanelInstance);
            newPanelInstance.gameObject.SetActive(false);

            panelInstanceInfo = _panelInstances[panelType];
        }

        private void DisposePanelInternal(Type panelType)
        {
            if (_panelInstances.TryGetValue(panelType, out UIPanelInstanceInfo panelInstance))
            {
                QuickLog.SInfo(
                    "Disposing panel of type {0}.",
                    panelType.Name
                );

                PanelReleased?.Invoke(panelInstance.Panel);
                _panelInstances.Remove(panelType);
                Destroy(panelInstance.Panel.gameObject);
            }
            else
            {
                QuickLog.SWarning(
                    "Cannot dispose panel of type {0} because it is not currently instantiated.",
                    panelType.Name
                );
                return;
            }
        }
        #endregion

        #region Nested Types
        private class UIPanelInstanceInfo
        {
            public UIPanelBase Panel { get; set; }
            public float? AutoDisposeTimer { get; set; }
        }
        #endregion
    }
}