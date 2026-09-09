using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// An animated world-space presentation offset. Position remains the gameplay
    /// anchor; the transform and all of its visual children follow the displayed pose.
    /// Owned and ticked by the entity, with no coroutine or global tween lifetime.
    /// </summary>
    public sealed class GridReleasePresentation
    {
        private readonly Transform _target;
        private Vector3 _offset;
        private Vector3 _startOffset;
        private Vector3 _gameplayPosition;
        private float _duration;
        private float _elapsed;

        public bool IsSettling => _duration > 0f;
        public Vector3 Position
        {
            get => IsSettling ? _gameplayPosition : _target.position;
            set
            {
                _gameplayPosition = value;
                _target.position = value + _offset;
            }
        }

        public GridReleasePresentation(Transform target) => _target = target;

        public void Begin(Vector3 previousPosition, float duration)
        {
            Complete();
            if (!(duration > 0f) || float.IsInfinity(duration)) return;
            _startOffset = previousPosition - _target.position;
            if (_startOffset.sqrMagnitude <= 0.00000001f) return;
            _gameplayPosition = _target.position;
            _duration = duration;
            _elapsed = 0f;
            SetOffset(_startOffset);
        }

        public void Tick(float deltaTime)
        {
            if (!IsSettling) return;
            _elapsed += Mathf.Max(0f, deltaTime);
            float remaining = 1f - Mathf.Clamp01(_elapsed / _duration);
            SetOffset(_startOffset * (remaining * remaining * remaining));
            if (_elapsed >= _duration) Complete();
        }

        /// <summary>Re-grab or disposal takes over the displayed pose without a jump.</summary>
        public void TakeOverDisplayedPosition()
        {
            _duration = 0f;
            _offset = Vector3.zero;
        }

        public void Complete()
        {
            SetOffset(Vector3.zero);
            _duration = 0f;
        }

        private void SetOffset(Vector3 offset)
        {
            Vector3 position = Position;
            _offset = offset;
            _target.position = position + offset;
        }
    }
}
