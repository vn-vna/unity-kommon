using System;
using Com.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    public enum SandBackendType
    {
        Auto = 0,
        Jobs = 1,
        Gpu = 2,
        SingleThread = 3
    }

    public sealed class SandBackendFactory
    {
        private SandBackendFactory()
        {
        }

        public static ISandBackend Create(
            ISandBackendSettings simulator,
            int width,
            int height
        )
        {
#if PLA_CREATIVE_BUILD
            return new Pla.SandPlaBackend(width, height);
#else
            bool hasSettings = SandSettingsLifetime.IsAvailable(simulator);
            SandBackendType requestedType = hasSettings
                ? simulator.BackendType
                : SandBackendType.Auto;
            ComputeShader shader = hasSettings
                ? simulator.GpuComputeShader
                : null;
            bool allowFallback = !hasSettings || simulator.AllowBackendFallback;

            if (IsWebPlayer())
            {
                LogForcedWebFallback(requestedType);
                return new SingleThreadBackend(width, height);
            }

            if (requestedType == SandBackendType.SingleThread)
            {
                return new SingleThreadBackend(width, height);
            }

            if (requestedType == SandBackendType.Jobs)
            {
                return new JobBackend(width, height);
            }

            if (requestedType == SandBackendType.Gpu)
            {
                return CreateGpuOrFallback(
                    shader,
                    width,
                    height,
                    allowFallback
                );
            }

            return CreateAutoBackend(shader, width, height);
#endif
        }

#if !PLA_CREATIVE_BUILD
        private static ISandBackend CreateAutoBackend(
            ComputeShader shader,
            int width,
            int height
        )
        {
            if (!GpuBackend.IsSupported(shader))
            {
                return new JobBackend(width, height);
            }

            try
            {
                return new GpuBackend(width, height, shader);
            }
            catch (Exception exception)
            {
                QuickLog.Warning<SandBackendFactory>(
                    "GPU sand backend initialization failed; using Jobs. {0}",
                    exception.Message
                );
                return new JobBackend(width, height);
            }
        }

        private static ISandBackend CreateGpuOrFallback(
            ComputeShader shader,
            int width,
            int height,
            bool allowFallback
        )
        {
            if (GpuBackend.IsSupported(shader))
            {
                try
                {
                    return new GpuBackend(width, height, shader);
                }
                catch (Exception exception) when (allowFallback)
                {
                    QuickLog.Warning<SandBackendFactory>(
                        "GPU sand backend initialization failed; using Jobs. {0}",
                        exception.Message
                    );
                    return new JobBackend(width, height);
                }
            }

            if (!allowFallback)
            {
                throw new PlatformNotSupportedException(
                    "GPU sand simulation was requested, but its compute shader "
                    + "or required platform features are unavailable."
                );
            }

            QuickLog.Warning<SandBackendFactory>(
                "GPU sand backend is unavailable; using the Jobs backend."
            );
            return new JobBackend(width, height);
        }

#endif
        private static void LogForcedWebFallback(SandBackendType requestedType)
        {
            if (requestedType == SandBackendType.Auto
                || requestedType == SandBackendType.SingleThread)
            {
                return;
            }

            QuickLog.Warning<SandBackendFactory>(
                "Sand backend {0} is unavailable in Web builds; using SingleThread.",
                requestedType
            );
        }

        private static bool IsWebPlayer()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }
}
