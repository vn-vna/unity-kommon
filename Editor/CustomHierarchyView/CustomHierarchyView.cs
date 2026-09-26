using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.Editor.CustomHierarchy
{
    [InitializeOnLoad]
    internal static class CustomHierarchyView
    {
        #region Constants

        private const double MetricsRefreshInterval = 1d;
        private const float CompactHeightReduction = 2f;
        private const float MinimumRowHeight = 12f;
        private const float GuideLineWidth = 1f;

        private const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        #endregion

        #region Private Fields

        private static readonly Type HierarchyWindowType = typeof(EditorWindow).Assembly.GetType(
            "UnityEditor.SceneHierarchyWindow"
        );

        private static readonly Dictionary<object, HierarchyMetrics> OriginalMetrics =
            new Dictionary<object, HierarchyMetrics>();

        private static PropertyInfo _sceneHierarchyProperty;
        private static FieldInfo _treeViewField;
        private static PropertyInfo _treeViewGuiProperty;
        private static FieldInfo _lineHeightField;
        private static FieldInfo _indentWidthField;
        private static FieldInfo _foldoutYOffsetField;
        private static MethodInfo _reloadDataMethod;
        private static double _nextMetricsRefreshTime;
        private static bool _loggedReflectionFailure;

        #endregion

        #region Constructor

        static CustomHierarchyView()
        {
            EditorApplication.hierarchyWindowItemOnGUI -= HandleHierarchyItemGUI;
            EditorApplication.hierarchyWindowItemOnGUI += HandleHierarchyItemGUI;

            EditorApplication.update -= HandleEditorUpdate;
            EditorApplication.update += HandleEditorUpdate;

            AssemblyReloadEvents.beforeAssemblyReload -= RestoreOriginalMetrics;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreOriginalMetrics;

            EditorApplication.quitting -= RestoreOriginalMetrics;
            EditorApplication.quitting += RestoreOriginalMetrics;

            CustomHierarchyViewPreferences.Changed -= HandlePreferencesChanged;
            CustomHierarchyViewPreferences.Changed += HandlePreferencesChanged;

            EditorApplication.delayCall += ApplyToAllHierarchyWindows;
        }

        #endregion

        #region Private Methods

        private static void HandleHierarchyItemGUI(int instanceId, Rect selectionRect)
        {
            if (!CustomHierarchyViewPreferences.Enabled || Event.current.type != EventType.Repaint)
                return;

            DrawZebraBackground(instanceId, selectionRect);

            if (!CustomHierarchyViewPreferences.GuideLinesEnabled)
                return;

            GameObject gameObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (gameObject == null)
                return;

            DrawGuideLines(gameObject.transform, selectionRect);
        }

        private static void DrawZebraBackground(int instanceId, Rect selectionRect)
        {
            if (!CustomHierarchyViewPreferences.ZebraBackgroundEnabled ||
                Selection.Contains(instanceId))
            {
                return;
            }

            int rowIndex = Mathf.RoundToInt(selectionRect.y / selectionRect.height);
            if ((rowIndex & 1) == 0)
                return;

            Rect backgroundRect = new Rect(
                0f,
                selectionRect.y,
                EditorGUIUtility.currentViewWidth,
                selectionRect.height
            );
            EditorGUI.DrawRect(
                backgroundRect,
                CustomHierarchyViewPreferences.ZebraBackgroundColor
            );
        }

        private static void DrawGuideLines(Transform transform, Rect selectionRect)
        {
            int depth = GetDepth(transform);
            if (depth == 0)
                return;

            float indentWidth = CustomHierarchyViewPreferences.IndentWidth;
            float firstGuideX = selectionRect.x - depth * indentWidth - indentWidth * 0.5f;
            Color guideColor = CustomHierarchyViewPreferences.GuideLineColor;

            for (int level = 0; level < depth; level++)
            {
                float guideX = firstGuideX + level * indentWidth;
                EditorGUI.DrawRect(
                    new Rect(
                        Mathf.Round(guideX),
                        selectionRect.y,
                        GuideLineWidth,
                        selectionRect.height
                    ),
                    guideColor
                );
            }

            float lastGuideX = firstGuideX + (depth - 1) * indentWidth;
            float connectorEndX = selectionRect.x - indentWidth * 0.5f;
            EditorGUI.DrawRect(
                new Rect(
                    Mathf.Round(lastGuideX),
                    Mathf.Round(selectionRect.center.y),
                    Mathf.Max(GuideLineWidth, connectorEndX - lastGuideX),
                    GuideLineWidth
                ),
                guideColor
            );
        }

        private static int GetDepth(Transform transform)
        {
            int depth = 0;
            Transform parent = transform.parent;

            while (parent != null)
            {
                depth++;
                parent = parent.parent;
            }

            return depth;
        }

        private static void HandlePreferencesChanged()
        {
            ApplyToAllHierarchyWindows();
            EditorApplication.RepaintHierarchyWindow();
        }

        private static void HandleEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextMetricsRefreshTime)
                return;

            _nextMetricsRefreshTime = EditorApplication.timeSinceStartup + MetricsRefreshInterval;
            ApplyToAllHierarchyWindows();
        }

        private static void ApplyToAllHierarchyWindows()
        {
            if (HierarchyWindowType == null)
            {
                LogReflectionFailure("UnityEditor.SceneHierarchyWindow was not found.");
                return;
            }

            UnityEngine.Object[] hierarchyWindows = Resources.FindObjectsOfTypeAll(
                HierarchyWindowType
            );

            foreach (UnityEngine.Object hierarchyWindow in hierarchyWindows)
                ApplyToHierarchyWindow(hierarchyWindow as EditorWindow);
        }

        private static void ApplyToHierarchyWindow(EditorWindow hierarchyWindow)
        {
            if (hierarchyWindow == null ||
                !TryGetHierarchyInternals(
                    hierarchyWindow,
                    out object treeView,
                    out object treeViewGui
                ))
            {
                return;
            }

            if (!OriginalMetrics.TryGetValue(treeViewGui, out HierarchyMetrics original))
            {
                original = ReadMetrics(treeViewGui);
                OriginalMetrics.Add(treeViewGui, original);
            }

            float targetIndentWidth = CustomHierarchyViewPreferences.Enabled
                ? CustomHierarchyViewPreferences.IndentWidth
                : original.IndentWidth;

            float targetLineHeight = CustomHierarchyViewPreferences.Enabled &&
                                     CustomHierarchyViewPreferences.CompactMode
                ? Mathf.Max(MinimumRowHeight, original.LineHeight - CompactHeightReduction)
                : original.LineHeight;

            float targetFoldoutYOffset = original.FoldoutYOffset +
                                         (targetLineHeight - original.LineHeight) * 0.5f;

            bool changed = SetMetric(treeViewGui, _indentWidthField, targetIndentWidth);
            changed |= SetMetric(treeViewGui, _lineHeightField, targetLineHeight);
            changed |= SetMetric(treeViewGui, _foldoutYOffsetField, targetFoldoutYOffset);

            if (!changed)
                return;

            _reloadDataMethod?.Invoke(treeView, null);
            hierarchyWindow.Repaint();
        }

        private static bool TryGetHierarchyInternals(
            EditorWindow hierarchyWindow,
            out object treeView,
            out object treeViewGui
        )
        {
            treeView = null;
            treeViewGui = null;

            try
            {
                _sceneHierarchyProperty ??= HierarchyWindowType.GetProperty(
                    "sceneHierarchy",
                    InstanceFlags
                );
                object sceneHierarchy = _sceneHierarchyProperty?.GetValue(hierarchyWindow);
                if (sceneHierarchy == null)
                    return false;

                _treeViewField ??= sceneHierarchy.GetType().GetField("m_TreeView", InstanceFlags);
                treeView = _treeViewField?.GetValue(sceneHierarchy);
                if (treeView == null)
                    return false;

                _treeViewGuiProperty ??= treeView.GetType().GetProperty("gui", InstanceFlags);
                treeViewGui = _treeViewGuiProperty?.GetValue(treeView);
                if (treeViewGui == null)
                    return false;

                Type treeViewGuiType = treeViewGui.GetType().BaseType;
                if (treeViewGuiType == null)
                    return false;

                _lineHeightField ??= treeViewGuiType.GetField("m_LineHeight", InstanceFlags);
                _indentWidthField ??= treeViewGuiType.GetField("k_IndentWidth", InstanceFlags);
                _foldoutYOffsetField ??= treeViewGuiType.GetField(
                    "customFoldoutYOffset",
                    InstanceFlags
                );
                _reloadDataMethod ??= treeView.GetType().GetMethod(
                    "ReloadData",
                    InstanceFlags,
                    null,
                    Type.EmptyTypes,
                    null
                );

                if (_lineHeightField != null && _indentWidthField != null)
                    return true;

                LogReflectionFailure("Required hierarchy layout fields were not found.");
                return false;
            }
            catch (Exception exception)
            {
                LogReflectionFailure(exception.Message);
                return false;
            }
        }

        private static HierarchyMetrics ReadMetrics(object treeViewGui)
        {
            return new HierarchyMetrics(
                ReadMetric(treeViewGui, _lineHeightField),
                ReadMetric(treeViewGui, _indentWidthField),
                ReadMetric(treeViewGui, _foldoutYOffsetField)
            );
        }

        private static float ReadMetric(object target, FieldInfo field)
        {
            if (field == null)
                return 0f;

            return Convert.ToSingle(field.GetValue(target));
        }

        private static bool SetMetric(object target, FieldInfo field, float value)
        {
            if (field == null)
                return false;

            float currentValue = ReadMetric(target, field);
            if (Mathf.Approximately(currentValue, value))
                return false;

            field.SetValue(target, value);
            return true;
        }

        private static void RestoreOriginalMetrics()
        {
            foreach (KeyValuePair<object, HierarchyMetrics> entry in OriginalMetrics)
            {
                SetMetric(entry.Key, _lineHeightField, entry.Value.LineHeight);
                SetMetric(entry.Key, _indentWidthField, entry.Value.IndentWidth);
                SetMetric(entry.Key, _foldoutYOffsetField, entry.Value.FoldoutYOffset);
            }

            OriginalMetrics.Clear();
            EditorApplication.RepaintHierarchyWindow();
        }

        private static void LogReflectionFailure(string reason)
        {
            if (_loggedReflectionFailure)
                return;

            _loggedReflectionFailure = true;
            Debug.LogWarning(
                "[Custom Hierarchy View] Layout customization is unavailable: " + reason
            );
        }

        #endregion

        #region Nested Types

        private readonly struct HierarchyMetrics
        {
            internal float LineHeight { get; }
            internal float IndentWidth { get; }
            internal float FoldoutYOffset { get; }

            internal HierarchyMetrics(
                float lineHeight,
                float indentWidth,
                float foldoutYOffset
            )
            {
                LineHeight = lineHeight;
                IndentWidth = indentWidth;
                FoldoutYOffset = foldoutYOffset;
            }
        }

        #endregion
    }
}
