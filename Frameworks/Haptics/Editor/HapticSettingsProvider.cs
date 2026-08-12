using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics.Editor
{
    internal sealed class HapticSettingsProvider : SettingsProvider
    {
        #region Constants

        private const string SettingsAssetPath =
            "Assets/Resources/Integration/Managers/HapticConfiguration.asset";

        private const string ProviderAssetFolder =
            "Assets/Resources/Integration/Haptics/Providers";

        private const string AndroidManifestPath =
            "Assets/Plugins/Android/AndroidManifest.xml";

        private const string VibratePermission =
            "android.permission.VIBRATE";

        private const string TabPrefKey =
            "HapticSettingsProvider_SelectedTab";

        private const string ProviderSubtabPrefKey =
            "HapticSettingsProvider_ProviderSubtab";

        private const string SearchPrefKey =
            "HapticSettingsProvider_Search";

        private const int ButtonWidth = 22;
        private const int HeaderHeight = 18;
        private const int AddButtonHeight = 26;
        private const int AddButtonWidth = 160;
        private const int ProviderEnableWidth = 110;
        private const int ActiveWidth = 90;
        private const int SearchLabelWidth = 50;
        private const int SpaceGap = 8;
        private const int TimelineHeight = 46;

        private static readonly string[] TabNames = { "Providers", "Rhythms" };
        private static readonly string[] SubtabNames = { "Android", "iOS" };
        private static readonly string[] PlatformFieldNames =
            { "_androidProvider", "_iosProvider" };

        private static readonly string[] SortOptions =
            { "id", "name", "duration" };

        private static readonly Color ActiveGreen = new Color(0.3f, 0.7f, 0.3f);
        private static readonly Color DeleteRed = new Color(1f, 0.4f, 0.4f);
        private static readonly Color DividerColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
        private static readonly Color CardDividerColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
        private static readonly Color FilterActiveBg = new Color(0.25f, 0.45f, 0.75f);
        private static readonly Color FilterInactiveBg = new Color(0.35f, 0.35f, 0.35f);

        #endregion

        #region Private Fields

        private SerializedObject _serializedSettings;
        private int _selectedTabIndex;
        private int _selectedSubtabIndex;
        private Vector2 _scrollPosition;
        private Vector2 _rhythmsScrollPosition;

        private string _searchText = string.Empty;
        private int _sortMode;    // 0=id 1=name 2=duration
        private int _rhythmFilter; // 0=all 1=has keyframes 2=empty

        private readonly Dictionary<string, int> _selectedKeyframeByRhythm =
            new Dictionary<string, int>();

        private Dictionary<ScriptableObject, UnityEditor.Editor> _inlineEditorCache;

        private static Type[] _cachedProviderTypes;

        #endregion

        #region Constructor

        private HapticSettingsProvider(
            string path,
            SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        {
            _selectedTabIndex = EditorPrefs.GetInt(TabPrefKey, 0);
            _selectedSubtabIndex = EditorPrefs.GetInt(ProviderSubtabPrefKey, 0);
            _searchText = EditorPrefs.GetString(SearchPrefKey, string.Empty);
        }

        #endregion

        #region SettingsProvider Registration

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new HapticSettingsProvider(
                "Project/Frameworks/Haptics",
                SettingsScope.Project,
                new[] { "haptic", "haptics", "vibration", "vibrate", "tactile", "rumble", "feedback" }
            );
        }

        #endregion

        #region GUI

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            HapticConfiguration settings = GetOrCreateSettings();
            if (settings == null) return;

            EnsureSerializedObject(settings);
            _serializedSettings.Update();

            EditorGUILayout.Space();
            DrawStatusBar(settings);
            EditorGUILayout.Space();

            int newTab = GUILayout.Toolbar(_selectedTabIndex, TabNames);
            if (newTab != _selectedTabIndex)
            {
                _selectedTabIndex = newTab;
                EditorPrefs.SetInt(TabPrefKey, _selectedTabIndex);
            }
            EditorGUILayout.Space();

            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(dividerRect, DividerColor);
            EditorGUILayout.Space();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_selectedTabIndex)
            {
                case 0: DrawProvidersTab(); break;
                case 1: DrawRhythmsTab(); break;
            }

            EditorGUILayout.EndScrollView();
            _serializedSettings.ApplyModifiedProperties();
        }

        public override void OnDeactivate()
        {
            base.OnDeactivate();
            EditorPrefs.SetInt(TabPrefKey, _selectedTabIndex);
            EditorPrefs.SetInt(ProviderSubtabPrefKey, _selectedSubtabIndex);
            EditorPrefs.SetString(SearchPrefKey, _searchText);
            DestroyInlineEditors();
            _cachedProviderTypes = null;
        }

        #endregion

        #region Status Bar

        private void DrawStatusBar(HapticConfiguration config)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int rhythmCount = config.Rhythms.Length;
                bool hasAndroid = config.AndroidProvider != null;
                bool hasIos = config.IosProvider != null;
                bool valid = config.HasAnyProvider && rhythmCount > 0;

                Color statusColor = valid ? Color.green : Color.yellow;
                string statusText = valid ? "Ready" : "Incomplete";

                var style = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = statusColor }
                };

                string platforms = string.Empty;
                if (hasAndroid) platforms += "Android: " + config.AndroidProvider.ProviderId + "  ";
                if (hasIos) platforms += "iOS: " + config.IosProvider.ProviderId;
                if (string.IsNullOrEmpty(platforms)) platforms = "No provider";

                EditorGUILayout.LabelField(
                    "Status: " + statusText + "  |  "
                    + rhythmCount + " rhythm(s)  |  " + platforms,
                    style
                );
            }
        }

        #endregion

        #region Tab - Providers

        private void DrawProvidersTab()
        {
            EditorGUILayout.HelpBox(
                "Select the haptic provider for each platform. "
                + "Haptics require a different runtime per platform, so "
                + "Android and iOS are configured separately.",
                MessageType.None);
            EditorGUILayout.Space();

            int newSubtab = GUILayout.Toolbar(_selectedSubtabIndex, SubtabNames, GUILayout.Height(22));
            if (newSubtab != _selectedSubtabIndex)
            {
                _selectedSubtabIndex = newSubtab;
                EditorPrefs.SetInt(ProviderSubtabPrefKey, _selectedSubtabIndex);
            }
            EditorGUILayout.Space();

            if (_selectedSubtabIndex == 0)
            {
                DrawAndroidSubtab();
            }
            else
            {
                DrawIosSubtab();
            }
        }

        private void DrawAndroidSubtab()
        {
            DrawVibratePermissionBox();
            DrawAndroidCompatibilityBox();
            EditorGUILayout.Space();
            DrawProviderSlot(PlatformFieldNames[0], "Android Provider");
        }

        private void DrawIosSubtab()
        {
            EditorGUILayout.HelpBox(
                "iOS Simulator has no Taptic Engine — capability checks "
                + "must not crash; the provider reports IsAvailable=false "
                + "and falls back to no-op.",
                MessageType.Warning);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Frequency is best-effort on iOS — CHHapticEngine exposes "
                + "intensity, not frequency parity. HapticKeyframe.frequency "
                + "acts as a continuous-intensity driver on iOS and maps to "
                + "Hz spacing on Android. Verify feel on a real device.",
                MessageType.Warning);
            EditorGUILayout.Space();

            DrawProviderSlot(PlatformFieldNames[1], "iOS Provider");
        }

        private void DrawVibratePermissionBox()
        {
            bool hasPermission = ManifestHasVibratePermission();

            if (hasPermission)
            {
                EditorGUILayout.HelpBox(
                    "Android VIBRATE permission is present "
                    + "(Assets/Plugins/Android/AndroidManifest.xml).",
                    MessageType.Info);

                GUI.backgroundColor = ActiveGreen;
                GUILayout.Label(
                    "VIBRATE permission present",
                    EditorStyles.miniButton,
                    GUILayout.Width(180));
                GUI.backgroundColor = Color.white;
                EditorGUILayout.Space();
                return;
            }

            EditorGUILayout.HelpBox(
                "Android VIBRATE permission is missing. Haptics silently "
                + "no-op on device without it.",
                MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                        "+ Add VIBRATE Permission",
                        GUILayout.Width(180),
                        GUILayout.Height(22)))
                {
                    AddVibratePermissionToManifest();
                }

                EditorGUILayout.LabelField(
                    "creates/injects " + AndroidManifestPath + " (idempotent)",
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.Space();
        }

        private void DrawAndroidCompatibilityBox()
        {
            EditorGUILayout.HelpBox(
                "Frequency is best-effort: Android maps to Hz; iOS continuous "
                + "haptics use intensity only. HeavyImpact auto-downgrades to "
                + "MediumImpact when unsupported; legacy iOS uses impact ids "
                + "1519/1520/1521. Verify on device.",
                MessageType.Warning);
        }

        private void DrawProviderSlot(string fieldName, string slotTitle)
        {
            EditorGUILayout.LabelField(slotTitle, EditorStyles.boldLabel);

            SerializedProperty providerProp = _serializedSettings.FindProperty(fieldName);
            if (providerProp == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find '" + fieldName + "' field.",
                    MessageType.Error);
                return;
            }

            Type[] providerTypes = GetCachedProviderTypes(typeof(IHapticProvider));
            if (providerTypes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No ScriptableObject types implementing IHapticProvider found.",
                    MessageType.Info);
                return;
            }

            ScriptableObject currentProvider = providerProp.objectReferenceValue as ScriptableObject;
            Type currentType = currentProvider != null ? currentProvider.GetType() : null;

            foreach (Type providerType in providerTypes)
            {
                bool isActive = currentType != null && providerType.IsAssignableFrom(currentType);
                DrawProviderCard(providerType, isActive, currentProvider, providerProp);
            }
        }

        private void DrawProviderCard(
            Type providerType,
            bool isActive,
            ScriptableObject currentProvider,
            SerializedProperty prop)
        {
            string displayName = ObjectNames.NicifyVariableName(providerType.Name);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(displayName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (isActive && currentProvider != null)
                    {
                        GUI.backgroundColor = ActiveGreen;
                        GUILayout.Label(
                            "ACTIVE", EditorStyles.miniButton,
                            GUILayout.Width(ActiveWidth));
                        GUI.backgroundColor = Color.white;

                        if (DrawDeleteButton())
                        {
                            DeleteAssetAndClearField(currentProvider, prop);
                            return;
                        }
                    }
                    else
                    {
                        if (GUILayout.Button(
                                "Enable",
                                GUILayout.Width(ProviderEnableWidth)))
                        {
                            ScriptableObject asset =
                                FindOrCreateProviderAsset(providerType);
                            prop.objectReferenceValue = asset;
                            _serializedSettings.ApplyModifiedProperties();
                            EditorUtility.SetDirty(_serializedSettings.targetObject);
                            AssetDatabase.SaveAssets();
                            DestroyInlineEditors();
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }

                if (isActive && currentProvider != null)
                {
                    GUILayout.Space(4);
                    DrawInlineInspectorFor(currentProvider);
                }
            }
        }

        #endregion

        #region Tab - Rhythms

        private void DrawRhythmsTab()
        {
            EditorGUILayout.HelpBox(
                "Define haptic rhythms. Each rhythm is a keyframe timeline; "
                + "designers can seed from a template and tweak in the "
                + "timeline editor.",
                MessageType.None);

            DrawRhythmToolbar();

            SerializedProperty rhythmsProp = _serializedSettings.FindProperty("_rhythms");
            if (rhythmsProp == null || !rhythmsProp.isArray)
            {
                EditorGUILayout.HelpBox(
                    "Could not find '_rhythms' field.",
                    MessageType.Error);
                return;
            }

            int[] order = BuildVisibleOrder(rhythmsProp);
            if (order.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No rhythms match the current search/filter.",
                    MessageType.Info);
            }

            _rhythmsScrollPosition = EditorGUILayout.BeginScrollView(_rhythmsScrollPosition);

            for (int i = 0; i < order.Length; i++)
            {
                int index = order[i];
                if (DrawRhythmCard(rhythmsProp, index))
                {
                    // Rhythm was deleted; rebuild visible order next frame.
                    GUIUtility.ExitGUI();
                    break;
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();
            DrawAddRhythmRow(rhythmsProp);
        }

        private void DrawRhythmToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Search", GUILayout.Width(SearchLabelWidth));
                _searchText = EditorGUILayout.TextField(_searchText);

                EditorGUILayout.Space(SpaceGap);

                EditorGUILayout.LabelField("Sort", GUILayout.Width(30));
                _sortMode = EditorGUILayout.Popup(_sortMode, SortOptions, GUILayout.Width(86));

                EditorGUILayout.Space(SpaceGap);

                _rhythmFilter = DrawFilterButtons(_rhythmFilter);
            }
        }

        private int DrawFilterButtons(int currentFilter)
        {
            int newFilter = currentFilter;
            if (DrawFilterButton("All", 0, newFilter, 40)) newFilter = 0;
            if (DrawFilterButton("Has kf", 1, newFilter, 56)) newFilter = 1;
            if (DrawFilterButton("Empty", 2, newFilter, 56)) newFilter = 2;
            return newFilter;
        }

        private static bool DrawFilterButton(string label, int value, int current, int width)
        {
            bool isActive = current == value;
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = isActive ? FilterActiveBg : FilterInactiveBg;

            bool clicked = GUILayout.Button(
                label,
                EditorStyles.miniButton,
                GUILayout.Width(width),
                GUILayout.Height(HeaderHeight));

            GUI.backgroundColor = oldBg;
            return clicked;
        }

        private int[] BuildVisibleOrder(SerializedProperty rhythmsProp)
        {
            List<int> visible = new List<int>();

            for (int i = 0; i < rhythmsProp.arraySize; i++)
            {
                var rhythm = rhythmsProp.GetArrayElementAtIndex(i).objectReferenceValue as HapticRhythm;
                if (rhythm == null) continue;
                if (!MatchesRhythmFilter(rhythm)) continue;
                visible.Add(i);
            }

            visible.Sort((a, b) =>
            {
                var ra = rhythmsProp.GetArrayElementAtIndex(a).objectReferenceValue as HapticRhythm;
                var rb = rhythmsProp.GetArrayElementAtIndex(b).objectReferenceValue as HapticRhythm;
                if (ra == null || rb == null) return 0;

                switch (_sortMode)
                {
                    case 0: return string.Compare(ra.Id, rb.Id, StringComparison.OrdinalIgnoreCase);
                    case 1: return string.Compare(ra.DisplayLabel, rb.DisplayLabel, StringComparison.OrdinalIgnoreCase);
                    default: return ra.ComputeDuration().CompareTo(rb.ComputeDuration());
                }
            });

            return visible.ToArray();
        }

        private bool MatchesRhythmFilter(HapticRhythm rhythm)
        {
            if (_rhythmFilter == 1 && rhythm.Keyframes.Length == 0) return false;
            if (_rhythmFilter == 2 && rhythm.Keyframes.Length > 0) return false;

            if (string.IsNullOrEmpty(_searchText)) return true;
            string query = _searchText.Trim();
            if (query.Length == 0) return true;

            if (rhythm.Id != null
                && rhythm.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (rhythm.DisplayName != null
                && rhythm.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return false;
        }

        /// <returns>True when the rhythm was deleted by the header.</returns>
        private bool DrawRhythmCard(SerializedProperty arrayProp, int index)
        {
            SerializedProperty element = arrayProp.GetArrayElementAtIndex(index);
            var rhythm = element.objectReferenceValue as HapticRhythm;
            if (rhythm == null) return false;

            SerializedObject rhythmSo = new SerializedObject(rhythm);
            rhythmSo.Update();

            string title = rhythm.DisplayLabel;
            if (string.IsNullOrEmpty(title)) title = "Rhythm " + (index + 1);

            bool deleted = false;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (DrawRhythmCardHeader(arrayProp, index, rhythm, title))
                {
                    deleted = true;
                }
                else
                {
                    Rect sep = EditorGUILayout.GetControlRect(false, 1f);
                    EditorGUI.DrawRect(sep, CardDividerColor);
                    GUILayout.Space(4);

                    DrawRhythmFields(rhythmSo);
                    DrawTimelineArea(rhythm, rhythmSo);
                    DrawSelectedKeyframePanel(rhythm, rhythmSo);

                    if (rhythm.HasTemplateSeed)
                    {
                        EditorGUILayout.LabelField(
                            "Template: " + HapticRhythmTemplates.GetName(rhythm.TemplateSeed),
                            EditorStyles.miniLabel);
                    }

                    rhythmSo.ApplyModifiedProperties();
                    rhythm.RefreshDuration();
                }
            }
            return deleted;
        }

        private void DrawRhythmFields(SerializedObject rhythmSo)
        {
            DrawPropFrom(rhythmSo, "_id", "ID");
            DrawPropFrom(rhythmSo, "_displayName", "Display Name");
            DrawPropFrom(rhythmSo, "_loop", "Loop");
        }

        private void DrawTimelineArea(HapticRhythm rhythm, SerializedObject rhythmSo)
        {
            if (!_selectedKeyframeByRhythm.TryGetValue(rhythm.Id, out int selected))
            {
                selected = -1;
            }

            Rect timelineRect = EditorGUILayout.GetControlRect(
                false, TimelineHeight, GUILayout.ExpandWidth(true));
            HapticRhythmTimelineDrawer.DrawTimeline(
                timelineRect, rhythm, rhythmSo, ref selected);

            _selectedKeyframeByRhythm[rhythm.Id] = selected;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Add Keyframe", GUILayout.Width(110)))
                {
                    SerializedProperty keyframesProp = rhythmSo.FindProperty("_keyframes");
                    if (keyframesProp != null)
                    {
                        float at = rhythm.ComputeDuration();
                        _selectedKeyframeByRhythm[rhythm.Id] =
                            HapticRhythmTimelineDrawer.AddKeyframe(keyframesProp, at);
                        ClearTemplateSeedIfEdited(rhythm, rhythmSo);
                    }
                }

                GUI.backgroundColor = DeleteRed;
                if (GUILayout.Button("Clear Keyframes", GUILayout.Width(110)))
                {
                    SerializedProperty keyframesProp = rhythmSo.FindProperty("_keyframes");
                    if (keyframesProp != null)
                    {
                        keyframesProp.ClearArray();
                        _selectedKeyframeByRhythm[rhythm.Id] = -1;
                        rhythmSo.ApplyModifiedProperties();
                        rhythm.ClearTemplateSeed();
                        EditorUtility.SetDirty(rhythm);
                    }
                }
                GUI.backgroundColor = Color.white;

                GUILayout.FlexibleSpace();
            }
        }

        private void DrawSelectedKeyframePanel(HapticRhythm rhythm, SerializedObject rhythmSo)
        {
            if (!_selectedKeyframeByRhythm.TryGetValue(rhythm.Id, out int selected)
                || selected < 0)
            {
                return;
            }

            SerializedProperty keyframesProp = rhythmSo.FindProperty("_keyframes");
            if (keyframesProp == null || selected >= keyframesProp.arraySize) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Keyframe " + (selected + 1) + " of " + keyframesProp.arraySize,
                EditorStyles.miniBoldLabel);

            SerializedProperty selectedElement = keyframesProp.GetArrayElementAtIndex(selected);
            EditorGUILayout.PropertyField(selectedElement, new GUIContent("Properties"), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Delete Selected", GUILayout.Width(110)))
                {
                    keyframesProp.DeleteArrayElementAtIndex(selected);
                    _selectedKeyframeByRhythm[rhythm.Id] = -1;
                    rhythmSo.ApplyModifiedProperties();
                    ClearTemplateSeedIfEdited(rhythm, rhythmSo);
                }
                GUILayout.FlexibleSpace();
            }
        }

        private static void ClearTemplateSeedIfEdited(HapticRhythm rhythm, SerializedObject rhythmSo)
        {
            if (rhythm.HasTemplateSeed)
            {
                rhythm.ClearTemplateSeed();
                rhythmSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(rhythm);
            }
        }

        private bool DrawRhythmCardHeader(
            SerializedProperty arrayProp,
            int index,
            HapticRhythm rhythm,
            string title)
        {
            bool deleted = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "(" + rhythm.Id + ")  " + rhythm.ComputeDuration().ToString("0.00") + "s",
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                DrawPreviewButtons(rhythm);

                using (new EditorGUI.DisabledScope(index == 0))
                {
                    if (GUILayout.Button(
                            "\u25B2", EditorStyles.miniButtonLeft,
                            GUILayout.Width(ButtonWidth), GUILayout.Height(HeaderHeight)))
                    {
                        arrayProp.MoveArrayElement(index, index - 1);
                    }
                }

                using (new EditorGUI.DisabledScope(index >= arrayProp.arraySize - 1))
                {
                    if (GUILayout.Button(
                            "\u25BC", EditorStyles.miniButtonRight,
                            GUILayout.Width(ButtonWidth), GUILayout.Height(HeaderHeight)))
                    {
                        arrayProp.MoveArrayElement(index, index + 1);
                    }
                }

                GUI.backgroundColor = DeleteRed;
                if (GUILayout.Button(
                        "\u2717", EditorStyles.miniButton,
                        GUILayout.Width(ButtonWidth), GUILayout.Height(HeaderHeight)))
                {
                    RemoveRhythmSubAsset(arrayProp, index);
                    deleted = true;
                }
                GUI.backgroundColor = Color.white;
            }
            return deleted;
        }

        private static void DrawPreviewButtons(HapticRhythm rhythm)
        {
            if (GUILayout.Button(
                    "\u25B6", EditorStyles.miniButton,
                    GUILayout.Width(ButtonWidth), GUILayout.Height(HeaderHeight)))
            {
                PreviewRhythm(rhythm);
            }

            if (GUILayout.Button(
                    "\u25A0", EditorStyles.miniButton,
                    GUILayout.Width(ButtonWidth), GUILayout.Height(HeaderHeight)))
            {
                if (EditorApplication.isPlaying)
                {
                    HapticManager.Instance?.StopAll();
                }
            }
        }

        private static void PreviewRhythm(HapticRhythm rhythm)
        {
            if (EditorApplication.isPlaying)
            {
                HapticManager.Instance?.PlayRhythm(rhythm);
                return;
            }

            // Edit-mode preview: simulate via QuickLog; never touches the runtime manager.
            QuickLog.Info<HapticSettingsProvider>(
                "[Preview] Rhythm '{0}' would fire {1} keyframe(s):",
                rhythm.DisplayLabel, rhythm.Keyframes.Length);

            for (int i = 0; i < rhythm.Keyframes.Length; i++)
            {
                HapticKeyframe kf = rhythm.Keyframes[i];
                QuickLog.Info<HapticSettingsProvider>(
                    "  t={0:F2}s  {1}  int={2:F2}  dur={3:F2}s",
                    kf.TimeSeconds, kf.Waveform, kf.Intensity, kf.DurationSeconds);
            }
        }

        private void DrawAddRhythmRow(SerializedProperty rhythmsProp)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        "+ Add Rhythm \u25BE",
                        GUILayout.Height(AddButtonHeight),
                        GUILayout.Width(AddButtonWidth)))
                {
                    ShowAddRhythmMenu(rhythmsProp);
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void ShowAddRhythmMenu(SerializedProperty rhythmsProp)
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(
                new GUIContent("Blank Rhythm"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.Custom));

            menu.AddSeparator("");

            menu.AddItem(
                new GUIContent("From Template/Light Tap"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.LightTap));
            menu.AddItem(
                new GUIContent("From Template/Medium Tap"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.MediumTap));
            menu.AddItem(
                new GUIContent("From Template/Heavy Tap"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.HeavyTap));
            menu.AddItem(
                new GUIContent("From Template/Alert Tap"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.AlertTap));
            menu.AddItem(
                new GUIContent("From Template/Selection Tick"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.SelectionTick));
            menu.AddItem(
                new GUIContent("From Template/Notification Success"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.NotificationSuccess));
            menu.AddItem(
                new GUIContent("From Template/Notification Warning"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.NotificationWarning));
            menu.AddItem(
                new GUIContent("From Template/Notification Error"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.NotificationError));
            menu.AddItem(
                new GUIContent("From Template/Double Tap"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.DoubleTap));
            menu.AddItem(
                new GUIContent("From Template/Triple Pulse"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.TriplePulse));
            menu.AddItem(
                new GUIContent("From Template/Burst"),
                false,
                () => AddRhythmSubAsset(rhythmsProp, HapticRhythmTemplate.Burst));

            menu.ShowAsContext();
        }

        #endregion

        #region Sub-Asset Helpers

        private void AddRhythmSubAsset(
            SerializedProperty arrayProp,
            HapticRhythmTemplate template)
        {
            var config = _serializedSettings.targetObject as HapticConfiguration;
            if (config == null) return;

            string assetPath = AssetDatabase.GetAssetPath(config);
            if (string.IsNullOrEmpty(assetPath)) return;

            string id = "rhythm_" + (arrayProp.arraySize + 1);
            HapticRhythm rhythm;

            if (template == HapticRhythmTemplate.Custom)
            {
                rhythm = ScriptableObject.CreateInstance<HapticRhythm>();
                rhythm.Id = id;
                rhythm.DisplayName = "Rhythm " + (arrayProp.arraySize + 1);
                rhythm.Keyframes = new HapticKeyframe[0];
                rhythm.RefreshDuration();
            }
            else
            {
                rhythm = HapticRhythmTemplates.CreateRhythm(
                    id, HapticRhythmTemplates.GetName(template), template);
            }

            rhythm.name = "HapticRhythm_" + id;
            AssetDatabase.AddObjectToAsset(rhythm, assetPath);
            AssetDatabase.SaveAssets();

            arrayProp.arraySize++;
            SerializedProperty element =
                arrayProp.GetArrayElementAtIndex(arrayProp.arraySize - 1);
            element.objectReferenceValue = rhythm;

            _serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        private void RemoveRhythmSubAsset(
            SerializedProperty arrayProp,
            int index)
        {
            SerializedProperty element = arrayProp.GetArrayElementAtIndex(index);
            var rhythm = element.objectReferenceValue as HapticRhythm;

            arrayProp.DeleteArrayElementAtIndex(index);
            CleanNullArrayEntries(arrayProp);
            _serializedSettings.ApplyModifiedProperties();

            if (rhythm != null)
            {
                _selectedKeyframeByRhythm.Remove(rhythm.Id);
                AssetDatabase.RemoveObjectFromAsset(rhythm);
                UnityEngine.Object.DestroyImmediate(rhythm, true);
                AssetDatabase.SaveAssets();
            }

            EditorUtility.SetDirty(_serializedSettings.targetObject);
        }

        private static void CleanNullArrayEntries(SerializedProperty arrayProp)
        {
            for (int i = arrayProp.arraySize - 1; i >= 0; i--)
            {
                if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    arrayProp.DeleteArrayElementAtIndex(i);
                }
            }
        }

        #endregion

        #region Provider Helpers

        private static Type[] GetCachedProviderTypes(Type interfaceType)
        {
            _cachedProviderTypes ??= ScanProviderTypes(interfaceType);
            return _cachedProviderTypes;
        }

        private static Type[] ScanProviderTypes(Type interfaceType)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(t =>
                    t.IsClass && !t.IsAbstract &&
                    typeof(ScriptableObject).IsAssignableFrom(t) &&
                    interfaceType.IsAssignableFrom(t))
                .OrderBy(t => t.FullName)
                .ToArray();
        }

        private ScriptableObject FindOrCreateProviderAsset(Type providerType)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + providerType.Name);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath(path, providerType) as ScriptableObject;
                if (asset != null) return asset;
            }

            var newAsset = ScriptableObject.CreateInstance(providerType);
            newAsset.name = providerType.Name;

            if (!Directory.Exists(ProviderAssetFolder))
            {
                Directory.CreateDirectory(ProviderAssetFolder);
            }

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(ProviderAssetFolder, providerType.Name + ".asset"));
            AssetDatabase.CreateAsset(newAsset, assetPath);
            AssetDatabase.SaveAssets();

            return newAsset;
        }

        private void DeleteAssetAndClearField(
            ScriptableObject asset,
            SerializedProperty prop)
        {
            if (asset == null) return;

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath)) return;

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Provider Asset",
                "Delete '" + asset.name + "' at '" + assetPath + "'?\n\n"
                + "This cannot be undone.",
                "Delete", "Cancel");

            if (!confirmed) return;

            prop.objectReferenceValue = null;
            _serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(_serializedSettings.targetObject);

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.SaveAssets();
            DestroyInlineEditors();
        }

        #endregion

        #region Android Manifest Helpers

        private static bool ManifestHasVibratePermission()
        {
            if (!File.Exists(AndroidManifestPath)) return false;

            try
            {
                string text = File.ReadAllText(AndroidManifestPath);
                return text.Contains(VibratePermission);
            }
            catch (Exception ex)
            {
                QuickLog.Error<HapticSettingsProvider>(
                    "Failed to read AndroidManifest.xml: {0}", ex.Message);
                return false;
            }
        }

        private static void AddVibratePermissionToManifest()
        {
            string folder = Path.GetDirectoryName(AndroidManifestPath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            const string usesPermission =
                "<uses-permission android:name=\"android.permission.VIBRATE\"/>";

            try
            {
                if (!File.Exists(AndroidManifestPath))
                {
                    CreateManifestWithPermission(usesPermission);
                    return;
                }

                string text = File.ReadAllText(AndroidManifestPath);
                if (text.Contains(VibratePermission))
                {
                    QuickLog.Info<HapticSettingsProvider>(
                        "VIBRATE permission already present in AndroidManifest.xml.");
                    return;
                }

                int manifestIdx = text.IndexOf("<manifest", StringComparison.Ordinal);
                if (manifestIdx < 0)
                {
                    QuickLog.Error<HapticSettingsProvider>(
                        "AndroidManifest.xml has no <manifest> tag; cannot inject permission.");
                    return;
                }

                int insertIdx = text.IndexOf('>', manifestIdx);
                if (insertIdx < 0)
                {
                    QuickLog.Error<HapticSettingsProvider>(
                        "AndroidManifest.xml <manifest> tag is malformed.");
                    return;
                }

                insertIdx++;
                text = text.Insert(insertIdx, "\n    " + usesPermission);
                File.WriteAllText(AndroidManifestPath, text);

                AssetDatabase.Refresh();
                QuickLog.Info<HapticSettingsProvider>(
                    "Injected VIBRATE permission into '{0}'.", AndroidManifestPath);
            }
            catch (Exception ex)
            {
                QuickLog.Error<HapticSettingsProvider>(
                    "Failed to add VIBRATE permission: {0}", ex.Message);
            }
        }

        private static void CreateManifestWithPermission(string usesPermission)
        {
            string content =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\">\n"
                + "    " + usesPermission + "\n"
                + "</manifest>\n";

            File.WriteAllText(AndroidManifestPath, content);
            AssetDatabase.Refresh();
            QuickLog.Info<HapticSettingsProvider>(
                "Created '{0}' with the VIBRATE permission.", AndroidManifestPath);
        }

        #endregion

        #region Asset Helpers

        private static HapticConfiguration GetOrCreateSettings()
        {
            HapticConfiguration settings = HapticConfiguration.Instance;
            if (settings != null) return settings;

            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "No Haptic Configuration asset found. "
                + "Click the button below to create one.",
                MessageType.Info);

            if (GUILayout.Button(
                    "Create Haptic Configuration",
                    GUILayout.Height(30)))
            {
                HapticConfiguration.CreateOrMoveToDesignatedPath();
            }

            return null;
        }

        private void EnsureSerializedObject(HapticConfiguration settings)
        {
            if (_serializedSettings != null
                && _serializedSettings.targetObject == settings)
            {
                return;
            }

            DestroyInlineEditors();
            _serializedSettings = new SerializedObject(settings);
        }

        #endregion

        #region Inline Inspector

        private void DrawInlineInspectorFor(ScriptableObject target)
        {
            if (target == null) return;

            _inlineEditorCache ??=
                new Dictionary<ScriptableObject, UnityEditor.Editor>();

            if (!_inlineEditorCache.TryGetValue(target, out UnityEditor.Editor editor))
            {
                editor = UnityEditor.Editor.CreateEditor(target);
                _inlineEditorCache[target] = editor;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                editor.OnInspectorGUI();
            }
        }

        private void DestroyInlineEditors()
        {
            if (_inlineEditorCache == null) return;

            foreach (var editor in _inlineEditorCache.Values)
            {
                if (editor != null)
                {
                    UnityEngine.Object.DestroyImmediate(editor);
                }
            }

            _inlineEditorCache.Clear();
        }

        #endregion

        #region Drawing Utilities

        private static void DrawPropFrom(
            SerializedObject so,
            string fieldName,
            string label)
        {
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) return;
            EditorGUILayout.PropertyField(prop, new GUIContent(label));
        }

        private static bool DrawDeleteButton()
        {
            var content = EditorGUIUtility.IconContent("TreeEditor.Trash");
            if (content == null || content.image == null)
            {
                content = new GUIContent("\u2717", "Delete");
            }

            content.tooltip = "Delete the provider asset from disk.";
            return GUILayout.Button(
                content,
                EditorStyles.miniButton,
                GUILayout.Width(ButtonWidth),
                GUILayout.Height(HeaderHeight));
        }

        #endregion
    }

    /// <summary>
    /// [InitializeOnLoad] sanity checker — scans HapticConfiguration on domain
    /// load and logs duplicate ids, invalid keyframes, and missing providers.
    /// </summary>
    [InitializeOnLoad]
    internal static class HapticConfigurationSanityChecker
    {
        #region Constructor

        static HapticConfigurationSanityChecker()
        {
            EditorApplication.delayCall += RunCheck;
        }

        #endregion

        #region Private Methods

        private static void RunCheck()
        {
            EditorApplication.delayCall -= RunCheck;

            HapticConfiguration config = HapticConfiguration.Instance;
            if (config == null) return;

            CheckDuplicateIds(config);
            config.ValidateAll();

            if (!config.HasAnyProvider)
            {
                QuickLog.Warning<HapticSettingsProvider>(
                    "No haptic provider configured (Android or iOS). "
                    + "Haptics will no-op at runtime.");
            }
        }

        private static void CheckDuplicateIds(HapticConfiguration config)
        {
            HashSet<string> seen = new HashSet<string>();
            HapticRhythm[] rhythms = config.Rhythms;

            for (int i = 0; i < rhythms.Length; i++)
            {
                HapticRhythm rhythm = rhythms[i];
                if (rhythm == null || string.IsNullOrEmpty(rhythm.Id)) continue;

                if (!seen.Add(rhythm.Id))
                {
                    QuickLog.Warning<HapticSettingsProvider>(
                        "Duplicate rhythm id '{0}' at index {1}.",
                        rhythm.Id, i);
                }
            }
        }

        #endregion
    }
}
