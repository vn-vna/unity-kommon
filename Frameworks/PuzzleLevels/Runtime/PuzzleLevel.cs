using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    /// <summary>
    /// Defensive static facade over <see cref="PuzzleLevelManager"/>.
    ///
    /// Awaitable methods are the preferred Unity 6 API. Task and coroutine
    /// variants are provided for non-Unity async code and legacy callers.
    /// </summary>
    public static class PuzzleLevel
    {
        #region Interfaces & Properties

        public static PuzzleLevelManager Manager => PuzzleLevelManager.Instance;

        public static bool IsAvailable => Manager != null;

        public static bool IsInitialized =>
            Status == ResourceManagerStatus.Initialized;

        public static ResourceManagerStatus Status =>
            Manager?.Status ?? ResourceManagerStatus.Uninitialized;

        public static bool AllowLoadLevels
        {
            get => Manager != null && Manager.AllowLoadLevels;
            set
            {
                if (TryGetManager(out PuzzleLevelManager manager))
                {
                    manager.AllowLoadLevels = value;
                }
            }
        }

        public static IReadOnlyCollection<string> CachedLevelIds =>
            Manager?.CachedLevelIds ?? Array.Empty<string>();

        #endregion

        #region Public Methods

        public static void Initialize(float timeout = float.MaxValue)
        {
            if (!TryValidateTimeout(timeout, out Exception validationError))
            {
                LogError(validationError);
                return;
            }

            if (!TryGetManager(out PuzzleLevelManager manager))
            {
                return;
            }

            manager.InitializeManager(timeout);
        }

        public static async Awaitable InitializeAsync(
            float timeout = float.MaxValue,
            CancellationToken cancellationToken = default)
        {
            await InitializeTaskAsync(timeout, cancellationToken);
        }

        public static Task InitializeTaskAsync(
            float timeout = float.MaxValue,
            CancellationToken cancellationToken = default)
        {
            if (!TryValidateTimeout(timeout, out Exception validationError))
            {
                return Task.FromException(validationError);
            }

            if (!TryGetManager(out PuzzleLevelManager manager))
            {
                return Task.FromException(CreateManagerUnavailableException());
            }

            return RunCoroutineAsTask(
                manager.InitializeManagerCoroutine(timeout),
                cancellationToken,
                "initialize");
        }

        public static IEnumerator InitializeCoroutine(
            float timeout = float.MaxValue,
            Action<Exception> onError = null)
        {
            if (!TryValidateTimeout(timeout, out Exception validationError))
            {
                ReportError(onError, validationError);
                yield break;
            }

            if (!TryGetManager(out PuzzleLevelManager manager))
            {
                ReportError(onError, CreateManagerUnavailableException());
                yield break;
            }

            IEnumerator operation = manager.InitializeManagerCoroutine(timeout);
            while (true)
            {
                bool hasNext;
                object current = null;
                try
                {
                    hasNext = operation.MoveNext();
                    if (hasNext)
                    {
                        current = operation.Current;
                    }
                }
                catch (Exception exception)
                {
                    ReportError(onError, exception);
                    yield break;
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return current;
            }
        }

        public static async Awaitable<IPuzzleLevelData> GetLevelAsync(
            string levelId,
            float timeoutSeconds = 30f,
            CancellationToken cancellationToken = default)
        {
            if (!TryValidateLevelRequest(
                    levelId,
                    timeoutSeconds,
                    out Exception validationError))
            {
                LogError(validationError);
                throw validationError;
            }

            if (!TryGetManager(out PuzzleLevelManager manager))
            {
                throw CreateManagerUnavailableException();
            }

            if (!TryGetDispatcher(out Exception dispatcherError))
            {
                throw dispatcherError;
            }

            ResourceLoadingHandler<IPuzzleLevelData> handler;
            try
            {
                handler = manager.GetLevelAsync(levelId);
            }
            catch (Exception exception)
            {
                LogError(exception, "Puzzle level '{0}' failed to start.", levelId);
                throw;
            }

            float startTime = Time.realtimeSinceStartup;
            while (handler != null
                && handler.LoadingStatus != LoadingStatus.Completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (HasTimedOut(startTime, timeoutSeconds))
                {
                    throw CreateTimeoutException(levelId, timeoutSeconds);
                }

                await Awaitable.NextFrameAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (handler == null)
            {
                throw new InvalidOperationException(
                    $"Puzzle level '{levelId}' returned a null loading handler.");
            }

            if (handler.ResourceStatus != ResourceStatus.Loaded
                || handler.Resouce == null)
            {
                throw handler.Exception
                    ?? new InvalidOperationException(
                        $"Puzzle level '{levelId}' failed to load.");
            }

            return handler.Resouce;
        }

        public static Task<IPuzzleLevelData> GetLevelTaskAsync(
            string levelId,
            float timeoutSeconds = 30f,
            CancellationToken cancellationToken = default)
        {
            if (!TryValidateLevelRequest(
                    levelId,
                    timeoutSeconds,
                    out Exception validationError))
            {
                return Task.FromException<IPuzzleLevelData>(validationError);
            }

            if (!TryGetManager(out _))
            {
                return Task.FromException<IPuzzleLevelData>(
                    CreateManagerUnavailableException());
            }

            if (!TryGetDispatcher(out Exception dispatcherError))
            {
                return Task.FromException<IPuzzleLevelData>(dispatcherError);
            }

            return RunLevelCoroutineAsTask(
                levelId,
                timeoutSeconds,
                cancellationToken);
        }

        public static IEnumerator GetLevelCoroutine(
            string levelId,
            Action<IPuzzleLevelData> onLoaded = null,
            Action<Exception> onError = null,
            float timeoutSeconds = 30f,
            CancellationToken cancellationToken = default)
        {
            if (!TryValidateLevelRequest(
                    levelId,
                    timeoutSeconds,
                    out Exception validationError))
            {
                ReportError(onError, validationError);
                yield break;
            }

            if (!TryGetManager(out PuzzleLevelManager manager))
            {
                ReportError(onError, CreateManagerUnavailableException());
                yield break;
            }

            if (!TryGetDispatcher(out Exception dispatcherError))
            {
                ReportError(onError, dispatcherError);
                yield break;
            }

            ResourceLoadingHandler<IPuzzleLevelData> handler;
            try
            {
                handler = manager.GetLevelAsync(levelId);
            }
            catch (Exception exception)
            {
                ReportError(onError, exception);
                yield break;
            }

            float startTime = Time.realtimeSinceStartup;
            while (handler != null
                && handler.LoadingStatus != LoadingStatus.Completed)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    ReportError(
                        onError,
                        new OperationCanceledException(cancellationToken));
                    yield break;
                }

                if (HasTimedOut(startTime, timeoutSeconds))
                {
                    ReportError(
                        onError,
                        CreateTimeoutException(levelId, timeoutSeconds));
                    yield break;
                }

                yield return null;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                ReportError(
                    onError,
                    new OperationCanceledException(cancellationToken));
                yield break;
            }

            if (handler == null)
            {
                ReportError(
                    onError,
                    new InvalidOperationException(
                        $"Puzzle level '{levelId}' returned a null loading handler."));
                yield break;
            }

            if (handler.ResourceStatus != ResourceStatus.Loaded
                || handler.Resouce == null)
            {
                ReportError(
                    onError,
                    handler.Exception
                    ?? new InvalidOperationException(
                        $"Puzzle level '{levelId}' failed to load."));
                yield break;
            }

            try
            {
                onLoaded?.Invoke(handler.Resouce);
            }
            catch (Exception exception)
            {
                LogError(exception);
                ReportError(onError, exception);
            }
        }

        public static bool TryGetPreloadedLevel(
            string levelId,
            out IPuzzleLevelData data)
        {
            data = null;
            if (!TryValidateLevelId(levelId, out Exception validationError))
            {
                LogError(validationError);
                return false;
            }

            return TryGetManager(out PuzzleLevelManager manager)
                && manager.TryGetPreloadedLevel(levelId, out data);
        }

        public static void Preload(string levelId)
        {
            if (!TryValidateLevelId(levelId, out Exception validationError))
            {
                LogError(validationError);
                return;
            }

            if (TryGetManager(out PuzzleLevelManager manager))
            {
                manager.PreloadLevel(levelId);
            }
        }

        public static void ClearCache()
        {
            if (TryGetManager(out PuzzleLevelManager manager))
            {
                manager.ClearCache();
            }
        }

        public static void RefreshCatalogs(
            CatalogInvalidationMode mode = CatalogInvalidationMode.Aggressive)
        {
            if (!TryValidateInvalidationMode(mode, out Exception validationError))
            {
                LogError(validationError);
                return;
            }

            if (TryGetManager(out PuzzleLevelManager manager))
            {
                manager.RefreshCatalogs(mode);
            }
        }

        public static async Awaitable RefreshCatalogsAsync(
            CatalogInvalidationMode mode = CatalogInvalidationMode.Aggressive,
            CancellationToken cancellationToken = default)
        {
            await RefreshCatalogsTaskAsync(mode, cancellationToken);
        }

        public static Task RefreshCatalogsTaskAsync(
            CatalogInvalidationMode mode = CatalogInvalidationMode.Aggressive,
            CancellationToken cancellationToken = default)
        {
            if (!TryValidateInvalidationMode(mode, out Exception validationError))
            {
                return Task.FromException(validationError);
            }

            if (!TryGetManager(out PuzzleLevelManager manager))
            {
                return Task.FromException(CreateManagerUnavailableException());
            }

            return RunCoroutineAsTask(
                manager.RefreshCatalogsCoroutine(mode),
                cancellationToken,
                "refresh catalogs");
        }

        public static IEnumerator RefreshCatalogsCoroutine(
            CatalogInvalidationMode mode = CatalogInvalidationMode.Aggressive,
            Action<Exception> onError = null)
        {
            if (!TryValidateInvalidationMode(mode, out Exception validationError))
            {
                ReportError(onError, validationError);
                yield break;
            }

            if (!TryGetManager(out PuzzleLevelManager manager))
            {
                ReportError(onError, CreateManagerUnavailableException());
                yield break;
            }

            IEnumerator operation = manager.RefreshCatalogsCoroutine(mode);
            while (true)
            {
                bool hasNext;
                object current = null;
                try
                {
                    hasNext = operation.MoveNext();
                    if (hasNext)
                    {
                        current = operation.Current;
                    }
                }
                catch (Exception exception)
                {
                    ReportError(onError, exception);
                    yield break;
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return current;
            }
        }

        #endregion

        #region Private Methods

        private static Task RunCoroutineAsTask(
            IEnumerator operation,
            CancellationToken cancellationToken,
            string operationName)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (!TryGetDispatcher(out Exception dispatcherError))
            {
                return Task.FromException(dispatcherError);
            }

            TaskCompletionSource<bool> completion =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = default;
            registration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));

            Coroutine coroutine = Dispatcher.DispatchCoroutine(
                CompleteCoroutine(
                    operation,
                    completion,
                    cancellationToken,
                    operationName,
                    registration.Dispose));

            if (coroutine == null)
            {
                registration.Dispose();
                completion.TrySetException(
                    new InvalidOperationException(
                        $"Puzzle level {operationName} coroutine could not be dispatched."));
            }

            return completion.Task;
        }

        private static Task<IPuzzleLevelData> RunLevelCoroutineAsTask(
            string levelId,
            float timeoutSeconds,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<IPuzzleLevelData>(cancellationToken);
            }

            TaskCompletionSource<IPuzzleLevelData> completion =
                new TaskCompletionSource<IPuzzleLevelData>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = default;
            registration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));

            IEnumerator operation = GetLevelCoroutine(
                levelId,
                data => completion.TrySetResult(data),
                exception => SetLevelTaskException(
                    completion,
                    exception,
                    cancellationToken),
                timeoutSeconds,
                cancellationToken);

            Coroutine coroutine = Dispatcher.DispatchCoroutine(
                CompleteLevelCoroutine(
                    operation,
                    completion,
                    cancellationToken,
                    levelId,
                    registration.Dispose));

            if (coroutine == null)
            {
                registration.Dispose();
                completion.TrySetException(
                    new InvalidOperationException(
                        "Puzzle level coroutine could not be dispatched."));
            }

            return completion.Task;
        }

        private static IEnumerator CompleteCoroutine(
            IEnumerator operation,
            TaskCompletionSource<bool> completion,
            CancellationToken cancellationToken,
            string operationName,
            Action disposeRegistration)
        {
            while (!completion.Task.IsCompleted)
            {
                bool hasNext;
                object current = null;
                try
                {
                    hasNext = operation.MoveNext();
                    if (hasNext)
                    {
                        current = operation.Current;
                    }
                }
                catch (Exception exception)
                {
                    LogError(
                        exception,
                        "Puzzle level {0} failed.",
                        operationName);
                    completion.TrySetException(exception);
                    disposeRegistration();
                    yield break;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return current;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            else if (!completion.Task.IsCompleted)
            {
                completion.TrySetResult(true);
            }

            disposeRegistration();
        }

        private static IEnumerator CompleteLevelCoroutine(
            IEnumerator operation,
            TaskCompletionSource<IPuzzleLevelData> completion,
            CancellationToken cancellationToken,
            string levelId,
            Action disposeRegistration)
        {
            while (!completion.Task.IsCompleted)
            {
                bool hasNext;
                object current = null;
                try
                {
                    hasNext = operation.MoveNext();
                    if (hasNext)
                    {
                        current = operation.Current;
                    }
                }
                catch (Exception exception)
                {
                    LogError(exception, "Puzzle level '{0}' failed.", levelId);
                    completion.TrySetException(exception);
                    disposeRegistration();
                    yield break;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return current;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            else if (!completion.Task.IsCompleted)
            {
                completion.TrySetException(
                    new InvalidOperationException(
                        $"Puzzle level '{levelId}' completed without a result."));
            }

            disposeRegistration();
        }

        private static void SetLevelTaskException(
            TaskCompletionSource<IPuzzleLevelData> completion,
            Exception exception,
            CancellationToken cancellationToken)
        {
            if (exception is OperationCanceledException
                && cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            completion.TrySetException(exception);
        }

        private static bool TryGetManager(out PuzzleLevelManager manager)
        {
            manager = Manager;
            if (manager != null)
            {
                return true;
            }

            LogError(CreateManagerUnavailableException());
            return false;
        }

        private static bool TryGetDispatcher(out Exception error)
        {
            if (Dispatcher.Instance != null)
            {
                error = null;
                return true;
            }

            error = new InvalidOperationException(
                "Puzzle level operations require a Dispatcher instance. "
                + "Ensure the Scheherazade dispatcher is present before loading levels.");
            LogError(error);
            return false;
        }

        private static bool TryValidateLevelRequest(
            string levelId,
            float timeoutSeconds,
            out Exception error)
        {
            if (!TryValidateLevelId(levelId, out error))
            {
                return false;
            }

            return TryValidateTimeout(timeoutSeconds, out error);
        }

        private static bool TryValidateLevelId(
            string levelId,
            out Exception error)
        {
            if (!string.IsNullOrWhiteSpace(levelId))
            {
                error = null;
                return true;
            }

            error = new ArgumentException(
                "Puzzle level id cannot be null, empty, or whitespace.",
                nameof(levelId));
            return false;
        }

        private static bool TryValidateTimeout(
            float timeout,
            out Exception error)
        {
            if (timeout >= 0f && !float.IsNaN(timeout)
                && !float.IsInfinity(timeout))
            {
                error = null;
                return true;
            }

            error = new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "Timeout must be finite and greater than or equal to zero.");
            return false;
        }

        private static bool TryValidateInvalidationMode(
            CatalogInvalidationMode mode,
            out Exception error)
        {
            if (Enum.IsDefined(typeof(CatalogInvalidationMode), mode))
            {
                error = null;
                return true;
            }

            error = new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unknown catalog invalidation mode.");
            return false;
        }

        private static bool HasTimedOut(float startTime, float timeoutSeconds)
        {
            return timeoutSeconds >= 0f
                && Time.realtimeSinceStartup - startTime >= timeoutSeconds;
        }

        private static Exception CreateManagerUnavailableException()
        {
            return new InvalidOperationException(
                "PuzzleLevelManager is not available. "
                + "Create and configure a PuzzleLevelManager asset in Resources.");
        }

        private static Exception CreateTimeoutException(
            string levelId,
            float timeoutSeconds)
        {
            return new TimeoutException(
                $"Loading puzzle level '{levelId}' timed out after "
                + $"{timeoutSeconds:F1} seconds.");
        }

        private static void ReportError(
            Action<Exception> onError,
            Exception exception)
        {
            LogError(exception);
            if (onError == null)
            {
                return;
            }

            try
            {
                onError.Invoke(exception);
            }
            catch (Exception callbackException)
            {
                LogError(callbackException, "Puzzle level error callback failed.");
            }
        }

        private static void LogError(
            Exception exception,
            string message = "Puzzle level operation failed.",
            params object[] args)
        {
            QuickLog.Log(
                "{0} Exception: {1}",
                nameof(PuzzleLevel),
                LogLevel.Error,
                new object[]
                {
                args == null || args.Length == 0
                    ? message
                    : string.Format(message, args),
                exception
                });
        }

        #endregion
    }
}
