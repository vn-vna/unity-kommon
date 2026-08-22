using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Com.Hapiga.Scheherazade.Common.Editor.Framework.Assets
{
    public interface IEditorAssetStore
    {
        T Load<T>(SettingsAssetDefinition<T> definition)
            where T : ScriptableObject;

        bool TryLoad<T>(
            SettingsAssetDefinition<T> definition,
            out T asset)
            where T : ScriptableObject;

        T LoadOrCreate<T>(SettingsAssetDefinition<T> definition)
            where T : ScriptableObject;

        T Create<T>(SettingsAssetDefinition<T> definition)
            where T : ScriptableObject;

        void Save(UnityObject asset);
    }

    public sealed class EditorAssetStore : IEditorAssetStore
    {
        #region Public Methods

        public T Load<T>(SettingsAssetDefinition<T> definition)
            where T : ScriptableObject
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return AssetDatabase.LoadAssetAtPath<T>(definition.AssetPath);
        }

        public bool TryLoad<T>(
            SettingsAssetDefinition<T> definition,
            out T asset)
            where T : ScriptableObject
        {
            asset = Load(definition);
            return asset != null;
        }

        public T LoadOrCreate<T>(SettingsAssetDefinition<T> definition)
            where T : ScriptableObject
        {
            if (TryLoad(definition, out T existingAsset))
            {
                return existingAsset;
            }

            return Create(definition);
        }

        public T Create<T>(SettingsAssetDefinition<T> definition)
            where T : ScriptableObject
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (AssetDatabase.LoadMainAssetAtPath(definition.AssetPath))
            {
                throw new InvalidOperationException(
                    $"An asset already exists at '{definition.AssetPath}'."
                );
            }

            EnsureFolderExists(definition.AssetPath);

            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, definition.AssetPath);
            Save(asset);
            return asset;
        }

        public void Save(UnityObject asset)
        {
            if (!asset)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        #endregion

        #region Private Methods

        private static void EnsureFolderExists(string assetPath)
        {
            string directoryPath = Path.GetDirectoryName(assetPath)
                ?.Replace('\\', '/');

            if (string.IsNullOrEmpty(directoryPath)
                || AssetDatabase.IsValidFolder(directoryPath))
            {
                return;
            }

            string[] folderNames = directoryPath.Split('/');
            string currentPath = folderNames[0];

            for (int i = 1; i < folderNames.Length; i++)
            {
                string nextPath = currentPath + "/" + folderNames[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, folderNames[i]);
                }

                currentPath = nextPath;
            }
        }

        #endregion
    }
}
