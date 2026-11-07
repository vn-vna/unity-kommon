using System;
using System.Collections.Generic;

namespace Com.Scheherazade.Common.Frameworks.ParallelCollectorQueue
{
    /// <summary>
    /// Single-threaded, manually driven collector queues with a shared FIFO reserve.
    /// Collection updates authoritative demand synchronously. Drain buffered events
    /// only after the caller has reconciled its own cargo/transport ownership.
    /// </summary>
    public sealed class ParallelCollectorQueue
    {
        #region Constants
        private const int NotListed = -1;
        private const int ReserveList = 0;
        #endregion

        #region Events & Delegates
        public event Action<CollectorQueueEvent> EventPublished;
        #endregion

        #region Properties
        public int QueueCount => _lists.Length - 1;
        public int TargetDepth { get; }
        public int CollectorCount => _records.Length;
        public int ReserveCount => _lists[ReserveList].Count;
        public int FilledCount { get; private set; }
        public int RemovedCount { get; private set; }
        public int RemainingCollectorCount => CollectorCount - RemovedCount;
        public long TotalRequiredUnits { get; }
        public long CollectedUnits { get; private set; }
        public bool IsCompleted => RemovedCount == CollectorCount;
        public int PendingEventCount => _eventCount;
        public int EventCapacity => _events.Length;
        public int MaximumEventsPerCollection => QueueCount + 9;
        #endregion

        #region Private Fields
        private readonly Record[] _records;
        private readonly IndexList[] _lists;
        private readonly Dictionary<int, int> _byId;
        private readonly bool[] _blocked;
        private readonly int[] _previousFronts;
        private readonly CollectorQueueEvent[] _events;
        private int _eventHead;
        private int _eventCount;
        private long _eventSequence;
        private bool _mutating;
        private bool _dispatching;
        #endregion

        #region Construction
        /// <param name="definitions">Copied in authored reserve order; IDs must be unique.</param>
        /// <param name="queueCount">Positive parallel queue count; linked pairs require at least two.</param>
        /// <param name="targetDepth">Desired queue depth, not a hard cap for linked partner admission.</param>
        /// <param name="eventCapacity">0 chooses an automatic capacity. Explicit capacities must be at least queueCount + 9.</param>
        public ParallelCollectorQueue(IReadOnlyList<CollectorDefinition> definitions, int queueCount = 4,
            int targetDepth = 3, int eventCapacity = 0)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (queueCount < 1 || queueCount > int.MaxValue - 9) throw new ArgumentOutOfRangeException(nameof(queueCount));
            if (targetDepth < 1) throw new ArgumentOutOfRangeException(nameof(targetDepth));
            if (eventCapacity < 0 || (eventCapacity != 0 && eventCapacity < queueCount + 9))
                throw new ArgumentOutOfRangeException(nameof(eventCapacity), "Event capacity must be zero or at least queueCount + 9.");

            TargetDepth = targetDepth;
            _records = new Record[definitions.Count];
            _lists = new IndexList[queueCount + 1];
            _byId = new Dictionary<int, int>(definitions.Count);
            _blocked = new bool[queueCount];
            _previousFronts = new int[queueCount];
            _events = new CollectorQueueEvent[eventCapacity == 0 ? Math.Max(64, queueCount + 9) : eventCapacity];
            for (int index = 0; index < _lists.Length; index++)
                _lists[index] = new IndexList { Head = NotListed, Tail = NotListed };

            long total = 0;
            for (int index = 0; index < _records.Length; index++)
            {
                CollectorDefinition definition = definitions[index];
                ValidateDefinition(definition);
                if (_byId.ContainsKey(definition.Id)) throw new ArgumentException("Collector IDs must be unique.", nameof(definitions));
                _byId.Add(definition.Id, index);
                _records[index] = new Record
                {
                    Definition = definition,
                    Remaining = definition.RequiredUnits,
                    List = NotListed,
                    Previous = NotListed,
                    Next = NotListed,
                    Partner = NotListed
                };
                total += definition.RequiredUnits;
                Append(ReserveList, index);
            }
            TotalRequiredUnits = total;
            LinkPairs();
            FillInitialQueues();
        }
        #endregion

