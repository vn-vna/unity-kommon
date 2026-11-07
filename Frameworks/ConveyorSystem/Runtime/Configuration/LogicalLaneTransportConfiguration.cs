using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Providers/Logical Transport")]
    public sealed class LogicalLaneTransportConfiguration : ScriptableObject, IConveyorTransportProvider
    {
        #region Serialized Fields
        [SerializeField, Min(0)] private float speed = 1.5f;
        [SerializeField, Min(0.001f)] private float slotSpacing = 0.5f;
        [SerializeField] private LogicalPayloadProfile[] profiles = { new LogicalPayloadProfile(0, 1) };
        [SerializeField, Tooltip("Single-cell indexed occupancy for dense swarms. Snaps admission to the nearest moving cell; multi-slot profiles reject.")]
        private bool indexedAdmission;
        #endregion

        #region Public Methods
        public IConveyorTransportBackend Create(in ConveyorTransportContext context)
            => new LogicalLaneTransportProvider(speed, slotSpacing, profiles, indexedAdmission).Create(context);

        public void Configure(float movementSpeed, float spacing, LogicalPayloadProfile[] payloadProfiles = null, bool useIndexedAdmission = false)
        {
            var validation = new LogicalLaneTransportProvider(movementSpeed, spacing, payloadProfiles);
            speed = movementSpeed;
            slotSpacing = spacing;
            profiles = payloadProfiles == null ? new[] { new LogicalPayloadProfile(0, 1) } : (LogicalPayloadProfile[])payloadProfiles.Clone();
            indexedAdmission = useIndexedAdmission;
        }
        #endregion
    }
}
