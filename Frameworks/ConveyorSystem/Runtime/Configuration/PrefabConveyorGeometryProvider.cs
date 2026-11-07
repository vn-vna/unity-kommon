using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Providers/Prefab Segment Geometry")]
    public sealed class PrefabConveyorGeometryProvider : ScriptableObject, IConveyorGeometryProvider
    {
        public ConveyorGeometry Build(in ConveyorGeometryContext context)
        {
            if (context.Definition.Mode != ConveyorDefinitionMode.PrefabSegments)
                throw new ArgumentException("Prefab geometry requires an authored segment route; arbitrary curves are not approximated.");
            if (context.Parent != null && (context.Parent.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                throw new ArgumentException("Prefab manufacture requires unit world scale so connectors and declared width remain compatible.");
            ConveyorPathCompiler.CompileSegments(context.Definition);
            var root = new GameObject("[Conveyor prefab geometry]");
            root.SetActive(false);
            try
            {
                root.transform.SetParent(context.Parent, false);
                for (int index = 0; index < context.Definition.Segments.Count; index++)
                {
                    ConveyorSegmentPlacement placement = context.Definition.Segments[index];
                    if (placement.segment.Prefab == null || Math.Abs(placement.segment.Width - context.Width) > 0.001f)
                        throw new ArgumentException("Segment prefab is missing or its declared width does not match the conveyor profile.");
                    GameObject instance = Instantiate(placement.segment.Prefab, root.transform, false);
                    instance.transform.localPosition = placement.position;
                    instance.transform.localRotation = Quaternion.Euler(placement.eulerAngles);
                    instance.transform.localScale = Vector3.one;
                }
                return new ConveyorGeometry(root);
            }
            catch { ConveyorObjectLifetime.DestroyOwned(root); throw; }
        }
    }
}