        #region Public Methods
        public int GetQueueCount(int queueIndex)
        {
            ValidateQueue(queueIndex);
            return _lists[queueIndex + 1].Count;
        }

        public bool IsQueueBlocked(int queueIndex)
        {
            ValidateQueue(queueIndex);
            return _blocked[queueIndex];
        }

        /// <summary>An explicit caller-owned intake gate. Does not reorder queues or emit lifecycle events.</summary>
        public void SetQueueBlocked(int queueIndex, bool blocked)
        {
            RequireMutable();
            ValidateQueue(queueIndex);
            _blocked[queueIndex] = blocked;
        }

        public bool TryGetFront(int queueIndex, out CollectorSnapshot collector)
        {
            ValidateQueue(queueIndex);
            int index = _lists[queueIndex + 1].Head;
            collector = Snapshot(index);
            return index != NotListed;
        }

        public bool TryGetCollector(int collectorId, out CollectorSnapshot collector)
        {
            if (_byId.TryGetValue(collectorId, out int index))
            {
                collector = Snapshot(index);
                return true;
            }
            collector = default;
            return false;
        }

        public CollectorSnapshot GetCollectorAt(int authoredIndex)
        {
            if (authoredIndex < 0 || authoredIndex >= _records.Length) throw new ArgumentOutOfRangeException(nameof(authoredIndex));
            return Snapshot(authoredIndex);
        }

        public CollectorSnapshot GetQueuedCollector(int queueIndex, int position)
        {
            ValidateQueue(queueIndex);
            return Snapshot(GetListItem(queueIndex + 1, position));
        }

        public CollectorSnapshot GetReserveCollector(int position) => Snapshot(GetListItem(ReserveList, position));

        /// <summary>
        /// Accepts at most the named current front's remaining demand, never spills into
        /// its successor. The caller retains offeredUnits - acceptedUnits. Rejections
        /// change neither model nor buffered events. This is not an idempotent cargo-ID API.
        /// </summary>
        public CollectorCollectionStatus TryCollect(int queueIndex, CollectorHandle expectedFront, int typeId,
            int offeredUnits, out int acceptedUnits)
        {
            RequireMutable();
            acceptedUnits = 0;
            if (queueIndex < 0 || queueIndex >= QueueCount) return CollectorCollectionStatus.InvalidQueue;
            if (offeredUnits < 1) return CollectorCollectionStatus.InvalidQuantity;
            int index = _lists[queueIndex + 1].Head;
            if (index == NotListed) return CollectorCollectionStatus.Empty;
            if (!expectedFront.BelongsTo(this) || expectedFront.Id != _records[index].Definition.Id)
                return CollectorCollectionStatus.StaleCollector;
            if (_blocked[queueIndex]) return CollectorCollectionStatus.Blocked;
            if (_records[index].Remaining == 0) return CollectorCollectionStatus.FilledHeld;
            if (_records[index].Definition.TypeId != typeId) return CollectorCollectionStatus.TypeMismatch;

            int units = Math.Min(offeredUnits, _records[index].Remaining);
            if (_events.Length - _eventCount < RequiredEventBudget(index, units))
                return CollectorCollectionStatus.EventBufferFull;

            _mutating = true;
            try
            {
                for (int queue = 0; queue < QueueCount; queue++) _previousFronts[queue] = _lists[queue + 1].Head;
                _records[index].Remaining -= units;
                CollectedUnits += units;
                Enqueue(CollectorQueueEventKind.CargoCollected, queueIndex, index, units: units);
                if (_records[index].Remaining == 0) CompleteCollector(index, queueIndex);
                PublishFrontChanges();
                if (IsCompleted) Enqueue(CollectorQueueEventKind.Completed, NotListed, NotListed);
                acceptedUnits = units;
                return CollectorCollectionStatus.Accepted;
            }
            finally { _mutating = false; }
        }

        public bool TryDequeueEvent(out CollectorQueueEvent result)
        {
            RequireMutable();
            if (_eventCount == 0) { result = default; return false; }
            result = _events[_eventHead];
            RemoveFirstEvent();
            return true;
        }

