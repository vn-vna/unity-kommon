using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditorInternal;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Editor.TextureAdjustment
{
    [CustomEditor(typeof(TextureAdjustmentImporter))]
    public class TextureAdjustmentImporterEditor : AssetImporterEditor
    {
        private const string ShaderName = "Hidden/Scheherazade/TextureAdjustment";

        private List<AdjustmentLayerData> _layers = new List<AdjustmentLayerData>();
        private ReorderableList _layerList;
        private int _expandedLayer = -1;
        private int _expandedAdvanced = -1;
        private int _selectedCurveKey = -1;
        private bool _isDraggingCurve;
        private bool _valuesChanged;

        private int[] _histL, _histR, _histG, _histB;
        private int _histMaxL, _histMaxR, _histMaxG, _histMaxB;
        private int _histogramMode;
        private bool _histogramValid;

        private Texture2D _previewTexture;
        private Material _previewMaterial;
        private Texture2D _sourceTexture;
        private string _sourceGUID;
        private string _assetPath;
        private bool _previewDirty = true;

        private static Texture2D _brightnessBarTex, _contrastBarTex, _hueBarTex, _saturationBarTex;
        private static GUIStyle _layerHeaderStyle;

        private static readonly AdjustmentPreset[] Presets =
        {
            new AdjustmentPreset("Reset", () => new List<AdjustmentLayerData>()),
            new AdjustmentPreset("Desaturate", () => new List<AdjustmentLayerData>
            {
                new AdjustmentLayerData(LayerType.HueSaturationVibrance, "Desaturate") { saturation = 0f }
            }),
            new AdjustmentPreset("Vintage Warm", () =>
            {
                var layers = new List<AdjustmentLayerData>();
                layers.Add(new AdjustmentLayerData(LayerType.HueSaturationVibrance, "Warm Color") { hueShift = 15f, saturation = 0.7f, vibrance = 0.2f });
                layers.Add(new AdjustmentLayerData(LayerType.BrightnessContrast, "Warm Contrast") { brightness = 0.1f, contrast = 0.9f });
                return layers;
            }),
            new AdjustmentPreset("High Contrast", () => new List<AdjustmentLayerData>
            {
                new AdjustmentLayerData(LayerType.BrightnessContrast, "High Contrast") { contrast = 1.8f }
            }),
            new AdjustmentPreset("Cool Blue", () =>
            {
                var layers = new List<AdjustmentLayerData>();
                layers.Add(new AdjustmentLayerData(LayerType.HueSaturationVibrance, "Cool Shift") { hueShift = -15f, saturation = 0.9f });
                layers.Add(new AdjustmentLayerData(LayerType.BrightnessContrast, "Cool Tone") { brightness = -0.05f, contrast = 1.1f });
                return layers;
            }),
            new AdjustmentPreset("Faded Film", () =>
            {
                var layers = new List<AdjustmentLayerData>();
                layers.Add(new AdjustmentLayerData(LayerType.BrightnessContrast, "Fade") { brightness = 0.1f, contrast = 0.8f, inputBlack = 0f, inputWhite = 1f, outputBlack = 0.05f, outputWhite = 0.9f });
                layers.Add(new AdjustmentLayerData(LayerType.HueSaturationVibrance, "Film Color") { hueShift = 5f, saturation = 0.6f, vibrance = 0.1f });
                return layers;
            }),
            new AdjustmentPreset("Vibrant", () => new List<AdjustmentLayerData>
            {
                new AdjustmentLayerData(LayerType.HueSaturationVibrance, "Vibrant") { saturation = 1.5f, vibrance = 0.5f }
            }),
            new AdjustmentPreset("Cinematic Teal", () =>
            {
                var layers = new List<AdjustmentLayerData>();
                layers.Add(new AdjustmentLayerData(LayerType.Tint, "Teal Tint") { tintR = 0.7f, tintG = 0.85f, tintB = 1f, tintStrength = 0.25f });
                layers.Add(new AdjustmentLayerData(LayerType.Overlay, "Shadow Blue") { overlayR = 0.1f, overlayG = 0.15f, overlayB = 0.3f, overlayStrength = 0.2f, overlayBlendMode = 2 });
                return layers;
            }),
        };

        private struct AdjustmentPreset
        {
            public string Name;
            public Func<List<AdjustmentLayerData>> Factory;
            public AdjustmentPreset(string name, Func<List<AdjustmentLayerData>> factory)
            { Name = name; Factory = factory; }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            ReadJsonIntoCaches();
            BuildLayerList();
        }

        public override void OnDisable()
        {
            if (_previewTexture != null) { DestroyImmediate(_previewTexture); _previewTexture = null; }
            if (_previewMaterial != null) { DestroyImmediate(_previewMaterial); _previewMaterial = null; }
            _sourceTexture = null;
            base.OnDisable();
        }

        public override void OnInspectorGUI()
        {
            TextureAdjustmentImporter importer = (TextureAdjustmentImporter)target;
            _assetPath = importer.assetPath;

            if (string.IsNullOrEmpty(_assetPath))
            {
                EditorGUILayout.HelpBox("Asset path is not available.", MessageType.Error);
                return;
            }

            if (_sourceTexture == null) RefreshSourceTexture();
            if (!_histogramValid) ComputeHistogram();

            DrawHeader();
            EditorGUILayout.Space(4);
            DrawSourceTextureField();
            EditorGUILayout.Space(8);
            DrawHistogramPanel();
            EditorGUILayout.Space(6);
            DrawLayerList();
            EditorGUILayout.Space(12);
            DrawPresets();
            EditorGUILayout.Space(4);
            DrawStatusLine();
            EditorGUILayout.Space(4);
            DrawPreviewPanel();
            EditorGUILayout.Space(6);

            if (_previewDirty) UpdatePreview();

            ApplyRevertGUI();
        }

        protected override void Apply()
        {
            WriteJsonToDisk();
            _valuesChanged = false;
            base.Apply();
        }

        public override bool HasModified() => _valuesChanged;

        private void BuildLayerList()
        {
            _layerList = new ReorderableList(_layers, typeof(AdjustmentLayerData), true, true, true, true);
            _layerList.drawHeaderCallback = r => EditorGUI.LabelField(r, "Adjustment Layers", EditorStyles.boldLabel);
            _layerList.drawElementCallback = DrawLayerElement;
            _layerList.elementHeightCallback = GetLayerHeight;
            _layerList.onAddCallback = OnAddLayer;
            _layerList.onChangedCallback = _ => { _valuesChanged = true; _previewDirty = true; };
            _layerList.onRemoveCallback = _ =>
            {
                if (_layers.Count > 1) { ReorderableList.defaultBehaviours.DoRemoveButton(_layerList); _valuesChanged = true; _previewDirty = true; }
            };
        }

        private float GetLayerHeight(int index)
        {
            if (index < 0 || index >= _layers.Count) return 22f;
            if (index != _expandedLayer) return 22f;
            return GetExpandedHeight(_layers[index]);
        }

        private float GetExpandedHeight(AdjustmentLayerData layer)
        {
            LayerType t = layer.GetLayerType();
            float h = 26f;
            switch (t)
            {
                case LayerType.BrightnessContrast: h += 32 + 32 + 32 + 24; break; // 2 sliders + adv foldout
                case LayerType.HueSaturationVibrance: h += 32 + 32 + 32 + 10; break;
                case LayerType.ToneCurve: h += 190; break;
                case LayerType.Tint: h += 32 + 24; break;
                case LayerType.Overlay: h += 32 + 32 + 42; break;
            }
            return h;
        }

        private void DrawLayerElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index < 0 || index >= _layers.Count) return;

            AdjustmentLayerData layer = _layers[index];
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, 20);

            if (_layerHeaderStyle == null)
            {
                _layerHeaderStyle = new GUIStyle(EditorStyles.toolbarButton);
                _layerHeaderStyle.alignment = TextAnchor.MiddleLeft;
            }

            Rect toggleRect = new Rect(headerRect.x + 4, headerRect.y + 3, 14, 14);
            layer.enabled = EditorGUI.Toggle(toggleRect, layer.enabled);

            Rect nameRect = new Rect(headerRect.x + 24, headerRect.y + 1, headerRect.width - 100, 18);
            string layerName = layer.name ?? layer.GetLayerType().ToString();

            if (index == _expandedLayer)
                layer.name = EditorGUI.TextField(nameRect, layerName);
            else
                EditorGUI.LabelField(nameRect, layerName);

            Rect expandRect = new Rect(headerRect.x + headerRect.width - 56, headerRect.y + 2, 20, 16);
            if (GUI.Button(expandRect, index == _expandedLayer ? "▲" : "▼", EditorStyles.miniButton))
            {
                _expandedLayer = (index == _expandedLayer) ? -1 : index;
                Repaint();
            }

            if (index == _expandedLayer)
            {
                Rect bodyRect = new Rect(rect.x + 16, rect.y + 22, rect.width - 20, rect.height - 22);
                DrawLayerControls(bodyRect, layer);
            }
        }

        private void DrawLayerControls(Rect rect, AdjustmentLayerData layer)
        {
            float y = rect.y;
            float w = rect.width;
            LayerType t = layer.GetLayerType();

            EditorGUI.BeginChangeCheck();

            switch (t)
            {
                case LayerType.BrightnessContrast:
                    layer.brightness = DrawMiniMagnifyingSlider(new Rect(rect.x, y, w, 28), "Brightness", layer.brightness, -1f, 1f, _brightnessBarTex);
                    y += 32;
                    layer.contrast = DrawMiniMagnifyingSlider(new Rect(rect.x, y, w, 28), "Contrast", layer.contrast, 0f, 2f, _contrastBarTex);
                    y += 32;
                    DrawAdvancedRangeFoldout(rect.x, ref y, w, 0, ref layer.inputBlack, ref layer.inputWhite, ref layer.outputBlack, ref layer.outputWhite);
                    break;

                case LayerType.HueSaturationVibrance:
                    layer.hueShift = DrawMiniMagnifyingSlider(new Rect(rect.x, y, w, 28), "Hue Shift", layer.hueShift, -180f, 180f, _hueBarTex);
                    y += 32;
                    layer.saturation = DrawMiniMagnifyingSlider(new Rect(rect.x, y, w, 28), "Saturation", layer.saturation, 0f, 2f, _saturationBarTex);
                    y += 32;
                    layer.vibrance = DrawMiniMagnifyingSlider(new Rect(rect.x, y, w, 28), "Vibrance", layer.vibrance, 0f, 1f, _saturationBarTex);
                    break;

                case LayerType.ToneCurve:
                    DrawInlineCurveEditor(new Rect(rect.x, y, w, 180), layer);
                    break;

                case LayerType.Tint:
                    layer.TintColor = EditorGUI.ColorField(new Rect(rect.x, y, w, 18), "Color", layer.TintColor);
                    y += 22;
                    layer.tintStrength = DrawMiniMagnifyingSlider(new Rect(rect.x, y, w, 28), "Strength", layer.tintStrength, 0f, 1f, null);
                    break;

                case LayerType.Overlay:
                    layer.OverlayColor = EditorGUI.ColorField(new Rect(rect.x, y, w, 18), "Color", layer.OverlayColor);
                    y += 22;
                    layer.overlayStrength = DrawMiniMagnifyingSlider(new Rect(rect.x, y, w, 28), "Strength", layer.overlayStrength, 0f, 1f, null);
                    y += 32;
                    string[] blendNames = { "Normal", "Multiply", "Screen", "Overlay" };
                    layer.overlayBlendMode = EditorGUI.Popup(new Rect(rect.x, y, w, 18), "Blend Mode", layer.overlayBlendMode, blendNames);
                    y += 22;
                    DrawAdvancedRangeFoldout(rect.x, ref y, w, 1, ref layer.inputBlack, ref layer.inputWhite, ref layer.outputBlack, ref layer.outputWhite);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                _valuesChanged = true;
                _previewDirty = true;
            }
        }

        private void DrawAdvancedRangeFoldout(float x, ref float y, float w, int slot, ref float inB, ref float inW, ref float outB, ref float outW)
        {
            int key = _expandedLayer * 10 + slot;
            bool show = EditorGUI.Foldout(new Rect(x, y, w, 18), _expandedAdvanced == key, "Input / Output Range", true);
            _expandedAdvanced = show ? key : -1;
            y += 18;
            if (!show) return;
            EditorGUI.MinMaxSlider(new Rect(x + 8, y, w - 8, 18), "Input", ref inB, ref inW, 0f, 1f);
            y += 20;
            EditorGUI.MinMaxSlider(new Rect(x + 8, y, w - 8, 18), "Output", ref outB, ref outW, 0f, 1f);
            y += 22;
        }

        private void DrawInlineCurveEditor(Rect curveRect, AdjustmentLayerData layer)
        {
            Color bg = new Color(0.12f, 0.12f, 0.12f);
            Color grid = new Color(0.25f, 0.25f, 0.25f);
            Color border = new Color(0.4f, 0.4f, 0.4f);

            EditorGUI.DrawRect(curveRect, bg);
            for (int i = 1; i < 4; i++)
            {
                float t = i / 4f;
                EditorGUI.DrawRect(new Rect(curveRect.x, curveRect.y + t * curveRect.height, curveRect.width, 1), grid);
                EditorGUI.DrawRect(new Rect(curveRect.x + t * curveRect.width, curveRect.y, 1, curveRect.height), grid);
            }
            EditorGUI.DrawRect(new Rect(curveRect.x, curveRect.y, curveRect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(curveRect.x, curveRect.yMax - 1f, curveRect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(curveRect.x, curveRect.y, 1f, curveRect.height), border);
            EditorGUI.DrawRect(new Rect(curveRect.xMax - 1f, curveRect.y, 1f, curveRect.height), border);

            AnimationCurve curve = TextureAdjustmentImporter.BuildCurveFromKeys(layer.toneCurveKeys);
            Handles.BeginGUI();
            Handles.color = Color.cyan;
            int steps = Mathf.Max((int)curveRect.width, 200);
            Vector3[] pts = new Vector3[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float v = Mathf.Clamp01(curve.Evaluate(t));
                pts[i] = new Vector3(curveRect.x + t * curveRect.width, curveRect.y + (1f - v) * curveRect.height, 0);
            }
            Handles.DrawAAPolyLine(2.5f, pts);
            Handles.EndGUI();

            Event e = Event.current;
            if (e.isMouse && curveRect.Contains(e.mousePosition))
            {
                Vector2 lp = e.mousePosition - curveRect.position;
                float lt = Mathf.Clamp01(lp.x / curveRect.width);
                float lv = Mathf.Clamp01(1f - lp.y / curveRect.height);

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    int near = FindClosestCurveKey(curve, curveRect, e.mousePosition, 12f);
                    if (near >= 0) _selectedCurveKey = near;
                    else if (curve.keys.Length < 16) { curve.AddKey(lt, lv); _selectedCurveKey = curve.keys.Length - 1; _valuesChanged = true; _previewDirty = true; }
                    e.Use(); Repaint();
                }
                else if (e.type == EventType.MouseDrag && _isDraggingCurve && _selectedCurveKey >= 0)
                {
                    Keyframe[] kfs = curve.keys;
                    kfs[_selectedCurveKey].time = lt;
                    kfs[_selectedCurveKey].value = lv;
                    Array.Sort(kfs, (a, b) => a.time.CompareTo(b.time));
                    curve.keys = kfs;
                    _valuesChanged = true; _previewDirty = true;
                    e.Use(); Repaint();
                }
                else if (e.type == EventType.MouseUp) { _isDraggingCurve = false; e.Use(); }
                else if (e.type == EventType.MouseDown && e.button == 1)
                {
                    int near = FindClosestCurveKey(curve, curveRect, e.mousePosition, 12f);
                    if (near >= 0 && curve.keys.Length > 2) { curve.RemoveKey(near); _selectedCurveKey = -1; _valuesChanged = true; _previewDirty = true; e.Use(); Repaint(); }
                }
            }

            layer.toneCurveKeys = Array.ConvertAll(curve.keys, k => new SerializableKeyframe(k));
        }

        private static int FindClosestCurveKey(AnimationCurve curve, Rect r, Vector2 mouse, float maxDist)
        {
            int best = -1;
            float bestD = maxDist;
            for (int i = 0; i < curve.keys.Length; i++)
            {
                float kx = r.x + curve.keys[i].time * r.width;
                float ky = r.y + (1f - curve.keys[i].value) * r.height;
                float d = Vector2.Distance(mouse, new Vector2(kx, ky));
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private float DrawMiniMagnifyingSlider(Rect rect, string label, float value, float min, float max, Texture2D barTex)
        {
            float labelW = 70f;
            float valueW = 44f;
            Rect lr = new Rect(rect.x, rect.y + 4f, labelW, 16f);
            Rect vr = new Rect(rect.xMax - valueW, rect.y + 4f, valueW, 16f);
            Rect tr = new Rect(rect.x + labelW + 4f, rect.y + 8f, rect.width - labelW - valueW - 8f, 8f);

            GUI.Label(lr, label);
            EditorGUI.DrawRect(tr, new Color(0.15f, 0.15f, 0.15f));
            if (barTex != null) { var c = GUI.color; GUI.color = new Color(1, 1, 1, 0.4f); GUI.DrawTexture(tr, barTex, ScaleMode.StretchToFill); GUI.color = c; }

            float t = Mathf.InverseLerp(min, max, value);
            float tx = tr.x + t * tr.width;
            EditorGUI.DrawRect(new Rect(tx - 1f, tr.y - 1f, 2f, tr.height + 2f), Color.white);

            GUI.Label(vr, value.ToString("F2"), EditorStyles.miniLabel);

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.isMouse && tr.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDown) { GUIUtility.hotControl = controlId; e.Use(); }
                else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
                {
                    float nt = Mathf.Clamp01((e.mousePosition.x - tr.x) / tr.width);
                    value = Mathf.Lerp(min, max, nt);
                    GUI.changed = true; e.Use(); Repaint();
                }
                else if (e.type == EventType.MouseUp && GUIUtility.hotControl == controlId) { GUIUtility.hotControl = 0; e.Use(); }
            }
            return value;
        }

        private void OnAddLayer(ReorderableList list)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Brightness / Contrast"), false, () => AddLayer(LayerType.BrightnessContrast));
            menu.AddItem(new GUIContent("Hue / Saturation / Vibrance"), false, () => AddLayer(LayerType.HueSaturationVibrance));
            menu.AddItem(new GUIContent("Tone Curve"), false, () => AddLayer(LayerType.ToneCurve));
            menu.AddItem(new GUIContent("Tint"), false, () => AddLayer(LayerType.Tint));
            menu.AddItem(new GUIContent("Overlay"), false, () => AddLayer(LayerType.Overlay));
            menu.ShowAsContext();
        }

        private void AddLayer(LayerType type)
        {
            _layers.Add(new AdjustmentLayerData(type, type.ToString()));
            _expandedLayer = _layers.Count - 1;
            _valuesChanged = true;
            _previewDirty = true;
            Repaint();
        }

        private void DrawLayerList()
        {
            EnsureBarTextures();
            if (_layerList == null) BuildLayerList();
            _layerList.DoLayoutList();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Texture Adjustment", new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter }, GUILayout.Height(24));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(1)), new Color(0.3f, 0.3f, 0.3f));
        }

        private void DrawSourceTextureField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Source Texture", GUILayout.Width(100));
            EditorGUI.BeginChangeCheck();
            _sourceTexture = (Texture2D)EditorGUILayout.ObjectField(_sourceTexture, typeof(Texture2D), false, GUILayout.Height(18));
            if (EditorGUI.EndChangeCheck() && _sourceTexture != null)
            {
                string p = AssetDatabase.GetAssetPath(_sourceTexture);
                string g = AssetDatabase.AssetPathToGUID(p);
                if (g != _sourceGUID) { _sourceGUID = g; _valuesChanged = true; ComputeHistogram(); _previewDirty = true; }
            }
            if (GUILayout.Button("Select", GUILayout.Width(50)) && !string.IsNullOrEmpty(_sourceGUID))
            {
                string p = AssetDatabase.GUIDToAssetPath(_sourceGUID);
                if (!string.IsNullOrEmpty(p)) { var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p); if (t) EditorGUIUtility.PingObject(t); }
            }
            EditorGUILayout.EndHorizontal();
            if (string.IsNullOrEmpty(_sourceGUID) || _sourceTexture == null)
                EditorGUILayout.HelpBox("No source texture assigned.", MessageType.Warning);
        }

        private void DrawPresets()
        {
            EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
            int cols = 4;
            int rows = Mathf.CeilToInt(Presets.Length / (float)cols);
            for (int r = 0; r < rows; r++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (i >= Presets.Length) { GUILayout.FlexibleSpace(); continue; }
                    if (GUILayout.Button(Presets[i].Name, new GUIStyle(GUI.skin.button) { fontSize = 9, padding = new RectOffset(4, 4, 3, 3), fixedHeight = 22 }, GUILayout.ExpandWidth(true)))
                        ApplyPreset(Presets[i]);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawStatusLine()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_valuesChanged ? "Unsaved — click Apply below" : "Settings match file", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (_previewTexture != null) EditorGUILayout.LabelField($"{_previewTexture.width}x{_previewTexture.height}", EditorStyles.miniLabel, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreviewPanel()
        {
            if (_previewTexture == null) return;
            Rect pr = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.MinHeight(140), GUILayout.MaxHeight(260));
            float a = (float)_previewTexture.width / Mathf.Max(1, _previewTexture.height);
            float dh = Mathf.Min(pr.height, pr.width / a);
            float dw = dh * a;
            GUI.DrawTexture(new Rect(pr.x + (pr.width - dw) / 2f, pr.y + (pr.height - dh) / 2f, dw, dh), _previewTexture, ScaleMode.ScaleToFit);
        }

        private void DrawHistogramPanel()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Histogram", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            string[] modes = { "L", "R", "G", "B", "RGB" };
            int nm = GUILayout.Toolbar(_histogramMode, modes, GUILayout.Height(18));
            if (nm != _histogramMode) { _histogramMode = nm; Repaint(); }
            EditorGUILayout.EndHorizontal();

            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(70));
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.08f));

            if (!_histogramValid || _histL == null) { GUI.Label(rect, "No data", EditorStyles.centeredGreyMiniLabel); return; }

            float rh = rect.height, rw = rect.width;
            int bins = Mathf.Min(256, (int)rw);
            for (int i = 0; i < bins; i++)
            {
                int idx = Mathf.Clamp(Mathf.FloorToInt(i * 256f / bins), 0, 255);
                float bw = Mathf.Max(1f, rw / bins * 0.8f);
                float x = rect.x + i * rw / bins;

                if ((_histogramMode == 0 || _histogramMode == 4) && _histMaxL > 0)
                { float h = Mathf.Max(1f, (float)_histL[idx] / _histMaxL * rh); EditorGUI.DrawRect(new Rect(x, rect.yMax - h, bw, h), new Color(1, 1, 1, 0.4f)); }
                if ((_histogramMode == 1 || _histogramMode == 4) && _histMaxR > 0)
                { float h = Mathf.Max(1f, (float)_histR[idx] / _histMaxR * rh); EditorGUI.DrawRect(new Rect(x, rect.yMax - h, bw, h), new Color(1, 0.2f, 0.2f, 0.45f)); }
                if ((_histogramMode == 2 || _histogramMode == 4) && _histMaxG > 0)
                { float h = Mathf.Max(1f, (float)_histG[idx] / _histMaxG * rh); EditorGUI.DrawRect(new Rect(x, rect.yMax - h, bw, h), new Color(0.2f, 1, 0.2f, 0.45f)); }
                if ((_histogramMode == 3 || _histogramMode == 4) && _histMaxB > 0)
                { float h = Mathf.Max(1f, (float)_histB[idx] / _histMaxB * rh); EditorGUI.DrawRect(new Rect(x, rect.yMax - h, bw, h), new Color(0.3f, 0.4f, 1, 0.45f)); }
            }
        }

        private void ApplyPreset(AdjustmentPreset preset)
        {
            _layers = preset.Factory();
            BuildLayerList();
            _expandedLayer = -1;
            _expandedAdvanced = -1;
            _valuesChanged = true;
            _previewDirty = true;
            Repaint();
        }

        private void RefreshSourceTexture()
        {
            if (!string.IsNullOrEmpty(_sourceGUID))
            {
                string p = AssetDatabase.GUIDToAssetPath(_sourceGUID);
                if (!string.IsNullOrEmpty(p)) _sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
        }

        private void ComputeHistogram()
        {
            if (_sourceTexture == null) { _histogramValid = false; return; }
            _histL = new int[256]; _histR = new int[256]; _histG = new int[256]; _histB = new int[256];
            _histMaxL = _histMaxR = _histMaxG = _histMaxB = 1;
            Texture2D src = (_previewTexture != null) ? _previewTexture : _sourceTexture;
            Texture2D tex = AcquirePixelsNonDestructive(src);
            if (tex == null) { _histogramValid = false; return; }
            try
            {
                Color[] px = tex.GetPixels();
                foreach (var c in px)
                {
                    float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                    int li = Mathf.Clamp((int)(lum * 255f), 0, 255);
                    int ri = Mathf.Clamp((int)(c.r * 255f), 0, 255);
                    int gi = Mathf.Clamp((int)(c.g * 255f), 0, 255);
                    int bi = Mathf.Clamp((int)(c.b * 255f), 0, 255);
                    _histL[li]++; _histR[ri]++; _histG[gi]++; _histB[bi]++;
                    _histMaxL = Mathf.Max(_histMaxL, _histL[li]);
                    _histMaxR = Mathf.Max(_histMaxR, _histR[ri]);
                    _histMaxG = Mathf.Max(_histMaxG, _histG[gi]);
                    _histMaxB = Mathf.Max(_histMaxB, _histB[bi]);
                }
            }
            finally { if (tex != _sourceTexture && tex != _previewTexture) DestroyImmediate(tex); }
            _histogramValid = true;
        }

        private static Texture2D AcquirePixelsNonDestructive(Texture2D source)
        {
            if (source == null) return null;
            if (source.isReadable) return source;
            int w = source.width, h = source.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            Texture2D copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        private void UpdatePreview()
        {
            if (_sourceTexture == null) return;
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) return;
            if (_previewMaterial == null) _previewMaterial = new Material(shader);

            int w = _sourceTexture.width, h = _sourceTexture.height;
            if (w <= 0 || h <= 0) return;

            RenderTexture ping = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture pong = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(_sourceTexture, ping);
            bool usePing = true;

            foreach (var layer in _layers)
            {
                if (!layer.enabled) continue;
                RenderTexture src = usePing ? ping : pong;
                RenderTexture dst = usePing ? pong : ping;
                TextureAdjustmentImporter.SetMaterialForLayer(_previewMaterial, layer);
                Graphics.Blit(src, dst, _previewMaterial);
                usePing = !usePing;
            }

            RenderTexture final = usePing ? ping : pong;

            if (_previewTexture != null && (_previewTexture.width != w || _previewTexture.height != h))
            { DestroyImmediate(_previewTexture); _previewTexture = null; }
            if (_previewTexture == null) _previewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false, false);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = final;
            _previewTexture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            _previewTexture.Apply();
            RenderTexture.active = prev;

            RenderTexture.ReleaseTemporary(ping);
            RenderTexture.ReleaseTemporary(pong);
            DestroyImmediate(_previewMaterial); _previewMaterial = null;

            _previewDirty = false;
            ComputeHistogram();
        }

        private void ReadJsonIntoCaches()
        {
            TextureAdjustmentImporter importer = (TextureAdjustmentImporter)target;
            _assetPath = importer.assetPath;
            if (string.IsNullOrEmpty(_assetPath) || !File.Exists(_assetPath)) return;

            try
            {
                string json = File.ReadAllText(_assetPath);
                AdjustmentData data = JsonConvert.DeserializeObject<AdjustmentData>(json) ?? new AdjustmentData();
                _sourceGUID = data.sourceTextureGUID ?? "";
                _layers = data.GetEffectiveLayers();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TextureAdjustment] Failed to read {_assetPath}: {ex.Message}");
            }
        }

        private void WriteJsonToDisk()
        {
            if (string.IsNullOrEmpty(_assetPath)) return;
            var data = new AdjustmentData { sourceTextureGUID = _sourceGUID ?? "", layers = _layers };
            try { File.WriteAllText(_assetPath, JsonConvert.SerializeObject(data, Formatting.Indented)); }
            catch (Exception ex) { Debug.LogError($"[TextureAdjustment] Failed to write: {ex.Message}"); }
        }

        private static void EnsureBarTextures()
        {
            if (_brightnessBarTex != null) return;
            _brightnessBarTex = CreateGradientTex(128, 8, t => Color.Lerp(Color.black, Color.white, t));
            _contrastBarTex = CreateGradientTex(128, 8, t => { float v = (t - 0.5f) * 3f + 0.5f; return Color.Lerp(Color.black, Color.white, Mathf.Clamp01(v)); });
            _hueBarTex = CreateGradientTex(128, 8, t => Color.HSVToRGB(t, 1f, 1f));
            _saturationBarTex = CreateGradientTex(128, 8, t => Color.Lerp(new Color(0.5f, 0.5f, 0.5f), Color.red, t));
        }

        private static Texture2D CreateGradientTex(int w, int h, Func<float, Color> sampler)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int x = 0; x < w; x++) { Color c = sampler(x / (float)(w - 1)); for (int y = 0; y < h; y++) tex.SetPixel(x, y, c); }
            tex.Apply();
            return tex;
        }
    }
}
