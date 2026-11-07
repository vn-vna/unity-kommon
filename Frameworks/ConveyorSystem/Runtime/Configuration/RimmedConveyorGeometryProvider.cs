using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Providers/Rimmed Belt Geometry")]
    public sealed class RimmedConveyorGeometryProvider : ScriptableObject, IConveyorGeometryProvider
    {
        #region Serialized Fields
        [SerializeField] private Material beltMaterial;
        [SerializeField] private Material rimMaterial;
        [SerializeField, Min(0.001f)] private float depth = 0.16f;
        [SerializeField, Min(0.001f)] private float rimRadius = 0.10f;
        [SerializeField] private float rimHeight = 0.12f;
        #endregion

        #region Public Methods
        public void Configure(Material belt, Material rim, float radius = 0.10f, float height = 0.12f)
        {
            ConveyorValidation.Positive(radius, nameof(radius));
            if (!ConveyorValidation.IsFinite(height)) throw new ArgumentOutOfRangeException(nameof(height));
            beltMaterial = belt;
            rimMaterial = rim;
            rimRadius = radius;
            rimHeight = height;
        }

        public ConveyorGeometry Build(in ConveyorGeometryContext context)
        {
            if (beltMaterial == null || rimMaterial == null) throw new ArgumentException("Assign belt and rim materials.");
            var root = new GameObject("[Conveyor rimmed geometry]");
            root.SetActive(false);
            root.transform.SetParent(context.Parent, false);
            Mesh deck = null, outer = null, inner = null, combined = null;
            try
            {
                Matrix4x4 matrix = root.transform.worldToLocalMatrix;
                deck = ConveyorMeshBuilder.Build(context.Path, context.Width, depth, matrix);
                outer = ConveyorRimMeshBuilder.Build(context.Path, context.Width * 0.5f, rimHeight, rimRadius, matrix);
                inner = ConveyorRimMeshBuilder.Build(context.Path, -context.Width * 0.5f, rimHeight, rimRadius, matrix);
                combined = new Mesh { name = "Conveyor deck and rounded rims", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                combined.CombineMeshes(new[] { new CombineInstance { mesh = deck }, new CombineInstance { mesh = outer },
                    new CombineInstance { mesh = inner } }, false, false);
                root.AddComponent<MeshFilter>().sharedMesh = combined;
                root.AddComponent<MeshRenderer>().sharedMaterials = new[] { beltMaterial, rimMaterial, rimMaterial };
                return new ConveyorGeometry(root, combined);
            }
            catch
            {
                ConveyorObjectLifetime.DestroyOwned(root);
                ConveyorObjectLifetime.DestroyOwned(combined);
                throw;
            }
            finally
            {
                ConveyorObjectLifetime.DestroyOwned(deck);
                ConveyorObjectLifetime.DestroyOwned(outer);
                ConveyorObjectLifetime.DestroyOwned(inner);
            }
        }
        #endregion
    }
}