        /// <summary>
        /// Publishes committed facts. Mutation/dequeuing/reentrant dispatch are forbidden
        /// in callbacks. If a subscriber throws, the current event remains for retry;
        /// earlier subscribers may see that event again and must reconcile by Sequence.
        /// No subscribers means events remain available for explicit polling.
        /// </summary>
        public int DispatchEvents()
        {
            RequireMutable();
            Action<CollectorQueueEvent> handler = EventPublished;
            if (handler == null) return 0;
            _dispatching = true;
            int dispatched = 0;
            try
            {
                while (_eventCount > 0)
                {
                    handler(_events[_eventHead]);
                    RemoveFirstEvent();
                    dispatched++;
                }
                return dispatched;
            }
            finally { _dispatching = false; }
        }
        #endregion

        #region Private Methods
        private static void ValidateDefinition(CollectorDefinition definition)
        {
            // Also reject default(CollectorDefinition), which bypasses its constructor.
            if (definition.Id < 0 || definition.TypeId < 0 || definition.RequiredUnits < 1 ||
                definition.LinkedGroupId < CollectorDefinition.NoLinkedGroup)
                throw new ArgumentException("Every collector needs nonnegative IDs and positive required units.", "definitions");
        }

        private void LinkPairs()
        {
            var firstByGroup = new Dictionary<int, int>();
            for (int index = 0; index < _records.Length; index++)
            {
                int group = _records[index].Definition.LinkedGroupId;
                if (group == CollectorDefinition.NoLinkedGroup) continue;
                if (QueueCount < 2) throw new ArgumentException("Linked collectors require at least two queues.", "definitions");
                if (!firstByGroup.TryGetValue(group, out int first)) { firstByGroup.Add(group, index); continue; }
                if (_records[first].Partner != NotListed) throw new ArgumentException("A linked group must have exactly two collectors.", "definitions");
                _records[first].Partner = index;
                _records[index].Partner = first;
            }
            foreach (int first in firstByGroup.Values)
            {
                if (_records[first].Partner == NotListed) throw new ArgumentException("A linked group must have exactly two collectors.", "definitions");
            }
        }

        private void FillInitialQueues()
        {
            for (int row = 0; row < TargetDepth && ReserveCount > 0; row++)
            {
                for (int queue = 0; queue < QueueCount && ReserveCount > 0; queue++)
                {
                    if (_lists[queue + 1].Count <= row) AdmitNext(queue, false);
                }
            }
        }

        private void AdmitNext(int queueIndex, bool notify)
        {
            int index = _lists[ReserveList].Head;
            int partner = _records[index].Partner;
            MoveToQueue(index, queueIndex, notify);
            if (partner == NotListed) return;
            // Co-admit both members, in admission order, to distinct queue tails. A later
            // group cannot cross an earlier group's order and create a linked hold cycle.
            int destination = ShortestOtherQueue(queueIndex);
            MoveToQueue(partner, destination, notify);
        }

        private int ShortestOtherQueue(int excluded)
        {
            int selected = NotListed;
            for (int queue = 0; queue < QueueCount; queue++)
            {
                if (queue == excluded) continue;
                if (selected == NotListed || _lists[queue + 1].Count < _lists[selected + 1].Count) selected = queue;
            }
            return selected;
        }

        private void MoveToQueue(int index, int queueIndex, bool notify)
        {
            Unlink(index);
            Append(queueIndex + 1, index);
            if (notify) Enqueue(CollectorQueueEventKind.CollectorQueued, queueIndex, index);
        }

        private int RequiredEventBudget(int index, int units)
        {
            if (units < _records[index].Remaining) return 1;
            int partner = _records[index].Partner;
            if (partner != NotListed && _records[partner].Remaining > 0) return 2;
            // Each removal opens at most one refill slot; each refill admits at most a pair.
            int removals = partner == NotListed ? 1 : 2;
            return 3 + 3 * removals + QueueCount;
        }

        private void CompleteCollector(int index, int queueIndex)
        {
            FilledCount++;
            Enqueue(CollectorQueueEventKind.CollectorFilled, queueIndex, index);
            int partner = _records[index].Partner;
            if (partner != NotListed && _records[partner].Remaining > 0) return;
            if (partner == NotListed) RemoveCollector(index);
            else
            {
                // Stable authored order, independent of which member received the last unit.
                RemoveCollector(Math.Min(index, partner));
                RemoveCollector(Math.Max(index, partner));
            }
            RefillQueues();
        }

