#if APPLOVIN_MAX

using System;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Editor.Toolkit;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Integration
{
    [CustomEditor(typeof(ApplovinMaxAdsServiceProvider))]
    internal sealed class ApplovinMaxAdsServiceProviderEditor : UnityEditor.Editor
    {
        #region Constants

        private const string PlatformTabPrefKey =
            "ApplovinMaxAdsServiceProviderEditor_PlatformTab";

        private const string RetryTabPrefKey =
            "ApplovinMaxAdsServiceProviderEditor_RetryTab";

        private const float BadgeWidth = 22f;
        private const float DeleteButtonWidth = 22f;
        private const float Spacing = 4f;
        private const float TypeDropdownRatio = 0.35f;
        private const float MaxScrollViewHeight = 150f;
        private const float SectionSpace = 8f;
        private const float ChipWidth = 64f;
        private const float ChipHeight = 18f;
        private const float ToolbarHeight = 24f;
        private const float ToggleButtonWidth = 90f;

        private static readonly string[] RetryTabNames =
            { "Open App", "Interstitial", "Rewarded", "Banner" };

        private static readonly string[] AdsTypeNames =
            Enum.GetNames(typeof(AdsType));

        private static readonly string[] RetryConfigFieldNames =
        {
            "openAppRetryConfig",
            "interstitialRetryConfig",
            "rewardedRetryConfig",
            "bannerRetryConfig"
        };

        private static readonly Color TestAdsChipColor = new Color(0.9f, 0.5f, 0.1f, 0.9f);
        private static readonly Color BadgeColor = new Color(0.25f, 0.45f, 0.65f, 0.8f);
        private static readonly Color BadgeWarningColor = new Color(1f, 0.6f, 0.1f, 0.8f);
        private static readonly Color SeparatorColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);

        #endregion

        #region Private Fields

        private SerializedProperty _isTestAdsProp;
        private SerializedProperty _enabledAdsProp;
        private SerializedProperty _unitIdsProp;
        private SerializedProperty _bannerPositionProp;
        private SerializedProperty _bannerAutoSizedProp;
        private SerializedProperty _bannerBackgroundColorProp;
        private SerializedProperty[] _retryConfigProps;

        private GUIContent[] _platformTabContents;
        private int _selectedPlatformIndex;
        private int _selectedRetryTabIndex;
        private Vector2 _unitIdsScrollPosition;

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            _isTestAdsProp = serializedObject.FindProperty("isTestAds");
            _enabledAdsProp = serializedObject.FindProperty("enabledAds");
            _unitIdsProp = serializedObject.FindProperty("unitIds");
            _bannerPositionProp = serializedObject.FindProperty("bannerAdsDisplayPosition");
            _bannerAutoSizedProp = serializedObject.FindProperty("bannerAutoSized");
            _bannerBackgroundColorProp = serializedObject.FindProperty("bannerBackgroundColor");

            _retryConfigProps = RetryConfigFieldNames
                .Select(serializedObject.FindProperty)
                .ToArray();

            _platformTabContents = CreatePlatformTabContents();

            _selectedPlatformIndex = EditorPrefs.GetInt(PlatformTabPrefKey, 0);
            _selectedRetryTabIndex = EditorPrefs.GetInt(RetryTabPrefKey, 0);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTestAdsField();
            DrawEnabledAds();
            DrawSectionSeparator();
            DrawUnitIdsSection();
            DrawSectionSeparator();
            DrawBannerSection();
            DrawSectionSeparator();
            DrawRetrySection();

            serializedObject.ApplyModifiedProperties();
        }

        #endregion

        #region Private Methods

        private void DrawTestAdsField()
        {
            if (_isTestAdsProp != null)
            {
                EditorGUILayout.PropertyField(_isTestAdsProp);
            }
        }

        private void DrawEnabledAds()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    "Enabled Ads",
                    GUILayout.Width(EditorGuiLayout.DefaultLabelWidth));

                ToggleFlagButton(ApplovinMaxAdsEnabledAds.Interstitial, "Interstitial");
                ToggleFlagButton(ApplovinMaxAdsEnabledAds.Banner, "Banner");
                ToggleFlagButton(ApplovinMaxAdsEnabledAds.OpenApp, "Open App");
                ToggleFlagButton(ApplovinMaxAdsEnabledAds.Rewarded, "Rewarded");
            }
        }

        private void ToggleFlagButton(ApplovinMaxAdsEnabledAds flag, string label)
        {
            bool isActive = _enabledAdsProp != null
                && (_enabledAdsProp.intValue & (int)flag) != 0;

            bool newActive = GUILayout.Toggle(
                isActive,
                label,
                EditorStyles.miniButton,
                GUILayout.Width(ToggleButtonWidth));

            if (_enabledAdsProp != null && newActive != isActive)
            {
                int newValue = newActive
                    ? _enabledAdsProp.intValue | (int)flag
                    : _enabledAdsProp.intValue & ~(int)flag;
                _enabledAdsProp.intValue = newValue;
            }
        }

        private void DrawUnitIdsSection()
        {
            EditorGuiLayout.DrawSectionHeader(
                "Ads Unit Ids",
                "Map ad unit IDs per format and platform.");

            int newPlatform = GUILayout.Toolbar(
                _selectedPlatformIndex,
                _platformTabContents,
                GUILayout.Height(ToolbarHeight));
            if (newPlatform != _selectedPlatformIndex)
            {
                _selectedPlatformIndex = newPlatform;
                EditorPrefs.SetInt(PlatformTabPrefKey, _selectedPlatformIndex);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                        "Fill",
                        EditorStyles.miniButton,
                        GUILayout.Width(40)))
                {
                    FillMissingUnitIds();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button(
                        "Clear",
                        EditorStyles.miniButton,
                        GUILayout.Width(50)))
                {
                    _unitIdsProp.ClearArray();
                    GUIUtility.ExitGUI();
                }

                GUILayout.FlexibleSpace();

                EditorGUILayout.LabelField(
                    _selectedPlatformIndex == 0 ? "Android unit IDs" : "iOS unit IDs",
                    EditorStyles.miniLabel);
            }

            if (HasDuplicateUnitIdTypes())
            {
                EditorGUILayout.HelpBox(
                    "Duplicate ad types found. Only the first entry is used at runtime.",
                    MessageType.Warning);
            }

            DrawUnitIdsTable();
        }

        private void DrawUnitIdsTable()
        {
            float rowHeight = EditorGUIUtility.singleLineHeight;
            float contentHeight = _unitIdsProp.arraySize * rowHeight;
            float scrollViewHeight = Mathf.Clamp(
                contentHeight + EditorGUIUtility.singleLineHeight,
                rowHeight,
                MaxScrollViewHeight);

            _unitIdsScrollPosition = EditorGUILayout.BeginScrollView(
                _unitIdsScrollPosition,
                GUILayout.Height(scrollViewHeight));

            for (int i = 0; i < _unitIdsProp.arraySize; i++)
            {
                if (DrawUnitIdEntry(i))
                {
                    break;
                }
            }

            if (_unitIdsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No ad unit IDs configured. Click 'Fill' to add all ad types.",
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private bool DrawUnitIdEntry(int index)
        {
            SerializedProperty element = _unitIdsProp.GetArrayElementAtIndex(index);
            SerializedProperty typeProp = element.FindPropertyRelative("type");
            SerializedProperty androidIdProp = element.FindPropertyRelative("androidUnitId");
            SerializedProperty iosIdProp = element.FindPropertyRelative("iosUnitId");

            if (typeProp == null || androidIdProp == null || iosIdProp == null)
            {
                EditorGUILayout.LabelField($"Entry {index}: invalid format");
                return false;
            }

            float lineHeight = EditorGUIUtility.singleLineHeight;
            Rect lineRect = EditorGUILayout.GetControlRect(true, lineHeight);

            float x = lineRect.x;
            float remainingWidth = lineRect.width - BadgeWidth - DeleteButtonWidth - Spacing * 3;
            float typeWidth = remainingWidth * TypeDropdownRatio;
            float idWidth = remainingWidth * (1f - TypeDropdownRatio);

            Rect badgeRect = new Rect(x, lineRect.y, BadgeWidth, lineHeight);
            SerializedProperty platformIdProp = _selectedPlatformIndex == 0
                ? androidIdProp
                : iosIdProp;
            DrawUnitIdBadge(badgeRect, index + 1, platformIdProp);
            x += BadgeWidth + Spacing;

            Rect typeRect = new Rect(x, lineRect.y, typeWidth, lineHeight);
            int newType = EditorGUI.Popup(typeRect, typeProp.intValue, AdsTypeNames);
            if (newType != typeProp.intValue)
            {
                typeProp.intValue = newType;
            }
            x += typeWidth + Spacing;

            Rect idRect = new Rect(x, lineRect.y, idWidth, lineHeight);
            EditorGUI.BeginChangeCheck();
            string newValue = EditorGUI.TextField(idRect, platformIdProp.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                platformIdProp.stringValue = newValue;
            }
            x += idWidth + Spacing;

            Rect deleteRect = new Rect(x, lineRect.y, DeleteButtonWidth, lineHeight);
            if (GUI.Button(deleteRect, "\u2717", EditorStyles.miniButton))
            {
                _unitIdsProp.DeleteArrayElementAtIndex(index);
                return true;
            }

            return false;
        }

        private static void DrawUnitIdBadge(
            Rect rect,
            int number,
            SerializedProperty platformIdProp)
        {
            bool isMissing = platformIdProp == null
                || string.IsNullOrEmpty(platformIdProp.stringValue);

            Color backgroundColor = isMissing ? BadgeWarningColor : BadgeColor;
            EditorGUI.DrawRect(rect, backgroundColor);

            string tooltip = isMissing
                ? "Unit ID is not set for the selected platform"
                : null;

            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            GUIContent content = new GUIContent(number.ToString(), tooltip);
            EditorGUI.LabelField(rect, content, style);
        }

        private void FillMissingUnitIds()
        {
            var existingTypes = new HashSet<int>();

            for (int i = 0; i < _unitIdsProp.arraySize; i++)
            {
                SerializedProperty typeProp = _unitIdsProp
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("type");
                if (typeProp != null)
                {
                    existingTypes.Add(typeProp.intValue);
                }
            }

            foreach (int type in Enum.GetValues(typeof(AdsType)).Cast<int>())
            {
                if (existingTypes.Contains(type))
                {
                    continue;
                }

                int newIndex = _unitIdsProp.arraySize;
                _unitIdsProp.arraySize++;
                SerializedProperty newElement = _unitIdsProp.GetArrayElementAtIndex(newIndex);

                newElement.FindPropertyRelative("type").intValue = type;
                newElement.FindPropertyRelative("androidUnitId").stringValue = string.Empty;
                newElement.FindPropertyRelative("iosUnitId").stringValue = string.Empty;
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        private bool HasDuplicateUnitIdTypes()
        {
            var seenTypes = new HashSet<int>();

            for (int i = 0; i < _unitIdsProp.arraySize; i++)
            {
                SerializedProperty typeProp = _unitIdsProp
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("type");
                if (typeProp == null)
                {
                    continue;
                }

                if (!seenTypes.Add(typeProp.intValue))
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawBannerSection()
        {
            EditorGuiLayout.DrawSectionHeader("Banner Configuration");

            if (_bannerPositionProp != null)
            {
                EditorGUILayout.PropertyField(_bannerPositionProp);
            }

            if (_bannerAutoSizedProp != null)
            {
                EditorGUILayout.PropertyField(_bannerAutoSizedProp);
            }

            if (_bannerBackgroundColorProp != null)
            {
                EditorGUILayout.PropertyField(_bannerBackgroundColorProp);
            }
        }

        private void DrawRetrySection()
        {
            EditorGuiLayout.DrawSectionHeader("Retry Configuration");

            int newTab = GUILayout.Toolbar(
                _selectedRetryTabIndex,
                RetryTabNames,
                GUILayout.Height(ToolbarHeight));
            if (newTab != _selectedRetryTabIndex)
            {
                _selectedRetryTabIndex = newTab;
                EditorPrefs.SetInt(RetryTabPrefKey, _selectedRetryTabIndex);
            }

            GUILayout.Space(Spacing);

            SerializedProperty retryProp =
                _retryConfigProps[_selectedRetryTabIndex];
            if (retryProp != null)
            {
                DrawPropertyChildrenInline(retryProp);
            }
        }

        private static GUIContent[] CreatePlatformTabContents()
        {
            GUIContent android = EditorGUIUtility.IconContent("BuildSettings.Android")
                ?? new GUIContent("Android");
            GUIContent ios = EditorGUIUtility.IconContent("BuildSettings.iPhone")
                ?? new GUIContent("iOS");

            android.text = " Android";
            ios.text = " iOS";

            return new[] { android, ios };
        }

        private static void DrawPropertyChildrenInline(SerializedProperty property)
        {
            SerializedProperty iterator = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (SerializedProperty.EqualContents(iterator, end))
                {
                    break;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        private static void DrawChip(string text, Color color)
        {
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            Rect chipRect = GUILayoutUtility.GetRect(
                new GUIContent(text),
                labelStyle,
                GUILayout.Width(ChipWidth),
                GUILayout.Height(ChipHeight));

            EditorGUI.DrawRect(chipRect, color);
            GUI.Label(chipRect, text, labelStyle);
        }

        private static void DrawSectionSeparator()
        {
            GUILayout.Space(SectionSpace);
            Rect separatorRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(separatorRect, SeparatorColor);
            GUILayout.Space(SectionSpace);
        }

        #endregion
    }
}

#endif
