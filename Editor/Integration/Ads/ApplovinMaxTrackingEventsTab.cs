using System;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Integration
{
    internal static class ApplovinMaxTrackingEventsTab
    {
        #region Constants

        private const string ProviderFieldName = "adServiceProvider";
        private const string TrackingEventsFieldName = "trackingEvents";
        private const string ApplovinMaxProviderTypeName = "ApplovinMaxAdsServiceProvider";

        private const float BadgeWidth = 22f;
        private const float BadgeHeight = 18f;
        private const float StatusIconWidth = 18f;
        private const float DefaultLabelWidth = 50f;
        private const float OverrideButtonWidth = 70f;
        private const float ResetButtonWidth = 50f;
        private const float GroupLabelWidth = 80f;
        private const float EnabledFieldWidth = 110f;
        private const float SeverityFieldWidth = 140f;
        private const float ResetAllButtonWidth = 140f;
        private const float SearchMinWidth = 120f;
        private const float SpaceSmall = 2f;
        private const float SpaceMedium = 4f;
        private const float SeparatorHeight = 1f;
        private const float MinEventsScrollViewHeight = 300f;
        private const float ScrollViewBottomMargin = 30f;

        private static readonly Color BadgeColor = new Color(0.25f, 0.45f, 0.65f, 0.8f);
        private static readonly Color OverrideColor = new Color(0.9f, 0.7f, 0.2f);
        private static readonly Color SeparatorColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);

        #endregion

        #region Private Fields

        private static string _searchText = string.Empty;
        private static Vector2 _scrollPosition;

        private static readonly HashSet<EventFormatFilter> _activeFormatFilters =
            new HashSet<EventFormatFilter>
            {
                EventFormatFilter.AppOpen,
                EventFormatFilter.Interstitial,
                EventFormatFilter.Rewarded,
                EventFormatFilter.Banner
            };

        private static readonly HashSet<EventActionFilter> _activeActionFilters =
            new HashSet<EventActionFilter>
            {
                EventActionFilter.Load,
                EventActionFilter.Failed,
                EventActionFilter.Display,
                EventActionFilter.Show,
                EventActionFilter.Hide,
                EventActionFilter.Clicked,
                EventActionFilter.Revenue,
                EventActionFilter.Reward
            };

        #endregion

        #region Public Methods

        public static void DrawTab(ScriptableObject manager)
        {
            GUILayout.Space(SpaceMedium);

            ScriptableObject provider = ResolveApplovinMaxProvider(manager);
            if (provider == null)
            {
                DrawMissingProviderHelpBox();
                return;
            }

            DrawTrackingEventsForProvider(provider);
        }

        public static void DrawTrackingEventsForProvider(ScriptableObject provider)
        {
            if (provider == null)
            {
                DrawMissingProviderHelpBox();
                return;
            }

            SerializedObject serializedProvider = new SerializedObject(provider);
            SerializedProperty eventsProp =
                serializedProvider.FindProperty(TrackingEventsFieldName);

            if (eventsProp == null || !eventsProp.isArray)
            {
                EditorGUILayout.HelpBox(
                    $"Could not find '{TrackingEventsFieldName}' field on the provider.",
                    MessageType.Error);
                return;
            }

            ApplovinMaxAdsTrackingEventType[] eventTypes = GetEventTypes();

            DrawInfoHelpBox();
            DrawStatusBar(eventTypes, eventsProp);
            DrawControlBar(eventsProp);
            DrawEventsTable(eventsProp, eventTypes);

            if (serializedProvider.hasModifiedProperties)
            {
                serializedProvider.ApplyModifiedProperties();
                EditorUtility.SetDirty(provider);
            }
        }

        #endregion

        #region Private Methods

        private static ScriptableObject ResolveApplovinMaxProvider(ScriptableObject manager)
        {
            if (manager == null)
            {
                return null;
            }

            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty providerProp =
                serializedManager.FindProperty(ProviderFieldName);

            ScriptableObject provider =
                providerProp?.objectReferenceValue as ScriptableObject;

            if (provider == null || provider.GetType().Name != ApplovinMaxProviderTypeName)
            {
                return null;
            }

            return provider;
        }

        private static void DrawMissingProviderHelpBox()
        {
            EditorGUILayout.HelpBox(
                "No AppLovin MAX provider is configured. "
                + "Enable it in the 'Providers' tab first.",
                MessageType.Info);
        }

        private static ApplovinMaxAdsTrackingEventType[] GetEventTypes()
            => Enum.GetValues(typeof(ApplovinMaxAdsTrackingEventType))
                .Cast<ApplovinMaxAdsTrackingEventType>()
                .ToArray();

        private static void DrawInfoHelpBox()
        {
            EditorGUILayout.HelpBox(
                "Each event is tracked using a default Action ID. Add an override to change "
                + "the ID or severity, or to disable tracking for that event. An empty Action "
                + "ID falls back to the default.",
                MessageType.None);
        }

        private static void DrawStatusBar(
            ApplovinMaxAdsTrackingEventType[] eventTypes,
            SerializedProperty eventsProp)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int overriddenCount = eventsProp.arraySize;
                int disabledCount = CountDisabledOverrides(eventsProp);
                bool valid = overriddenCount <= eventTypes.Length;

                Color statusColor = valid ? Color.green : Color.yellow;
                var style = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = statusColor }
                };

                EditorGUILayout.LabelField(
                    $"Status: {(valid ? "Ready" : "Incomplete")}  |  "
                    + $"{eventTypes.Length} events, "
                    + $"{overriddenCount} overridden, {disabledCount} disabled",
                    style);
            }
        }

        private static int CountDisabledOverrides(SerializedProperty eventsProp)
        {
            int count = 0;

            for (int i = 0; i < eventsProp.arraySize; i++)
            {
                SerializedProperty enabledProp =
                    eventsProp.GetArrayElementAtIndex(i).FindPropertyRelative("enabled");
                if (enabledProp != null && !enabledProp.boolValue)
                {
                    count++;
                }
            }

            return count;
        }

        private static void DrawControlBar(SerializedProperty eventsProp)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _searchText = EditorGUILayout.TextField(
                    _searchText,
                    EditorStyles.toolbarSearchField,
                    GUILayout.MinWidth(SearchMinWidth));

                if (GUILayout.Button(
                        "Reset All Overrides",
                        EditorStyles.toolbarButton,
                        GUILayout.Width(ResetAllButtonWidth)))
                {
                    ResetAllOverrides(eventsProp);
                }
            }

            GUILayout.Space(SpaceSmall);

            DrawFilterBar();

            GUILayout.Space(SpaceSmall);
        }

        private static void DrawFilterBar()
        {
            DrawFormatFilterRow();
            DrawActionFilterRow();
        }

        private static void DrawFormatFilterRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    "Format:",
                    EditorStyles.miniLabel,
                    GUILayout.Width(DefaultLabelWidth));

                ToggleFilterChip(_activeFormatFilters, EventFormatFilter.AppOpen, "App Open");
                ToggleFilterChip(_activeFormatFilters, EventFormatFilter.Interstitial, "Interstitial");
                ToggleFilterChip(_activeFormatFilters, EventFormatFilter.Rewarded, "Rewarded");
                ToggleFilterChip(_activeFormatFilters, EventFormatFilter.Banner, "Banner");
            }
        }

        private static void DrawActionFilterRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    "Action:",
                    EditorStyles.miniLabel,
                    GUILayout.Width(DefaultLabelWidth));

                ToggleFilterChip(_activeActionFilters, EventActionFilter.Load, "Load");
                ToggleFilterChip(_activeActionFilters, EventActionFilter.Failed, "Failed");
                ToggleFilterChip(_activeActionFilters, EventActionFilter.Display, "Display");
                ToggleFilterChip(_activeActionFilters, EventActionFilter.Show, "Show");
                ToggleFilterChip(_activeActionFilters, EventActionFilter.Hide, "Hide");
                ToggleFilterChip(_activeActionFilters, EventActionFilter.Clicked, "Clicked");
                ToggleFilterChip(_activeActionFilters, EventActionFilter.Revenue, "Revenue");
                ToggleFilterChip(_activeActionFilters, EventActionFilter.Reward, "Reward");
            }
        }

        private static void ToggleFilterChip<T>(
            HashSet<T> activeFilters,
            T filter,
            string label) where T : struct, Enum
        {
            bool isActive = activeFilters.Contains(filter);
            bool newActive = GUILayout.Toggle(
                isActive,
                label,
                EditorStyles.toolbarButton);

            if (newActive != isActive)
            {
                if (newActive)
                {
                    activeFilters.Add(filter);
                }
                else
                {
                    activeFilters.Remove(filter);
                }
            }
        }

        private static void ResetAllOverrides(SerializedProperty eventsProp)
        {
            if (eventsProp.arraySize == 0)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Reset Tracking Events",
                "Remove all tracking event overrides and return to defaults?\n\n"
                + "This cannot be undone.",
                "Reset",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            eventsProp.arraySize = 0;
        }

        private static void DrawEventsTable(
            SerializedProperty eventsProp,
            ApplovinMaxAdsTrackingEventType[] eventTypes)
        {
            if (HasDuplicateTypes(eventsProp))
            {
                EditorGUILayout.HelpBox(
                    "Duplicate event types found in the tracking events mapping. "
                    + "Only the first entry is used.",
                    MessageType.Warning);
            }

            string search = _searchText.Trim();

            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.ExpandHeight(true)
            );

            int visibleCount = 0;
            for (int i = 0; i < eventTypes.Length; i++)
            {
                ApplovinMaxAdsTrackingEventType type = eventTypes[i];
                if (!IsVisible(type, search))
                {
                    continue;
                }

                visibleCount++;
                DrawEventRow(eventsProp, i + 1, type);
            }

            if (visibleCount == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No tracking events matching '{search}'.",
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawEventRow(
            SerializedProperty eventsProp,
            int badgeNumber,
            ApplovinMaxAdsTrackingEventType type
        )
        {
            int overrideIndex = FindOverrideIndex(eventsProp, type);
            SerializedProperty element = overrideIndex >= 0
                ? eventsProp.GetArrayElementAtIndex(overrideIndex)
                : null;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (DrawEventHeader(eventsProp, element, overrideIndex, badgeNumber, type))
                {
                    return;
                }

                DrawDefaultRow(type);

                if (element != null)
                {
                    DrawOverrideFields(element);
                }
            }
        }

        private static bool DrawEventHeader(
            SerializedProperty eventsProp,
            SerializedProperty element,
            int overrideIndex,
            int badgeNumber,
            ApplovinMaxAdsTrackingEventType type
        )
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawBadge(badgeNumber);

                EditorGUILayout.LabelField(
                    GetEventDisplayName(type),
                    EditorStyles.boldLabel
                );

                EditorGUILayout.LabelField(
                    GetEventGroupLabel(type),
                    EditorStyles.miniLabel,
                    GUILayout.Width(GroupLabelWidth)
                );

                GUILayout.FlexibleSpace();

                DrawStatusIcon(element);

                if (element != null)
                {
                    if (GUILayout.Button(
                            "Reset",
                            EditorStyles.miniButton,
                            GUILayout.Width(ResetButtonWidth)))
                    {
                        eventsProp.DeleteArrayElementAtIndex(overrideIndex);
                        return true;
                    }
                }
                else if (GUILayout.Button(
                             "Override",
                             EditorStyles.miniButton,
                             GUILayout.Width(OverrideButtonWidth)))
                {
                    AddOverrideEntry(eventsProp, type);
                    return true;
                }
            }

            return false;
        }

        private static void DrawDefaultRow(ApplovinMaxAdsTrackingEventType type)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    "Default:",
                    EditorStyles.miniLabel,
                    GUILayout.Width(DefaultLabelWidth));

                EditorGUILayout.LabelField(
                    GetDefaultActionId(type),
                    EditorStyles.miniLabel);
            }
        }

        private static void DrawBadge(int number)
        {
            Rect badgeRect = GUILayoutUtility.GetRect(
                BadgeWidth, BadgeHeight,
                GUILayout.Width(BadgeWidth),
                GUILayout.Height(BadgeHeight));
            EditorGUI.DrawRect(badgeRect, BadgeColor);

            GUI.Label(
                badgeRect,
                number.ToString(),
                new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                });
        }

        private static void DrawStatusIcon(SerializedProperty element)
        {
            (string icon, Color color) = GetStatus(element);

            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = color }
            };

            EditorGUILayout.LabelField(icon, style, GUILayout.Width(StatusIconWidth));
        }

        private static (string Icon, Color Color) GetStatus(SerializedProperty element)
        {
            if (element == null)
            {
                return ("\u2713", Color.green);
            }

            SerializedProperty enabledProp = element.FindPropertyRelative("enabled");
            if (enabledProp != null && !enabledProp.boolValue)
            {
                return ("\u2717", Color.red);
            }

            return ("\u270E", OverrideColor);
        }

        private static void DrawOverrideFields(SerializedProperty element)
        {
            SerializedProperty enabledProp = element.FindPropertyRelative("enabled");
            SerializedProperty actionIdProp = element.FindPropertyRelative("actionId");
            SerializedProperty severityProp = element.FindPropertyRelative("severity");

            if (enabledProp == null || actionIdProp == null || severityProp == null)
            {
                return;
            }

            GUILayout.Space(SpaceSmall);
            Rect separatorRect = EditorGUILayout.GetControlRect(false, SeparatorHeight);
            EditorGUI.DrawRect(separatorRect, SeparatorColor);
            GUILayout.Space(SpaceSmall);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(
                    enabledProp,
                    new GUIContent("Enabled"),
                    GUILayout.Width(EnabledFieldWidth));

                EditorGUILayout.PropertyField(
                    actionIdProp,
                    new GUIContent("Action ID"));

                EditorGUILayout.PropertyField(
                    severityProp,
                    new GUIContent("Severity"),
                    GUILayout.Width(SeverityFieldWidth));
            }

            if (string.IsNullOrEmpty(actionIdProp.stringValue))
            {
                EditorGUILayout.LabelField(
                    "Empty Action ID - the default Action ID will be used.",
                    EditorStyles.miniLabel);
            }
        }

        private static void AddOverrideEntry(
            SerializedProperty eventsProp,
            ApplovinMaxAdsTrackingEventType type)
        {
            if (FindOverrideIndex(eventsProp, type) >= 0)
            {
                return;
            }

            (string actionId, ActionSeverity severity) =
                ApplovinMaxAdsTrackingEventDefaults.Get(type);

            int newIndex = eventsProp.arraySize;
            eventsProp.InsertArrayElementAtIndex(newIndex);
            SerializedProperty element = eventsProp.GetArrayElementAtIndex(newIndex);

            element.FindPropertyRelative("type").intValue = (int)type;
            element.FindPropertyRelative("enabled").boolValue = true;
            element.FindPropertyRelative("actionId").stringValue = actionId;
            element.FindPropertyRelative("severity").intValue = (int)severity;
        }

        private static int FindOverrideIndex(
            SerializedProperty eventsProp,
            ApplovinMaxAdsTrackingEventType type)
        {
            for (int i = 0; i < eventsProp.arraySize; i++)
            {
                SerializedProperty typeProp =
                    eventsProp.GetArrayElementAtIndex(i).FindPropertyRelative("type");
                if (typeProp != null && typeProp.intValue == (int)type)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsVisible(ApplovinMaxAdsTrackingEventType type, string search)
        {
            return IsFormatVisible(type)
                && IsActionVisible(type)
                && IsSearchVisible(type, search);
        }

        private static bool IsFormatVisible(ApplovinMaxAdsTrackingEventType type)
        {
            if (_activeFormatFilters.Count == 0)
            {
                return true;
            }

            return _activeFormatFilters.Contains(GetEventFormat(type));
        }

        private static bool IsActionVisible(ApplovinMaxAdsTrackingEventType type)
        {
            if (_activeActionFilters.Count == 0)
            {
                return true;
            }

            return _activeActionFilters.Contains(GetEventAction(type));
        }

        private static bool IsSearchVisible(ApplovinMaxAdsTrackingEventType type, string search)
        {
            if (string.IsNullOrEmpty(search))
            {
                return true;
            }

            if (type.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return ApplovinMaxAdsTrackingEventDefaults.TryGet(type, out var defaults)
                && defaults.ActionId.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasDuplicateTypes(SerializedProperty eventsProp)
        {
            var seenTypes = new HashSet<int>();

            for (int i = 0; i < eventsProp.arraySize; i++)
            {
                SerializedProperty typeProp =
                    eventsProp.GetArrayElementAtIndex(i).FindPropertyRelative("type");
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

        private static string GetEventDisplayName(ApplovinMaxAdsTrackingEventType type)
            => ObjectNames.NicifyVariableName(type.ToString());

        private static string GetEventGroupLabel(ApplovinMaxAdsTrackingEventType type)
            => GetEventFormat(type) switch
            {
                EventFormatFilter.AppOpen => "App Open",
                EventFormatFilter.Interstitial => "Interstitial",
                EventFormatFilter.Rewarded => "Rewarded",
                _ => "Banner"
            };

        private static EventFormatFilter GetEventFormat(ApplovinMaxAdsTrackingEventType type)
        {
            string name = type.ToString();

            if (name.StartsWith("AppOpen")) return EventFormatFilter.AppOpen;
            if (name.StartsWith("Inter")) return EventFormatFilter.Interstitial;
            if (name.StartsWith("Reward")) return EventFormatFilter.Rewarded;
            return EventFormatFilter.Banner;
        }

        private static EventActionFilter GetEventAction(ApplovinMaxAdsTrackingEventType type)
        {
            string name = type.ToString();

            if (name.EndsWith("FailedToLoad")) return EventActionFilter.Failed;
            if (name.EndsWith("Displayed") || name.EndsWith("DisplayFailed")) return EventActionFilter.Display;
            if (name.EndsWith("CallLoad") || name.EndsWith("Loaded")) return EventActionFilter.Load;
            if (name.EndsWith("CallShow")) return EventActionFilter.Show;
            if (name.EndsWith("Hidden")) return EventActionFilter.Hide;
            if (name.EndsWith("Clicked")) return EventActionFilter.Clicked;
            if (name.EndsWith("RevenuePaid")) return EventActionFilter.Revenue;
            if (name.EndsWith("RewardReceived")) return EventActionFilter.Reward;
            return EventActionFilter.Load;
        }

        private static string GetDefaultActionId(ApplovinMaxAdsTrackingEventType type)
            => ApplovinMaxAdsTrackingEventDefaults.Get(type).ActionId;

        #endregion

        #region Nested Types

        private enum EventFormatFilter
        {
            AppOpen,
            Interstitial,
            Rewarded,
            Banner
        }

        private enum EventActionFilter
        {
            Load,
            Failed,
            Display,
            Show,
            Hide,
            Clicked,
            Revenue,
            Reward
        }

        #endregion
    }
}
