using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [Serializable]
    public struct ConveyorPaletteEntry
    {
        public int profileId;
        public Color color;
        public ConveyorPaletteEntry(int profileId, Color color) { this.profileId = profileId; this.color = color; }
    }

    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Views/Palette Grains")]
    public sealed class PalettePayloadViewConfiguration : ScriptableObject, IConveyorPayloadViewProvider
    {
        #region Serialized Fields
        [SerializeField] private Mesh mesh;
        [SerializeField] private Material material;
        [SerializeField] private ConveyorPaletteEntry[] palette = Array.Empty<ConveyorPaletteEntry>();
        [SerializeField] private Vector3 scale = Vector3.one * 0.1f;
        [SerializeField] private float heightOffset = 0.07f;
        [SerializeField, Min(0)] private float positionJitter = 0.01f;
        [SerializeField, Range(0, 45)] private float rotationJitter = 15;
        [SerializeField, Range(0, 0.75f)] private float sizeVariation = 0.1f;
        [SerializeField, Min(0)] private float heightJitter;
        #endregion

        #region Public Methods
        public IConveyorPayloadView Create(in ConveyorViewContext context)
            => new PaletteInstancedPayloadView(mesh, material, palette, scale, heightOffset, positionJitter, rotationJitter,
                context.Parent == null ? 0 : context.Parent.gameObject.layer, sizeVariation, heightJitter);

        public void Configure(Mesh grainMesh, Material grainMaterial, ConveyorPaletteEntry[] colors, Vector3 grainScale,
            float height = 0.07f, float jitter = 0.01f, float rotation = 15, float variation = 0.1f, float verticalJitter = 0)
        {
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            mesh = grainMesh;
            material = grainMaterial;
            palette = (ConveyorPaletteEntry[])colors.Clone();
            scale = grainScale;
            heightOffset = height;
            positionJitter = jitter;
            rotationJitter = rotation;
            sizeVariation = variation;
            heightJitter = verticalJitter;
        }
        #endregion
    }
}
