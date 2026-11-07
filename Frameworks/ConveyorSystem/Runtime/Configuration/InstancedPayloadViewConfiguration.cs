using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Views/Instanced Payloads")]
    public sealed class InstancedPayloadViewConfiguration : ScriptableObject, IConveyorPayloadViewProvider
    {
        #region Serialized Fields
        [SerializeField] private ConveyorVisualProfile[] profiles = Array.Empty<ConveyorVisualProfile>();
        [SerializeField] private string colorProperty = "_BaseColor";
        #endregion

        #region Public Methods
        public IConveyorPayloadView Create(in ConveyorViewContext context)
            => new InstancedPayloadView((ConveyorVisualProfile[])profiles.Clone(), context.Parent == null ? 0 : context.Parent.gameObject.layer, colorProperty);

        public void Configure(ConveyorVisualProfile[] values, string tintProperty = "_BaseColor")
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            profiles = (ConveyorVisualProfile[])values.Clone();
            colorProperty = tintProperty;
        }
        #endregion
    }
}
