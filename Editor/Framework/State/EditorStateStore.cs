using System;
using UnityEditor;

namespace Com.Hapiga.Scheherazade.Common.Editor.Framework.State
{
    public enum EditorStateScope
    {
        Session,
        User
    }

    public interface IEditorStateStore
    {
        bool GetBool(string key, bool defaultValue, EditorStateScope scope);
        void SetBool(string key, bool value, EditorStateScope scope);
        int GetInt(string key, int defaultValue, EditorStateScope scope);
        void SetInt(string key, int value, EditorStateScope scope);
        string GetString(string key, string defaultValue, EditorStateScope scope);
        void SetString(string key, string value, EditorStateScope scope);
    }

    public sealed class EditorStateStore : IEditorStateStore
    {
        #region Constants

        private const string KeyPrefix =
            "Com.Hapiga.Scheherazade.Editor.Framework";

        #endregion

        #region Private Fields

        private readonly string _pageIdentity;
        private readonly int _version;

        #endregion

        #region Public Methods

        public EditorStateStore(string pageIdentity, int version = 1)
        {
            if (string.IsNullOrWhiteSpace(pageIdentity))
            {
                throw new ArgumentException(
                    "A page identity is required.",
                    nameof(pageIdentity)
                );
            }

            if (version < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }

            _pageIdentity = pageIdentity;
            _version = version;
        }

        public bool GetBool(
            string key,
            bool defaultValue,
            EditorStateScope scope)
        {
            string fullKey = GetKey(key);
            return scope == EditorStateScope.Session
                ? SessionState.GetBool(fullKey, defaultValue)
                : EditorPrefs.GetBool(fullKey, defaultValue);
        }

        public void SetBool(
            string key,
            bool value,
            EditorStateScope scope)
        {
            string fullKey = GetKey(key);
            if (scope == EditorStateScope.Session)
            {
                SessionState.SetBool(fullKey, value);
                return;
            }

            EditorPrefs.SetBool(fullKey, value);
        }

        public int GetInt(
            string key,
            int defaultValue,
            EditorStateScope scope)
        {
            string fullKey = GetKey(key);
            return scope == EditorStateScope.Session
                ? SessionState.GetInt(fullKey, defaultValue)
                : EditorPrefs.GetInt(fullKey, defaultValue);
        }

        public void SetInt(
            string key,
            int value,
            EditorStateScope scope)
        {
            string fullKey = GetKey(key);
            if (scope == EditorStateScope.Session)
            {
                SessionState.SetInt(fullKey, value);
                return;
            }

            EditorPrefs.SetInt(fullKey, value);
        }

        public string GetString(
            string key,
            string defaultValue,
            EditorStateScope scope)
        {
            string fullKey = GetKey(key);
            return scope == EditorStateScope.Session
                ? SessionState.GetString(fullKey, defaultValue)
                : EditorPrefs.GetString(fullKey, defaultValue);
        }

        public void SetString(
            string key,
            string value,
            EditorStateScope scope)
        {
            string fullKey = GetKey(key);
            if (scope == EditorStateScope.Session)
            {
                SessionState.SetString(fullKey, value);
                return;
            }

            EditorPrefs.SetString(fullKey, value);
        }

        #endregion

        #region Private Methods

        private string GetKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "A state key is required.",
                    nameof(key)
                );
            }

            return $"{KeyPrefix}.{_pageIdentity}.v{_version}.{key}";
        }

        #endregion
    }
}
