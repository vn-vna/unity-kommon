using UnityEngine;
using UnityEditor;

namespace Com.Hapiga.Scheherazade.Common.Inspector
{
    public class ValueDropDownAttribute : PropertyAttribute
    {
        public string ValueCollector { get; set; }

        public ValueDropDownAttribute(string valueCollector)
        {
            ValueCollector = valueCollector;
        }
    }

    [CustomPropertyDrawer(typeof(ValueDropDownAttribute))]
    internal class ValueDropDownDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var valueDropDownAttribute = (ValueDropDownAttribute)attribute;
            if (property.propertyType == SerializedPropertyType.String)
            {
                // Create a dropdown for string properties
                // var options = GetOptions(valueDropDownAttribute.ValueCollector);
                // int selectedIndex = Mathf.Max(0, System.Array.IndexOf(options, property.stringValue));
                // selectedIndex = EditorGUI.Popup(position, label.text, selectedIndex, options);
                // property.stringValue = options[selectedIndex];
            }
            else
            {
                EditorGUI.PropertyField(position, property, label);
            }
        }

        private object[] GetOptions(string valueCollector)
        {
            // This method should return the options based on the valueCollector.
            // For demonstration purposes, we return a static array.
            // Replace this with actual logic to fetch options.
            return new object[] { "Option1", "Option2", "Option3" };
        }
    }

}