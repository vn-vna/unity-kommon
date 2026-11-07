using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Modules/Teleport")]
    public sealed class TeleportModuleConfiguration : ScriptableObject, IConveyorModuleFactory
    {
        #region Serialized Fields
        [SerializeField] private string destinationId = "conveyor-b";
        [SerializeField, Range(0, 0.9999f)] private float entrance = 0.2f;
        [SerializeField, Range(0, 0.9999f)] private float exit = 0.65f;
        [SerializeField, Tooltip("Optional IConveyorTransferPolicy asset. Empty means Blocking.")]
        private ScriptableObject overflowPolicy;
        [SerializeField, Tooltip("Empty preserves lane index; otherwise one destination lane per source lane.")]
        private int[] laneMap = Array.Empty<int>();
        #endregion

        #region Public Methods
        public IConveyorModule Create()
        {
            IConveyorTransferPolicy policy = BlockingTransferPolicy.Instance;
            if (overflowPolicy != null)
                policy = overflowPolicy as IConveyorTransferPolicy ?? throw new ArgumentException("Overflow asset must implement IConveyorTransferPolicy.");
            return new TeleportModuleFactory(destinationId, entrance, exit, policy, laneMap == null || laneMap.Length == 0 ? null : laneMap).Create();
        }

        public void Configure(string targetId, float entryPosition, float exitPosition, ScriptableObject policy = null, int[] mapping = null)
        {
            destinationId = targetId;
            entrance = entryPosition;
            exit = exitPosition;
            overflowPolicy = policy;
            laneMap = mapping == null ? Array.Empty<int>() : (int[])mapping.Clone();
        }
        #endregion
    }
}
