using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase.Editor
{
    /// <summary>
    /// Fully custom editor for ItemDefinition.
    /// Hides the default script header and the raw ItemMetadata reference;
    /// shows only the identity fields (ItemId, Description) and the
    /// per-definition tag management section.
    /// </summary>
    [CustomEditor(typeof(ItemDefinition))]
    public class ItemDefinitionEditor : UnityEditor.Editor
    {
        #region Private Fields

        private ItemDefinition _def;
        private ItemDatabaseConfiguration _config;
        private SerializedProperty _itemIdProp;
        private SerializedProperty _descriptionProp;

        private readonly Dictionary<int, UnityEditor.Editor> _tagEditors
            = new Dictionary<int, UnityEditor.Editor>();

        #endregion

        #region Unity Editor Callbacks

        private void OnEnable()
        {
            _def = (ItemDefinition)target;
            _config = ItemDatabaseConfiguration.Instance;
            _itemIdProp = serializedObject.FindProperty("_itemId");
            _descriptionProp = serializedObject.FindProperty("_description");
        }

        private void OnDisable()
        {
            DestroyTagEditors();
        }

        #endregion

        #region Inspector GUI

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawIdentityFields();

            EditorGUILayout.Space();
            EditorGUI.DrawRect(
                EditorGUILayout.GetControlRect(false, 1f),
                new Color(0.5f, 0.5f, 0.5f, 0.3f));
            EditorGUILayout.Space();

            DrawTagsHeader();
            DrawTagRows();

            serializedObject.ApplyModifiedProperties();
        }

        #endregion

        #region Identity Fields

        private void DrawIdentityFields()
        {
            EditorGUILayout.LabelField("Item Definition", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (_itemIdProp != null)
            {
                EditorGUILayout.PropertyField(_itemIdProp,
                    new GUIContent("Item ID"), true);
            }

            if (_descriptionProp != null)
            {
                EditorGUILayout.PropertyField(_descriptionProp,
                    new GUIContent("Description"), true);
            }

            if (_itemIdProp == null || _descriptionProp == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find ItemDefinition serialized fields.",
                    MessageType.Error);
            }
        }

        #endregion

        #region Tags Section

        private void DrawTagsHeader()
        {
            var tags = _def.Tags ?? Array.Empty<TagDefinition>();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Tags", EditorStyles.boldLabel,
                    GUILayout.Width(60));

                if (tags.Length > 0)
                {
                    DrawBadge($"{tags.Length}", new Color(0.7f, 0.55f, 0.2f));
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("+ Add Tag", GUILayout.Width(90)))
                {
                    DrawAddTagPopup();
                }
            }
        }

        private void DrawTagRows()
        {
            var tags = _def.Tags ?? Array.Empty<TagDefinition>();
            if (tags.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No tags attached yet. Click '+ Add Tag' to attach the first tag.",
                    MessageType.Info);
                return;
            }

            foreach (var tag in tags)
            {
                if (tag == null) continue;
                DrawTagRow(tag);
            }
        }

        private void DrawTagRow(TagDefinition tag)
        {
            bool hasData = TagDataRegistry.GetDataType(tag.GetType()) != null;
            bool usesDefaults = _config != null
                && ItemDefinitionTagUtils.IsSharedTemplate(_config, tag);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(tag.GetType().Name, EditorStyles.boldLabel);

                    DrawBadge(hasData ? "has data" : "marker",
                        hasData
                            ? new Color(0.7f, 0.55f, 0.2f)
                            : new Color(0.55f, 0.4f, 0.75f));

                    if (usesDefaults)
                    {
                        DrawBadge("uses defaults", new Color(0.4f, 0.5f, 0.6f));
                    }

                    GUILayout.FlexibleSpace();

                    if (usesDefaults && GUILayout.Button("Clone", EditorStyles.miniButton, GUILayout.Width(46)))
                    {
                        ItemDefinitionTagUtils.CloneTagIntoDefinition(_config, _def, tag);
                        DestroyTagEditors();
                        return;
                    }

                    if (DrawSmallDeleteButton())
                    {
                        ItemDefinitionTagUtils.RemoveTagFromDefinition(_config, _def, tag);
                        DestroyTagEditors();
                        return;
                    }
                }

                if (!string.IsNullOrEmpty(tag.name))
                {
                    EditorGUILayout.LabelField($"Asset: {tag.name}", EditorStyles.miniLabel);
                }

                if (hasData)
                {
                    DrawTagDataEditor(tag);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "(marker tag — no runtime data)", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawAddTagPopup()
        {
            if (_config == null) return;

            Type[] available = GetAvailableTagTypes();
            if (available.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Add Tag",
                    "All available tag types are already attached.",
                    "OK");
                return;
            }

            var menu = new GenericMenu();
            foreach (var type in available)
            {
                menu.AddItem(new GUIContent(type.Name), false,
                    () => AddTag(type));
            }
            menu.ShowAsContext();
        }

        private void AddTag(Type tagType)
        {
            if (_config == null) return;
            ItemDefinitionTagUtils.AddTagToDefinition(_config, _def, tagType);
            DestroyTagEditors();
            Repaint();
        }

        private Type[] GetAvailableTagTypes()
        {
            var all = ItemDatabaseSettingsProvider.ScanTagTypes();
            var attached = _def.Tags ?? Array.Empty<TagDefinition>();
            var result = new List<Type>();
            foreach (var type in all)
            {
                bool exists = Array.Exists(attached, t => t != null && t.GetType() == type);
                if (!exists) result.Add(type);
            }
            return result.ToArray();
        }

        private void DrawTagDataEditor(TagDefinition tag)
        {
            int id = tag.GetInstanceID();
            if (!_tagEditors.TryGetValue(id, out var editor) || editor == null)
            {
                _tagEditors[id] = UnityEditor.Editor.CreateEditor(tag);
                editor = _tagEditors[id];
            }

            if (editor == null) return;
            editor.OnInspectorGUI();
        }

        private void DestroyTagEditors()
        {
            foreach (var editor in _tagEditors.Values)
            {
                if (editor != null) DestroyImmediate(editor);
            }
            _tagEditors.Clear();
        }

        #endregion

        #region Badge Helpers

        private static void DrawBadge(string text, Color color)
        {
            var style = new GUIStyle(EditorStyles.miniButton)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                fontStyle = FontStyle.Bold
            };
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label($" {text} ", style, GUILayout.Width(18 + text.Length * 6.5f));
            GUI.backgroundColor = prev;
        }

        private static bool DrawSmallDeleteButton()
        {
            var content = EditorGUIUtility.IconContent("TreeEditor.Trash");
            return GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(26));
        }

        #endregion
    }
}
