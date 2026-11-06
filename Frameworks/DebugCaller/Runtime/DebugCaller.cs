using System;
using Com.Scheherazade.Common.Logging;
using SRDebugger.Services;
using UnityEngine;
using UnityEngine.Serialization;

namespace Com.Scheherazade.Common.DebugCaller
{
    [AddComponentMenu("Scheherazade/Debug/Debug Caller")]
    [DisallowMultipleComponent]
    public sealed class DebugCaller : MonoBehaviour
    {
        #region Constants

        private const float DefaultCornerHoldDuration = 5f;
        private const float DefaultTapWindowDuration = 5f;
        private const int DefaultRequiredTapCount = 7;
        private const float DefaultCornerMinimumSizeInches = 0.9f;
        private const float DefaultCornerFallbackRatio = 0.25f;
        private const float DefaultCornerMaximumRatio = 0.45f;
        private const float DefaultBorderThickness = 6f;
        private const float DefaultBorderThicknessInches = 0.025f;

        #endregion

        #region Events & Delegates

        public static event Action DebuggerRequested;

        #endregion

        #region Serialized Fields

        [SerializeField, Min(0.1f)]
        private float cornerHoldDuration = DefaultCornerHoldDuration;

        [SerializeField, Min(0.1f)]
        private float tapWindowDuration = DefaultTapWindowDuration;

        [SerializeField, Min(1)]
        private int requiredTapCount = DefaultRequiredTapCount;

        [SerializeField, Min(0.1f)]
        [Tooltip("Minimum physical width and height of each corner touch target when device DPI is available.")]
        private float cornerMinimumSizeInches = DefaultCornerMinimumSizeInches;

        [FormerlySerializedAs("cornerZoneRatio")]
        [SerializeField, Range(0.05f, 0.45f)]
        [Tooltip("Fallback target size relative to the shorter screen edge when DPI is unavailable or too small.")]
        private float cornerFallbackRatio = DefaultCornerFallbackRatio;

        [SerializeField, Range(0.05f, 0.45f)]
        [Tooltip("Maximum target size relative to the shorter screen edge, preventing adjacent corner overlap.")]
        private float cornerMaximumRatio = DefaultCornerMaximumRatio;

        [SerializeField, Min(1f)]
        [Tooltip("Minimum border thickness in pixels when DPI is unavailable or too small.")]
        private float borderThickness = DefaultBorderThickness;

        [SerializeField, Min(0f)]
        [Tooltip("Physical border thickness when device DPI is available.")]
        private float borderThicknessInches = DefaultBorderThicknessInches;

        [SerializeField]
        private Color borderColor = Color.red;

        #endregion

        #region Private Fields

        private static DebugCaller _instance;
        private static DebugCallerOptions _options;

        private DebugCallerGesture _gesture;
        private int _lastLoggedHoldSecond = -1;
        private int _lastLoggedTapCount;

        #endregion

        #region Unity Callbacks

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            DebugCaller existing = _instance != null
                ? _instance
                : FindAnyObjectByType<DebugCaller>(FindObjectsInactive.Include);
            if (existing != null && existing.gameObject.activeInHierarchy)
            {
                existing.enabled = true;
                return;
            }

            if (existing != null)
            {
                existing.enabled = false;
                if (_instance == existing)
                {
                    _instance = null;
                }
            }

            GameObject gameObject = new("[Scheherazade Debug Caller]");
            gameObject.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<DebugCaller>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            CreateGesture();
            LogTouchConfiguration();
            if (DebugCallerState.IsEnabled)
            {
                EnsureDebuggerInitialized();
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                _options = null;
            }
        }

