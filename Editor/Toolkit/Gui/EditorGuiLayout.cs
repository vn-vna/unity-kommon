// ═══════════════════════════════════════════════════════════
// ── EditorGuiLayout ───────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor.Toolkit
{
    /// <summary>
    /// Reusable IMGUI layout helpers for consistent editor tool UI.
    /// </summary>
    public static class EditorGuiLayout
    {
        #region Constants

        public const float DefaultLabelWidth = 140f;
        public const float SmallButtonWidth = 22f;

        #endregion

        #region Sections

        /// <summary>
        /// Draws a section header with a bold title and optional subtitle.
        /// </summary>
        public static void DrawSectionHeader(string title, string subtitle = null)
        {
            GUILayout.Space(2);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(subtitle))
                EditorGUILayout.LabelField(subtitle, EditorStyles.miniLabel);

            GUILayout.Space(2);
        }

        /// <summary>
        /// Draws a compact header box (bold title + mini subtitle) for popup content.
        /// </summary>
        public static void DrawHeaderBox(string title, string subtitle)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(subtitle, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        /// <summary>
        /// Wraps content in a card-style container (helpBox).
        /// </summary>
        public static void DrawCard(Action drawContent)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            drawContent?.Invoke();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Draws a card with a bold header and content.
        /// </summary>
        public static void DrawCard(string header, Action drawContent)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (!string.IsNullOrEmpty(header))
                EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
            drawContent?.Invoke();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Draws a foldout section with automatic indent. Returns the new expanded state.
        /// </summary>
        public static bool DrawFoldoutSection(
            bool expanded, string title, Action drawContent)
        {
            expanded = EditorGUILayout.Foldout(expanded, title, true);

            if (expanded)
            {
                EditorGUI.indentLevel++;
                drawContent?.Invoke();
                EditorGUI.indentLevel--;
            }

            return expanded;
        }

        /// <summary>
        /// Draws a centered empty-state message with an optional sub-message.
        /// </summary>
        public static void DrawEmptyState(string message, string subMessage = null)
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical();
            GUILayout.Label(message, EditorStyles.centeredGreyMiniLabel);

            if (!string.IsNullOrEmpty(subMessage))
                GUILayout.Label(subMessage, EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        #endregion

        #region Tabs

        /// <summary>
        /// Draws a tab bar and returns the selected tab index.
        /// </summary>
        public static int DrawTabBar(int selectedIndex, string[] tabNames)
        {
            return GUILayout.Toolbar(selectedIndex, tabNames);
        }

        /// <summary>
        /// Draws a tab bar with a custom style and returns the selected index.
        /// </summary>
        public static int DrawTabBar(
            int selectedIndex, string[] tabNames, GUIStyle style)
        {
            return GUILayout.Toolbar(selectedIndex, tabNames, style);
        }

        #endregion

        #region Labeled Rows

        /// <summary>
        /// Draws a label + checkbox row.
        /// </summary>
        public static bool LabeledCheckbox(
            string label, bool value, float labelWidth = DefaultLabelWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            bool result = EditorGUILayout.Toggle(value);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        /// <summary>
        /// Draws a label + text field row for a SerializedProperty.
        /// </summary>
        public static void LabeledTextField(
            string label, SerializedProperty prop, float labelWidth = DefaultLabelWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            prop.stringValue = EditorGUILayout.TextField(prop.stringValue);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws a label + text field row, returning the new value.
        /// </summary>
        public static string LabeledTextField(
            string label, string value, float labelWidth = DefaultLabelWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            string result = EditorGUILayout.TextField(value);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        /// <summary>
        /// Draws a label + PropertyField row.
        /// </summary>
        public static void LabeledPropertyField(
            string label, SerializedProperty prop,
            float labelWidth = DefaultLabelWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            EditorGUILayout.PropertyField(prop, GUIContent.none);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws a label + popup row, returning the new index.
        /// </summary>
        public static int LabeledPopup(
            string label, int selectedIndex, string[] options,
            float labelWidth = DefaultLabelWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            int result = EditorGUILayout.Popup(selectedIndex, options);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        /// <summary>
        /// Draws a label + text field row with a "?" hint button that invokes a callback.
        /// </summary>
        public static void LabeledTextFieldWithHint(
            string label, SerializedProperty prop,
            Action<Rect> onHintClicked, float labelWidth = DefaultLabelWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            prop.stringValue = EditorGUILayout.TextField(prop.stringValue);

            if (GUILayout.Button("?", GUILayout.Width(SmallButtonWidth)))
                onHintClicked?.Invoke(GUILayoutUtility.GetLastRect());

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws a label + float field row for a SerializedProperty.
        /// </summary>
        public static void LabeledFloatField(
            string label, SerializedProperty prop, float labelWidth = DefaultLabelWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            prop.floatValue = EditorGUILayout.FloatField(prop.floatValue);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws a label + int field row for a SerializedProperty.
        /// </summary>
        public static void LabeledIntField(
            string label, SerializedProperty prop, float labelWidth = DefaultLabelWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            prop.intValue = EditorGUILayout.IntField(prop.intValue);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws a label + ObjectField row for a SerializedProperty.
        /// </summary>
        public static void LabeledObjectField(
            string label, SerializedProperty prop, Type objectType,
            bool allowSceneObjects, float labelWidth = DefaultLabelWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            EditorGUILayout.ObjectField(prop, objectType, GUIContent.none);
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Button Groups

        /// <summary>
        /// Draws a group of toggle buttons where only one can be active.
        /// </summary>
        public static void DrawButtonGroup(
            string label, SerializedProperty prop,
            string[] names, int[] values, float labelWidth = DefaultLabelWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));

            int currentIdx = Array.IndexOf(values, prop.enumValueIndex);
            if (currentIdx < 0) currentIdx = 0;

            for (int i = 0; i < names.Length; i++)
            {
                bool isActive = currentIdx == i;
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = isActive
                    ? new Color(0.25f, 0.45f, 0.75f)
                    : new Color(0.35f, 0.35f, 0.35f);

                if (GUILayout.Button(names[i], GUILayout.Height(20)))
                    prop.enumValueIndex = values[i];

                GUI.backgroundColor = oldBg;
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Badges & Status

        /// <summary>
        /// Draws a small color badge with text.
        /// </summary>
        public static void DrawColorBadge(string text, Color color, float width = 60f)
        {
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label(text, EditorGuiStyles.Badge, GUILayout.Width(width));
            GUI.backgroundColor = oldBg;
        }

        #endregion

        #region Inline Inspector

        /// <summary>
        /// Draws an inline inspector for a ScriptableObject, caching the Editor reference.
        /// </summary>
        public static void DrawInlineInspector(
            ref UnityEditor.Editor cachedEditor,
            ref ScriptableObject cachedTarget,
            ScriptableObject target,
            string header = null)
        {
            if (target == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (!string.IsNullOrEmpty(header))
                EditorGUILayout.LabelField(header, EditorStyles.miniBoldLabel);

            if (cachedEditor == null || cachedTarget != target)
            {
                if (cachedEditor != null)
                    UnityEngine.Object.DestroyImmediate(cachedEditor);

                cachedEditor = UnityEditor.Editor.CreateEditor(target);
                cachedTarget = target;
            }

            cachedEditor?.OnInspectorGUI();

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Draws an inline inspector using SerializedProperty iteration (no Editor caching needed).
        /// </summary>
        public static void DrawInlineInspector(SerializedObject serializedObject)
        {
            if (serializedObject == null)
                return;

            serializedObject.Update();
            SerializedProperty prop = serializedObject.GetIterator();
            bool enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;

                // Skip the m_Script reference
                if (prop.name == "m_Script")
                    continue;

                EditorGUILayout.PropertyField(prop, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        #endregion

        #region Feature Filter Bar

        /// <summary>
        /// Draws a row of filter buttons derived from a [Flags] enum.
        /// Supports multi-select — clicking a button toggles it on/off via XOR.
        /// An "All" button clears all filters. Returns the selected filter mask.
        ///
        /// Usage:
        /// <code>
        /// TrackingProviderFeatures filter = TrackingProviderFeatures.None;
        /// filter = EditorGuiLayout.DrawFeatureFilterBar(ref filter);
        /// // Show provider if (provider.FeatureFlags &amp; (int)filter) != 0
        /// </code>
        /// </summary>
        public static TEnum DrawFeatureFilterBar<TEnum>(
            ref TEnum selectedFilter,
            string allLabel = "All")
            where TEnum : struct, Enum
        {
            Type enumType = typeof(TEnum);
            if (!enumType.IsDefined(typeof(FlagsAttribute), false))
                throw new ArgumentException($"{enumType.Name} must have [Flags] attribute.");

            Array allValues = Enum.GetValues(enumType);
            var singleFlags = allValues
                .Cast<TEnum>()
                .Where(v => IsSingleBit(Convert.ToInt64(v)))
                .OrderBy(v => Convert.ToInt64(v))
                .ToList();

            EditorGUILayout.BeginHorizontal();

            // "All" button
            bool isAll = Convert.ToInt64(selectedFilter) == 0;
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = isAll
                ? new Color(0.25f, 0.45f, 0.75f)
                : new Color(0.35f, 0.35f, 0.35f);

            if (GUILayout.Button(allLabel, GUILayout.Height(20), GUILayout.ExpandWidth(false)))
                selectedFilter = default;

            GUI.backgroundColor = oldBg;

            // One button per single-bit flag
            foreach (TEnum flag in singleFlags)
            {
                long flagValue = Convert.ToInt64(flag);
                bool isActive = (Convert.ToInt64(selectedFilter) & flagValue) != 0;

                GUI.backgroundColor = isActive
                    ? new Color(0.25f, 0.45f, 0.75f)
                    : new Color(0.35f, 0.35f, 0.35f);

                string label = ObjectNames.NicifyVariableName(Enum.GetName(enumType, flag));
                if (GUILayout.Button(label, GUILayout.Height(20), GUILayout.ExpandWidth(false)))
                    selectedFilter = (TEnum)Enum.ToObject(enumType, Convert.ToInt64(selectedFilter) ^ flagValue);

                GUI.backgroundColor = oldBg;
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            return selectedFilter;
        }

        /// <summary>
        /// Non-generic overload for callers that only know the enum type at runtime.
        /// Supports multi-select — clicking toggles via XOR.
        /// </summary>
        public static int DrawFeatureFilterBar(
            Type enumType,
            int selectedFilter,
            string allLabel = "All")
        {
            if (!enumType.IsDefined(typeof(FlagsAttribute), false))
                throw new ArgumentException($"{enumType.Name} must have [Flags] attribute.");

            // Resolve single-bit values
            var singleFlags = Enum.GetValues(enumType)
                .Cast<object>()
                .Select(v => Convert.ToInt32(v))
                .Where(v => IsSingleBit(v))
                .OrderBy(v => v)
                .ToList();

            EditorGUILayout.BeginHorizontal();

            // "All" button
            bool isAll = selectedFilter == 0;
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = isAll
                ? new Color(0.25f, 0.45f, 0.75f)
                : new Color(0.35f, 0.35f, 0.35f);

            if (GUILayout.Button(allLabel, GUILayout.Height(20), GUILayout.ExpandWidth(false)))
                return 0;

            GUI.backgroundColor = oldBg;

            // One button per single-bit flag
            foreach (int flagValue in singleFlags)
            {
                bool isActive = (selectedFilter & flagValue) != 0;

                GUI.backgroundColor = isActive
                    ? new Color(0.25f, 0.45f, 0.75f)
                    : new Color(0.35f, 0.35f, 0.35f);

                string flagName = Enum.GetName(enumType, flagValue);
                string label = ObjectNames.NicifyVariableName(flagName);
                if (GUILayout.Button(label, GUILayout.Height(20), GUILayout.ExpandWidth(false)))
                    return selectedFilter ^ flagValue;

                GUI.backgroundColor = oldBg;
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            return selectedFilter;
        }

        private static bool IsSingleBit(long value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }

        #endregion
    }
}
