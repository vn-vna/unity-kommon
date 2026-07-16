using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    /// <summary>
    /// A group of adapters that are tried in order.
    /// Within a group, the first available adapter is used.
    /// Groups themselves are tried in priority order (index 0 = first).
    /// </summary>
    [Serializable]
    public class AdapterGroup
    {
        [SerializeField]
        private ScriptableObject[] _adapters = Array.Empty<ScriptableObject>();

        public ISaveAdapter[] Adapters
            => _adapters
                ?.Where(a => a != null)
                .OfType<ISaveAdapter>()
                .ToArray()
               ?? Array.Empty<ISaveAdapter>();

        public ScriptableObject[] RawAdapters
        {
            get => _adapters ?? Array.Empty<ScriptableObject>();
            set => _adapters = value ?? Array.Empty<ScriptableObject>();
        }

        public int Count => _adapters?.Length ?? 0;

        public ScriptableObject GetAdapterAt(int index)
        {
            if (_adapters == null || index < 0 || index >= _adapters.Length)
                return null;
            return _adapters[index];
        }

        public void AddAdapter(ScriptableObject adapter)
        {
            if (adapter == null) return;

            var list = _adapters?.ToList() ?? new System.Collections.Generic.List<ScriptableObject>();
            if (!list.Contains(adapter))
                list.Add(adapter);
            _adapters = list.ToArray();
        }

        public void RemoveAdapter(ScriptableObject adapter)
        {
            if (_adapters == null) return;
            _adapters = _adapters.Where(a => a != adapter).ToArray();
        }

        public void RemoveAdapterAt(int index)
        {
            if (_adapters == null || index < 0 || index >= _adapters.Length) return;
            var list = _adapters.ToList();
            list.RemoveAt(index);
            _adapters = list.ToArray();
        }

        public void MoveAdapter(int fromIndex, int toIndex)
        {
            if (_adapters == null) return;
            if (fromIndex < 0 || fromIndex >= _adapters.Length) return;
            if (toIndex < 0 || toIndex >= _adapters.Length) return;

            var list = _adapters.ToList();
            var item = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, item);
            _adapters = list.ToArray();
        }

        public bool Contains(ScriptableObject adapter)
        {
            return _adapters != null && _adapters.Contains(adapter);
        }
    }

    [CreateAssetMenu(
        fileName = "DataSyncConfiguration",
        menuName = "Scheherazade/Data Sync/Configuration")]
    public class DataSyncConfiguration : ScriptableObject
    {
        // --- Legacy flat list (backward compat) ---
        [SerializeField]
        private ScriptableObject[] _adapters;

        // --- Group-based orders ---
        [SerializeField]
        private AdapterGroup[] _saveOrder;

        [SerializeField]
        private AdapterGroup[] _loadOrder;

        // --- Translators ---
        [SerializeField]
        private ScriptableObject[] _translators;

        #region Properties

        public ISaveTranslator[] Translators
            => _translators?.OfType<ISaveTranslator>().ToArray()
               ?? Array.Empty<ISaveTranslator>();

        // --- Group-based orders (public API for editor) ---

        public AdapterGroup[] SaveOrder
        {
            get => _saveOrder ?? Array.Empty<AdapterGroup>();
            set => _saveOrder = value ?? Array.Empty<AdapterGroup>();
        }

        public AdapterGroup[] LoadOrder
        {
            get => _loadOrder ?? Array.Empty<AdapterGroup>();
            set => _loadOrder = value ?? Array.Empty<AdapterGroup>();
        }

        // --- Legacy flat adapters ---

        public ISaveAdapter[] Adapters
            => _adapters?.OfType<ISaveAdapter>().ToArray()
               ?? Array.Empty<ISaveAdapter>();

        public ScriptableObject[] RawAdapters
        {
            get => _adapters ?? Array.Empty<ScriptableObject>();
            set => _adapters = value ?? Array.Empty<ScriptableObject>();
        }

        #endregion

        #region Group Resolution (used by DataSyncDirector)

        /// <summary>
        /// Returns save order groups. If groups are configured, uses them.
        /// Otherwise wraps flat <see cref="_adapters"/> as a single group.
        /// </summary>
        internal ISaveAdapter[][] ResolveSaveOrderGroups()
        {
            if (_saveOrder != null && _saveOrder.Length > 0)
            {
                return _saveOrder
                    .Select(g => g.Adapters)
                    .Where(a => a.Length > 0)
                    .ToArray();
            }

            // Fallback: wrap flat legacy adapters as one group
            var flat = _adapters?.OfType<ISaveAdapter>().ToArray()
                       ?? Array.Empty<ISaveAdapter>();
            return flat.Length > 0
                ? new[] { flat }
                : Array.Empty<ISaveAdapter[]>();
        }

        /// <summary>
        /// Returns load order groups. If groups are configured, uses them.
        /// Otherwise wraps flat <see cref="_adapters"/> as a single group.
        /// </summary>
        internal ISaveAdapter[][] ResolveLoadOrderGroups()
        {
            if (_loadOrder != null && _loadOrder.Length > 0)
            {
                return _loadOrder
                    .Select(g => g.Adapters)
                    .Where(a => a.Length > 0)
                    .ToArray();
            }

            // Fallback: wrap flat legacy adapters as one group
            var flat = _adapters?.OfType<ISaveAdapter>().ToArray()
                       ?? Array.Empty<ISaveAdapter>();
            return flat.Length > 0
                ? new[] { flat }
                : Array.Empty<ISaveAdapter[]>();
        }

        #endregion
    }
}
