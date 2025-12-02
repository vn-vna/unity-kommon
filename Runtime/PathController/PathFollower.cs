using System;
using System.Collections.Generic;

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.PathController
{
    public class PathFollower : MonoBehaviour
    {
        #region Public Fields
        public PathFollowingGroup controller;
        public float currentAnchor;
        public Vector3 followingPosition;
        public Vector3 facingDirection;
        public LinkedListNode<PathFollower> followerNode;
        public bool enableAutoFollowing = true;
        #endregion

        #region Events
        public event Action<PathFollower> OnFollowerDetached;
        #endregion

        #region Unity Events

        private void FixedUpdate()
        {
            if (enableAutoFollowing)
            {
                UpdateFollowerManually();
            }
        }

        #endregion

        #region Methods
        public void UpdateFollowerManually()
        {
            transform.position = Vector3.Lerp(
                transform.position,
                followingPosition,
                Time.fixedDeltaTime * controller.lerpSpeed
            );
        }

        public void GoToFollowingPositionImmediately()
        {
            transform.position = followingPosition;
        }

        public void Detach(Transform parent = null)
        {
            if (parent != null)
            {
                transform.SetParent(parent);
            }

            controller.RemoveFollower(this);
            OnFollowerDetached?.Invoke(this);
            OnFollowerDetached = null;
        }

        public void DetachAndDestroy(Transform parent = null)
        {
            Detach(parent);
            Destroy(gameObject);
        }

        public void DetachAndDeactivate(Transform parent = null)
        {
            Detach(parent);
            gameObject.SetActive(false);
        }

        #endregion
    }
}