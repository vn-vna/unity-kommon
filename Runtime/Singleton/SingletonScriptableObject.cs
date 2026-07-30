using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Singleton
{
    public enum ScriptableLoadSource
    {
        Resources,
        StreamingAssets,
        AssetPath
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class SingletonScriptableConfigAttribute : Attribute
    {
        public ScriptableLoadSource LoadSource { get; }
        public string LoadPath { get; }

        public SingletonScriptableConfigAttribute(
            ScriptableLoadSource loadSource = ScriptableLoadSource.Resources,
            string loadPath = null)
        {
            LoadSource = loadSource;
            LoadPath = loadPath;
        }
    }

    public abstract class SingletonScriptableObject<T> : ScriptableObject
        where T : ScriptableObject
    {
        private static T _instance;
        private static SingletonScriptableConfigAttribute _cachedConfig;

        private static SingletonScriptableConfigAttribute Config
        {
            get
            {
                if (_cachedConfig == null)
                {
                    _cachedConfig = typeof(T)
                        .GetCustomAttribute<SingletonScriptableConfigAttribute>();
                }

                return _cachedConfig;
            }
        }

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    SingletonScriptableConfigAttribute config = Config;

                    if (config != null)
                    {
                        _instance = LoadByConfig(config);
                    }

                    if (_instance == null)
                    {
                        _instance = LoadFallback();
                    }
                }

                return _instance;
            }
            set => _instance = value;
        }

        protected virtual void OnEnable()
        {
            if (_instance == null)
            {
                _instance = this as T;
            }
        }

        protected virtual void OnDisable()
        {
            if (_instance == this as T)
            {
                _instance = null;
            }
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetInstance()
        {
            _instance = null;
        }
#endif

        #region Load Strategies

        private static T LoadByConfig(SingletonScriptableConfigAttribute config)
        {
            switch (config.LoadSource)
            {
                case ScriptableLoadSource.Resources:
                    return LoadFromResources(config.LoadPath);

                case ScriptableLoadSource.AssetPath:
                    return LoadFromAssetPath(config.LoadPath);

                case ScriptableLoadSource.StreamingAssets:
                    return LoadFromStreamingAssets(config.LoadPath);

                default:
                    return null;
            }
        }

        private static T LoadFromResources(string relativePath)
        {
            string path = relativePath ?? typeof(T).Name;
            return Resources.Load<T>(path);
        }

        private static T LoadFromAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
#else
            return null;
#endif
        }

        private static T LoadFromStreamingAssets(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

#if UNITY_EDITOR
            string fullPath = Path.Combine(
                Application.streamingAssetsPath,
                relativePath);
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(fullPath);
#else
            return null;
#endif
        }

        private static T LoadFallback()
        {
            T result = Resources.Load<T>(typeof(T).Name);

#if UNITY_EDITOR
            if (result == null)
            {
                result = FindInProject();
            }
#endif

            return result;
        }

        #endregion

        #region Editor Helpers

#if UNITY_EDITOR
        private static T FindInProject()
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets(
                $"t:{typeof(T).Name}");

            if (guids.Length == 0)
            {
                return null;
            }

            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
        }

        public static T CreateOrMoveToDesignatedPath()
        {
            SingletonScriptableConfigAttribute config = Config;
            if (config == null)
            {
                Debug.LogWarning(
                    $"[SingletonScriptableObject] {typeof(T).Name} has no "
                    + "SingletonScriptableConfigAttribute. "
                    + "CreateOrMoveToDesignatedPath requires the attribute "
                    + "to know the target path.");
                return null;
            }

            string targetPath = ResolveTargetPath(config);

            if (string.IsNullOrEmpty(targetPath))
            {
                Debug.LogError(
                    $"[SingletonScriptableObject] Cannot resolve target path "
                    + $"for {typeof(T).Name}. LoadPath must be set.");
                return null;
            }

            // Already exists at target — nothing to do
            T existing = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(
                targetPath);
            if (existing != null)
            {
                return existing;
            }

            // Search project for existing asset of this type
            string[] guids = UnityEditor.AssetDatabase.FindAssets(
                $"t:{typeof(T).Name}");
            T found = null;
            string foundPath = null;

            foreach (string guid in guids)
            {
                string assetPath =
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                T candidate =
                    UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);

                if (candidate != null)
                {
                    found = candidate;
                    foundPath = assetPath;
                    break;
                }
            }

            if (found != null)
            {
                // Move existing asset to target path
                string moveError = UnityEditor.AssetDatabase.ValidateMoveAsset(
                    foundPath, targetPath);

                if (!string.IsNullOrEmpty(moveError))
                {
                    Debug.LogWarning(
                        $"[SingletonScriptableObject] Cannot move "
                        + $"{typeof(T).Name} from '{foundPath}' to "
                        + $"'{targetPath}': {moveError}. "
                        + "The asset will remain at its current location "
                        + "and a new one will not be created.");
                    _instance = found;
                    return found;
                }

                UnityEditor.AssetDatabase.MoveAsset(
                    foundPath, targetPath);
                UnityEditor.AssetDatabase.SaveAssets();

                _instance = found;
                return found;
            }

            // No existing asset — create a new one
            T created = ScriptableObject.CreateInstance<T>();
            created.name = typeof(T).Name;

            string directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory)
                && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            UnityEditor.AssetDatabase.CreateAsset(created, targetPath);
            UnityEditor.AssetDatabase.SaveAssets();

            _instance = created;
            return created;
        }

        private static string ResolveTargetPath(
            SingletonScriptableConfigAttribute config)
        {
            string loadPath = config.LoadPath;
            if (string.IsNullOrEmpty(loadPath))
            {
                return null;
            }

            switch (config.LoadSource)
            {
                case ScriptableLoadSource.Resources:
                    // Resources path: "Integration/Managers/Config" →
                    // "Assets/Resources/Integration/Managers/Config.asset"
                    return $"Assets/Resources/{loadPath}.asset";

                case ScriptableLoadSource.AssetPath:
                    // AssetPath is already a full project path
                    return loadPath;

                case ScriptableLoadSource.StreamingAssets:
                    return Path.Combine(
                        "Assets/StreamingAssets", loadPath);

                default:
                    return null;
            }
        }
#endif

        #endregion
    }
}
