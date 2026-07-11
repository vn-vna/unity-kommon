// ═══════════════════════════════════════════════════════════
// ── SerializedPropertyExtensions ──────────────────────
// ═══════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor.Toolkit
{
    /// <summary>
    /// Extension methods for SerializedProperty to reduce boilerplate.
    /// </summary>
    public static class SerializedPropertyExtensions
    {
        #region Drawing Extensions

        /// <summary>
        /// Draws the property with a label and optional layout options.
        /// </summary>
        public static void Draw(this SerializedProperty prop, string label = null)
        {
            EditorGUILayout.PropertyField(
                prop,
                label != null ? new GUIContent(label) : GUIContent.none);
        }

        /// <summary>
        /// Draws a color field for the property.
        /// </summary>
        public static void DrawColor(this SerializedProperty prop, string label = null)
        {
            GUIContent content = label != null ? new GUIContent(label) : GUIContent.none;
            prop.colorValue = EditorGUILayout.ColorField(content, prop.colorValue);
        }

        /// <summary>
        /// Draws a float field for the property.
        /// </summary>
        public static void DrawFloat(this SerializedProperty prop, string label = null)
        {
            GUIContent content = label != null ? new GUIContent(label) : GUIContent.none;
            prop.floatValue = EditorGUILayout.FloatField(content, prop.floatValue);
        }

        /// <summary>
        /// Draws an int field for the property.
        /// </summary>
        public static void DrawInt(this SerializedProperty prop, string label = null)
        {
            GUIContent content = label != null ? new GUIContent(label) : GUIContent.none;
            prop.intValue = EditorGUILayout.IntField(content, prop.intValue);
        }

        /// <summary>
        /// Draws a string field for the property.
        /// </summary>
        public static void DrawString(this SerializedProperty prop, string label = null)
        {
            GUIContent content = label != null ? new GUIContent(label) : GUIContent.none;
            prop.stringValue = EditorGUILayout.TextField(content, prop.stringValue);
        }

        /// <summary>
        /// Draws a bool toggle for the property.
        /// </summary>
        public static void DrawBool(this SerializedProperty prop, string label = null)
        {
            GUIContent content = label != null ? new GUIContent(label) : GUIContent.none;
            prop.boolValue = EditorGUILayout.Toggle(content, prop.boolValue);
        }

        #endregion

        #region Navigation Extensions

        /// <summary>
        /// Finds a relative property and returns it, or null if not found.
        /// </summary>
        public static SerializedProperty Find(this SerializedObject so, string path)
        {
            return so.FindProperty(path);
        }

        /// <summary>
        /// Finds a relative child property.
        /// </summary>
        public static SerializedProperty FindRelative(this SerializedProperty prop, string path)
        {
            return prop.FindPropertyRelative(path);
        }

        #endregion
    }
}
