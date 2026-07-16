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
using Com.Hapiga.Scheherazade.Common.Integration.MPRL;
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

            // ── Control Bar ──
            DrawProviderControlBar(types);

            // ── Filter ──
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
                        asset, method, getterAttr.ParameterName,
                        "getter", seenNames, factoryResults));
                    continue;
                }

                TrackingParamDefaultFactoryAttribute factoryAttr =
                    method.GetCustomAttribute<TrackingParamDefaultFactoryAttribute>();
                if (factoryAttr != null)
                {
                    entries.Add(ValidateParamMethod(
                        asset, method, factoryAttr.ParameterName,
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
            string arrow = expanded ? "▼" : "▶";
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
            string kind,
            HashSet<string> seenNames,
            Dictionary<string, string> factoryResults)
        {
            var entry = new ParamMethodEntry
            {
                ParamName = paramName,
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

            // ═══ Header: name + status + navigate ═══
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

                EditorGUILayout.LabelField(
                    $"{statusIcon} {entry.ParamName}",
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

        private struct ParamMethodEntry
        {
            public string ParamName;
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

            // ── Control Bar ──
            DrawEventsControlBar(serializedManager, eventsProp);

            // ── Filter events ──
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
                // ── Header: asset name + Edit button + Delete ──
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

                // ── Event name (read-only) ──
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

                // ── Parameters section ──
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

            List<string> availableParamNames =
                CollectAvailableParamNamesFromProviders(manager);

            // ── Header row: Parameters + Edit + Collapse/Expand ──
            using (new EditorGUILayout.HorizontalScope())
            {
                string arrow = expanded ? "▼" : "▶";
                int paramCount = paramsProp.arraySize;
                EditorGUILayout.LabelField(
                    $"Parameters ({paramCount})",
                    EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                // if (GUILayout.Button("Edit", EditorStyles.miniButton, GUILayout.Width(40)))
                // {
                //     Rect buttonRect = GUILayoutUtility.GetLastRect();
                //     buttonRect.position = GUIUtility.GUIToScreenPoint(buttonRect.position);
                //     var popup = new EventEditPopup(
                //         eventAsset, eventNameProp, paramsProp,
                //         manager, serializedManager, eventsProp, index);
                //     PopupWindow.Show(buttonRect, popup);
                // }

                string toggleLabel = expanded ? "Collapse" : "Expand";
                if (GUILayout.Button(toggleLabel, EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    _eventParamFoldouts[key] = !expanded;
                }
            }

            if (!expanded) return;

            GUILayout.Space(4);

            // ── Read-only parameter rows ──
            for (int p = 0; p < paramsProp.arraySize; p++)
            {
                SerializedProperty nameProp =
                    paramsProp.GetArrayElementAtIndex(p)
                        .FindPropertyRelative("_name");
                string pName = nameProp?.stringValue ?? "";

                // Determine parameter type from providers
                string typeLabel = ResolveParamType(manager, pName);

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(pName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (!string.IsNullOrEmpty(typeLabel))
                    {
                        EditorGUILayout.LabelField(
                            typeLabel,
                            EditorStyles.miniLabel,
                            GUILayout.Width(60));
                    }
                }
            }

            // ── Warnings ──
            DrawEventParameterWarnings(paramsProp, manager);
        }

        private static string ResolveParamType(ScriptableObject manager, string paramName)
        {
            if (string.IsNullOrEmpty(paramName)) return "";

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
                    if (getterAttr != null && getterAttr.ParameterName == paramName)
                        return GetFriendlyTypeName(method.ReturnType);
                }
            }

            return "";
        }

        private static List<string> CollectAvailableParamNamesFromProviders(
            ScriptableObject manager)
        {
            var names = new List<string>();
            var seen = new HashSet<string>();

            SerializedObject so = new SerializedObject(manager);
            SerializedProperty providersProp =
                so.FindProperty("templatedParameterProviders");

            if (providersProp == null || !providersProp.isArray) return names;

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
                    if (getterAttr != null && seen.Add(getterAttr.ParameterName))
                    {
                        names.Add(getterAttr.ParameterName);
                        continue;
                    }

                    TrackingParamDefaultFactoryAttribute factoryAttr =
                        method.GetCustomAttribute<TrackingParamDefaultFactoryAttribute>();
                    if (factoryAttr != null && seen.Add(factoryAttr.ParameterName))
                    {
                        names.Add(factoryAttr.ParameterName);
                    }
                }
            }

            names.Sort();
            return names;
        }

        private static bool DrawSingleEventParameter(
            int index,
            SerializedProperty nameProp,
            string[] availableOptions,
            SerializedObject eventSo,
            SerializedProperty paramsProp,
            UnityEngine.Object eventAsset)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"#{index}", GUILayout.Width(25));

                if (nameProp != null)
                {
                    string currentName = nameProp.stringValue ?? "";

                    if (availableOptions.Length > 0)
                    {
                        int currentIndex = Array.IndexOf(
                            availableOptions, currentName);
                        if (currentIndex < 0) currentIndex = 0;

                        int newIndex = EditorGUILayout.Popup(
                            currentIndex,
                            availableOptions);

                        if (newIndex != currentIndex
                            || (currentIndex == 0 && currentName != availableOptions[0]))
                        {
                            nameProp.stringValue = availableOptions[newIndex];
                            eventSo.ApplyModifiedProperties();
                            EditorUtility.SetDirty(eventAsset);
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(nameProp,
                            GUIContent.none);
                        if (EditorGUI.EndChangeCheck())
                        {
                            eventSo.ApplyModifiedProperties();
                            EditorUtility.SetDirty(eventAsset);
                        }
                    }
                }

                if (GUILayout.Button("X", EditorStyles.miniButton,
                        GUILayout.Width(22), GUILayout.Height(18)))
                {
                    paramsProp.DeleteArrayElementAtIndex(index);
                    eventSo.ApplyModifiedProperties();
                    EditorUtility.SetDirty(eventAsset);
                    return true;
                }
            }

            return false;
        }

        private static void DrawEventParameterWarnings(
            SerializedProperty paramsProp,
            ScriptableObject manager)
        {
            if (paramsProp == null || !paramsProp.isArray) return;

            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty providersProp =
                serializedManager.FindProperty("templatedParameterProviders");

            // Collect all available parameter names from enabled providers
            HashSet<string> availableParams = new HashSet<string>();

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

                    CollectProviderParamNames(asset, availableParams);
                }
            }

            for (int i = 0; i < paramsProp.arraySize; i++)
            {
                SerializedProperty nameProp =
                    paramsProp.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("_name");

                string pName = nameProp?.stringValue;
                if (string.IsNullOrEmpty(pName)) continue;

                SerializedProperty defaultProp =
                    paramsProp.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("_defaultValue");

                bool hasDefault = !string.IsNullOrEmpty(defaultProp?.stringValue);

                if (!availableParams.Contains(pName) && !hasDefault)
                {
                    EditorGUILayout.HelpBox(
                        $"Parameter \"{pName}\" has no getter/factory in any enabled "
                        + "parameter provider and no default value.\n"
                        + "It will be omitted from the tracking event at runtime.",
                        MessageType.Warning);
                }
            }
        }

        private static void CollectProviderParamNames(
            ScriptableObject asset,
            HashSet<string> names)
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
                    names.Add(getterAttr.ParameterName);
                    continue;
                }

                TrackingParamDefaultFactoryAttribute factoryAttr =
                    method.GetCustomAttribute<TrackingParamDefaultFactoryAttribute>();
                if (factoryAttr != null)
                {
                    names.Add(factoryAttr.ParameterName);
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
            private List<string> _availableParams;
            private HashSet<string> _selectedParams;
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
                _selectedParams = new HashSet<string>();
                if (paramsProp != null && paramsProp.isArray)
                {
                    for (int i = 0; i < paramsProp.arraySize; i++)
                    {
                        string name = paramsProp.GetArrayElementAtIndex(i)
                            .FindPropertyRelative("_name")?.stringValue;
                        if (!string.IsNullOrEmpty(name))
                            _selectedParams.Add(name);
                    }
                }
            }

            public override Vector2 GetWindowSize()
            {
                float rows = 4f // header + asset name + event name + separator
                    + _availableParams.Count;
                float h = 28f + rows * 26f + 8f;
                return new Vector2(360f, Mathf.Min(h, 420f));
            }

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField("Edit Event", EditorStyles.boldLabel);
                GUILayout.Space(4);

                // ── Asset Name ──
                _assetName = EditorGUILayout.TextField("Asset Name", _assetName);

                // ── Event Name ──
                _eventName = EditorGUILayout.TextField("Event Name", _eventName);

                GUILayout.Space(4);
                Rect div = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(div, new Color(0.5f, 0.5f, 0.5f, 0.3f));
                GUILayout.Space(4);

                // ── Parameters ──
                EditorGUILayout.LabelField(
                    $"Parameters ({_selectedParams.Count}/{_availableParams.Count})",
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
                            p.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                        ).ToList();

                    _scroll = EditorGUILayout.BeginScrollView(_scroll);

                    for (int i = filteredParams.Count - 1; i >= 0; i--)
                    {
                        string param = filteredParams[i];
                        bool isSelected = _selectedParams.Contains(param);
                        string typeLabel = ResolveParamType(_manager, param);

                        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                        {
                            bool newSelected = EditorGUILayout.Toggle(
                                isSelected, GUILayout.Width(20));
                            EditorGUILayout.LabelField(
                                param, EditorStyles.boldLabel);
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
                                    _selectedParams.Add(param);
                                else
                                    _selectedParams.Remove(param);
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
                // ── Commit asset name ──
                if (!string.IsNullOrEmpty(_assetName) && _assetName != _eventAsset.name)
                {
                    _eventAsset.name = _assetName;
                    EditorUtility.SetDirty(_eventAsset);
                    AssetDatabase.SaveAssets();
                }

                // ── Commit event name ──
                if (_eventName != (_eventNameProp?.stringValue ?? ""))
                {
                    _eventNameProp.stringValue = _eventName;
                    _eventNameProp.serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_eventAsset);
                }

                // ── Commit parameters: rebuild the array from _selectedParams ──
                if (_paramsProp != null && _paramsProp.isArray)
                {
                    // Remove all existing parameters
                    while (_paramsProp.arraySize > 0)
                        _paramsProp.DeleteArrayElementAtIndex(_paramsProp.arraySize - 1);

                    // Add selected parameters
                    foreach (string param in _availableParams)
                    {
                        if (!_selectedParams.Contains(param)) continue;

                        int newIdx = _paramsProp.arraySize++;
                        SerializedProperty newParam = _paramsProp.GetArrayElementAtIndex(newIdx);
                        SerializedProperty nameProp = newParam.FindPropertyRelative("_name");
                        if (nameProp != null)
                            nameProp.stringValue = param;
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

    public class AsyncResourceManagerSettingsProvider : SettingsProvider
    {
        private Vector2 _scrollPosition;
        private readonly Dictionary<string, int> _tabIndices = new Dictionary<string, int>();
        private readonly Dictionary<string, bool> _expandedTypes = new Dictionary<string, bool>();
        private readonly Dictionary<string, ProviderCreationWizard> _providerWizards = new Dictionary<string, ProviderCreationWizard>();

        private readonly string[] TabNames = { "Manager Config", "Providers" };

        private AsyncResourceManagerSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new AsyncResourceManagerSettingsProvider(
                "Project/Integration/Async Resource Manager",
                SettingsScope.Project,
                new[] { "resource", "MPRL", "async", "provider", "reference", "folder" }
            );
        }

        public override void OnGUI(string searchContext)
        {
            try
            {
                base.OnGUI(searchContext);
                IntegrationSettingsDrawingUtils.ProcessPendingProviderCreations();

                var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
                if (centre == null) return;

                var managerTypes = FindAllResourceManagerTypes(centre);

                if (managerTypes.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No concrete ResourceManagerBase classes found in the project.\n\n" +
                    "Create a class that inherits from ResourceManagerBase<SelfType, ResourceType>.",
                    MessageType.Info
                );

                GUILayout.Space(8);

                if (GUILayout.Button("Create New Resource Manager", GUILayout.Height(30)))
                {
                    global::Com.Hapiga.Scheherazade.Common.Editor.Integration
                        .CreateResourceManagerWindow.Open();
                }

                return;
            }

            GUILayout.Space(4);
            EditorGUILayout.LabelField(
                "Async Resource Manager Configuration", EditorStyles.boldLabel);
            GUILayout.Space(4);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            foreach (var info in managerTypes)
            {
                DrawManagerCard(info, centre);
                GUILayout.Space(6);
            }

            EditorGUILayout.EndScrollView();

            GUILayout.Space(8);

            if (GUILayout.Button("Create New Resource Manager", GUILayout.Height(28)))
            {
                global::Com.Hapiga.Scheherazade.Common.Editor.Integration
                    .CreateResourceManagerWindow.Open();
            }
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AsyncResourceManager] {ex}");
            }
        }

        // ═══════════════════════════════════════════════════
        // ── Per‑Type Card ─────────────────────────────────
        // ═══════════════════════════════════════════════════

        private void DrawManagerCard(ConcreteManagerInfo info, IntegrationCentre centre)
        {
            Rect cardRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ── Header ──
            DrawCardHeader(info, centre);

            // ── Body (attached only) ──
            if (info.IsAttached && IsExpanded(info))
            {
                GUILayout.Space(2);

                int tabIndex = GetTabIndex(info);
                tabIndex = GUILayout.Toolbar(tabIndex, TabNames);
                SetTabIndex(info, tabIndex);

                GUILayout.Space(4);

                var dividerRect = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(dividerRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
                GUILayout.Space(4);

                switch (tabIndex)
                {
                    case 0:
                        DrawManagerConfig(info.AttachedAsset, info.ConcreteType,
                            info.ResourceType);
                        break;
                    case 1:
                        DrawProviders(info, centre);
                        break;
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCardHeader(ConcreteManagerInfo info, IntegrationCentre centre)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // ── State badge + type name ──
                DrawStateBadge(info);

                EditorGUILayout.LabelField(info.DisplayName, EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                // ── Actions ──
                DrawCardActions(info, centre);
            }
        }

        private void DrawStateBadge(ConcreteManagerInfo info)
        {
            string badgeText;
            Color badgeColor;

            if (info.IsAttached)
            {
                badgeText = " ACTIVE ";
                badgeColor = new Color(0.2f, 0.7f, 0.2f);
            }
            else if (info.HasAsset)
            {
                badgeText = " NOT ATTACHED ";
                badgeColor = new Color(0.8f, 0.7f, 0.2f);
            }
            else
            {
                badgeText = " NO ASSET ";
                badgeColor = Color.gray;
            }

            GUIStyle badgeStyle = new GUIStyle(EditorStyles.miniButton)
            {
                normal = { textColor = Color.white },
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedWidth = 90
            };

            Color prevColor = GUI.backgroundColor;
            GUI.backgroundColor = badgeColor;
            GUILayout.Label(badgeText, badgeStyle, GUILayout.Height(20));
            GUI.backgroundColor = prevColor;
        }

        private void DrawCardActions(ConcreteManagerInfo info, IntegrationCentre centre)
        {
            if (info.IsAttached)
            {
                if (DrawSmallButton("Configure",
                        $"Show/hide configuration for {info.DisplayName}"))
                {
                    ToggleExpanded(info);
                }

                if (DrawSmallButton("Disable",
                        $"Remove {info.DisplayName} from Integration Centre"))
                {
                    IntegrationSettingsDrawingUtils.SetModuleAsset(
                        centre, info.AttachedAsset, null);
                }

                if (DrawSmallButton("Delete",
                        $"Delete the {info.ConcreteType.Name} asset from disk"))
                {
                    DeleteManagerAsset(info);
                }
            }
            else if (info.HasAsset)
            {
                ScriptableObject firstAsset = info.ExistingAssets[0];

                if (DrawSmallButton("Enable",
                        $"Add {firstAsset.name} to Integration Centre"))
                {
                    IntegrationSettingsDrawingUtils.SetModuleAsset(
                        centre, null, firstAsset);
                    info.AttachedAsset = firstAsset;
                }

                if (DrawSmallButton("Delete",
                        $"Delete the {info.ConcreteType.Name} asset from disk"))
                {
                    DeleteManagerAsset(info);
                }
            }
            else
            {
                if (DrawSmallButton("Create Asset",
                        $"Create a new {info.ConcreteType.Name} ScriptableObject asset"))
                {
                    CreateManagerAsset(info, centre);
                }
            }
        }

        // ═══════════════════════════════════════════════════
        // ── Manager Config Tab ────────────────────────────
        // ═══════════════════════════════════════════════════

        private void DrawManagerConfig(
            ScriptableObject manager, Type concreteType, Type resourceType)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Resource Type", resourceType.Name);
                EditorGUILayout.LabelField("Manager Type", concreteType.FullName);

                bool isInitialized = GetManagerStatus(manager);
                GUIStyle statusStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = isInitialized ? Color.green : Color.gray }
                };
                EditorGUILayout.LabelField(
                    "Status",
                    isInitialized ? "Initialized" : "Not Initialized",
                    statusStyle
                );
            }

            GUILayout.Space(6);
            IntegrationSettingsDrawingUtils.DrawInlineInspector(
                manager,
                $"{PascalCaseToSpaced(manager.name).ToUpperInvariant()} CONFIGURATION"
            );
        }

        // ═══════════════════════════════════════════════════
        // ── Providers Tab ─────────────────────────────────
        // ═══════════════════════════════════════════════════

        private void DrawProviders(ConcreteManagerInfo info, IntegrationCentre centre)
        {
            ScriptableObject manager = info.AttachedAsset;
            if (manager == null) return;

            Type resourceType = info.ResourceType;

            // ── Built‑in Templates ──
            var templates = FindProviderTemplates(manager, resourceType);
            if (templates.Count > 0)
            {
                EditorGUILayout.LabelField("Built‑in Templates",
                    EditorStyles.miniBoldLabel);
                GUILayout.Space(2);

                foreach (var tmpl in templates)
                {
                    DrawTemplateProviderCard(tmpl, info, centre, resourceType);
                    GUILayout.Space(4);
                }

                GUILayout.Space(4);
            }

            // ── Enabled (in array) providers ──
            SerializedObject serializedManager = new SerializedObject(manager);
            var providersProp = serializedManager.FindProperty("initialProviders");
            if (providersProp != null)
            {
                EditorGUILayout.LabelField("Enabled Providers",
                    EditorStyles.miniBoldLabel);
                GUILayout.Space(2);

                for (int i = 0; i < providersProp.arraySize; i++)
                {
                    var element = providersProp.GetArrayElementAtIndex(i);
                    ScriptableObject provider = element.objectReferenceValue as ScriptableObject;

                    if (provider == null) continue;

                    DrawEnabledProviderCard(
                        provider, i, providersProp, serializedManager, manager);
                    GUILayout.Space(4);
                }
            }

            // ── Not‑in‑array providers (created but not enabled) ──
            var unattached = FindUnattachedProviders(info, resourceType);
            if (unattached.Count > 0)
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField("Detached Providers", EditorStyles.miniBoldLabel);
                GUILayout.Space(2);

                foreach (var provInfo in unattached)
                {
                    DrawDetachedProviderCard(provInfo, info, centre, resourceType);
                    GUILayout.Space(4);
                }
            }

            // ── New Custom Provider ──
            GUILayout.Space(8);
            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(dividerRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);

            if (GUILayout.Button("+ New Custom Provider", GUILayout.Height(24)))
            {
                ScriptTemplateGenerator.CreatePluginScript(
                    null,
                    "Assets/",
                    typeof(IAsyncResourceProvider<>).MakeGenericType(resourceType),
                    ScriptTemplateGenerator.GenerationMode.InterfaceImplementation);
            }
        }

        // ═══════════════════════════════════════════════════
        // ── Template Provider Card ────────────────────────
        // ═══════════════════════════════════════════════════

        private sealed class ProviderTemplateEntry
        {
            public ResourceProviderAttribute Attribute;
            public Type OpenGenericType;
            public Type ConcreteType;
            public ScriptableObject ExistingAsset;
            public bool IsInArray;
            public int ArrayIndex;
        }

        private void DrawTemplateProviderCard(ProviderTemplateEntry tmpl,
            ConcreteManagerInfo info, IntegrationCentre centre, Type resourceType)
        {
            string key = info.ConcreteType.FullName + "::" +
                         tmpl.OpenGenericType.FullName;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // ── Header ──
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        tmpl.Attribute.DisplayName, EditorStyles.boldLabel);

                    if (tmpl.Attribute.RequiredDefines != null)
                    {
                        string[] missing = IntegrationSettingsDrawingUtils
                            .GetMissingDefines(tmpl.Attribute.RequiredDefines);

                        if (missing.Length > 0)
                        {
                            GUIStyle defStyle = new GUIStyle(EditorStyles.miniLabel)
                            {
                                normal = { textColor = new Color(0.9f, 0.6f, 0.1f) }
                            };
                            EditorGUILayout.LabelField(
                                $"Requires: {string.Join(", ", missing)}",
                                defStyle);

                            if (DrawSmallButton("Enable",
                                    "Enable required scripting defines"))
                            {
                                IntegrationSettingsDrawingUtils
                                    .EnsureScriptingDefines(missing);
                            }
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.LabelField(tmpl.Attribute.Description,
                                EditorStyles.wordWrappedMiniLabel);
                            return;
                        }
                    }

                    GUILayout.FlexibleSpace();

                    DrawTemplateCardActions(tmpl, info, centre, resourceType, key);
                }

                EditorGUILayout.LabelField(tmpl.Attribute.Description,
                    EditorStyles.wordWrappedMiniLabel);

                // ── Inline wizard ──
                if (_providerWizards.TryGetValue(key, out var wizard) && wizard.Active)
                {
                    GUILayout.Space(6);
                    DrawProviderCreationWizard(wizard, key, tmpl,
                        info, centre, resourceType);
                }
            }
        }

        private void DrawTemplateCardActions(ProviderTemplateEntry tmpl,
            ConcreteManagerInfo info, IntegrationCentre centre,
            Type resourceType, string key)
        {
            bool inWizard = _providerWizards.TryGetValue(key, out var wizard)
                            && wizard.Active;

            if (tmpl.IsInArray)
            {
                if (DrawSmallButton("Config", "Toggle inline inspector"))
                {
                    ToggleExpanded(info);
                }

                if (!inWizard && DrawSmallButton("Disable",
                        $"Remove provider from the array"))
                {
                    RemoveProviderFromArray(info.AttachedAsset,
                        tmpl.ArrayIndex);
                    tmpl.IsInArray = false;
                }

                if (!inWizard && DrawSmallButton("Delete",
                        $"Delete the asset from disk"))
                {
                    DeleteProviderAsset(tmpl.ExistingAsset, info);
                    tmpl.ExistingAsset = null;
                    tmpl.ConcreteType = null;
                    tmpl.IsInArray = false;
                    tmpl.ArrayIndex = -1;
                }
            }
            else if (tmpl.ConcreteType != null && tmpl.ExistingAsset != null)
            {
                if (DrawSmallButton("Enable",
                        $"Add to provider array"))
                {
                    AppendProviderToArray(info.AttachedAsset,
                        tmpl.ExistingAsset);
                    tmpl.IsInArray = true;
                }

                if (DrawSmallButton("Delete",
                        $"Delete the asset from disk"))
                {
                    DeleteProviderAsset(tmpl.ExistingAsset, info);
                    tmpl.ExistingAsset = null;
                    tmpl.ConcreteType = null;
                }
            }
            else
            {
                if (!inWizard && DrawSmallButton("Create",
                        $"Generate concrete subclass + ScriptableObject asset"))
                {
                    _providerWizards[key] = new ProviderCreationWizard
                    {
                        Active = true,
                        ClassName = $"{resourceType.Name}{tmpl.OpenGenericType.Name}",
                        Namespace = EditorSettings.projectGenerationRootNamespace ?? "Scripts",
                        FolderPath = "Assets/"
                    };
                }
            }
        }

        // ═══════════════════════════════════════════════════
        // ── Enabled Provider Card ─────────────────────────
        // ═══════════════════════════════════════════════════

        private void DrawEnabledProviderCard(ScriptableObject provider, int index,
            SerializedProperty providersProp, SerializedObject serializedManager,
            ScriptableObject manager)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(provider.name, EditorStyles.boldLabel);

                    // Reorder arrows
                    bool canMoveUp = index > 0;
                    bool canMoveDown = index < providersProp.arraySize - 1;

                    if (canMoveUp && DrawSmallButton("▲", "Move up"))
                    {
                        providersProp.MoveArrayElement(index, index - 1);
                        serializedManager.ApplyModifiedProperties();
                        EditorUtility.SetDirty(manager);
                        AssetDatabase.SaveAssets();
                        return;
                    }

                    if (canMoveDown && DrawSmallButton("▼", "Move down"))
                    {
                        providersProp.MoveArrayElement(index, index + 1);
                        serializedManager.ApplyModifiedProperties();
                        EditorUtility.SetDirty(manager);
                        AssetDatabase.SaveAssets();
                        return;
                    }

                    GUILayout.FlexibleSpace();

                    if (DrawSmallButton("Disable",
                            $"Remove {provider.name} from provider array"))
                    {
                        RemoveProviderFromArray(manager, index);
                        return;
                    }

                    if (DrawSmallButton("Delete",
                            $"Delete {provider.name} asset from disk"))
                    {
                        DeleteProviderAsset(provider, null);
                        return;
                    }
                }

                GUILayout.Space(2);
                IntegrationSettingsDrawingUtils.DrawInlineInspector(
                    provider,
                    $"{PascalCaseToSpaced(provider.name).ToUpperInvariant()} PROVIDER"
                );
            }
        }

        // ═══════════════════════════════════════════════════
        // ── Detached Provider Card ────────────────────────
        // ═══════════════════════════════════════════════════

        private sealed class DetachedProviderInfo
        {
            public Type ConcreteType;
            public ScriptableObject Asset;
        }

        private void DrawDetachedProviderCard(DetachedProviderInfo provInfo,
            ConcreteManagerInfo info, IntegrationCentre centre, Type resourceType)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(provInfo.Asset.name,
                        EditorStyles.boldLabel);

                    Color prevColor = GUI.color;
                    GUI.color = new Color(0.6f, 0.6f, 0.6f);
                    EditorGUILayout.LabelField(
                        $"({provInfo.ConcreteType.Name})",
                        EditorStyles.miniLabel);
                    GUI.color = prevColor;

                    GUILayout.FlexibleSpace();

                    if (DrawSmallButton("Enable",
                            $"Add {provInfo.Asset.name} to provider array"))
                    {
                        AppendProviderToArray(info.AttachedAsset, provInfo.Asset);
                    }

                    if (DrawSmallButton("Delete",
                            $"Delete {provInfo.Asset.name} from disk"))
                    {
                        DeleteProviderAsset(provInfo.Asset, info);
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════
        // ── Inline Creation Wizard ────────────────────────
        // ═══════════════════════════════════════════════════

        private sealed class ProviderCreationWizard
        {
            public bool Active;
            public string ClassName = "";
            public string Namespace = "";
            public string FolderPath = "Assets/";
            public bool Fired;
        }

        private void DrawProviderCreationWizard(ProviderCreationWizard wizard,
            string key, ProviderTemplateEntry tmpl, ConcreteManagerInfo info,
            IntegrationCentre centre, Type resourceType)
        {
            Rect wizardRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Create Provider Subclass",
                EditorStyles.miniBoldLabel);

            wizard.ClassName = EditorGUILayout.TextField("Class Name",
                wizard.ClassName);

            EditorGUI.BeginChangeCheck();
            wizard.Namespace = EditorGUILayout.TextField("Namespace",
                wizard.Namespace);
            if (EditorGUI.EndChangeCheck())
            {
                wizard.FolderPath = NamespaceToFolder(wizard.Namespace);
            }

            wizard.FolderPath = EditorGUILayout.TextField("Folder",
                wizard.FolderPath);

            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(70)))
                {
                    _providerWizards.Remove(key);
                }

                if (GUILayout.Button("Generate", GUILayout.Width(80)))
                {
                    GenerateProviderSubclass(wizard, tmpl, info, resourceType);
                    _providerWizards.Remove(key);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void GenerateProviderSubclass(ProviderCreationWizard wizard,
            ProviderTemplateEntry tmpl, ConcreteManagerInfo info,
            Type resourceType)
        {
            string className = wizard.ClassName;
            string ns = wizard.Namespace;
            string folder = wizard.FolderPath.TrimEnd('/');

            string openGenName = tmpl.OpenGenericType.FullName;
            int backtick = openGenName.IndexOf('`');
            string baseName = backtick > 0
                ? openGenName.Substring(0, backtick)
                : openGenName;
            string resName = resourceType.FullName;

            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"using {tmpl.OpenGenericType.Namespace};");
            }
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
            }
            string indent = string.IsNullOrEmpty(ns) ? "" : "    ";
            sb.AppendLine(
                $"{indent}public sealed class {className} : {baseName}<{resName}>");
            sb.AppendLine($"{indent}{{");

            // Add stub overrides for abstract members
            Type closedGeneric = tmpl.OpenGenericType.MakeGenericType(resourceType);
            WriteAbstractMemberStubs(sb, closedGeneric, indent);

            sb.AppendLine($"{indent}}}");
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine("}");
            }

            string filePath = System.IO.Path.Combine(folder,
                className + ".cs").Replace('\\', '/');

            string dir = System.IO.Path.GetDirectoryName(filePath);
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            System.IO.File.WriteAllText(filePath, sb.ToString(),
                System.Text.Encoding.UTF8);
            AssetDatabase.Refresh();

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(filePath);
            if (script != null)
            {
                EditorGUIUtility.PingObject(script);
            }
        }

        private static void WriteAbstractMemberStubs(System.Text.StringBuilder sb,
            Type type, string indent)
        {
            if (!type.IsAbstract) return;

            string inner = indent + "    ";

            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!method.IsAbstract) continue;

                string ret = GetTypeName(method.ReturnType);
                var paramList = string.Join(", ",
                    method.GetParameters().Select(p =>
                        $"{GetTypeName(p.ParameterType)} {p.Name}"));
                string access = method.IsPublic ? "public" : "protected";

                sb.AppendLine();
                sb.AppendLine(
                    $"{inner}{access} override {ret} {method.Name}({paramList})");
                sb.AppendLine($"{inner}{{");

                if (method.ReturnType != typeof(void))
                {
                    sb.AppendLine($"{inner}    throw new System.NotImplementedException();");
                }
                else
                {
                    sb.AppendLine(
                        $"{inner}    throw new System.NotImplementedException();");
                }

                sb.AppendLine($"{inner}}}");
            }

            foreach (var prop in type.GetProperties(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var getter = prop.GetGetMethod(true);
                var setter = prop.GetSetMethod(true);
                if (getter == null || !getter.IsAbstract) continue;

                string propType = GetTypeName(prop.PropertyType);
                string access = (getter.IsPublic || (setter != null && setter.IsPublic))
                    ? "public" : "protected";

                sb.AppendLine();
                sb.AppendLine(
                    $"{inner}{access} override {propType} {prop.Name}");
                sb.AppendLine($"{inner}{{");

                if (getter.IsAbstract)
                    sb.AppendLine(
                        $"{inner}    get => throw new System.NotImplementedException();");
                if (setter != null && setter.IsAbstract)
                    sb.AppendLine(
                        $"{inner}    set => throw new System.NotImplementedException();");

                sb.AppendLine($"{inner}}}");
            }
        }

        private static string GetTypeName(Type type)
        {
            if (!type.IsGenericType) return type.FullName ?? type.Name;

            string name = type.Name;
            int backtick = name.IndexOf('`');
            name = backtick > 0 ? name.Substring(0, backtick) : name;
            string args = string.Join(", ",
                type.GenericTypeArguments.Select(GetTypeName));
            return $"{name}<{args}>";
        }

        // ═══════════════════════════════════════════════════
        // ── Provider Array Manipulation ───────────────────
        // ═══════════════════════════════════════════════════

        private void AppendProviderToArray(ScriptableObject manager,
            ScriptableObject provider)
        {
            if (manager == null || provider == null) return;

            SerializedObject serializedManager = new SerializedObject(manager);
            var providersProp = serializedManager.FindProperty("initialProviders");
            if (providersProp == null) return;

            int newIdx = providersProp.arraySize++;
            providersProp.GetArrayElementAtIndex(newIdx).objectReferenceValue
                = provider;
            serializedManager.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
        }

        private void RemoveProviderFromArray(ScriptableObject manager, int index)
        {
            if (manager == null || index < 0) return;

            SerializedObject serializedManager = new SerializedObject(manager);
            var providersProp = serializedManager.FindProperty("initialProviders");
            if (providersProp == null) return;

            if (index >= providersProp.arraySize) return;

            IntegrationSettingsDrawingUtils.DeleteArrayElement(
                providersProp, index);
            serializedManager.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
        }

        private void DeleteProviderAsset(ScriptableObject provider,
            ConcreteManagerInfo info)
        {
            if (provider == null) return;

            string path = AssetDatabase.GetAssetPath(provider);
            if (string.IsNullOrEmpty(path)) return;

            bool confirm = EditorUtility.DisplayDialog(
                "Delete Provider Asset",
                $"Permanently delete '{path}'?\n\n" +
                "This cannot be undone.",
                "Delete", "Cancel");
            if (!confirm) return;

            // Remove from provider arrays if the manager is attached
            if (info?.AttachedAsset != null)
            {
                SerializedObject serializedManager =
                    new SerializedObject(info.AttachedAsset);
                var providersProp = serializedManager
                    .FindProperty("initialProviders");
                if (providersProp != null)
                {
                    for (int i = providersProp.arraySize - 1; i >= 0; i--)
                    {
                        if (providersProp.GetArrayElementAtIndex(i)
                                .objectReferenceValue == provider)
                        {
                            IntegrationSettingsDrawingUtils.DeleteArrayElement(
                                providersProp, i);
                            break;
                        }
                    }
                    serializedManager.ApplyModifiedProperties();
                    EditorUtility.SetDirty(info.AttachedAsset);
                }
            }

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
        }

        private static string NamespaceToFolder(string ns)
        {
            if (string.IsNullOrEmpty(ns)) return "Assets/";
            string rootNs = EditorSettings.projectGenerationRootNamespace;
            if (!string.IsNullOrEmpty(rootNs) && ns.StartsWith(rootNs))
            {
                string sub = ns.Substring(rootNs.Length).TrimStart('.');
                return "Assets/" + sub.Replace('.', '/') + "/";
            }
            return "Assets/" + ns.Replace('.', '/') + "/";
        }

        // ═══════════════════════════════════════════════════
        // ── Actions ───────────────────────────────────────
        // ═══════════════════════════════════════════════════

        private void CreateManagerAsset(ConcreteManagerInfo info,
            IntegrationCentre centre)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                $"Create {info.ConcreteType.Name}",
                info.ConcreteType.Name,
                "asset",
                $"Create a new {info.ConcreteType.Name} asset"
            );

            if (string.IsNullOrEmpty(path)) return;

            ScriptableObject asset = ScriptableObject.CreateInstance(info.ConcreteType);
            asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);

            IntegrationSettingsDrawingUtils.SetModuleAsset(
                centre, null, asset);
            info.ExistingAssets.Add(asset);
            info.AttachedAsset = asset;
        }

        private void DeleteManagerAsset(ConcreteManagerInfo info)
        {
            ScriptableObject assetToDelete = info.AttachedAsset;

            if (assetToDelete == null && info.ExistingAssets.Count > 0)
                assetToDelete = info.ExistingAssets[0];

            if (assetToDelete == null) return;

            string assetName = assetToDelete.name;
            string path = AssetDatabase.GetAssetPath(assetToDelete);

            bool confirm = EditorUtility.DisplayDialog(
                "Delete Asset",
                $"Permanently delete '{path}'?\n\nThis cannot be undone.",
                "Delete", "Cancel"
            );

            if (!confirm) return;

            // Remove from IntegrationCentre if attached
            if (info.AttachedAsset == assetToDelete)
            {
                var centre = IntegrationSettingsDrawingUtils
                    .GetOrCreateIntegrationCentre();
                if (centre != null)
                {
                    IntegrationSettingsDrawingUtils.SetModuleAsset(
                        centre, assetToDelete, null);
                }
            }

            info.ExistingAssets.Remove(assetToDelete);
            info.AttachedAsset = null;

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
        }

        // ═══════════════════════════════════════════════════
        // ── State Helpers ─────────────────────────────────
        // ═══════════════════════════════════════════════════

        private int GetTabIndex(ConcreteManagerInfo info)
        {
            string key = info.ConcreteType.FullName;
            _tabIndices.TryGetValue(key, out int idx);
            return idx;
        }

        private void SetTabIndex(ConcreteManagerInfo info, int idx)
        {
            _tabIndices[info.ConcreteType.FullName] = idx;
        }

        private bool IsExpanded(ConcreteManagerInfo info)
        {
            string key = info.ConcreteType.FullName;
            _expandedTypes.TryGetValue(key, out bool expanded);
            return expanded;
        }

        private void ToggleExpanded(ConcreteManagerInfo info)
        {
            string key = info.ConcreteType.FullName;
            _expandedTypes[key] = !IsExpanded(info);
        }

        // ═══════════════════════════════════════════════════
        // ── Cached Type Discovery ────────────────────────
        // ═══════════════════════════════════════════════════

        [DidReloadScripts]
        private static void InvalidateCaches()
        {
            s_cachedManagerEntries = null;
            s_cachedTemplateTypes = null;
            s_cachedConcreteSubclass = null;
            s_cachedInterfaceTypes = null;
        }

        // ═══════════════════════════════════════════════════
        // ── Type Discovery (cached) ───────────────────────
        // ═══════════════════════════════════════════════════

        private sealed class ConcreteManagerInfo
        {
            public Type ConcreteType;
            public Type ResourceType;
            public string DisplayName;
            public List<ScriptableObject> ExistingAssets;
            public ScriptableObject AttachedAsset;
            public bool HasAsset => ExistingAssets != null && ExistingAssets.Count > 0;
            public bool IsAttached => AttachedAsset != null;
        }

        // Cached type-scan results — built once, cleared on [DidReloadScripts]
        private sealed class ManagerTypeEntry
        {
            public Type ConcreteType;
            public Type ResourceType;
        }

        private static List<ManagerTypeEntry> s_cachedManagerEntries;
        private static List<Type> s_cachedTemplateTypes;
        private static Dictionary<string, Type> s_cachedConcreteSubclass;
        private static Dictionary<string, List<Type>> s_cachedInterfaceTypes;

        private static void EnsureCaches()
        {
            if (s_cachedManagerEntries != null) return;

            var managerEntries = new List<ManagerTypeEntry>();
            var templateTypes = new List<Type>();
            var subclassMap = new Dictionary<string, Type>();

            // Pass 1: [ResourceProvider] templates (can be abstract)
            foreach (Type type in IntegrationSettingsDrawingUtils.GetAllTypes())
            {
                var attr = type.GetCustomAttribute<ResourceProviderAttribute>();
                if (attr != null && type.IsGenericTypeDefinition
                    && type.Namespace != null
                    && type.Namespace.StartsWith(
                        "Com.Hapiga.Scheherazade.Common.Integration"))
                {
                    templateTypes.Add(type);
                }
            }

            // Pass 2: concrete types (managers + subclass map)
            foreach (Type type in IntegrationSettingsDrawingUtils.GetAllTypes())
            {
                if (!type.IsClass || type.IsAbstract) continue;

                // ResourceManagerBase subclasses
                Type resType = GetResourceManagerResourceType(type);
                if (resType != null && typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    managerEntries.Add(new ManagerTypeEntry
                    {
                        ConcreteType = type, ResourceType = resType
                    });
                }
            }

            // Build concrete-subclass lookup: for every concrete ScriptableObject,
            // record its relationship to open generic base types
            foreach (Type type in IntegrationSettingsDrawingUtils.GetAllTypes())
            {
                if (!type.IsClass || type.IsAbstract) continue;
                if (type.IsGenericTypeDefinition) continue;
                if (!typeof(ScriptableObject).IsAssignableFrom(type)) continue;

                Type baseType = type.BaseType;
                while (baseType != null && baseType != typeof(ScriptableObject))
                {
                    if (baseType.IsGenericType && baseType.IsGenericTypeDefinition)
                    {
                        // We found a base like ResourceFolderAsyncResourceProvider<>
                        // The concrete type's generic args tell us the resource type
                        Type[] genArgs = null;
                        Type check = type.BaseType;
                        while (check != null && check != typeof(ScriptableObject)
                               && check != typeof(object))
                        {
                            if (check.IsGenericType && !check.IsGenericTypeDefinition
                                && check.GetGenericTypeDefinition() == baseType)
                            {
                                genArgs = check.GetGenericArguments();
                                break;
                            }
                            check = check.BaseType;
                        }

                        if (genArgs != null && genArgs.Length > 0)
                        {
                            string key = baseType.FullName + "|" + genArgs[0].FullName;
                            subclassMap[key] = type;
                        }
                    }
                    baseType = baseType.BaseType;
                }
            }

            s_cachedManagerEntries = managerEntries;
            s_cachedTemplateTypes = templateTypes;
            s_cachedConcreteSubclass = subclassMap;

            // Build interface → concrete types lookup for IAsyncResourceProvider<>
            var interfaceMap = new Dictionary<string, List<Type>>();
            foreach (Type type in IntegrationSettingsDrawingUtils.GetAllTypes())
            {
                if (!type.IsClass || type.IsAbstract) continue;
                if (!typeof(ScriptableObject).IsAssignableFrom(type)) continue;

                foreach (Type iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType) continue;
                    if (iface.GetGenericTypeDefinition() !=
                        typeof(IAsyncResourceProvider<>)) continue;

                    string key = iface.FullName;
                    if (string.IsNullOrEmpty(key)) continue;

                    if (!interfaceMap.TryGetValue(key, out var list))
                    {
                        list = new List<Type>();
                        interfaceMap[key] = list;
                    }
                    list.Add(type);
                }
            }
            s_cachedInterfaceTypes = interfaceMap;
        }

        private static List<ConcreteManagerInfo> FindAllResourceManagerTypes(
            IntegrationCentre centre)
        {
            EnsureCaches();

            // Build lookup of types attached to IntegrationCentre
            var attachedByType = new Dictionary<Type, ScriptableObject>();
            SerializedObject serializedCentre = new SerializedObject(centre);
            var listProp = serializedCentre.FindProperty("moduleScriptableObjects");
            if (listProp != null)
            {
                for (int i = 0; i < listProp.arraySize; i++)
                {
                    ScriptableObject module = listProp
                        .GetArrayElementAtIndex(i).objectReferenceValue
                        as ScriptableObject;
                    if (module == null) continue;
                    Type moduleType = module.GetType();
                    if (IsResourceManagerType(moduleType))
                        attachedByType[moduleType] = module;
                }
            }

            var results = new List<ConcreteManagerInfo>();
            foreach (var entry in s_cachedManagerEntries)
            {
                // Find existing .asset files on disk
                string[] guids = AssetDatabase.FindAssets("t:" + entry.ConcreteType.Name);
                var existingAssets = new List<ScriptableObject>();
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    ScriptableObject asset = AssetDatabase.LoadAssetAtPath(
                        path, entry.ConcreteType) as ScriptableObject;
                    if (asset != null) existingAssets.Add(asset);
                }

                attachedByType.TryGetValue(entry.ConcreteType,
                    out ScriptableObject attachedAsset);

                if (attachedAsset != null && !existingAssets.Contains(attachedAsset))
                    existingAssets.Insert(0, attachedAsset);

                results.Add(new ConcreteManagerInfo
                {
                    ConcreteType = entry.ConcreteType,
                    ResourceType = entry.ResourceType,
                    DisplayName = $"{entry.ConcreteType.Name} ({entry.ResourceType.Name})",
                    ExistingAssets = existingAssets,
                    AttachedAsset = attachedAsset
                });
            }

            return results;
        }

        private static List<ProviderTemplateEntry> FindProviderTemplates(
            ScriptableObject manager, Type resourceType)
        {
            EnsureCaches();

            // Build lookup of what's currently in initialProviders
            var assetToIndex = new Dictionary<ScriptableObject, int>();
            if (manager != null)
            {
                SerializedObject so = new SerializedObject(manager);
                var arrProp = so.FindProperty("initialProviders");
                if (arrProp != null)
                {
                    for (int i = 0; i < arrProp.arraySize; i++)
                    {
                        ScriptableObject p = arrProp
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue as ScriptableObject;
                        if (p != null) assetToIndex[p] = i;
                    }
                }
            }

            var results = new List<ProviderTemplateEntry>();

            foreach (Type templateType in s_cachedTemplateTypes)
            {
                var attr = templateType.GetCustomAttribute<ResourceProviderAttribute>();
                if (attr == null) continue;

                string key = templateType.FullName + "|" + resourceType.FullName;
                s_cachedConcreteSubclass.TryGetValue(key, out Type concreteType);

                ScriptableObject existingAsset = null;
                int arrayIndex = -1;

                if (concreteType != null)
                {
                    string[] guids = AssetDatabase.FindAssets(
                        "t:" + concreteType.Name);
                    if (guids.Length > 0)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        existingAsset = AssetDatabase.LoadAssetAtPath(
                            path, concreteType) as ScriptableObject;
                    }
                }

                if (existingAsset != null
                    && assetToIndex.TryGetValue(existingAsset, out int idx))
                {
                    arrayIndex = idx;
                }

                results.Add(new ProviderTemplateEntry
                {
                    Attribute = attr,
                    OpenGenericType = templateType,
                    ConcreteType = concreteType,
                    ExistingAsset = existingAsset,
                    IsInArray = arrayIndex >= 0,
                    ArrayIndex = arrayIndex
                });
            }

            return results;
        }

        private static List<DetachedProviderInfo> FindUnattachedProviders(
            ConcreteManagerInfo info, Type resourceType)
        {
            EnsureCaches();

            Type iface = typeof(IAsyncResourceProvider<>).MakeGenericType(
                resourceType);

            var inArray = new HashSet<ScriptableObject>();
            if (info.AttachedAsset != null)
            {
                SerializedObject serializedManager =
                    new SerializedObject(info.AttachedAsset);
                var providersProp = serializedManager
                    .FindProperty("initialProviders");
                if (providersProp != null)
                {
                    for (int i = 0; i < providersProp.arraySize; i++)
                    {
                        ScriptableObject so = providersProp
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue as ScriptableObject;
                        if (so != null) inArray.Add(so);
                    }
                }
            }

            var results = new List<DetachedProviderInfo>();

            string ifaceKey = iface.FullName;
            if (s_cachedInterfaceTypes != null
                && s_cachedInterfaceTypes.TryGetValue(ifaceKey, out var typeList))
            {
                foreach (Type type in typeList)
                {
                    if (type.GetCustomAttribute<ResourceProviderAttribute>() != null)
                        continue;

                    string[] guids = AssetDatabase.FindAssets("t:" + type.Name);
                    foreach (string guid in guids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        ScriptableObject asset = AssetDatabase.LoadAssetAtPath(
                            path, type) as ScriptableObject;
                        if (asset == null) continue;
                        if (inArray.Contains(asset)) continue;

                        results.Add(new DetachedProviderInfo
                        {
                            ConcreteType = type,
                            Asset = asset
                        });
                    }
                }
            }

            return results;
        }

        private static bool IsResourceManagerType(Type type)
        {
            Type baseType = type.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType &&
                    baseType.GetGenericTypeDefinition() ==
                    typeof(ResourceManagerBase<,>))
                {
                    return true;
                }
                baseType = baseType.BaseType;
            }
            return false;
        }

        private static Type GetResourceManagerResourceType(Type type)
        {
            // Walk up the inheritance chain to find the closed
            // ResourceManagerBase<SelfType, ResourceType>
            Type baseType = type.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType &&
                    baseType.GetGenericTypeDefinition() ==
                    typeof(ResourceManagerBase<,>))
                {
                    return baseType.GenericTypeArguments[1];
                }
                baseType = baseType.BaseType;
            }
            return null;
        }

        // ═══════════════════════════════════════════════════
        // ── Utilities ─────────────────────────────────────
        // ═══════════════════════════════════════════════════

        private static bool DrawSmallButton(string label, string tooltip)
        {
            return GUILayout.Button(
                new GUIContent(label, tooltip),
                EditorStyles.miniButton,
                GUILayout.Height(22)
            );
        }

        private static bool GetManagerStatus(ScriptableObject manager)
        {
            foreach (Type iface in manager.GetType().GetInterfaces())
            {
                if (!iface.IsGenericType) continue;
                if (iface.GetGenericTypeDefinition() != typeof(IResourceManager<>)) continue;

                var statusProp = iface.GetProperty("Status");
                if (statusProp == null) continue;

                object status = statusProp.GetValue(manager);
                return status is ResourceManagerStatus s
                    && s == ResourceManagerStatus.Initialized;
            }

            return false;
        }

        private static readonly Regex PascalCaseRegex = new Regex(
            @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
            RegexOptions.Compiled
        );

        private static string PascalCaseToSpaced(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return PascalCaseRegex.Replace(name, " ");
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
