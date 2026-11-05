#if !PLA_CREATIVE_BUILD
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Com.Scheherazade.Common.DebugCaller
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMeshProUGUI))]
    [AddComponentMenu("Scheherazade/Debug/Version Debug Unlock")]
    public sealed class VersionDebugUnlock : MonoBehaviour
    {
        #region Private Fields

        private readonly VersionDebugUnlockGesture _gesture = new();
        private readonly List<RaycastResult> _raycasts = new();
        private TMP_Text _text;
        private EventSystem _eventSystem;
        private PointerEventData _pointer;
        private Color _originalColor;
        private bool _originalRaycastTarget;
        private bool _isRed;

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            _text = GetComponent<TMP_Text>();
            _originalColor = _text.color;
            _originalRaycastTarget = _text.raycastTarget;
            _text.raycastTarget = true;
            ResetGesture();
        }

        private void Update()
        {
            int count = Input.touchCount;
            VersionDebugTouch first = count > 0 ? ReadTouch(0) : default;
            VersionDebugTouch second = count > 1 ? ReadTouch(1) : default;
            // Wall-clock time is intentional: Settings pauses the game's simulation clock.
            bool succeeded = _gesture.UpdateTouches(count, first, second, Time.realtimeSinceStartup);
            RefreshColor();
            if (succeeded)
            {
                ResetGesture();
                DebugCaller.EnableAndShowDebugger();
            }
        }

        private void OnDisable()
        {
            ResetGesture();
            if (_text != null)
            {
                _text.raycastTarget = _originalRaycastTarget;
            }
            _pointer = null;
            _eventSystem = null;
            _raycasts.Clear();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ResetGesture();
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused) ResetGesture();
        }

        #endregion

        #region Private Methods

        private VersionDebugTouch ReadTouch(int index)
        {
            Touch touch = Input.GetTouch(index);
            return new VersionDebugTouch(touch.fingerId, touch.phase, IsOverVersion(touch.position));
        }

        private bool IsOverVersion(Vector2 position)
        {
            Canvas canvas = _text.canvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_text.rectTransform, position, camera)
                || EventSystem.current == null)
            {
                return false;
            }

            if (_eventSystem != EventSystem.current)
            {
                _eventSystem = EventSystem.current;
                _pointer = new PointerEventData(_eventSystem);
            }

            _pointer.Reset();
            _pointer.position = position;
            _raycasts.Clear();
            _eventSystem.RaycastAll(_pointer, _raycasts);
            // An overlapping modal or another UI control must break the secret sequence.
            return _raycasts.Count > 0 && _raycasts[0].gameObject == gameObject;
        }

        private void RefreshColor()
        {
            if (_text == null || _isRed == _gesture.IsArmed) return;
            if (_gesture.IsArmed) _originalColor = _text.color;
            _isRed = _gesture.IsArmed;
            _text.color = _isRed ? Color.red : _originalColor;
        }

        private void ResetGesture()
        {
            _gesture.Reset();
            RefreshColor();
        }

        #endregion
    }
}
#endif
