using System;
using Com.Hapiga.Scheherazade.Common.Archetype;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Singleton
{
    public abstract class SingletonScriptableObject<T> : ScriptableObject
        where T : ScriptableObject
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<T>(typeof(T).Name);
#if UNITY_EDITOR
                    if (_instance == null)
                    {
                        string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:{typeof(T).Name}");
                        if (guids.Length > 0)
                        {
                            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                            _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
                        }
                    }
#endif
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
                AutoRegisterArchetypes();
            }
        }

        protected virtual void OnDisable()
        {
            if (_instance == this)
            {
                AutoUnregisterArchetypes();
                _instance = null;
            }
        }

        protected void AutoRegisterArchetypes()
        {
            ArchetypeAttribute[] attributes;
            try
            {
                object[] attrs = GetType().GetCustomAttributes(
                    typeof(ArchetypeAttribute), inherit: false);
                attributes = Array.ConvertAll(attrs, a => (ArchetypeAttribute)a);
            }
            catch
            {
                return;
            }

            if (attributes == null || attributes.Length == 0)
            {
                return;
            }

            ArchetypeReflectionHelper.TryRegister(
                this,
                attributes);
        }

        protected void AutoUnregisterArchetypes()
        {
            ArchetypeAttribute[] attributes;
            try
            {
                object[] attrs = GetType().GetCustomAttributes(
                    typeof(ArchetypeAttribute), inherit: false);
                attributes = Array.ConvertAll(attrs, a => (ArchetypeAttribute)a);
            }
            catch
            {
                return;
            }

            if (attributes == null || attributes.Length == 0)
            {
                return;
            }

            ArchetypeReflectionHelper.TryUnregister(
                this,
                attributes);
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetInstance()
        {
            _instance = null;
        }
#endif
    }
}
