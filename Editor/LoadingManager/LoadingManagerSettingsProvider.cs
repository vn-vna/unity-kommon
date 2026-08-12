using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager.Editor
{
    internal sealed class LoadingManagerSettingsProvider : SettingsProvider
    {
        #region Constants
        private const string SettingsPath = "Project/Tools/Loading Manager";
        private const string AssetPath = "Assets/Resources/Integration/Managers/LoadingConfiguration.asset";
        #endregion

        #region Constructors
        private LoadingManagerSettingsProvider(
            string path,
            SettingsScope scope,
            IEnumerable<string> keywords
        )
            : base(path, scope, keywords)
        {
        }
        #endregion

        #region SettingsProvider Registration
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new LoadingManagerSettingsProvider(
                SettingsPath,
                SettingsScope.Project,
                new[] { "loading", "progress", "load", "task", "group" }
            );
        }
        #endregion

        #region GUI
        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            EditorGUILayout.LabelField("Loading Manager Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Configure the task-group Loading Manager. The asset is loaded at runtime from " +
                "Resources/Integration/Managers/LoadingConfiguration.",
                MessageType.None
            );

            LoadingConfiguration settings = GetOrCreateSettings();
            if (settings == null)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(settings);
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("minimumLoadingTime"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("progressSmoothening"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("loadingTexts"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("loadingTextInterval"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultExecutionMode"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Save", GUILayout.Height(30)))
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
        #endregion

        #region Private Methods — Asset Management
        private static LoadingConfiguration GetOrCreateSettings()
        {
            LoadingConfiguration settings =
                AssetDatabase.LoadAssetAtPath<LoadingConfiguration>(AssetPath);
            if (settings != null)
            {
                return settings;
            }

            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "No LoadingConfiguration asset found. Click the button below to create one.",
                MessageType.Info
            );

            if (GUILayout.Button("Create LoadingConfiguration asset", GUILayout.Height(30)))
            {
                settings = CreateSettingsAsset();
            }

            return settings;
        }

        private static LoadingConfiguration CreateSettingsAsset()
        {
            string folder = Path.GetDirectoryName(AssetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                string leaf = Path.GetFileName(folder);
                AssetDatabase.CreateFolder(parent, leaf);
            }

            LoadingConfiguration settings =
                ScriptableObject.CreateInstance<LoadingConfiguration>();

            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LoadingManager] Created settings asset at '{AssetPath}'.");
            return settings;
        }
        #endregion
    }
}
