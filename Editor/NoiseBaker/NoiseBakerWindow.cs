using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.NoiseBaker;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker.Editor
{
    /// <summary>
    /// Editor window for baking noise / voronoi textures: live preview,
    /// full parameter control, presets and PNG asset export.
    /// Open via Tools > Scheherazade > Noise Baker.
    /// </summary>
    public class NoiseBakerWindow : EditorWindow
    {
        private const string PrefsAutoRefresh = "Scheherazade.NoiseBaker.AutoRefresh";
        private const string PrefsTab = "Scheherazade.NoiseBaker.Tab";
        private const string PrefsZoom = "Scheherazade.NoiseBaker.Zoom";
        private const float RefreshDebounce = 0.25f;
        private const string PreviewTitle = "Preview";

        [SerializeField] private NoiseBakerSettings _settings = new NoiseBakerSettings();
        [SerializeField] private NoiseBakerPreset _preset;

        private Texture2D _preview;
        private Vector2 _scrollPos;
        private bool _dirty;
        private double _lastChangeTime;
        private bool _autoRefresh = true;
        private int _tab;
        private int _activeChannel;
        private float _zoom = 1f;
        private long _bakeMs;

        private CancellationTokenSource _bakeCts;
        private bool _isBaking;

        private static GUIStyle _channelActive;
        private static GUIStyle _channelActiveDisabled;
        private static GUIStyle _channelDisabled;

        [MenuItem("Dev Menu/Tools/Noise Generator")]
        private static void Open()
        {
            NoiseBakerWindow window = GetWindow<NoiseBakerWindow>(false, "Noise Generator");
            window.minSize = new Vector2(760f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            _autoRefresh = EditorPrefs.GetBool(PrefsAutoRefresh, true);
            _tab = Mathf.Clamp(EditorPrefs.GetInt(PrefsTab, 0), 0, 1);
            _zoom = EditorPrefs.GetFloat(PrefsZoom, 1f);

            if (_settings == null)
            {
                _settings = new NoiseBakerSettings();
            }

            EnsureChannels();
            RequestBake();
        }

        private void Update()
        {
            // Runs every frame while the window is open, regardless of input,
            // so debounced bakes fire even after the mouse stops moving.
            if (_dirty && _autoRefresh && !_isBaking &&
                EditorApplication.timeSinceStartup - _lastChangeTime > RefreshDebounce)
            {
                RequestBake();
            }

            // Keep the UI live while work is pending.
            if (_dirty || _isBaking)
            {
                Repaint();
            }
        }

        private void OnDisable()
        {
            EditorPrefs.SetBool(PrefsAutoRefresh, _autoRefresh);
            EditorPrefs.SetInt(PrefsTab, _tab);
            EditorPrefs.SetFloat(PrefsZoom, _zoom);

            _bakeCts?.Cancel();
            ReleasePreview();
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawBody();
            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            DrawPresetPopup();

            GUILayout.Space(8f);
            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(48f)))
            {
                _settings = new NoiseBakerSettings();
                _preset = null;
                MarkChanged();
            }

            if (GUILayout.Button("Save Template", EditorStyles.toolbarButton, GUILayout.Width(104f)))
            {
                SaveToPreset(false);
            }

            if (GUILayout.Button("Save As...", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                SaveToPreset(true);
            }

            GUILayout.FlexibleSpace();

            bool newAutoRefresh = GUILayout.Toggle(_autoRefresh, "Auto-refresh", EditorStyles.toolbarButton, GUILayout.Width(96f));
            if (newAutoRefresh != _autoRefresh)
            {
                _autoRefresh = newAutoRefresh;
                if (_autoRefresh)
                {
                    MarkChanged();
                }
            }

            GUI.enabled = _isBaking;
            if (GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(56f)))
            {
                _bakeCts?.Cancel();
            }

            GUI.enabled = true;
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                RequestBake();
            }

            if (GUILayout.Button("Export PNG...", EditorStyles.toolbarButton, GUILayout.Width(100f)))
            {
                ExportPng();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPresetPopup()
        {
            NoiseBakerPreset[] presets = FindPresets();
            string[] names = new string[presets.Length];
            int selected = -1;
            for (int i = 0; i < presets.Length; i++)
            {
                names[i] = string.IsNullOrEmpty(presets[i].DisplayName) ? presets[i].name : presets[i].DisplayName;
                if (presets[i] == _preset)
                {
                    selected = i;
                }
            }

            if (presets.Length == 0)
            {
                GUILayout.Label("No templates", EditorStyles.toolbarButton);
                return;
            }

            int newIndex = EditorGUILayout.Popup(
                selected,
                names,
                EditorStyles.toolbarPopup,
                GUILayout.Width(180f)
            );
            if (newIndex >= 0 && newIndex != selected)
            {
                LoadPreset(presets[newIndex]);
            }
        }

        private void DrawBody()
        {
            EditorGUILayout.BeginHorizontal();

            DrawPreviewPane();

            EditorGUILayout.BeginVertical(GUILayout.Width(300f));
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            int newTab = GUILayout.Toolbar(_tab, new[] { "Generator", "Output" });
            if (newTab != _tab)
            {
                _tab = newTab;
            }

            EditorGUILayout.Space(6f);
            // ExpandHeight keeps the panel filling the window; the stored
            // scroll position makes the content scrollable (channel pack etc.).
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
            switch (_tab)
            {
                case 0: DrawGeneratorTab(); break;
                default: DrawOutputTab(); break;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreviewPane()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

            float availableWidth = Mathf.Max(200f, position.width - 340f);
            float availableHeight = Mathf.Max(200f, position.height - 90f);

            Rect rect = GUILayoutUtility.GetRect(availableWidth, availableHeight, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.1f, 1f));

            // Clip group: keeps the zoomed texture inside the preview view.
            GUI.BeginClip(rect);
            if (_preview != null)
            {
                float scaledWidth = rect.width * _zoom;
                float scaledHeight = rect.height * _zoom;
                float offsetX = (rect.width - scaledWidth) * 0.5f;
                float offsetY = (rect.height - scaledHeight) * 0.5f;
                GUI.DrawTexture(
                    new Rect(offsetX, offsetY, scaledWidth, scaledHeight),
                    _preview,
                    ScaleMode.ScaleToFit
                );
            }
            else if (_isBaking)
            {
                EditorGUI.LabelField(
                    new Rect(0f, rect.height * 0.5f - 10f, rect.width, 20f),
                    "Baking...",
                    EditorStyles.centeredGreyMiniLabel
                );
            }
            else
            {
                EditorGUI.LabelField(
                    new Rect(0f, rect.height * 0.5f - 10f, rect.width, 20f),
                    "No preview",
                    EditorStyles.centeredGreyMiniLabel
                );
            }

            GUI.EndClip();

            EditorGUILayout.Space(2f);
            _zoom = EditorGUILayout.Slider("Zoom", _zoom, 0.25f, 8f);
            EditorGUILayout.LabelField(
                string.Format("{0}x{1}  {2} ms", _settings.width, _settings.height, _bakeMs),
                EditorStyles.miniLabel
            );

            EditorGUILayout.EndVertical();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (_isBaking)
            {
                GUILayout.Label("Baking...", EditorStyles.miniLabel);
            }
            else if (_dirty && !_autoRefresh)
            {
                GUILayout.Label("Settings changed - press Refresh to update the preview.", EditorStyles.miniLabel);
            }
            else
            {
                string channelLabel = _settings.colorMode == ColorMode.ChannelPack
                    ? "R/G/B/A"
                    : "R";
                GUILayout.Label(
                    string.Format("Channel: {0}  |  Color: {1}  |  Domain: {2}", channelLabel, _settings.colorMode, _settings.domain),
                    EditorStyles.miniLabel
                );
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(PreviewTitle, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGeneratorTab()
        {
            // ---- Color output ----
            ColorMode newMode = (ColorMode)EditorGUILayout.EnumPopup("Color mode", _settings.colorMode);
            if (newMode != _settings.colorMode)
            {
                _settings.colorMode = newMode;
                MarkChanged();
            }

            if (_settings.colorMode == ColorMode.Gradient)
            {
                Gradient newGradient = EditorGUILayout.GradientField("Gradient", _settings.gradient);
                if (newGradient != _settings.gradient)
                {
                    _settings.gradient = newGradient;
                    MarkChanged();
                }
            }

            EditorGUILayout.Space(6f);

            // ---- Channel selection ----
            DrawChannelButtons();

            EditorGUILayout.Space(6f);

            EnsureChannels();
            ChannelSettings channel = _settings.channels[_activeChannel];
            DrawChannelSection(channel, _activeChannel);
        }

        private void DrawChannelButtons()
        {
            string[] labels = { "R", "G", "B", "A" };
            bool channelPack = _settings.colorMode == ColorMode.ChannelPack;

            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < 4; i++)
            {
                bool selectable = channelPack || i == 0;
                bool enabled = _settings.channels[i] != null && _settings.channels[i].enabled;
                GUIStyle style = ChannelButtonStyle(i == _activeChannel, enabled);
                GUIContent content = new GUIContent(
                    labels[i],
                    selectable ? "Edit channel " + labels[i] : "Only used in ChannelPack mode"
                );

                GUI.enabled = selectable;
                if (GUILayout.Button(content, style, GUILayout.Width(46f), GUILayout.Height(22f)))
                {
                    _activeChannel = i;
                }

                GUI.enabled = true;
            }

            GUI.enabled = channelPack;
            if (GUILayout.Button("All", EditorStyles.toolbarButton, GUILayout.Width(40f)))
            {
                SetAllChannelsEnabled(true);
            }

            if (GUILayout.Button("None", EditorStyles.toolbarButton, GUILayout.Width(48f)))
            {
                SetAllChannelsEnabled(false);
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void SetAllChannelsEnabled(bool enabled)
        {
            for (int i = 0; i < _settings.channels.Length; i++)
            {
                if (_settings.channels[i] != null)
                {
                    _settings.channels[i].enabled = enabled;
                }
            }

            MarkChanged();
        }

        private void DrawChannelSection(ChannelSettings channel, int index)
        {
            string[] names = { "R", "G", "B", "A" };
            bool channelPack = _settings.colorMode == ColorMode.ChannelPack;

            EditorGUILayout.LabelField("Channel " + names[index], EditorStyles.boldLabel);

            GUI.enabled = channelPack;
            DrawBoolField("Enabled", ref channel.enabled);
            GUI.enabled = true;

            DrawEnumField("Module", ref channel.moduleType);
            DrawFloatField("Frequency", ref channel.frequency, 0.01f, 64f);
            DrawSeedField("Seed", ref channel.seed);

            if (channel.moduleType == NoiseModuleType.Fractal)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Fractal", EditorStyles.boldLabel);
                channel.fractal = channel.fractal ?? new FractalSettings();
                FractalSettings fractal = channel.fractal;
                DrawEnumField("Base noise", ref fractal.baseType);
                DrawIntField("Octaves", ref fractal.octaves, 1, 12);
                DrawFloatField("Lacunarity", ref fractal.lacunarity, 1.01f, 4f);
                DrawFloatField("Gain", ref fractal.gain, 0.1f, 1f);
                DrawEnumField("Variant", ref fractal.type);
                DrawFloatField("Ridge offset", ref fractal.ridgeOffset, 0f, 2f);
                DrawBoolField("Normalize", ref fractal.normalize);
            }

            if (channel.moduleType == NoiseModuleType.Voronoi)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Voronoi", EditorStyles.boldLabel);
                channel.voronoi = channel.voronoi ?? new VoronoiSettings();
                VoronoiSettings voronoi = channel.voronoi;
                DrawIntField("Cells", ref voronoi.cellCount, 1, 512);
                DrawFloatField("Jitter", ref voronoi.jitter, 0f, 0.95f);
                DrawEnumField("Metric", ref voronoi.metric);
                if (voronoi.metric == VoronoiDistanceMetric.Exponent)
                {
                    DrawFloatField("Exponent p", ref voronoi.exponent, 1f, 8f);
                }

                DrawEnumField("Feature", ref voronoi.feature);
                if (voronoi.feature == VoronoiFeature.Border)
                {
                    DrawFloatField("Border width", ref voronoi.borderWidth, 0f, 0.5f);
                    DrawFloatField("Border softness", ref voronoi.borderSoftness, 0f, 1f);
                }

                DrawBoolField("Normalize distances", ref voronoi.normalizeDistances);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Post-processing", EditorStyles.boldLabel);
            DrawBoolField("Normalize (min/max)", ref channel.normalize);
            DrawFloatField("Contrast", ref channel.contrast, 0.1f, 5f);
            DrawFloatField("Gamma", ref channel.gamma, 0.2f, 5f);
            DrawIntField("Quantize steps (0 = off)", ref channel.quantizeSteps, 0, 256);
            DrawBoolField("Invert", ref channel.invert);
            DrawFloatField("Threshold (0 = off)", ref channel.threshold, 0f, 1f);
            DrawFloatField("Threshold softness", ref channel.thresholdSoftness, 0f, 1f);
        }

        private void EnsureChannels()
        {
            if (_settings.channels == null || _settings.channels.Length != 4)
            {
                _settings.channels = new[]
                {
                    new ChannelSettings(),
                    new ChannelSettings { seed = 1337 + 7919 },
                    new ChannelSettings { seed = 1337 + 15838 },
                    new ChannelSettings { seed = 1337 + 23757 },
                };
            }

            for (int i = 0; i < _settings.channels.Length; i++)
            {
                if (_settings.channels[i] == null)
                {
                    _settings.channels[i] = new ChannelSettings { seed = 1337 + i * 7919 };
                }
            }
        }

        private static GUIStyle ChannelButtonStyle(bool isActive, bool isEnabled)
        {
            if (isActive)
            {
                if (_channelActive == null)
                {
                    _channelActive = new GUIStyle(EditorStyles.toolbarButton);
                    _channelActive.normal = _channelActive.onNormal;
                    _channelActive.onNormal = _channelActive.onNormal;
                }

                if (isEnabled)
                {
                    return _channelActive;
                }

                if (_channelActiveDisabled == null)
                {
                    _channelActiveDisabled = new GUIStyle(_channelActive);
                    _channelActiveDisabled.normal.textColor = new Color(0.55f, 0.55f, 0.55f, 1f);
                }

                return _channelActiveDisabled;
            }

            if (isEnabled)
            {
                return EditorStyles.toolbarButton;
            }

            if (_channelDisabled == null)
            {
                _channelDisabled = new GUIStyle(EditorStyles.toolbarButton);
                _channelDisabled.normal.textColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            }

            return _channelDisabled;
        }

        private void DrawOutputTab()
        {
            DomainMode newDomain = (DomainMode)EditorGUILayout.EnumPopup("Tileable", _settings.domain);
            if (newDomain != _settings.domain)
            {
                _settings.domain = newDomain;
                MarkChanged();
            }

            EditorGUILayout.Space(4f);

            DrawIntField("Width", ref _settings.width, 1, 8192);
            DrawIntField("Height", ref _settings.height, 1, 8192);

            string[] formats = { "R8", "R16", "RGBA32", "RGBAFloat" };
            TextureFormat[] formatValues =
            {
                TextureFormat.R8,
                TextureFormat.R16,
                TextureFormat.RGBA32,
                TextureFormat.RGBAFloat,
            };
            int formatIndex = Array.IndexOf(formatValues, _settings.format);
            if (formatIndex < 0)
            {
                formatIndex = 2;
            }

            int newFormatIndex = EditorGUILayout.Popup("Format", formatIndex, formats);
            if (formatValues[newFormatIndex] != _settings.format)
            {
                _settings.format = formatValues[newFormatIndex];
                MarkChanged();
            }

            DrawBoolField("Mipmaps", ref _settings.mipmaps);
            DrawEnumField("Filter", ref _settings.filterMode);
            DrawEnumField("Wrap mode", ref _settings.wrapMode);

            EditorGUILayout.Space(10f);
            EditorGUILayout.HelpBox(
                "Runtime generation: save this setup as a template, then call\n" +
                "NoiseBakerPreset template = Resources.Load<NoiseBakerPreset>(\"Templates/Name\");\n" +
                "Texture2D tex = template.Bake();",
                MessageType.Info
            );
        }

        private void DrawSeedField(string label, ref int seed)
        {
            EditorGUILayout.BeginHorizontal();
            int newSeed = EditorGUILayout.IntField(label, seed);
            if (newSeed != seed)
            {
                seed = newSeed;
                MarkChanged();
            }

            if (GUILayout.Button("Rnd", GUILayout.Width(40f)))
            {
                seed = UnityEngine.Random.Range(int.MinValue / 2, int.MaxValue / 2);
                MarkChanged();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawIntField(string label, ref int value, int min, int max)
        {
            int newValue = EditorGUILayout.IntField(label, value);
            newValue = Mathf.Clamp(newValue, min, max);
            if (newValue != value)
            {
                value = newValue;
                MarkChanged();
            }
        }

        private void DrawFloatField(string label, ref float value, float min, float max)
        {
            float newValue = EditorGUILayout.FloatField(label, value);
            newValue = Mathf.Clamp(newValue, min, max);
            if (Mathf.Abs(newValue - value) > 0.0001f)
            {
                value = newValue;
                MarkChanged();
            }
        }

        private void DrawBoolField(string label, ref bool value)
        {
            bool newValue = EditorGUILayout.Toggle(label, value);
            if (newValue != value)
            {
                value = newValue;
                MarkChanged();
            }
        }

        private void DrawEnumField<T>(string label, ref T value) where T : struct, Enum
        {
            Enum newValue = EditorGUILayout.EnumPopup(label, value);
            if (!Equals(newValue, value))
            {
                value = (T)newValue;
                MarkChanged();
            }
        }

        private void MarkChanged()
        {
            _dirty = true;
            _lastChangeTime = EditorApplication.timeSinceStartup;
        }

        private void RequestBake()
        {
            _dirty = false;
            BakeNow();
        }

        private async void BakeNow()
        {
            _bakeCts?.Cancel();
            CancellationTokenSource cts = new CancellationTokenSource();
            _bakeCts = cts;
            _isBaking = true;

            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                Texture2D texture = await NoiseBaker.BakeAsync(_settings, cts.Token);
                if (!cts.IsCancellationRequested && texture != null)
                {
                    ReleasePreview();
                    _preview = texture;
                    _bakeMs = watch.ElapsedMilliseconds;
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when parameters change mid-bake.
            }
            catch (AggregateException aggregate) when (aggregate.InnerException is OperationCanceledException)
            {
                // Parallel loops may surface cancellation wrapped; still expected.
            }
            catch (Exception exception)
            {
                QuickLog.Error<NoiseBakerWindow>("Bake failed: {0}", exception.Message);
            }
            finally
            {
                _isBaking = false;
                Repaint();
            }
        }

        private void ReleasePreview()
        {
            if (_preview != null)
            {
                DestroyImmediate(_preview);
                _preview = null;
            }
        }

        private void LoadPreset(NoiseBakerPreset preset)
        {
            _preset = preset;
            _settings = preset.Settings != null ? preset.Settings.Clone() : new NoiseBakerSettings();
            MarkChanged();
        }

        private void SaveToPreset(bool saveAs)
        {
            if (_preset == null || saveAs)
            {
                // Default to Resources/Templates so the template can be loaded
                // at runtime with Resources.Load<NoiseBakerPreset>("Templates/..."),
                // or assigned to a serialized field in the inspector.
                string directory = "Assets/Resources/Templates";
                if (_preset != null)
                {
                    directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(_preset));
                }

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string assetPath = EditorUtility.SaveFilePanelInProject(
                    "Save Noise Generator Template",
                    "NoiseGeneratorTemplate",
                    "asset",
                    "Save the current settings as a template asset",
                    directory
                );
                if (string.IsNullOrEmpty(assetPath))
                {
                    return;
                }

                NoiseBakerPreset preset = CreateInstance<NoiseBakerPreset>();
                preset.DisplayName = Path.GetFileNameWithoutExtension(assetPath);
                preset.Settings = _settings.Clone();
                AssetDatabase.CreateAsset(preset, assetPath);
                AssetDatabase.SaveAssets();
                _preset = preset;
                QuickLog.Info<NoiseBakerWindow>("Template saved to {0}.", assetPath);
            }
            else
            {
                Undo.RecordObject(_preset, "Update Noise Generator Template");
                _preset.Settings = _settings.Clone();
                EditorUtility.SetDirty(_preset);
                AssetDatabase.SaveAssets();
                QuickLog.Info<NoiseBakerWindow>("Template updated: {0}.", _preset.name);
            }
        }

        private void ExportPng()
        {
            string moduleName = _settings.channels != null && _settings.channels.Length > 0 && _settings.channels[0] != null
                ? _settings.channels[0].moduleType.ToString()
                : "Noise";
            string initialName = string.Format(
                "Noise_{0}_{1}",
                moduleName,
                string.Format("{0}x{1}", _settings.width, _settings.height)
            );
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Export Baked Texture",
                initialName,
                "png",
                "Save the baked texture as a PNG asset",
                "Assets"
            );
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                assetPath += ".png";
            }

            TextureAssetWriter.Export(_settings, assetPath);
        }

        private static NoiseBakerPreset[] FindPresets()
        {
            string[] guids = AssetDatabase.FindAssets("t:NoiseBakerPreset");
            NoiseBakerPreset[] presets = new NoiseBakerPreset[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                presets[i] = AssetDatabase.LoadAssetAtPath<NoiseBakerPreset>(AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            return presets;
        }
    }
}
