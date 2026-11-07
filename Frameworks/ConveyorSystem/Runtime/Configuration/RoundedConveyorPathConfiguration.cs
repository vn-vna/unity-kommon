using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Providers/Rounded Path")]
    public sealed class RoundedConveyorPathConfiguration : ScriptableObject, IConveyorPathProvider
    {
        #region Serialized Fields
        [SerializeField, Min(0)] private float radius = 0.5f;
        [SerializeField, Range(1, 64)] private int cornerSteps = 8;
        #endregion

        #region Public Methods
        public ConveyorPath Bake(ConveyorDefinitionSnapshot definition, Matrix4x4 localToWorld)
            => new RoundedPolylinePathProvider(radius, cornerSteps).Bake(definition, localToWorld);
        #endregion
    }
}
