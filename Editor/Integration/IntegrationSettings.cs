using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Integration
{

    public class IntegrationSettingsProvider : SettingsProvider
    {
        private IntegrationSettingsTab _currentTab = IntegrationSettingsTab.Ads;

        public IntegrationSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }

        public void OnEnable()
        {
            Debug.Log("IntegrationSettingsProvider enabled");
        }

        public override void OnGUI(string searchContext)
        {
            _currentTab = (IntegrationSettingsTab)GUILayout.Toolbar(
                (int)_currentTab,
                Enum.GetNames(typeof(IntegrationSettingsTab))
            );

            GUILayout.Space(10);

            switch (_currentTab)
            {
                case IntegrationSettingsTab.Ads:
                    DrawAdsSettings();
                    break;
                case IntegrationSettingsTab.Tracking:
                    DrawTrackingSettings();
                    break;
            }
        }

        private void DrawAdsSettings()
        {
        }

        private void DrawTrackingSettings()
        {
            EditorGUILayout.HelpBox(
                "Tracking providers can be configured in the Tracking Manager component in the scene. " +
                "Please open the scene containing the Tracking Manager to configure tracking providers.",
                MessageType.Info
            );
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new IntegrationSettingsProvider(
                "Project/Integration",
                SettingsScope.Project
            );

            return provider;
        }

        public enum IntegrationSettingsTab
        {
            Ads,
            Tracking
        }
    }

}