        private void RemoveCollector(int index)
        {
            int queueIndex = _records[index].List - 1;
            Unlink(index);
            RemovedCount++;
            Enqueue(CollectorQueueEventKind.CollectorRemoved, queueIndex, index);
        }

        private void RefillQueues()
        {
            for (int queue = QueueCount - 1; queue >= 0; queue--)
            {
                while (_lists[queue + 1].Count < TargetDepth && ReserveCount > 0) AdmitNext(queue, true);
            }
        }

        private void PublishFrontChanges()
        {
            for (int queue = 0; queue < QueueCount; queue++)
            {
                int current = _lists[queue + 1].Head;
                int previous = _previousFronts[queue];
                if (current == previous) continue;
                CollectorHandle previousHandle = previous == NotListed ? default : Handle(previous);
                Enqueue(CollectorQueueEventKind.FrontChanged, queue, current, previousHandle);
            }
        }

        private CollectorHandle Handle(int index) => new CollectorHandle(this, _records[index].Definition.Id);

        private CollectorSnapshot Snapshot(int index)
        {
            if (index == NotListed) return default;
            Record record = _records[index];
            CollectorLocation location = record.List == NotListed ? CollectorLocation.Removed :
                record.List == ReserveList ? CollectorLocation.Reserve : CollectorLocation.Queue;
            return new CollectorSnapshot(Handle(index), record.Definition, record.Remaining, location,
                location == CollectorLocation.Queue ? record.List - 1 : NotListed);
        }

        private int GetListItem(int list, int position)
        {
            if (position < 0 || position >= _lists[list].Count) throw new ArgumentOutOfRangeException(nameof(position));
            int index = _lists[list].Head;
            for (int offset = 0; offset < position; offset++) index = _records[index].Next;
            return index;
        }

        private void Append(int list, int index)
        {
            int tail = _lists[list].Tail;
            _records[index].List = list;
            _records[index].Previous = tail;
            _records[index].Next = NotListed;
            if (tail == NotListed) _lists[list].Head = index;
            else _records[tail].Next = index;
            _lists[list].Tail = index;
            _lists[list].Count++;
        }

        private void Unlink(int index)
        {
            Record record = _records[index];
            if (record.Previous == NotListed) _lists[record.List].Head = record.Next;
            else _records[record.Previous].Next = record.Next;
            if (record.Next == NotListed) _lists[record.List].Tail = record.Previous;
            else _records[record.Next].Previous = record.Previous;
            _lists[record.List].Count--;
            _records[index].List = NotListed;
            _records[index].Previous = NotListed;
            _records[index].Next = NotListed;
        }

        private void Enqueue(CollectorQueueEventKind kind, int queueIndex, int index,
            CollectorHandle previousFront = default, int units = 0)
        {
            int slot = (int)(((long)_eventHead + _eventCount) % _events.Length);
            _events[slot] = new CollectorQueueEvent(++_eventSequence, kind, queueIndex, Snapshot(index), previousFront, units);
            _eventCount++;
        }

        private void RemoveFirstEvent()
        {
            _events[_eventHead] = default;
            _eventHead = (_eventHead + 1) % _events.Length;
            _eventCount--;
        }

        private void RequireMutable()
        {
            if (_mutating || _dispatching) throw new InvalidOperationException("Queue mutation, event polling and dispatch cannot reenter an active command or notification.");
        }

        private void ValidateQueue(int queueIndex)
        {
            if (queueIndex < 0 || queueIndex >= QueueCount) throw new ArgumentOutOfRangeException(nameof(queueIndex));
        }
        #endregion

        #region Nested Types
        private struct Record
        {
            public CollectorDefinition Definition;
            public int Remaining;
            public int List;
            public int Previous;
            public int Next;
            public int Partner;
        }

        private struct IndexList
        {
            public int Head;
            public int Tail;
            public int Count;
        }
        #endregion
    }
}
