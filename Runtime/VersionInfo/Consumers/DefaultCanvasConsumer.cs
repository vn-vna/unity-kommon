using UnityEngine;
using UnityEngine.UI;

namespace Com.Hapiga.Scheherazade.Common.VIC.Consumers
{
    [CreateAssetMenu(
        fileName = "DefaultCanvasConsumer",
        menuName = "Scheherazade/Version Info/Default Canvas Consumer"
    )]
    public class DefaultCanvasConsumer : ScriptableObject, IVersionInfoConsumer
    {
        #region Serialized Fields — Canvas
        [SerializeField]
        private RenderMode _renderMode = RenderMode.ScreenSpaceOverlay;

        [SerializeField]
        private int _sortingOrder = short.MaxValue;

        [SerializeField]
        private AdditionalCanvasShaderChannels _additionalShaderChannels;
        #endregion

        #region Serialized Fields — Canvas Scaler
        [SerializeField]
        private bool _useCanvasScaler = true;

        [SerializeField]
        private CanvasScaler.ScaleMode _scaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;

        [SerializeField]
        private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        [SerializeField]
        [Range(0f, 1f)]
        private float _matchWidthOrHeight = 0.5f;

        [SerializeField]
        private CanvasScaler.ScreenMatchMode _screenMatchMode =
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

        [SerializeField]
        private float _scaleFactor = 1f;

        [SerializeField]
        private float _dynamicPixelsPerUnit = 1f;

        [SerializeField]
        private float _fallbackScreenDPI = 96f;
        #endregion

        #region Serialized Fields — Text
        [SerializeField]
        private TextAnchor _anchor = TextAnchor.LowerLeft;

        [SerializeField]
        private Vector2 _anchoredPosition = new Vector2(20f, 20f);

        [SerializeField]
        private int _fontSize = 24;

        [SerializeField]
        private Font _font;

        [SerializeField]
        private Color _textColor = Color.white;

        [SerializeField]
        private Color _backgroundColor = new Color(0f, 0f, 0f, 0.5f);

        [SerializeField]
        private Vector2 _backgroundPadding = new Vector2(16f, 8f);
        #endregion

        #region Private Fields
        private Text _versionText;
        #endregion

        #region IVersionInfoConsumer
        public bool IsActive => true;

        public void Consume(string versionInfo)
        {
            EnsureCanvasExists();
            if (_versionText != null)
            {
                _versionText.text = versionInfo;
            }
        }
        #endregion

        #region Private Methods
        private void EnsureCanvasExists()
        {
            if (_versionText != null)
            {
                return;
            }

            GameObject canvasGO = new GameObject(
                "[Version Info Canvas]",
                typeof(Canvas),
                typeof(GraphicRaycaster)
            );
            Object.DontDestroyOnLoad(canvasGO);

            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = _renderMode;
            canvas.sortingOrder = _sortingOrder;
            canvas.additionalShaderChannels = _additionalShaderChannels;

            if (_useCanvasScaler)
            {
                CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = _scaleMode;
                scaler.referenceResolution = _referenceResolution;
                scaler.screenMatchMode = _screenMatchMode;
                scaler.matchWidthOrHeight = _matchWidthOrHeight;
                scaler.scaleFactor = _scaleFactor;
                scaler.dynamicPixelsPerUnit = _dynamicPixelsPerUnit;
                scaler.fallbackScreenDPI = _fallbackScreenDPI;
            }

            CreateBackground(canvasGO.transform);
            CreateText(canvasGO.transform);
        }

        private void CreateBackground(Transform parent)
        {
            GameObject bgGO = new GameObject("Background", typeof(Image));
            bgGO.transform.SetParent(parent, false);

            Image bgImage = bgGO.GetComponent<Image>();
            bgImage.color = _backgroundColor;

            RectTransform bgRT = bgGO.GetComponent<RectTransform>();
            bgRT.anchorMin = AnchorFromTextAnchor();
            bgRT.anchorMax = AnchorFromTextAnchor();
            bgRT.pivot = PivotFromTextAnchor();
            bgRT.anchoredPosition = _anchoredPosition;
            bgRT.sizeDelta = new Vector2(
                300f + _backgroundPadding.x,
                _fontSize + _backgroundPadding.y);
        }

        private void CreateText(Transform parent)
        {
            Transform bgTransform = parent.Find("Background");
            if (bgTransform == null)
            {
                return;
            }

            GameObject textGO = new GameObject("VersionText", typeof(Text));
            textGO.transform.SetParent(bgTransform, false);

            _versionText = textGO.GetComponent<Text>();
            _versionText.alignment = _anchor;
            _versionText.fontSize = _fontSize;
            _versionText.color = _textColor;
            _versionText.font = _font != null
                ? _font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _versionText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _versionText.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;
        }

        private Vector2 AnchorFromTextAnchor()
        {
            return _anchor switch
            {
                TextAnchor.UpperLeft => new Vector2(0f, 1f),
                TextAnchor.UpperCenter => new Vector2(0.5f, 1f),
                TextAnchor.UpperRight => new Vector2(1f, 1f),
                TextAnchor.MiddleLeft => new Vector2(0f, 0.5f),
                TextAnchor.MiddleCenter => new Vector2(0.5f, 0.5f),
                TextAnchor.MiddleRight => new Vector2(1f, 0.5f),
                TextAnchor.LowerLeft => new Vector2(0f, 0f),
                TextAnchor.LowerCenter => new Vector2(0.5f, 0f),
                TextAnchor.LowerRight => new Vector2(1f, 0f),
                _ => new Vector2(0f, 0f),
            };
        }

        private Vector2 PivotFromTextAnchor()
        {
            return _anchor switch
            {
                TextAnchor.UpperLeft => new Vector2(0f, 1f),
                TextAnchor.UpperCenter => new Vector2(0.5f, 1f),
                TextAnchor.UpperRight => new Vector2(1f, 1f),
                TextAnchor.MiddleLeft => new Vector2(0f, 0.5f),
                TextAnchor.MiddleCenter => new Vector2(0.5f, 0.5f),
                TextAnchor.MiddleRight => new Vector2(1f, 0.5f),
                TextAnchor.LowerLeft => new Vector2(0f, 0f),
                TextAnchor.LowerCenter => new Vector2(0.5f, 0f),
                TextAnchor.LowerRight => new Vector2(1f, 0f),
                _ => new Vector2(0.5f, 0.5f),
            };
        }
        #endregion
    }
}
