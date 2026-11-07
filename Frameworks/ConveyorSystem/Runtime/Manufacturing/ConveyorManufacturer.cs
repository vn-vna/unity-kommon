using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public sealed class ConveyorBuildResult : IDisposable
    {
        public ConveyorPath Path { get; }
        public ConveyorGeometry Geometry { get; }
        public bool IsDisposed { get; private set; }

        internal ConveyorBuildResult(ConveyorPath path, ConveyorGeometry geometry) { Path = path; Geometry = geometry; }
        public void Activate() { if (IsDisposed) throw new ObjectDisposedException(nameof(ConveyorBuildResult)); if (Geometry?.Root != null) Geometry.Root.SetActive(true); }
        public void Dispose() { if (IsDisposed) return; IsDisposed = true; Geometry?.Dispose(); }
    }

    public static class ConveyorManufacturer
    {
        public static ConveyorBuildResult Build(ConveyorDefinitionSnapshot definition, IConveyorPathProvider pathProvider,
            IConveyorGeometryProvider geometryProvider, Transform parent = null, float width = 1, int channels = 1)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (pathProvider == null) throw new ArgumentNullException(nameof(pathProvider));
            ConveyorValidation.Positive(width, nameof(width));
            if (channels < 1 || channels > 64) throw new ArgumentOutOfRangeException(nameof(channels));
            Matrix4x4 matrix = parent == null ? Matrix4x4.identity : parent.localToWorldMatrix;
            ConveyorPath path = pathProvider.Bake(definition, matrix) ?? throw new ArgumentException("Path provider returned null.");
            ConveyorGeometry geometry = null;
            try
            {
                var context = new ConveyorGeometryContext(definition, path, parent, width, channels);
                if (geometryProvider != null)
                {
                    geometry = geometryProvider.Build(context) ?? throw new ArgumentException("Geometry provider returned null.");
                    if (geometry.Root == null) throw new ArgumentException("Geometry provider returned no owned root.");
                    geometry.Root.SetActive(false);
                }
                return new ConveyorBuildResult(path, geometry);
            }
            catch { geometry?.Dispose(); throw; }
        }
    }
}
