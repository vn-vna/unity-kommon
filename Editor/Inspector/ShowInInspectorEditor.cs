using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Inspector
{

    [CustomEditor(typeof(MonoBehaviour), true)]
    [CanEditMultipleObjects]
    public class ShowInInspectorEditor : Editor
    {
        private static Dictionary<Type, Type> drawerTypeCache;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ShowInInspectorAttribute showInInspectorAttributes = target.GetType()
                .GetCustomAttributes<ShowInInspectorAttribute>(true)
                .FirstOrDefault();
            
            if (showInInspectorAttributes != null && showInInspectorAttributes.ReadOnly)
            {
                EditorGUILayout.HelpBox("This component is read-only in the inspector.", MessageType.Info);
                return;
            }

            MemberInfo[] members = target.GetType()
                .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (MemberInfo member in members)
            {
                if (member.GetCustomAttribute<ShowInInspectorAttribute>() == null)
                    continue;

                object value = null;
                Type type = null;
                bool canWrite = false;

                if (member is FieldInfo field)
                {
                    type = field.FieldType;
                    value = field.GetValue(target);
                    canWrite = !field.IsInitOnly && !field.IsLiteral;
                }
                else if (member is PropertyInfo prop)
                {
                    if (!prop.CanRead) continue;
                    type = prop.PropertyType;
                    value = prop.GetValue(target);
                    canWrite = prop.CanWrite;
                }
                else continue;

                string label = ObjectNames.NicifyVariableName(member.Name);

                canWrite = canWrite && (showInInspectorAttributes == null || !showInInspectorAttributes.ReadOnly);
                EditorGUI.BeginChangeCheck();
                object newValue = DrawWithAttributes(member, label, value, type);
                if (EditorGUI.EndChangeCheck() && canWrite)
                {
                    Undo.RecordObject(target, $"Modify {member.Name}");
                    if (member is FieldInfo f) f.SetValue(target, newValue);
                    else if (member is PropertyInfo p) p.SetValue(target, newValue);
                    EditorUtility.SetDirty(target);

                    if (showInInspectorAttributes != null && showInInspectorAttributes.LiveReload)
                    {
                        Repaint();
                    }
                }
            }
        }

        private object DrawWithAttributes(MemberInfo member, string label, object value, Type type)
        {
            object result = TryUsePropertyDrawer(member, label, value, type);
            if (result != null) return result;

            object[] attributes = member.GetCustomAttributes(true);

            if (attributes.OfType<RangeAttribute>().FirstOrDefault() is RangeAttribute range)
            {
                if (type == typeof(int)) return EditorGUILayout.IntSlider(label, (int)value, (int)range.min, (int)range.max);
                if (type == typeof(float)) return EditorGUILayout.Slider(label, (float)value, range.min, range.max);
            }

            if (attributes.OfType<MinAttribute>().FirstOrDefault() is MinAttribute min)
            {
                if (type == typeof(int)) return Mathf.Max((int)min.min, EditorGUILayout.IntField(label, (int)value));
                if (type == typeof(float)) return Mathf.Max(min.min, EditorGUILayout.FloatField(label, (float)value));
            }

            if (attributes.OfType<MultilineAttribute>().Any() && type == typeof(string))
                return EditorGUILayout.TextArea((string)value, GUILayout.MinHeight(40));

            if (attributes.OfType<TextAreaAttribute>().FirstOrDefault() is TextAreaAttribute ta && type == typeof(string))
                return EditorGUILayout.TextArea((string)value, GUILayout.MinHeight(ta.minLines * 13));

            return DrawValueField(label, value, type);
        }

        private object DrawValueField(string label, object value, Type type)
        {
            if (type == typeof(int)) return EditorGUILayout.IntField(label, (int)value);
            if (type == typeof(float)) return EditorGUILayout.FloatField(label, (float)value);
            if (type == typeof(bool)) return EditorGUILayout.Toggle(label, (bool)value);
            if (type == typeof(string)) return EditorGUILayout.TextField(label, (string)value);
            if (type.IsEnum) return EditorGUILayout.EnumPopup(label, (Enum)value);
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return EditorGUILayout.ObjectField(label, (UnityEngine.Object)value, type, true);

            EditorGUILayout.LabelField(label, $"(Unsupported type: {type.Name})");
            return value;
        }

        private object TryUsePropertyDrawer(MemberInfo member, string label, object value, Type type)
        {
            GUIContent labelContent = new GUIContent(label);

            // 1. Attribute-based drawers
            {
                object[] attributes = member.GetCustomAttributes(true);
                foreach (object attr in attributes)
                {
                    Type drawerType = GetDrawerFor(attr.GetType());
                    float height = GetDrawerHeight(drawerType, value, labelContent);
                    Rect rect = EditorGUILayout.GetControlRect(false, height);
                    if (drawerType != null && InvokeDrawer(drawerType, rect, labelContent, value, out object result))
                        return result;
                }
            }

            // 2. Type-based drawers
            {
                Type typeDrawer = GetDrawerFor(type);
                float height = GetDrawerHeight(typeDrawer, value, labelContent);
                Rect rect = EditorGUILayout.GetControlRect(false, height);
                if (typeDrawer != null && InvokeDrawer(typeDrawer, rect, labelContent, value, out object result2))
                    return result2;
            }

            return null;
        }

        private float GetDrawerHeight(Type drawerType, object value, GUIContent label)
        {
            MethodInfo method = drawerType.GetMethod("GetPropertyHeightDirect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null)
            {
                if (Activator.CreateInstance(drawerType) is PropertyDrawer drawer)
                {
                    try
                    {
                        return Convert.ToSingle(method.Invoke(drawer, new object[] { value, label }));
                    }
                    catch { }
                }
            }

            return EditorGUIUtility.singleLineHeight; // fallback
        }

        private bool InvokeDrawer(Type drawerType, Rect rect, GUIContent label, object value, out object result)
        {
            result = null;

            if (!(Activator.CreateInstance(drawerType) is PropertyDrawer drawer))
                return false;

            MethodInfo method = drawerType.GetMethod("OnGUIDirect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                return false;

            result = method.Invoke(drawer, new object[] { rect, label, value });
            return true;
        }

        private static Type GetDrawerFor(Type key)
        {
            if (drawerTypeCache == null)
            {
                drawerTypeCache = new Dictionary<Type, Type>();

                IEnumerable<Type> allTypes = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .SelectMany(assembly =>
                    {
                        try { return assembly.GetTypes(); }
                        catch { return Array.Empty<Type>(); }
                    });

                foreach (Type drawerType in allTypes.Where(t => typeof(PropertyDrawer).IsAssignableFrom(t) && !t.IsAbstract))
                {
                    IEnumerable<CustomPropertyDrawer> drawerAttrs = drawerType.GetCustomAttributes(typeof(CustomPropertyDrawer), false)
                        .Cast<CustomPropertyDrawer>();

                    foreach (CustomPropertyDrawer drawerAttr in drawerAttrs)
                    {
                        FieldInfo field = typeof(CustomPropertyDrawer).GetField("m_Type", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field == null) continue;

                        Type targetType = (Type)field.GetValue(drawerAttr);
                        if (targetType != null && !drawerTypeCache.ContainsKey(targetType))
                            drawerTypeCache[targetType] = drawerType;
                    }
                }
            }

            drawerTypeCache.TryGetValue(key, out Type result);
            return result;
        }
    }

}
