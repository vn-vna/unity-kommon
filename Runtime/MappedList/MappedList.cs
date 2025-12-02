using System;
using System.Collections;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.MappedList
{

    public class MappedList<K, T> : IDictionary<K, T>
        where K : notnull
        where T : class
    {
        private Dictionary<K, T> _lookup;

        public MappedList(IEnumerable<T> list, Func<T, K> keySelecttor)
        {
            if (list == null)
            {
                throw new ArgumentNullException(
                    nameof(list),
                    "List cannot be null"
                );
            }

            if (keySelecttor == null)
            {
                throw new ArgumentNullException(
                    nameof(keySelecttor),
                    "Key selector cannot be null"
                );
            }

            _lookup = new Dictionary<K, T>();

            foreach (var item in list)
            {
                if (item == null)
                {
                    throw new ArgumentException(
                        "List contains null items",
                        nameof(list)
                    );
                }

                var key = keySelecttor(item);
                if (_lookup.ContainsKey(key))
                {
                    throw new ArgumentException(
                        $"Duplicate key found: {key}",
                        nameof(list)
                    );
                }

                _lookup[key] = item;
            }
        }

        public T this[K key]
        {
            get
            {
                if (_lookup.TryGetValue(key, out var value))
                {
                    return value;
                }
                throw new KeyNotFoundException($"Key '{key}' not found in the mapped list.");
            }
            set
            {
                if (_lookup.ContainsKey(key))
                {
                    _lookup[key] = value;
                }
                else
                {
                    throw new KeyNotFoundException($"Key '{key}' not found in the mapped list.");
                }
            }
        }

        public ICollection<K> Keys => _lookup.Keys;

        public ICollection<T> Values => _lookup.Values;

        public int Count => _lookup.Count;

        public bool IsReadOnly => false;

        public void Add(K key, T value)
        {
            if (_lookup.ContainsKey(key))
            {
                throw new ArgumentException($"Key '{key}' already exists in the mapped list.", nameof(key));
            }
            _lookup[key] = value;
        }

        public void Add(KeyValuePair<K, T> item)
        {
            if (_lookup.ContainsKey(item.Key))
            {
                throw new ArgumentException($"Key '{item.Key}' already exists in the mapped list.", nameof(item));
            }
            _lookup[item.Key] = item.Value;
        }

        public void Clear()
        {
            _lookup.Clear();
        }

        public bool Contains(KeyValuePair<K, T> item)
        {
            if (_lookup.TryGetValue(item.Key, out var value))
            {
                return EqualityComparer<T>.Default.Equals(value, item.Value);
            }
            return false;
        }

        public bool ContainsKey(K key)
        {
            return _lookup.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<K, T>[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array), "Array cannot be null");
            }

            if (arrayIndex < 0 || arrayIndex + Count > array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), "Invalid array index");
            }

            foreach (var kvp in _lookup)
            {
                array[arrayIndex++] = kvp;
            }
        }

        public IEnumerator<KeyValuePair<K, T>> GetEnumerator()
        {
            foreach (var kvp in _lookup)
            {
                yield return kvp;
            }
        }

        public bool Remove(K key)
        {
            if (_lookup.ContainsKey(key))
            {
                return _lookup.Remove(key);
            }
            return false;
        }

        public bool Remove(KeyValuePair<K, T> item)
        {
            if (_lookup.TryGetValue(item.Key, out var value) && EqualityComparer<T>.Default.Equals(value, item.Value))
            {
                return _lookup.Remove(item.Key);
            }
            return false;
        }

        public bool TryGetValue(K key, out T value)
        {
            return _lookup.TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _lookup.GetEnumerator();
        }
    }

}