using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    /// <summary>How a configured key is matched to its conflict-resolution plan.</summary>
    public enum KeyMatchType
    {
        Exact,
        Wildcard,
        Regex
    }

    /// <summary>
    /// Platform-neutral snapshot of one adapter's data for a key, handed to a
    /// <see cref="ConflictResolutionPlan"/> for ranking. LastWriteTime is
    /// enriched by the director before ranking.
    /// </summary>
    public readonly struct ResolutionCandidate
    {
        public ISaveAdapter Adapter { get; }

        public string AdapterId => Adapter.AdapterId;

        public int GroupIndex { get; }

        public int AdapterIndex { get; }

        public byte[] Data { get; }

        public DateTime? LastWriteTime { get; }

        public ResolutionCandidate(
            ISaveAdapter adapter,
            int groupIndex,
            int adapterIndex,
            byte[] data,
            DateTime? lastWriteTime)
        {
            Adapter = adapter;
            GroupIndex = groupIndex;
            AdapterIndex = adapterIndex;
            Data = data;
            LastWriteTime = lastWriteTime;
        }

        public ResolutionCandidate WithLastWriteTime(DateTime? writeTime)
        {
            return new ResolutionCandidate(
                Adapter, GroupIndex, AdapterIndex, Data, writeTime
            );
        }
    }

    /// <summary>
    /// Base class for inter-adapter conflict resolution. Implementations rank
    /// the candidates (adapters that returned data for the key); the director
    /// decodes them in ranked order until one succeeds.
    /// Built-in plans: <see cref="PriorityOrderPlan"/>,
    /// <see cref="LastWriteCompletePlan"/>, <see cref="PreferredAdapterPlan"/>.
    /// </summary>
    public abstract class ConflictResolutionPlan : ScriptableObject
    {
        public abstract Task<IReadOnlyList<ResolutionCandidate>> RankCandidatesAsync(
            IReadOnlyList<ResolutionCandidate> candidates,
            CancellationToken ct);
    }

    /// <summary>First available adapter in the load order wins.</summary>
    [CreateAssetMenu(
        fileName = "PriorityOrderPlan",
        menuName = "Scheherazade/Data Sync/Conflict Plans/Priority Order Plan"
    )]
    public sealed class PriorityOrderPlan : ConflictResolutionPlan
    {
        public override Task<IReadOnlyList<ResolutionCandidate>> RankCandidatesAsync(
            IReadOnlyList<ResolutionCandidate> candidates,
            CancellationToken ct)
        {
            var ranked = new List<ResolutionCandidate>(candidates);
            ranked.Sort(CompareByPriority);
            return Task.FromResult<IReadOnlyList<ResolutionCandidate>>(ranked);
        }

        internal static int CompareByPriority(
            ResolutionCandidate a, ResolutionCandidate b)
        {
            int cmp = a.GroupIndex.CompareTo(b.GroupIndex);
            if (cmp != 0) return cmp;
            return a.AdapterIndex.CompareTo(b.AdapterIndex);
        }
    }

    /// <summary>Adapter with the newest last-write time wins; ties fall back to load order.</summary>
    [CreateAssetMenu(
        fileName = "LastWriteCompletePlan",
        menuName = "Scheherazade/Data Sync/Conflict Plans/Last Write Complete Plan"
    )]
    public sealed class LastWriteCompletePlan : ConflictResolutionPlan
    {
        public override Task<IReadOnlyList<ResolutionCandidate>> RankCandidatesAsync(
            IReadOnlyList<ResolutionCandidate> candidates,
            CancellationToken ct)
        {
            var ranked = new List<ResolutionCandidate>(candidates);
            ranked.Sort((a, b) =>
            {
                int hasA = a.LastWriteTime.HasValue ? 1 : 0;
                int hasB = b.LastWriteTime.HasValue ? 1 : 0;
                int cmp = hasB.CompareTo(hasA);
                if (cmp != 0) return cmp;

                if (a.LastWriteTime.HasValue && b.LastWriteTime.HasValue)
                {
                    cmp = b.LastWriteTime.Value.CompareTo(a.LastWriteTime.Value);
                    if (cmp != 0) return cmp;
                }

                return PriorityOrderPlan.CompareByPriority(a, b);
            });
            return Task.FromResult<IReadOnlyList<ResolutionCandidate>>(ranked);
        }
    }

    /// <summary>
    /// A specific adapter (by <see cref="ISaveAdapter.AdapterId"/>) wins when
    /// it has data for the key; otherwise resolution falls back to load order.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PreferredAdapterPlan",
        menuName = "Scheherazade/Data Sync/Conflict Plans/Preferred Adapter Plan"
    )]
    public sealed class PreferredAdapterPlan : ConflictResolutionPlan
    {
        [Tooltip("AdapterId that should win when it has data for the key.")]
        [SerializeField] private string _preferredAdapterId;

        public string PreferredAdapterId => _preferredAdapterId;

        public override Task<IReadOnlyList<ResolutionCandidate>> RankCandidatesAsync(
            IReadOnlyList<ResolutionCandidate> candidates,
            CancellationToken ct)
        {
            var ranked = new List<ResolutionCandidate>(candidates);
            ranked.Sort((a, b) =>
            {
                bool aPreferred = string.Equals(
                    a.AdapterId, _preferredAdapterId, StringComparison.Ordinal
                );
                bool bPreferred = string.Equals(
                    b.AdapterId, _preferredAdapterId, StringComparison.Ordinal
                );
                if (aPreferred != bPreferred)
                {
                    return aPreferred ? -1 : 1;
                }

                return PriorityOrderPlan.CompareByPriority(a, b);
            });
            return Task.FromResult<IReadOnlyList<ResolutionCandidate>>(ranked);
        }
    }
}
