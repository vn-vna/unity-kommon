using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Extensions
{
    public static class CollectionExtensions
    {
        public static T Access<T>(this T[,] array, Vector2Int index)
        {
            return Access(array, index.x, index.y);
        }

        public static T Access<T>(this T[,] array, int x, int y)
        {
            if (x < 0 || x >= array.GetLength(0))
                throw new IndexOutOfRangeException($"X index {x} is out of bounds for array with length {array.GetLength(0)}");
            if (y < 0 || y >= array.GetLength(1))
                throw new IndexOutOfRangeException($"Y index {y} is out of bounds for array with length {array.GetLength(1)}");

            return array[x, y];
        }

        public static T Access<T>(this T[,,] array, Vector3Int index)
        {
            return Access(array, index.x, index.y, index.z);
        }

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
