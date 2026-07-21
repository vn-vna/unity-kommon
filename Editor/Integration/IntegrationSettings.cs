using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Com.Hapiga.Scheherazade.Common.Editor.ScriptGeneration;
using Com.Hapiga.Scheherazade.Common.Editor.Toolkit;
using Com.Hapiga.Scheherazade.Common.Integration;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Integration.IAR;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase;
using Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig;
using Com.Hapiga.Scheherazade.Common.Integration.Segmentation;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Integration
{
    public abstract class BaseIntegrationSettingsProvider<TInterface> : SettingsProvider
        where TInterface : class
    {
        private readonly string _sectionName;
        private readonly string _managerLabel;
        private readonly IReadOnlyList<SettingsTab> _tabs;
        private readonly Type _filterFlagsEnumType;

        private int _selectedTabIndex;
        private int _featureFilter;

        internal BaseIntegrationSettingsProvider(
            string path,
            SettingsScope scopes,
            string sectionName,
            string managerLabel,
            IReadOnlyList<SettingsTab> tabs = null,
            IEnumerable<string> keywords = null,
            Type filterFlagsEnumType = null
        ) : base(path, scopes, keywords)
        {
            _sectionName = sectionName;
            _managerLabel = managerLabel;
            _tabs = tabs;
            _filterFlagsEnumType = filterFlagsEnumType;
        }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            IntegrationSettingsDrawingUtils.ProcessPendingProviderCreations();

            var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
            if (centre == null) return;

            ScriptableObject manager = IntegrationSettingsDrawingUtils.FindModuleAsset<TInterface>(centre);

            Type[] concreteTypes = IntegrationSettingsDrawingUtils.FindConcreteManagerTypes<TInterface>();

            IntegrationSettingsDrawingUtils.DrawManagerSettings<TInterface>(
                _sectionName,
                _managerLabel,
                concreteTypes,
                ref manager,
                centre,
                _tabs,
                ref _selectedTabIndex,
                ref _featureFilter,
                _filterFlagsEnumType
            );

            DrawExtraContent(manager);
        }

        protected virtual void DrawExtraContent(ScriptableObject manager) { }
    }

    public class IntegrationCentreSettingsProvider : SettingsProvider
    {
        private IntegrationCentreSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new IntegrationCentreSettingsProvider(
                "Project/Integration", SettingsScope.Project,
                new[] { "integration", "centre", "center", "module", "modules" }
            );
        }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            IntegrationSettingsDrawingUtils.ProcessPendingProviderCreations();

            var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
            if (centre == null) return;

            EditorGUILayout.LabelField("Integration Centre Configuration", EditorStyles.boldLabel);
            GUILayout.Space(4);

            IntegrationSettingsDrawingUtils.DrawInlineInspector(
                centre,
                "INTEGRATION CENTRE"
            );
        }
    }

    public class AdsIntegrationSettingsProvider : BaseIntegrationSettingsProvider<IAdsManager>
    {
        private static readonly SettingsTab[] Tabs =
        {
            new SettingsTab(
                "Providers",
                new ProviderDescriptor[]
                {
                    new ProviderDescriptor(
                        "AppLovin MAX",
                        "adServiceProvider",
                        ProviderBindingMode.Single,
                        "Com.Hapiga.Scheherazade.Common.Integration.Ads.ApplovinMaxAdsServiceProvider",
                        new[] { "APPLOVIN_MAX" },
                        new[] { "MaxSdk" },
                        customProviderBaseTypeName: "Com.Hapiga.Scheherazade.Common.Integration.Ads.IAdsServiceProvider"
                    )
                }
            )
        };

        private AdsIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, "Ads Manager Configuration", "Ads Manager Asset",
            Tabs, keywords)
        { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new AdsIntegrationSettingsProvider("Project/Integration/Ads", SettingsScope.Project);
        }
    }

    public class TrackingIntegrationSettingsProvider : BaseIntegrationSettingsProvider<ITrackingManager>
    {
        private static readonly string[] TemplateSubTabNames = { "Parameter Providers", "Templated Events" };

        private static int _templateSubTabIndex;
        private static Vector2 _templateProvidersScrollPos;
        private static Vector2 _templateEventsScrollPos;
        private static int _selectedParamIndex;
        private static TemplatedTrackingEvent _pendingEventToAdd;
        private static readonly Dictionary<int, bool> _eventParamFoldouts
            = new Dictionary<int, bool>();

        private static string _providerSearchText = "";
        private static string _eventSearchText = "";

        private static Type[] _cachedTemplatedProviderTypes;
        private static GUIStyle _cachedValidNameStyle;
        private static GUIStyle _cachedInvalidNameStyle;
        private static readonly Dictionary<int, bool> _parameterFoldouts
            = new Dictionary<int, bool>();

        private static readonly SettingsTab[] Tabs =
        {
            new SettingsTab(
                "Providers",
                new ProviderDescriptor[]
                {
                    new ProviderDescriptor(
                        "Firebase Analytics",
                        "initialProviders",
                        ProviderBindingMode.Collection,
                        "Com.Hapiga.Scheherazade.Common.Integration.Tracking.FirebaseTrackingProvider",
                        new[] { "FIREBASE_ANALYTICS" },
                        new[] { "Firebase.Analytics.FirebaseAnalytics" },
                        featureFlags: (int)TrackingProviderFeatures.AllFeatures,
                        customProviderBaseTypeName: "Com.Hapiga.Scheherazade.Common.Integration.Tracking.ITrackingProvider"
                    ),
                    new ProviderDescriptor(
                        "Adjust",
                        "initialProviders",
                        ProviderBindingMode.Collection,
                        "Com.Hapiga.Scheherazade.Common.Integration.Tracking.AdjustTrackingProvider",
                        new[] { "TRACKING_ADJUST" },
                        new[] { "AdjustSdk.Adjust" },
                        featureFlags: (int)TrackingProviderFeatures.Revenue,
                        customProviderBaseTypeName: "Com.Hapiga.Scheherazade.Common.Integration.Tracking.ITrackingProvider"
                    ),
                    new ProviderDescriptor(
                        "AppMetrica",
                        "initialProviders",
                        ProviderBindingMode.Collection,
                        "Com.Hapiga.Scheherazade.Common.Integration.Tracking.AppMetricaTrackingProvider",
                        new[] { "TRACKING_APPMETRICA" },
                        new[] { "Io.AppMetrica.AppMetrica" },
                        featureFlags: (int)TrackingProviderFeatures.AllFeatures,
                        customProviderBaseTypeName: "Com.Hapiga.Scheherazade.Common.Integration.Tracking.ITrackingProvider"
                    )
                }
            ),
            new SettingsTab(
                "Template Tracking",
                customRenderer: DrawTemplateTrackingTab
            )
        };

        private TrackingIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, "Tracking Manager Configuration", "Tracking Manager Asset",
            Tabs, keywords, filterFlagsEnumType: typeof(TrackingProviderFeatures))
        { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new TrackingIntegrationSettingsProvider("Project/Integration/Tracking", SettingsScope.Project);
        }

        private static void DrawTemplateTrackingTab(ScriptableObject manager)
        {
            GUILayout.Space(4);
            _templateSubTabIndex = GUILayout.Toolbar(_templateSubTabIndex, TemplateSubTabNames);

            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(dividerRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);

            switch (_templateSubTabIndex)
            {
                case 0:
                    DrawTemplatedParameterProviders(manager);
                    break;
                case 1:
                    DrawTemplatedEvents(manager);
                    break;
            }
        }

        #region Parameter Providers Drawing

        private static Type[] FindTemplatedProviderTypes()
        {
            if (_cachedTemplatedProviderTypes != null)
                return _cachedTemplatedProviderTypes;

            _cachedTemplatedProviderTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(t => t.IsClass
                            && !t.IsAbstract
                            && typeof(ScriptableObject).IsAssignableFrom(t)
                            && typeof(ITemplatedTrackingParametersProvider).IsAssignableFrom(t))
                .OrderBy(t => t.FullName)
                .ToArray();

            return _cachedTemplatedProviderTypes;
        }

        private static ScriptableObject FindExistingProviderAsset(
            SerializedProperty providersProp,
            Type providerType)
        {
            for (int i = 0; i < providersProp.arraySize; i++)
            {
                ScriptableObject asset =
                    providersProp.GetArrayElementAtIndex(i).objectReferenceValue
                        as ScriptableObject;
                if (asset != null && providerType.IsInstanceOfType(asset))
                {
                    return asset;
                }
            }

            return null;
        }

        private static int FindProviderAssetIndex(
            SerializedProperty providersProp,
            Type providerType)
        {
            for (int i = 0; i < providersProp.arraySize; i++)
            {
                ScriptableObject asset =
                    providersProp.GetArrayElementAtIndex(i).objectReferenceValue
                        as ScriptableObject;
                if (asset != null && providerType.IsInstanceOfType(asset))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void DrawTemplatedParameterProviders(ScriptableObject manager)
        {
            Type[] types = FindTemplatedProviderTypes();

            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty providersProp =
                serializedManager.FindProperty("templatedParameterProviders");

            if (providersProp == null || !providersProp.isArray)
            {
                EditorGUILayout.HelpBox(
                    "Could not find 'templatedParameterProviders' field on the manager.",
                    MessageType.Error);
                return;
            }

            // â”€â”€ Control Bar â”€â”€
            DrawProviderControlBar(types);

            // â”€â”€ Filter â”€â”€
            string search = _providerSearchText.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                types = types.Where(t =>
                    t.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                ).ToArray();
            }

            _templateProvidersScrollPos =
                EditorGUILayout.BeginScrollView(_templateProvidersScrollPos);

            if (types.Length == 0)
            {
                string msg = string.IsNullOrEmpty(search)
                    ? "No classes implementing ITemplatedTrackingParametersProvider found.\n\n"
                      + "Create one with the button below."
                    : $"No providers matching '{search}'.";
                EditorGUILayout.HelpBox(msg, MessageType.Info);
            }
            else
            {
                foreach (Type providerType in types)
                {
                    ScriptableObject existingAsset =
                        FindExistingProviderAsset(providersProp, providerType);
                    DrawTemplatedProviderCard(
                        manager, serializedManager, providersProp,
                        providerType, existingAsset);
                }
            }

            EditorGUILayout.EndScrollView();

            DrawNewParameterProviderButton();
        }

        private static void DrawProviderControlBar(Type[] types)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _providerSearchText = EditorGUILayout.TextField(
                    _providerSearchText,
                    EditorStyles.toolbarSearchField,
                    GUILayout.MinWidth(120));

                if (GUILayout.Button(
                        _parameterFoldouts.Values.All(v => v) ? "Collapse All" : "Expand All",
                        EditorStyles.toolbarButton,
                        GUILayout.Width(90)))
                {
                    bool expand = !_parameterFoldouts.Values.All(v => v);
                    var keys = new List<int>(_parameterFoldouts.Keys);
                    foreach (int k in keys)
                        _parameterFoldouts[k] = expand;
                }

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    _cachedTemplatedProviderTypes = null;
                    _providerSearchText = "";
                }
            }

            GUILayout.Space(2);
        }

        private static void DrawTemplatedProviderCard(
            ScriptableObject manager,
            SerializedObject serializedManager,
            SerializedProperty providersProp,
            Type providerType,
            ScriptableObject existingAsset)
        {
            bool isEnabled = existingAsset != null
                && IsParameterProviderEnabled(existingAsset);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Header row
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        GetFriendlyTypeName(providerType),
                        EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (isEnabled)
                    {
                        if (GUILayout.Button("Disable", GUILayout.Width(70)))
                        {
                            SetParameterProviderEnabled(existingAsset, false);
                            serializedManager.ApplyModifiedProperties();
                            EditorUtility.SetDirty(manager);
                            AssetDatabase.SaveAssets();
                            return;
                        }

                        if (DrawSmallDeleteButton())
                        {
                            int idx = FindProviderAssetIndex(
                                providersProp, providerType);
                            if (idx >= 0)
                            {
                                providersProp.DeleteArrayElementAtIndex(idx);
                                // Clean up null gaps
                                if (idx < providersProp.arraySize
                                    && providersProp.GetArrayElementAtIndex(idx)
                                        .objectReferenceValue == null)
                                {
                                    providersProp.DeleteArrayElementAtIndex(idx);
                                }
                            }

                            DeleteParameterProviderAsset(existingAsset);
                            serializedManager.ApplyModifiedProperties();
                            EditorUtility.SetDirty(manager);
                            AssetDatabase.SaveAssets();
                            return;
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Enable", GUILayout.Width(70)))
                        {
                            EnableTemplatedProvider(
                                manager, serializedManager, providersProp,
                                providerType, existingAsset);
                            return;
                        }

                        // Only show delete for existing disabled assets
                        if (existingAsset != null && DrawSmallDeleteButton())
                        {
                            int idx = FindProviderAssetIndex(
                                providersProp, providerType);
                            if (idx >= 0)
                            {
                                providersProp.DeleteArrayElementAtIndex(idx);
                                if (idx < providersProp.arraySize
                                    && providersProp.GetArrayElementAtIndex(idx)
                                        .objectReferenceValue == null)
                                {
                                    providersProp.DeleteArrayElementAtIndex(idx);
                                }
                            }

                            DeleteParameterProviderAsset(existingAsset);
                            serializedManager.ApplyModifiedProperties();
                            EditorUtility.SetDirty(manager);
                            AssetDatabase.SaveAssets();
                            return;
                        }
                    }
                }

                if (!isEnabled)
                {
                    if (existingAsset != null)
                    {
                        EditorGUILayout.LabelField(
                            "Provider is disabled.", EditorStyles.miniLabel);
                    }
                    else
                    {
                        EditorGUILayout.LabelField(
                            "Not yet created. Click Enable to create and assign.",
                            EditorStyles.miniLabel);
                    }

                    return;
                }

                // Enabled: draw config + separator + defined parameters
                GUILayout.Space(4);
                IntegrationSettingsDrawingUtils.DrawInlineInspector(
                    existingAsset,
                    $"{existingAsset.name.ToUpperInvariant()} CONFIGURATION");

                GUILayout.Space(4);
                Rect sepRect = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(sepRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
                GUILayout.Space(4);

                DrawDefinedParameters(existingAsset);
            }
        }

        private static void EnableTemplatedProvider(
            ScriptableObject manager,
            SerializedObject serializedManager,
            SerializedProperty providersProp,
            Type providerType,
            ScriptableObject existingAsset)
        {
            ScriptableObject asset = existingAsset;

            if (asset == null)
            {
                // Search for existing asset on disk
                string[] guids = AssetDatabase.FindAssets("t:" + providerType.Name);
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    asset = AssetDatabase.LoadAssetAtPath(path, providerType)
                        as ScriptableObject;
                    if (asset != null) break;
                }

                // Create new if not found
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance(providerType);
                    asset.name = providerType.Name;

                    string folderPath = "Assets/Resources/Integration";
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                    }

                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                        $"{folderPath}/{providerType.Name}.asset");
                    AssetDatabase.CreateAsset(asset, assetPath);
                }

                // Add to array
                int newIndex = providersProp.arraySize++;
                providersProp.GetArrayElementAtIndex(newIndex)
                    .objectReferenceValue = asset;
            }

            SetParameterProviderEnabled(asset, true);
            serializedManager.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);
        }

        private static void DeleteParameterProviderAsset(ScriptableObject asset)
        {
            if (asset == null) return;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return;

            if (EditorUtility.DisplayDialog(
                    "Delete Parameter Provider",
                    $"Delete '{asset.name}' at '{path}'?",
                    "Delete", "Cancel"))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets();
            }
        }

        private static bool IsParameterProviderEnabled(ScriptableObject asset)
        {
            if (asset is ITemplatedTrackingParametersProvider provider)
            {
                return provider.IsEnabled;
            }

            return false;
        }

        private static void SetParameterProviderEnabled(
            ScriptableObject asset, bool enabled)
        {
            SerializedObject so = new SerializedObject(asset);
            SerializedProperty prop = so.FindProperty("_isEnabled");

            if (prop == null)
            {
                prop = so.FindProperty("isEnabled");
            }

            if (prop != null)
            {
                prop.boolValue = enabled;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
            }
        }

        private static void DrawNewParameterProviderButton()
        {
            GUILayout.Space(4);
            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(dividerRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);

            Type targetType = typeof(ITemplatedTrackingParametersProvider);

            if (GUILayout.Button("New Parameter Provider", GUILayout.Height(25)))
            {
                ScriptTemplateGenerator.CreatePluginScript(
                    null,
                    "Assets/",
                    targetType,
                    ScriptTemplateGenerator.GenerationMode.InterfaceImplementation);
            }
        }

        #endregion

        #region Parameter Validation & Display

        private static void DrawDefinedParameters(ScriptableObject asset)
        {
            if (asset is not ITemplatedTrackingParametersProvider) return;

            Type type = asset.GetType();
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var entries = new List<ParamMethodEntry>();
            var seenNames = new HashSet<string>();
            var factoryResults = new Dictionary<string, string>();

            // First pass: collect factory results for "Default" display
            foreach (MethodInfo method in methods)
            {
                TrackingParamDefaultFactoryAttribute factoryAttr =
                    method.GetCustomAttribute<TrackingParamDefaultFactoryAttribute>();
                if (factoryAttr == null) continue;
                if (!IsMethodValidForTemplated(method)) continue;

                try
                {
                    object result = method.Invoke(asset, null);
                    factoryResults[factoryAttr.ParameterName] = result?.ToString() ?? "null";
                }
                catch
                {
                    factoryResults[factoryAttr.ParameterName] = "<error>";
                }
            }

            // Second pass: build entries
            foreach (MethodInfo method in methods)
            {
                TrackingParamGetterAttribute getterAttr =
                    method.GetCustomAttribute<TrackingParamGetterAttribute>();
                if (getterAttr != null)
                {
                    entries.Add(ValidateParamMethod(
                        asset, method, getterAttr.ParameterName, getterAttr.DisplayName,
                        "getter", seenNames, factoryResults));
                    continue;
                }

                TrackingParamDefaultFactoryAttribute factoryAttr =
                    method.GetCustomAttribute<TrackingParamDefaultFactoryAttribute>();
                if (factoryAttr != null)
                {
                    entries.Add(ValidateParamMethod(
                        asset, method, factoryAttr.ParameterName, factoryAttr.DisplayName,
                        "factory", seenNames, factoryResults));
                }
            }

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No [TrackingParamGetter] or [TrackingParamDefaultFactory] methods found on this provider.",
                    MessageType.Info);
                return;
            }

            int validGetterCount = entries.Count(e => e.Kind == "getter" && e.IsValid);
            int validFactoryCount = entries.Count(e => e.Kind == "factory" && e.IsValid);
            int key = asset.GetInstanceID();

            if (!_parameterFoldouts.ContainsKey(key))
                _parameterFoldouts[key] = true;

            bool expanded = _parameterFoldouts[key];
            string arrow = expanded ? "â–¼" : "â–¶";
            string label = $"{arrow} Defined Parameters ({validGetterCount} getters, {validFactoryCount} factories)";

            if (GUILayout.Button(label, EditorStyles.boldLabel))
            {
                _parameterFoldouts[key] = !expanded;
            }

            if (!expanded) return;

            GUILayout.Space(4);

            foreach (ParamMethodEntry entry in entries)
            {
                DrawParamMethodBox(entry);
            }
        }

        private static bool IsMethodValidForTemplated(MethodInfo method)
        {
            if (method.IsStatic) return false;
            if (method.GetParameters().Length > 0) return false;
            if (method.ReturnType == typeof(void)) return false;
            return true;
        }

        private static ParamMethodEntry ValidateParamMethod(
            ScriptableObject asset,
            MethodInfo method,
            string paramName,
            string displayName,
            string kind,
            HashSet<string> seenNames,
            Dictionary<string, string> factoryResults)
        {
            var entry = new ParamMethodEntry
            {
                ParamName = paramName,
                DisplayName = displayName,
                MethodName = method.Name,
                ReturnType = method.ReturnType,
                Kind = kind,
                IsValid = true,
                MethodInfo = method,
                ProviderAsset = asset
            };

            // Extract description attribute
            TrackingParamDescriptionAttribute descAttr =
                method.GetCustomAttribute<TrackingParamDescriptionAttribute>();
            entry.Description = descAttr?.Description;

            // Extract preview attribute
            entry.HasPreview =
                method.GetCustomAttribute<TrackingParamPreviewAttribute>() != null;

            // Validation
            if (method.IsStatic)
            {
                entry.IsValid = false;
                entry.ErrorMessage = "Must be an instance method (static not allowed).";
            }
            else if (method.GetParameters().Length > 0)
            {
                entry.IsValid = false;
                entry.ErrorMessage = "Must have zero parameters.";
            }
            else if (method.ReturnType == typeof(void))
            {
                entry.IsValid = false;
                entry.ErrorMessage = "Must return a value (cannot be void).";
            }
            else if (!seenNames.Add(paramName))
            {
                entry.IsValid = false;
                entry.ErrorMessage = $"Duplicate parameter name '{paramName}'.";
            }

            // Default value from factory
            if (entry.IsValid && factoryResults.TryGetValue(paramName, out string defVal))
            {
                entry.DefaultValue = defVal;
            }

            return entry;
        }

        private static void DrawParamMethodBox(ParamMethodEntry entry)
        {
            Color boxColor = entry.IsValid
                ? new Color(0.22f, 0.22f, 0.22f, 0.5f)
                : new Color(0.35f, 0.15f, 0.15f, 0.5f);

            Rect boxRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.DrawRect(boxRect, boxColor);

            // â•â•â• Header: name + status + navigate â•â•â•
            using (new EditorGUILayout.HorizontalScope())
            {
                string statusIcon = entry.IsValid ? "\u2713" : "\u2717";
                Color originalColor = GUI.color;

                if (!entry.IsValid)
                {
                    GUI.color = Color.red;
                }

                if (_cachedValidNameStyle == null)
                {
                    _cachedValidNameStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        normal = { textColor = Color.white }
                    };
                    _cachedInvalidNameStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        normal = { textColor = Color.red }
                    };
                }

                GUIStyle nameStyle = entry.IsValid
                    ? _cachedValidNameStyle
                    : _cachedInvalidNameStyle;

                string headerLabel = entry.DisplayName != null
                    && entry.DisplayName != entry.ParamName
                    ? $"{statusIcon} {entry.DisplayName} [{entry.ParamName}]"
                    : $"{statusIcon} {entry.ParamName}";

                EditorGUILayout.LabelField(
                    headerLabel,
                    nameStyle);

                GUI.color = originalColor;

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Navigate", EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    NavigateToMethod(entry.MethodInfo);
                }
            }

            // Error message
            if (!entry.IsValid && !string.IsNullOrEmpty(entry.ErrorMessage))
            {
                EditorGUILayout.HelpBox(entry.ErrorMessage, MessageType.Error);
            }

            using (new EditorGUI.IndentLevelScope())
            {
                // Type row
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Type:", GUILayout.Width(80));
                    EditorGUILayout.LabelField(
                        GetFriendlyTypeName(entry.ReturnType),
                        EditorStyles.boldLabel);
                }

                // Default row
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Default:", GUILayout.Width(80));
                    string defaultDisplay = entry.DefaultValue ?? "<missing>";
                    EditorGUILayout.LabelField(
                        defaultDisplay,
                        string.IsNullOrEmpty(entry.DefaultValue)
                            ? EditorStyles.miniLabel
                            : EditorStyles.label);
                }

                // Preview row (only for getters with preview attribute)
                if (entry.Kind == "getter" && entry.HasPreview && entry.IsValid)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Preview:", GUILayout.Width(80));
                        string previewValue = GetPreviewValue(entry);
                        EditorGUILayout.LabelField(
                            previewValue,
                            EditorStyles.wordWrappedLabel);
                    }
                }

                // Description row
                if (!string.IsNullOrEmpty(entry.Description))
                {
                    EditorGUILayout.LabelField("Description:", GUILayout.Width(80));
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.LabelField(
                            entry.Description,
                            EditorStyles.wordWrappedLabel);
                    }
                }
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        private static string GetPreviewValue(ParamMethodEntry entry)
        {
            if (entry.ProviderAsset == null || entry.MethodInfo == null)
            {
                return "<unavailable>";
            }

            try
            {
                object result = entry.MethodInfo.Invoke(entry.ProviderAsset, null);
                return result?.ToString() ?? "null";
            }
            catch (Exception ex)
            {
                return $"<error: {ex.Message}>";
            }
        }

        private static void NavigateToMethod(MethodInfo method)
        {
            if (method?.DeclaringType == null) return;

            string[] guids = AssetDatabase.FindAssets(
                $"t:MonoScript {method.DeclaringType.Name}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == method.DeclaringType)
                {
                    AssetDatabase.OpenAsset(script);
                    return;
                }
            }
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(long)) return "long";
            if (type == typeof(short)) return "short";
            if (type == typeof(byte)) return "byte";
            return type.Name;
        }

        private struct ParamEntry
        {
            public string Id;
            public string Name;
            public string ReturnType;
            public string Description;
        }

        private struct ParamMethodEntry
        {
            public string ParamName;
            public string DisplayName;
            public string MethodName;
            public Type ReturnType;
            public string Kind;
            public bool IsValid;
            public string ErrorMessage;
            public MethodInfo MethodInfo;
            public ScriptableObject ProviderAsset;
            public string Description;
            public string DefaultValue;
            public bool HasPreview;
        }

        #endregion

        #region Templated Events Drawing

        private static void DrawTemplatedEvents(ScriptableObject manager)
        {
            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty eventsProp =
                serializedManager.FindProperty("templatedEvents");

            if (eventsProp == null || !eventsProp.isArray)
            {
                EditorGUILayout.HelpBox(
                    "Could not find 'templatedEvents' field on the manager.",
                    MessageType.Error);
                return;
            }

            // â”€â”€ Control Bar â”€â”€
            DrawEventsControlBar(serializedManager, eventsProp);

            // â”€â”€ Filter events â”€â”€
            List<int> visibleIndices = new List<int>();
            string search = _eventSearchText.Trim();
            for (int i = eventsProp.arraySize - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(search))
                {
                    visibleIndices.Add(i);
                }
                else
                {
                    SerializedProperty element = eventsProp.GetArrayElementAtIndex(i);
                    ScriptableObject asset = element.objectReferenceValue as ScriptableObject;
                    SerializedObject so = asset != null ? new SerializedObject(asset) : null;
                    string eventName = so?.FindProperty("_eventName")?.stringValue ?? "";
                    string assetName = asset?.name ?? "";
                    if (assetName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                        || eventName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        visibleIndices.Add(i);
                    }
                }
            }

            _templateEventsScrollPos =
                EditorGUILayout.BeginScrollView(_templateEventsScrollPos);

            foreach (int i in visibleIndices)
            {
                SerializedProperty element = eventsProp.GetArrayElementAtIndex(i);
                DrawSingleTemplatedEvent(manager, serializedManager, eventsProp,
                    i, element);
            }

            EditorGUILayout.EndScrollView();

            if (eventsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No templated events defined. Create one below.",
                    MessageType.Info);
            }

            GUILayout.Space(8);
            DrawNewTemplatedEventButton(manager, serializedManager, eventsProp);
        }

        private static void DrawEventsControlBar(
            SerializedObject serializedManager,
            SerializedProperty eventsProp)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _eventSearchText = EditorGUILayout.TextField(
                    _eventSearchText,
                    EditorStyles.toolbarSearchField,
                    GUILayout.MinWidth(120));

                if (GUILayout.Button(
                        _eventParamFoldouts.Values.All(v => v) ? "Collapse All" : "Expand All",
                        EditorStyles.toolbarButton,
                        GUILayout.Width(90)))
                {
                    bool expand = !_eventParamFoldouts.Values.All(v => v);
                    var keys = new List<int>(_eventParamFoldouts.Keys);
                    foreach (int k in keys)
                        _eventParamFoldouts[k] = expand;
                }

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    _eventSearchText = "";
                    _eventParamFoldouts.Clear();
                }
            }

            GUILayout.Space(2);
        }

        private static void DrawSingleTemplatedEvent(
            ScriptableObject manager,
            SerializedObject serializedManager,
            SerializedProperty eventsProp,
            int index,
            SerializedProperty element)
        {
            ScriptableObject eventAsset = element.objectReferenceValue as ScriptableObject;
            if (eventAsset == null)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(
                        $"<missing> (slot {index})",
                        EditorStyles.boldLabel);
                    if (DrawSmallDeleteButton())
                    {
                        eventsProp.DeleteArrayElementAtIndex(index);
                        serializedManager.ApplyModifiedProperties();
                        EditorUtility.SetDirty(manager);
                        AssetDatabase.SaveAssets();
                    }
                }
                return;
            }

            SerializedObject eventSo = new SerializedObject(eventAsset);
            SerializedProperty eventNameProp = eventSo.FindProperty("_eventName");
            SerializedProperty paramsProp = eventSo.FindProperty("_parameters");
            string assetName = eventAsset.name;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // â”€â”€ Header: asset name + Edit button + Delete â”€â”€
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(assetName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Edit", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        Rect buttonRect = GUILayoutUtility.GetLastRect();
                        buttonRect.position = GUIUtility.GUIToScreenPoint(buttonRect.position);
                        var popup = new EventEditPopup(
                            eventAsset, eventNameProp, paramsProp,
                            manager, serializedManager, eventsProp, index);
                        PopupWindow.Show(buttonRect, popup);
                    }

                    if (DrawSmallDeleteButton())
                    {
                        AssetDatabase.RemoveObjectFromAsset(eventAsset);
                        UnityEngine.Object.DestroyImmediate(eventAsset, true);
                        eventsProp.DeleteArrayElementAtIndex(index);
                        serializedManager.ApplyModifiedProperties();
                        EditorUtility.SetDirty(manager);
                        AssetDatabase.SaveAssets();
                        return;
                    }
                }

                // â”€â”€ Event name (read-only) â”€â”€
                string eventName = eventNameProp?.stringValue ?? "";
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Event Name", eventName);
                }

                if (string.IsNullOrEmpty(eventName))
                {
                    EditorGUILayout.HelpBox(
                        "Event name must not be empty.",
                        MessageType.Error);
                }

                // â”€â”€ Parameters section â”€â”€
                DrawEventParametersSection(
                    manager, eventAsset, eventSo,
                    eventNameProp, paramsProp,
                    serializedManager, eventsProp, index);

                if (eventSo.hasModifiedProperties)
                {
                    eventSo.ApplyModifiedProperties();
                    EditorUtility.SetDirty(eventAsset);
                }
            }
        }

        private static void DrawEventParametersSection(
            ScriptableObject manager,
            ScriptableObject eventAsset,
            SerializedObject eventSo,
            SerializedProperty eventNameProp,
            SerializedProperty paramsProp,
            SerializedObject serializedManager,
            SerializedProperty eventsProp,
            int index)
        {
            if (paramsProp == null || !paramsProp.isArray) return;

            int key = eventAsset.GetInstanceID();
            if (!_eventParamFoldouts.ContainsKey(key))
                _eventParamFoldouts[key] = true;
            bool expanded = _eventParamFoldouts[key];

            // â”€â”€ Header row: Parameters + Edit + Collapse/Expand â”€â”€
            using (new EditorGUILayout.HorizontalScope())
            {
                string arrow = expanded ? "â–¼" : "â–¶";
                int paramCount = paramsProp.arraySize;
                EditorGUILayout.LabelField(
                    $"Parameters ({paramCount})",
                    EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                string toggleLabel = expanded ? "Collapse" : "Expand";
                if (GUILayout.Button(toggleLabel, EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    _eventParamFoldouts[key] = !expanded;
                }
            }

            if (!expanded) return;

            GUILayout.Space(4);

            // â”€â”€ Read-only parameter rows â”€â”€
            for (int p = 0; p < paramsProp.arraySize; p++)
            {
                SerializedProperty element =
                    paramsProp.GetArrayElementAtIndex(p);

                SerializedProperty idProp =
                    element.FindPropertyRelative("_parameterId");
                SerializedProperty nameProp =
                    element.FindPropertyRelative("_name");
                SerializedProperty descProp =
                    element.FindPropertyRelative("_description");
                SerializedProperty defaultProp =
                    element.FindPropertyRelative("_defaultValue");

                string pId = idProp?.stringValue ?? "";
                string pName = nameProp?.stringValue ?? "";
                string pDesc = descProp?.stringValue ?? "";
                string pDefault = defaultProp?.stringValue ?? "";

                // Determine parameter type from providers
                string lookupKey = !string.IsNullOrEmpty(pId) ? pId : pName;
                string typeLabel = ResolveParamType(manager, lookupKey);

                string display = (string.IsNullOrEmpty(pId) || pId == pName)
                    ? pName
                    : $"{pName} [{pId}]";

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(display, EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        if (!string.IsNullOrEmpty(typeLabel))
                        {
                            EditorGUILayout.LabelField(
                                typeLabel,
                                EditorStyles.miniLabel,
                                GUILayout.Width(60));
                        }
                    }

                    using (new EditorGUI.IndentLevelScope())
                    {
                        if (!string.IsNullOrEmpty(pDesc))
                        {
                            EditorGUILayout.LabelField(
                                pDesc,
                                EditorStyles.wordWrappedMiniLabel);
                        }

                        if (!string.IsNullOrEmpty(pDefault))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField(
                                    "Default:",
                                    EditorStyles.miniLabel,
                                    GUILayout.Width(50));
                                EditorGUILayout.LabelField(
                                    pDefault,
                                    EditorStyles.miniLabel);
                            }
                        }
                    }
                }
            }

            // â”€â”€ Warnings â”€â”€
            DrawEventParameterWarnings(paramsProp, manager);
        }

        private static string ResolveParamType(ScriptableObject manager, string paramId)
        {
            if (string.IsNullOrEmpty(paramId)) return "";

            SerializedObject so = new SerializedObject(manager);
            SerializedProperty providersProp =
                so.FindProperty("templatedParameterProviders");

            if (providersProp == null || !providersProp.isArray) return "";

            for (int i = 0; i < providersProp.arraySize; i++)
            {
                ScriptableObject asset =
                    providersProp.GetArrayElementAtIndex(i)
                        .objectReferenceValue as ScriptableObject;
                if (asset == null) continue;
                if (asset is not ITemplatedTrackingParametersProvider provider) continue;
                if (!provider.IsEnabled) continue;

                Type type = asset.GetType();
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    TrackingParamGetterAttribute getterAttr =
                        method.GetCustomAttribute<TrackingParamGetterAttribute>();
                    if (getterAttr != null && getterAttr.ParameterName == paramId)
                        return GetFriendlyTypeName(method.ReturnType);
                }
            }

            return "";
        }

        private static List<ParamEntry> CollectAvailableParamNamesFromProviders(
            ScriptableObject manager)
        {
            var entries = new List<ParamEntry>();
            var seenIds = new HashSet<string>();

            SerializedObject so = new SerializedObject(manager);
            SerializedProperty providersProp =
                so.FindProperty("templatedParameterProviders");

            if (providersProp == null || !providersProp.isArray) return entries;

            for (int i = 0; i < providersProp.arraySize; i++)
            {
                ScriptableObject asset =
                    providersProp.GetArrayElementAtIndex(i)
                        .objectReferenceValue as ScriptableObject;
                if (asset == null) continue;
                if (asset is not ITemplatedTrackingParametersProvider provider) continue;
                if (!provider.IsEnabled) continue;

                Type type = asset.GetType();
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    TrackingParamGetterAttribute getterAttr =
                        method.GetCustomAttribute<TrackingParamGetterAttribute>();
                    if (getterAttr != null && seenIds.Add(getterAttr.ParameterName))
                    {
                        TrackingParamDescriptionAttribute descAttr =
                            method.GetCustomAttribute<TrackingParamDescriptionAttribute>();
                        entries.Add(new ParamEntry
                        {
                            Id = getterAttr.ParameterName,
                            Name = getterAttr.DisplayName,
                            ReturnType = GetFriendlyTypeName(method.ReturnType),
                            Description = descAttr?.Description
                        });
                        continue;
                    }

                    TrackingParamDefaultFactoryAttribute factoryAttr =
                        method.GetCustomAttribute<TrackingParamDefaultFactoryAttribute>();
                    if (factoryAttr != null && seenIds.Add(factoryAttr.ParameterName))
                    {
                        TrackingParamDescriptionAttribute descAttr =
                            method.GetCustomAttribute<TrackingParamDescriptionAttribute>();
                        entries.Add(new ParamEntry
                        {
                            Id = factoryAttr.ParameterName,
                            Name = factoryAttr.DisplayName,
                            ReturnType = GetFriendlyTypeName(method.ReturnType),
                            Description = descAttr?.Description
                        });
                    }
                }
            }

            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            return entries;
        }

        #endregion

        #region Parameter Warnings

        private static void DrawEventParameterWarnings(
            SerializedProperty paramsProp,
            ScriptableObject manager)
        {
            if (paramsProp == null || !paramsProp.isArray) return;

            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty providersProp =
                serializedManager.FindProperty("templatedParameterProviders");

            // Collect all available parameter IDs from enabled providers
            HashSet<string> availableIds = new HashSet<string>();

            if (providersProp != null && providersProp.isArray)
            {
                for (int i = 0; i < providersProp.arraySize; i++)
                {
                    ScriptableObject asset =
                        providersProp.GetArrayElementAtIndex(i)
                            .objectReferenceValue as ScriptableObject;

                    if (asset == null) continue;
                    if (asset is not ITemplatedTrackingParametersProvider provider) continue;
                    if (!provider.IsEnabled) continue;

                    CollectProviderParamIds(asset, availableIds);
                }
            }

            for (int i = 0; i < paramsProp.arraySize; i++)
            {
                SerializedProperty idProp =
                    paramsProp.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("_parameterId");
                string pId = idProp?.stringValue;

                SerializedProperty nameProp =
                    paramsProp.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("_name");
                string pName = nameProp?.stringValue;
                if (string.IsNullOrEmpty(pName)) continue;

                // Fall back to name if no id set (backward compat)
                string lookupKey = !string.IsNullOrEmpty(pId) ? pId : pName;

                SerializedProperty defaultProp =
                    paramsProp.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("_defaultValue");

                bool hasDefault = !string.IsNullOrEmpty(defaultProp?.stringValue);

                if (!availableIds.Contains(lookupKey) && !hasDefault)
                {
                    string displayLabel = string.IsNullOrEmpty(pId) || pId == pName
                        ? pName
                        : $"{pName} [{pId}]";

                    EditorGUILayout.HelpBox(
                        $"Parameter \"{displayLabel}\" has no getter/factory in any enabled "
                        + "parameter provider and no default value.\n"
                        + "It will be omitted from the tracking event at runtime.",
                        MessageType.Warning);
                }
            }
        }

        private static void CollectProviderParamIds(
            ScriptableObject asset,
            HashSet<string> ids)
        {
            Type type = asset.GetType();
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (MethodInfo method in methods)
            {
                TrackingParamGetterAttribute getterAttr =
                    method.GetCustomAttribute<TrackingParamGetterAttribute>();
                if (getterAttr != null)
                {
                    ids.Add(getterAttr.ParameterName);
                    continue;
                }

                TrackingParamDefaultFactoryAttribute factoryAttr =
                    method.GetCustomAttribute<TrackingParamDefaultFactoryAttribute>();
                if (factoryAttr != null)
                {
                    ids.Add(factoryAttr.ParameterName);
                }
            }
        }

        #endregion

        #region Event Edit Popup

        private sealed class EventEditPopup : PopupWindowContent
        {
            private readonly ScriptableObject _eventAsset;
            private readonly SerializedProperty _eventNameProp;
            private readonly SerializedProperty _paramsProp;
            private readonly ScriptableObject _manager;
            private readonly SerializedObject _serializedManager;
            private readonly SerializedProperty _eventsProp;
            private readonly int _index;

            private string _assetName;
            private string _eventName;
            private string _popupSearchText = "";
            private List<ParamEntry> _availableParams;
            private HashSet<string> _selectedIds;
            private Vector2 _scroll;

            public EventEditPopup(
                ScriptableObject eventAsset,
                SerializedProperty eventNameProp,
                SerializedProperty paramsProp,
                ScriptableObject manager,
                SerializedObject serializedManager,
                SerializedProperty eventsProp,
                int index)
            {
                _eventAsset = eventAsset;
                _eventNameProp = eventNameProp;
                _paramsProp = paramsProp;
                _manager = manager;
                _serializedManager = serializedManager;
                _eventsProp = eventsProp;
                _index = index;

                _assetName = eventAsset.name;
                _eventName = eventNameProp?.stringValue ?? "";

                _availableParams = CollectAvailableParamNamesFromProviders(manager);
                _selectedIds = new HashSet<string>();
                if (paramsProp != null && paramsProp.isArray)
                {
                    for (int i = 0; i < paramsProp.arraySize; i++)
                    {
                        SerializedProperty element =
                            paramsProp.GetArrayElementAtIndex(i);
                        string id = element.FindPropertyRelative("_parameterId")?.stringValue;
                        string name = element.FindPropertyRelative("_name")?.stringValue;

                        // Backward compat: if no id, use name as the key
                        string key = !string.IsNullOrEmpty(id) ? id : name;
                        if (!string.IsNullOrEmpty(key))
                            _selectedIds.Add(key);
                    }
                }
            }

            public override Vector2 GetWindowSize()
            {
                float rows = 4f // header + asset name + event name + separator
                    + _availableParams.Count;
                float h = 28f + rows * 26f + 8f;
                return new Vector2(390f, Mathf.Min(h, 420f));
            }

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField("Edit Event", EditorStyles.boldLabel);
                GUILayout.Space(4);

                // â”€â”€ Asset Name â”€â”€
                _assetName = EditorGUILayout.TextField("Asset Name", _assetName);

                // â”€â”€ Event Name â”€â”€
                _eventName = EditorGUILayout.TextField("Event Name", _eventName);

                GUILayout.Space(4);
                Rect div = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(div, new Color(0.5f, 0.5f, 0.5f, 0.3f));
                GUILayout.Space(4);

                // â”€â”€ Parameters â”€â”€
                EditorGUILayout.LabelField(
                    $"Parameters ({_selectedIds.Count}/{_availableParams.Count})",
                    EditorStyles.boldLabel);

                if (_availableParams.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No parameters available from enabled providers.",
                        MessageType.Info);
                }
                else
                {
                    _popupSearchText = EditorGUILayout.TextField(
                        _popupSearchText,
                        EditorStyles.toolbarSearchField);

                    GUILayout.Space(2);

                    string search = _popupSearchText.Trim();
                    var filteredParams = string.IsNullOrEmpty(search)
                        ? _availableParams
                        : _availableParams.Where(p =>
                            p.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                            || p.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                        ).ToList();

                    _scroll = EditorGUILayout.BeginScrollView(_scroll);

                    for (int i = filteredParams.Count - 1; i >= 0; i--)
                    {
                        ParamEntry entry = filteredParams[i];
                        bool isSelected = _selectedIds.Contains(entry.Id);

                        string display = entry.Id == entry.Name
                            ? entry.Name
                            : $"{entry.Name} [{entry.Id}]";

                        string typeLabel = entry.ReturnType;

                        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                        {
                            bool newSelected = EditorGUILayout.Toggle(
                                isSelected, GUILayout.Width(20));

                            GUIContent labelContent = string.IsNullOrEmpty(entry.Description)
                                ? new GUIContent(display)
                                : new GUIContent(display, entry.Description);
                            EditorGUILayout.LabelField(
                                labelContent, EditorStyles.boldLabel);

                            GUILayout.FlexibleSpace();
                            if (!string.IsNullOrEmpty(typeLabel))
                            {
                                EditorGUILayout.LabelField(
                                    typeLabel,
                                    EditorStyles.miniLabel,
                                    GUILayout.Width(60));
                            }

                            if (newSelected != isSelected)
                            {
                                if (newSelected)
                                    _selectedIds.Add(entry.Id);
                                else
                                    _selectedIds.Remove(entry.Id);
                            }
                        }
                    }

                    EditorGUILayout.EndScrollView();
                }

                GUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Done", GUILayout.Height(24)))
                    {
                        CommitChanges();
                        editorWindow.Close();
                    }

                    if (GUILayout.Button("Cancel", GUILayout.Height(24)))
                    {
                        editorWindow.Close();
                    }
                }
            }

            private void CommitChanges()
            {
                // â”€â”€ Commit asset name â”€â”€
                if (!string.IsNullOrEmpty(_assetName) && _assetName != _eventAsset.name)
                {
                    _eventAsset.name = _assetName;
                    EditorUtility.SetDirty(_eventAsset);
                    AssetDatabase.SaveAssets();
                }

                // â”€â”€ Commit event name â”€â”€
                if (_eventName != (_eventNameProp?.stringValue ?? ""))
                {
                    _eventNameProp.stringValue = _eventName;
                    _eventNameProp.serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_eventAsset);
                }

                // â”€â”€ Commit parameters: rebuild the array from _selectedIds â”€â”€
                if (_paramsProp != null && _paramsProp.isArray)
                {
                    // Remove all existing parameters
                    while (_paramsProp.arraySize > 0)
                        _paramsProp.DeleteArrayElementAtIndex(_paramsProp.arraySize - 1);

                    // Add selected parameters
                    foreach (ParamEntry entry in _availableParams)
                    {
                        if (!_selectedIds.Contains(entry.Id)) continue;

                        int newIdx = _paramsProp.arraySize++;
                        SerializedProperty newParam = _paramsProp.GetArrayElementAtIndex(newIdx);

                        SerializedProperty idProp = newParam.FindPropertyRelative("_parameterId");
                        if (idProp != null)
                            idProp.stringValue = entry.Id;

                        SerializedProperty nameProp = newParam.FindPropertyRelative("_name");
                        if (nameProp != null)
                            nameProp.stringValue = entry.Name;
                    }

                    _paramsProp.serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_eventAsset);
                }
            }
        }

        #endregion

        #region Templated Events Drawing

        private static void DrawNewTemplatedEventButton(
            ScriptableObject manager,
            SerializedObject serializedManager,
            SerializedProperty eventsProp)
        {
            GUILayout.Space(4);
            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(dividerRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                _pendingEventToAdd = EditorGUILayout.ObjectField(
                    "Add Event",
                    _pendingEventToAdd,
                    typeof(TemplatedTrackingEvent),
                    false
                ) as TemplatedTrackingEvent;

                if (_pendingEventToAdd != null && GUILayout.Button("+", GUILayout.Width(24)))
                {
                    int idx = eventsProp.arraySize++;
                    eventsProp.GetArrayElementAtIndex(idx).objectReferenceValue =
                        _pendingEventToAdd;
                    serializedManager.ApplyModifiedProperties();
                    EditorUtility.SetDirty(manager);
                    AssetDatabase.SaveAssets();
                    _pendingEventToAdd = null;
                }
            }

            GUILayout.Space(4);

            if (GUILayout.Button("New Templated Event", GUILayout.Height(25)))
            {
                TemplatedTrackingEvent newEvent =
                    ScriptableObject.CreateInstance<TemplatedTrackingEvent>();
                newEvent.name = "NewTemplatedEvent";

                AssetDatabase.AddObjectToAsset(newEvent, manager);

                int idx = eventsProp.arraySize++;
                eventsProp.GetArrayElementAtIndex(idx).objectReferenceValue =
                    newEvent;
                serializedManager.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);

                AssetDatabase.SaveAssets();

                EditorGUIUtility.PingObject(newEvent);
                Selection.activeObject = newEvent;
            }
        }

        #endregion

        #region Editor Helpers

        private static bool DrawSmallDeleteButton()
        {
            GUIContent content = EditorGUIUtility.IconContent("TreeEditor.Trash");
            if (content == null || content.image == null)
            {
                content = new GUIContent("\u2717", "Delete");
            }
            else
            {
                content.tooltip = "Delete";
            }

            return GUILayout.Button(
                content,
                EditorStyles.miniButton,
                GUILayout.Width(24),
                GUILayout.Height(18));
        }

        #endregion
    }

    public class InAppPurchaseIntegrationSettingsProvider : BaseIntegrationSettingsProvider<IInAppPurchaseManager>
    {
        private static readonly SettingsTab[] Tabs =
        {
            new SettingsTab(
                "Providers",
                new ProviderDescriptor[]
                {
                    new ProviderDescriptor(
                        "Unity IAP",
                        "provider",
                        ProviderBindingMode.Single,
                        "Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.UnityInAppPurchaseProvider",
                        new[] { "UNITY_PURCHASING" },
                        new[] { "UnityEngine.Purchasing.StandardPurchasingModule" },
                        customProviderBaseTypeName: "Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.IInAppPurchaseProvider"
                    ),
                    new ProviderDescriptor(
                        "Pseudo Provider",
                        "provider",
                        ProviderBindingMode.Single,
                        "Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.PseudoInAppPurchaseProvider",
                        customProviderBaseTypeName: "Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.IInAppPurchaseProvider"
                    )
                }
            )
        };

        private InAppPurchaseIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, "In-App Purchase Manager Configuration",
            "In-App Purchase Manager Asset",
            Tabs, keywords)
        { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new InAppPurchaseIntegrationSettingsProvider(
                "Project/Integration/In-App Purchase", SettingsScope.Project);
        }
    }

    public class RemoteConfigIntegrationSettingsProvider : BaseIntegrationSettingsProvider<IRemoteConfigManager>
    {
        private static readonly SettingsTab[] Tabs =
        {
            new SettingsTab(
                "Providers",
                new ProviderDescriptor[]
                {
                    new ProviderDescriptor(
                        "Firebase Remote Config",
                        "initialProviders",
                        ProviderBindingMode.Collection,
                        "Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig.FirebaseRemoteConfigProvider",
                        new[] { "FIREBASE_REMOTE" },
                        new[] { "Firebase.RemoteConfig.FirebaseRemoteConfig" },
                        customProviderBaseTypeName: "Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig.IRemoteConfigProvider"
                    )
                }
            )
        };

        private RemoteConfigIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, "Remote Config Manager Configuration",
            "Remote Config Manager Asset",
            Tabs, keywords)
        { }

        protected override void DrawExtraContent(ScriptableObject manager)
        {
            if (manager is not IRemoteConfigManager rcManager) return;

            GUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Registered Config Properties", EditorStyles.boldLabel);

                if (Application.isPlaying && rcManager.Status == RemoteConfigStatus.Ready)
                {
                    if (GUILayout.Button("Clear Cache", GUILayout.Width(90)))
                    {
                        ClearRemoteConfigCache(manager);
                    }
                }
            }

            Type configType = rcManager.RemoteConfigType;
            object configData = rcManager.Config;

            if (configType == null) return;

            bool isPlaying = Application.isPlaying;
            PropertyInfo[] properties = configType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            bool hasAny = false;

            foreach (var prop in properties)
            {
                var attr = prop.GetCustomAttribute<RemoteConfigAttribute>();
                if (attr == null) continue;
                hasAny = true;

                object currentValue = configData != null ? prop.GetValue(configData) : null;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(attr.Key, EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("Property", $"{prop.Name} ({prop.PropertyType.Name})");
                    EditorGUILayout.LabelField("Cached Value", currentValue?.ToString() ?? "null");

                    if (isPlaying)
                    {
                        string runtimeValueStr = GetRuntimeConfigValue(rcManager, attr.Key, prop.PropertyType);
                        EditorGUILayout.LabelField("Runtime Value", runtimeValueStr);
                    }

                    if (attr.ParserModule != null)
                        EditorGUILayout.LabelField("Parser", attr.ParserModule.Name);
                    if (attr.DefaultValue != null)
                        EditorGUILayout.LabelField("Default", attr.DefaultValue.ToString());
                }
                EditorGUILayout.EndVertical();
            }

            if (!hasAny)
            {
                EditorGUILayout.HelpBox(
                    "No RemoteConfigAttribute-decorated properties found in the config data type.",
                    MessageType.Info);
            }
        }

        private static string GetRuntimeConfigValue(IRemoteConfigManager rcManager, string key, Type propertyType)
        {
            foreach (var provider in rcManager.Providers)
            {
                var tryGetConfigMethod = typeof(IRemoteConfigProvider).GetMethod("TryGetConfig");
                var genericMethod = tryGetConfigMethod.MakeGenericMethod(propertyType);
                object[] parameters = new object[] { key, null };
                bool success = (bool)genericMethod.Invoke(provider, parameters);
                if (success)
                {
                    return parameters[1]?.ToString() ?? "null";
                }
            }
            return "not available";
        }

        private static void ClearRemoteConfigCache(ScriptableObject manager)
        {
            var configDataField = manager.GetType().BaseType?.GetField("_configData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (configDataField == null) return;

            var remoteConfigType = ((IRemoteConfigManager)manager).RemoteConfigType;
            var newConfigData = Activator.CreateInstance(remoteConfigType);
            configDataField.SetValue(manager, newConfigData);
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new RemoteConfigIntegrationSettingsProvider(
                "Project/Integration/Remote Config", SettingsScope.Project);
        }
    }

    public class InAppReviewIntegrationSettingsProvider : BaseIntegrationSettingsProvider<IInAppReviewManager>
    {
        private static readonly SettingsTab[] Tabs =
        {
            new SettingsTab(
                "Providers",
                new ProviderDescriptor[]
                {
                    new ProviderDescriptor(
                        "Open Store",
                        "reviewProvider",
                        ProviderBindingMode.Single,
                        "Com.Hapiga.Scheherazade.Common.Integration.IAR.OpenStoreInAppReviewModule",
                        customProviderBaseTypeName: "Com.Hapiga.Scheherazade.Common.Integration.IAR.IInAppReviewModule"
                    )
                }
            )
        };

        private InAppReviewIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, "In-App Review Manager Configuration",
            "In-App Review Manager Asset",
            Tabs, keywords)
        { }

        protected override void DrawExtraContent(ScriptableObject manager)
        {
            if (manager == null) return;

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Google Play Review Module", EditorStyles.boldLabel);
            DrawGooglePlayModuleStatus();
        }

        private static void DrawGooglePlayModuleStatus()
        {
            Type moduleType = IntegrationSettingsDrawingUtils.ResolveType(
                "Com.Hapiga.Scheherazade.Common.Integration.IAR.GooglePlayInAppReviewModule");
            string[] missingDefines = IntegrationSettingsDrawingUtils.GetMissingDefines(
                new[] { "GOOGLEPLAY_REVIEW" });

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (moduleType != null && missingDefines.Length == 0)
                {
                    EditorGUILayout.HelpBox(
                        "Google Play In-App Review module is available and configured.",
                        MessageType.Info);
                }
                else
                {
                    string message = "Google Play In-App Review module is not available.";
                    if (missingDefines.Length > 0)
                        message += $"\n\nMissing scripting define: {string.Join(", ", missingDefines)}";
                    else
                        message += "\n\nThe GooglePlayInAppReviewModule type is not compiled. Ensure the Google Play Review plugin is installed.";

                    EditorGUILayout.HelpBox(message, MessageType.Warning);

                    if (missingDefines.Length > 0 && GUILayout.Button("Enable GOOGLEPLAY_REVIEW Define"))
                    {
                        IntegrationSettingsDrawingUtils.EnsureScriptingDefines(
                            new[] { "GOOGLEPLAY_REVIEW" });
                    }
                }
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new InAppReviewIntegrationSettingsProvider(
                "Project/Integration/In-App Review", SettingsScope.Project);
        }
    }

    public class UserSegmentationIntegrationSettingsProvider : BaseIntegrationSettingsProvider<IUserSegmentation>
    {
        private static readonly SettingsTab[] Tabs =
        {
            new SettingsTab(
                "Providers",
                new ProviderDescriptor[]
                {
                    new ProviderDescriptor(
                        "Cached",
                        "initialProviders",
                        ProviderBindingMode.Collection,
                        "Com.Hapiga.Scheherazade.Common.Integration.Segmentation.CachedSegmentationProvider",
                        customProviderBaseTypeName: "Com.Hapiga.Scheherazade.Common.Integration.Segmentation.IUserSegmentationProvider"
                    ),
                    new ProviderDescriptor(
                        "Adjust",
                        "initialProviders",
                        ProviderBindingMode.Collection,
                        "Com.Hapiga.Scheherazade.Common.Integration.Segmentation.AdjustSegmentationProvider",
                        new[] { "TRACKING_ADJUST" },
                        new[] { "AdjustSdk.Adjust" },
                        customProviderBaseTypeName: "Com.Hapiga.Scheherazade.Common.Integration.Segmentation.IUserSegmentationProvider"
                    )
                }
            ),
            new SettingsTab(
                "Trackers",
                new ProviderDescriptor[]
                {
                    new ProviderDescriptor(
                        "Firebase Analytics",
                        "initialTrackers",
                        ProviderBindingMode.Collection,
                        "Com.Hapiga.Scheherazade.Common.Integration.Segmentation.FirebaseUserSegmentationTracker",
                        new[] { "FIREBASE_ANALYTICS" },
                        new[] { "Firebase.Analytics.FirebaseAnalytics" },
                        customProviderBaseTypeName: "Com.Hapiga.Scheherazade.Common.Integration.Segmentation.IUserSegmentationTracker"
                    )
                }
            )
        };

        private UserSegmentationIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, "User Segmentation Manager Configuration",
            "User Segmentation Manager Asset",
            Tabs, keywords)
        { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new UserSegmentationIntegrationSettingsProvider(
                "Project/Integration/User Segmentation", SettingsScope.Project);
        }
    }

    internal enum ProviderBindingMode
    {
        Single,
        Collection
    }

    [Serializable]
    internal sealed class ProviderDescriptor
    {
        public string DisplayName { get; }
        public string ManagerFieldName { get; }
        public ProviderBindingMode BindingMode { get; }
        public string ProviderTypeName { get; }
        public string[] RequiredDefines { get; }
        public string[] DependencyTypeNames { get; }
        public int FeatureFlags { get; }
        public string CustomProviderBaseTypeName { get; }

        public ProviderDescriptor(
            string displayName,
            string managerFieldName,
            ProviderBindingMode bindingMode,
            string providerTypeName,
            string[] requiredDefines = null,
            string[] dependencyTypeNames = null,
            int featureFlags = 0,
            string customProviderBaseTypeName = null
        )
        {
            DisplayName = displayName;
            ManagerFieldName = managerFieldName;
            BindingMode = bindingMode;
            ProviderTypeName = providerTypeName;
            RequiredDefines = requiredDefines ?? Array.Empty<string>();
            DependencyTypeNames = dependencyTypeNames ?? Array.Empty<string>();
            FeatureFlags = featureFlags;
            CustomProviderBaseTypeName = customProviderBaseTypeName;
        }
    }

    [Serializable]
    internal sealed class SettingsTab
    {
        public string Name { get; }
        public ProviderDescriptor[] Descriptors { get; }
        public Action<ScriptableObject> CustomRenderer { get; }

        public SettingsTab(
            string name,
            ProviderDescriptor[] descriptors = null,
            Action<ScriptableObject> customRenderer = null
        )
        {
            Name = name;
            Descriptors = descriptors ?? Array.Empty<ProviderDescriptor>();
            CustomRenderer = customRenderer;
        }
    }

    [Serializable]
    internal sealed class PendingProviderCreationRequest
    {
        public string managerAssetGuid;
        public string managerFieldName;
        public ProviderBindingMode bindingMode;
        public string providerTypeName;
        public string providerDisplayName;
    }

    [Serializable]
    internal sealed class PendingProviderCreationState
    {
        public List<PendingProviderCreationRequest> requests = new List<PendingProviderCreationRequest>();
    }

    internal static class IntegrationSettingsDrawingUtils
    {
        private const string PendingProviderCreationStateKey = "Com.Hapiga.Scheherazade.Integration.PendingProviderCreationState";

        private const string IntegrationCentreResourcePath = "IntegrationCentre";
        private const string IntegrationCentreAssetPath = "Assets/Resources/IntegrationCentre.asset";
        private const string IntegrationProviderAssetsFolder = "Assets/Resources/Integration";
        private static readonly Dictionary<string, Type> ResolvedTypesByName = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly HashSet<string> MissingTypeNames = new HashSet<string>(StringComparer.Ordinal);
        private static Type[] _allTypesCache;

        public static IntegrationCentre GetOrCreateIntegrationCentre()
        {
            var centre = IntegrationCentre.Instance;
            if (centre == null)
            {
                centre = AssetDatabase.LoadAssetAtPath<IntegrationCentre>(IntegrationCentreAssetPath);
                if (centre == null)
                {
                    centre = ScriptableObject.CreateInstance<IntegrationCentre>();
                    if (!Directory.Exists("Assets/Resources"))
                    {
                        Directory.CreateDirectory("Assets/Resources");
                    }
                    AssetDatabase.CreateAsset(centre, IntegrationCentreAssetPath);
                    AssetDatabase.SaveAssets();
                }
            }
            return centre;
        }

        public static ScriptableObject FindModuleAsset<TInterface>(IntegrationCentre centre)
            where TInterface : class
        {
            SerializedObject serializedCentre = new SerializedObject(centre);
            var listProp = serializedCentre.FindProperty("moduleScriptableObjects");
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var value = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (value is TInterface)
                {
                    return value as ScriptableObject;
                }
            }

            return null;
        }

        internal static Type[] FindConcreteManagerTypes<TInterface>()
            where TInterface : class
        {
            var interfaceType = typeof(TInterface);
            return GetAllTypes()
                .Where(t => t.IsClass
                            && !t.IsAbstract
                            && typeof(ScriptableObject).IsAssignableFrom(t)
                            && interfaceType.IsAssignableFrom(t))
                .OrderBy(t => t.FullName)
                .ToArray();
        }

        public static void DrawManagerSettings<TInterface>(
            string sectionName,
            string label,
            Type[] concreteTypes,
            ref ScriptableObject currentManager,
            IntegrationCentre centre,
            IReadOnlyList<SettingsTab> tabs,
            ref int selectedTabIndex,
            ref int featureFilterValue,
            Type filterFlagsEnumType = null
        ) where TInterface : class
        {
            if (concreteTypes.Length > 1)
            {
                GUILayout.Space(10);
                var typeList = string.Join("\n",
                    concreteTypes.Select(t => $"  \u2022 {t.FullName}"));
                EditorGUILayout.HelpBox(
                    $"Multiple concrete ScriptableObject types implementing {typeof(TInterface).Name} were found:\n\n{typeList}\n\n" +
                    "Only one implementation should exist. Remove or abstract the extra types.",
                    MessageType.Error
                );
                return;
            }

            if (concreteTypes.Length == 0)
            {
                GUILayout.Space(10);
                EditorGUILayout.LabelField(sectionName, EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    $"No concrete ScriptableObject class implementing {typeof(TInterface).Name} was found.\n\n" +
                    "Create a new ScriptableObject class that inherits from the appropriate manager base class " +
                    "(e.g., public sealed class MyManager : TrackingManagerBase<MyManager> {{ }}).",
                    MessageType.Warning
                );

                string defaultClassName = typeof(TInterface).Name.StartsWith("I")
                    ? typeof(TInterface).Name.Substring(1)
                    : typeof(TInterface).Name + "Manager";

                if (GUILayout.Button("Enable Manager", GUILayout.Height(30)))
                {
                    ScriptTemplateGenerator.CreateManagerScript(
                        defaultClassName,
                        "Assets/",
                        typeof(TInterface));
                }

                return;
            }

            Type concreteType = concreteTypes[0];

            GUILayout.Space(10);
            EditorGUILayout.LabelField(sectionName, EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            ScriptableObject newManager = EditorGUILayout.ObjectField(
                label,
                currentManager,
                concreteType,
                false
            ) as ScriptableObject;

            if (EditorGUI.EndChangeCheck())
            {
                SetModuleAsset(centre, currentManager, newManager);
                currentManager = newManager;
            }

            if (currentManager == null)
            {
                EditorGUILayout.HelpBox(
                    $"No {label} asset selected. Please create and assign a {concreteType.Name} asset.",
                    MessageType.Info
                );

                if (GUILayout.Button($"Create New {label} Asset"))
                {
                    string path = EditorUtility.SaveFilePanelInProject(
                        $"Create {label}",
                        concreteType.Name,
                        "asset",
                        "Save Manager Asset"
                    );

                    if (string.IsNullOrEmpty(path))
                    {
                        return;
                    }

                    ScriptableObject newAsset = ScriptableObject.CreateInstance(concreteType);
                    AssetDatabase.CreateAsset(newAsset, path);
                    AssetDatabase.SaveAssets();
                    SetModuleAsset(centre, currentManager, newAsset);
                    currentManager = newAsset;
                }

                return;
            }

            if (tabs == null || tabs.Count == 0)
            {
                DrawInlineInspector(
                    currentManager,
                    $"{currentManager.name.ToUpperInvariant()} CONFIGURATION"
                );
                return;
            }

            string[] tabNames = new string[tabs.Count + 1];
            tabNames[0] = "Manager";
            for (int i = 0; i < tabs.Count; i++)
            {
                tabNames[i + 1] = tabs[i].Name;
            }

            GUILayout.Space(6);
            selectedTabIndex = GUILayout.Toolbar(selectedTabIndex, tabNames);
            GUILayout.Space(4);

            if (selectedTabIndex == 0)
            {
                DrawInlineInspector(
                    currentManager,
                    $"{currentManager.name.ToUpperInvariant()} CONFIGURATION"
                );
            }
            else
            {
                SettingsTab activeTab = tabs[selectedTabIndex - 1];

                if (activeTab.CustomRenderer != null)
                {
                    activeTab.CustomRenderer(currentManager);
                    return;
                }

                EditorGUILayout.LabelField(activeTab.Name, EditorStyles.boldLabel);

                // Render feature filter bar if any descriptor has feature flags
                if (filterFlagsEnumType != null &&
                    activeTab.Descriptors != null &&
                    activeTab.Descriptors.Any(d => d.FeatureFlags != 0))
                {
                    featureFilterValue = EditorGuiLayout.DrawFeatureFilterBar(
                        filterFlagsEnumType, featureFilterValue);
                }

                if (activeTab.Descriptors != null && activeTab.Descriptors.Length > 0)
                {
                    foreach (var descriptor in activeTab.Descriptors)
                    {
                        DrawProviderSection(currentManager, descriptor, featureFilterValue);
                    }

                    DrawNewCustomProviderButton(activeTab.Descriptors);
                }
            }
        }

        public static void ProcessPendingProviderCreations()
        {
            var state = GetPendingProviderCreationState();
            if (state.requests.Count == 0)
            {
                return;
            }

            bool changed = false;

            for (int i = state.requests.Count - 1; i >= 0; i--)
            {
                if (TryProcessPendingProviderCreation(state.requests[i]))
                {
                    state.requests.RemoveAt(i);
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            SavePendingProviderCreationState(state);
        }

        [DidReloadScripts]
        private static void HandleScriptsReloaded()
        {
            ProcessPendingProviderCreations();
        }

        internal static void SetModuleAsset(
            IntegrationCentre centre,
            ScriptableObject currentManager,
            ScriptableObject newManager
        )
        {
            SerializedObject serializedCentre = new SerializedObject(centre);
            var listProp = serializedCentre.FindProperty("moduleScriptableObjects");

            int foundIndex = -1;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                if (listProp.GetArrayElementAtIndex(i).objectReferenceValue == currentManager)
                {
                    foundIndex = i;
                    break;
                }
            }

            if (newManager != null)
            {
                if (foundIndex >= 0)
                {
                    listProp.GetArrayElementAtIndex(foundIndex).objectReferenceValue = newManager;
                }
                else
                {
                    int newIndex = listProp.arraySize++;
                    listProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = newManager;
                }
            }
            else if (foundIndex >= 0)
            {
                DeleteArrayElement(listProp, foundIndex);
            }

            serializedCentre.ApplyModifiedProperties();
            EditorUtility.SetDirty(centre);
            AssetDatabase.SaveAssets();
        }

        internal static void DrawProviderSection(
            ScriptableObject manager,
            ProviderDescriptor descriptor,
            int featureFilter = -1
        )
        {
            // Feature filter guard: skip providers that don't support the selected feature
            if (featureFilter > 0 && (descriptor.FeatureFlags & featureFilter) == 0)
                return;

            GUILayout.Space(4);

            SerializedObject serializedManager = new SerializedObject(manager);
            var fieldProp = serializedManager.FindProperty(descriptor.ManagerFieldName);
            if (fieldProp == null)
            {
                EditorGUILayout.HelpBox(
                    $"Could not find manager field '{descriptor.ManagerFieldName}' on {manager.GetType().Name}.",
                    MessageType.Error
                );
                return;
            }

            var providerType = ResolveType(descriptor.ProviderTypeName);
            var currentProvider = GetAssignedProvider(fieldProp, descriptor, providerType);
            bool isEnabled = currentProvider != null;

            bool dependenciesSatisfied = AreDependenciesSatisfied(descriptor);
            string[] missingDefines = GetMissingDefines(descriptor.RequiredDefines);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Header row
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        descriptor.DisplayName,
                        EditorStyles.boldLabel
                    );

                    GUILayout.FlexibleSpace();

                    if (isEnabled && currentProvider != null)
                    {
                        if (GUILayout.Button("Disable", GUILayout.Width(70)))
                        {
                            SetProviderAssignment(
                                fieldProp,
                                descriptor,
                                providerType,
                                null
                            );

                            serializedManager.ApplyModifiedProperties();
                            EditorUtility.SetDirty(manager);
                            AssetDatabase.SaveAssets();
                            isEnabled = false;
                            currentProvider = null;
                        }
                    }
                    else if (dependenciesSatisfied
                        && providerType != null
                        && missingDefines.Length == 0)
                    {
                        if (GUILayout.Button("Enable", GUILayout.Width(70)))
                        {
                            EnableProvider(
                                manager,
                                descriptor,
                                providerType,
                                fieldProp,
                                serializedManager
                            );

                            currentProvider = GetAssignedProvider(
                                fieldProp,
                                descriptor,
                                providerType
                            );

                            isEnabled = currentProvider != null;
                        }
                    }
                }

                // Locked state message
                if (!isEnabled && missingDefines.Length > 0)
                {
                    EditorGUILayout.LabelField(
                        $"Missing defines: {string.Join(", ", missingDefines)}. "
                        + "Enable the required scripting define to unlock this provider.",
                        EditorStyles.miniLabel
                    );

                    Color oldColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.9f, 0.7f, 0.3f);

                    if (GUILayout.Button(
                            "Enable " + string.Join(", ", missingDefines)))
                    {
                        EnsureScriptingDefines(descriptor.RequiredDefines);
                    }

                    GUI.backgroundColor = oldColor;
                }
                else if (!isEnabled && !dependenciesSatisfied)
                {
                    EditorGUILayout.LabelField(
                        "Dependencies not satisfied. "
                        + "Install the required packages to unlock this provider.",
                        EditorStyles.miniLabel
                    );
                }

                // Configuration (when enabled)
                if (isEnabled && currentProvider != null)
                {
                    GUILayout.Space(4);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            "Configuration of " + descriptor.DisplayName,
                            EditorStyles.miniBoldLabel
                        );

                        GUILayout.FlexibleSpace();

                        if (DrawDeleteButton())
                        {
                            DeleteProviderAsset(
                                manager,
                                descriptor,
                                providerType,
                                currentProvider
                            );
                        }
                    }

                    GUILayout.Space(2);
                    DrawInlineInspector(
                        currentProvider,
                        $"{descriptor.DisplayName.ToUpperInvariant()} PROVIDER"
                    );
                }
            }
        }

        private static void EnableProvider(
            ScriptableObject manager,
            ProviderDescriptor descriptor,
            Type providerType,
            SerializedProperty fieldProp,
            SerializedObject serializedManager
        )
        {
            ScriptableObject asset = FindOrCreateProviderAsset(
                providerType,
                descriptor.ProviderTypeName
            );

            if (asset == null)
            {
                return;
            }

            SetProviderAssignment(
                fieldProp,
                descriptor,
                providerType,
                asset
            );

            serializedManager.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);
        }

        private static ScriptableObject FindOrCreateProviderAsset(
            Type providerType,
            string defaultName)
        {
            // Search existing
            string[] guids = AssetDatabase.FindAssets(
                "t:" + providerType.Name
            );

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableObject asset = AssetDatabase.LoadAssetAtPath(
                    path,
                    providerType
                ) as ScriptableObject;

                if (asset != null)
                {
                    return asset;
                }
            }

            // Create new
            EnsureDirectoryExists(IntegrationProviderAssetsFolder);

            string shortName = defaultName.Contains('.')
                ? defaultName.Substring(defaultName.LastIndexOf('.') + 1)
                : defaultName;

            string assetPath = IntegrationProviderAssetsFolder
                + "/"
                + shortName
                + ".asset";

            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            ScriptableObject newAsset = ScriptableObject
                .CreateInstance(providerType);

            newAsset.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(newAsset, assetPath);
            AssetDatabase.SaveAssets();

            return newAsset;
        }

        private static bool AreDependenciesSatisfied(
            ProviderDescriptor descriptor)
        {
            if (descriptor.DependencyTypeNames == null
                || descriptor.DependencyTypeNames.Length == 0)
            {
                return true;
            }

            return descriptor.DependencyTypeNames.All(
                typeName => ResolveType(typeName) != null
            );
        }

        private static bool DrawDeleteButton()
        {
            var content = EditorGUIUtility.IconContent("TreeEditor.Trash");
            if (content == null || content.image == null)
            {
                content = new GUIContent(
                    "\u2717",
                    "Delete the provider asset from disk."
                );
            }
            else
            {
                content.tooltip = "Delete the provider asset from disk.";
            }

            return GUILayout.Button(
                content,
                EditorStyles.miniButton,
                GUILayout.Width(24),
                GUILayout.Height(18)
            );
        }

        private static ScriptableObject GetAssignedProvider(
            SerializedProperty fieldProp,
            ProviderDescriptor descriptor,
            Type providerType
        )
        {
            if (descriptor.BindingMode == ProviderBindingMode.Single)
            {
                ScriptableObject assignedProvider = fieldProp.objectReferenceValue as ScriptableObject;
                if (assignedProvider == null)
                {
                    return null;
                }

                if (providerType != null)
                {
                    return providerType.IsInstanceOfType(assignedProvider)
                        ? assignedProvider
                        : null;
                }

                return assignedProvider.GetType().FullName == descriptor.ProviderTypeName
                    ? assignedProvider
                    : null;
            }

            for (int i = 0; i < fieldProp.arraySize; i++)
            {
                ScriptableObject value = fieldProp.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableObject;
                if (value == null)
                {
                    continue;
                }

                if (providerType != null)
                {
                    if (providerType.IsInstanceOfType(value))
                    {
                        return value;
                    }

                    continue;
                }

                if (value.GetType().FullName == descriptor.ProviderTypeName)
                {
                    return value;
                }
            }

            return null;
        }

        private static void SetProviderAssignment(
            SerializedProperty fieldProp,
            ProviderDescriptor descriptor,
            Type providerType,
            ScriptableObject nextProvider
        )
        {
            if (descriptor.BindingMode == ProviderBindingMode.Single)
            {
                fieldProp.objectReferenceValue = nextProvider;
                return;
            }

            int existingIndex = FindProviderIndex(fieldProp, descriptor, providerType);
            if (nextProvider != null)
            {
                if (existingIndex >= 0)
                {
                    fieldProp.GetArrayElementAtIndex(existingIndex).objectReferenceValue = nextProvider;
                }
                else
                {
                    int newIndex = fieldProp.arraySize++;
                    fieldProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = nextProvider;
                }

                return;
            }

            if (existingIndex >= 0)
            {
                DeleteArrayElement(fieldProp, existingIndex);
            }
        }

        private static int FindProviderIndex(
            SerializedProperty fieldProp,
            ProviderDescriptor descriptor,
            Type providerType
        )
        {
            if (descriptor.BindingMode == ProviderBindingMode.Single)
            {
                return fieldProp.objectReferenceValue != null ? 0 : -1;
            }

            for (int i = 0; i < fieldProp.arraySize; i++)
            {
                ScriptableObject value = fieldProp.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableObject;
                if (value == null)
                {
                    continue;
                }

                if (providerType != null)
                {
                    if (providerType.IsInstanceOfType(value))
                    {
                        return i;
                    }

                    continue;
                }

                if (value.GetType().FullName == descriptor.ProviderTypeName)
                {
                    return i;
                }
            }

            return -1;
        }

        internal static void DeleteArrayElement(SerializedProperty arrayProp, int index)
        {
            arrayProp.DeleteArrayElementAtIndex(index);
            if (index < arrayProp.arraySize &&
                arrayProp.GetArrayElementAtIndex(index).objectReferenceValue == null)
            {
                arrayProp.DeleteArrayElementAtIndex(index);
            }
        }

        internal static void DrawInlineInspector(ScriptableObject asset, string header)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                TryDrawingInlineInspector(asset, header);
            }
        }

        private static void TryDrawingInlineInspector(ScriptableObject asset, string header)
        {
            EditorGUILayout.LabelField(header, EditorStyles.miniBoldLabel);
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(asset);
            try
            {
#if ODIN_INSPECTOR
                if (editor is Sirenix.OdinInspector.Editor.OdinEditor odinEditor)
                {
                    editor.DrawDefaultInspector();
                }
#else
                editor.OnInspectorGUI();
#endif
            }
            finally
            {
                if (editor != null)
                {
                    UnityEngine.Object.DestroyImmediate(editor);
                }
            }
        }

        private static void CreateAndAssignProviderAsset(
            ScriptableObject manager,
            ProviderDescriptor descriptor,
            Type providerType
        )
        {
            string managerPath = AssetDatabase.GetAssetPath(manager);
            if (string.IsNullOrEmpty(managerPath))
            {
                EditorUtility.DisplayDialog(
                    "Manager Asset Required",
                    "Please save the manager asset before creating a provider asset in place.",
                    "OK"
                );
                return;
            }

            var existingProvider = LoadProviderAssetFromResources(descriptor, providerType);
            if (existingProvider != null)
            {
                AssignProvider(manager, descriptor, providerType, existingProvider);
                EditorGUIUtility.PingObject(existingProvider);
                Selection.activeObject = existingProvider;
                return;
            }

            EnsureDirectoryExists(IntegrationProviderAssetsFolder);

            string assetPath = GetProviderAssetPath(providerType);
            var conflictingAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (conflictingAsset != null)
            {
                EditorUtility.DisplayDialog(
                    "Provider Asset Conflict",
                    $"Could not create {descriptor.DisplayName} provider because '{assetPath}' already exists with a different asset type.",
                    "OK"
                );
                return;
            }

            ScriptableObject providerAsset = ScriptableObject.CreateInstance(providerType);
            providerAsset.name = providerType.Name;
            AssetDatabase.CreateAsset(providerAsset, assetPath);

            if (!AssignProvider(manager, descriptor, providerType, providerAsset))
            {
                UnityEngine.Object.DestroyImmediate(providerAsset);
                AssetDatabase.DeleteAsset(assetPath);
                return;
            }

            EditorGUIUtility.PingObject(providerAsset);
            Selection.activeObject = providerAsset;
        }

        private static bool DeleteProviderAsset(
            ScriptableObject manager,
            ProviderDescriptor descriptor,
            Type providerType,
            ScriptableObject providerAsset
        )
        {
            if (providerAsset == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(providerAsset);
            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog(
                    "Provider Delete Failed",
                    "The selected provider asset could not be located on disk.",
                    "OK"
                );
                return false;
            }

            bool deleteConfirmed = EditorUtility.DisplayDialog(
                "Delete Provider Asset",
                $"Delete '{providerAsset.name}' at '{assetPath}'?",
                "Delete",
                "Cancel"
            );
            if (!deleteConfirmed)
            {
                return false;
            }

            SerializedObject serializedManager = new SerializedObject(manager);
            var fieldProp = serializedManager.FindProperty(descriptor.ManagerFieldName);
            if (fieldProp != null && GetAssignedProvider(fieldProp, descriptor, providerType) == providerAsset)
            {
                SetProviderAssignment(fieldProp, descriptor, providerType, null);
                serializedManager.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);
            }

            RemovePendingProviderCreation(descriptor.ProviderTypeName);

            if (!AssetDatabase.DeleteAsset(assetPath))
            {
                EditorUtility.DisplayDialog(
                    "Provider Delete Failed",
                    $"Unity could not delete '{assetPath}'.",
                    "OK"
                );
                return false;
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        private static void EnqueuePendingProviderCreation(
            ScriptableObject manager,
            ProviderDescriptor descriptor
        )
        {
            string managerPath = AssetDatabase.GetAssetPath(manager);
            if (string.IsNullOrEmpty(managerPath))
            {
                return;
            }

            string managerAssetGuid = AssetDatabase.AssetPathToGUID(managerPath);
            if (string.IsNullOrEmpty(managerAssetGuid))
            {
                return;
            }

            var state = GetPendingProviderCreationState();
            bool alreadyQueued = state.requests.Any(request =>
                request.managerAssetGuid == managerAssetGuid &&
                request.managerFieldName == descriptor.ManagerFieldName &&
                request.providerTypeName == descriptor.ProviderTypeName
            );

            if (alreadyQueued)
            {
                return;
            }

            state.requests.Add(new PendingProviderCreationRequest
            {
                managerAssetGuid = managerAssetGuid,
                managerFieldName = descriptor.ManagerFieldName,
                bindingMode = descriptor.BindingMode,
                providerTypeName = descriptor.ProviderTypeName,
                providerDisplayName = descriptor.DisplayName
            });
            SavePendingProviderCreationState(state);
        }

        private static bool TryProcessPendingProviderCreation(PendingProviderCreationRequest request)
        {
            var providerType = ResolveType(request.providerTypeName);
            if (providerType == null || !typeof(ScriptableObject).IsAssignableFrom(providerType))
            {
                return false;
            }

            string managerPath = AssetDatabase.GUIDToAssetPath(request.managerAssetGuid);
            if (string.IsNullOrEmpty(managerPath))
            {
                return true;
            }

            var manager = AssetDatabase.LoadAssetAtPath<ScriptableObject>(managerPath);
            if (manager == null)
            {
                return true;
            }

            ProviderDescriptor descriptor = new ProviderDescriptor(
                request.providerDisplayName,
                request.managerFieldName,
                request.bindingMode,
                request.providerTypeName
            );

            SerializedObject serializedManager = new SerializedObject(manager);
            var fieldProp = serializedManager.FindProperty(request.managerFieldName);
            if (fieldProp == null)
            {
                return true;
            }

            if (GetAssignedProvider(fieldProp, descriptor, providerType) != null)
            {
                return true;
            }

            var providerAsset = LoadProviderAssetFromResources(descriptor, providerType);
            if (providerAsset != null)
            {
                SetProviderAssignment(fieldProp, descriptor, providerType, providerAsset);
                serializedManager.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);
                AssetDatabase.SaveAssets();
                return true;
            }

            CreateAndAssignProviderAsset(manager, descriptor, providerType);
            return true;
        }

        private static PendingProviderCreationState GetPendingProviderCreationState()
        {
            string json = SessionState.GetString(PendingProviderCreationStateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new PendingProviderCreationState();
            }

            var state = JsonUtility.FromJson<PendingProviderCreationState>(json);
            return state ?? new PendingProviderCreationState();
        }

        private static void SavePendingProviderCreationState(PendingProviderCreationState state)
        {
            if (state.requests.Count == 0)
            {
                SessionState.SetString(PendingProviderCreationStateKey, string.Empty);
                return;
            }

            SessionState.SetString(
                PendingProviderCreationStateKey,
                JsonUtility.ToJson(state)
            );
        }

        private static void RemovePendingProviderCreation(string providerTypeName)
        {
            if (string.IsNullOrEmpty(providerTypeName))
            {
                return;
            }

            var state = GetPendingProviderCreationState();
            int removedCount = state.requests.RemoveAll(request =>
                request.providerTypeName == providerTypeName
            );

            if (removedCount > 0)
            {
                SavePendingProviderCreationState(state);
            }
        }

        internal static string[] GetMissingDefines(IReadOnlyList<string> requiredDefines)
        {
            if (requiredDefines == null || requiredDefines.Count == 0)
            {
                return Array.Empty<string>();
            }

            var currentDefines = GetCurrentScriptingDefines();

            return requiredDefines
                .Where(requiredDefine => !currentDefines.Contains(requiredDefine))
                .ToArray();
        }

        private static string[] GetEnabledDefines(IReadOnlyList<string> requiredDefines)
        {
            if (requiredDefines == null || requiredDefines.Count == 0)
            {
                return Array.Empty<string>();
            }

            var currentDefines = GetCurrentScriptingDefines();

            return requiredDefines
                .Where(requiredDefine => currentDefines.Contains(requiredDefine))
                .ToArray();
        }

        internal static void EnsureScriptingDefines(IEnumerable<string> requiredDefines)
        {
            if (requiredDefines == null)
            {
                return;
            }

            NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup
            );
            List<string> defineList = new List<string>(
                PlayerSettings
                    .GetScriptingDefineSymbols(namedBuildTarget)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            );

            bool changed = false;
            foreach (var define in requiredDefines)
            {
                if (defineList.Contains(define))
                {
                    continue;
                }

                defineList.Add(define);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            PlayerSettings.SetScriptingDefineSymbols(
                namedBuildTarget,
                string.Join(";", defineList)
            );
        }

        private static void RemoveScriptingDefines(IEnumerable<string> requiredDefines)
        {
            if (requiredDefines == null)
            {
                return;
            }

            NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup
            );
            List<string> defineList = new List<string>(
                PlayerSettings
                    .GetScriptingDefineSymbols(namedBuildTarget)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            );

            bool changed = false;
            foreach (var define in requiredDefines)
            {
                changed |= defineList.RemoveAll(existingDefine => existingDefine == define) > 0;
            }

            if (!changed)
            {
                return;
            }

            PlayerSettings.SetScriptingDefineSymbols(
                namedBuildTarget,
                string.Join(";", defineList)
            );
        }

        private static HashSet<string> GetCurrentScriptingDefines()
        {
            NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup
            );

            return new HashSet<string>(
                PlayerSettings
                    .GetScriptingDefineSymbols(namedBuildTarget)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            );
        }

        internal static Type ResolveType(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
            {
                return null;
            }

            if (ResolvedTypesByName.TryGetValue(fullTypeName, out var cachedType))
            {
                return cachedType;
            }

            if (MissingTypeNames.Contains(fullTypeName))
            {
                return null;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var directType = assembly.GetType(fullTypeName);
                if (directType != null)
                {
                    ResolvedTypesByName[fullTypeName] = directType;
                    return directType;
                }
            }

            var resolvedType = GetAllTypes()
                .FirstOrDefault(type =>
                    string.Equals(type.FullName, fullTypeName, StringComparison.Ordinal));
            if (resolvedType != null)
            {
                ResolvedTypesByName[fullTypeName] = resolvedType;
                return resolvedType;
            }

            MissingTypeNames.Add(fullTypeName);
            return null;
        }

        internal static IEnumerable<Type> GetAllTypes()
        {
            if (_allTypesCache != null)
            {
                return _allTypesCache;
            }

            List<Type> allTypes = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types.Where(type => type != null).ToArray();
                }

                allTypes.AddRange(types);
            }

            _allTypesCache = allTypes.ToArray();
            return _allTypesCache;
        }

        private static string SanitizeAssetName(string assetName)
        {
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                assetName = assetName.Replace(invalidCharacter, '_');
            }

            return assetName.Replace(' ', '_');
        }

        private static bool AssignProvider(
            ScriptableObject manager,
            ProviderDescriptor descriptor,
            Type providerType,
            ScriptableObject providerAsset
        )
        {
            SerializedObject serializedManager = new SerializedObject(manager);
            var fieldProp = serializedManager.FindProperty(descriptor.ManagerFieldName);
            if (fieldProp == null)
            {
                EditorUtility.DisplayDialog(
                    "Provider Assignment Failed",
                    $"Could not find manager field '{descriptor.ManagerFieldName}' on {manager.GetType().Name}.",
                    "OK"
                );
                return false;
            }

            SetProviderAssignment(fieldProp, descriptor, providerType, providerAsset);
            serializedManager.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static ScriptableObject LoadProviderAssetFromResources(
            ProviderDescriptor descriptor,
            Type providerType
        )
        {
            string assetPath = providerType != null
                ? GetProviderAssetPath(providerType)
                : GetProviderAssetPath(descriptor.ProviderTypeName);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            ScriptableObject providerAsset = AssetDatabase.LoadAssetAtPath(assetPath, providerType ?? typeof(ScriptableObject)) as ScriptableObject;
            if (providerAsset == null)
            {
                return null;
            }

            if (providerType != null)
            {
                return providerType.IsInstanceOfType(providerAsset)
                    ? providerAsset
                    : null;
            }

            return providerAsset.GetType().FullName == descriptor.ProviderTypeName
                ? providerAsset
                : null;
        }

        private static string GetProviderAssetPath(Type providerType)
        {
            return providerType == null
                ? null
                : GetProviderAssetPath(providerType.Name);
        }

        private static string GetProviderAssetPath(string providerTypeName)
        {
            string simpleTypeName = GetSimpleTypeName(providerTypeName);
            if (string.IsNullOrEmpty(simpleTypeName))
            {
                return null;
            }

            return $"{IntegrationProviderAssetsFolder}/{SanitizeAssetName(simpleTypeName)}.asset";
        }

        private static string GetSimpleTypeName(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
            {
                return null;
            }

            int lastDotIndex = fullTypeName.LastIndexOf('.');
            return lastDotIndex >= 0
                ? fullTypeName.Substring(lastDotIndex + 1)
                : fullTypeName;
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static void DrawNewCustomProviderButton(ProviderDescriptor[] descriptors)
        {
            string baseTypeName = descriptors
                .Select(d => d.CustomProviderBaseTypeName)
                .FirstOrDefault(n => !string.IsNullOrEmpty(n));

            if (string.IsNullOrEmpty(baseTypeName))
            {
                return;
            }

            Type targetType = ScriptTemplateGenerator.ResolveType(baseTypeName);
            if (targetType == null)
            {
                return;
            }

            GUILayout.Space(8);
            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(
                dividerRect,
                new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);

            if (GUILayout.Button("New Custom Provider", GUILayout.Height(25)))
            {
                ScriptTemplateGenerator.CreatePluginScript(
                    null,
                    "Assets/",
                    targetType,
                    targetType.IsInterface
                        ? ScriptTemplateGenerator.GenerationMode.InterfaceImplementation
                        : ScriptTemplateGenerator.GenerationMode.AbstractClassInheritance);
            }
        }

        private struct DependencyStatus
        {
            public string DisplayName { get; }
            public bool IsAvailable { get; }

            public DependencyStatus(string fullTypeName, bool isAvailable)
            {
                DisplayName = fullTypeName;
                IsAvailable = isAvailable;
            }
        }
    }
}