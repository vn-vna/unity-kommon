using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    /// <summary>One shared mesh/material, per-instance palette colors and deterministic visual-only variation.</summary>
    public sealed class PaletteInstancedPayloadView : IConveyorPayloadView
    {
        private const int BatchCapacity = 511;
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private readonly Matrix4x4[] _matrices = new Matrix4x4[BatchCapacity];
        private readonly Vector4[] _colors = new Vector4[BatchCapacity];
        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();
        private readonly Dictionary<int, Vector4> _palette;
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly Vector3 _scale;
        private readonly float _height;
        private readonly float _positionJitter;
        private readonly float _rotationJitter;
        private readonly float _sizeVariation;
        private readonly float _heightJitter;
        private readonly int _layer;
        private bool _disposed;
        public int LastDrawCalls { get; private set; }
        public long StagingPayloadBytes => _matrices.LongLength * 64 + _colors.LongLength * 16;

        public PaletteInstancedPayloadView(Mesh mesh, Material material, IReadOnlyList<ConveyorPaletteEntry> palette,
            Vector3 scale, float height = 0.07f, float positionJitter = 0.01f, float rotationJitter = 15, int layer = 0,
            float sizeVariation = 0.1f, float heightJitter = 0)
        {
            if (!SystemInfo.supportsInstancing) throw new NotSupportedException("GPU instancing is unavailable; use another payload view.");
            if (mesh == null || material == null || !material.HasProperty(ColorId))
                throw new ArgumentException("Assign a grain mesh and an instancing shader with per-instance _BaseColor support.");
            if (palette == null || palette.Count == 0) throw new ArgumentException("A nonempty palette is required.");
            if (!ConveyorValidation.IsFinite(scale) || scale.x <= 0 || scale.y <= 0 || scale.z <= 0
                || !ConveyorValidation.IsFinite(height) || !ConveyorValidation.IsFinite(positionJitter) || positionJitter < 0
                || !ConveyorValidation.IsFinite(rotationJitter) || rotationJitter < 0 || rotationJitter > 45)
                throw new ArgumentException("Grain scale/variation settings must be finite and within their supported ranges.");
            if (!ConveyorValidation.IsFinite(sizeVariation) || sizeVariation < 0 || sizeVariation > 0.75f
                || !ConveyorValidation.IsFinite(heightJitter) || heightJitter < 0)
                throw new ArgumentException("Size variation must be 0–0.75 and height jitter nonnegative, both finite.");
            _palette = new Dictionary<int, Vector4>(palette.Count);
            for (int index = 0; index < palette.Count; index++)
            {
                ConveyorPaletteEntry entry = palette[index];
                if (_palette.ContainsKey(entry.profileId)) throw new ArgumentException("Palette profile IDs must be unique.");
                _palette.Add(entry.profileId, entry.color);
            }
            _mesh = mesh;
            _material = new Material(material) { enableInstancing = true };
            _scale = scale;
            _height = height;
            _positionJitter = positionJitter;
            _rotationJitter = rotationJitter;
            _sizeVariation = sizeVariation;
            _heightJitter = heightJitter;
            _layer = layer;
        }

        public void Render(in ConveyorTransportFrame frame)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PaletteInstancedPayloadView));
            LastDrawCalls = 0;
            int count = 0;
            for (int index = 0; index < frame.Count; index++)
            {
                ConveyorFrameItem item = frame[index];
                if (!_palette.TryGetValue(item.Payload.ProfileId, out Vector4 color))
                    throw new ArgumentException("No palette entry for payload profile " + item.Payload.ProfileId + ".");
                uint seed = Hash(unchecked((uint)item.Payload.ConsumerKey * 747796405u ^ (uint)item.Handle.Slot * 2891336453u) ^ item.Handle.Generation);
                float x = Unit(seed) * 2 - 1;
                float z = Unit(Hash(seed)) * 2 - 1;
                float angle = Unit(Hash(seed + 1)) * 2 - 1;
                Vector3 position = item.Pose.position + item.Pose.rotation * new Vector3(x * _positionJitter,
                    _height + Unit(Hash(seed + 3)) * _heightJitter, z * _positionJitter);
                Quaternion rotation = item.Pose.rotation * Quaternion.Euler(x * _rotationJitter, angle * _rotationJitter, z * _rotationJitter);
                _matrices[count] = Matrix4x4.TRS(position, rotation,
                    _scale * (1 - _sizeVariation + Unit(Hash(seed + 2)) * 2 * _sizeVariation));
                _colors[count++] = color;
                if (count == BatchCapacity) { Draw(count); count = 0; }
            }
            if (count > 0) Draw(count);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ConveyorObjectLifetime.DestroyOwned(_material);
        }

        private void Draw(int count)
        {
            _properties.SetVectorArray(ColorId, _colors);
            var parameters = new RenderParams(_material)
            {
                layer = _layer, matProps = _properties,
                shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false
            };
            Graphics.RenderMeshInstanced(parameters, _mesh, 0, _matrices, count);
            LastDrawCalls++;
        }

        private static uint Hash(uint value)
        {
            unchecked
            {
                value ^= value >> 16; value *= 0x7feb352d;
                value ^= value >> 15; value *= 0x846ca68b;
                return value ^ (value >> 16);
            }
        }

        private static float Unit(uint value) => (value & 0xffffff) / 16777215f;
    }
}
