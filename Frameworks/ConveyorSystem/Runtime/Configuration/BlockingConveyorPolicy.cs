using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Policies/Blocking")]
    public sealed class BlockingConveyorPolicy : ScriptableObject, IConveyorTransferPolicy
    {
        public ConveyorOverflowDisposition EvaluateFullDestination(in ConveyorTransferContext context) => ConveyorOverflowDisposition.Block;
    }
}
