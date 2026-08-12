using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Sound;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Sound.Editor
{
    internal sealed class SoundSettingsProvider : SettingsProvider
    {
        #region Constants

        private const string SettingsAssetPath =
            "Assets/Resources/Integration/Managers/SoundConfiguration.asset";

        private const string ProviderAssetFolder =
            "Assets/Resources/Integration/Sound/Providers";

        private const string TabPrefKey =
            "SoundSettingsProvider_SelectedTab";

        private const string SearchPrefKey =
            "SoundSettingsProvider_Search";

        private const int SfxBadgeWidth = 34;
        private const int BgmBadgeWidth = 40;
        private const int ButtonWidth = 22;
        private const int HeaderHeight = 18;
        private const int DividerHeight = 1;
        private const int SmallGap = 4;
        private const int AddButtonHeight = 26;
        private const int AddButtonWidth = 160;
        private const int ProviderEnableWidth = 110;
        private const int BusActiveWidth = 90;
        private const int BusFilterAllWidth = 40;
        private const int BusFilterSfxWidth = 48;
        private const int BusFilterBgmWidth = 52;
        private const int SearchLabelWidth = 50;
        private const int BusLabelWidth = 30;
        private const int SpaceGap = 8;

        private static readonly string[] TabNames = { "Providers", "Sounds" };

        private static readonly Color BadgeSfx = new Color(0.35f, 0.45f, 0.75f);
        private static readonly Color BadgeBgm = new Color(0.65f, 0.35f, 0.8f);
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
        private Vector2 _scrollPosition;
        private Vector2 _providerScrollPosition;
        private Vector2 _soundScrollPosition;

        private string _searchText = string.Empty;
        private int _busFilter; // 0 = All, 1 = SFX, 2 = BGM

        private Dictionary<ScriptableObject, UnityEditor.Editor> _inlineEditorCache;

        private static Type[] _cachedProviderTypes;
        private static AudioSource _previewSource;

        #endregion

        #region Constructor

        private SoundSettingsProvider(
            string path,
            SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        {
            _selectedTabIndex = EditorPrefs.GetInt(TabPrefKey, 0);
            _searchText = EditorPrefs.GetString(SearchPrefKey, string.Empty);
        }

        #endregion

        #region SettingsProvider Registration

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SoundSettingsProvider(
                "Project/Frameworks/Sounds",
                SettingsScope.Project,
                new[] { "sound", "sfx", "bgm", "music", "audio", "volume" }
            );
        }

        #endregion

        #region GUI

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            SoundConfiguration settings = GetOrCreateSettings();
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

            Rect dividerRect = EditorGUILayout.GetControlRect(false, DividerHeight);
            EditorGUI.DrawRect(dividerRect, DividerColor);
            EditorGUILayout.Space();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_selectedTabIndex)
            {
                case 0: DrawProvidersTab(); break;
                case 1: DrawSoundsTab(); break;
            }

            EditorGUILayout.EndScrollView();
            _serializedSettings.ApplyModifiedProperties();
        }

        public override void OnDeactivate()
        {
            base.OnDeactivate();
            EditorPrefs.SetInt(TabPrefKey, _selectedTabIndex);
            EditorPrefs.SetString(SearchPrefKey, _searchText);
            DestroyInlineEditors();
            DestroyPreviewSource();
            _cachedProviderTypes = null;
        }

        #endregion

        #region Status Bar

        private void DrawStatusBar(SoundConfiguration config)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int sfxCount = config.Sounds.Count(s => s != null && s.IsSfx);
                int bgmCount = config.Sounds.Count(s => s != null && s.IsBgm);
                int total = sfxCount + bgmCount;
                bool hasSfxProvider = config.SfxProvider != null;
                bool hasBgmProvider = config.BgmProvider != null;
                bool valid = hasSfxProvider && hasBgmProvider && total > 0;

                Color statusColor = valid ? Color.green : Color.yellow;
                string statusText = valid ? "Ready" : "Incomplete";

                var style = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = statusColor }
                };

                EditorGUILayout.LabelField(
                    "Status: " + statusText + "  |  "
                    + total + " sounds (" + sfxCount + " SFX / " + bgmCount + " BGM)  |  "
                    + "SFX: " + (hasSfxProvider ? "ok" : "none") + "  "
                    + "BGM: " + (hasBgmProvider ? "ok" : "none"),
                    style
                );
            }
        }

        #endregion

        #region Tab - Providers

        private void DrawProvidersTab()
        {
            EditorGUILayout.HelpBox(
                "Select the player provider for each bus. SFX uses the pooled "
                + "player provider (disable = stop); BGM can share it or use a "
                + "dedicated provider.",
                MessageType.None);
            EditorGUILayout.Space();

            DrawProviderSlot("_sfxProvider", "SFX Provider", "SFX");
            EditorGUILayout.Space();
            DrawProviderSlot("_bgmProvider", "BGM Provider", "BGM");
        }

        private void DrawProviderSlot(
            string fieldName,
            string slotTitle,
            string busLabel)
        {
            EditorGUILayout.LabelField(slotTitle, EditorStyles.boldLabel);

            SerializedProperty providerProp =
                _serializedSettings.FindProperty(fieldName);
            if (providerProp == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find '" + fieldName + "' field.",
                    MessageType.Error);
                return;
            }

            Type[] providerTypes =
                GetCachedProviderTypes(typeof(ISoundPlayerProvider));
            if (providerTypes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No ScriptableObject types implementing "
                    + "ISoundPlayerProvider found.",
                    MessageType.Info);
                return;
            }

            ScriptableObject currentProvider =
                providerProp.objectReferenceValue as ScriptableObject;
            Type currentType = currentProvider != null ? currentProvider.GetType() : null;

            _providerScrollPosition =
                EditorGUILayout.BeginScrollView(_providerScrollPosition);

            foreach (Type providerType in providerTypes)
            {
                bool isActive = currentType != null
                    && providerType.IsAssignableFrom(currentType);

                DrawProviderCard(
                    providerType,
                    busLabel,
                    isActive,
                    currentProvider,
                    providerProp);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawProviderCard(
            Type providerType,
            string busLabel,
            bool isActive,
            ScriptableObject currentProvider,
            SerializedProperty prop)
        {
            string displayName =
                ObjectNames.NicifyVariableName(providerType.Name);

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
                            busLabel + " ACTIVE",
                            EditorStyles.miniButton,
                            GUILayout.Width(BusActiveWidth));
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
                                "Enable " + busLabel,
                                GUILayout.Width(ProviderEnableWidth)))
                        {
                            ScriptableObject asset =
                                FindOrCreateProviderAsset(providerType);
                            prop.objectReferenceValue = asset;
                            _serializedSettings.ApplyModifiedProperties();
                            EditorUtility.SetDirty(
                                _serializedSettings.targetObject);
                            AssetDatabase.SaveAssets();
                            DestroyInlineEditors();
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }

                if (isActive && currentProvider != null)
                {
                    GUILayout.Space(SmallGap);
                    DrawInlineInspectorFor(currentProvider);
                }
            }
        }

        #endregion

        #region Tab - Sounds

        private void DrawSoundsTab()
        {
            EditorGUILayout.HelpBox(
                "Define sounds. SFX plays through the pooled player provider; "
                + "BGM is the looping music track.",
                MessageType.None);

            DrawSoundToolbar();

            SerializedProperty soundsProp =
                _serializedSettings.FindProperty("_sounds");
            if (soundsProp == null || !soundsProp.isArray)
            {
                EditorGUILayout.HelpBox(
                    "Could not find '_sounds' field.",
                    MessageType.Error);
                return;
            }

            int visibleCount = 0;
            _soundScrollPosition =
                EditorGUILayout.BeginScrollView(_soundScrollPosition);

            for (int i = 0; i < soundsProp.arraySize; i++)
            {
                SerializedProperty element =
                    soundsProp.GetArrayElementAtIndex(i);
                var def = element.objectReferenceValue as SoundDefinition;
                if (def == null) continue;

                if (!MatchesFilter(def)) continue;

                visibleCount++;
                if (DrawSoundCard(soundsProp, i, def))
                {
                    // The definition was destroyed; the array shifted left.
                    // Reprocess this index to avoid skipping the next card.
                    i--;
                }
            }

            if (visibleCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "No sounds match the current search/filter.",
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        "+ Add Sound",
                        GUILayout.Height(AddButtonHeight),
                        GUILayout.Width(AddButtonWidth)))
                {
                    AddSoundSubAsset(soundsProp);
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawSoundToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Search", GUILayout.Width(SearchLabelWidth));
                _searchText = EditorGUILayout.TextField(_searchText);

                EditorGUILayout.Space(SpaceGap);

                EditorGUILayout.LabelField("Bus", GUILayout.Width(BusLabelWidth));

                int newFilter = _busFilter;
                if (DrawFilterButton("All", 0, newFilter, BusFilterAllWidth))
                {
                    newFilter = 0;
                }
                if (DrawFilterButton("SFX", 1, newFilter, BusFilterSfxWidth))
                {
                    newFilter = 1;
                }
                if (DrawFilterButton("BGM", 2, newFilter, BusFilterBgmWidth))
                {
                    newFilter = 2;
                }
                _busFilter = newFilter;
            }
        }

        private static bool DrawFilterButton(
            string label,
            int filterValue,
            int currentFilter,
            int width)
        {
            bool isActive = currentFilter == filterValue;
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

        private bool MatchesFilter(SoundDefinition def)
        {
            if (_busFilter == 1 && !def.IsSfx) return false;
            if (_busFilter == 2 && !def.IsBgm) return false;

            if (string.IsNullOrEmpty(_searchText)) return true;

            string query = _searchText.Trim();
            if (query.Length == 0) return true;

            if (def.Id != null
                && def.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (def.DisplayName != null
                && def.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return false;
        }

        /// <returns>True when the definition was deleted by the header (caller must
        /// stop using the definition / SerializedObject immediately).</returns>
        private bool DrawSoundCard(
            SerializedProperty arrayProp,
            int index,
            SoundDefinition def)
        {
            SerializedObject defSo = new SerializedObject(def);
            defSo.Update();

            string title = def.DisplayLabel;
            if (string.IsNullOrEmpty(title))
            {
                title = "Sound " + (index + 1);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (DrawSoundCardHeader(arrayProp, index, def, title))
                {
                    return true;
                }

                Rect sep = EditorGUILayout.GetControlRect(false, DividerHeight);
                EditorGUI.DrawRect(sep, CardDividerColor);
                GUILayout.Space(SmallGap);

                DrawSoundFields(def, defSo);
                defSo.ApplyModifiedProperties();
            }
            return false;
        }

        /// <returns>True when the delete button destroyed the definition.</returns>
        private bool DrawSoundCardHeader(
            SerializedProperty arrayProp,
            int index,
            SoundDefinition def,
            string title)
        {
            bool deleted = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                Color badgeColor = def.IsSfx ? BadgeSfx : BadgeBgm;
                string badge = def.IsSfx ? "SFX" : "BGM";
                int badgeWidth = def.IsSfx ? SfxBadgeWidth : BgmBadgeWidth;

                GUI.backgroundColor = badgeColor;
                GUILayout.Label(
                    badge, EditorStyles.miniButton,
                    GUILayout.Width(badgeWidth));
                GUI.backgroundColor = Color.white;

                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                DrawPreviewButtons(def);

                using (new EditorGUI.DisabledScope(index == 0))
                {
                    if (GUILayout.Button(
                            "\u25B2", EditorStyles.miniButtonLeft,
                            GUILayout.Width(ButtonWidth),
                            GUILayout.Height(HeaderHeight)))
                    {
                        arrayProp.MoveArrayElement(index, index - 1);
                    }
                }

                using (new EditorGUI.DisabledScope(
                           index >= arrayProp.arraySize - 1))
                {
                    if (GUILayout.Button(
                            "\u25BC", EditorStyles.miniButtonRight,
                            GUILayout.Width(ButtonWidth),
                            GUILayout.Height(HeaderHeight)))
                    {
                        arrayProp.MoveArrayElement(index, index + 1);
                    }
                }

                GUI.backgroundColor = DeleteRed;
                if (GUILayout.Button(
                        "\u2717", EditorStyles.miniButton,
                        GUILayout.Width(ButtonWidth),
                        GUILayout.Height(HeaderHeight)))
                {
                    RemoveSoundSubAsset(arrayProp, index);
                    deleted = true;
                }
                GUI.backgroundColor = Color.white;
            }
            return deleted;
        }

        private static void DrawSoundFields(
            SoundDefinition def,
            SerializedObject defSo)
        {
            DrawPropFrom(defSo, "_id", "ID");
            DrawPropFrom(defSo, "_displayName", "Display Name");
            DrawPropFrom(defSo, "_clip", "Clip");

            SerializedProperty busProp = defSo.FindProperty("_bus");
            using (var change = new EditorGUI.ChangeCheckScope())
            {
                DrawPropFrom(defSo, "_bus", "Bus");
                if (change.changed && busProp != null)
                {
                    defSo.ApplyModifiedProperties();
                    defSo.Update();
                }
            }

            if (def.IsBgm)
            {
                EditorGUILayout.LabelField("BGM Settings", EditorStyles.miniBoldLabel);
                DrawPropFrom(defSo, "_loop", "Loop");
                DrawPropFrom(defSo, "_fadeInSeconds", "Fade In (s)");
                DrawPropFrom(defSo, "_fadeOutSeconds", "Fade Out (s)");
            }
            else
            {
                DrawPropFrom(defSo, "_volume", "Volume");
                DrawPropFrom(defSo, "_pitch", "Pitch");
                DrawPropFrom(defSo, "_loop", "Loop");
                DrawPropFrom(defSo, "_priority", "Priority");
                DrawPropFrom(defSo, "_spatialBlend", "Spatial Blend");
            }
        }

        private static void DrawPreviewButtons(SoundDefinition def)
        {
            if (GUILayout.Button(
                    "\u25B6", EditorStyles.miniButton,
                    GUILayout.Width(ButtonWidth),
                    GUILayout.Height(HeaderHeight)))
            {
                PreviewSound(def);
            }

            if (GUILayout.Button(
                    "\u25A0", EditorStyles.miniButton,
                    GUILayout.Width(ButtonWidth),
                    GUILayout.Height(HeaderHeight)))
            {
                StopPreview();
            }
        }

        private static void PreviewSound(SoundDefinition def)
        {
            if (def == null || def.Clip == null) return;

            if (EditorApplication.isPlaying)
            {
                if (def.IsBgm)
                {
                    SoundManager.Instance?.PlayBgm(def);
                }
                else
                {
                    SoundManager.Instance?.PlaySfx(def);
                }
                return;
            }

            StopPreview();
            EnsurePreviewSource();

            _previewSource.clip = def.Clip;
            _previewSource.volume = def.Volume;
            _previewSource.pitch = def.Pitch;
            _previewSource.loop = def.Loop;
            _previewSource.Play();
        }

        private static void StopPreview()
        {
            if (_previewSource != null)
            {
                _previewSource.Stop();
                _previewSource.clip = null;
            }
        }

        private static void EnsurePreviewSource()
        {
            if (_previewSource != null) return;

            GameObject go = new GameObject("[Sound Preview]");
            go.hideFlags = HideFlags.HideAndDontSave;
            _previewSource = go.AddComponent<AudioSource>();
            _previewSource.playOnAwake = false;
        }

        private static void DestroyPreviewSource()
        {
            if (_previewSource == null) return;
            UnityEngine.Object.DestroyImmediate(_previewSource.gameObject);
            _previewSource = null;
        }

        private static void DrawPropFrom(
            SerializedObject so,
            string fieldName,
            string label)
        {
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) return;
            EditorGUILayout.PropertyField(prop, new GUIContent(label));
        }

        #endregion

        #region Sub-Asset Helpers

        private void AddSoundSubAsset(SerializedProperty arrayProp)
        {
            var config =
                _serializedSettings.targetObject as SoundConfiguration;
            if (config == null) return;

            string assetPath = AssetDatabase.GetAssetPath(config);
            if (string.IsNullOrEmpty(assetPath)) return;

            var def = ScriptableObject.CreateInstance<SoundDefinition>();
            def.name = "Sound_" + (arrayProp.arraySize + 1);

            AssetDatabase.AddObjectToAsset(def, assetPath);
            AssetDatabase.SaveAssets();

            arrayProp.arraySize++;
            SerializedProperty element =
                arrayProp.GetArrayElementAtIndex(arrayProp.arraySize - 1);
            element.objectReferenceValue = def;

            _serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        private void RemoveSoundSubAsset(
            SerializedProperty arrayProp,
            int index)
        {
            SerializedProperty element =
                arrayProp.GetArrayElementAtIndex(index);
            var def = element.objectReferenceValue as SoundDefinition;

            arrayProp.DeleteArrayElementAtIndex(index);
            CleanNullArrayEntries(arrayProp);
            _serializedSettings.ApplyModifiedProperties();

            if (def != null)
            {
                AssetDatabase.RemoveObjectFromAsset(def);
                UnityEngine.Object.DestroyImmediate(def, true);
                AssetDatabase.SaveAssets();
            }

            EditorUtility.SetDirty(_serializedSettings.targetObject);
        }

        private static void CleanNullArrayEntries(SerializedProperty arrayProp)
        {
            for (int i = arrayProp.arraySize - 1; i >= 0; i--)
            {
                if (arrayProp.GetArrayElementAtIndex(i)
                        .objectReferenceValue == null)
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
            string[] guids =
                AssetDatabase.FindAssets("t:" + providerType.Name);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath(
                    path, providerType) as ScriptableObject;
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

        #region Asset Helpers

        private static SoundConfiguration GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SoundConfiguration>(
                SettingsAssetPath);
            if (settings != null) return settings;

            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "No Sound Configuration asset found. "
                + "Click the button below to create one.",
                MessageType.Info);

            if (GUILayout.Button(
                    "Create Sound Configuration",
                    GUILayout.Height(30)))
            {
                CreateSettingsAsset();
            }

            return null;
        }

        private static void CreateSettingsAsset()
        {
            string folder = Path.GetDirectoryName(SettingsAssetPath);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var settings =
                ScriptableObject.CreateInstance<SoundConfiguration>();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();

            QuickLog.Info<SoundSettingsProvider>(
                "Created configuration at '{0}'.", SettingsAssetPath);
        }

        private void EnsureSerializedObject(SoundConfiguration settings)
        {
            if (_serializedSettings != null
                && _serializedSettings.targetObject == settings)
                return;

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

            if (!_inlineEditorCache.TryGetValue(
                    target, out UnityEditor.Editor editor))
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
                    UnityEngine.Object.DestroyImmediate(editor);
            }

            _inlineEditorCache.Clear();
        }

        #endregion

        #region Drawing Utilities

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
}