        private void Update()
        {
            float currentTime = Time.realtimeSinceStartup;
            DebugCallerPhase previousPhase = _gesture.Phase;
            float previousHoldElapsed = _gesture.CornerHoldElapsed;
            DebugCallerCornerPair heldCorners = GetHeldCornerPair();

            _gesture.UpdateCornerHold(heldCorners, currentTime);
            LogHoldProgress(previousPhase, previousHoldElapsed, heldCorners);
            if (_gesture.Phase != DebugCallerPhase.AwaitingTapSequence)
            {
                return;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase != TouchPhase.Began)
                {
                    continue;
                }

                bool succeeded = _gesture.RegisterTap(
                    GetCorner(touch.position),
                    currentTime
                );
                LogTapProgress();
                if (!succeeded)
                {
                    continue;
                }

                QuickLog.Info<DebugCaller>(
                    "Gesture completed. Enabling debug mode and requesting SRDebugger."
                );
                DebugCallerState.Enable();
                RequestDebugger();
                ResetProgressLogging();
                _gesture.Reset();
                return;
            }
        }

        private void OnDisable()
        {
            ResetGesture();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                ResetGesture();
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                ResetGesture();
            }
        }

        private void OnGUI()
        {
            if (_gesture == null || !_gesture.IsBorderVisible)
            {
                return;
            }

            float screenWidth = Screen.width;
            float screenHeight = Screen.height;
            float screenDpi = Screen.dpi;
            bool hasUsableDpi = screenDpi > 0f
                && !float.IsNaN(screenDpi)
                && !float.IsInfinity(screenDpi);
            float dpiThickness = hasUsableDpi
                ? screenDpi * borderThicknessInches
                : 0f;
            float thickness = Mathf.Min(
                Mathf.Max(borderThickness, dpiThickness),
                Mathf.Min(screenWidth, screenHeight) * 0.5f
            );
            Color previousColor = GUI.color;
            GUI.color = borderColor;

            GUI.DrawTexture(new Rect(0f, 0f, screenWidth, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, screenHeight - thickness, screenWidth, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, thickness, thickness, screenHeight - thickness * 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(screenWidth - thickness, thickness, thickness, screenHeight - thickness * 2f),
                Texture2D.whiteTexture
            );

            GUI.color = previousColor;
        }

        #endregion

        #region Private Methods

        private void CreateGesture()
        {
            _gesture = new DebugCallerGesture(
                cornerHoldDuration,
                tapWindowDuration,
                requiredTapCount
            );
        }

        private void LogHoldProgress(
            DebugCallerPhase previousPhase,
            float previousHoldElapsed,
            DebugCallerCornerPair heldCorners)
        {
            if (previousPhase == DebugCallerPhase.AwaitingTapSequence
                && _gesture.Phase == DebugCallerPhase.AwaitingCornerHold)
            {
                QuickLog.Info<DebugCaller>(
                    "Tap window expired. Debug gesture reset."
                );
                ResetProgressLogging();
                return;
            }

            if (previousPhase == DebugCallerPhase.AwaitingCornerHold
                && _gesture.Phase == DebugCallerPhase.AwaitingTapSequence)
            {
                QuickLog.Info<DebugCaller>(
                    "Opposite-corner hold completed. Tap either held corner {0} times within {1:0.#} seconds.",
                    requiredTapCount,
                    tapWindowDuration
                );
                _lastLoggedHoldSecond = -1;
                return;
            }

            if (_gesture.Phase == DebugCallerPhase.AwaitingTapSequence)
            {
                return;
            }

            if (!heldCorners.IsOpposite)
            {
                if (_lastLoggedHoldSecond >= 0)
                {
                    QuickLog.Info<DebugCaller>(
                        "Opposite-corner hold cancelled after {0:0.0}/{1:0.#} seconds.",
                        previousHoldElapsed,
                        cornerHoldDuration
                    );
                }

                ResetProgressLogging();
                return;
            }

            int completedSeconds = Mathf.FloorToInt(_gesture.CornerHoldElapsed);
            if (completedSeconds == _lastLoggedHoldSecond)
            {
                return;
            }

            _lastLoggedHoldSecond = completedSeconds;
            QuickLog.Info<DebugCaller>(
                "Opposite-corner hold progress: {0}/{1} seconds.",
                completedSeconds,
                Mathf.CeilToInt(cornerHoldDuration)
            );
        }

        private void LogTapProgress()
        {
            if (_gesture.Phase == DebugCallerPhase.Succeeded)
            {
                QuickLog.Info<DebugCaller>(
                    "Corner tap progress: {0}/{1}.",
                    _gesture.TapCount,
                    requiredTapCount
                );
                return;
            }

            if (_gesture.Phase != DebugCallerPhase.AwaitingTapSequence)
            {
                QuickLog.Info<DebugCaller>(
                    "Tap sequence cancelled. Debug gesture reset."
                );
                ResetProgressLogging();
                return;
            }

            if (_gesture.TapCount == _lastLoggedTapCount)
            {
                return;
            }

            _lastLoggedTapCount = _gesture.TapCount;
            QuickLog.Info<DebugCaller>(
                "Corner tap progress: {0}/{1}.",
                _lastLoggedTapCount,
                requiredTapCount
            );
        }

        private void ResetProgressLogging()
        {
            _lastLoggedHoldSecond = -1;
            _lastLoggedTapCount = 0;
        }

        private void ResetGesture()
        {
            _gesture?.Reset();
            ResetProgressLogging();
        }

        private DebugCallerCornerPair GetHeldCornerPair()
        {
            if (Input.touchCount != 2)
            {
                return default;
            }

            Touch firstTouch = Input.GetTouch(0);
            Touch secondTouch = Input.GetTouch(1);
            if (!DebugCallerTouchGeometry.IsActiveTouchPhase(firstTouch.phase)
                || !DebugCallerTouchGeometry.IsActiveTouchPhase(secondTouch.phase))
            {
                return default;
            }

            return new DebugCallerCornerPair(
                GetCorner(firstTouch.position),
                GetCorner(secondTouch.position)
            );
        }

        private DebugCallerCorner GetCorner(Vector2 position)
        {
            Vector2 screenSize = new(Screen.width, Screen.height);
            return DebugCallerTouchGeometry.GetCorner(
                position,
                screenSize,
                ResolveCornerExtent(screenSize)
            );
        }

        private float ResolveCornerExtent(Vector2 screenSize)
        {
            return DebugCallerTouchGeometry.CalculateCornerExtent(
                screenSize,
                Screen.dpi,
                cornerMinimumSizeInches,
                cornerFallbackRatio,
                cornerMaximumRatio
            );
        }

        private void LogTouchConfiguration()
        {
            Vector2 screenSize = new(Screen.width, Screen.height);
            float extent = ResolveCornerExtent(screenSize);
            QuickLog.Info<DebugCaller>(
                "Initialized touch targets for {0}x{1} at {2:0.#} DPI: {3:0.#} px per corner ({4:0.##} in minimum).",
                Screen.width,
                Screen.height,
                Screen.dpi,
                extent,
                cornerMinimumSizeInches
            );
        }

        private static void RequestDebugger()
        {
            RaiseDebuggerRequested();

            IDebugService debugService = EnsureDebuggerInitialized();
            if (debugService == null)
            {
                return;
            }

            try
            {
                debugService.ShowDebugPanel(false);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static IDebugService EnsureDebuggerInitialized()
        {
            try
            {
                if (!SRDebug.IsInitialized)
                {
                    SRDebug.Init();
                }

                IDebugService debugService = SRDebug.Instance;
                if (debugService == null)
                {
                    Debug.LogError("[DebugCaller] SRDebugger service is unavailable.");
                    return null;
                }

                if (_options == null)
                {
                    _options = new DebugCallerOptions();
                    debugService.AddOptionContainer(_options);
                }

                return debugService;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }
        }

        private static void RaiseDebuggerRequested()
        {
            Action handlers = DebuggerRequested;
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler).Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        #endregion
    }
}
