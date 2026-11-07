using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Policies/Vaporizing")]
    public sealed class VaporizingConveyorPolicy : ScriptableObject, IConveyorTransferPolicy
    {
        public ConveyorOverflowDisposition EvaluateFullDestination(in ConveyorTransferContext context) => ConveyorOverflowDisposition.Vaporize;
    }
}
