using System;
using System.Collections.Generic;
using Com.Scheherazade.Common.Editor.Toolkit;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.Editor.CustomHierarchy
{
    internal static class CustomHierarchyViewPreferences
    {
        #region Constants

        private const string EnabledKey = "Enabled";
        private const string IndentWidthKey = "IndentWidth";
        private const string GuideLinesEnabledKey = "GuideLinesEnabled";
        private const string GuideLineColorKey = "GuideLineColor";
        private const string ZebraBackgroundEnabledKey = "ZebraBackgroundEnabled";
        private const string ZebraBackgroundColorKey = "ZebraBackgroundColor";
        private const string CompactModeKey = "CompactMode";

        internal const float MinimumIndentWidth = 8f;
        internal const float MaximumIndentWidth = 32f;
        internal const float DefaultIndentWidth = 14f;

        #endregion

        #region Events & Delegates

        internal static event Action Changed;

        #endregion

        #region Properties

        internal static bool Enabled
        {
            get => Store.GetBool(EnabledKey, true);
            set => SetBool(EnabledKey, value);
        }

        internal static float IndentWidth
        {
            get => Mathf.Clamp(
                Store.GetFloat(IndentWidthKey, DefaultIndentWidth),
                MinimumIndentWidth,
                MaximumIndentWidth
            );
            set => SetFloat(
                IndentWidthKey,
                Mathf.Clamp(value, MinimumIndentWidth, MaximumIndentWidth)
            );
        }

        internal static bool GuideLinesEnabled
        {
            get => Store.GetBool(GuideLinesEnabledKey, true);
            set => SetBool(GuideLinesEnabledKey, value);
        }

        internal static Color GuideLineColor
        {
            get => GetColor(GuideLineColorKey, DefaultGuideLineColor);
            set => SetColor(GuideLineColorKey, value);
        }

        internal static bool ZebraBackgroundEnabled
        {
            get => Store.GetBool(ZebraBackgroundEnabledKey, true);
            set => SetBool(ZebraBackgroundEnabledKey, value);
        }

        internal static Color ZebraBackgroundColor
        {
            get => GetColor(ZebraBackgroundColorKey, DefaultZebraBackgroundColor);
            set => SetColor(ZebraBackgroundColorKey, value);
        }

        internal static bool CompactMode
        {
            get => Store.GetBool(CompactModeKey, false);
            set => SetBool(CompactModeKey, value);
        }

        private static Color DefaultGuideLineColor => EditorGUIUtility.isProSkin
            ? new Color(0.55f, 0.55f, 0.55f, 0.42f)
            : new Color(0.25f, 0.25f, 0.25f, 0.32f);

        private static Color DefaultZebraBackgroundColor => EditorGUIUtility.isProSkin
            ? new Color(0f, 0f, 0f, 0.12f)
            : new Color(0f, 0f, 0f, 0.055f);

        #endregion

        #region Private Fields

        private static readonly EditorPrefsStore Store = new EditorPrefsStore(
            "Com.Scheherazade.CustomHierarchyView"
        );

        #endregion

        #region Public Methods

        internal static void ResetToDefaults()
        {
            Store.Delete(EnabledKey);
            Store.Delete(IndentWidthKey);
            Store.Delete(GuideLinesEnabledKey);
            Store.Delete(GuideLineColorKey);
            Store.Delete(ZebraBackgroundEnabledKey);
            Store.Delete(ZebraBackgroundColorKey);
            Store.Delete(CompactModeKey);
            Changed?.Invoke();
        }

        #endregion

        #region Private Methods

        private static Color GetColor(string key, Color defaultValue)
        {
            string defaultHtml = ColorUtility.ToHtmlStringRGBA(defaultValue);
            string html = Store.Get(key, defaultHtml);
            return ColorUtility.TryParseHtmlString("#" + html, out Color color)
                ? color
                : defaultValue;
        }

        private static void SetBool(string key, bool value)
        {
            if (Store.GetBool(key, value) == value && Store.HasKey(key))
                return;

            Store.SetBool(key, value);
            Changed?.Invoke();
        }

        private static void SetFloat(string key, float value)
        {
            if (Store.HasKey(key) && Mathf.Approximately(Store.GetFloat(key), value))
                return;

            Store.SetFloat(key, value);
            Changed?.Invoke();
        }

        private static void SetColor(string key, Color value)
        {
            string html = ColorUtility.ToHtmlStringRGBA(value);
            if (Store.HasKey(key) && Store.Get(key) == html)
                return;

            Store.Set(key, html);
            Changed?.Invoke();
        }

        #endregion
    }

    internal sealed class CustomHierarchyViewSettingsProvider : SettingsProvider
    {
        #region Constants

        private const string SettingsPath = "Preferences/Custom Hierarchy View";

        #endregion

        #region Constructor

        private CustomHierarchyViewSettingsProvider(
            string path,
            SettingsScope scope,
            IEnumerable<string> keywords
        ) : base(path, scope, keywords)
        {
        }

        #endregion

        #region SettingsProvider Registration

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new CustomHierarchyViewSettingsProvider(
                SettingsPath,
                SettingsScope.User,
                new[]
                {
                    "hierarchy", "indentation", "guide", "line", "zebra", "compact"
                }
            );
        }

        #endregion

        #region GUI

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.LabelField("Custom Hierarchy View", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "Personal editor settings are saved in EditorPrefs and applied immediately.",
                MessageType.Info
            );
            EditorGUILayout.Space(6f);

            CustomHierarchyViewPreferences.Enabled = EditorGUILayout.Toggle(
                "Enabled",
                CustomHierarchyViewPreferences.Enabled
            );

            EditorGUILayout.Space(4f);
            DrawLayoutSection();
            DrawGuideLinesSection();
            DrawZebraBackgroundSection();

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Reset to Defaults", GUILayout.Width(140f)))
                CustomHierarchyViewPreferences.ResetToDefaults();
        }

        private static void DrawLayoutSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);

            CustomHierarchyViewPreferences.IndentWidth = EditorGUILayout.Slider(
                "Indentation",
                CustomHierarchyViewPreferences.IndentWidth,
                CustomHierarchyViewPreferences.MinimumIndentWidth,
                CustomHierarchyViewPreferences.MaximumIndentWidth
            );

            CustomHierarchyViewPreferences.CompactMode = EditorGUILayout.Toggle(
                "Compact Mode",
                CustomHierarchyViewPreferences.CompactMode
            );
            EditorGUILayout.EndVertical();
        }

        private static void DrawGuideLinesSection()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Guide Lines", EditorStyles.boldLabel);

            CustomHierarchyViewPreferences.GuideLinesEnabled = EditorGUILayout.Toggle(
                "Show Guide Lines",
                CustomHierarchyViewPreferences.GuideLinesEnabled
            );

            using (new EditorGUI.DisabledScope(!CustomHierarchyViewPreferences.GuideLinesEnabled))
            {
                CustomHierarchyViewPreferences.GuideLineColor = EditorGUILayout.ColorField(
                    new GUIContent("Color"),
                    CustomHierarchyViewPreferences.GuideLineColor,
                    true,
                    true,
                    false
                );
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawZebraBackgroundSection()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Zebra Background", EditorStyles.boldLabel);

            CustomHierarchyViewPreferences.ZebraBackgroundEnabled = EditorGUILayout.Toggle(
                "Show Alternating Rows",
                CustomHierarchyViewPreferences.ZebraBackgroundEnabled
            );

            using (new EditorGUI.DisabledScope(!CustomHierarchyViewPreferences.ZebraBackgroundEnabled))
            {
                CustomHierarchyViewPreferences.ZebraBackgroundColor = EditorGUILayout.ColorField(
                    new GUIContent("Color"),
                    CustomHierarchyViewPreferences.ZebraBackgroundColor,
                    true,
                    true,
                    false
                );
            }

            EditorGUILayout.EndVertical();
        }

        #endregion
    }
}
