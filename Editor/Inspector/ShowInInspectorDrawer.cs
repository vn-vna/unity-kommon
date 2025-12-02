using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace Com.Hapiga.Scheherazade.Common.Inspector
{
    [CustomPropertyDrawer(typeof(ShowInInspectorAttribute), true)]
    public class ShowInInspectorDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Unity already handles serialized fields automatically
            EditorGUI.PropertyField(position, property, label, true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
}
