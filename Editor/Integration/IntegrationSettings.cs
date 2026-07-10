using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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

        private int _selectedTabIndex;

        internal BaseIntegrationSettingsProvider(
            string path,
            SettingsScope scopes,
            string sectionName,
            string managerLabel,
            IReadOnlyList<SettingsTab> tabs = null,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        {
            _sectionName = sectionName;
            _managerLabel = managerLabel;
            _tabs = tabs;
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
                ref _selectedTabIndex
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
                        new[] { "MaxSdk" }
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
                        new[] { "Firebase.Analytics.FirebaseAnalytics" }
                    ),
                    new ProviderDescriptor(
                        "Adjust",
                        "initialProviders",
                        ProviderBindingMode.Collection,
                        "Com.Hapiga.Scheherazade.Common.Integration.Tracking.AdjustTrackingProvider",
                        new[] { "TRACKING_ADJUST" },
                        new[] { "AdjustSdk.Adjust" }
                    ),
                    new ProviderDescriptor(
                        "AppMetrica",
                        "initialProviders",
                        ProviderBindingMode.Collection,
                        "Com.Hapiga.Scheherazade.Common.Integration.Tracking.AppMetricaTrackingProvider",
                        new[] { "TRACKING_APPMETRICA" },
                        new[] { "Io.AppMetrica.AppMetrica" }
                    )
                }
            )
        };

        private TrackingIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, "Tracking Manager Configuration", "Tracking Manager Asset",
            Tabs, keywords)
        { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new TrackingIntegrationSettingsProvider("Project/Integration/Tracking", SettingsScope.Project);
        }
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
                        new[] { "UnityEngine.Purchasing.StandardPurchasingModule" }
                    ),
                    new ProviderDescriptor(
                        "Pseudo Provider",
                        "provider",
                        ProviderBindingMode.Single,
                        "Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.PseudoInAppPurchaseProvider"
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
                        new[] { "Firebase.RemoteConfig.FirebaseRemoteConfig" }
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
                        "Com.Hapiga.Scheherazade.Common.Integration.IAR.OpenStoreInAppReviewModule"
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
                        "Com.Hapiga.Scheherazade.Common.Integration.Segmentation.CachedSegmentationProvider"
                    ),
                    new ProviderDescriptor(
                        "Adjust",
                        "initialProviders",
                        ProviderBindingMode.Collection,
                        "Com.Hapiga.Scheherazade.Common.Integration.Segmentation.AdjustSegmentationProvider",
                        new[] { "TRACKING_ADJUST" },
                        new[] { "AdjustSdk.Adjust" }
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
                        new[] { "Firebase.Analytics.FirebaseAnalytics" }
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
        private int _selectedManagerIndex;
        private int _selectedTabIndex;
        private ScriptableObject _pendingProviderToAdd;
        private Vector2 _scrollPosition;

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
            base.OnGUI(searchContext);

            IntegrationSettingsDrawingUtils.ProcessPendingProviderCreations();

            var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
            if (centre == null) return;

            var resourceManagers = FindResourceManagers(centre);

            if (resourceManagers.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No Async Resource Managers found in Integration Centre.\n\n" +
                    "Create a resource manager ScriptableObject, assign its providers, " +
                    "then add it to the Integration Centre's module list.",
                    MessageType.Info
                );
                return;
            }

            if (_selectedManagerIndex >= resourceManagers.Count)
                _selectedManagerIndex = 0;

            GUILayout.Space(4);
            EditorGUILayout.LabelField("Async Resource Manager Configuration", EditorStyles.boldLabel);

            GUILayout.Space(2);

            var managerNames = resourceManagers.Select(m => m.DisplayName).ToArray();
            _selectedManagerIndex = EditorGUILayout.Popup(
                "Resource Manager",
                _selectedManagerIndex,
                managerNames
            );

            var entry = resourceManagers[_selectedManagerIndex];

            GUILayout.Space(6);
            _selectedTabIndex = GUILayout.Toolbar(_selectedTabIndex, TabNames);
            GUILayout.Space(4);

            var dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(dividerRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_selectedTabIndex)
            {
                case 0:
                    DrawManagerConfig(entry);
                    break;
                case 1:
                    DrawProviders(entry);
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawManagerConfig(ResourceManagerEntry entry)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Resource Type", entry.ResourceType.Name);
                EditorGUILayout.LabelField("Manager Type", entry.ConcreteType.FullName);

                bool isInitialized = GetManagerStatus(entry.Manager);
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
                entry.Manager,
                $"{PascalCaseToSpaced(entry.Manager.name).ToUpperInvariant()} CONFIGURATION"
            );
        }

        private void DrawProviders(ResourceManagerEntry entry)
        {
            SerializedObject serializedManager = new SerializedObject(entry.Manager);
            var providersProp = serializedManager.FindProperty("initialProviders");

            if (providersProp == null || !providersProp.isArray)
            {
                EditorGUILayout.HelpBox(
                    "Could not find 'initialProviders' field on this manager.",
                    MessageType.Error
                );
                return;
            }

            bool anyVisible = false;
            for (int i = providersProp.arraySize - 1; i >= 0; i--)
            {
                var element = providersProp.GetArrayElementAtIndex(i);
                ScriptableObject provider = element.objectReferenceValue as ScriptableObject;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string displayName = provider != null
                            ? provider.name
                            : $"<missing> (slot {i})";

                        EditorGUILayout.LabelField(displayName, EditorStyles.miniBoldLabel);

                        if (DrawSmallButton("−", $"Remove provider at slot {i}"))
                        {
                            providersProp.DeleteArrayElementAtIndex(i);
                            serializedManager.ApplyModifiedProperties();
                            EditorUtility.SetDirty(entry.Manager);
                            AssetDatabase.SaveAssets();
                            break;
                        }
                    }

                    if (provider != null)
                    {
                        IntegrationSettingsDrawingUtils.DrawInlineInspector(
                            provider,
                            $"{PascalCaseToSpaced(provider.name).ToUpperInvariant()} PROVIDER"
                        );
                        anyVisible = true;
                    }
                }
            }

            if (!anyVisible && providersProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No providers assigned. Use the field below to add one.",
                    MessageType.Info
                );
            }

            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                _pendingProviderToAdd = EditorGUILayout.ObjectField(
                    "Add Provider",
                    _pendingProviderToAdd,
                    typeof(ScriptableObject),
                    false
                ) as ScriptableObject;

                if (_pendingProviderToAdd != null && GUILayout.Button("+", GUILayout.Width(24)))
                {
                    int newIndex = providersProp.arraySize++;
                    providersProp.GetArrayElementAtIndex(newIndex).objectReferenceValue =
                        _pendingProviderToAdd;
                    serializedManager.ApplyModifiedProperties();
                    EditorUtility.SetDirty(entry.Manager);
                    AssetDatabase.SaveAssets();
                    _pendingProviderToAdd = null;
                }
            }
        }

        private struct ResourceManagerEntry
        {
            public ScriptableObject Manager;
            public Type ConcreteType;
            public Type ResourceType;
            public string DisplayName;
        }

        private static List<ResourceManagerEntry> FindResourceManagers(IntegrationCentre centre)
        {
            List<ResourceManagerEntry> results = new List<ResourceManagerEntry>();
            SerializedObject serializedCentre = new SerializedObject(centre);
            var listProp = serializedCentre.FindProperty("moduleScriptableObjects");
            if (listProp == null) return results;

            for (int i = 0; i < listProp.arraySize; i++)
            {
                var element = listProp.GetArrayElementAtIndex(i);
                ScriptableObject module = element.objectReferenceValue as ScriptableObject;
                if (module == null) continue;

                foreach (Type iface in module.GetType().GetInterfaces())
                {
                    if (!iface.IsGenericType) continue;
                    if (iface.GetGenericTypeDefinition() != typeof(IResourceManager<>)) continue;

                    Type resourceType = iface.GenericTypeArguments[0];
                    results.Add(new ResourceManagerEntry
                    {
                        Manager = module,
                        ConcreteType = module.GetType(),
                        ResourceType = resourceType,
                        DisplayName = $"{PascalCaseToSpaced(module.name)} ({module.GetType().Name}<{resourceType.Name}>)"
                    });
                    break;
                }
            }

            return results;
        }

        private static bool DrawSmallButton(string label, string tooltip)
        {
            return GUILayout.Button(
                new GUIContent(label, tooltip),
                EditorStyles.miniButton,
                GUILayout.Width(22),
                GUILayout.Height(18)
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
                return status is ResourceManagerStatus s && s == ResourceManagerStatus.Initialized;
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

        public ProviderDescriptor(
            string displayName,
            string managerFieldName,
            ProviderBindingMode bindingMode,
            string providerTypeName,
            string[] requiredDefines = null,
            string[] dependencyTypeNames = null
        )
        {
            DisplayName = displayName;
            ManagerFieldName = managerFieldName;
            BindingMode = bindingMode;
            ProviderTypeName = providerTypeName;
            RequiredDefines = requiredDefines ?? Array.Empty<string>();
            DependencyTypeNames = dependencyTypeNames ?? Array.Empty<string>();
        }
    }

    [Serializable]
    internal sealed class SettingsTab
    {
        public string Name { get; }
        public ProviderDescriptor[] Descriptors { get; }

        public SettingsTab(
            string name,
            ProviderDescriptor[] descriptors = null
        )
        {
            Name = name;
            Descriptors = descriptors ?? Array.Empty<ProviderDescriptor>();
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
            ref int selectedTabIndex
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
                EditorGUILayout.LabelField(activeTab.Name, EditorStyles.boldLabel);

                if (activeTab.Descriptors != null && activeTab.Descriptors.Length > 0)
                {
                    foreach (var descriptor in activeTab.Descriptors)
                    {
                        DrawProviderSection(currentManager, descriptor);
                    }
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

        private static void SetModuleAsset(
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
            ProviderDescriptor descriptor
        )
        {
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

        private static void DeleteArrayElement(SerializedProperty arrayProp, int index)
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

        private static IEnumerable<Type> GetAllTypes()
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
