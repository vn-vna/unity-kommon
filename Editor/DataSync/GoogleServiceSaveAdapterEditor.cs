using Com.Hapiga.Scheherazade.Common.DataSync;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync.Editor
{
    [CustomEditor(typeof(GoogleServiceSaveAdapter))]
    internal sealed class GoogleServiceSaveAdapterEditor : UnityEditor.Editor
    {
        #region Private Fields

        private SerializedProperty _adapterIdProp;
        private SerializedProperty _readTimeoutSecondsProp;
        private SerializedProperty _initTimeoutSecondsProp;
        private SerializedProperty _openModeProp;
        private SerializedProperty _dataSourceProp;
        private SerializedProperty _autoConflictStrategyProp;
        private SerializedProperty _manualConflictStrategyProp;
        private SerializedProperty _prefetchDataOnConflictProp;
        private SerializedProperty _updateDescriptionProp;
        private SerializedProperty _descriptionTemplateProp;
        private SerializedProperty _updatePlayedTimeProp;
        private SerializedProperty _playedTimeSecondsProp;

        private bool _openConflictFoldout = true;
        private bool _commitUpdateFoldout = true;

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            _adapterIdProp = serializedObject.FindProperty("_adapterId");
            _readTimeoutSecondsProp = serializedObject.FindProperty("_readTimeoutSeconds");
            _initTimeoutSecondsProp = serializedObject.FindProperty("_initTimeoutSeconds");
            _openModeProp = serializedObject.FindProperty("_openMode");
            _dataSourceProp = serializedObject.FindProperty("_dataSource");
            _autoConflictStrategyProp = serializedObject.FindProperty("_autoConflictStrategy");
            _manualConflictStrategyProp = serializedObject.FindProperty("_manualConflictStrategy");
            _prefetchDataOnConflictProp = serializedObject.FindProperty("_prefetchDataOnConflict");
            _updateDescriptionProp = serializedObject.FindProperty("_updateDescription");
            _descriptionTemplateProp = serializedObject.FindProperty("_descriptionTemplate");
            _updatePlayedTimeProp = serializedObject.FindProperty("_updatePlayedTime");
            _playedTimeSecondsProp = serializedObject.FindProperty("_playedTimeSeconds");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawMiscSection();
            DrawOpenConflictSection();
            DrawCommitUpdateSection();

            serializedObject.ApplyModifiedProperties();
        }

        #endregion

        #region Private Methods

        private void DrawMiscSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Misc", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_adapterIdProp);
                EditorGUILayout.PropertyField(_readTimeoutSecondsProp);
                EditorGUILayout.PropertyField(_initTimeoutSecondsProp);
            }

            EditorGUILayout.Space();
        }

        private void DrawOpenConflictSection()
        {
            _openConflictFoldout = EditorGUILayout.Foldout(
                _openConflictFoldout,
                "Open / Conflict Resolution",
                true
            );
            if (!_openConflictFoldout) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_openModeProp);
                EditorGUILayout.PropertyField(_dataSourceProp);

                bool manual = _openModeProp != null
                    && _openModeProp.enumValueIndex
                       == (int)GoogleServiceSaveAdapter.OpenMode.Manual;

                if (manual)
                {
                    EditorGUILayout.PropertyField(_manualConflictStrategyProp);
                    EditorGUILayout.PropertyField(_prefetchDataOnConflictProp);
                }
                else
                {
                    EditorGUILayout.PropertyField(_autoConflictStrategyProp);
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawCommitUpdateSection()
        {
            _commitUpdateFoldout = EditorGUILayout.Foldout(
                _commitUpdateFoldout,
                "Commit Update",
                true
            );
            if (!_commitUpdateFoldout) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_updateDescriptionProp);
                if (_updateDescriptionProp != null && _updateDescriptionProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_descriptionTemplateProp);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.PropertyField(_updatePlayedTimeProp);
                if (_updatePlayedTimeProp != null && _updatePlayedTimeProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_playedTimeSecondsProp);
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space();
        }

        #endregion
    }
}
