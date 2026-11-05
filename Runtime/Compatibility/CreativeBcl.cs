#if PLA_CREATIVE_BUILD
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Com.Scheherazade.Common.Compatibility
{
    /// <summary>Bridge's Array.Empty signature returns System.Array rather than T[].</summary>
    public static class CreativeArray
    {
        public static T[] Empty<T>() => EmptyStorage<T>.Value;

        private static class EmptyStorage<T>
        {
            internal static readonly T[] Value = new T[0];
        }
    }

    /// <summary>Only the numeric overloads missing from the retained Bridge surface.</summary>
    public static class CreativeMath
    {
        public static int Clamp(int value, int min, int max)
        {
            if (min > max) throw new ArgumentException("Minimum must not exceed maximum.");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (min > max) throw new ArgumentException("Minimum must not exceed maximum.");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static int Sign(int value) => value < 0 ? -1 : value > 0 ? 1 : 0;
    }

    /// <summary>
    /// Selected by callsites ONLY for PLA_CREATIVE_BUILD && UNITY_LUNA, whose code
    /// runs on one JavaScript thread. This is not a worker synchronization API.
    /// All native builds, including creative Editor tests, retain real Interlocked.
    /// </summary>
    public static class CreativeSingleThreadInterlocked
    {
        public static int Exchange(ref int location, int value)
        {
            int previous = location;
            location = value;
            return previous;
        }

        public static T CompareExchange<T>(ref T location, T value, T comparand) where T : class
        {
            T previous = location;
            if (ReferenceEquals(previous, comparand)) location = value;
            return previous;
        }

        public static int Increment(ref int location)
        {
            location = unchecked(location + 1);
            return location;
        }
    }

    public static class CreativeTasks
    {
        public static Task<T> FromCanceled<T>(CancellationToken cancellationToken)
        {
#if UNITY_LUNA
            if (!cancellationToken.IsCancellationRequested)
                throw new ArgumentOutOfRangeException(nameof(cancellationToken));
            // Bridge exposes genuine canceled task state, but no token-taking SetCanceled
            // overload. Browser exception-token metadata is therefore not promised.
            var completion = new TaskCompletionSource<T>();
            completion.SetCanceled();
            return completion.Task;
#else
            // Preserve the native exception token as well as task state in the Editor.
            return Task.FromCanceled<T>(cancellationToken);
#endif
        }
    }
}
#endif
