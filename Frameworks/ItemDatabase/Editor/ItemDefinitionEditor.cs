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
        private bool _isExternal;

        #endregion

        #region Unity Editor Callbacks

        private void OnEnable()
        {
            _def = (ItemDefinition)target;
            _isExternal = false;
            try
            {
                _config = ItemDefinitionTagUtils.ResolveConfiguration(
                    ItemDatabaseConfiguration.Instance,
                    _def
                );
            }
            catch (InvalidOperationException)
            {
                _config = null;
                _isExternal = true;
            }

            _itemIdProp = serializedObject.FindProperty("_itemId");
            _descriptionProp = serializedObject.FindProperty("_description");
        }

        private void OnDisable()
        {
        }

        #endregion

        #region Inspector GUI

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            string owner = ItemDatabase.GetDefinitionOwner(_def);
            bool isReadOnly = _isExternal || !string.IsNullOrEmpty(owner);
            if (_isExternal)
            {
                EditorGUILayout.HelpBox(
                    "This definition is not hosted by an Item Database "
                    + "configuration and is read-only here.",
                    MessageType.Warning
                );
            }

            if (!string.IsNullOrEmpty(owner))
            {
                EditorGUILayout.HelpBox(
                    $"This definition is managed by '{owner}' and is read-only.",
                    MessageType.Info
                );
            }

            using (new EditorGUI.DisabledScope(isReadOnly))
            {
                DrawIdentityFields();
                DrawMetadataFields();
            }
            if (serializedObject.ApplyModifiedProperties())
            {
                ItemDefinitionTagUtils.MarkDirty(_config, _def, _def.Metadata);
            }

            EditorGUILayout.Space();
            EditorGUI.DrawRect(
                EditorGUILayout.GetControlRect(false, 1f),
                new Color(0.5f, 0.5f, 0.5f, 0.3f));
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(isReadOnly))
            {
                DrawTagsHeader();
                DrawTagRows();
            }
        }

        #endregion

        #region Identity Fields

        private void DrawIdentityFields()
        {
            EditorGUILayout.LabelField("Item Definition", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (_itemIdProp != null)
            {
                bool duplicate = HasDuplicateItemId(_itemIdProp.stringValue);
                bool canRepairId = string.IsNullOrWhiteSpace(
                    _itemIdProp.stringValue
                ) || duplicate;
                using (new EditorGUI.DisabledScope(!canRepairId))
                {
                    EditorGUILayout.PropertyField(_itemIdProp,
                        new GUIContent(
                            "Item ID",
                            "Persistent identifier. Unique IDs are immutable without a save migration."
                        ), true);
                }

                if (!canRepairId)
                {
                    EditorGUILayout.LabelField(
                        "Item IDs are locked after assignment.",
                        EditorStyles.miniLabel
                    );
                }

                if (duplicate)
                {
                    EditorGUILayout.HelpBox(
                        $"Item ID '{_itemIdProp.stringValue}' is duplicated. "
                        + "Runtime initialization and builds are blocked until it is unique.",
                        MessageType.Error
                    );
                }
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

        private void DrawMetadataFields()
        {
            if (_def.Metadata == null)
            {
                EditorGUILayout.HelpBox(
                    "Metadata is missing. Adding a tag will recreate it, or use Repair Metadata.",
                    MessageType.Error
                );
                if (GUILayout.Button("Repair Metadata", GUILayout.Width(120)))
                {
                    ItemDefinitionTagUtils.EnsureMetadata(_config, _def);
                }

                return;
            }

            bool requiresClone = ItemDefinitionTagUtils.RequiresMetadataClone(
                _config,
                _def
            );
            if (requiresClone)
            {
                EditorGUILayout.HelpBox(
                    "Metadata is shared or external. Clone it before editing.",
                    MessageType.Warning
                );
                if (GUILayout.Button("Clone Metadata", GUILayout.Width(130)))
                {
                    ItemDefinitionTagUtils.CloneSharedMetadataForDefinition(
                        _config,
                        _def
                    );
                    return;
                }
            }

            var metadataObject = new SerializedObject(_def.Metadata);
            SerializedProperty iconProperty = metadataObject.FindProperty("_icon");
            SerializedProperty extrasProperty = metadataObject.FindProperty("_extras");
            metadataObject.Update();

            using (new EditorGUI.DisabledScope(requiresClone))
            {
                if (iconProperty != null)
                {
                    EditorGUILayout.PropertyField(
                        iconProperty,
                        new GUIContent("Icon")
                    );
                }

                if (extrasProperty != null)
                {
                    EditorGUILayout.PropertyField(
                        extrasProperty,
                        new GUIContent("Extras (JSON)"),
                        true
                    );
                }
            }

            if (metadataObject.ApplyModifiedProperties())
            {
                ItemDefinitionTagUtils.MarkDirty(_config, _def.Metadata);
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

                using (new EditorGUI.DisabledScope(
                           ItemDefinitionTagUtils.RequiresMetadataClone(
                               _config,
                               _def
                           )))
                {
                    if (GUILayout.Button("+ Add Tag", GUILayout.Width(90)))
                    {
                        DrawAddTagPopup();
                    }
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
            bool hasDefinitionFields = HasEditableDefinitionFields(tag);
            bool usesDefaults = _config != null
                && ItemDefinitionTagUtils.IsSharedTemplate(_config, tag);
            bool isExternal = _config != null
                && !ItemDefinitionTagUtils.IsOwnedSubAsset(_config, tag);
            bool requiresClone = usesDefaults || isExternal;
            string owner = ItemDatabase.GetTagOwner(tag.GetType());

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

                    if (!string.IsNullOrEmpty(owner))
                    {
                        DrawBadge(
                            $"managed by {owner}",
                            new Color(0.55f, 0.4f, 0.75f)
                        );
                    }

                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(
                               !string.IsNullOrEmpty(owner)))
                    {
                        if (requiresClone
                            && GUILayout.Button(
                                new GUIContent(
                                    "Clone",
                                    "Create an editable per-definition copy."
                                ),
                                EditorStyles.miniButton,
                                GUILayout.Width(46)))
                        {
                            ItemDefinitionTagUtils.CloneTagIntoDefinition(
                                _config,
                                _def,
                                tag
                            );
                            return;
                        }
                    }

                    using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(owner)))
                    {
                        if (DrawSmallDeleteButton())
                        {
                            ItemDefinitionTagUtils.RemoveTagFromDefinition(
                                _config,
                                _def,
                                tag
                            );
                            return;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(tag.name))
                {
                    EditorGUILayout.LabelField($"Asset: {tag.name}", EditorStyles.miniLabel);
                }

                if (usesDefaults)
                {
                    EditorGUILayout.HelpBox(
                        "This definition references the shared default template. "
                        + "Clone it before editing; clearing the default is blocked until references are materialized.",
                        MessageType.Warning
                    );
                }
                else if (isExternal)
                {
                    EditorGUILayout.HelpBox(
                        "This tag is external. Clone it before editing.",
                        MessageType.Warning
                    );
                }

                if (hasDefinitionFields)
                {
                    using (new EditorGUI.DisabledScope(
                               requiresClone || !string.IsNullOrEmpty(owner)))
                    {
                        DrawTagDefinitionFields(tag);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(
                        hasData
                            ? "Runtime payload only — no definition-level fields."
                            : "Marker tag — no definition-level fields.",
                        EditorStyles.miniLabel
                    );
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
                if (!exists && string.IsNullOrEmpty(ItemDatabase.GetTagOwner(type)))
                {
                    result.Add(type);
                }
            }
            return result.ToArray();
        }

        private static bool HasEditableDefinitionFields(TagDefinition tag)
        {
            if (tag == null) return false;
            var tagObject = new SerializedObject(tag);
            SerializedProperty iterator = tagObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath != "m_Script") return true;
            }

            return false;
        }

        private static void DrawTagDefinitionFields(TagDefinition tag)
        {
            var tagObject = new SerializedObject(tag);
            tagObject.Update();
            SerializedProperty iterator = tagObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script") continue;
                EditorGUILayout.PropertyField(iterator, true);
            }

            if (tagObject.ApplyModifiedProperties())
            {
                ItemDefinitionTagUtils.MarkDirty(tag);
            }
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
            content.tooltip = "Remove this tag from the definition.";
            return GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(26));
        }

        private bool HasDuplicateItemId(string itemId)
        {
            if (_config == null || string.IsNullOrWhiteSpace(itemId)) return false;

            int count = 0;
            foreach (ItemDefinition definition in _config.ItemDefinitions)
            {
                if (definition != null && definition.ItemId == itemId) count++;
            }

            return count > 1;
        }

        #endregion
    }
}
