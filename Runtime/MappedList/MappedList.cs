using System;
using System.Collections;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.MappedList
{
    /// <summary>
    /// A dictionary-like collection that maps keys to values using a key selector function.
    /// </summary>
    /// <typeparam name="K">The type of keys, which must not be null.</typeparam>
    /// <typeparam name="T">The type of values, which must be reference types.</typeparam>
    /// <remarks>
    /// This class creates a lookup dictionary from a list of items by extracting keys using a selector function.
    /// It implements IDictionary and provides standard dictionary operations while ensuring no duplicate keys exist.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create a mapped list from a collection of players
    /// var players = new List&lt;Player&gt; { player1, player2, player3 };
    /// var playerMap = new MappedList&lt;string, Player&gt;(players, p => p.Name);
    /// 
    /// // Access players by name
    /// var player = playerMap["John"];
    /// 
    /// // Check if a player exists
    /// if (playerMap.ContainsKey("Jane"))
    /// {
    ///     // Do something
    /// }
    /// </code>
    /// </example>
    public class MappedList<K, T> : IDictionary<K, T>
        where K : notnull
        where T : class
    {
        private Dictionary<K, T> _lookup;

        /// <summary>
        /// Initializes a new instance of the MappedList class from a list and key selector.
        /// </summary>
        /// <param name="list">The list of items to create the mapped list from.</param>
        /// <param name="keySelecttor">A function to extract the key from each item.</param>
        /// <exception cref="ArgumentNullException">Thrown when list or keySelecttor is null.</exception>
        /// <exception cref="ArgumentException">Thrown when list contains null items or duplicate keys.</exception>
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

        /// <summary>
        /// Gets or sets the value associated with the specified key.
        /// </summary>
        /// <param name="key">The key of the value to get or set.</param>
        /// <returns>The value associated with the specified key.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the key is not found in the collection.</exception>
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

        /// <summary>
        /// Gets a collection containing the keys in the MappedList.
        /// </summary>
        public ICollection<K> Keys => _lookup.Keys;

        /// <summary>
        /// Gets a collection containing the values in the MappedList.
        /// </summary>
        public ICollection<T> Values => _lookup.Values;

        /// <summary>
        /// Gets the number of key-value pairs in the MappedList.
        /// </summary>
        public int Count => _lookup.Count;

        /// <summary>
        /// Gets a value indicating whether the MappedList is read-only (always false).
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// Adds a key-value pair to the MappedList.
        /// </summary>
        /// <param name="key">The key to add.</param>
        /// <param name="value">The value to add.</param>
        /// <exception cref="ArgumentException">Thrown when the key already exists.</exception>
        public void Add(K key, T value)
        {
            if (_lookup.ContainsKey(key))
            {
                throw new ArgumentException($"Key '{key}' already exists in the mapped list.", nameof(key));
            }
            _lookup[key] = value;
        }

        /// <summary>
        /// Adds a key-value pair to the MappedList.
        /// </summary>
        /// <param name="item">The key-value pair to add.</param>
        /// <exception cref="ArgumentException">Thrown when the key already exists.</exception>
        public void Add(KeyValuePair<K, T> item)
        {
            if (_lookup.ContainsKey(item.Key))
            {
                throw new ArgumentException($"Key '{item.Key}' already exists in the mapped list.", nameof(item));
            }
            _lookup[item.Key] = item.Value;
        }

        /// <summary>
        /// Removes all key-value pairs from the MappedList.
        /// </summary>
        public void Clear()
        {
            _lookup.Clear();
        }

        /// <summary>
        /// Determines whether the MappedList contains a specific key-value pair.
        /// </summary>
        /// <param name="item">The key-value pair to locate.</param>
        /// <returns>True if the item is found; otherwise, false.</returns>
        public bool Contains(KeyValuePair<K, T> item)
        {
            if (_lookup.TryGetValue(item.Key, out var value))
            {
                return EqualityComparer<T>.Default.Equals(value, item.Value);
            }
            return false;
        }

        /// <summary>
        /// Determines whether the MappedList contains the specified key.
        /// </summary>
        /// <param name="key">The key to locate.</param>
        /// <returns>True if the key is found; otherwise, false.</returns>
        public bool ContainsKey(K key)
        {
            return _lookup.ContainsKey(key);
        }

        /// <summary>
        /// Copies the key-value pairs to an array, starting at a specified array index.
        /// </summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The zero-based index in array at which copying begins.</param>
        /// <exception cref="ArgumentNullException">Thrown when array is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when arrayIndex is invalid.</exception>
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

        /// <summary>
        /// Returns an enumerator that iterates through the key-value pairs.
        /// </summary>
        /// <returns>An enumerator for the MappedList.</returns>
        public IEnumerator<KeyValuePair<K, T>> GetEnumerator()
        {
            foreach (var kvp in _lookup)
            {
                yield return kvp;
            }
        }

        /// <summary>
        /// Removes the value with the specified key from the MappedList.
        /// </summary>
        /// <param name="key">The key of the element to remove.</param>
        /// <returns>True if the element was removed; otherwise, false.</returns>
        public bool Remove(K key)
        {
            if (_lookup.ContainsKey(key))
            {
                return _lookup.Remove(key);
            }
            return false;
        }

        /// <summary>
        /// Removes a specific key-value pair from the MappedList.
        /// </summary>
        /// <param name="item">The key-value pair to remove.</param>
        /// <returns>True if the item was removed; otherwise, false.</returns>
        public bool Remove(KeyValuePair<K, T> item)
        {
            if (_lookup.TryGetValue(item.Key, out var value) && EqualityComparer<T>.Default.Equals(value, item.Value))
            {
                return _lookup.Remove(item.Key);
            }
            return false;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        /// <param name="key">The key of the value to get.</param>
        /// <param name="value">When this method returns, contains the value associated with the key, if found.</param>
        /// <returns>True if the key was found; otherwise, false.</returns>
        public bool TryGetValue(K key, out T value)
        {
            return _lookup.TryGetValue(key, out value);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>An enumerator for the collection.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return _lookup.GetEnumerator();
        }
    }

}