// ═══════════════════════════════════════════════════════════
// ── EditorPrefsStore ──────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor.Toolkit
{
    /// <summary>
    /// Typed wrapper around EditorPrefs with JSON serialization and optional
    /// XOR encryption for sensitive data (e.g., keystore passwords).
    ///
    /// <example>
    /// // Simple usage
    /// var store = new EditorPrefsStore("MyTool");
    /// store.Set("lastPath", somePath);
    /// string path = store.Get("lastPath", "");
    ///
    /// // JSON serialization
    /// var prefs = new EditorPrefsStore("MyTool");
    /// prefs.SetJson("history", myHistoryList);
    /// var list = prefs.GetJson&lt;List&lt;HistoryItem&gt;&gt;("history");
    ///
    /// // Encrypted (XOR) for sensitive data
    /// var secure = new EditorPrefsStore("Keystore").Encrypted("my-secret-key");
    /// secure.Set("password", secretValue);
    /// </example>
    /// </summary>
    public class EditorPrefsStore
    {
        #region Interfaces & Properties

        private readonly string _prefix;
        private bool _encrypted;
        private string _encryptionKey;

        #endregion

        #region Constructor

        public EditorPrefsStore(string keyPrefix)
        {
            _prefix = string.IsNullOrEmpty(keyPrefix) ? "" : keyPrefix + "_";
            _encrypted = false;
            _encryptionKey = "";
        }

        #endregion

        #region Public Methods - Simple Types

        public string Get(string key, string defaultValue = "")
        {
            string raw = EditorPrefs.GetString(FullKey(key), defaultValue);
            return _encrypted ? XorDecrypt(raw, _encryptionKey) : raw;
        }

        public void Set(string key, string value)
        {
            EditorPrefs.SetString(FullKey(key), _encrypted ? XorEncrypt(value, _encryptionKey) : value);
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            return EditorPrefs.GetInt(FullKey(key), defaultValue);
        }

        public void SetInt(string key, int value)
        {
            EditorPrefs.SetInt(FullKey(key), value);
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            return EditorPrefs.GetInt(FullKey(key), defaultValue ? 1 : 0) == 1;
        }

        public void SetBool(string key, bool value)
        {
            EditorPrefs.SetInt(FullKey(key), value ? 1 : 0);
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            return EditorPrefs.GetFloat(FullKey(key), defaultValue);
        }

        public void SetFloat(string key, float value)
        {
            EditorPrefs.SetFloat(FullKey(key), value);
        }

        #endregion

        #region Public Methods - JSON Serialization

        public T GetJson<T>(string key, T defaultValue = default) where T : class
        {
            string json = Get(key, null);
            if (string.IsNullOrEmpty(json))
                return defaultValue;

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EditorPrefsStore] Failed to deserialize '{FullKey(key)}': {ex.Message}");
                return defaultValue;
            }
        }

        public void SetJson<T>(string key, T value) where T : class
        {
            if (value == null)
            {
                Delete(key);
                return;
            }

            string json = JsonUtility.ToJson(value);
            Set(key, json);
        }

        #endregion

        #region Public Methods - Utility

        public void Delete(string key)
        {
            EditorPrefs.DeleteKey(FullKey(key));
        }

        public bool HasKey(string key)
        {
            return EditorPrefs.HasKey(FullKey(key));
        }

        /// <summary>
        /// Returns a copy of this store that transparently encrypts/decrypts
        /// all string values using XOR with the provided key.
        /// </summary>
        public EditorPrefsStore Encrypted(string encryptionKey)
        {
            EditorPrefsStore clone = new EditorPrefsStore(_prefix.TrimEnd('_'));
            clone._encrypted = true;
            clone._encryptionKey = encryptionKey ?? "";
            return clone;
        }

        #endregion

        #region Private Methods

        private string FullKey(string key)
        {
            return _prefix + key;
        }

        private static string XorEncrypt(string text, string key)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key))
                return text;

            byte[] textBytes = Encoding.UTF8.GetBytes(text);
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] result = new byte[textBytes.Length];

            for (int i = 0; i < textBytes.Length; i++)
                result[i] = (byte)(textBytes[i] ^ keyBytes[i % keyBytes.Length]);

            return Convert.ToBase64String(result);
        }

        private static string XorDecrypt(string base64, string key)
        {
            if (string.IsNullOrEmpty(base64) || string.IsNullOrEmpty(key))
                return base64;

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(base64);
                byte[] keyBytes = Encoding.UTF8.GetBytes(key);
                byte[] result = new byte[encryptedBytes.Length];

                for (int i = 0; i < encryptedBytes.Length; i++)
                    result[i] = (byte)(encryptedBytes[i] ^ keyBytes[i % keyBytes.Length]);

                return Encoding.UTF8.GetString(result);
            }
            catch
            {
                // If decryption fails (e.g., legacy unencrypted data), return as-is
                return base64;
            }
        }

        #endregion
    }
}
