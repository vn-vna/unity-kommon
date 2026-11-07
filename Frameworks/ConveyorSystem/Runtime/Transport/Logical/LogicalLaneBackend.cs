using System;
using System.Collections.Generic;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [Serializable]
    public struct LogicalPayloadProfile
    {
        public int id;
        public int footprintSlots;

        public LogicalPayloadProfile(int id, int footprintSlots)
        {
            this.id = id;
            this.footprintSlots = footprintSlots;
        }
    }

    /// <summary>Immutable plain-object factory; the asset-backed factory delegates to this same backend.</summary>
    public sealed class LogicalLaneTransportProvider : IConveyorTransportProvider
    {
        private readonly double _speed;
        private readonly double _slotSpacing;
        private readonly LogicalPayloadProfile[] _profiles;
        private readonly bool _indexedAdmission;

        public LogicalLaneTransportProvider(double speed = 1, double slotSpacing = 0.5,
            IReadOnlyList<LogicalPayloadProfile> profiles = null, bool indexedAdmission = false)
        {
            if (!ConveyorValidation.IsFinite(speed) || speed < 0) throw new ArgumentOutOfRangeException(nameof(speed));
            ConveyorValidation.Positive(slotSpacing, nameof(slotSpacing));
            _speed = speed;
            _slotSpacing = slotSpacing;
            _indexedAdmission = indexedAdmission;
            _profiles = profiles == null ? new[] { new LogicalPayloadProfile(0, 1) } : new LogicalPayloadProfile[profiles.Count];
            if (profiles != null)
            {
                for (int index = 0; index < profiles.Count; index++) _profiles[index] = profiles[index];
            }
            for (int index = 0; index < _profiles.Length; index++)
            {
                if (_profiles[index].footprintSlots < 1) throw new ArgumentException("Footprints must be positive.");
                for (int other = 0; other < index; other++)
                {
                    if (_profiles[other].id == _profiles[index].id) throw new ArgumentException("Profile IDs must be unique.");
                }
            }
        }

        public IConveyorTransportBackend Create(in ConveyorTransportContext context)
            => new LogicalLaneBackend(context, _speed, _slotSpacing, _profiles, _indexedAdmission);
    }

    /// <summary>
    /// Slot-sized circular interval footprints with shared per-lane phase. Admission scans live records;
    /// logical advancement is per lane, while gate detection and publication still scan payload records.
    /// </summary>
    public sealed class LogicalLaneBackend : IConveyorTransportBackend, IConveyorGateBackend, IConveyorLaneControl, IConveyorSelfTransferValidation
    {
        #region Constants
        private const double Epsilon = 0.000001;
        private const int MaximumGatesPerChannel = 64;
        #endregion

        #region Properties
        public ConveyorTransportCapabilities Capabilities => ConveyorTransportCapabilities.PosePublication
            | ConveyorTransportCapabilities.AdmissionReservations | ConveyorTransportCapabilities.Gates
            | ConveyorTransportCapabilities.LaneHolding;
        public ConveyorStepDriver StepDriver => ConveyorStepDriver.Synchronous;
        public int Count { get; private set; }
        public int SlotsPerLane { get; }
        public double SlotSpacing { get; }
        public bool UsesIndexedAdmission => _indexed != null;
        #endregion

        #region Private Fields
        private readonly ConveyorTransportContext _context;
        private readonly LogicalPayloadProfile[] _profiles;
        private readonly Record[] _records;
        private readonly Reservation[] _reservations;
        private readonly int[] _freeReservations;
        private readonly double[] _offsets;
        private readonly double[] _movement;
        private readonly bool[] _holds;
        private readonly ConveyorPayloadHandle[] _arrivals;
        private readonly int[] _arrivalGates;
        private readonly List<Gate> _gates = new List<Gate>();
        private readonly IndexedLaneOccupancy _indexed;
        private int _freeReservationCount;
        private int _pendingReservations;
        private uint _serial;
        private double _speed;
        private bool _prepared;
        private bool _suspended;
        private bool _disposed;
        #endregion

        #region Construction
        internal LogicalLaneBackend(ConveyorTransportContext context, double speed, double spacing, LogicalPayloadProfile[] profiles, bool indexedAdmission)
        {
            _context = context;
            _speed = speed;
            _profiles = profiles;
            double slots = Math.Floor(context.Path.Length / spacing);
            if (slots < 1 || slots > int.MaxValue) throw new ArgumentException("Slot spacing is incompatible with path length.");
            SlotsPerLane = (int)slots;
            SlotSpacing = context.Path.Length / SlotsPerLane;
            if (indexedAdmission) _indexed = new IndexedLaneOccupancy(context, SlotsPerLane, SlotSpacing);
            _records = new Record[context.PayloadCapacity];
            _reservations = new Reservation[context.PayloadCapacity];
            _freeReservations = new int[context.PayloadCapacity];
            _freeReservationCount = context.PayloadCapacity;
            for (int index = 0; index < context.PayloadCapacity; index++) _freeReservations[index] = index;
            _offsets = new double[context.Channels];
            _movement = new double[context.Channels];
            _holds = new bool[context.Channels];
            _arrivals = new ConveyorPayloadHandle[context.Channels];
            _arrivalGates = new int[context.Channels];
        }
        #endregion

        #region Public Methods
        public ConveyorAdmissionStatus TryReserve(in ConveyorAdmissionRequest request, out ConveyorBackendReservation reservation)
        {
            RequireMutable();
            reservation = default;
            if (_suspended) return ConveyorAdmissionStatus.Unavailable;
            if ((uint)request.Channel >= (uint)_context.Channels || !ConveyorValidation.IsFinite(request.Pose.position))
                return ConveyorAdmissionStatus.Unsupported;
            int slots = GetFootprint(request.Payload.ProfileId);
            if (slots < 1 || slots > SlotsPerLane || (_indexed != null && slots != 1)) return ConveyorAdmissionStatus.Unsupported;
            double position = request.RouteDistance ?? _context.Path.FindNearestDistance(request.Pose.position);
            if (!ConveyorValidation.IsFinite(position)) return ConveyorAdmissionStatus.Unsupported;
            position = ConveyorValidation.Wrap(position, _context.Path.Length);
            double length = slots * SlotSpacing;
            if (_freeReservationCount == 0 || Count + _pendingReservations >= _records.Length) return ConveyorAdmissionStatus.Full;
            int cell = -1;
            if (_indexed != null)
            {
                cell = _indexed.FindCell(request.Channel, position, _offsets[request.Channel]);
                if (!_indexed.TryReserve(cell)) return ConveyorAdmissionStatus.Full;
                position = ConveyorValidation.Wrap(_indexed.LogicalDistance(cell) + _offsets[request.Channel], _context.Path.Length);
            }
            else if (Overlaps(request.Channel, position, length)) return ConveyorAdmissionStatus.Full;
            int index = _freeReservations[--_freeReservationCount];
            if (++_serial == 0) ++_serial;
            long token = ((long)_serial << 32) | (uint)(index + 1);
            _reservations[index] = new Reservation
            {
                Token = token, Channel = request.Channel, Position = position, Length = length, Cell = cell
            };
            _pendingReservations++;
            reservation = new ConveyorBackendReservation(this, token);
            return ConveyorAdmissionStatus.Accepted;
        }

        public bool IsReservationValid(in ConveyorBackendReservation reservation)
        {
            int index = unchecked((int)(uint)reservation.Token) - 1;
            return !_disposed && ReferenceEquals(reservation.Owner, this) && (uint)index < (uint)_reservations.Length
                && reservation.Token != 0 && _reservations[index].Token == reservation.Token;
        }

        public void CancelReservation(in ConveyorBackendReservation reservation)
        {
            RequireMutable();
            if (!IsReservationValid(reservation)) throw new InvalidOperationException("Stale or foreign admission reservation.");
            ReleaseReservation(unchecked((int)(uint)reservation.Token) - 1);
        }

        public void CommitAdmission(in ConveyorBackendReservation reservation, ConveyorPayloadHandle handle)
        {
            RequireMutable();
            if (!IsReservationValid(reservation) || !handle.IsValid || (uint)handle.Slot >= (uint)_records.Length
                || _records[handle.Slot].Active) throw new InvalidOperationException("Invalid admission commit.");
            int index = unchecked((int)(uint)reservation.Token) - 1;
            Reservation pending = _reservations[index];
            _records[handle.Slot] = new Record
            {
                Active = true, Handle = handle, Channel = pending.Channel, Length = pending.Length,
                LogicalDistance = ConveyorValidation.Wrap(pending.Position - _offsets[pending.Channel], _context.Path.Length),
                WaitingGate = -1, IgnoreInitialGate = true, Cell = pending.Cell
            };
            ReleaseReservation(index, false);
            Count++;
        }

        public void Remove(ConveyorPayloadHandle handle)
        {
            RequireMutable();
            if (!Contains(handle)) throw new InvalidOperationException("Payload is not owned by this backend.");
            if (_indexed != null) _indexed.Release(_records[handle.Slot].Cell);
            _records[handle.Slot] = default;
            Count--;
        }

        public bool TryGetState(ConveyorPayloadHandle handle, out ConveyorTransportState state)
        {
            RequireAlive();
            if (!Contains(handle)) { state = default; return false; }
            Record record = _records[handle.Slot];
            double distance = Distance(record);
            state = new ConveyorTransportState(handle,
                _indexed == null ? _context.Path.EvaluateLane(distance, record.Channel, _context.Channels, _context.Width)
                    : _indexed.EvaluateLane(distance, record.Channel),
                record.Channel, distance, record.WaitingGate);
            return true;
        }

        public int CopyStates(ConveyorTransportState[] destination)
        {
            RequireAlive();
            if (_prepared) throw new InvalidOperationException("Complete the step before reading published states.");
            if (destination == null || destination.Length < Count) throw new ArgumentException("State buffer is too small.");
            int count = 0;
            for (int index = 0; index < _records.Length; index++)
            {
                if (!_records[index].Active) continue;
                TryGetState(_records[index].Handle, out destination[count++]);
            }
            return count;
        }

        public void PrepareStep(double deltaTime)
        {
            RequireMutable();
            if (_pendingReservations != 0) throw new InvalidOperationException("Commit or cancel admissions before stepping.");
            if (!ConveyorValidation.IsFinite(deltaTime) || deltaTime < 0 || !ConveyorValidation.IsFinite(deltaTime * _speed))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            for (int channel = 0; channel < _context.Channels; channel++)
            {
                _movement[channel] = _suspended || _holds[channel] ? 0 : deltaTime * _speed;
                _arrivals[channel] = default;
                _arrivalGates[channel] = -1;
            }
            FindGateEncounters();
            _prepared = true;
        }

        public void CompleteStep()
        {
            RequireAlive();
            if (!_prepared) throw new InvalidOperationException("No prepared step exists.");
            for (int channel = 0; channel < _context.Channels; channel++)
                _offsets[channel] = ConveyorValidation.Wrap(_offsets[channel] + _movement[channel], _context.Path.Length);
            for (int index = 0; index < _records.Length; index++)
            {
                if (_records[index].Active && _movement[_records[index].Channel] > Epsilon)
                    _records[index].IgnoreInitialGate = false;
            }
            for (int channel = 0; channel < _context.Channels; channel++)
            {
                if (_arrivals[channel].IsValid)
                    _records[_arrivals[channel].Slot].WaitingGate = _arrivalGates[channel];
            }
            _prepared = false;
        }

        public int AddGate(int channel, double distance)
        {
            RequireMutable();
            if ((uint)channel >= (uint)_context.Channels || !ConveyorValidation.IsFinite(distance))
                throw new ArgumentOutOfRangeException(nameof(channel));
            distance = ConveyorValidation.Wrap(distance, _context.Path.Length);
            int reusable = -1;
            int channelGates = 0;
            for (int index = 0; index < _gates.Count; index++)
            {
                Gate gate = _gates[index];
                if (!gate.Active) { reusable = index; continue; }
                if (gate.Channel == channel) channelGates++;
                if (gate.Active && gate.Channel == channel && CircularGap(gate.Distance, distance) < Epsilon)
                    throw new ArgumentException("Two entrance gates cannot occupy the same lane position.");
            }
            if (channelGates == MaximumGatesPerChannel) throw new InvalidOperationException("Logical transport supports at most 64 entrances per lane.");
            if (reusable >= 0)
            {
                _gates[reusable] = new Gate { Active = true, Channel = channel, Distance = distance };
                return reusable;
            }
            _gates.Add(new Gate { Active = true, Channel = channel, Distance = distance });
            return _gates.Count - 1;
        }

        public void RemoveGate(int gateId)
        {
            RequireMutable();
            if ((uint)gateId >= (uint)_gates.Count || !_gates[gateId].Active) return;
            Gate gate = _gates[gateId];
            gate.Active = false;
            _gates[gateId] = gate;
            for (int index = 0; index < _records.Length; index++)
            {
                if (_records[index].Active && _records[index].WaitingGate == gateId) _records[index].WaitingGate = -1;
            }
        }

        public bool TryGetWaiting(int gateId, out ConveyorPayloadHandle handle)
        {
            RequireAlive();
            for (int index = 0; index < _records.Length; index++)
            {
                if (!_records[index].Active || _records[index].WaitingGate != gateId) continue;
                handle = _records[index].Handle;
                return true;
            }
            handle = default;
            return false;
        }

        public void SetLaneHeld(int channel, bool held)
        {
            RequireMutable();
            if ((uint)channel >= (uint)_holds.Length) throw new ArgumentOutOfRangeException(nameof(channel));
            _holds[channel] = held;
        }

        public bool IsLaneHeld(int channel)
        {
            RequireAlive();
            if ((uint)channel >= (uint)_holds.Length) throw new ArgumentOutOfRangeException(nameof(channel));
            if (_holds[channel] || _suspended) return true;
            for (int index = 0; index < _records.Length; index++)
            {
                if (_records[index].Active && _records[index].Channel == channel && _records[index].WaitingGate >= 0) return true;
            }
            return false;
        }

        public void SetSpeed(double speed)
        {
            RequireMutable();
            if (!ConveyorValidation.IsFinite(speed) || speed < 0) throw new ArgumentOutOfRangeException(nameof(speed));
            _speed = speed;
        }

        public bool IsDestinationSupported(ConveyorPayloadHandle source, in ConveyorAdmissionRequest destination)
        {
            RequireAlive();
            if (!Contains(source) || (uint)destination.Channel >= (uint)_context.Channels) return false;
            Record record = _records[source.Slot];
            if (record.Channel != destination.Channel) return true;
            int slots = GetFootprint(destination.Payload.ProfileId);
            if (slots < 1 || slots > SlotsPerLane) return false;
            double position = destination.RouteDistance ?? _context.Path.FindNearestDistance(destination.Pose.position);
            if (!ConveyorValidation.IsFinite(position)) return false;
            position = ConveyorValidation.Wrap(position, _context.Path.Length);
            if (_indexed != null)
                return slots == 1 && _indexed.FindCell(destination.Channel, position, _offsets[destination.Channel]) != record.Cell;
            return CircularGap(position, Distance(record)) >= (record.Length + slots * SlotSpacing) * 0.5 - Epsilon;
        }

        public void SetSuspended(bool suspended) { RequireMutable(); _suspended = suspended; }
        public void Dispose() { if (_disposed) return; _disposed = true; Count = 0; }
        #endregion

        #region Private Methods
        private void FindGateEncounters()
        {
            if (_gates.Count == 0) return;
            for (int index = 0; index < _records.Length; index++)
            {
                Record record = _records[index];
                if (!record.Active) continue;
                int channel = record.Channel;
                if (record.WaitingGate >= 0) { _movement[channel] = 0; _arrivals[channel] = default; continue; }
                FindRecordEncounter(record);
            }
        }

        private void FindRecordEncounter(Record record)
        {
            int channel = record.Channel;
            double position = Distance(record);
            for (int index = 0; index < _gates.Count; index++)
            {
                Gate gate = _gates[index];
                if (!gate.Active || gate.Channel != channel) continue;
                double ahead = ConveyorValidation.Wrap(gate.Distance - position, _context.Path.Length);
                if (ahead < Epsilon) ahead = record.IgnoreInitialGate ? _context.Path.Length : 0;
                if (ahead > _movement[channel] + Epsilon) continue;
                _movement[channel] = Math.Min(_movement[channel], ahead);
                _arrivals[channel] = record.Handle;
                _arrivalGates[channel] = index;
            }
        }

        private bool Overlaps(int channel, double position, double length)
        {
            for (int index = 0; index < _records.Length; index++)
            {
                Record record = _records[index];
                if (record.Active && record.Channel == channel
                    && CircularGap(position, Distance(record)) < (length + record.Length) * 0.5 - Epsilon) return true;
                Reservation reservation = _reservations[index];
                if (reservation.Token != 0 && reservation.Channel == channel
                    && CircularGap(position, reservation.Position) < (length + reservation.Length) * 0.5 - Epsilon) return true;
            }
            return false;
        }

        private int GetFootprint(int id)
        {
            for (int index = 0; index < _profiles.Length; index++)
            {
                if (_profiles[index].id == id) return _profiles[index].footprintSlots;
            }
            return 0;
        }

        private void ReleaseReservation(int index, bool releaseCell = true)
        {
            if (releaseCell && _indexed != null) _indexed.Release(_reservations[index].Cell);
            _reservations[index] = default;
            _freeReservations[_freeReservationCount++] = index;
            _pendingReservations--;
        }

        private double CircularGap(double a, double b) { double delta = Math.Abs(a - b); return Math.Min(delta, _context.Path.Length - delta); }
        private double Distance(Record record) => ConveyorValidation.Wrap(record.LogicalDistance + _offsets[record.Channel], _context.Path.Length);
        private bool Contains(ConveyorPayloadHandle handle) => (uint)handle.Slot < (uint)_records.Length
            && _records[handle.Slot].Active && _records[handle.Slot].Handle == handle;
        private void RequireAlive() { if (_disposed) throw new ObjectDisposedException(nameof(LogicalLaneBackend)); }
        private void RequireMutable() { RequireAlive(); if (_prepared) throw new InvalidOperationException("Complete the prepared step first."); }
        #endregion

        #region Nested Types
        private struct Record
        {
            internal bool Active;
            internal ConveyorPayloadHandle Handle;
            internal int Channel;
            internal double LogicalDistance;
            internal double Length;
            internal int WaitingGate;
            internal bool IgnoreInitialGate;
            internal int Cell;
        }

        private struct Reservation { internal long Token; internal int Channel; internal double Position; internal double Length; internal int Cell; }
        private struct Gate { internal bool Active; internal int Channel; internal double Distance; }
        #endregion
    }
}
