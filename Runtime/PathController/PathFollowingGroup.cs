using System.Collections.Generic;

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.PathController
{
    public class PathFollowingGroup : MonoBehaviour
    {
        private IEnumerable<PathFollower> PathFollowers => pathFollowers;
        private LinkedList<PathFollower> pathFollowers;
        public PathController pathController;
        public uint pathResolution;
        public float gap;
        public float lerpSpeed;
        public float anchor;
        private Vector3[] pathPositions;
        private float absoluteAnchoredLength;
        public float PathLength { get; private set; }
        public float FollowerLineLength => pathFollowers == null ? 0 : pathFollowers.Count * gap;

        public IEnumerable<PathFollower> Followers => pathFollowers;

        private void Awake()
        {
            pathFollowers = new LinkedList<PathFollower>();
        }

        private void Start()
        {
        }

        private void FixedUpdate()
        {
            ReCalculatePathPositions();
            RecalculateFollowerPositions();
        }

        private void OnDrawGizmosSelected()
        {
            ReCalculatePathPositions();
            Gizmos.color = Color.yellow;

            if (pathFollowers == null) return;

            for (var i = 0; i < pathFollowers.Count; i++)
            {
                GetAnchoredPosition(absoluteAnchoredLength - i * gap, out var anchoredPosition, out var facingDirection);
                Gizmos.DrawCube(anchoredPosition, Vector3.one * 0.1f);
                Gizmos.DrawLine(anchoredPosition, anchoredPosition + facingDirection.normalized * 0.5f);
            }
        }

        public void ReCalculatePathPositions()
        {
            if (pathController == null || pathController.waypoints.Count < 2) return;

            pathPositions = new Vector3[(pathController.waypoints.Count - 1) * pathResolution];
            pathController.GetPathPositionsNonAllocate((int)pathResolution, pathPositions);

            PathLength = 0;

            if (pathPositions.Length == 0) return;

            var lastPosition = pathPositions[0];
            for (var i = 1; i < pathPositions.Length; i++)
            {
                PathLength += (pathPositions[i] - lastPosition).magnitude;
                lastPosition = pathPositions[i];
            }

            absoluteAnchoredLength = PathLength * anchor;
        }

        public void RecalculateFollowerPositions()
        {
            if (pathFollowers == null) return;
            var index = 0;

            foreach (var follower in pathFollowers)
            {
                if (follower == null || !follower.isActiveAndEnabled) continue;
                RecalculateFollowerPosition(follower, index);
                index++;
            }
        }

        public void RecalculateFollowerPosition(PathFollower follower, int index, bool noLerp = false)
        {
            var newAnchor = absoluteAnchoredLength - index * gap;
            var followerAnchor = noLerp 
                ?  newAnchor 
                : follower.currentAnchor + (newAnchor - follower.currentAnchor) * lerpSpeed * Time.fixedDeltaTime;
            follower.currentAnchor = followerAnchor;
            GetAnchoredPosition(followerAnchor, out var followingPosition, out var facingDirection);
            follower.followingPosition = followingPosition;
            follower.facingDirection = facingDirection;
        }

        public void RecalculateLastFollowerPosition()
        {
            var lastIndex = pathFollowers.Count - 1;
            if (lastIndex < 0) return;
            RecalculateFollowerPosition(pathFollowers.Last.Value, lastIndex, true);
        }

        private void GetAnchoredPosition(float anchoredLength, out Vector3 followingPosition, out Vector3 facingDirection)
        {
            PathController.GetAnchoredPosition(
                pathPositions, anchoredLength, PathLength,
                out followingPosition, out facingDirection
            );
        }

        public void ClearFollowers()
        {
            foreach (var follower in pathFollowers)
            {
                if (follower == null) continue;
                if (follower.transform.parent == transform) follower.transform.parent = null;
                Destroy(follower.gameObject);
            }

            pathFollowers = new LinkedList<PathFollower>();
        }

        public LinkedListNode<PathFollower> AddFollower<T>(T target) where T : MonoBehaviour
        {
            var follower = target.GetComponent<PathFollower>();
            follower = follower != null ? follower : target.gameObject.AddComponent<PathFollower>();
            follower.transform.parent = transform;
            follower.controller = this;
            follower.followerNode = pathFollowers.AddLast(follower);
            return follower.followerNode;
        }

        public void RemoveFollower(PathFollower pathFollower)
        {
            if (pathFollower == null) return;
            if (pathFollower.followerNode.List != pathFollowers)
            {
                Debug.LogError($"PathFollower {pathFollower.name} is not managed by PathFollowingGroup {name}");
                return;
            }

            if (pathFollower.transform.parent == transform) pathFollower.transform.parent = null;
            pathFollowers.Remove(pathFollower.followerNode);
            // Destroy(pathFollower.gameObject);
        }

        public void TeleportToEntrance()
        {
            if (pathFollowers == null) return;
            anchor = 0.0f;

            foreach (var follower in pathFollowers)
            {
                if (follower == null) continue;
                follower.transform.position = pathController.waypoints[0].point.position;
            }
        }

    }
}