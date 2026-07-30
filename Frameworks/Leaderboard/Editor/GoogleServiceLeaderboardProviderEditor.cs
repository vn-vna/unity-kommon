using System;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Leaderboard;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard.Editor
{
    [CustomEditor(typeof(GoogleServiceLeaderboardProvider))]
    internal sealed class GoogleServiceLeaderboardProviderEditor : UnityEditor.Editor
    {
        #region Constants

        private const float BadgeWidth = 22f;
        private const float DeleteButtonWidth = 22f;
        private const float Spacing = 4f;
        private const float DropdownRatio = 0.35f;

        private static readonly Color BadgeNormalColor = new Color(0.25f, 0.45f, 0.65f, 0.8f);
        private static readonly Color BadgeWarningColor = new Color(1f, 0.6f, 0.1f, 0.8f);

        #endregion

        #region Private Fields

        private SerializedProperty _idMappingProp;
        private string[] _knownIds = Array.Empty<string>();
        private Vector2 _scrollPosition;

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            _idMappingProp = serializedObject.FindProperty("idMapping");
        }

        public override void OnInspectorGUI()
        {
            RefreshKnownIds();
            serializedObject.Update();

            DrawOtherFields();

            EditorGUILayout.Space();
            DrawSeparator();

            DrawHeader();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            for (int i = 0; i < _idMappingProp.arraySize; i++)
            {
                if (DrawEntry(i))
                {
                    // Entry was deleted; bail out and let the next
                    // repaint rebuild the loop with the updated count.
                    break;
                }
            }

            EditorGUILayout.EndScrollView();

            DrawFooter();

            serializedObject.ApplyModifiedProperties();
        }

        #endregion

        #region Private Methods

        private void RefreshKnownIds()
        {
            LeaderboardConfiguration config = LeaderboardConfiguration.Instance;
            if (config != null)
            {
                _knownIds = config.Leaderboards
                    .Select(d => d.Id)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToArray();
            }
            else
            {
                _knownIds = Array.Empty<string>();
            }
        }

        private void DrawOtherFields()
        {
            SerializedProperty prop = serializedObject.GetIterator();
            bool enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (prop.name == "idMapping" || prop.name == "m_Script")
                {
                    continue;
                }

                EditorGUILayout.PropertyField(prop);
            }
        }

        private static void DrawSeparator()
        {
            Rect separatorRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(separatorRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Id Mapping", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_knownIds.Length == 0))
                {
                    if (GUILayout.Button("Fill", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        FillMissing();
                        GUIUtility.ExitGUI();
                    }

                    if (GUILayout.Button("Truncate", EditorStyles.miniButton, GUILayout.Width(68)))
                    {
                        TruncateInvalid();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private bool DrawEntry(int index)
        {
            SerializedProperty element = _idMappingProp.GetArrayElementAtIndex(index);
            SerializedProperty leaderboardIdProp = element.FindPropertyRelative("leaderboardId");
            SerializedProperty playServiceLeaderboardIdProp = element.FindPropertyRelative("playServiceLeaderboardId");

            if (leaderboardIdProp == null || playServiceLeaderboardIdProp == null)
            {
                EditorGUILayout.LabelField($"Entry {index}: invalid format");
                return false;
            }

            string currentId = leaderboardIdProp.stringValue;
            int knownIndex = Array.IndexOf(_knownIds, currentId);
            bool isInvalid = !string.IsNullOrEmpty(currentId) && knownIndex < 0;

            float lineHeight = EditorGUIUtility.singleLineHeight;
            Rect lineRect = EditorGUILayout.GetControlRect(true, lineHeight);

            float x = lineRect.x;
            float remainingWidth = lineRect.width - BadgeWidth - DeleteButtonWidth - Spacing * 3;
            float dropdownWidth = remainingWidth * DropdownRatio;
            float textWidth = remainingWidth * (1f - DropdownRatio);

            // Badge
            Rect badgeRect = new Rect(x, lineRect.y, BadgeWidth, lineHeight);
            DrawBadge(badgeRect, index + 1, isInvalid, currentId);
            x += BadgeWidth + Spacing;

            // Dropdown for leaderboardId
            Rect dropdownRect = new Rect(x, lineRect.y, dropdownWidth, lineHeight);
            DrawIdDropdown(dropdownRect, leaderboardIdProp, currentId);
            x += dropdownWidth + Spacing;

            // Text field for Google Play Service ID
            Rect textRect = new Rect(x, lineRect.y, textWidth, lineHeight);
            EditorGUI.BeginChangeCheck();
            string newValue = EditorGUI.TextField(textRect, playServiceLeaderboardIdProp.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                playServiceLeaderboardIdProp.stringValue = newValue;
            }
            x += textWidth + Spacing;

            // Delete button
            Rect deleteRect = new Rect(x, lineRect.y, DeleteButtonWidth, lineHeight);
            if (GUI.Button(deleteRect, "\u2717", EditorStyles.miniButton))
            {
                _idMappingProp.DeleteArrayElementAtIndex(index);
                return true;
            }

            return false;
        }

        private void DrawBadge(Rect rect, int number, bool isWarning, string leaderboardId)
        {
            Color bgColor = isWarning ? BadgeWarningColor : BadgeNormalColor;
            EditorGUI.DrawRect(rect, bgColor);

            string tooltip = isWarning
                ? $"\"{leaderboardId}\" not found in leaderboard definitions"
                : null;

            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            GUIContent content = new GUIContent(number.ToString(), tooltip);
            EditorGUI.LabelField(rect, content, style);
        }

        private void DrawIdDropdown(Rect rect, SerializedProperty property, string currentValue)
        {
            string buttonText = string.IsNullOrEmpty(currentValue) ? "\u2014" : currentValue;

            if (EditorGUI.DropdownButton(rect, new GUIContent(buttonText), FocusType.Passive))
            {
                var menu = new GenericMenu();

                if (_knownIds.Length == 0)
                {
                    menu.AddDisabledItem(new GUIContent("No leaderboards defined"));
                }
                else
                {
                    GenericMenu.MenuFunction2 onSelected = (userData) =>
                    {
                        property.stringValue = (string)userData;
                        serializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(serializedObject.targetObject);
                    };

                    foreach (string knownId in _knownIds)
                    {
                        bool isSelected = knownId == currentValue;
                        menu.AddItem(
                            new GUIContent(knownId),
                            isSelected,
                            onSelected,
                            knownId);
                    }
                }

                menu.DropDown(rect);
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(2);

            Rect buttonRect = EditorGUILayout.GetControlRect(
                false, EditorGUIUtility.singleLineHeight);
            buttonRect.x += buttonRect.width * 0.25f;
            buttonRect.width *= 0.5f;

            if (GUI.Button(buttonRect, "+ Add Entry", EditorStyles.miniButton))
            {
                int newIndex = _idMappingProp.arraySize;
                _idMappingProp.arraySize++;
                SerializedProperty newElement = _idMappingProp.GetArrayElementAtIndex(newIndex);

                SerializedProperty idProp = newElement.FindPropertyRelative("leaderboardId");
                SerializedProperty gpgsProp = newElement.FindPropertyRelative("playServiceLeaderboardId");

                if (idProp != null)
                {
                    idProp.stringValue = string.Empty;
                }

                if (gpgsProp != null)
                {
                    gpgsProp.stringValue = string.Empty;
                }

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(serializedObject.targetObject);
                GUIUtility.ExitGUI();
            }
        }

        private void FillMissing()
        {
            if (_knownIds.Length == 0)
            {
                return;
            }

            var mappedIds = new HashSet<string>();

            for (int i = 0; i < _idMappingProp.arraySize; i++)
            {
                SerializedProperty element = _idMappingProp.GetArrayElementAtIndex(i);
                SerializedProperty idProp = element.FindPropertyRelative("leaderboardId");

                if (idProp != null && !string.IsNullOrEmpty(idProp.stringValue))
                {
                    mappedIds.Add(idProp.stringValue);
                }
            }

            foreach (string id in _knownIds)
            {
                if (!mappedIds.Contains(id))
                {
                    int newIndex = _idMappingProp.arraySize;
                    _idMappingProp.arraySize++;
                    SerializedProperty newElement = _idMappingProp.GetArrayElementAtIndex(newIndex);

                    SerializedProperty idProp = newElement.FindPropertyRelative("leaderboardId");
                    SerializedProperty gpgsProp = newElement.FindPropertyRelative("playServiceLeaderboardId");

                    if (idProp != null)
                    {
                        idProp.stringValue = id;
                    }

                    if (gpgsProp != null)
                    {
                        gpgsProp.stringValue = string.Empty;
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        private void TruncateInvalid()
        {
            if (_knownIds.Length == 0)
            {
                _idMappingProp.ClearArray();
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(serializedObject.targetObject);
                return;
            }

            var validIds = new HashSet<string>(_knownIds);

            for (int i = _idMappingProp.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = _idMappingProp.GetArrayElementAtIndex(i);
                SerializedProperty idProp = element.FindPropertyRelative("leaderboardId");

                if (idProp == null || string.IsNullOrEmpty(idProp.stringValue)
                    || !validIds.Contains(idProp.stringValue))
                {
                    _idMappingProp.DeleteArrayElementAtIndex(i);
                }
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        #endregion
    }
}
