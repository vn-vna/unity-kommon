using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEngine;
using Com.Hapiga.Scheherazade.Common.Integration;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase;
using Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig;

namespace Com.Hapiga.Scheherazade.Integration
{
    public class AdsIntegrationSettingsProvider : SettingsProvider
    {
        private static readonly ProviderDescriptor[] ProviderDescriptors =
        {
            new ProviderDescriptor(
                "AppLovin MAX",
                "adServiceProvider",
                ProviderBindingMode.Single,
                "Com.Hapiga.Scheherazade.Common.Integration.Ads.ApplovinMaxAdsServiceProvider",
                new[] { "APPLOVIN_MAX" },
                new[] { "MaxSdk" }
            )
        };

        public AdsIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            IntegrationSettingsDrawingUtils.ProcessPendingProviderCreations();

            var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
            if (centre == null) return;

            ScriptableObject adsManager = IntegrationSettingsDrawingUtils.FindModuleAsset<IAdsManager>(centre);

            IntegrationSettingsDrawingUtils.DrawManagerSettings<IAdsManager>(
                "Ads Manager Configuration",
                "Ads Manager Asset",
                "CoreLoop.BusFlow.AdsManager",
                ref adsManager,
                centre,
                ProviderDescriptors
            );
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new AdsIntegrationSettingsProvider(
                "Project/Integration/Ads",
                SettingsScope.Project
            );
        }
    }

    public class TrackingIntegrationSettingsProvider : SettingsProvider
    {
        private static readonly ProviderDescriptor[] ProviderDescriptors =
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
        };

        public TrackingIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            IntegrationSettingsDrawingUtils.ProcessPendingProviderCreations();

            var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
            if (centre == null) return;

            ScriptableObject trackingManager = IntegrationSettingsDrawingUtils.FindModuleAsset<ITrackingManager>(centre);

            IntegrationSettingsDrawingUtils.DrawManagerSettings<ITrackingManager>(
                "Tracking Manager Configuration",
                "Tracking Manager Asset",
                "CoreLoop.BusFlow.TrackingManager",
                ref trackingManager,
                centre,
                ProviderDescriptors
            );
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new TrackingIntegrationSettingsProvider(
                "Project/Integration/Tracking",
                SettingsScope.Project
            );
        }
    }

    public class InAppPurchaseIntegrationSettingsProvider : SettingsProvider
    {
        private static readonly ProviderDescriptor[] ProviderDescriptors =
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
        };

        public InAppPurchaseIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            IntegrationSettingsDrawingUtils.ProcessPendingProviderCreations();

            var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
            if (centre == null) return;

            ScriptableObject iapManager = IntegrationSettingsDrawingUtils.FindModuleAsset<IInAppPurchaseManager>(centre);

            IntegrationSettingsDrawingUtils.DrawManagerSettings<IInAppPurchaseManager>(
                "In-App Purchase Manager Configuration",
                "In-App Purchase Manager Asset",
                "CoreLoop.BusFlow.InAppPurchaseManager",
                ref iapManager,
                centre,
                ProviderDescriptors
            );
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new InAppPurchaseIntegrationSettingsProvider(
                "Project/Integration/In-App Purchase",
                SettingsScope.Project
            );
        }
    }

    public class RemoteConfigIntegrationSettingsProvider : SettingsProvider
    {
        private static readonly ProviderDescriptor[] ProviderDescriptors =
        {
            new ProviderDescriptor(
                "Firebase Remote Config",
                "initialProviders",
                ProviderBindingMode.Collection,
                "Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig.FirebaseRemoteConfigProvider",
                new[] { "FIREBASE_REMOTE" },
                new[] { "Firebase.RemoteConfig.FirebaseRemoteConfig" }
            )
        };

        public RemoteConfigIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            IntegrationSettingsDrawingUtils.ProcessPendingProviderCreations();

            var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
            if (centre == null) return;

            ScriptableObject remoteConfigManager = IntegrationSettingsDrawingUtils.FindModuleAsset<IRemoteConfigManager>(centre);

            IntegrationSettingsDrawingUtils.DrawManagerSettings<IRemoteConfigManager>(
                "Remote Config Manager Configuration",
                "Remote Config Manager Asset",
                "CoreLoop.BusFlow.RemoteConfigManager",
                ref remoteConfigManager,
                centre,
                ProviderDescriptors
            );
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new RemoteConfigIntegrationSettingsProvider(
                "Project/Integration/Remote Config",
                SettingsScope.Project
            );
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

        public static IntegrationCentre GetOrCreateIntegrationCentre()
        {
            var centre = IntegrationCentre.Instance;
            if (centre == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:IntegrationCentre");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    centre = AssetDatabase.LoadAssetAtPath<IntegrationCentre>(path);
                }
                else
                {
                    centre = ScriptableObject.CreateInstance<IntegrationCentre>();
                    if (!Directory.Exists("Assets/Resources"))
                    {
                        Directory.CreateDirectory("Assets/Resources");
                    }
                    AssetDatabase.CreateAsset(centre, "Assets/Resources/IntegrationCentre.asset");
                    AssetDatabase.SaveAssets();
                }
            }
            return centre;
        }

        public static ScriptableObject FindModuleAsset<TInterface>(IntegrationCentre centre)
            where TInterface : class
        {
            var serializedCentre = new SerializedObject(centre);
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

        public static void DrawManagerSettings<TInterface>(
            string sectionName,
            string label,
            string concreteTypeName,
            ref ScriptableObject currentManager,
            IntegrationCentre centre,
            IReadOnlyList<ProviderDescriptor> providerDescriptors = null
        ) where TInterface : class
        {
            Type concreteType = ResolveType(concreteTypeName);
            if (concreteType == null)
            {
                EditorGUILayout.HelpBox(
                    $"Could not find concrete class type '{concreteTypeName}'. Please make sure it is compiled and present.",
                    MessageType.Error
                );
                return;
            }

            GUILayout.Space(10);
            EditorGUILayout.LabelField(sectionName, EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newManager = EditorGUILayout.ObjectField(
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

            if (currentManager != null)
            {
                DrawInlineInspector(
                    currentManager,
                    $"{currentManager.name.ToUpperInvariant()} CONFIGURATION"
                );

                if (providerDescriptors != null && providerDescriptors.Count > 0)
                {
                    GUILayout.Space(10);
                    EditorGUILayout.LabelField("Providers", EditorStyles.boldLabel);
                    foreach (var providerDescriptor in providerDescriptors)
                    {
                        DrawProviderSection(currentManager, providerDescriptor);
                    }
                }
            }
            else
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

                    var newAsset = ScriptableObject.CreateInstance(concreteType);
                    AssetDatabase.CreateAsset(newAsset, path);
                    AssetDatabase.SaveAssets();
                    SetModuleAsset(centre, currentManager, newAsset);
                    currentManager = newAsset;
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
            var serializedCentre = new SerializedObject(centre);
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

        private static void DrawProviderSection(
            ScriptableObject manager,
            ProviderDescriptor descriptor
        )
        {
            GUILayout.Space(6);
            EditorGUILayout.LabelField(descriptor.DisplayName, EditorStyles.boldLabel);

            var serializedManager = new SerializedObject(manager);
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

            if (providerType != null)
            {
                if (!typeof(ScriptableObject).IsAssignableFrom(providerType))
                {
                    EditorGUILayout.HelpBox(
                        $"{descriptor.DisplayName} provider type is not a ScriptableObject and cannot be assigned inline.",
                        MessageType.Error
                    );
                    return;
                }

                EditorGUI.BeginChangeCheck();
                var updatedProvider = EditorGUILayout.ObjectField(
                    "Asset",
                    currentProvider,
                    providerType,
                    false
                ) as ScriptableObject;

                if (EditorGUI.EndChangeCheck())
                {
                    SetProviderAssignment(fieldProp, descriptor, providerType, updatedProvider);
                    serializedManager.ApplyModifiedProperties();
                    EditorUtility.SetDirty(manager);
                    AssetDatabase.SaveAssets();
                    currentProvider = updatedProvider;
                }
            }

            if (currentProvider != null)
            {
                DrawInlineInspector(
                    currentProvider,
                    $"{descriptor.DisplayName.ToUpperInvariant()} PROVIDER"
                );
                return;
            }

            DrawMissingProviderBox(manager, descriptor, providerType);
        }

        private static void DrawMissingProviderBox(
            ScriptableObject manager,
            ProviderDescriptor descriptor,
            Type providerType
        )
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var dependencyStates = descriptor.DependencyTypeNames
                    .Select(typeName => new DependencyStatus(typeName, ResolveType(typeName) != null))
                    .ToArray();
                bool dependenciesSatisfied = dependencyStates.All(status => status.IsAvailable);
                bool providerTypeAvailable = providerType != null;
                string[] missingDefines = GetMissingDefines(descriptor.RequiredDefines);

                string dependencySummary = dependencyStates.Length == 0
                    ? "No external dependencies are required."
                    : string.Join(
                        "\n",
                        dependencyStates.Select(status => $"{(status.IsAvailable ? "[OK]" : "[Missing]")} {status.DisplayName}")
                    );

                MessageType messageType = dependenciesSatisfied
                    ? MessageType.Info
                    : MessageType.Warning;
                string message =
                    $"{descriptor.DisplayName} provider is not assigned.\n\n" +
                    $"{dependencySummary}";

                if (dependenciesSatisfied && missingDefines.Length > 0)
                {
                    message += $"\n\nMissing scripting defines: {string.Join(", ", missingDefines)}";
                }
                else if (dependenciesSatisfied && !providerTypeAvailable)
                {
                    message += "\n\nThe dependency is available, but the provider type is still not compiled. If you just enabled the define, wait for Unity to finish recompiling.";
                    messageType = MessageType.Warning;
                }

                EditorGUILayout.HelpBox(message, messageType);

                if (!dependenciesSatisfied)
                {
                    return;
                }

                if (providerTypeAvailable)
                {
                    string buttonLabel = missingDefines.Length > 0
                        ? $"Enable {descriptor.DisplayName} and Create Provider Asset"
                        : $"Create {descriptor.DisplayName} Provider Asset";
                    if (GUILayout.Button(buttonLabel))
                    {
                        EnsureScriptingDefines(descriptor.RequiredDefines);
                        CreateAndAssignProviderAsset(manager, descriptor, providerType);
                    }
                    return;
                }

                if (missingDefines.Length == 0)
                {
                    return;
                }

                if (GUILayout.Button($"Enable {descriptor.DisplayName} and Create Provider Asset"))
                {
                    EnqueuePendingProviderCreation(manager, descriptor);
                    EnsureScriptingDefines(descriptor.RequiredDefines);
                }
            }
        }

        private static ScriptableObject GetAssignedProvider(
            SerializedProperty fieldProp,
            ProviderDescriptor descriptor,
            Type providerType
        )
        {
            if (descriptor.BindingMode == ProviderBindingMode.Single)
            {
                var assignedProvider = fieldProp.objectReferenceValue as ScriptableObject;
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
                var value = fieldProp.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableObject;
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
                var value = fieldProp.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableObject;
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

        private static void DrawInlineInspector(ScriptableObject asset, string header)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(header, EditorStyles.miniBoldLabel);
                var editor = Editor.CreateEditor(asset);
                try
                {
                    editor.OnInspectorGUI();
                }
                finally
                {
                    if (editor != null)
                    {
                        UnityEngine.Object.DestroyImmediate(editor);
                    }
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

            string folder = Path.GetDirectoryName(managerPath)?.Replace('\\', '/');
            string assetName = $"{SanitizeAssetName(manager.name)}_{SanitizeAssetName(providerType.Name)}.asset";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{assetName}");

            var providerAsset = ScriptableObject.CreateInstance(providerType);
            providerAsset.name = providerType.Name;
            AssetDatabase.CreateAsset(providerAsset, assetPath);

            var serializedManager = new SerializedObject(manager);
            var fieldProp = serializedManager.FindProperty(descriptor.ManagerFieldName);
            if (fieldProp == null)
            {
                UnityEngine.Object.DestroyImmediate(providerAsset);
                AssetDatabase.DeleteAsset(assetPath);
                EditorUtility.DisplayDialog(
                    "Provider Assignment Failed",
                    $"Could not find manager field '{descriptor.ManagerFieldName}' on {manager.GetType().Name}.",
                    "OK"
                );
                return;
            }

            SetProviderAssignment(fieldProp, descriptor, providerType, providerAsset);
            serializedManager.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();

            EditorGUIUtility.PingObject(providerAsset);
            Selection.activeObject = providerAsset;
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

            var descriptor = new ProviderDescriptor(
                request.providerDisplayName,
                request.managerFieldName,
                request.bindingMode,
                request.providerTypeName
            );

            var serializedManager = new SerializedObject(manager);
            var fieldProp = serializedManager.FindProperty(request.managerFieldName);
            if (fieldProp == null)
            {
                return true;
            }

            if (GetAssignedProvider(fieldProp, descriptor, providerType) != null)
            {
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

        private static string[] GetMissingDefines(IReadOnlyList<string> requiredDefines)
        {
            if (requiredDefines == null || requiredDefines.Count == 0)
            {
                return Array.Empty<string>();
            }

            var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup
            );
            var currentDefines = new HashSet<string>(
                PlayerSettings
                    .GetScriptingDefineSymbols(namedBuildTarget)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            );

            return requiredDefines
                .Where(requiredDefine => !currentDefines.Contains(requiredDefine))
                .ToArray();
        }

        private static void EnsureScriptingDefines(IEnumerable<string> requiredDefines)
        {
            if (requiredDefines == null)
            {
                return;
            }

            var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup
            );
            var defineList = new List<string>(
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

        private static Type ResolveType(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
            {
                return null;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var directType = assembly.GetType(fullTypeName);
                if (directType != null)
                {
                    return directType;
                }
            }

            return GetAllTypes()
                .FirstOrDefault(type =>
                    string.Equals(type.FullName, fullTypeName, StringComparison.Ordinal));
        }

        private static IEnumerable<Type> GetAllTypes()
        {
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

                foreach (var type in types)
                {
                    yield return type;
                }
            }
        }

        private static string SanitizeAssetName(string assetName)
        {
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                assetName = assetName.Replace(invalidCharacter, '_');
            }

            return assetName.Replace(' ', '_');
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
