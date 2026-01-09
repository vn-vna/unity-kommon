using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Extensions
{
    /// <summary>
    /// Provides extension methods for collection types including arrays and IEnumerable.
    /// </summary>
    /// <remarks>
    /// This class extends arrays and collections with safe access methods, iteration utilities,
    /// and random element selection capabilities.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Safe 2D array access
    /// T[,] grid = new T[10, 10];
    /// var value = grid.Access(new Vector2Int(5, 3));
    /// 
    /// // Iterate with index
    /// myList.IterateThrough((item, index) => {
    ///     Debug.Log($"{index}: {item}");
    /// });
    /// 
    /// // Get random element
    /// var randomItem = myList.Random();
    /// </code>
    /// </example>
    public static class CollectionExtensions
    {
        /// <summary>
        /// Safely accesses a 2D array element using a Vector2Int index.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="array">The 2D array to access.</param>
        /// <param name="index">The 2D index as a Vector2Int.</param>
        /// <returns>The element at the specified index.</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown when index is out of bounds.</exception>
        public static T Access<T>(this T[,] array, Vector2Int index)
        {
            return Access(array, index.x, index.y);
        }

        /// <summary>
        /// Safely accesses a 2D array element using X and Y indices.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="array">The 2D array to access.</param>
        /// <param name="x">The X index.</param>
        /// <param name="y">The Y index.</param>
        /// <returns>The element at the specified indices.</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown when indices are out of bounds.</exception>
        public static T Access<T>(this T[,] array, int x, int y)
        {
            if (x < 0 || x >= array.GetLength(0))
                throw new IndexOutOfRangeException($"X index {x} is out of bounds for array with length {array.GetLength(0)}");
            if (y < 0 || y >= array.GetLength(1))
                throw new IndexOutOfRangeException($"Y index {y} is out of bounds for array with length {array.GetLength(1)}");

            return array[x, y];
        }

        /// <summary>
        /// Safely accesses a 3D array element using a Vector3Int index.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="array">The 3D array to access.</param>
        /// <param name="index">The 3D index as a Vector3Int.</param>
        /// <returns>The element at the specified index.</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown when index is out of bounds.</exception>
        public static T Access<T>(this T[,,] array, Vector3Int index)
        {
            return Access(array, index.x, index.y, index.z);
        }

        /// <summary>
        /// Safely accesses a 3D array element using X, Y, and Z indices.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="array">The 3D array to access.</param>
        /// <param name="x">The X index.</param>
        /// <param name="y">The Y index.</param>
        /// <param name="z">The Z index.</param>
        /// <returns>The element at the specified indices.</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown when indices are out of bounds.</exception>
        public static T Access<T>(this T[,,] array, int x, int y, int z)
        {
            if (x < 0 || x >= array.GetLength(0))
                throw new IndexOutOfRangeException($"X index {x} is out of bounds for array with length {array.GetLength(0)}");
            if (y < 0 || y >= array.GetLength(1))
                throw new IndexOutOfRangeException($"Y index {y} is out of bounds for array with length {array.GetLength(1)}");
            if (z < 0 || z >= array.GetLength(2))
                throw new IndexOutOfRangeException($"Z index {z} is out of bounds for array with length {array.GetLength(2)}");

            return array[x, y, z];
        }

        /// <summary>
        /// Iterates through a collection, invoking an action for each element with its index.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="array">The collection to iterate.</param>
        /// <param name="action">The action to invoke for each element, receiving the element and its index.</param>
        /// <returns>The total number of elements processed.</returns>
        public static int IterateThrough<T>(this IEnumerable<T> array, Action<T, int> action)
        {
            int count = 0;
            foreach (var item in array)
            {
                action(item, count);
                count++;
            }
            return count;
        }

        public static IEnumerable<(int, T)> EnumerateWithIndex<T>(this IEnumerable<T> array)
        {
            return array.Select((item, index) => (index, item));
        }

        public static IEnumerable<(T, U)> ZipWith<T, U>(this IEnumerable<T> first, IEnumerable<U> second)
        {
            using IEnumerator<T> enum1 = first.GetEnumerator();
            using IEnumerator<U> enum2 = second.GetEnumerator();

            while (enum1.MoveNext() && enum2.MoveNext())
            {
                yield return (enum1.Current, enum2.Current);
            }

            if (enum1.MoveNext() || enum2.MoveNext())
            {
                throw new ArgumentException("Sequences are of different lengths.");
            }
        }

        public static IEnumerable<T> ConcatWith<T>(this IEnumerable<T> first, IEnumerable<T> second)
        {
            foreach (var item in first)
            {
                yield return item;
            }
            foreach (var item in second)
            {
                yield return item;
            }
        }

        public static void ForEach<T>(this IEnumerable<T> array, Action<T> action)
        {
            foreach (var item in array)
            {
                action(item);
            }
        }

        public static void IterateRectangular<T>(this T[,] array, Action<T, int, int, int> action)
        {
            int lengthX = array.GetLength(0);
            int lengthY = array.GetLength(1);

            for (int x = 0; x < lengthX; x++)
            {
                for (int y = 0; y < lengthY; y++)
                {
                    action(array[x, y], x, y, x + y * lengthX);
                }
            }
        }

        public static void IterateRectangular<T>(this T[] flatten, int w, int h, int ox, int oy, Action<T, int, int, int> action)
        {
            if (w * h > flatten.Length)
                throw new ArgumentException(
                    "Width and height do not match the length of the flattened array."
                );

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int index = (ox + x) + (oy + y) * w;
                    action(flatten[index], x, y, index);
                }
            }
        }
    }
}
