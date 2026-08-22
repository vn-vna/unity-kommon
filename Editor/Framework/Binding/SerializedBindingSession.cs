using System;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Com.Hapiga.Scheherazade.Common.Editor.Framework.Binding
{
    public interface ISerializedBindingSession : IDisposable
    {
        UnityObject Target { get; }
        SerializedObject SerializedObject { get; }

        void Rebind(UnityObject target);
        SerializedProperty Require(string propertyPath);
        bool TryGet(string propertyPath, out SerializedProperty property);
        void Update();
        bool Commit();
        void Save();
    }

    public sealed class SerializedBindingSession : ISerializedBindingSession
    {
        #region Interfaces & Properties

        public UnityObject Target => _serializedObject?.targetObject;

        public SerializedObject SerializedObject => _serializedObject
            ?? throw new ObjectDisposedException(nameof(SerializedBindingSession));

        #endregion

        #region Private Fields

        private SerializedObject _serializedObject;

        #endregion

        #region Public Methods

        public SerializedBindingSession(UnityObject target)
        {
            Rebind(target);
        }

        public void Rebind(UnityObject target)
        {
            if (!target)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (Target == target)
            {
                return;
            }

            DisposeSerializedObject();
            _serializedObject = new SerializedObject(target);
        }

        public SerializedProperty Require(string propertyPath)
        {
            if (TryGet(propertyPath, out SerializedProperty property))
            {
                return property;
            }

            string targetName = Target ? Target.name : "(disposed)";
            throw new ArgumentException(
                $"Property '{propertyPath}' was not found on '{targetName}'.",
                nameof(propertyPath)
            );
        }

        public bool TryGet(
            string propertyPath,
            out SerializedProperty property)
        {
            property = null;

            if (string.IsNullOrWhiteSpace(propertyPath) || !Target)
            {
                return false;
            }

            property = SerializedObject.FindProperty(propertyPath);
            return property != null;
        }

        public void Update()
        {
            SerializedObject.Update();
        }

        public bool Commit()
        {
            return SerializedObject.ApplyModifiedProperties();
        }

        public void Save()
        {
            if (!Target)
            {
                return;
            }

            EditorUtility.SetDirty(Target);
            AssetDatabase.SaveAssets();
        }

        public void Dispose()
        {
            DisposeSerializedObject();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private Methods

        private void DisposeSerializedObject()
        {
            _serializedObject?.Dispose();
            _serializedObject = null;
        }

        #endregion
    }
}
