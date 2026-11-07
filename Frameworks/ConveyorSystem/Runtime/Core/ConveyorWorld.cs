using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public sealed partial class ConveyorWorld : IDisposable
    {
        #region Properties
        public long Id { get; }
        public int Capacity => _records.Length;
        public int Count { get; private set; }
        public int ResultCount => _resultCount;
        public int PendingAdmissions => _pendingIngress;
        public long TickIndex { get; private set; }
        public long AcceptedCount { get; private set; }
        public long RemovedCount { get; private set; }
        public long DeliveredCount { get; private set; }
        public long VaporizedCount { get; private set; }
        public long TransferCount { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool IsFaulted { get; private set; }
        public bool IsBusy => _busy || _dispatching || _awaitingCompletion;
        public IReadOnlyList<ConveyorRuntime> Conveyors => _runtimeView;
        public int AvailableResultCapacity => _results.Length - _resultCount - _reservedResults;
        #endregion

        #region Private Fields
        private static long _nextWorldId;
        private readonly Record[] _records;
        private readonly Ingress[] _ingress;
        private readonly int[] _freeSlots;
        private readonly ConveyorResult[] _results;
        private readonly Action<ConveyorWorld>[] _commands;
        private readonly List<ConveyorRuntime> _runtimes = new List<ConveyorRuntime>();
        private readonly ReadOnlyCollection<ConveyorRuntime> _runtimeView;
        private readonly Dictionary<string, ConveyorRuntime> _byId = new Dictionary<string, ConveyorRuntime>(StringComparer.Ordinal);
        private readonly ConveyorTransferRequests _requestSink;
        private readonly TransferRequest[] _requests;
        private readonly TransferOutcome[] _outcomes;
        private readonly long[] _requestedEpoch;
        private int _freeCount;
        private int _pendingIngress;
        private int _resultHead;
        private int _resultCount;
        private int _reservedResults;
        private int _commandHead;
        private int _commandCount;
        private int _requestCount;
        private long _reservationSerial;
        private long _collectionEpoch;
        private bool _busy;
        private bool _dispatching;
        private bool _stepping;
        private bool _awaitingCompletion;
        private object _clockOwner;
        #endregion

        #region Construction
        public ConveyorWorld(int payloadCapacity = 4096, int resultCapacity = 8192, int commandCapacity = 128)
        {
            if (payloadCapacity < 1 || resultCapacity < 1 || commandCapacity < 1) throw new ArgumentOutOfRangeException(nameof(payloadCapacity));
            Id = Interlocked.Increment(ref _nextWorldId);
            _records = new Record[payloadCapacity];
            _ingress = new Ingress[payloadCapacity];
            _freeSlots = new int[payloadCapacity];
            _results = new ConveyorResult[resultCapacity];
            _commands = new Action<ConveyorWorld>[commandCapacity];
            _requests = new TransferRequest[payloadCapacity];
            _outcomes = new TransferOutcome[payloadCapacity];
            _requestedEpoch = new long[payloadCapacity];
            _freeCount = payloadCapacity;
            for (int index = 0; index < payloadCapacity; index++)
            {
                _freeSlots[index] = payloadCapacity - index - 1;
                _records[index].Generation = 1;
            }
            _runtimeView = _runtimes.AsReadOnly();
            _requestSink = new ConveyorTransferRequests { World = this };
        }
        #endregion

        #region Public Methods
        public ConveyorRuntime CreateConveyor(string id, ConveyorPath path, IConveyorTransportProvider provider,
            int channels = 1, float width = 1, IReadOnlyList<IConveyorModuleFactory> modules = null, bool replaceEmpty = false)
        {
            RequireMutable();
            RequireNoAdmissions();
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Conveyor ID is required.");
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (channels < 1 || channels > 64) throw new ArgumentOutOfRangeException(nameof(channels));
            _byId.TryGetValue(id, out ConveyorRuntime previous);
            if (previous != null && (!replaceEmpty || previous.Count != 0))
                throw new InvalidOperationException("Duplicate ID or occupied conveyor replacement.");
            var context = new ConveyorTransportContext(path, Capacity, channels, width);
            ConveyorRuntime candidate = null;
            IConveyorTransportBackend backend = null;
            bool ownsBackend = false;
            _busy = true;
            try
            {
                backend = provider.Create(context) ?? throw new ArgumentException("Transport provider returned null.");
                for (int index = 0; index < _runtimes.Count; index++)
                {
                    if (ReferenceEquals(_runtimes[index].Backend, backend))
                        throw new InvalidOperationException("A provider reused an already-owned backend instance.");
                }
                ownsBackend = true;
                ConveyorTransportCapabilities required = ConveyorTransportCapabilities.PosePublication | ConveyorTransportCapabilities.AdmissionReservations;
                if (backend.Count != 0 || (backend.Capabilities & required) != required)
                    throw new NotSupportedException("Backend must start empty and support admission reservations and pose publication.");
                candidate = new ConveyorRuntime(this, id, context, backend);
                if (modules != null)
                {
                    for (int index = 0; index < modules.Count; index++) candidate.AttachModule(modules[index]);
                }
                candidate.Publish();
                if (previous != null) _runtimes.Remove(previous);
                _byId[id] = candidate;
                _runtimes.Add(candidate);
                _runtimes.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
            }
            catch
            {
                if (candidate != null) candidate.DisposeOwned();
                else if (ownsBackend) backend.Dispose();
                throw;
            }
            finally { _busy = false; }
            _busy = true;
            try { previous?.DisposeOwned(); }
            catch { IsFaulted = true; throw; }
            finally { _busy = false; }
            return candidate;
        }

        public bool TryGetConveyor(string id, out ConveyorRuntime runtime)
        {
            if (id == null) { runtime = null; return false; }
            return _byId.TryGetValue(id, out runtime);
        }

        public IConveyorModule AttachModule(ConveyorRuntime runtime, IConveyorModuleFactory factory)
        {
            RequireMutable(); RequireNoAdmissions(); RequireOwned(runtime);
            _busy = true;
            try { return runtime.AttachModule(factory); }
            finally { _busy = false; }
        }

        public void DetachModule(ConveyorRuntime runtime, IConveyorModule module)
        {
            RequireMutable(); RequireNoAdmissions(); RequireOwned(runtime);
            if (!ReserveResults(ReportedHoldCount(runtime))) throw new InvalidOperationException("Drain lane notifications before detaching a module.");
            _busy = true;
            try
            {
                runtime.DetachModule(module);
                for (int channel = 0; channel < runtime.Channels; channel++)
                {
                    bool held = runtime.Backend is IConveyorLaneControl lanes && lanes.IsLaneHeld(channel);
                    if (runtime.ReportedHolds[channel])
                    {
                        if (held) _reservedResults--;
                        else ReleaseLane(runtime, channel);
                    }
                    else if (!held) runtime.Blocks[channel] = ConveyorBlockReason.None;
                }
            }
            finally { _busy = false; }
        }

        public ConveyorAdmissionStatus TryReserveIngress(ConveyorRuntime runtime, in ConveyorAdmissionRequest request,
            out ConveyorIngressReservation reservation)
        {
            RequireMutable(); RequireOwned(runtime);
            reservation = default;
            if (runtime.IsSuspended) return ConveyorAdmissionStatus.Unavailable;
            if (_freeCount == 0 || AvailableResultCapacity == 0) return ConveyorAdmissionStatus.Full;
            _busy = true;
            try
            {
                ConveyorAdmissionStatus status = runtime.Backend.TryReserve(request, out ConveyorBackendReservation backendToken);
                if (status != ConveyorAdmissionStatus.Accepted) return status;
                if (!runtime.Backend.IsReservationValid(backendToken)) throw new InvalidOperationException("Backend returned an invalid reservation.");
                int slot = _freeSlots[--_freeCount];
                long serial = ++_reservationSerial;
                _ingress[slot] = new Ingress { Token = serial, Runtime = runtime, Payload = request.Payload, BackendToken = backendToken };
                _pendingIngress++;
                _reservedResults++;
                reservation = new ConveyorIngressReservation(this, slot, serial);
                return ConveyorAdmissionStatus.Accepted;
            }
            finally { _busy = false; }
        }

        public bool CommitIngress(in ConveyorIngressReservation reservation, out ConveyorPayloadHandle handle)
        {
            RequireMutable();
            handle = default;
            if (!ValidIngress(reservation)) return false;
            _busy = true;
            try
            {
                int slot = reservation.Slot;
                Ingress ingress = _ingress[slot];
                if (!ingress.Runtime.Backend.IsReservationValid(ingress.BackendToken))
                    throw new InvalidOperationException("Backend invalidated an outstanding ingress reservation.");
                handle = new ConveyorPayloadHandle(Id, slot, _records[slot].Generation);
                ingress.Runtime.Backend.CommitAdmission(ingress.BackendToken, handle);
                _records[slot].Active = true;
                _records[slot].Runtime = ingress.Runtime;
                _records[slot].Payload = ingress.Payload;
                _ingress[slot] = default;
                _pendingIngress--;
                Count++;
                AcceptedCount++;
                EmitReserved(new ConveyorResult(ConveyorResultKind.Inserted, handle, ingress.Payload, ingress.Runtime.Id));
                return true;
            }
            catch { IsFaulted = true; throw; }
            finally { _busy = false; }
        }

        public bool CancelIngress(in ConveyorIngressReservation reservation)
        {
            RequireMutable();
            if (!ValidIngress(reservation)) return false;
            _busy = true;
            try
            {
                Ingress ingress = _ingress[reservation.Slot];
                ingress.Runtime.Backend.CancelReservation(ingress.BackendToken);
                _ingress[reservation.Slot] = default;
                _pendingIngress--;
                _reservedResults--;
                ReleaseSlot(reservation.Slot);
                return true;
            }
            finally { _busy = false; }
        }

        public ConveyorAdmissionStatus TryInsert(ConveyorRuntime runtime, in ConveyorAdmissionRequest request,
            out ConveyorPayloadHandle handle)
        {
            handle = default;
            ConveyorAdmissionStatus status = TryReserveIngress(runtime, request, out ConveyorIngressReservation reservation);
            if (status == ConveyorAdmissionStatus.Accepted && !CommitIngress(reservation, out handle))
                throw new InvalidOperationException("An immediate ingress commit unexpectedly failed.");
            return status;
        }

        public ConveyorAdmissionStatus TryInsertAt(ConveyorRuntime runtime, ConveyorPayloadDescriptor payload,
            int channel, double distance, out ConveyorPayloadHandle handle)
        {
            RequireOwned(runtime);
            var request = new ConveyorAdmissionRequest(payload,
                runtime.Path.EvaluateLane(distance, channel, runtime.Channels, runtime.Width), channel, distance);
            return TryInsert(runtime, request, out handle);
        }

        public bool TryGetPayload(ConveyorPayloadHandle handle, out ConveyorPayloadDescriptor payload, out ConveyorRuntime owner)
        {
            if (!Contains(handle)) { payload = default; owner = null; return false; }
            Record record = _records[handle.Slot];
            payload = record.Payload;
            owner = record.Runtime;
            return true;
        }

        public bool TryGetState(ConveyorPayloadHandle handle, out ConveyorTransportState state)
        {
            if (_awaitingCompletion) throw new InvalidOperationException("Complete the world step before reading transport state.");
            if (!Contains(handle)) { state = default; return false; }
            return _records[handle.Slot].Runtime.Backend.TryGetState(handle, out state);
        }

        public bool TryRemove(ConveyorPayloadHandle handle, ConveyorRemovalReason reason = ConveyorRemovalReason.Removed)
        {
            RequireMutable();
            if (!Contains(handle)) return false;
            if ((uint)reason > (uint)ConveyorRemovalReason.Shutdown) throw new ArgumentOutOfRangeException(nameof(reason));
            ConveyorRuntime runtime = _records[handle.Slot].Runtime;
            runtime.Backend.TryGetState(handle, out ConveyorTransportState state);
            int channel = state.Channel;
            bool possibleRelease = (uint)channel < (uint)runtime.Channels && runtime.ReportedHolds[channel];
            if (!ReserveResults(possibleRelease ? 2 : 1)) return false;
            _busy = true;
            try
            {
                RemoveRecord(handle.Slot, reason);
                if (possibleRelease)
                {
                    if (!runtime.IsLaneHeld(channel)) ReleaseLane(runtime, channel);
                    else _reservedResults--;
                }
                return true;
            }
            catch { IsFaulted = true; throw; }
            finally { _busy = false; }
        }

        public void SetSuspended(ConveyorRuntime runtime, bool suspended)
        {
            RequireMutable(); RequireNoAdmissions(); RequireOwned(runtime);
            _busy = true;
            try { runtime.Backend.SetSuspended(suspended); runtime.IsSuspended = suspended; }
            finally { _busy = false; }
        }

        public void SetSpeed(ConveyorRuntime runtime, double speed)
        {
            RequireMutable(); RequireNoAdmissions(); RequireOwned(runtime);
            if (runtime.Backend is not IConveyorLaneControl lanes) throw new NotSupportedException("Backend has no lane-speed control.");
            _busy = true;
            try { lanes.SetSpeed(speed); }
            finally { _busy = false; }
        }

        public bool TryEnqueue(Action<ConveyorWorld> command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (IsDisposed || IsFaulted || _commandCount == _commands.Length) return false;
            _commands[(_commandHead + _commandCount++) % _commands.Length] = command;
            return true;
        }

        public void ClaimClock(object owner)
        {
            RequireMutable();
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (_clockOwner != null && !ReferenceEquals(_clockOwner, owner)) throw new InvalidOperationException("This world already has a step driver.");
            _clockOwner = owner;
        }

        public void ReleaseClock(object owner)
        {
            RequireMutable();
            if (!ReferenceEquals(_clockOwner, owner)) throw new InvalidOperationException("Only the clock owner can release it.");
            _clockOwner = null;
        }

        public void Tick(double deltaTime, object driver = null)
        {
            RequireClock(driver);
            RequireSynchronousBackends();
            BeginStepInternal(deltaTime, driver, true);
            CompleteStep(driver);
        }

        public void BeginStep(double deltaTime, object driver = null)
            => BeginStepInternal(deltaTime, driver, false);

        public void CompleteStep(object driver = null)
        {
            RequireClock(driver);
            if (!_awaitingCompletion || IsDisposed || IsFaulted) throw new InvalidOperationException("No completable world step exists.");
            try
            {
                for (int index = 0; index < _runtimes.Count; index++) _runtimes[index].Backend.CompleteStep();
                _awaitingCompletion = false;
                PublishInternal();
                TickIndex++;
            }
            catch { IsFaulted = true; throw; }
            finally { _busy = _stepping = _awaitingCompletion = false; }
        }

        public void Publish()
        {
            RequireMutable(); RequireNoAdmissions();
            _busy = true;
            try { PublishInternal(); }
            finally { _busy = false; }
        }

        public bool TryDequeueResult(out ConveyorResult result)
        {
            if (IsBusy) throw new InvalidOperationException("Results cannot be drained during simulation or result callbacks.");
            if (_resultCount == 0) { result = default; return false; }
            result = _results[_resultHead];
            _results[_resultHead] = default;
            _resultHead = (_resultHead + 1) % _results.Length;
            _resultCount--;
            return true;
        }

        public void DispatchResults(Action<ConveyorResult> consumer)
        {
            if (consumer == null) throw new ArgumentNullException(nameof(consumer));
            if (IsBusy || _stepping) throw new InvalidOperationException("Cannot dispatch inside a world operation.");
            _dispatching = true;
            try
            {
                int count = _resultCount;
                for (int index = 0; index < count; index++)
                {
                    consumer(_results[_resultHead]);
                    _results[_resultHead] = default;
                    _resultHead = (_resultHead + 1) % _results.Length;
                    _resultCount--;
                }
            }
            finally { _dispatching = false; }
        }

        public void RemoveConveyor(ConveyorRuntime runtime, ConveyorShutdownDisposition disposition = ConveyorShutdownDisposition.RequireEmpty)
        {
            RequireMutable(); RequireNoAdmissions(); RequireOwned(runtime);
            if ((uint)disposition > (uint)ConveyorShutdownDisposition.Discard) throw new ArgumentOutOfRangeException(nameof(disposition));
            if (runtime.Count != 0 && disposition != ConveyorShutdownDisposition.Discard)
                throw new InvalidOperationException("Drain cargo or explicitly select Discard before removal.");
            int required = runtime.Count + ReportedHoldCount(runtime);
            if (!ReserveResults(required)) throw new InvalidOperationException("Drain result records before removing this conveyor.");
            _busy = true;
            try
            {
                for (int slot = 0; slot < Capacity; slot++)
                {
                    if (_records[slot].Active && _records[slot].Runtime == runtime) RemoveRecord(slot, ConveyorRemovalReason.Shutdown);
                }
                for (int channel = 0; channel < runtime.Channels; channel++)
                {
                    if (runtime.ReportedHolds[channel]) ReleaseLane(runtime, channel);
                }
                _byId.Remove(runtime.Id);
                _runtimes.Remove(runtime);
                runtime.DisposeOwned();
            }
            finally { _busy = false; }
        }

        public void Shutdown(ConveyorShutdownDisposition disposition)
        {
            if (IsDisposed) return;
            if ((uint)disposition > (uint)ConveyorShutdownDisposition.Discard) throw new ArgumentOutOfRangeException(nameof(disposition));
            if (IsBusy || _stepping) throw new InvalidOperationException("Cannot shut down during a world step.");
            RequireNoAdmissions();
            if (Count != 0 && disposition != ConveyorShutdownDisposition.Discard)
                throw new InvalidOperationException("World still owns cargo; explicitly choose a shutdown disposition.");
            int required = Count;
            for (int index = 0; index < _runtimes.Count; index++) required += ReportedHoldCount(_runtimes[index]);
            if (!ReserveResults(required)) throw new InvalidOperationException("Drain result records before shutdown.");
            _busy = true;
            try
            {
                for (int slot = 0; slot < Capacity; slot++)
                {
                    if (_records[slot].Active) RemoveRecord(slot, ConveyorRemovalReason.Shutdown);
                }
                for (int index = _runtimes.Count - 1; index >= 0; index--)
                {
                    ConveyorRuntime runtime = _runtimes[index];
                    for (int channel = 0; channel < runtime.Channels; channel++)
                    {
                        if (runtime.ReportedHolds[channel]) ReleaseLane(runtime, channel);
                    }
                    runtime.DisposeOwned();
                }
                _runtimes.Clear();
                _byId.Clear();
                Array.Clear(_commands, 0, _commands.Length);
                _commandCount = 0;
                IsDisposed = true;
            }
            finally { _busy = false; }
        }

        public void Dispose() => Shutdown(ConveyorShutdownDisposition.RequireEmpty);
        #endregion

        #region Private Methods
        private void BeginStepInternal(double deltaTime, object driver, bool synchronousOnly)
        {
            RequireClock(driver);
            RequireMutable(); RequireNoAdmissions();
            if (_stepping) throw new InvalidOperationException("World step is already active.");
            if (!ConveyorValidation.IsFinite(deltaTime) || deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            _stepping = true;
            try
            {
                ApplyCommands();
                RequireNoAdmissions();
                if (synchronousOnly) RequireSynchronousBackends();
                _busy = true;
                ServiceTransfers();
                for (int index = 0; index < _runtimes.Count; index++) _runtimes[index].Backend.PrepareStep(deltaTime);
                _awaitingCompletion = true;
            }
            catch { IsFaulted = true; _busy = _stepping = _awaitingCompletion = false; throw; }
        }

        private void ApplyCommands()
        {
            int commands = _commandCount;
            for (int index = 0; index < commands; index++)
            {
                Action<ConveyorWorld> command = _commands[_commandHead];
                _commands[_commandHead] = null;
                _commandHead = (_commandHead + 1) % _commands.Length;
                _commandCount--;
                command(this);
            }
        }

        private void RequireSynchronousBackends()
        {
            for (int index = 0; index < _runtimes.Count; index++)
            {
                if (_runtimes[index].Backend.StepDriver != ConveyorStepDriver.Synchronous)
                    throw new InvalidOperationException("An external backend requires BeginStep / driver / CompleteStep, not Tick.");
            }
        }

        private void PublishInternal()
        {
            for (int index = 0; index < _runtimes.Count; index++) _runtimes[index].Publish();
        }

        private void RemoveRecord(int slot, ConveyorRemovalReason reason)
        {
            Record record = _records[slot];
            var handle = new ConveyorPayloadHandle(Id, slot, record.Generation);
            record.Runtime.Backend.Remove(handle);
            EmitReserved(new ConveyorResult(ConveyorResultKind.Removed, handle, record.Payload, record.Runtime.Id, removalReason: reason));
            Count--;
            if (reason == ConveyorRemovalReason.Delivered) DeliveredCount++;
            else if (reason == ConveyorRemovalReason.Vaporized) VaporizedCount++;
            else RemovedCount++;
            ReleaseSlot(slot);
        }

        private void ReleaseSlot(int slot)
        {
            uint generation = _records[slot].Generation;
            _records[slot] = default;
            if (generation == uint.MaxValue) return;
            _records[slot].Generation = generation + 1;
            _freeSlots[_freeCount++] = slot;
        }

        private bool Contains(ConveyorPayloadHandle handle) => !IsDisposed && handle.WorldId == Id
            && (uint)handle.Slot < (uint)Capacity && _records[handle.Slot].Active
            && _records[handle.Slot].Generation == handle.Generation;
        private bool ValidIngress(ConveyorIngressReservation reservation) => ReferenceEquals(reservation.World, this)
            && (uint)reservation.Slot < (uint)Capacity && reservation.Token != 0 && _ingress[reservation.Slot].Token == reservation.Token;

        private bool ReserveResults(int count)
        {
            if (AvailableResultCapacity < count) return false;
            _reservedResults += count;
            return true;
        }

        private void EmitReserved(ConveyorResult result)
        {
            if (_reservedResults <= 0 || _resultCount == _results.Length) throw new InvalidOperationException("Result reservation invariant failed.");
            _reservedResults--;
            _results[(_resultHead + _resultCount++) % _results.Length] = result;
        }

        private void ReleaseLane(ConveyorRuntime source, int channel)
        {
            source.ReportedHolds[channel] = false;
            source.Blocks[channel] = ConveyorBlockReason.None;
            EmitReserved(new ConveyorResult(ConveyorResultKind.LaneReleased, default, default, source.Id, channel: channel));
        }

        private static int ReportedHoldCount(ConveyorRuntime runtime)
        {
            int count = 0;
            for (int channel = 0; channel < runtime.Channels; channel++) if (runtime.ReportedHolds[channel]) count++;
            return count;
        }

        private void RequireOwned(ConveyorRuntime runtime)
        {
            if (runtime == null || runtime.World != this || runtime.IsDisposed
                || !_byId.TryGetValue(runtime.Id, out ConveyorRuntime current) || current != runtime)
                throw new ArgumentException("Runtime is not registered in this world.", nameof(runtime));
        }

        private void RequireMutable()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(ConveyorWorld));
            if (IsFaulted) throw new InvalidOperationException("The world is faulted; inspect the failing provider before reconstructing it.");
            if (IsBusy) throw new InvalidOperationException("Reentrant mutation is forbidden; enqueue commands for the next tick.");
        }

        private void RequireNoAdmissions()
        {
            if (_pendingIngress != 0) throw new InvalidOperationException("Commit or cancel outstanding ingress reservations first.");
        }

        private void RequireClock(object driver)
        {
            if (_clockOwner != null && !ReferenceEquals(_clockOwner, driver))
                throw new InvalidOperationException("Use the world's registered step driver; a second clock is forbidden.");
        }
        #endregion

        #region Nested Types
        private struct Record
        {
            internal uint Generation;
            internal bool Active;
            internal ConveyorRuntime Runtime;
            internal ConveyorPayloadDescriptor Payload;
        }

        private struct Ingress
        {
            internal long Token;
            internal ConveyorRuntime Runtime;
            internal ConveyorPayloadDescriptor Payload;
            internal ConveyorBackendReservation BackendToken;
        }
        #endregion
    }
}
