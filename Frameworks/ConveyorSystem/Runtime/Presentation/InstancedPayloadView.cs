using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public sealed class InstancedPayloadView : IConveyorPayloadView
    {
        #region Constants
        // 511 also accommodates shaders carrying both objectToWorld and worldToObject.
        private const int BatchCapacity = 511;
        #endregion

        #region Private Fields
        private readonly Batch[] _batches;
        private readonly int _layer;
        private bool _disposed;
        #endregion

        #region Construction
        public InstancedPayloadView(ConveyorVisualProfile[] profiles, int layer = 0, string colorProperty = "_BaseColor")
        {
            if (!SystemInfo.supportsInstancing) throw new NotSupportedException("GPU instancing is unavailable. Select a pooled/custom view provider.");
            if (profiles == null || profiles.Length == 0) throw new ArgumentException("Assign at least one visual profile.");
            _layer = layer;
            _batches = new Batch[profiles.Length];
            try
            {
                for (int index = 0; index < profiles.Length; index++)
                {
                    ConveyorVisualProfile profile = profiles[index];
                    ValidateProfile(profile);
                    for (int other = 0; other < index; other++)
                    {
                        if (profiles[other].id == profile.id) throw new ArgumentException("Visual profile IDs must be unique.");
                    }
                    var material = new Material(profile.material) { enableInstancing = true };
                    _batches[index] = new Batch { Profile = profile, Material = material, Matrices = new Matrix4x4[BatchCapacity] };
                    if (!string.IsNullOrEmpty(colorProperty))
                    {
                        if (!material.HasProperty(colorProperty)) throw new ArgumentException("Material has no configured tint property: " + colorProperty);
                        material.SetColor(colorProperty, profile.tint);
                    }
                }
            }
            catch { Dispose(); throw; }
        }
        #endregion

        #region Public Methods
        public void Render(in ConveyorTransportFrame frame)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InstancedPayloadView));
            for (int index = 0; index < _batches.Length; index++) _batches[index].Count = 0;
            for (int index = 0; index < frame.Count; index++)
            {
                ConveyorFrameItem item = frame[index];
                Batch batch = Find(item.Payload.ProfileId);
                Vector3 position = item.Pose.position + item.Pose.rotation * Vector3.up * batch.Profile.heightOffset;
                batch.Matrices[batch.Count++] = Matrix4x4.TRS(position, item.Pose.rotation, batch.Profile.scale);
                if (batch.Count == BatchCapacity) Flush(batch);
            }
            for (int index = 0; index < _batches.Length; index++) Flush(_batches[index]);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int index = 0; index < _batches.Length; index++)
            {
                if (_batches[index] != null) ConveyorObjectLifetime.DestroyOwned(_batches[index].Material);
            }
        }
        #endregion

        #region Private Methods
        private Batch Find(int id)
        {
            for (int index = 0; index < _batches.Length; index++) if (_batches[index].Profile.id == id) return _batches[index];
            throw new ArgumentException("No visual profile for payload profile " + id + ".");
        }

        private void Flush(Batch batch)
        {
            if (batch.Count == 0) return;
            var parameters = new RenderParams(batch.Material)
            {
                layer = _layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false
            };
            Graphics.RenderMeshInstanced(parameters, batch.Profile.mesh, 0, batch.Matrices, batch.Count);
            batch.Count = 0;
        }

        internal static void ValidateProfile(ConveyorVisualProfile profile)
        {
            if (profile.mesh == null || profile.material == null) throw new ArgumentException("Assign mesh and material for each instanced profile.");
            if (!ConveyorValidation.IsFinite(profile.scale) || profile.scale.x <= 0 || profile.scale.y <= 0 || profile.scale.z <= 0)
                throw new ArgumentException("Visual scale must be finite and positive.");
        }
        #endregion

        #region Nested Types
        private sealed class Batch
        {
            internal ConveyorVisualProfile Profile;
            internal Material Material;
            internal Matrix4x4[] Matrices;
            internal int Count;
        }
        #endregion
    }
}
