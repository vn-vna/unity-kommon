using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public sealed class PooledPayloadView : IConveyorPayloadView
    {
        private readonly GameObject _root;
        private readonly GameObject[] _instances;
        private readonly uint[] _generations;
        private readonly int[] _profileIds;
        private readonly bool[] _seen;
        private readonly Pool[] _pools;
        private bool _disposed;

        public PooledPayloadView(ConveyorViewContext context, ConveyorVisualProfile[] profiles)
        {
            if (context.Capacity < 1 || profiles == null || profiles.Length == 0) throw new ArgumentException("Capacity and visual profiles are required.");
            _instances = new GameObject[context.Capacity];
            _generations = new uint[context.Capacity];
            _profileIds = new int[context.Capacity];
            _seen = new bool[context.Capacity];
            _pools = new Pool[profiles.Length];
            for (int index = 0; index < profiles.Length; index++)
            {
                ConveyorVisualProfile profile = profiles[index];
                if (profile.prefab == null || !ConveyorValidation.IsFinite(profile.scale)
                    || profile.scale.x <= 0 || profile.scale.y <= 0 || profile.scale.z <= 0)
                    throw new ArgumentException("Pooled profiles require a prefab and positive scale.");
                if (profile.prefab.GetComponentInChildren<Rigidbody>(true) != null)
                    throw new ArgumentException("Payload views cannot own authoritative rigidbodies; use a transport backend.");
                for (int other = 0; other < index; other++)
                {
                    if (profiles[other].id == profile.id) throw new ArgumentException("Visual profile IDs must be unique.");
                }
                _pools[index] = new Pool { Profile = profile };
            }
            _root = new GameObject("[Conveyor pooled payloads]");
            _root.transform.SetParent(context.Parent, false);
        }

        public void Render(in ConveyorTransportFrame frame)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PooledPayloadView));
            Array.Clear(_seen, 0, _seen.Length);
            for (int index = 0; index < frame.Count; index++) RenderItem(frame[index]);
            for (int slot = 0; slot < _instances.Length; slot++)
            {
                if (_instances[slot] != null && !_seen[slot]) Release(slot);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ConveyorObjectLifetime.DestroyOwned(_root);
        }

        private void RenderItem(ConveyorFrameItem item)
        {
            int slot = item.Handle.Slot;
            if ((uint)slot >= (uint)_instances.Length) throw new ArgumentException("Frame capacity exceeds view capacity.");
            Pool pool = Find(item.Payload.ProfileId);
            if (_instances[slot] != null && (_generations[slot] != item.Handle.Generation || _profileIds[slot] != item.Payload.ProfileId)) Release(slot);
            if (_instances[slot] == null)
            {
                _instances[slot] = pool.Free.Count == 0 ? UnityEngine.Object.Instantiate(pool.Profile.prefab, _root.transform, false) : pool.Free.Pop();
                _generations[slot] = item.Handle.Generation;
                _profileIds[slot] = item.Payload.ProfileId;
                _instances[slot].SetActive(true);
            }
            Transform instance = _instances[slot].transform;
            instance.SetPositionAndRotation(item.Pose.position + item.Pose.rotation * Vector3.up * pool.Profile.heightOffset, item.Pose.rotation);
            Vector3 parentScale = _root.transform.lossyScale;
            if (Math.Abs(parentScale.x * parentScale.y * parentScale.z) < 0.000001f) throw new ArgumentException("View parent scale cannot collapse.");
            instance.localScale = new Vector3(pool.Profile.scale.x / parentScale.x, pool.Profile.scale.y / parentScale.y, pool.Profile.scale.z / parentScale.z);
            _seen[slot] = true;
        }

        private void Release(int slot)
        {
            GameObject instance = _instances[slot];
            instance.SetActive(false);
            Find(_profileIds[slot]).Free.Push(instance);
            _instances[slot] = null;
        }

        private Pool Find(int id)
        {
            for (int index = 0; index < _pools.Length; index++) if (_pools[index].Profile.id == id) return _pools[index];
            throw new ArgumentException("No pooled visual profile for " + id + ".");
        }

        private sealed class Pool
        {
            internal ConveyorVisualProfile Profile;
            internal readonly Stack<GameObject> Free = new Stack<GameObject>();
        }
    }
}
