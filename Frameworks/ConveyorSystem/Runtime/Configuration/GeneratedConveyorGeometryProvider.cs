using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Providers/Generated Belt Geometry")]
    public sealed class GeneratedConveyorGeometryProvider : ScriptableObject, IConveyorGeometryProvider
    {
        #region Serialized Fields
        [SerializeField] private Material material;
        [SerializeField, Min(0.001f)] private float depth = 0.2f;
        #endregion

        #region Public Methods
        public ConveyorGeometry Build(in ConveyorGeometryContext context)
        {
            if (material == null) throw new ArgumentException("Assign a belt material to the geometry provider.");
            var root = new GameObject("[Conveyor geometry]");
            root.SetActive(false);
            Mesh mesh = null;
            try
            {
                root.transform.SetParent(context.Parent, false);
                mesh = ConveyorMeshBuilder.Build(context.Path, context.Width, depth, root.transform.worldToLocalMatrix);
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterial = material;
                return new ConveyorGeometry(root, mesh);
            }
            catch { ConveyorObjectLifetime.DestroyOwned(root); ConveyorObjectLifetime.DestroyOwned(mesh); throw; }
        }

        public void Configure(Material beltMaterial, float beltDepth = 0.2f)
        {
            ConveyorValidation.Positive(beltDepth, nameof(beltDepth));
            material = beltMaterial;
            depth = beltDepth;
        }
        #endregion
    }
}
