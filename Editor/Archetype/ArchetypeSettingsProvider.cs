using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Com.Hapiga.Scheherazade.Common.Archetype;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Archetype.Editor
{
    internal sealed class ArchetypeSettingsProvider : SettingsProvider
    {
        #region Constants
        private static readonly string[] TabNames =
            { "Configuration", "Archetypes", "Cache & Status" };
        #endregion

        #region Private Fields
        private SerializedObject _serializedSettings;
        private int _selectedTabIndex;
        private Vector2 _scrollPosition;
        private Vector2 _cacheScrollPosition;
        private GUIStyle _statusLabelStyle;
        #endregion

        #region Constructor
        private ArchetypeSettingsProvider(
            string path,
            SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords) { }
        #endregion

        #region SettingsProvider Registration
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new ArchetypeSettingsProvider(
                "Project/Tools/Archetype",
                SettingsScope.Project,
                new[]
                {
                    "archetype", "singleton", "global",
                    "reference", "registry", "generator"
                }
            );
        }
        #endregion

        #region GUI
        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            ArchetypeSettings settings = GetOrCreateSettings();
            if (settings == null)
            {
                return;
            }

            EnsureSerializedObject(settings);
            _serializedSettings.Update();

            GUILayout.Space(4);
            EditorGUILayout.LabelField(
                "Archetype Configuration",
                EditorStyles.boldLabel);
            GUILayout.Space(4);

            _selectedTabIndex = GUILayout.Toolbar(
                _selectedTabIndex,
                TabNames);
            GUILayout.Space(4);

            Rect dividerRect = EditorGUILayout.GetControlRect(
                false, 1f);
            EditorGUI.DrawRect(
                dividerRect,
                new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);

            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition);

            switch (_selectedTabIndex)
            {
                case 0:
                    DrawConfigTab(settings);
                    break;
                case 1:
                    DrawPreviewTab();
                    break;
                case 2:
                    DrawCacheTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
            _serializedSettings.ApplyModifiedProperties();
        }
        #endregion

        #region Tab — Configuration
        private void DrawConfigTab(ArchetypeSettings settings)
        {
            EditorGUILayout.HelpBox(
                "Configure archetype generation paths and behavior.",
                MessageType.None);
            GUILayout.Space(4);

            // Config asset path (read-only)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    "Config Asset",
                    GUILayout.Width(EditorGUIUtility.labelWidth));
                EditorGUILayout.LabelField(
                    ArchetypeSettings.AssetPath,
                    EditorStyles.miniLabel);
            }

            GUILayout.Space(6);

            // General Settings
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("General Settings", EditorStyles.miniBoldLabel);

                DrawSerializedProperty("generatedFolder", "Generated Folder");
                DrawSerializedProperty("autoGenerateOnReload", "Auto-Generate on Reload");
                DrawSerializedProperty("namespaceOverride", "Namespace Override");
            }

            GUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Output", EditorStyles.miniBoldLabel);

                DrawSerializedProperty("managedOutputFolder", "Managed DLL Output Folder");

                bool hasOutput = !string.IsNullOrEmpty(settings.managedOutputFolder);

                using (new EditorGUI.DisabledScope(!hasOutput))
                {
                    if (GUILayout.Button("Build Plugin DLL", GUILayout.Height(24)))
                    {
                        BuildPluginDll(settings);
                    }
                }
            }

            GUILayout.Space(8);
            Rect divider = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(
                divider, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);

            if (GUILayout.Button(
                    "Reset Settings to Default",
                    GUILayout.Height(22)))
            {
                if (EditorUtility.DisplayDialog(
                        "Reset Settings",
                        "Reset all archetype settings to defaults?",
                        "Reset",
                        "Cancel"))
                {
                    ResetSettings(settings);
                }
            }
        }
        #endregion

        #region Tab — Preview
        private void DrawPreviewTab()
        {
            List<ArchetypeCacheEntry> entries
                = ArchetypeScanner.ScanCurrentEntries();
            ArchetypeCache cache = ArchetypeHashCache.Load();

            string currentHash
                = ArchetypeHashCache.ComputeHash(entries);
            bool upToDate = cache != null
                && cache.hash == currentHash
                && AllFilesExist(cache.files);

            int uniqueArchetypes = CountUniqueArchetypes(entries);

            // Status bar
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string statusText = upToDate
                    ? "UP TO DATE"
                    : "NEEDS REGENERATION";
                Color statusColor = upToDate
                    ? new Color(0.3f, 0.8f, 0.3f)
                    : new Color(0.9f, 0.7f, 0.2f);

                EditorGUILayout.LabelField(
                    $"Status: {statusText}  |  "
                    + $"{uniqueArchetypes} archetypes, "
                    + $"{entries.Count} types registered",
                    new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = statusColor }
                    });

                GUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!upToDate && GUILayout.Button(
                            "Force Regenerate",
                            GUILayout.Height(22)))
                    {
                        ArchetypeScanner.ForceRegenerate();
                    }

                    if (GUILayout.Button(
                            "Refresh",
                            GUILayout.Width(70),
                            GUILayout.Height(22)))
                    {
                        // invalidate cached scan
                        _cachedPreviewEntries = null;
                    }
                }
            }

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No [Archetype] attributes found in the project. "
                    + "Add [Archetype(typeof(IMyInterface), "
                    + "\"ArchetypeName\", \"ArchetypeField\")] to "
                    + "a singleton class to create an archetype.",
                    MessageType.Info);
                return;
            }

            GUILayout.Space(6);

            // Group entries by ArchetypeName
            Dictionary<string, List<ArchetypeCacheEntry>> grouped
                = GroupByArchetype(entries);

            _previewScrollPosition = EditorGUILayout.BeginScrollView(
                _previewScrollPosition);

            foreach (KeyValuePair<string, List<ArchetypeCacheEntry>> kvp
                in grouped)
            {
                DrawArchetypeCard(kvp.Key, kvp.Value, cache);
                GUILayout.Space(6);
            }

            EditorGUILayout.EndScrollView();

            GUILayout.Space(8);
            Rect div = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(
                div, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);

            if (GUILayout.Button(
                    "Export Summary as JSON",
                    GUILayout.Height(22)))
            {
                ExportSummaryJson(entries);
            }
        }

        private List<ArchetypeCacheEntry> _cachedPreviewEntries;
        private Vector2 _previewScrollPosition;

        private void DrawArchetypeCard(
            string archetypeName,
            List<ArchetypeCacheEntry> entries,
            ArchetypeCache cache)
        {
            ArchetypeCacheEntry first = entries[0];
            ArchetypeSettings settings = GetOrCreateSettings();
            string expectedFilePath = settings.FullGeneratedFolder
                + "/" + archetypeName + ".g.cs";
            bool isUpToDate = cache != null
                && cache.files != null
                && cache.files.Contains(expectedFilePath);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Header row with badge
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawStateBadge(
                        isUpToDate
                            ? "UP TO DATE"
                            : "STALE",
                        isUpToDate
                            ? new Color(0.2f, 0.7f, 0.2f)
                            : new Color(0.8f, 0.7f, 0.2f));

                    EditorGUILayout.LabelField(
                        archetypeName,
                        EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(
                            "Navigate",
                            EditorStyles.miniButton,
                            GUILayout.Width(70)))
                    {
                        PingGeneratedFile(archetypeName);
                    }
                }

                GUILayout.Space(2);

                // Details
                using (new EditorGUI.IndentLevelScope())
                {
                    string interfaceName = ExtractShortName(
                        first.interfaceTypeFullName);
                    EditorGUILayout.LabelField(
                        "Interface:",
                        interfaceName,
                        EditorStyles.miniLabel);

                    EditorGUILayout.LabelField(
                        "Property:",
                        $"{first.archetypeField} {{ get; private set; }}",
                        EditorStyles.miniLabel);
                }

                // Separator
                Rect sep = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(
                    sep, new Color(0.5f, 0.5f, 0.5f, 0.2f));
                GUILayout.Space(2);

                // Registered types
                EditorGUILayout.LabelField(
                    "Registered Types",
                    EditorStyles.miniBoldLabel);

                for (int i = 0; i < entries.Count; i++)
                {
                    ArchetypeCacheEntry entry = entries[i];
                    bool typeFound = Type.GetType(
                        entry.concreteTypeFullName) != null;

                    string displayName = ExtractShortName(
                        entry.concreteTypeFullName);

                    Color iconColor = typeFound
                        ? Color.green
                        : Color.red;
                    string icon = typeFound ? "\u2713" : "\u2717";

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            icon,
                            new GUIStyle(EditorStyles.label)
                            {
                                normal = { textColor = iconColor }
                            },
                            GUILayout.Width(18));

                        EditorGUILayout.LabelField(
                            displayName,
                            EditorStyles.label);

                        if (!typeFound)
                        {
                            EditorGUILayout.LabelField(
                                "(type not found)",
                                EditorStyles.miniLabel);
                        }
                    }
                }
            }
        }
        #endregion

        #region Tab — Cache & Status
        private void DrawCacheTab()
        {
            ArchetypeCache cache = ArchetypeHashCache.Load();
            List<ArchetypeCacheEntry> entries
                = ArchetypeScanner.ScanCurrentEntries();
            string currentHash
                = ArchetypeHashCache.ComputeHash(entries);

            // Cache info box
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Cache Info",
                    EditorStyles.miniBoldLabel);
                GUILayout.Space(2);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Cache File:",
                        GUILayout.Width(100));
                    EditorGUILayout.LabelField(
                        "Library/ScheherazadeArchetypeCache/cache.json",
                        EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Current Hash:",
                        GUILayout.Width(100));
                    EditorGUILayout.LabelField(
                        currentHash.Length > 16
                            ? currentHash.Substring(0, 16) + "..."
                            : currentHash,
                        EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Last Generated:",
                        GUILayout.Width(100));
                    EditorGUILayout.LabelField(
                        cache?.generatedAt ?? "(never)",
                        EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Generated Files:",
                        GUILayout.Width(100));
                    EditorGUILayout.LabelField(
                        (cache?.files?.Count ?? 0).ToString(),
                        EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Registered Types:",
                        GUILayout.Width(100));
                    EditorGUILayout.LabelField(
                        entries.Count.ToString(),
                        EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Status:",
                        GUILayout.Width(100));

                    string statusText;
                    Color statusColor;

                    if (cache == null)
                    {
                        statusText = "\u26A0 No Cache";
                        statusColor = new Color(0.9f, 0.6f, 0.1f);
                    }
                    else if (cache.hash == currentHash)
                    {
                        statusText = "\u2713 Generation OK";
                        statusColor = Color.green;
                    }
                    else
                    {
                        statusText = "\u26A0 Hash Mismatch";
                        statusColor = new Color(0.9f, 0.7f, 0.2f);
                    }

                    EditorGUILayout.LabelField(
                        statusText,
                        new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal = { textColor = statusColor }
                        });
                }
            }

            GUILayout.Space(6);

            // Generated files list
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Generated Files",
                    EditorStyles.miniBoldLabel);

                if (cache == null
                    || cache.files == null
                    || cache.files.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No generated files found.",
                        MessageType.Info);
                }
                else
                {
                    _cacheScrollPosition = EditorGUILayout.BeginScrollView(
                        _cacheScrollPosition,
                        GUILayout.Height(100));

                    for (int i = 0; i < cache.files.Count; i++)
                    {
                        string file = cache.files[i];
                        bool exists = File.Exists(file);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            string icon = exists ? "\u2713" : "\u2717";
                            Color iconColor = exists
                                ? Color.green
                                : Color.red;

                            EditorGUILayout.LabelField(
                                icon,
                                new GUIStyle(EditorStyles.label)
                                {
                                    normal = { textColor = iconColor }
                                },
                                GUILayout.Width(18));

                            EditorGUILayout.LabelField(
                                file,
                                EditorStyles.miniLabel);

                            if (exists && GUILayout.Button(
                                    "Ping",
                                    EditorStyles.miniButton,
                                    GUILayout.Width(40)))
                            {
                                string assetPath
                                    = GetAssetRelativePath(file);
                                if (!string.IsNullOrEmpty(assetPath))
                                {
                                    PingAsset(assetPath);
                                }
                            }
                        }
                    }

                    EditorGUILayout.EndScrollView();
                }
            }

            GUILayout.Space(6);

            // Actions
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Actions",
                    EditorStyles.miniBoldLabel);
                GUILayout.Space(2);

                if (GUILayout.Button(
                        "Force Regenerate All",
                        GUILayout.Height(24)))
                {
                    ArchetypeScanner.ForceRegenerate();
                }

                if (GUILayout.Button(
                        "Clear Cache & Regenerate",
                        GUILayout.Height(24)))
                {
                    if (EditorUtility.DisplayDialog(
                            "Clear Cache",
                            "Delete the cache and force a full "
                            + "regeneration on next compile?",
                            "Clear & Regenerate",
                            "Cancel"))
                    {
                        ArchetypeScanner.ForceRegenerate();
                    }
                }

                if (GUILayout.Button(
                        "Delete All Generated Files",
                        GUILayout.Height(24)))
                {
                    if (EditorUtility.DisplayDialog(
                            "Delete Generated Files",
                            "Delete all generated archetype .g.cs files? "
                            + "They will be regenerated on next compile.",
                            "Delete All",
                            "Cancel"))
                    {
                        DeleteAllGeneratedFiles(cache);
                    }
                }
            }
        }
        #endregion

        #region Serialized Property Helper
        private void DrawSerializedProperty(
            string propertyName,
            string label)
        {
            SerializedProperty prop = _serializedSettings
                .FindProperty(propertyName);

            if (prop == null)
            {
                EditorGUILayout.LabelField(
                    label,
                    $"<missing property: {propertyName}>");
                return;
            }

            EditorGUILayout.PropertyField(
                prop, new GUIContent(label));
        }
        #endregion

        #region State Badge
        private static void DrawStateBadge(
            string text,
            Color color)
        {
            GUIStyle badgeStyle = new GUIStyle(EditorStyles.miniButton)
            {
                normal = { textColor = Color.white },
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedWidth = 100
            };

            Color prevColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label(text, badgeStyle, GUILayout.Height(18));
            GUI.backgroundColor = prevColor;
        }
        #endregion

        #region Settings Asset Management
        internal static ArchetypeSettings GetOrCreateSettings()
        {
            ArchetypeSettings settings = AssetDatabase
                .LoadAssetAtPath<ArchetypeSettings>(
                    ArchetypeSettings.AssetPath);

            if (settings != null)
            {
                return settings;
            }

            // Create if missing
            string folder = ArchetypeSettings.AssetFolder;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            settings = ScriptableObject.CreateInstance<ArchetypeSettings>();
            AssetDatabase.CreateAsset(
                settings, ArchetypeSettings.AssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[Archetype] Created settings asset at '"
                + ArchetypeSettings.AssetPath + "'.");

            return settings;
        }

        private void EnsureSerializedObject(ArchetypeSettings settings)
        {
            if (_serializedSettings != null
                && _serializedSettings.targetObject == settings)
            {
                return;
            }

            _serializedSettings = new SerializedObject(settings);
        }

        private static void ResetSettings(ArchetypeSettings settings)
        {
            settings.generatedFolder = "Generated";
            settings.autoGenerateOnReload = true;
            settings.namespaceOverride = "";
            settings.managedOutputFolder = "";

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log("[Archetype] Settings reset to defaults.");
        }
        #endregion

        #region Utility Methods
        private static int CountUniqueArchetypes(
            List<ArchetypeCacheEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return 0;
            }

            HashSet<string> names = new HashSet<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].archetypeName))
                {
                    names.Add(entries[i].archetypeName);
                }
            }

            return names.Count;
        }

        private static bool AllFilesExist(List<string> files)
        {
            if (files == null || files.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < files.Count; i++)
            {
                if (!string.IsNullOrEmpty(files[i])
                    && !File.Exists(files[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, List<ArchetypeCacheEntry>>
            GroupByArchetype(List<ArchetypeCacheEntry> entries)
        {
            Dictionary<string, List<ArchetypeCacheEntry>> grouped
                = new Dictionary<string, List<ArchetypeCacheEntry>>();

            for (int i = 0; i < entries.Count; i++)
            {
                ArchetypeCacheEntry entry = entries[i];
                string key = entry.archetypeName;

                if (!grouped.TryGetValue(
                        key,
                        out List<ArchetypeCacheEntry> list))
                {
                    list = new List<ArchetypeCacheEntry>();
                    grouped[key] = list;
                }

                list.Add(entry);
            }

            return grouped;
        }

        private static string ExtractShortName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return "(null)";
            }

            int lastDot = fullName.LastIndexOf('.');
            return lastDot >= 0
                ? fullName.Substring(lastDot + 1)
                : fullName;
        }

        private static void PingGeneratedFile(string archetypeName)
        {
            ArchetypeSettings settings = GetOrCreateSettings();
            string filePath = settings.FullGeneratedFolder + "/"
                + archetypeName + ".g.cs";
            PingAsset(filePath);
        }

        private static void PingAsset(string assetPath)
        {
            UnityEngine.Object asset = AssetDatabase
                .LoadAssetAtPath<UnityEngine.Object>(assetPath);

            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
            }
            else
            {
                Debug.LogWarning(
                    $"[Archetype] Could not find asset at "
                    + $"'{assetPath}'.");
            }
        }

        private static string GetAssetRelativePath(
            string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return null;
            }

            string dataPath = Application.dataPath;
            if (absolutePath.StartsWith(
                    dataPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + absolutePath.Substring(
                    dataPath.Length).Replace('\\', '/');
            }

            return null;
        }

        private static void ExportSummaryJson(
            List<ArchetypeCacheEntry> entries)
        {
            // Build JSON manually since JsonUtility doesn't support
            // anonymous types on all Unity scripting backends.
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"entries\": [");

            for (int i = 0; i < entries.Count; i++)
            {
                ArchetypeCacheEntry e = entries[i];
                sb.AppendLine("    {");
                sb.AppendLine(
                    $"      \"concreteType\": \"{e.concreteTypeFullName}\",");
                sb.AppendLine(
                    $"      \"interfaceType\": \"{e.interfaceTypeFullName}\",");
                sb.AppendLine(
                    $"      \"archetypeName\": \"{e.archetypeName}\",");
                sb.AppendLine(
                    $"      \"archetypeField\": \"{e.archetypeField}\"");
                sb.Append("    }");

                if (i < entries.Count - 1)
                {
                    sb.AppendLine(",");
                }
                else
                {
                    sb.AppendLine();
                }
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            string json = sb.ToString();

            Debug.Log(
                "[Archetype] Summary JSON:\n" + json);

            EditorUtility.DisplayDialog(
                "Archetype Summary",
                "Summary has been logged to the Console window.",
                "OK");
        }

        private static void DeleteAllGeneratedFiles(
            ArchetypeCache cache)
        {
            if (cache != null && cache.files != null)
            {
                for (int i = 0; i < cache.files.Count; i++)
                {
                    string file = cache.files[i];
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                        }

                        string metaFile = file + ".meta";
                        if (File.Exists(metaFile))
                        {
                            File.Delete(metaFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"[Archetype] Failed to delete '{file}'. "
                            + $"Error: {ex.Message}");
                    }
                }
            }

            ArchetypeHashCache.Delete();
            AssetDatabase.Refresh();

            Debug.Log("[Archetype] All generated files deleted.");
        }

        private static void BuildPluginDll(ArchetypeSettings settings)
        {
            string outputFolder = settings.managedOutputFolder;
            if (string.IsNullOrEmpty(outputFolder))
            {
                EditorUtility.DisplayDialog(
                    "Build Plugin DLL",
                    "No output folder configured. "
                    + "Set the \"Managed DLL Output Folder\" "
                    + "in the Configuration tab.",
                    "OK");
                return;
            }

            Debug.Log(
                $"[Archetype] Build Plugin DLL requested. "
                + $"Output: {outputFolder}");

            // Placeholder — actual implementation depends on
            // build pipeline integration requirements.
            EditorUtility.DisplayDialog(
                "Build Plugin DLL",
                $"Plugin DLL build to '{outputFolder}' "
                + "is not yet implemented.",
                "OK");
        }
        #endregion
    }
}
