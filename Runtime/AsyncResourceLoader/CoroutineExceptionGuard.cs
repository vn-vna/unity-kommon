using System;
using System.Collections;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    internal static class CoroutineExceptionGuard
    {
        public static IEnumerator Run(
            IEnumerator coroutine,
            Action<Exception> exceptionHandler)
        {
            if (coroutine == null)
            {
                yield break;
            }

            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(coroutine);
            while (stack.Count > 0)
            {
                IEnumerator currentCoroutine = stack.Peek();
                Exception caughtException = null;
                bool hasNext = false;
                object current = null;
                try
                {
                    hasNext = currentCoroutine.MoveNext();
                    if (hasNext)
                    {
                        current = currentCoroutine.Current;
                    }
                }
                catch (Exception exception)
                {
                    caughtException = exception;
                }

                if (caughtException != null)
                {
                    Exception combinedException = DisposeCoroutines(
                        stack,
                        caughtException);
                    exceptionHandler?.Invoke(combinedException);
                    yield break;
                }

                if (!hasNext)
                {
                    stack.Pop();
                    Exception disposeException = TryDispose(currentCoroutine);
                    if (disposeException != null)
                    {
                        Exception combinedException = DisposeCoroutines(
                            stack,
                            disposeException);
                        exceptionHandler?.Invoke(combinedException);
                        yield break;
                    }

                    continue;
                }

                if (current is IEnumerator nestedCoroutine)
                {
                    stack.Push(nestedCoroutine);
                    continue;
                }

                yield return current;
            }
        }

        private static Exception DisposeCoroutines(
            Stack<IEnumerator> stack,
            Exception primaryException)
        {
            List<Exception> exceptions = new List<Exception>
            {
                primaryException
            };
            while (stack.Count > 0)
            {
                Exception disposeException = TryDispose(stack.Pop());
                if (disposeException != null)
                {
                    exceptions.Add(disposeException);
                }
            }

            return exceptions.Count == 1
                ? exceptions[0]
                : new AggregateException(
                    "Coroutine execution and cleanup both failed.",
                    exceptions);
        }

        private static Exception TryDispose(IEnumerator coroutine)
        {
            try
            {
                (coroutine as IDisposable)?.Dispose();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }
}
