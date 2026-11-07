using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public interface IConveyorPathProvider
    {
        ConveyorPath Bake(ConveyorDefinitionSnapshot definition, Matrix4x4 localToWorld);
    }

    public readonly struct ConveyorGeometryContext
    {
        public ConveyorDefinitionSnapshot Definition { get; }
        public ConveyorPath Path { get; }
        public Transform Parent { get; }
        public float Width { get; }
        public int Channels { get; }

        public ConveyorGeometryContext(ConveyorDefinitionSnapshot definition, ConveyorPath path, Transform parent, float width, int channels)
        {
            Definition = definition;
            Path = path;
            Parent = parent;
            Width = width;
            Channels = channels;
        }
    }

    public interface IConveyorGeometryProvider
    {
        ConveyorGeometry Build(in ConveyorGeometryContext context);
    }

    public sealed class ConveyorGeometry : IDisposable
    {
        public GameObject Root { get; private set; }
        private readonly Mesh _ownedMesh;

        public ConveyorGeometry(GameObject ownedRoot, Mesh ownedMesh = null)
        {
            Root = ownedRoot;
            _ownedMesh = ownedMesh;
        }

        public void Dispose()
        {
            ConveyorObjectLifetime.DestroyOwned(Root);
            ConveyorObjectLifetime.DestroyOwned(_ownedMesh);
            Root = null;
        }
    }

    internal static class ConveyorObjectLifetime
    {
        internal static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
