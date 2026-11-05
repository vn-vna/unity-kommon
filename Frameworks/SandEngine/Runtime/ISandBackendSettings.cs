using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    /// <summary>
    /// Backend selection inputs, read only when the factory creates a backend.
    /// Later changes do not replace an existing backend. Grid dimensions belong
    /// to the caller and are passed explicitly to the factory.
    /// </summary>
    public interface ISandBackendSettings
    {
        SandBackendType BackendType { get; }
#if !PLA_CREATIVE_BUILD
        ComputeShader GpuComputeShader { get; }
#endif
        bool AllowBackendFallback { get; }
    }
}
