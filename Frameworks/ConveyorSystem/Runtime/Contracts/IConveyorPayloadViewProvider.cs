using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public readonly struct ConveyorViewContext
    {
        public Transform Parent { get; }
        public int Capacity { get; }
        public ConveyorViewContext(Transform parent, int capacity) { Parent = parent; Capacity = capacity; }
    }

    public interface IConveyorPayloadViewProvider
    {
        IConveyorPayloadView Create(in ConveyorViewContext context);
    }

    /// <summary>Frame is borrowed for this call. A view owns visuals only, never transport bodies or cargo.</summary>
    public interface IConveyorPayloadView : IDisposable
    {
        void Render(in ConveyorTransportFrame frame);
    }

    [Serializable]
    public struct ConveyorVisualProfile
    {
        public int id;
        public Mesh mesh;
        public Material material;
        public GameObject prefab;
        public Vector3 scale;
        public Color tint;
        public float heightOffset;
    }
}
