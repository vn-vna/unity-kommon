#if !ODIN_INSPECTOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
#endif

#if !FAKE_ODIN_NAMESPACE && !ODIN_INSPECTOR
namespace Com.Hapiga.Scheherazade.Common.OdinInspector
#else
namespace Sirenix.OdinInspector
#endif
{
#if !ODIN_INSPECTOR
    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class ButtonAttribute : Attribute
    {
        public ButtonAttribute() { }
        public ButtonAttribute(object size) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class BoxGroupAttribute : Attribute
    {
        public BoxGroupAttribute() { }
        public BoxGroupAttribute(string group, bool showLabel = true, bool centerLabel = false, float order = 0F) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class FoldoutGroupAttribute : Attribute
    {
        public FoldoutGroupAttribute(string groupName, bool expanded, float order = 0F) { }
        public FoldoutGroupAttribute(string groupName, float order = 0F) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class LabelTextAttribute : Attribute
    {
        public LabelTextAttribute(string text) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class PropertyRangeAttribute : Attribute
    {
        public PropertyRangeAttribute(float min, float max) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class ReadOnlyAttribute : Attribute
    {
        public ReadOnlyAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class ShowInInspectorAttribute : Attribute
    {
        public ShowInInspectorAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class InlineEditorAttribute : Attribute
    {
        public InlineEditorAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class HideLabelAttribute : Attribute
    {
        public HideLabelAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class RequiredAttribute : Attribute
    {
        public RequiredAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class ShowIfAttribute : Attribute
    {
        public ShowIfAttribute(string condition) { }
        public ShowIfAttribute(string condition, object value) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class HideIfAttribute : Attribute
    {
        public HideIfAttribute(string condition) { }
        public HideIfAttribute(string condition, object value) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class HorizontalGroupAttribute : Attribute
    {
        public HorizontalGroupAttribute(string groupName, float width = 0, float margnLeft = 0, float marginRight = 0, float order = 0) { }
        public HorizontalGroupAttribute(float labelWidth = 0, float width = 0, float margnLeft = 0, float marginRight = 0, float order = 0) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class VerticalGroupAttribute : Attribute
    {
        public VerticalGroupAttribute(float order = 0.0f) { }
        public VerticalGroupAttribute(string groupName, float order = 0.0f) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class TabGroupAttribute : Attribute
    {
        public TabGroupAttribute(string groupName) { }
        public TabGroupAttribute(string groupName, string tabName) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class ValueDropdownAttribute : Attribute
    {
        public ValueDropdownAttribute(string methodName) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class HideReferenceObjectPickerAttribute : Attribute
    {
        public HideReferenceObjectPickerAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class TableListAttribute : Attribute
    {
        public TableListAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class TableMatrixAttribute : Attribute
    {
        public TableMatrixAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class HideIfGroupAttribute : Attribute
    {
        public HideIfGroupAttribute(string groupName) { }
        public HideIfGroupAttribute(string groupName, object value) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class ShowIfGroupAttribute : Attribute
    {
        public ShowIfGroupAttribute(string groupName) { }
        public ShowIfGroupAttribute(string groupName, object value) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class CustomValueDrawerAttribute : Attribute
    {
        public CustomValueDrawerAttribute(string methodName) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class PropertyOrderAttribute : Attribute
    {
        public PropertyOrderAttribute(int order) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class FolderPathAttribute : Attribute
    {
        public FolderPathAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class OnValueChangedAttribute : Attribute
    {
        public OnValueChangedAttribute(string methodName) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class ValidateInputAttribute : Attribute
    {
        public ValidateInputAttribute(string methodName) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class AssetsOnlyAttribute : Attribute
    {
        public AssetsOnlyAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class AssetSelectorAttribute : Attribute
    {
        public AssetSelectorAttribute() { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class DrawWithUnityAttribute : Attribute
    {
        public DrawWithUnityAttribute() { }
    }
#endif
}
