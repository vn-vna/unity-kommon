using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
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
        public AdsIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            IntegrationSettingsDrawingUtils.DrawRequiredScriptDefinition(
                new[] { "APPLOVIN_MAX" },
                IsMaxApplovinProviderPresent,
                "AppLovin MAX"
            );

            var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
            if (centre == null) return;

            ScriptableObject adsManager = null;
            var serializedCentre = new SerializedObject(centre);
            var listProp = serializedCentre.FindProperty("moduleScriptableObjects");
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var val = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (val is IAdsManager)
                {
                    adsManager = val as ScriptableObject;
                    break;
                }
            }

            IntegrationSettingsDrawingUtils.DrawManagerSettings<IAdsManager>(
                "Ads Manager Configuration",
                "Ads Manager Asset",
                "CoreLoop.BusFlow.AdsManager",
                ref adsManager,
                centre
            );
        }

        private bool IsMaxApplovinProviderPresent()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Any(type => type.Name == "MaxSdk");
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
        public TrackingIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
            if (centre == null) return;

            ScriptableObject trackingManager = null;
            var serializedCentre = new SerializedObject(centre);
            var listProp = serializedCentre.FindProperty("moduleScriptableObjects");
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var val = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (val is ITrackingManager)
                {
                    trackingManager = val as ScriptableObject;
                    break;
                }
            }

            IntegrationSettingsDrawingUtils.DrawManagerSettings<ITrackingManager>(
                "Tracking Manager Configuration",
                "Tracking Manager Asset",
                "CoreLoop.BusFlow.TrackingManager",
                ref trackingManager,
                centre
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
        public InAppPurchaseIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
            if (centre == null) return;

            ScriptableObject iapManager = null;
            var serializedCentre = new SerializedObject(centre);
            var listProp = serializedCentre.FindProperty("moduleScriptableObjects");
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var val = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (val is IInAppPurchaseManager)
                {
                    iapManager = val as ScriptableObject;
                    break;
                }
            }

            IntegrationSettingsDrawingUtils.DrawManagerSettings<IInAppPurchaseManager>(
                "In-App Purchase Manager Configuration",
                "In-App Purchase Manager Asset",
                "CoreLoop.BusFlow.InAppPurchaseManager",
                ref iapManager,
                centre
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
        public RemoteConfigIntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            var centre = IntegrationSettingsDrawingUtils.GetOrCreateIntegrationCentre();
            if (centre == null) return;

            ScriptableObject remoteConfigManager = null;
            var serializedCentre = new SerializedObject(centre);
            var listProp = serializedCentre.FindProperty("moduleScriptableObjects");
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var val = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (val is IRemoteConfigManager)
                {
                    remoteConfigManager = val as ScriptableObject;
                    break;
                }
            }

            IntegrationSettingsDrawingUtils.DrawManagerSettings<IRemoteConfigManager>(
                "Remote Config Manager Configuration",
                "Remote Config Manager Asset",
                "CoreLoop.BusFlow.RemoteConfigManager",
                ref remoteConfigManager,
                centre
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

    internal static class IntegrationSettingsDrawingUtils
    {
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
                    if (!System.IO.Directory.Exists("Assets/Resources"))
                    {
                        System.IO.Directory.CreateDirectory("Assets/Resources");
                    }
                    AssetDatabase.CreateAsset(centre, "Assets/Resources/IntegrationCentre.asset");
                    AssetDatabase.SaveAssets();
                }
            }
            return centre;
        }

        public static void DrawRequiredScriptDefinition(
            string[] defines, Func<bool> providerExistsFunc, string providerName
        )
        {
            if (!providerExistsFunc())
            {
                return;
            }

            bool isDefinePresent = defines.Any(define =>
                PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup)
                    .Split(';')
                    .Contains(define)
            );

            if (isDefinePresent)
            {
                EditorGUILayout.HelpBox(
                    "Detected scripting define symbols for the provider, but the provider's main class was not found. Please ensure that the provider is correctly integrated.",
                    MessageType.Info
                );
                return;
            }

            EditorGUILayout.HelpBox(
                $"{providerName} provider is not detected. Please ensure that the required scripting define symbols are added: {string.Join(", ", defines)}",
                MessageType.Warning
            );

            if (GUILayout.Button($"Add {providerName} Scripting Define Symbols"))
            {
                string currentDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
                List<string> defineList = new List<string>(currentDefines.Split(';'));
                foreach (string define in defines)
                {
                    if (!defineList.Contains(define))
                    {
                        defineList.Add(define);
                    }
                }

                PlayerSettings.SetScriptingDefineSymbolsForGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup,
                    string.Join(";", defineList)
                );
            }
        }

        public static void DrawManagerSettings<TInterface>(
            string sectionName,
            string label,
            string concreteTypeName,
            ref ScriptableObject currentManager,
            IntegrationCentre centre
        ) where TInterface : class
        {
            Type concreteType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                concreteType = assembly.GetType(concreteTypeName);
                if (concreteType != null) break;
            }

            if (concreteType == null)
            {
                EditorGUILayout.HelpBox($"Could not find concrete class type '{concreteTypeName}'. Please make sure it is compiled and present.", MessageType.Error);
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
                else
                {
                    if (foundIndex >= 0)
                    {
                        listProp.DeleteArrayElementAtIndex(foundIndex);
                        if (foundIndex < listProp.arraySize && listProp.GetArrayElementAtIndex(foundIndex).objectReferenceValue == null)
                        {
                            listProp.DeleteArrayElementAtIndex(foundIndex);
                        }
                    }
                }

                serializedCentre.ApplyModifiedProperties();
                EditorUtility.SetDirty(centre);
                AssetDatabase.SaveAssets();
                currentManager = newManager;
            }

            if (currentManager != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"{currentManager.name.ToUpper()} CONFIGURATION", EditorStyles.miniBoldLabel);
                var managerEditor = Editor.CreateEditor(currentManager);
                managerEditor.OnInspectorGUI();
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.HelpBox($"No {label} asset selected. Please create and assign a {concreteType.Name} asset.", MessageType.Info);
                if (GUILayout.Button($"Create New {label} Asset"))
                {
                    string path = EditorUtility.SaveFilePanelInProject($"Create {label}", concreteType.Name, "asset", "Save Manager Asset");
                    if (!string.IsNullOrEmpty(path))
                    {
                        var newAsset = ScriptableObject.CreateInstance(concreteType);
                        AssetDatabase.CreateAsset(newAsset, path);
                        AssetDatabase.SaveAssets();

                        var serializedCentre = new SerializedObject(centre);
                        var listProp = serializedCentre.FindProperty("moduleScriptableObjects");
                        int newIndex = listProp.arraySize++;
                        listProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = newAsset;
                        serializedCentre.ApplyModifiedProperties();
                        EditorUtility.SetDirty(centre);
                        AssetDatabase.SaveAssets();
                        currentManager = newAsset;
                    }
                }
            }
        }
    }
}