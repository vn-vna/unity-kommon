using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.VIC.Providers
{
    [CreateAssetMenu(
        fileName = "KeyValuePlaceholderProvider",
        menuName = "Scheherazade/Version Info/Key-Value Placeholder Provider"
    )]
    public class KeyValuePlaceholderProvider :
        ScriptableObject, IVersionNamePlaceholderProvider
    {
        #region Nested Types
        [Serializable]
        public struct PlaceholderEntry
        {
            [Tooltip("Placeholder key (without braces), e.g. \"build-type\".")]
            public string key;

            [Tooltip("Value to substitute, e.g. \"qa\" or \"staging\".")]
            public string value;
        }
        #endregion

        #region Serialized Fields
        [SerializeField]
        private List<PlaceholderEntry> _entries = new List<PlaceholderEntry>();
        #endregion

        #region IVersionNamePlaceholderProvider
        public bool TryResolve(string key, string format, out string value)
        {
            foreach (PlaceholderEntry entry in _entries)
            {
                if (string.Equals(
                        entry.key,
                        key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value = entry.value ?? "";
                    return true;
                }
            }

            value = null;
            return false;
        }
        #endregion
    }
}
