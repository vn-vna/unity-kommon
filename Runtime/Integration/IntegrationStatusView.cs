using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Integration.Converter;
using Com.Hapiga.Scheherazade.Common.Integration.IAR;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase;
using Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig;
using Com.Hapiga.Scheherazade.Common.Integration.Segmentation;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;
using Com.Hapiga.Scheherazade.Common.Singleton;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Com.Hapiga.Scheherazade.Common.Integration
{
    [DisallowMultipleComponent]
    public sealed class IntegrationStatusView : SingletonBehavior<IntegrationStatusView>
    {
        private const string CanvasObjectName = "Integration Status Canvas";
        private const string ExpandedPanelObjectName = "Integration Status Panel";
        private const string ExpandedContentObjectName = "Integration Status Content";
        private const string ExpandedToggleButtonObjectName = "Expanded Toggle Button";
        private const string ExpandedHideButtonObjectName = "Expanded Hide Button";
        private const string MinimizedPanelObjectName = "Integration Status Minimized Panel";
        private const string MinimizedTextObjectName = "Minimized Summary";

        [Header("Expanded Panel")]
        [SerializeField] private Vector2 panelSize = new Vector2(960f, 720f);
        [SerializeField] private bool autoCalculateExpandedPanelSize;
        [SerializeField] private HorizontalPanelAlignment horizontalAlignment = HorizontalPanelAlignment.Left;
        [SerializeField] private VerticalPanelAlignment verticalAlignment = VerticalPanelAlignment.Top;
        [SerializeField] private Vector2 panelOffset = new Vector2(24f, -24f);
        [SerializeField] private Vector4 panelPadding = new Vector4(24f, 24f, 24f, 24f);
        [SerializeField] private float moduleSpacing = 10f;
        [SerializeField] private float providerSpacing = 4f;
        [SerializeField] private float providerIndent = 28f;
        [SerializeField] private float statusColumnWidth = 240f;

        [Header("Minimized Panel")]
        [SerializeField] private Vector2 minimizedPanelSize = new Vector2(480f, 80f);
        [SerializeField] private bool autoCalculateMinimizedPanelSize = true;
        [SerializeField] private HorizontalPanelAlignment minimizedHorizontalAlignment = HorizontalPanelAlignment.Right;
        [SerializeField] private VerticalPanelAlignment minimizedVerticalAlignment = VerticalPanelAlignment.Top;
        [SerializeField] private Vector2 minimizedPanelOffset = new Vector2(-24f, -24f);
        [SerializeField] private Vector4 minimizedPanelPadding = new Vector4(20f, 16f, 20f, 16f);
        [SerializeField] private float minimizedItemSpacing = 12f;
        [SerializeField] private float minimizedTextSize = 24f;
        [SerializeField] private bool minimizedOnEnable;

        [Header("General")]
        [SerializeField] private int sortingOrder = 1000;
        [SerializeField] private float refreshInterval = 0.5f;
        [SerializeField] private bool panelVisibleOnEnable = true;
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.75f);

        [Header("Text")]
        [SerializeField] private TMP_FontAsset fontAsset;
        [SerializeField] private float moduleFontSize = 28f;
        [SerializeField] private float providerFontSize = 24f;
        [SerializeField] private FontStyles moduleFontStyle = FontStyles.Bold;
        [SerializeField] private FontStyles providerFontStyle = FontStyles.Normal;
        [SerializeField] private Color labelColor = Color.white;

        [Header("Toggle Button")]
        [SerializeField] private Vector2 toggleButtonSize = new Vector2(140f, 48f);
        [SerializeField] private float toggleButtonFontSize = 22f;
        [SerializeField] private Color toggleButtonTextColor = Color.white;
        [SerializeField] private Color toggleButtonBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.95f);

        [Header("Status Colors")]
        [SerializeField] private Color initializedColor = new Color(0.35f, 0.85f, 0.45f);
        [SerializeField] private Color initializingColor = new Color(0.35f, 0.75f, 1f);
        [SerializeField] private Color uninitializedColor = new Color(1f, 0.8f, 0.2f);
        [SerializeField] private Color unboundColor = new Color(1f, 0.35f, 0.35f);
        [SerializeField] private Color refreshingColor = new Color(0.85f, 0.5f, 1f);
        [SerializeField] private Color disabledColor = new Color(0.7f, 0.7f, 0.7f);
        [SerializeField] private Color failedColor = new Color(1f, 0.25f, 0.25f);
        [SerializeField] private Color unknownColor = new Color(0.8f, 0.8f, 0.8f);

        private readonly List<ModuleRowView> _moduleRows = new List<ModuleRowView>();

        private Canvas _canvas;
        private RectTransform _expandedPanelRect;
        private RectTransform _expandedContentRect;
        private RectTransform _minimizedPanelRect;
        private Image _expandedPanelBackground;
        private Image _minimizedPanelBackground;
        private VerticalLayoutGroup _expandedContentLayout;
        private HorizontalLayoutGroup _minimizedPanelLayout;
        private ContentSizeFitter _minimizedPanelFitter;
        private TextMeshProUGUI _minimizedSummaryText;
        private LayoutElement _minimizedSummaryLayout;
        private Button _expandedToggleButton;
        private Button _expandedHideButton;
        private Button _minimizedPanelButton;
        private TextMeshProUGUI _expandedToggleButtonText;
        private TextMeshProUGUI _expandedHideButtonText;
        private float _nextRefreshTime;
        private bool _isPanelVisible = true;
        private bool _isMinimized;

        public bool IsPanelVisible => _isPanelVisible;

        public bool IsMinimized => _isMinimized;

        protected override void Awake()
        {
            base.Awake();
            _isPanelVisible = panelVisibleOnEnable;
            _isMinimized = minimizedOnEnable;
            EnsureCanvas();
            ApplyPanelState();
            RefreshView();
        }

        private void OnEnable()
        {
            EnsureCanvas();
            ApplyCanvasSettings();
            ApplyPanelState();
            _nextRefreshTime = 0f;
            RefreshView();
        }

        private void Update()
        {
            if (!_isPanelVisible)
            {
                return;
            }

            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
            RefreshView();
        }

        private void OnValidate()
        {
            refreshInterval = Mathf.Max(0.1f, refreshInterval);
            moduleFontSize = Mathf.Max(1f, moduleFontSize);
            providerFontSize = Mathf.Max(1f, providerFontSize);
            minimizedTextSize = Mathf.Max(1f, minimizedTextSize);
            toggleButtonFontSize = Mathf.Max(1f, toggleButtonFontSize);
            providerIndent = Mathf.Max(0f, providerIndent);
            statusColumnWidth = Mathf.Max(120f, statusColumnWidth);
            toggleButtonSize.x = Mathf.Max(60f, toggleButtonSize.x);
            toggleButtonSize.y = Mathf.Max(28f, toggleButtonSize.y);
            panelSize.x = Mathf.Max(120f, panelSize.x);
            panelSize.y = Mathf.Max(80f, panelSize.y);
            minimizedPanelSize.x = Mathf.Max(120f, minimizedPanelSize.x);
            minimizedPanelSize.y = Mathf.Max(48f, minimizedPanelSize.y);

            if (!Application.isPlaying)
            {
                return;
            }

            EnsureCanvas();
            ApplyCanvasSettings();
            ApplyPanelState();
            RefreshView();
        }

        public void ShowPanel()
        {
            SetPanelVisible(true);
        }

        public void HidePanel()
        {
            SetPanelVisible(false);
        }

        public void SetPanelVisible(bool isVisible)
        {
            _isPanelVisible = isVisible;
            ApplyPanelState();
        }

        public void ToggleMinimized()
        {
            SetMinimized(!_isMinimized);
        }

        public void SetMinimized(bool isMinimized)
        {
            _isMinimized = isMinimized;
            ApplyPanelState();
            RefreshView();
        }

        private void EnsureCanvas()
        {
            if (_canvas != null && _expandedPanelRect != null && _expandedContentRect != null && _minimizedPanelRect != null)
            {
                return;
            }

            Transform canvasTransform = transform.Find(CanvasObjectName);
            if (canvasTransform == null)
            {
                GameObject canvasObject = new GameObject(CanvasObjectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(transform, false);
                canvasTransform = canvasObject.transform;
            }

            _canvas = canvasTransform.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;

            CanvasScaler canvasScaler = canvasTransform.GetComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight = 0.5f;

            Transform expandedPanelTransform = canvasTransform.Find(ExpandedPanelObjectName);
            if (expandedPanelTransform == null)
            {
                GameObject expandedPanelObject = new GameObject(ExpandedPanelObjectName, typeof(RectTransform), typeof(Image));
                expandedPanelObject.transform.SetParent(canvasTransform, false);
                expandedPanelTransform = expandedPanelObject.transform;
            }

            _expandedPanelRect = expandedPanelTransform.GetComponent<RectTransform>();
            _expandedPanelBackground = expandedPanelTransform.GetComponent<Image>();

            Transform expandedContentTransform = expandedPanelTransform.Find(ExpandedContentObjectName);
            if (expandedContentTransform == null)
            {
                GameObject contentObject = new GameObject(ExpandedContentObjectName, typeof(RectTransform), typeof(VerticalLayoutGroup));
                contentObject.transform.SetParent(expandedPanelTransform, false);
                expandedContentTransform = contentObject.transform;
            }

            _expandedContentRect = expandedContentTransform.GetComponent<RectTransform>();
            _expandedContentLayout = expandedContentTransform.GetComponent<VerticalLayoutGroup>();

            Transform expandedButtonTransform = expandedPanelTransform.Find(ExpandedToggleButtonObjectName);
            if (expandedButtonTransform == null)
            {
                CreateButton(ExpandedToggleButtonObjectName, expandedPanelTransform, out _expandedToggleButton, out _expandedToggleButtonText);
            }
            else
            {
                _expandedToggleButton = expandedButtonTransform.GetComponent<Button>();
                _expandedToggleButtonText = expandedButtonTransform.GetComponentInChildren<TextMeshProUGUI>();
            }

            Transform expandedHideButtonTransform = expandedPanelTransform.Find(ExpandedHideButtonObjectName);
            if (expandedHideButtonTransform == null)
            {
                CreateButton(ExpandedHideButtonObjectName, expandedPanelTransform, out _expandedHideButton, out _expandedHideButtonText);
            }
            else
            {
                _expandedHideButton = expandedHideButtonTransform.GetComponent<Button>();
                _expandedHideButtonText = expandedHideButtonTransform.GetComponentInChildren<TextMeshProUGUI>();
            }

            ConfigureExpandedButton(_expandedToggleButton, _expandedToggleButtonText, "M", ToggleMinimized);
            ConfigureExpandedButton(_expandedHideButton, _expandedHideButtonText, "H", HidePanel);

            Transform minimizedPanelTransform = canvasTransform.Find(MinimizedPanelObjectName);
            if (minimizedPanelTransform == null)
            {
                GameObject minimizedPanelObject = new GameObject(MinimizedPanelObjectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
                minimizedPanelObject.transform.SetParent(canvasTransform, false);
                minimizedPanelTransform = minimizedPanelObject.transform;
            }

            _minimizedPanelRect = minimizedPanelTransform.GetComponent<RectTransform>();
            _minimizedPanelBackground = minimizedPanelTransform.GetComponent<Image>();
            _minimizedPanelButton = minimizedPanelTransform.GetComponent<Button>();
            _minimizedPanelLayout = minimizedPanelTransform.GetComponent<HorizontalLayoutGroup>();

            _minimizedPanelFitter = minimizedPanelTransform.GetComponent<ContentSizeFitter>();

            Transform minimizedTextTransform = minimizedPanelTransform.Find(MinimizedTextObjectName);
            if (minimizedTextTransform == null)
            {
                _minimizedSummaryText = CreateText(MinimizedTextObjectName, minimizedPanelTransform, minimizedTextSize, FontStyles.Normal, labelColor);
            }
            else
            {
                _minimizedSummaryText = minimizedTextTransform.GetComponent<TextMeshProUGUI>();
            }

            _minimizedSummaryLayout = _minimizedSummaryText.GetComponent<LayoutElement>();
            _minimizedSummaryLayout.flexibleWidth = 0f;
            _minimizedSummaryLayout.minHeight = minimizedTextSize + 4f;
            ConfigureMinimizedPanelButton();
            ApplyCanvasSettings();
        }

        private void ApplyCanvasSettings()
        {
            if (_canvas == null || _expandedPanelRect == null || _expandedContentRect == null || _expandedContentLayout == null || _expandedPanelBackground == null || _minimizedPanelRect == null || _minimizedPanelBackground == null || _minimizedPanelLayout == null)
            {
                return;
            }

            _canvas.sortingOrder = sortingOrder;

            Vector2 expandedAnchor = new Vector2(GetHorizontalAnchor(horizontalAlignment), GetVerticalAnchor(verticalAlignment));
            _expandedPanelRect.anchorMin = expandedAnchor;
            _expandedPanelRect.anchorMax = expandedAnchor;
            _expandedPanelRect.pivot = expandedAnchor;
            _expandedPanelRect.anchoredPosition = panelOffset;
            _expandedPanelBackground.color = backgroundColor;

            float headerHeight = toggleButtonSize.y + 8f;
            _expandedContentRect.anchorMin = Vector2.zero;
            _expandedContentRect.anchorMax = Vector2.one;
            _expandedContentRect.pivot = new Vector2(0.5f, 0.5f);
            _expandedContentRect.offsetMin = new Vector2(panelPadding.x, panelPadding.w);
            _expandedContentRect.offsetMax = new Vector2(-panelPadding.z, -(panelPadding.y + headerHeight));

            _expandedContentLayout.childAlignment = TextAnchor.UpperLeft;
            _expandedContentLayout.childControlHeight = true;
            _expandedContentLayout.childControlWidth = true;
            _expandedContentLayout.childForceExpandHeight = false;
            _expandedContentLayout.childForceExpandWidth = true;
            _expandedContentLayout.spacing = moduleSpacing;
            _expandedContentLayout.padding = new RectOffset(0, 0, 0, 0);

            ConfigureExpandedButtonRects();

            Vector2 minimizedAnchor = new Vector2(GetHorizontalAnchor(minimizedHorizontalAlignment), GetVerticalAnchor(minimizedVerticalAlignment));
            _minimizedPanelRect.anchorMin = minimizedAnchor;
            _minimizedPanelRect.anchorMax = minimizedAnchor;
            _minimizedPanelRect.pivot = minimizedAnchor;
            _minimizedPanelRect.anchoredPosition = minimizedPanelOffset;
            _minimizedPanelBackground.color = backgroundColor;

            _minimizedPanelLayout.childAlignment = TextAnchor.MiddleLeft;
            _minimizedPanelLayout.childControlHeight = true;
            _minimizedPanelLayout.childControlWidth = false;
            _minimizedPanelLayout.childForceExpandHeight = false;
            _minimizedPanelLayout.childForceExpandWidth = false;
            _minimizedPanelLayout.spacing = minimizedItemSpacing;
            _minimizedPanelLayout.padding = new RectOffset(
                Mathf.RoundToInt(minimizedPanelPadding.x),
                Mathf.RoundToInt(minimizedPanelPadding.z),
                Mathf.RoundToInt(minimizedPanelPadding.y),
                Mathf.RoundToInt(minimizedPanelPadding.w));

            _minimizedPanelFitter.horizontalFit = autoCalculateMinimizedPanelSize ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            _minimizedPanelFitter.verticalFit = autoCalculateMinimizedPanelSize ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;

            ApplyTextStyle(_minimizedSummaryText, minimizedTextSize, FontStyles.Normal, labelColor);
            ConfigureExpandedButton(_expandedToggleButton, _expandedToggleButtonText, "M", ToggleMinimized);
            ConfigureExpandedButton(_expandedHideButton, _expandedHideButtonText, "H", HidePanel);
            ConfigureMinimizedPanelButton();
            ApplyPanelSizeSettings();
        }

        private void ConfigureExpandedButtonRects()
        {
            ConfigureExpandedButtonRect(_expandedHideButton, 0);
            ConfigureExpandedButtonRect(_expandedToggleButton, 1);
        }

        private void ConfigureExpandedButtonRect(Button button, int indexFromRight)
        {
            if (button == null)
            {
                return;
            }

            RectTransform buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 1f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(1f, 1f);
            buttonRect.anchoredPosition = new Vector2(-panelPadding.z - (toggleButtonSize.x + minimizedItemSpacing) * indexFromRight, -panelPadding.y);
            buttonRect.sizeDelta = toggleButtonSize;
        }

        private void RefreshView()
        {
            EnsureCanvas();

            List<ModuleSnapshot> modules = CollectModules();
            if (_moduleRows.Count != modules.Count)
            {
                RebuildRows(modules);
            }
            else
            {
                for (int i = 0; i < modules.Count; i++)
                {
                    _moduleRows[i].Apply(modules[i], this);
                }
            }

            _minimizedSummaryText.text = BuildMinimizedSummary(modules);
            ApplyPanelSizeSettings();
        }

        private void RebuildRows(List<ModuleSnapshot> modules)
        {
            for (int i = _expandedContentRect.childCount - 1; i >= 0; i--)
            {
                Destroy(_expandedContentRect.GetChild(i).gameObject);
            }

            _moduleRows.Clear();

            for (int i = 0; i < modules.Count; i++)
            {
                ModuleRowView row = new ModuleRowView(_expandedContentRect, this);
                row.Apply(modules[i], this);
                _moduleRows.Add(row);
            }
        }

        private List<ModuleSnapshot> CollectModules()
        {
            return new List<ModuleSnapshot>
            {
                CreateManagerSnapshot("Ads Manager", Integration.AdsManager, new[] { "Provider", "_provider" }),
                CreateManagerSnapshot("Tracking Manager", Integration.TrackingManager, new[] { "Providers", "_providers" }),
                CreateManagerSnapshot("IAP Manager", Integration.InAppPurchaseManager, new[] { "Provider", "_provider" }),
                CreateManagerSnapshot("Remote Config Manager", Integration.RemoteConfigManager, new[] { "Providers", "_providers" }),
                CreateManagerSnapshot("In-App Review Manager", Integration.InAppReviewManager, new[] { "Module", "_module" }),
                CreateSegmentationSnapshot(),
                CreateManagerSnapshot("Currency Converter", Integration.CurrencyConverter, new[] { "Modules", "_modules" })
            };
        }

        private ModuleSnapshot CreateManagerSnapshot(string moduleName, object manager, string[] providerMemberNames)
        {
            if (manager == null)
            {
                return new ModuleSnapshot(moduleName, DisplayStatus.Unbound, new List<ProviderSnapshot>());
            }

            return new ModuleSnapshot(moduleName, GetManagerStatus(manager), ExtractProviders(manager, providerMemberNames));
        }

        private ModuleSnapshot CreateSegmentationSnapshot()
        {
            IUserSegmentation segmentation = Integration.UserSegmentation;
            if (segmentation == null)
            {
                return new ModuleSnapshot("User Segmentation", DisplayStatus.Unbound, new List<ProviderSnapshot>());
            }

            return new ModuleSnapshot("User Segmentation", GetSegmentationStatus(segmentation), new List<ProviderSnapshot>());
        }

        private List<ProviderSnapshot> ExtractProviders(object manager, string[] providerMemberNames)
        {
            List<ProviderSnapshot> providers = new List<ProviderSnapshot>();
            HashSet<object> seenProviders = new HashSet<object>();

            for (int i = 0; i < providerMemberNames.Length; i++)
            {
                object value = GetMemberValue(manager, providerMemberNames[i]);
                AppendProviders(value, providers, seenProviders);
            }

            return providers;
        }

        private void AppendProviders(object value, List<ProviderSnapshot> providers, HashSet<object> seenProviders)
        {
            if (value == null)
            {
                return;
            }

            if (!(value is string) && value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    if (item == null || !seenProviders.Add(item))
                    {
                        continue;
                    }

                    providers.Add(new ProviderSnapshot(GetDisplayName(item), GetProviderStatus(item)));
                }

                return;
            }

            if (!seenProviders.Add(value))
            {
                return;
            }

            providers.Add(new ProviderSnapshot(GetDisplayName(value), GetProviderStatus(value)));
        }

        private DisplayStatus GetManagerStatus(object manager)
        {
            object statusValue = GetMemberValue(manager, "Status");
            if (statusValue != null)
            {
                return NormalizeStatus(statusValue.ToString());
            }

            return DisplayStatus.Uninitialized;
        }

        private DisplayStatus GetProviderStatus(object provider)
        {
            object statusValue = GetMemberValue(provider, "Status");
            if (statusValue != null)
            {
                return NormalizeStatus(statusValue.ToString());
            }

            object isInitialized = GetMemberValue(provider, "IsInitialized");
            if (isInitialized is bool initialized)
            {
                return initialized ? DisplayStatus.Initialized : DisplayStatus.Uninitialized;
            }

            object isReady = GetMemberValue(provider, "IsReady");
            if (isReady is bool ready)
            {
                return ready ? DisplayStatus.Initialized : DisplayStatus.Uninitialized;
            }

            return DisplayStatus.Unknown;
        }

        private DisplayStatus GetSegmentationStatus(IUserSegmentation segmentation)
        {
            DisplayStatus status = NormalizeStatus(segmentation.Status.ToString());
            if (status != DisplayStatus.Unknown)
            {
                return status;
            }

            object isFirstSegmentDetermined = GetMemberValue(segmentation, "IsFirstSegmentDetermined");
            if (isFirstSegmentDetermined is bool determined)
            {
                return determined ? DisplayStatus.Initialized : DisplayStatus.Uninitialized;
            }

            if (segmentation.CurrentSegmentDeclaration != null || segmentation.SegmentInformation != null)
            {
                return DisplayStatus.Initialized;
            }

            return DisplayStatus.Uninitialized;
        }

        private void ApplyPanelState()
        {
            if (_expandedPanelRect == null || _minimizedPanelRect == null)
            {
                return;
            }

            bool showExpanded = _isPanelVisible && !_isMinimized;
            bool showMinimized = _isPanelVisible && _isMinimized;

            _expandedPanelRect.gameObject.SetActive(showExpanded);
            _minimizedPanelRect.gameObject.SetActive(showMinimized);

            if (_expandedToggleButtonText != null)
            {
                _expandedToggleButtonText.text = "M";
            }

            if (_expandedHideButtonText != null)
            {
                _expandedHideButtonText.text = "H";
            }
        }

        private string BuildMinimizedSummary(List<ModuleSnapshot> modules)
        {
            int maxShortNameLength = 0;
            int maxProviderCountDigits = 1;
            for (int i = 0; i < modules.Count; i++)
            {
                ModuleSnapshot module = modules[i];
                string shortName = GetModuleShortName(module.Name);
                int initializedProviders = CountProvidersWithStatus(module.Providers, DisplayStatus.Initialized);
                int uninitializedProviders = module.Providers.Count - initializedProviders;

                maxShortNameLength = Mathf.Max(maxShortNameLength, shortName.Length);
                maxProviderCountDigits = Mathf.Max(maxProviderCountDigits, initializedProviders.ToString().Length, uninitializedProviders.ToString().Length);
            }

            StringBuilder builder = new StringBuilder(modules.Count * 32);
            for (int i = 0; i < modules.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                ModuleSnapshot module = modules[i];
                string shortName = GetModuleShortName(module.Name);
                int initializedProviders = CountProvidersWithStatus(module.Providers, DisplayStatus.Initialized);
                int uninitializedProviders = module.Providers.Count - initializedProviders;
                string paddedShortName = shortName.PadRight(maxShortNameLength);
                string paddedInitializedProviders = initializedProviders.ToString().PadLeft(maxProviderCountDigits);
                string paddedUninitializedProviders = uninitializedProviders.ToString().PadLeft(maxProviderCountDigits);

                builder.Append("<mspace=0.6em>");
                builder.Append("<color=#");
                builder.Append(ColorUtility.ToHtmlStringRGBA(GetStatusColor(module.Status)));
                builder.Append(">");
                builder.Append(paddedShortName);
                builder.Append("</color>");
                builder.Append(" <color=#");
                builder.Append(ColorUtility.ToHtmlStringRGBA(labelColor));
                builder.Append("> - </color>");
                builder.Append("<color=#");
                builder.Append(ColorUtility.ToHtmlStringRGBA(initializedColor));
                builder.Append(">");
                builder.Append(paddedInitializedProviders);
                builder.Append("</color>");
                builder.Append(" ");
                builder.Append("<color=#");
                builder.Append(ColorUtility.ToHtmlStringRGBA(uninitializedColor));
                builder.Append(">");
                builder.Append(paddedUninitializedProviders);
                builder.Append("</color>");
                builder.Append("</mspace>");
            }

            return builder.ToString();
        }

        private static int CountProvidersWithStatus(List<ProviderSnapshot> providers, DisplayStatus status)
        {
            int count = 0;
            for (int i = 0; i < providers.Count; i++)
            {
                if (providers[i].Status == status)
                {
                    count++;
                }
            }

            return count;
        }

        private static string GetModuleShortName(string moduleName)
        {
            switch (moduleName)
            {
                case "Ads Manager":
                    return "AdM";
                case "Tracking Manager":
                    return "TrM";
                case "IAP Manager":
                    return "IAP";
                case "Remote Config Manager":
                    return "RcM";
                case "In-App Review Manager":
                    return "IAR";
                case "User Segmentation":
                    return "USM";
                case "Currency Converter":
                    return "CcM";
                default:
                    return moduleName;
            }
        }

        private void ApplyPanelSizeSettings()
        {
            if (_expandedPanelRect == null || _expandedContentRect == null || _minimizedPanelRect == null || _minimizedSummaryText == null || _expandedToggleButton == null || _expandedHideButton == null || _minimizedSummaryLayout == null)
            {
                return;
            }

            if (autoCalculateExpandedPanelSize)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_expandedContentRect);

                float headerHeight = toggleButtonSize.y + 8f;
                float preferredContentWidth = GetExpandedContentPreferredWidth();
                float preferredContentHeight = GetExpandedContentPreferredHeight();
                float buttonsWidth = (toggleButtonSize.x * 2f) + minimizedItemSpacing;
                float minimumHeaderWidth = panelPadding.x + buttonsWidth + panelPadding.z;
                float width = Mathf.Max(panelPadding.x + preferredContentWidth + panelPadding.z, minimumHeaderWidth);
                float height = panelPadding.y + headerHeight + preferredContentHeight + panelPadding.w;
                _expandedPanelRect.sizeDelta = new Vector2(Mathf.Max(120f, width), Mathf.Max(80f, height));
            }
            else
            {
                _expandedPanelRect.sizeDelta = panelSize;
            }

            if (!autoCalculateMinimizedPanelSize)
            {
                _minimizedPanelRect.sizeDelta = minimizedPanelSize;
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_minimizedPanelRect);

            Vector2 preferredSummarySize = GetMinimizedSummaryPreferredSize();
            _minimizedSummaryLayout.preferredWidth = preferredSummarySize.x;
            _minimizedSummaryLayout.preferredHeight = preferredSummarySize.y;

            float preferredSummaryWidth = preferredSummarySize.x;
            float preferredSummaryHeight = preferredSummarySize.y;
            float widthWithPadding = minimizedPanelPadding.x + preferredSummaryWidth + minimizedPanelPadding.z;
            float heightWithPadding = minimizedPanelPadding.y + preferredSummaryHeight + minimizedPanelPadding.w;
            _minimizedPanelRect.sizeDelta = new Vector2(Mathf.Max(120f, widthWithPadding), Mathf.Max(48f, heightWithPadding));
        }

        private Vector2 GetMinimizedSummaryPreferredSize()
        {
            _minimizedSummaryText.ForceMeshUpdate();
            return _minimizedSummaryText.GetPreferredValues(_minimizedSummaryText.text, Mathf.Infinity, Mathf.Infinity);
        }

        private float GetExpandedContentPreferredWidth()
        {
            float maxWidth = 0f;
            for (int i = 0; i < _moduleRows.Count; i++)
            {
                maxWidth = Mathf.Max(maxWidth, _moduleRows[i].GetPreferredWidth(this));
            }

            return maxWidth;
        }

        private float GetExpandedContentPreferredHeight()
        {
            if (_moduleRows.Count == 0)
            {
                return 0f;
            }

            float totalHeight = 0f;
            for (int i = 0; i < _moduleRows.Count; i++)
            {
                if (i > 0)
                {
                    totalHeight += moduleSpacing;
                }

                totalHeight += _moduleRows[i].GetPreferredHeight(this);
            }

            return totalHeight;
        }

        private static object GetMemberValue(object source, string memberName)
        {
            if (source == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            Type currentType = source.GetType();
            while (currentType != null)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                PropertyInfo propertyInfo = currentType.GetProperty(memberName, flags);
                if (propertyInfo != null)
                {
                    return propertyInfo.GetValue(source, null);
                }

                FieldInfo fieldInfo = currentType.GetField(memberName, flags);
                if (fieldInfo != null)
                {
                    return fieldInfo.GetValue(source);
                }

                currentType = currentType.BaseType;
            }

            return null;
        }

        private static string GetDisplayName(object instance)
        {
            if (instance is UnityEngine.Object unityObject && !string.IsNullOrWhiteSpace(unityObject.name))
            {
                return unityObject.name;
            }

            return NicifyName(instance.GetType().Name);
        }

        private static string NicifyName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "Unknown";
            }

            StringBuilder builder = new StringBuilder(rawName.Length * 2);
            for (int i = 0; i < rawName.Length; i++)
            {
                char current = rawName[i];
                if (i > 0 && char.IsUpper(current) && !char.IsUpper(rawName[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(current);
            }

            return builder
                .ToString()
                .Replace(" Iap", " IAP")
                .Replace(" Id", " ID");
        }

        private static DisplayStatus NormalizeStatus(string rawStatus)
        {
            switch (rawStatus)
            {
                case "Ready":
                case "Initialized":
                    return DisplayStatus.Initialized;
                case "Initializing":
                    return DisplayStatus.Initializing;
                case "Refreshing":
                    return DisplayStatus.Refreshing;
                case "Disabled":
                    return DisplayStatus.Disabled;
                case "Failed":
                    return DisplayStatus.Failed;
                case "Unbound":
                    return DisplayStatus.Unbound;
                case "Uninitialized":
                case "NotInitialized":
                    return DisplayStatus.Uninitialized;
                default:
                    return DisplayStatus.Unknown;
            }
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, float fontSize, FontStyles fontStyles, Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            ApplyTextStyle(text, fontSize, fontStyles, color);

            LayoutElement layoutElement = textObject.GetComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.minHeight = fontSize + 6f;

            return text;
        }

        private void ApplyTextStyle(TextMeshProUGUI text, float fontSize, FontStyles fontStyles, Color color)
        {
            if (text == null)
            {
                return;
            }

            if (fontAsset != null)
            {
                text.font = fontAsset;
            }

            text.fontSize = fontSize;
            text.fontStyle = fontStyles;
            text.color = color;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.richText = true;
        }

        private void CreateButton(string name, Transform parent, out Button button, out TextMeshProUGUI text)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            button = buttonObject.GetComponent<Button>();
            LayoutElement layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = toggleButtonSize.x;
            layoutElement.preferredHeight = toggleButtonSize.y;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            text = labelObject.GetComponent<TextMeshProUGUI>();
            ApplyTextStyle(text, toggleButtonFontSize, FontStyles.Bold, toggleButtonTextColor);
            text.alignment = TextAlignmentOptions.Center;
        }

        private void ConfigureExpandedButton(Button button, TextMeshProUGUI text, string label, UnityEngine.Events.UnityAction onClick)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);
            ConfigureButtonAppearance(button, text, label);
        }

        private void ConfigureMinimizedPanelButton()
        {
            if (_minimizedPanelButton == null)
            {
                return;
            }

            Image image = _minimizedPanelButton.GetComponent<Image>();
            image.color = backgroundColor;

            _minimizedPanelButton.onClick.RemoveAllListeners();
            _minimizedPanelButton.onClick.AddListener(ToggleMinimized);

            ColorBlock colors = _minimizedPanelButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            _minimizedPanelButton.colors = colors;
        }

        private void ConfigureButtonAppearance(Button button, TextMeshProUGUI text, string label)
        {
            if (button == null || text == null)
            {
                return;
            }

            RectTransform buttonRect = button.GetComponent<RectTransform>();
            buttonRect.sizeDelta = toggleButtonSize;

            Image image = button.GetComponent<Image>();
            image.color = toggleButtonBackgroundColor;

            LayoutElement layoutElement = button.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = toggleButtonSize.x;
            layoutElement.preferredHeight = toggleButtonSize.y;

            ApplyTextStyle(text, toggleButtonFontSize, FontStyles.Bold, toggleButtonTextColor);
            text.alignment = TextAlignmentOptions.Center;
            text.text = label;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;
        }

        private HorizontalLayoutGroup AddHorizontalLayout(GameObject target)
        {
            HorizontalLayoutGroup layout = target.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
            {
                layout = target.AddComponent<HorizontalLayoutGroup>();
            }

            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = 8f;

            return layout;
        }

        private VerticalLayoutGroup AddVerticalLayout(GameObject target, float spacing)
        {
            VerticalLayoutGroup layout = target.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = target.AddComponent<VerticalLayoutGroup>();
            }

            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = spacing;

            return layout;
        }

        private Color GetStatusColor(DisplayStatus status)
        {
            switch (status)
            {
                case DisplayStatus.Initialized:
                    return initializedColor;
                case DisplayStatus.Initializing:
                    return initializingColor;
                case DisplayStatus.Unbound:
                    return unboundColor;
                case DisplayStatus.Refreshing:
                    return refreshingColor;
                case DisplayStatus.Disabled:
                    return disabledColor;
                case DisplayStatus.Failed:
                    return failedColor;
                case DisplayStatus.Unknown:
                    return unknownColor;
                case DisplayStatus.Uninitialized:
                default:
                    return uninitializedColor;
            }
        }

        private static string GetStatusLabel(DisplayStatus status)
        {
            switch (status)
            {
                case DisplayStatus.Initialized:
                    return "[INITIALIZED]";
                case DisplayStatus.Initializing:
                    return "[INITIALIZING]";
                case DisplayStatus.Unbound:
                    return "[UNBOUND]";
                case DisplayStatus.Refreshing:
                    return "[REFRESHING]";
                case DisplayStatus.Disabled:
                    return "[DISABLED]";
                case DisplayStatus.Failed:
                    return "[FAILED]";
                case DisplayStatus.Unknown:
                    return "[UNKNOWN]";
                case DisplayStatus.Uninitialized:
                default:
                    return "[UNINITIALIZED]";
            }
        }

        private static float GetHorizontalAnchor(HorizontalPanelAlignment alignment)
        {
            switch (alignment)
            {
                case HorizontalPanelAlignment.Center:
                    return 0.5f;
                case HorizontalPanelAlignment.Right:
                    return 1f;
                case HorizontalPanelAlignment.Left:
                default:
                    return 0f;
            }
        }

        private static float GetVerticalAnchor(VerticalPanelAlignment alignment)
        {
            switch (alignment)
            {
                case VerticalPanelAlignment.Middle:
                    return 0.5f;
                case VerticalPanelAlignment.Bottom:
                    return 0f;
                case VerticalPanelAlignment.Top:
                default:
                    return 1f;
            }
        }

        private sealed class ModuleRowView
        {
            private readonly TextMeshProUGUI _statusText;
            private readonly TextMeshProUGUI _moduleText;
            private readonly RectTransform _providersRoot;
            private readonly List<ProviderRowView> _providerRows = new List<ProviderRowView>();

            public ModuleRowView(Transform parent, IntegrationStatusView owner)
            {
                GameObject rootObject = new GameObject("Module", typeof(RectTransform), typeof(LayoutElement));
                rootObject.transform.SetParent(parent, false);

                RectTransform rootRect = rootObject.GetComponent<RectTransform>();
                rootRect.localScale = Vector3.one;

                VerticalLayoutGroup rootLayout = owner.AddVerticalLayout(rootObject, owner.providerSpacing);
                rootLayout.padding = new RectOffset(0, 0, 0, 0);

                GameObject headerObject = new GameObject("Header", typeof(RectTransform));
                headerObject.transform.SetParent(rootObject.transform, false);
                owner.AddHorizontalLayout(headerObject);

                _statusText = owner.CreateText("Status", headerObject.transform, owner.moduleFontSize, owner.moduleFontStyle, owner.initializedColor);
                LayoutElement statusLayout = _statusText.GetComponent<LayoutElement>();
                statusLayout.preferredWidth = owner.statusColumnWidth;
                statusLayout.flexibleWidth = 0f;

                _moduleText = owner.CreateText("ModuleName", headerObject.transform, owner.moduleFontSize, owner.moduleFontStyle, owner.labelColor);

                GameObject providersObject = new GameObject("Providers", typeof(RectTransform), typeof(LayoutElement));
                providersObject.transform.SetParent(rootObject.transform, false);
                _providersRoot = providersObject.GetComponent<RectTransform>();
                owner.AddVerticalLayout(providersObject, owner.providerSpacing);

                LayoutElement providersLayout = providersObject.GetComponent<LayoutElement>();
                providersLayout.flexibleWidth = 1f;
            }

            public void Apply(ModuleSnapshot snapshot, IntegrationStatusView owner)
            {
                _statusText.text = GetStatusLabel(snapshot.Status);
                _statusText.color = owner.GetStatusColor(snapshot.Status);
                owner.ApplyTextStyle(_statusText, owner.moduleFontSize, owner.moduleFontStyle, owner.GetStatusColor(snapshot.Status));

                _moduleText.text = "---- " + snapshot.Name + " (" + snapshot.Providers.Count + " Provider)";
                owner.ApplyTextStyle(_moduleText, owner.moduleFontSize, owner.moduleFontStyle, owner.labelColor);

                if (_providerRows.Count != snapshot.Providers.Count)
                {
                    ResizeProviders(snapshot.Providers.Count, owner);
                }

                for (int i = 0; i < snapshot.Providers.Count; i++)
                {
                    _providerRows[i].Apply(snapshot.Providers[i], owner);
                }
            }

            private void ResizeProviders(int targetCount, IntegrationStatusView owner)
            {
                for (int i = _providerRows.Count - 1; i >= targetCount; i--)
                {
                    _providerRows[i].Dispose();
                    _providerRows.RemoveAt(i);
                }

                while (_providerRows.Count < targetCount)
                {
                    _providerRows.Add(new ProviderRowView(_providersRoot, owner));
                }
            }

            public float GetPreferredWidth(IntegrationStatusView owner)
            {
                float headerWidth = owner.statusColumnWidth + 8f + _moduleText.GetPreferredValues(_moduleText.text).x;
                float maxWidth = headerWidth;

                for (int i = 0; i < _providerRows.Count; i++)
                {
                    maxWidth = Mathf.Max(maxWidth, _providerRows[i].GetPreferredWidth(owner));
                }

                return maxWidth;
            }

            public float GetPreferredHeight(IntegrationStatusView owner)
            {
                float headerHeight = Mathf.Max(owner.moduleFontSize + 6f, _statusText.GetPreferredValues(_statusText.text).y, _moduleText.GetPreferredValues(_moduleText.text).y);
                if (_providerRows.Count == 0)
                {
                    return headerHeight;
                }

                float providersHeight = owner.providerSpacing;
                for (int i = 0; i < _providerRows.Count; i++)
                {
                    if (i > 0)
                    {
                        providersHeight += owner.providerSpacing;
                    }

                    providersHeight += _providerRows[i].GetPreferredHeight(owner);
                }

                return headerHeight + providersHeight;
            }
        }

        private sealed class ProviderRowView
        {
            private readonly GameObject _rootObject;
            private readonly TextMeshProUGUI _statusText;
            private readonly TextMeshProUGUI _nameText;

            public ProviderRowView(Transform parent, IntegrationStatusView owner)
            {
                _rootObject = new GameObject("Provider", typeof(RectTransform));
                _rootObject.transform.SetParent(parent, false);

                owner.AddHorizontalLayout(_rootObject);

                GameObject indentObject = new GameObject("Indent", typeof(RectTransform), typeof(LayoutElement));
                indentObject.transform.SetParent(_rootObject.transform, false);
                LayoutElement indentLayout = indentObject.GetComponent<LayoutElement>();
                indentLayout.preferredWidth = owner.providerIndent;
                indentLayout.flexibleWidth = 0f;

                _statusText = owner.CreateText("Status", _rootObject.transform, owner.providerFontSize, owner.providerFontStyle, owner.initializedColor);
                LayoutElement statusLayout = _statusText.GetComponent<LayoutElement>();
                statusLayout.preferredWidth = owner.statusColumnWidth;
                statusLayout.flexibleWidth = 0f;

                _nameText = owner.CreateText("Name", _rootObject.transform, owner.providerFontSize, owner.providerFontStyle, owner.labelColor);
            }

            public void Apply(ProviderSnapshot snapshot, IntegrationStatusView owner)
            {
                _statusText.text = "+ " + GetStatusLabel(snapshot.Status);
                owner.ApplyTextStyle(_statusText, owner.providerFontSize, owner.providerFontStyle, owner.GetStatusColor(snapshot.Status));

                _nameText.text = snapshot.Name;
                owner.ApplyTextStyle(_nameText, owner.providerFontSize, owner.providerFontStyle, owner.labelColor);
            }

            public void Dispose()
            {
                UnityEngine.Object.Destroy(_rootObject);
            }

            public float GetPreferredWidth(IntegrationStatusView owner)
            {
                return owner.providerIndent
                    + 8f
                    + owner.statusColumnWidth
                    + 8f
                    + _nameText.GetPreferredValues(_nameText.text).x;
            }

            public float GetPreferredHeight(IntegrationStatusView owner)
            {
                return Mathf.Max(owner.providerFontSize + 6f, _statusText.GetPreferredValues(_statusText.text).y, _nameText.GetPreferredValues(_nameText.text).y);
            }
        }

        private readonly struct ModuleSnapshot
        {
            public ModuleSnapshot(string name, DisplayStatus status, List<ProviderSnapshot> providers)
            {
                Name = name;
                Status = status;
                Providers = providers;
            }

            public string Name { get; }

            public DisplayStatus Status { get; }

            public List<ProviderSnapshot> Providers { get; }
        }

        private readonly struct ProviderSnapshot
        {
            public ProviderSnapshot(string name, DisplayStatus status)
            {
                Name = name;
                Status = status;
            }

            public string Name { get; }

            public DisplayStatus Status { get; }
        }

        private enum DisplayStatus
        {
            Unbound,
            Uninitialized,
            Initializing,
            Initialized,
            Refreshing,
            Disabled,
            Failed,
            Unknown
        }

        public enum HorizontalPanelAlignment
        {
            Left,
            Center,
            Right
        }

        public enum VerticalPanelAlignment
        {
            Top,
            Middle,
            Bottom
        }
    }
}
