using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Configuration")]
    public sealed class ConveyorConfiguration : ScriptableObject
    {
        #region Properties
        public float Width => width;
        public int Channels => channels;
        public IConveyorPathProvider PathProvider => Required<IConveyorPathProvider>(pathProvider, nameof(pathProvider));
        public IConveyorTransportProvider TransportProvider => Required<IConveyorTransportProvider>(transportProvider, nameof(transportProvider));
        public IConveyorGeometryProvider GeometryProvider => Optional<IConveyorGeometryProvider>(geometryProvider, nameof(geometryProvider));
        public IConveyorPayloadViewProvider PayloadViewProvider => Optional<IConveyorPayloadViewProvider>(payloadViewProvider, nameof(payloadViewProvider));
        #endregion

        #region Serialized Fields
        [SerializeField, Min(0.01f)] private float width = 1.5f;
        [SerializeField, Range(1, 64)] private int channels = 2;
        [SerializeField, Tooltip("Required asset implementing IConveyorPathProvider.")] private ScriptableObject pathProvider;
        [SerializeField, Tooltip("Required asset implementing IConveyorTransportProvider.")] private ScriptableObject transportProvider;
        [SerializeField, Tooltip("Optional IConveyorGeometryProvider. Empty is headless geometry.")] private ScriptableObject geometryProvider;
        [SerializeField, Tooltip("Optional IConveyorPayloadViewProvider, including custom batch renderers.")] private ScriptableObject payloadViewProvider;
        [SerializeField] private ScriptableObject[] modules = Array.Empty<ScriptableObject>();
        #endregion

        #region Public Methods
        public void Validate()
        {
            ConveyorValidation.Positive(width, nameof(width));
            if (channels < 1 || channels > 64) throw new ArgumentOutOfRangeException(nameof(channels));
            _ = PathProvider; _ = TransportProvider; _ = GeometryProvider; _ = PayloadViewProvider;
            GetModuleFactories();
        }

        public IConveyorModuleFactory[] GetModuleFactories()
        {
            if (modules == null) return Array.Empty<IConveyorModuleFactory>();
            var result = new IConveyorModuleFactory[modules.Length];
            for (int index = 0; index < result.Length; index++)
                result[index] = Required<IConveyorModuleFactory>(modules[index], "modules[" + index + "]");
            return result;
        }

        public void Configure(ScriptableObject path, ScriptableObject transport, ScriptableObject geometry = null,
            ScriptableObject view = null, float beltWidth = 1.5f, int laneCount = 2, ScriptableObject[] attachedModules = null)
        {
            Required<IConveyorPathProvider>(path, nameof(path));
            Required<IConveyorTransportProvider>(transport, nameof(transport));
            Optional<IConveyorGeometryProvider>(geometry, nameof(geometry));
            Optional<IConveyorPayloadViewProvider>(view, nameof(view));
            ConveyorValidation.Positive(beltWidth, nameof(beltWidth));
            if (laneCount < 1 || laneCount > 64) throw new ArgumentOutOfRangeException(nameof(laneCount));
            if (attachedModules != null)
            {
                for (int index = 0; index < attachedModules.Length; index++) Required<IConveyorModuleFactory>(attachedModules[index], "module");
            }
            pathProvider = path;
            transportProvider = transport;
            geometryProvider = geometry;
            payloadViewProvider = view;
            width = beltWidth;
            channels = laneCount;
            modules = attachedModules == null ? Array.Empty<ScriptableObject>() : (ScriptableObject[])attachedModules.Clone();
        }
        #endregion

        #region Private Methods
        private static T Required<T>(ScriptableObject asset, string field) where T : class
        {
            if (asset == null) throw new ArgumentException("Assign " + field + " (" + typeof(T).Name + ").");
            return asset as T ?? throw new ArgumentException(field + " must implement " + typeof(T).Name + ".");
        }

        private static T Optional<T>(ScriptableObject asset, string field) where T : class
            => asset == null ? null : Required<T>(asset, field);
        #endregion
    }
}
