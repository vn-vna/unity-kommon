using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Views/Pooled Payloads")]
    public sealed class PooledPayloadViewConfiguration : ScriptableObject, IConveyorPayloadViewProvider
    {
        #region Serialized Fields
        [SerializeField] private ConveyorVisualProfile[] profiles = Array.Empty<ConveyorVisualProfile>();
        #endregion

        #region Public Methods
        public IConveyorPayloadView Create(in ConveyorViewContext context)
            => new PooledPayloadView(context, (ConveyorVisualProfile[])profiles.Clone());

        public void Configure(ConveyorVisualProfile[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            profiles = (ConveyorVisualProfile[])values.Clone();
        }
        #endregion
    }
}
