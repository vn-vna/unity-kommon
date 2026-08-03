#if FIREBASE_ANALYTICS

using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Editor.Toolkit;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking.Editor
{
    [CustomEditor(typeof(FirebaseTrackingProvider))]
    internal sealed class FirebaseTrackingProviderEditor : UnityEditor.Editor
    {
        #region Constants

        private const float BadgeWidth = 22f;
        private const float DeleteButtonWidth = 22f;
        private const float Spacing = 4f;
        private const float ToggleButtonWidth = 100f;
        private const float MinNameWidth = 40f;
        private const float ChipWidth = 96f;
        private const float ChipHeight = 18f;

        private static readonly Color BadgeColor = new Color(0.25f, 0.45f, 0.65f, 0.8f);
        private static readonly Color BadgeWarningColor = new Color(1f, 0.6f, 0.1f, 0.8f);
        private static readonly Color ChipColor = new Color(0.2f, 0.6f, 0.35f, 0.9f);
        private static readonly Color ChipWarningColor = new Color(1f, 0.6f, 0.1f, 0.8f);

        private static readonly TrackingProviderFeatures[] FeatureFlags =
        {
            TrackingProviderFeatures.IngameAction,
            TrackingProviderFeatures.PurchaseRevenue,
            TrackingProviderFeatures.AdRevenue,
            TrackingProviderFeatures.ScreenView
        };

        private static readonly string[] FeatureNames =
        {
            "Ingame Action",
            "Purchase Revenue",
            "Ad Revenue",
            "Screen View"
        };

        private static readonly FirebaseRevenueTrackingOptions[] RevenueFlagValues =
        {
            FirebaseRevenueTrackingOptions.BannerAdsRevenue,
            FirebaseRevenueTrackingOptions.InterstitialAdsRevenue,
            FirebaseRevenueTrackingOptions.RewardedAdsRevenue,
            FirebaseRevenueTrackingOptions.AppOpenAdsRevenue,
            FirebaseRevenueTrackingOptions.IapRevenue
        };

        private static readonly RevenueFlagInfo[] RevenueFlags =
        {
            new RevenueFlagInfo("Banner", "Track banner ad revenue events"),
            new RevenueFlagInfo("Inter", "Track interstitial ad revenue events"),
            new RevenueFlagInfo("Reward", "Track rewarded ad revenue events"),
            new RevenueFlagInfo("AO", "Track app open ad revenue events"),
            new RevenueFlagInfo("IAP", "Track in-app purchase revenue events")
        };

        #endregion

        #region Private Fields

        private SerializedProperty _revenueConfigProp;
        private SerializedProperty _iapMultiplierProp;
        private SerializedProperty _enabledFeaturesProp;
        private SerializedProperty _minimumSeverityProp;
        private SerializedProperty _providerMaskProp;
        private SerializedProperty _initializationTimeoutProp;
        private SerializedProperty _retryAttemptProp;
        private SerializedProperty _retryDelayProp;

        private float[] _revenueFlagWidths;
        private float _flagsBlockWidth;
        private Vector2 _revenueScrollPosition;

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            _revenueConfigProp = serializedObject.FindProperty("adsRevenueTrackingConfig");
            _iapMultiplierProp = serializedObject.FindProperty("iapMultiplier");
            _enabledFeaturesProp = serializedObject.FindProperty("enabledFeatures");
            _minimumSeverityProp = serializedObject.FindProperty("minimumActionSeverity");
            _providerMaskProp = serializedObject.FindProperty("providerMaskNumber");
            _initializationTimeoutProp = serializedObject.FindProperty("initializationTimeout");
            _retryAttemptProp = serializedObject.FindProperty("retryAttempt");
            _retryDelayProp = serializedObject.FindProperty("retryDelay");

            MeasureRevenueFlagWidths();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGuiLayout.DrawCard("Enabled Features", DrawEnabledFeatures);
            EditorGuiLayout.DrawCard("Revenue Tracking Configuration", DrawRevenueConfigurationCard);
            EditorGuiLayout.DrawCard("IAP Configuration", DrawIapCard);
            EditorGuiLayout.DrawCard("General", DrawGeneralCard);
            EditorGuiLayout.DrawCard("Initialization & Retry", DrawInitializationCard);

            serializedObject.ApplyModifiedProperties();
        }

        #endregion

        #region Private Methods

        private int CountEnabledFeatures()
        {
            if (_enabledFeaturesProp == null)
            {
                return 0;
            }

            int count = 0;
            foreach (TrackingProviderFeatures flag in FeatureFlags)
            {
                if ((_enabledFeaturesProp.intValue & (int)flag) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private void DrawEnabledFeatures()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Enabled Features", GUILayout.Width(EditorGuiLayout.DefaultLabelWidth));
                ToggleFlagButton(TrackingProviderFeatures.IngameAction, FeatureNames[0]);
                ToggleFlagButton(TrackingProviderFeatures.PurchaseRevenue, FeatureNames[1]);
                ToggleFlagButton(TrackingProviderFeatures.AdRevenue, FeatureNames[2]);
                ToggleFlagButton(TrackingProviderFeatures.ScreenView, FeatureNames[3]);
            }
        }

        private void ToggleFlagButton(TrackingProviderFeatures flag, string label)
        {
            bool isActive = _enabledFeaturesProp != null
                && (_enabledFeaturesProp.intValue & (int)flag) != 0;

            bool newActive = GUILayout.Toggle(
                isActive,
                label,
                EditorStyles.miniButton,
                GUILayout.Width(ToggleButtonWidth));

            if (_enabledFeaturesProp != null && newActive != isActive)
            {
                int newValue = newActive
                    ? _enabledFeaturesProp.intValue | (int)flag
                    : _enabledFeaturesProp.intValue & ~(int)flag;
                _enabledFeaturesProp.intValue = newValue;
            }
        }

        private void DrawRevenueConfigurationCard()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                        "Add",
                        EditorStyles.miniButton,
                        GUILayout.Width(40)))
                {
                    AddRevenueConfig();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button(
                        "Fill",
                        EditorStyles.miniButton,
                        GUILayout.Width(40)))
                {
                    FillMissingRevenueConfigs();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button(
                        "Clear",
                        EditorStyles.miniButton,
                        GUILayout.Width(50)))
                {
                    _revenueConfigProp.ClearArray();
                    GUIUtility.ExitGUI();
                }

                GUILayout.FlexibleSpace();

                EditorGUILayout.LabelField(
                    $"{_revenueConfigProp.arraySize} revenue events",
                    EditorStyles.miniLabel);
            }

            if (HasDuplicateEventNames())
            {
                EditorGUILayout.HelpBox(
                    "Duplicate event names will log the same event multiple times.",
                    MessageType.Warning);
            }

            DrawRevenueConfigTable();
        }

        private void DrawRevenueConfigTable()
        {
            EditorGUILayout.BeginVertical();

            for (int i = 0; i < _revenueConfigProp.arraySize; i++)
            {
                if (DrawRevenueConfigEntry(i))
                {
                    break;
                }
            }

            if (_revenueConfigProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No revenue events configured. Click 'Fill' to add one entry per tracking flag.",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private bool DrawRevenueConfigEntry(int index)
        {
            SerializedProperty element = _revenueConfigProp.GetArrayElementAtIndex(index);
            SerializedProperty eventNameProp = element.FindPropertyRelative("eventName");
            SerializedProperty trackingOptionsProp = element.FindPropertyRelative("trackingOptions");

            if (eventNameProp == null || trackingOptionsProp == null)
            {
                EditorGUILayout.LabelField($"Entry {index}: invalid format");
                return false;
            }

            float lineHeight = EditorGUIUtility.singleLineHeight;
            Rect lineRect = EditorGUILayout.GetControlRect(true, lineHeight);
            float x = lineRect.x;

            Rect badgeRect = new Rect(x, lineRect.y, BadgeWidth, lineHeight);
            DrawEventBadge(badgeRect, index + 1, eventNameProp);
            x += BadgeWidth + Spacing;

            float nameWidth = lineRect.xMax
                - x
                - _flagsBlockWidth
                - DeleteButtonWidth
                - Spacing * 2;
            nameWidth = Mathf.Max(nameWidth, MinNameWidth);
            Rect nameRect = new Rect(x, lineRect.y, nameWidth, lineHeight);
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUI.TextField(nameRect, eventNameProp.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                eventNameProp.stringValue = newName;
            }
            x += nameWidth + Spacing;

            for (int i = 0; i < RevenueFlags.Length; i++)
            {
                float flagWidth = _revenueFlagWidths[i];
                Rect flagRect = new Rect(x, lineRect.y, flagWidth, lineHeight);
                DrawRevenueFlagToggle(
                    flagRect,
                    trackingOptionsProp,
                    RevenueFlagValues[i],
                    RevenueFlags[i].Label,
                    RevenueFlags[i].Tooltip);
                x += flagWidth + Spacing;
            }

            Rect deleteRect = new Rect(x, lineRect.y, DeleteButtonWidth, lineHeight);
            if (GUI.Button(deleteRect, "\u2717", EditorStyles.miniButton))
            {
                _revenueConfigProp.DeleteArrayElementAtIndex(index);
                return true;
            }

            return false;
        }

        private static void DrawRevenueFlagToggle(
            Rect rect,
            SerializedProperty trackingOptionsProp,
            FirebaseRevenueTrackingOptions flag,
            string label,
            string tooltip)
        {
            bool isActive = (trackingOptionsProp.intValue & (int)flag) != 0;

            bool newActive = GUI.Toggle(
                rect,
                isActive,
                new GUIContent(label, tooltip),
                EditorStyles.miniButton);

            if (newActive != isActive)
            {
                int newValue = newActive
                    ? trackingOptionsProp.intValue | (int)flag
                    : trackingOptionsProp.intValue & ~(int)flag;
                trackingOptionsProp.intValue = newValue;
            }
        }

        private static void DrawEventBadge(
            Rect rect,
            int number,
            SerializedProperty eventNameProp)
        {
            bool isMissing = eventNameProp == null
                || string.IsNullOrEmpty(eventNameProp.stringValue);

            Color backgroundColor = isMissing ? BadgeWarningColor : BadgeColor;
            EditorGUI.DrawRect(rect, backgroundColor);

            string tooltip = isMissing
                ? "Event name is not set"
                : null;

            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            GUIContent content = new GUIContent(number.ToString(), tooltip);
            EditorGUI.LabelField(rect, content, style);
        }

        private void AddRevenueConfig()
        {
            int newIndex = _revenueConfigProp.arraySize;
            _revenueConfigProp.arraySize++;
            SerializedProperty newElement = _revenueConfigProp.GetArrayElementAtIndex(newIndex);

            newElement.FindPropertyRelative("eventName").stringValue = string.Empty;
            newElement.FindPropertyRelative("trackingOptions").intValue = 0;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        private void FillMissingRevenueConfigs()
        {
            var coveredFlags = FirebaseRevenueTrackingOptions.None;

            for (int i = 0; i < _revenueConfigProp.arraySize; i++)
            {
                SerializedProperty optionsProp = _revenueConfigProp
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("trackingOptions");
                if (optionsProp != null)
                {
                    coveredFlags |= (FirebaseRevenueTrackingOptions)optionsProp.intValue;
                }
            }

            foreach (FirebaseRevenueTrackingOptions flag in RevenueFlagValues)
            {
                if (coveredFlags.HasFlag(flag))
                {
                    continue;
                }

                int newIndex = _revenueConfigProp.arraySize;
                _revenueConfigProp.arraySize++;
                SerializedProperty newElement = _revenueConfigProp.GetArrayElementAtIndex(newIndex);

                newElement.FindPropertyRelative("eventName").stringValue = string.Empty;
                newElement.FindPropertyRelative("trackingOptions").intValue = (int)flag;
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        private bool HasDuplicateEventNames()
        {
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < _revenueConfigProp.arraySize; i++)
            {
                SerializedProperty nameProp = _revenueConfigProp
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("eventName");
                if (nameProp == null || string.IsNullOrEmpty(nameProp.stringValue))
                {
                    continue;
                }

                if (!seenNames.Add(nameProp.stringValue))
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawIapCard()
        {
            EditorGUILayout.PropertyField(_iapMultiplierProp);
        }

        private void DrawGeneralCard()
        {
            EditorGUILayout.PropertyField(_minimumSeverityProp);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(_providerMaskProp);
                GUILayout.FlexibleSpace();

                ProviderIdentity identity =
                    (ProviderIdentity)(1 << _providerMaskProp.intValue);
                EditorGUILayout.LabelField(
                    $"Identity: {identity}",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawInitializationCard()
        {
            EditorGUILayout.PropertyField(_initializationTimeoutProp);
            EditorGUILayout.PropertyField(_retryAttemptProp);
            EditorGUILayout.PropertyField(_retryDelayProp);
        }

        private void MeasureRevenueFlagWidths()
        {
            _revenueFlagWidths = new float[RevenueFlags.Length];
            float total = 0f;

            for (int i = 0; i < RevenueFlags.Length; i++)
            {
                float width = EditorStyles.miniButton.CalcSize(
                    new GUIContent(RevenueFlags[i].Label)).x;
                _revenueFlagWidths[i] = width;
                total += width;
            }

            _flagsBlockWidth = total + Spacing * (RevenueFlags.Length - 1);
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

        #endregion

        #region Nested Types

        private readonly struct RevenueFlagInfo
        {
            public RevenueFlagInfo(string label, string tooltip)
            {
                Label = label;
                Tooltip = tooltip;
            }

            public string Label { get; }
            public string Tooltip { get; }
        }

        #endregion
    }
}

#endif
