using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public sealed partial class ConveyorWorld
    {
        internal bool CollectTransfer(ConveyorRuntime source, ConveyorPayloadHandle handle, int gateId,
            string destinationId, int destinationChannel, double normalizedExit, IConveyorTransferPolicy policy)
        {
            if (!_busy || !_stepping || !Contains(handle) || _records[handle.Slot].Runtime != source)
                throw new InvalidOperationException("Invalid source payload in transfer query.");
            if (!source.Backend.TryGetState(handle, out ConveyorTransportState state) || state.WaitingGate != gateId)
                throw new InvalidOperationException("Transfers may only service completed gate arrivals.");
            if (_requestedEpoch[handle.Slot] == _collectionEpoch)
                throw new InvalidOperationException("Two modules requested the same payload in one transfer batch.");
            if (_requestCount == _requests.Length) return false;
            _requestedEpoch[handle.Slot] = _collectionEpoch;
            _requests[_requestCount++] = new TransferRequest
            {
                Source = source, Handle = handle, SourceChannel = state.Channel, SourceDistance = state.Distance,
                DestinationId = destinationId, DestinationChannel = destinationChannel, Exit = normalizedExit, Policy = policy
            };
            return true;
        }

        private void ServiceTransfers()
        {
            int priorResultReservations = _reservedResults;
            _requestCount = 0;
            if (++_collectionEpoch == 0) { Array.Clear(_requestedEpoch, 0, _requestedEpoch.Length); _collectionEpoch = 1; }
            try
            {
                for (int index = 0; index < _runtimes.Count; index++)
                {
                    if (!_runtimes[index].IsSuspended) _runtimes[index].Collect(_requestSink);
                }
                for (int index = 0; index < _requestCount; index++) ResolveTransfer(index);
                for (int index = 0; index < _requestCount; index++) CommitTransfer(index);
            }
            finally
            {
                try
                {
                    for (int index = 0; index < _requestCount; index++)
                    {
                        TransferOutcome outcome = _outcomes[index];
                        if (outcome.Reservation.IsValid && outcome.Destination.Backend.IsReservationValid(outcome.Reservation))
                            outcome.Destination.Backend.CancelReservation(outcome.Reservation);
                        _outcomes[index] = default;
                        _requests[index] = default;
                    }
                }
                finally { _reservedResults = priorResultReservations; }
            }
        }

        private void ResolveTransfer(int index)
        {
            TransferRequest request = _requests[index];
            ConveyorRuntime source = request.Source;
            if (!_byId.TryGetValue(request.DestinationId, out ConveyorRuntime destination) || destination.IsSuspended)
            {
                ResolveBlock(index, ConveyorBlockReason.Unavailable);
                return;
            }
            if (destination.Backend.GetType() != source.Backend.GetType()
                || (uint)request.DestinationChannel >= (uint)destination.Channels)
            {
                ResolveBlock(index, ConveyorBlockReason.Unsupported);
                return;
            }
            double distance = request.Exit * destination.Path.Length;
            if (source == destination && request.SourceChannel == request.DestinationChannel)
            {
                double gap = Math.Abs(request.SourceDistance - distance);
                if (Math.Min(gap, source.Path.Length - gap) < 0.00001)
                {
                    ResolveBlock(index, ConveyorBlockReason.Unsupported);
                    return;
                }
            }
            Pose pose = destination.Path.EvaluateLane(distance, request.DestinationChannel, destination.Channels, destination.Width);
            var admission = new ConveyorAdmissionRequest(_records[request.Handle.Slot].Payload, pose, request.DestinationChannel, distance);
            if (source == destination && (source.Backend is not IConveyorSelfTransferValidation validation
                || !validation.IsDestinationSupported(request.Handle, admission)))
            {
                ResolveBlock(index, ConveyorBlockReason.Unsupported);
                return;
            }
            ConveyorAdmissionStatus status = destination.Backend.TryReserve(admission, out ConveyorBackendReservation reservation);
            bool vaporize = false;
            if (status == ConveyorAdmissionStatus.Full)
            {
                var context = new ConveyorTransferContext(request.Handle, source.Id, destination.Id);
                ConveyorOverflowDisposition disposition = request.Policy.EvaluateFullDestination(context);
                if (disposition != ConveyorOverflowDisposition.Block && disposition != ConveyorOverflowDisposition.Vaporize)
                    throw new InvalidOperationException("Policy returned an unsupported disposition.");
                vaporize = disposition == ConveyorOverflowDisposition.Vaporize;
            }
            if (status != ConveyorAdmissionStatus.Accepted && !vaporize)
            {
                ResolveBlock(index, status == ConveyorAdmissionStatus.Full ? ConveyorBlockReason.Full
                    : status == ConveyorAdmissionStatus.Unavailable ? ConveyorBlockReason.Unavailable : ConveyorBlockReason.Unsupported);
                return;
            }
            int needed = source.ReportedHolds[request.SourceChannel] ? 2 : 1;
            if (!ReserveResults(needed))
            {
                if (reservation.IsValid) destination.Backend.CancelReservation(reservation);
                ResolveBlock(index, ConveyorBlockReason.ResultBackpressure);
                return;
            }
            _outcomes[index] = new TransferOutcome
            {
                Destination = destination, Reservation = reservation,
                Kind = vaporize ? OutcomeKind.Vaporize : OutcomeKind.Transfer,
                ReleaseReportedLane = needed == 2
            };
        }

        private void ResolveBlock(int index, ConveyorBlockReason reason)
        {
            TransferRequest request = _requests[index];
            if (request.Source.Blocks[request.SourceChannel] == ConveyorBlockReason.None)
                request.Source.BlockedSince[request.SourceChannel] = TickIndex;
            request.Source.Blocks[request.SourceChannel] = reason;
            bool report = !request.Source.ReportedHolds[request.SourceChannel] && ReserveResults(1);
            _outcomes[index] = new TransferOutcome { Kind = OutcomeKind.Block, EmitBlock = report, BlockReason = reason };
        }

        private void CommitTransfer(int index)
        {
            TransferRequest request = _requests[index];
            TransferOutcome outcome = _outcomes[index];
            ConveyorRuntime source = request.Source;
            if (outcome.Kind == OutcomeKind.Block)
            {
                if (outcome.EmitBlock)
                {
                    source.ReportedHolds[request.SourceChannel] = true;
                    EmitReserved(new ConveyorResult(ConveyorResultKind.LaneBlocked, request.Handle,
                        _records[request.Handle.Slot].Payload, source.Id, request.DestinationId,
                        request.SourceChannel, blockReason: outcome.BlockReason));
                }
                return;
            }
            if (outcome.Kind == OutcomeKind.Vaporize) RemoveRecord(request.Handle.Slot, ConveyorRemovalReason.Vaporized);
            else
            {
                if (!outcome.Destination.Backend.IsReservationValid(outcome.Reservation))
                    throw new InvalidOperationException("Destination invalidated a prepared transfer reservation.");
                source.Backend.Remove(request.Handle);
                outcome.Destination.Backend.CommitAdmission(outcome.Reservation, request.Handle);
                _records[request.Handle.Slot].Runtime = outcome.Destination;
                TransferCount++;
                EmitReserved(new ConveyorResult(ConveyorResultKind.Transferred, request.Handle,
                    _records[request.Handle.Slot].Payload, source.Id, outcome.Destination.Id, request.DestinationChannel));
            }
            source.Blocks[request.SourceChannel] = ConveyorBlockReason.None;
            if (outcome.ReleaseReportedLane) ReleaseLane(source, request.SourceChannel);
        }

        private enum OutcomeKind { Block, Transfer, Vaporize }
        private struct TransferRequest
        {
            internal ConveyorRuntime Source;
            internal ConveyorPayloadHandle Handle;
            internal int SourceChannel;
            internal double SourceDistance;
            internal string DestinationId;
            internal int DestinationChannel;
            internal double Exit;
            internal IConveyorTransferPolicy Policy;
        }

        private struct TransferOutcome
        {
            internal OutcomeKind Kind;
            internal ConveyorRuntime Destination;
            internal ConveyorBackendReservation Reservation;
            internal bool ReleaseReportedLane;
            internal bool EmitBlock;
            internal ConveyorBlockReason BlockReason;
        }
    }
}
